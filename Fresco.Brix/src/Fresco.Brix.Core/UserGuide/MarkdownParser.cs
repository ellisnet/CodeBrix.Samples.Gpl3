// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Text;

namespace Fresco.Brix.UserGuide; //was previously: frescobaldi/simplemarkdown.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The basic markdown-like parser: it reads text and calls
/// <see cref="MarkdownOutput.Push"/> and <see cref="MarkdownOutput.Pop"/> on
/// an output handler.
/// </summary>
/// <remarks>Upstream's <c>simplemarkdown.Parser</c>, ported line for line.
/// <see cref="Lineno"/> is upstream's <c>lineno</c> attribute and moves the
/// same way it does.</remarks>
public class MarkdownParser
{
    private readonly List<(string Type, int Indent)> _lists
        = new List<(string, int)>();

    /// <summary>Creates a parser writing into a do-nothing output.</summary>
    public MarkdownParser() => Output = new NullOutput();

    /// <summary>Gets or sets where the parsed document goes.</summary>
    public MarkdownOutput Output { get; set; }

    /// <summary>Gets or sets the line number being parsed.</summary>
    public int Lineno { get; set; } = 1;

    /// <summary>Parses the text.</summary>
    /// <param name="text">The markdown text.</param>
    /// <param name="output">The output, or null to keep the current one.</param>
    /// <param name="lineno">The line number to start at, or null.</param>
    public void Parse(string text, MarkdownOutput output = null, int? lineno = null)
        => ParseLines(SimpleMarkdown.SplitLines(text ?? string.Empty), output, lineno);

    /// <summary>Parses text line by line.</summary>
    /// <param name="lines">The lines.</param>
    /// <param name="output">The output, or null to keep the current one.</param>
    /// <param name="lineno">The line number to start at, or null.</param>
    public void ParseLines(
        IEnumerable<string> lines, MarkdownOutput output = null, int? lineno = null)
    {
        if (output != null) { Output = output; }

        if (lineno.HasValue) { Lineno = lineno.Value; }

        List<string> paragraph = new List<string>();
        using IEnumerator<string> walker = lines.GetEnumerator();
        while (walker.MoveNext())
        {
            string line = walker.Current;
            if (SimpleMarkdown.LeftStrip(line).StartsWith("```", StringComparison.Ordinal))
            {
                if (paragraph.Count > 0)
                {
                    ParseParagraph(paragraph);
                    paragraph = new List<string>();
                }

                int indent = SimpleMarkdown.ChopLeft(line).Length;
                string specifier = SimpleMarkdown.RightStrip(
                    SimpleMarkdown.LeftStrip(line, "` "));
                if (specifier.Length == 0) { specifier = null; }

                List<string> code = new List<string>();
                while (walker.MoveNext())
                {
                    line = walker.Current;
                    if (SimpleMarkdown.LeftStrip(line)
                        .StartsWith("```", StringComparison.Ordinal))
                    {
                        break;
                    }

                    code.Add(line);
                }

                HandleLists(indent);
                Output.Append("code", string.Join("\n", code), specifier);
                Lineno += code.Count + 2;
            }
            else if (line.Length == 0 || IsSpace(line))
            {
                if (paragraph.Count > 0)
                {
                    ParseParagraph(paragraph);
                    paragraph = new List<string>();
                }

                Lineno++;
            }
            else
            {
                paragraph.Add(line);
            }
        }

        if (paragraph.Count > 0) { ParseParagraph(paragraph); }
    }

    /// <summary>
    /// Parses one or more lines with no blank line in between: a heading, a
    /// list item, or a plain paragraph.
    /// </summary>
    /// <param name="lines">The lines.</param>
    public void ParseParagraph(List<string> lines)
    {
        int indent = SimpleMarkdown.ChopLeft(lines[0]).Length;
        if (SimpleMarkdown.LeftStrip(lines[0]).StartsWith("=", StringComparison.Ordinal))
        {
            HandleLists(indent);
            ParseHeading(lines);
        }
        else if (IsUnorderedItem(lines[0]))
        {
            HandleLists(indent, "unorderedlist");
            ParseUnordered(lines);
        }
        else if (IsOrderedItem(lines[0]))
        {
            HandleLists(indent, "orderedlist");
            ParseOrdered(lines);
        }
        else if (IsDefinitionItem(lines))
        {
            HandleLists(indent, "definitionlist");
            ParseDefinition(lines);
        }
        else if (!SpecialParagraph(lines))
        {
            HandleLists(indent);
            using (Output.Scope("paragraph")) { ParseInlineLines(lines); }
        }
    }

    /// <summary>
    /// Called for a paragraph that is neither a heading nor a list item; a
    /// subclass returning true has handled the lines itself.
    /// </summary>
    /// <param name="lines">The lines.</param>
    /// <returns>Whether the lines were handled.</returns>
    protected virtual bool SpecialParagraph(List<string> lines) => false;

