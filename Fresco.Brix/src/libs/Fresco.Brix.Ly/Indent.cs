// This file is part of python-ly, https://pypi.python.org/pypi/python-ly
//
// Copyright (c) 2008 - 2015 by Wilbert Berendsen
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation, either version 3
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
// See http://www.gnu.org/licenses/ for more information.

using Fresco.Brix.Ly.Lex;
using LilyPondMode = Fresco.Brix.Ly.Lex.LilyPondMode;
using State = Fresco.Brix.Ly.Lex.State;
using SchemeMode = Fresco.Brix.Ly.Lex.SchemeMode;
using Fresco.Brix.Ly.Slexing;
using Token = Fresco.Brix.Ly.Slexing.Token;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Ly; //was previously: ly/indent.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The auto-indenter: computes and applies the indent lines should have from
/// the indent/dedent tokens the tokenizer marks, with scheme-aware alignment
/// (Emacs's Guile-style rules).
/// </summary>
public class Indenter
{
    /// <summary>Gets or sets whether tabs are used for indent.</summary>
    public bool IndentTabs { get; set; }

    /// <summary>Gets or sets the amount of spaces per indent level when
    /// <see cref="IndentTabs"/> is off.</summary>
    public int IndentWidth { get; set; } = 2;

    private string OneIndent => IndentTabs ? "\t" : new string(' ', IndentWidth);

    /// <summary>
    /// Indents all lines in the cursor's range. When
    /// <paramref name="indentBlankLines"/> is set, the indent of blank lines
    /// is made larger if necessary; otherwise a blank line's shorter indent is
    /// left alone.
    /// </summary>
    /// <param name="cursor">The range to indent.</param>
    /// <param name="indentBlankLines">Whether blank lines' indents grow.</param>
    public void Indent(Cursor cursor, bool indentBlankLines = false)
    {
        List<string> indents = new List<string> { string.Empty };
        DocumentBlock startBlock = cursor.StartBlock();
        DocumentBlock endBlock = cursor.EndBlock();
        bool inRange = false;
        Line pline = null;
        string prevIndent = string.Empty;
        DocumentBase d = cursor.Document;
        using (d.Writing())
        {
            foreach (DocumentBlock b in d.Blocks())
            {
                if (ReferenceEquals(b, startBlock))
                {
                    inRange = true;
                }

                Line line = new Line(d, b);

                // handle indents of prev line
                if (pline != null)
                {
                    if (pline.Indent != null)
                    {
                        prevIndent = pline.Indent;
                    }

                    if (pline.Indenters.Count > 0)
                    {
                        string currentIndent = indents[indents.Count - 1];
                        foreach ((int? align, bool indent) in pline.Indenters)
                        {
                            string newIndent = currentIndent;
                            if (align != null && align != 0)
                            {
                                newIndent += new string(
                                    ' ', align.Value - prevIndent.Length);
                            }

                            if (indent)
                            {
                                newIndent += OneIndent;
                            }

                            indents.Add(newIndent);
                        }
                    }
                }

                TrimTo(indents, indents.Count - line.DedentersStart);

                // if we may not change the indent just remember the current
                if (line.Indent != null)
                {
                    if (!inRange)
                    {
                        indents[indents.Count - 1] = line.Indent;
                    }
                    else if (!indentBlankLines && line.IsBlank
                        && indents[indents.Count - 1].StartsWith(
                            line.Indent, System.StringComparison.Ordinal))
                    {
                        // don't make shorter indents longer on blank lines
                    }
                    else if (line.Indent != indents[indents.Count - 1])
                    {
                        d.SetText(
                            d.Position(b),
                            d.Position(b) + line.Indent.Length,
                            indents[indents.Count - 1]);
                    }
                }

                TrimTo(indents, indents.Count - line.DedentersEnd);

                if (ReferenceEquals(b, endBlock))
                {
                    break;
                }

                pline = line;
            }
        }
    }

    private static void TrimTo(List<string> indents, int keep)
    {
        int from = keep < 1 ? 1 : keep;
        if (from < indents.Count)
        {
            indents.RemoveRange(from, indents.Count - from);
        }
    }

    /// <summary>Manually adds one indent level to all lines of the cursor,
    /// inserting after a leading tab when there is one.</summary>
    /// <param name="cursor">The lines to indent.</param>
    public void IncreaseIndent(Cursor cursor)
    {
        string indent = OneIndent;
        DocumentBase d = cursor.Document;
        using (d.Writing())
        {
            foreach (DocumentBlock block in cursor.Blocks())
            {
                int ins = d.Position(block);
                Token[] tokens = d.Tokens(block);
                if (tokens.Length > 0 && tokens[0] is Space)
                {
                    int tabPos = tokens[0].Text.LastIndexOf('\t');
                    if (tabPos != -1)
                    {
                        ins += tokens[0].Pos + tabPos + 1;
                    }
                    else
                    {
                        ins += tokens[0].End;
                    }
                }

                d.SetText(ins, ins, indent);
            }
        }
    }

    /// <summary>Manually removes one level of indent from all lines of the
    /// cursor.</summary>
    /// <param name="cursor">The lines to dedent.</param>
    public void DecreaseIndent(Cursor cursor)
    {
        DocumentBase d = cursor.Document;
        using (d.Writing())
        {
            foreach (DocumentBlock block in cursor.Blocks())
            {
                Token[] tokens = d.Tokens(block);
                if (tokens.Length == 0)
                {
                    continue;
                }

                Token token = tokens[0];
                string space;
                if (token is Space)
                {
                    space = token.Text;
                }
                else
                {
                    // upstream's token[0:-len(token.lstrip())]: the leading
                    // whitespace, and the EMPTY string when nothing remains
                    // after lstrip (python's [0:-0] quirk).
                    int stripped = token.Text.TrimStart().Length;
                    space = stripped == 0
                        ? string.Empty
                        : token.Text.Substring(0, token.Text.Length - stripped);
                }

                int pos = d.Position(block);
                int end = pos + space.Length;
                if (space.Contains('\t')
                    && space.EndsWith(" ", System.StringComparison.Ordinal))
                {
                    // strip alignment
                    d.Delete(pos + space.LastIndexOf('\t') + 1, end);
                }
                else if (space.EndsWith("\t", System.StringComparison.Ordinal))
                {
                    // just strip one tab
                    d.Delete(end - 1, end);
                }
                else if (space.EndsWith(
                    new string(' ', IndentWidth), System.StringComparison.Ordinal))
                {
                    d.Delete(end - IndentWidth, end);
                }
                else
                {
                    d.Delete(pos, end);
                }
            }
        }
    }

    /// <summary>Returns the indent the block currently has, or
    /// <see langword="null"/> when the block is not indentable (e.g. inside a
    /// multiline string).</summary>
    /// <param name="document">The document.</param>
    /// <param name="block">The block.</param>
    /// <returns>The indent text or <see langword="null"/>.</returns>
    public string GetIndent(DocumentBase document, DocumentBlock block)
        => new Line(document, block).Indent;

    /// <summary>
    /// Returns the indent the block SHOULD have (looking only at previous
    /// lines), or <see langword="null"/> when it is not indentable. Use for
    /// one line or the first of a group.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="block">The block.</param>
    /// <returns>The indent text or <see langword="null"/>.</returns>
    public string ComputeIndent(DocumentBase document, DocumentBlock block)
    {
        Line line = new Line(document, block);
        if (line.Indent == null)
        {
            return null;
        }

        int depth = line.DedentersStart;

        // ONE enumerator consumed across both loops, exactly upstream's shared
        // generator.
        IEnumerator<DocumentBlock> blocks = document
            .BlocksBackward(document.PreviousBlock(block))
            .GetEnumerator();
        int? align = null;
        bool indent = false;
        bool found = false;
        while (blocks.MoveNext())
        {
            line = new Line(document, blocks.Current);
            int indentCount = line.Indenters.Count;
            if (depth >= 0 && depth < indentCount)
            {
                // we found the indent token
                int index = indentCount - depth - 1;
                (align, indent) = line.Indenters[index];
                found = true;
                break;
            }

            depth -= indentCount;
            depth += line.DedentersEnd;
            if (depth == 0)
            {
                // same indent as this line
                found = true;
                break;
            }

            depth += line.DedentersStart;
        }

        if (!found)
        {
            return string.Empty;
        }

        // here we arrive after 'break'
        string result = line.Indent;
        if (result == null)
        {
            result = string.Empty;
            while (blocks.MoveNext())
            {
                string candidate = new Line(document, blocks.Current).Indent;
                if (candidate != null)
                {
                    result = candidate;
                    break;
                }
            }
        }

        if (align != null && align != 0)
        {
            result += new string(' ', align.Value - result.Length);
        }

        if (indent)
        {
            result += OneIndent;
        }

        return result;
    }
}