    /// <summary>Whether the line starts an unordered list item.</summary>
    /// <param name="line">The line.</param>
    /// <returns>Whether it does.</returns>
    protected virtual bool IsUnorderedItem(string line)
    {
        List<string> parts = SimpleMarkdown.SplitWhitespace(line, 1);
        return parts.Count >= 2 && parts[0] == "*";
    }

    /// <summary>Whether the line starts an ordered list item.</summary>
    /// <param name="line">The line.</param>
    /// <returns>Whether it does.</returns>
    protected virtual bool IsOrderedItem(string line)
    {
        List<string> parts = SimpleMarkdown.SplitWhitespace(line, 1);
        if (parts.Count < 2) { return false; }

        string prefix = parts[0];
        if (!prefix.EndsWith(".", StringComparison.Ordinal)) { return false; }

        string digits = prefix.Substring(0, prefix.Length - 1);
        if (digits.Length == 0) { return false; }

        foreach (char character in digits)
        {
            if (!char.IsDigit(character)) { return false; }
        }

        return true;
    }

    /// <summary>Whether the lines are a definition list item.</summary>
    /// <param name="lines">The lines.</param>
    /// <returns>Whether they are.</returns>
    protected virtual bool IsDefinitionItem(List<string> lines)
        => lines.Count > 1
            && SimpleMarkdown.LeftStrip(lines[1]).StartsWith(": ", StringComparison.Ordinal);

    /// <summary>Parses a heading.</summary>
    /// <param name="lines">The lines.</param>
    protected void ParseHeading(List<string> lines)
    {
        string prefix = SimpleMarkdown.ChopLeft(lines[0], "= ");
        int equals = 0;
        foreach (char character in prefix)
        {
            if (character == '=') { equals++; }
        }

        int headingType = 4 - Math.Min(equals, 3);
        lines[0] = SimpleMarkdown.Strip(lines[0], "= ");
        using (Output.Scope("heading", headingType)) { ParseInlineLines(lines); }
    }

    /// <summary>Parses an ordered list.</summary>
    /// <param name="lines">The lines.</param>
    /// <remarks>⚠ A group of lines holding exactly ONE item gets a paragraph
    /// around it and a group holding several does not — upstream's own rule,
    /// which is how a "compact item list" is written.</remarks>
    protected void ParseOrdered(List<string> lines)
    {
        List<List<string>> items = SplitListItems(lines, IsOrderedItem);
        bool paragraphItem = items.Count == 1;
        foreach (List<string> item in items)
        {
            using (Output.Scope("orderedlist_item"))
            {
                if (paragraphItem)
                {
                    using (Output.Scope("paragraph")) { ParseInlineLines(item); }
                }
                else
                {
                    ParseInlineLines(item);
                }
            }
        }
    }

    /// <summary>Parses an unordered list.</summary>
    /// <param name="lines">The lines.</param>
    protected void ParseUnordered(List<string> lines)
    {
        List<List<string>> items = SplitListItems(lines, IsUnorderedItem);
        bool paragraphItem = items.Count == 1;
        foreach (List<string> item in items)
        {
            using (Output.Scope("unorderedlist_item"))
            {
                if (paragraphItem)
                {
                    using (Output.Scope("paragraph")) { ParseInlineLines(item); }
                }
                else
                {
                    ParseInlineLines(item);
                }
            }
        }
    }

    /// <summary>Splits lines into one list of lines per list item.</summary>
    /// <param name="lines">The lines.</param>
    /// <param name="isItem">Whether a line carries an item prefix.</param>
    /// <returns>The items.</returns>
    protected static List<List<string>> SplitListItems(
        List<string> lines, Func<string, bool> isItem)
    {
        List<List<string>> items = new List<List<string>>();
        List<string> item = new List<string>();
        foreach (string line in lines)
        {
            if (isItem(line))
            {
                if (item.Count > 0) { items.Add(item); }

                item = new List<string> { SimpleMarkdown.SplitWhitespace(line, 1)[1] };
            }
            else
            {
                item.Add(line);
            }
        }

        if (item.Count > 0) { items.Add(item); }

        return items;
    }

    /// <summary>Parses a definition list item.</summary>
    /// <param name="lines">The lines.</param>
    protected void ParseDefinition(List<string> lines)
    {
        string definition = lines[0];
        lines[1] = SimpleMarkdown.SplitAtMost(lines[1], ":", 1)[1];
        using (Output.Scope("definitionlist_item"))
        {
            using (Output.Scope("definitionlist_item_term"))
            {
                ParseInlineLines(new List<string> { definition });
            }

            using (Output.Scope("definitionlist_item_definition"))
            {
                ParseInlineLines(lines.GetRange(1, lines.Count - 1));
            }
        }
    }

    /// <summary>Closes ongoing lists, or starts new ones, as the indent asks.</summary>
    /// <param name="indent">The paragraph's indent.</param>
    /// <param name="listType">The list to start, or null.</param>
    protected void HandleLists(int indent, string listType = null)
    {
        if (listType != null
            && (_lists.Count == 0 || _lists[_lists.Count - 1].Indent < indent))
        {
            _lists.Add((listType, indent));
            Output.Push(listType);
            return;
        }

        while (_lists.Count > 0)
        {
            (string Type, int Indent) top = _lists[_lists.Count - 1];
            if (top.Indent > indent)
            {
                Output.Pop();
                _lists.RemoveAt(_lists.Count - 1);
                continue;
            }

            if (top.Indent == indent
                && !string.Equals(top.Type, listType, StringComparison.Ordinal))
            {
                Output.Pop();
                _lists.RemoveAt(_lists.Count - 1);
                if (listType != null)
                {
                    _lists.Add((listType, indent));
                    Output.Push(listType);
                }
            }

            break;
        }
    }

    /// <summary>Parses plain text lines that may carry inline markup.</summary>
    /// <param name="lines">The lines.</param>
    public void ParseInlineLines(List<string> lines)
    {
        int lineno = Lineno;
        StringBuilder joined = new StringBuilder();
        for (int index = 0; index < lines.Count; index++)
        {
            if (index > 0) { joined.Append('\n'); }

            joined.Append(SimpleMarkdown.Strip(lines[index]));
        }

        ParseInlineText(joined.ToString());
        Lineno = lineno + lines.Count;
    }

    /// <summary>Parses a continuous block of text that may carry inline markup.</summary>
    /// <param name="text">The text.</param>
    public virtual void ParseInlineText(string text)
    {
        using (Output.Scope("inline"))
        {
            List<string> nest = new List<string>();
            foreach ((string outside, string code) in
                SimpleMarkdown.IterSplit(text, "`"))
            {
                string rest = outside;
                while (rest.Length > 0)
                {
                    bool inLink = nest.Contains("link");
                    bool inEmphasis = nest.Count > 0 && nest[nest.Count - 1] == "emph";
                    (char? character, int? position) =
                        SimpleMarkdown.FindFirst(rest, inLink ? "*]" : "*[");
                    if (!character.HasValue)
                    {
                        OutputInlineText(rest);
                        break;
                    }

                    int pos = position.Value;
                    if (pos > 0) { OutputInlineText(rest.Substring(0, pos)); }

                    if (character.Value == '*')
                    {
                        if (inEmphasis)
                        {
                            Output.Pop();
                            nest.RemoveAt(nest.Count - 1);
                        }
                        else
                        {
                            Output.Push("inline_emphasis");
                            nest.Add("emph");
                        }

                        rest = rest.Substring(pos + 1);
                    }
                    else if (inLink)
                    {
                        while (true)
                        {
                            Output.Pop();
                            string popped = nest[nest.Count - 1];
                            nest.RemoveAt(nest.Count - 1);
                            if (popped == "link") { break; }
                        }

                        rest = rest.Substring(pos + 1);
                    }
                    else
                    {
                        (char? closing, int? end) =
                            SimpleMarkdown.FindFirst(rest, " \n\t]", pos + 1);
                        if (!closing.HasValue)
                        {
                            Output.Push("link", rest.Substring(pos + 1));
                            nest.Add("link");
                            break;
                        }

                        if (closing.Value == ']')
                        {
                            string url = rest.Substring(pos + 1, end.Value - pos - 1);
                            using (Output.Scope("link", url)) { OutputInlineText(url); }

                            rest = rest.Substring(end.Value + 1);
                        }
                        else
                        {
                            Output.Push("link", rest.Substring(pos + 1, end.Value - pos - 1));
                            nest.Add("link");
                            rest = SimpleMarkdown.LeftStrip(rest.Substring(end.Value + 1));
                        }
                    }
                }

                if (code.Length > 0)
                {
                    using (Output.Scope("inline_code")) { OutputInlineText(code); }
                }
            }

            while (nest.Count > 0)
            {
                nest.RemoveAt(nest.Count - 1);
                Output.Pop();
            }
        }
    }

    /// <summary>Appends an <c>inline_text</c> node to the output.</summary>
    /// <param name="text">The text.</param>
    protected void OutputInlineText(string text) => Output.Append("inline_text", text);

    private static bool IsSpace(string line)
    {
        foreach (char character in line)
        {
            if (!char.IsWhiteSpace(character)) { return false; }
        }

        return line.Length > 0;
    }

    /// <summary>An output that drops everything, so a parser always has one.</summary>
    private sealed class NullOutput : MarkdownOutput
    {
        public override void Push(string name, params object[] arguments) { }

        public override void Pop() { }
    }
}