/// <summary>
/// All relevant indenting information about one line: its current indent
/// (<see langword="null"/> = not indentable), blankness, the dedent counts at
/// its start and end, and the (align, indent) pairs of indents it opens.
/// </summary>
public class Line
{
    /// <summary>Analyzes one block of a document.</summary>
    /// <param name="document">The document.</param>
    /// <param name="block">The block.</param>
    public Line(DocumentBase document, DocumentBlock block)
    {
        State state = document.State(block);
        Token[] tokens = document.Tokens(block);

        // are we in a multi-line string?
        Slexing.Parser parser = state.CurrentParser();
        if (parser is LilyPondMode.ParseString || parser is SchemeMode.ParseString)
        {
            Indent = null;
            IsBlank = false;
        }
        else if (parser is LilyPondMode.ParseBlockComment
            || parser is SchemeMode.ParseBlockComment)
        {
            // do allow indenting the last line of a block comment if it only
            // contains space
            if (tokens.Length > 0 && tokens[0] is BlockCommentEnd)
            {
                Indent = string.Empty;
            }
            else if (tokens.Length > 1
                && tokens[0] is BlockComment
                && tokens[1] is BlockCommentEnd
                && tokens[0].Text.Length > 0
                && tokens[0].Text.All(char.IsWhiteSpace))
            {
                Indent = tokens[0].Text;
            }
            else
            {
                Indent = null;
            }

            IsBlank = false;
        }
        else if (tokens.Length > 0 && tokens[0] is Space)
        {
            Indent = tokens[0].Text;
            IsBlank = tokens.Length == 1;
        }
        else
        {
            Indent = string.Empty;
            IsBlank = tokens.Length == 0;
        }

        bool findDedenters = true;

        // quickly iterate over the tokens, collecting the indent tokens and
        // possible stuff to align to after the indent tokens
        List<List<Token>> indenters = new List<List<Token>>();
        foreach (Token t in tokens)
        {
            if (t is IIndent)
            {
                findDedenters = false;
                if (indenters.Count > 0)
                {
                    indenters[indenters.Count - 1].Add(t);
                }

                indenters.Add(new List<Token> { t });
            }
            else if (t is IDedent)
            {
                if (findDedenters && !(t is SchemeMode.CloseParen))
                {
                    DedentersStart += 1;
                }
                else
                {
                    findDedenters = false;
                    if (indenters.Count > 0)
                    {
                        indenters.RemoveAt(indenters.Count - 1);
                    }
                    else
                    {
                        DedentersEnd += 1;
                    }
                }
            }
            else if (!(t is Space))
            {
                findDedenters = false;
                if (indenters.Count > 0)
                {
                    indenters[indenters.Count - 1].Add(t);
                }
            }
        }

        // now analyse the indent tokens that are not closed on the same line
        // and determine how the next line should be indented
        Indenters = new List<(int? Align, bool Indent)>();
        foreach (List<Token> group in indenters)
        {
            Token token = group[0];
            List<Token> rest = group.GetRange(1, group.Count - 1);
            int? align;
            bool indent;
            if (token is SchemeMode.OpenParen)
            {
                if (rest.Count > 1 && !IsSpecialSchemeKeyword(rest[0]))
                {
                    (align, indent) = (rest[1].Pos, false);
                }
                else if (rest.Count == 1 && !(rest[0] is Comment))
                {
                    (align, indent) = (rest[0].Pos, false);
                }
                else
                {
                    (align, indent) = (token.Pos, true);
                }
            }
            else if (rest.Count > 0 && !(rest[0] is Comment))
            {
                (align, indent) = (rest[0].Pos, false);
            }
            else
            {
                (align, indent) = (null, true);
            }

            Indenters.Add((align, indent));
        }
    }

    /// <summary>Gets the line's current indent text, or <see langword="null"/>
    /// when the line is not indentable.</summary>
    public string Indent { get; }

    /// <summary>Gets whether the line is empty or whitespace only.</summary>
    public bool IsBlank { get; }

    /// <summary>Gets how many dedents at the line's start move the indenter a
    /// level up.</summary>
    public int DedentersStart { get; }

    /// <summary>Gets how many dedents at the line's end move the NEXT line a
    /// level up.</summary>
    public int DedentersEnd { get; }

    /// <summary>Gets one (align, indent) pair per indent the line opens and
    /// does not close.</summary>
    public List<(int? Align, bool Indent)> Indenters { get; }

    /// <summary>
    /// Returns whether the token is a special Scheme word like "define" that
    /// does not follow standard Scheme alignment — the Emacs scheme.el list,
    /// plus everything starting with "def".
    /// </summary>
    /// <param name="token">The token to test.</param>
    /// <returns>Whether it is special.</returns>
    public static bool IsSpecialSchemeKeyword(Token token)
        => token is SchemeMode.Word
            && (token.Text.StartsWith("def", System.StringComparison.Ordinal)
                || SpecialSchemeWords.Contains(token.Text));

    private static readonly HashSet<string> SpecialSchemeWords = new HashSet<string>
    {
        "begin", "case", "delay", "do", "lambda", "let", "let*", "letrec",
        "let-values", "let*-values", "sequence", "let-syntax", "letrec-syntax",
        "syntax-rules", "syntax-case", "library", "call-with-input-file",
        "with-input-from-file", "with-input-from-port", "call-with-output-file",
        "with-output-to-file", "with-output-to-port", "call-with-values",
        "dynamic-wind", "when", "unless", "letrec*", "parameterize",
        "define-values", "define-record-type", "define-library", "receive",
    };
}
