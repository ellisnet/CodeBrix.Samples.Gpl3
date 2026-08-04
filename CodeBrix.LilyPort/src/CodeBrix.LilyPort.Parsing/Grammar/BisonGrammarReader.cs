// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CodeBrix.LilyPort.Parsing.Grammar;

/// <summary>
/// Reads LilyPond's <c>parser.yy</c> directly, into a grammar the in-repo LALR
/// generator can work from.
/// <para>
/// This is the first half of decision O7 (master plan section 13): the grammar SOURCE
/// is vendored and the tables are built in-repo, so that an upstream re-sync needs no
/// external toolchain. Everything this reader does not understand is a hard error —
/// see <see cref="UnsupportedBisonFeatureException"/> — because a skipped declaration
/// changes the language the parser accepts without changing anything visible.
/// </para>
/// <para>
/// The action bodies are extracted as OPAQUE TEXT and keyed by rule identity. They are
/// C++ and they are hand-ported; the reader's job is to know which rules have one and
/// to give each a stable name, not to understand any of it.
/// </para>
/// </summary>
public static class BisonGrammarReader
{
    // The declarations this generator understands. Anything else in the declarations
    // section stops the read, on purpose.
    private static readonly HashSet<string> KnownDeclarations = new HashSet<string>(StringComparer.Ordinal)
    {
        "token", "left", "right", "nonassoc", "precedence",
        "define", "locations", "debug", "parse-param", "lex-param",
        "start", "expect", "expect-rr", "pure-parser", "name-prefix",
    };

    /// <summary>Reads a grammar from Bison source text.</summary>
    /// <param name="source">The contents of <c>parser.yy</c>.</param>
    /// <returns>The grammar.</returns>
    public static BisonGrammar Read(string source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        BisonGrammar grammar = new BisonGrammar();

        // `error` is Bison's own built-in terminal: nothing declares it, and it is what
        // every error-recovery rule matches. Left undeclared it reads as a nonterminal
        // with no rules, which makes `lilypond: lilypond error` unreachable and takes
        // the port's error recovery with it.
        grammar.Declare("error", SymbolKind.Token);

        (int declarationsStart, int rulesStart, int rulesEnd) = SplitSections(source);

        ReadDeclarations(grammar, source, declarationsStart, rulesStart - 2);
        ReadRules(grammar, source, rulesStart, rulesEnd);
        AssignIdentities(grammar);

        return grammar;
    }

    /// <summary>Reads the grammar mirrored into this assembly.</summary>
    /// <returns>The grammar.</returns>
    public static BisonGrammar ReadMirroredGrammar() => Read(GrammarMirror.ParserSource);

    /// <summary>
    /// Splits the file at its two <c>%%</c> separators, skipping any that appear inside
    /// a <c>%{ ... %}</c> prologue block, a comment or a string.
    /// </summary>
    private static (int DeclarationsStart, int RulesStart, int RulesEnd) SplitSections(string source)
    {
        List<int> separators = new List<int>();
        Cursor cursor = new Cursor(source);

        while (!cursor.AtEnd)
        {
            if (cursor.AtLineStart && cursor.Matches("%%"))
            {
                separators.Add(cursor.Position);
                cursor.Advance(2);
                continue;
            }

            if (cursor.AtLineStart && cursor.Matches("%{"))
            {
                cursor.SkipPrologueBlock();
                continue;
            }

            if (cursor.SkipTrivia())
            {
                continue;
            }

            cursor.Advance(1);
        }

        if (separators.Count < 1)
        {
            throw new UnsupportedBisonFeatureException(
                "a Bison file with no %% separator", 1);
        }

        // The declarations end AT the first separator, not after it: including the
        // "%%" itself would present it to the declaration reader as an empty %
        // declaration. The epilogue separator is optional in Bison, so a file with only
        // one runs its rules to the end.
        int rulesEnd = separators.Count > 1 ? separators[1] : source.Length;
        return (0, separators[0] + 2, rulesEnd);
    }

    private static void ReadDeclarations(BisonGrammar grammar, string source, int start, int end)
    {
        Cursor cursor = new Cursor(source, start, end);
        int precedenceLevel = 0;

        while (!cursor.AtEnd)
        {
            if (cursor.SkipTrivia())
            {
                continue;
            }

            if (cursor.AtLineStart && cursor.Matches("%{"))
            {
                cursor.SkipPrologueBlock();
                continue;
            }

            if (cursor.Current != '%')
            {
                cursor.Advance(1);
                continue;
            }

            int line = cursor.Line;
            cursor.Advance(1);
            string name = cursor.ReadDeclarationName();

            if (!KnownDeclarations.Contains(name))
            {
                throw new UnsupportedBisonFeatureException("%" + name, line);
            }

            switch (name)
            {
                case "token":
                    ReadTokenDeclaration(grammar, cursor);
                    break;

                case "left":
                case "right":
                case "nonassoc":
                case "precedence":
                    precedenceLevel++;
                    ReadPrecedenceDeclaration(grammar, cursor, name, precedenceLevel);
                    break;

                case "define":
                    ReadDefine(grammar, cursor);
                    break;

                case "parse-param":
                    grammar.ParseParameters.Add(cursor.ReadBracedText());
                    break;

                case "lex-param":
                    grammar.LexParameters.Add(cursor.ReadBracedText());
                    break;

                case "locations":
                    grammar.HasLocations = true;
                    break;

                case "debug":
                    grammar.HasDebug = true;
                    break;

                default:
                    // Declared known above but carrying no data this generator needs.
                    cursor.SkipToEndOfLine();
                    break;
            }
        }

        grammar.PrecedenceLevelCount = precedenceLevel;
    }

    private static void ReadTokenDeclaration(BisonGrammar grammar, Cursor cursor)
    {
        // %token NAME ["alias"] and %token NAME 0 "alias" -- one or more per line, and
        // a declaration continues onto following lines until the next % at line start.
        while (true)
        {
            cursor.SkipTriviaAndNewlines(out bool reachedNextDeclaration);
            if (reachedNextDeclaration || cursor.AtEnd)
            {
                return;
            }

            string name = cursor.ReadSymbolName();
            if (name == null)
            {
                return;
            }

            GrammarSymbol symbol = grammar.Declare(name, SymbolKind.Token);

            cursor.SkipSpacesAndComments();
            if (cursor.TryReadInteger(out int number))
            {
                symbol.DeclaredNumber = number;
                cursor.SkipSpacesAndComments();
            }

            if (cursor.Current == '"')
            {
                symbol.Alias = cursor.ReadQuotedString();
            }
        }
    }

    private static void ReadPrecedenceDeclaration(
        BisonGrammar grammar,
        Cursor cursor,
        string declaration,
        int level)
    {
        Associativity associativity = declaration switch
        {
            "left" => Associativity.Left,
            "right" => Associativity.Right,
            _ => Associativity.None,
        };

        while (true)
        {
            cursor.SkipTriviaAndNewlines(out bool reachedNextDeclaration);
            if (reachedNextDeclaration || cursor.AtEnd)
            {
                return;
            }

            string name = cursor.ReadSymbolName();
            if (name == null)
            {
                return;
            }

            // A precedence declaration also DECLARES the symbol as a terminal, which is
            // how PREC_BOT, COMPOSITE and PREC_TOP exist at all -- they are named only
            // here and in %prec, never in a rule.
            GrammarSymbol symbol = grammar.Declare(
                name,
                name[0] == '\'' ? SymbolKind.CharacterLiteral : SymbolKind.Token);

            symbol.Precedence = level;
            symbol.Associativity = associativity;
        }
    }

    private static void ReadDefine(BisonGrammar grammar, Cursor cursor)
    {
        cursor.SkipSpacesAndComments();
        string variable = cursor.ReadDefineName();
        cursor.SkipSpacesAndComments();

        string value = cursor.Current == '{'
            ? cursor.ReadBracedText()
            : cursor.ReadToEndOfLine().Trim();

        grammar.Defines[variable] = value;
    }

    private static void ReadRules(BisonGrammar grammar, string source, int start, int end)
    {
        Cursor cursor = new Cursor(source, start, end);
        int ruleIndex = 0;
        int midRuleCounter = 0;

        while (true)
        {
            cursor.SkipTrivia();
            if (cursor.AtEnd)
            {
                break;
            }

            int line = cursor.Line;
            string leftHandSide = cursor.ReadSymbolName();
            if (leftHandSide == null)
            {
                cursor.Advance(1);
                continue;
            }

            cursor.SkipTrivia();
            if (cursor.Current != ':')
            {
                throw new UnsupportedBisonFeatureException(
                    "a rule for '" + leftHandSide + "' with no ':'", line);
            }

            cursor.Advance(1);
            grammar.Declare(leftHandSide, SymbolKind.Nonterminal);

            // One alternative per iteration, until the ';' that closes the rule.
            while (true)
            {
                List<string> rightHandSide = new List<string>();
                string action = null;
                string precedenceSymbol = null;
                int alternativeLine = cursor.Line;

                while (true)
                {
                    cursor.SkipTrivia();
                    if (cursor.AtEnd)
                    {
                        break;
                    }

                    char current = cursor.Current;

                    if (current == '|' || current == ';')
                    {
                        break;
                    }

                    if (current == '{')
                    {
                        string body = cursor.ReadBracedText();

                        // Look ahead: an action followed by another SYMBOL is a MID-RULE
                        // action, which Bison rewrites into an anonymous empty rule.
                        //
                        // "Another symbol" is the exact test, and %prec is NOT one --
                        // it is an annotation on the enclosing rule. Seventeen rules in
                        // this grammar are written `... { action } %prec X ;`, and
                        // treating that action as mid-rule invents seventeen extra
                        // productions and seventeen extra nonterminals. Bison's own
                        // automaton is what caught it: 15 mid-rule actions, not 32.
                        cursor.SkipTrivia();

                        while (!cursor.AtEnd && cursor.Current == '%')
                        {
                            int annotationLine = cursor.Line;
                            cursor.Advance(1);
                            string annotation = cursor.ReadDeclarationName();
                            if (!string.Equals(annotation, "prec", StringComparison.Ordinal))
                            {
                                throw new UnsupportedBisonFeatureException("%" + annotation, annotationLine);
                            }

                            cursor.SkipTrivia();
                            precedenceSymbol = cursor.ReadSymbolName();
                            cursor.SkipTrivia();
                        }

                        bool isMidRule = !cursor.AtEnd
                                         && cursor.Current != '|'
                                         && cursor.Current != ';';

                        if (isMidRule)
                        {
                            midRuleCounter++;
                            string anonymous = "$@" + midRuleCounter.ToString(CultureInfo.InvariantCulture);
                            grammar.Declare(anonymous, SymbolKind.Nonterminal);

                            GrammarRule midRule = new GrammarRule(
                                ruleIndex++,
                                anonymous,
                                Array.Empty<string>())
                            {
                                ActionText = body,
                                Line = alternativeLine,
                                IsMidRuleAction = true,
                            };

                            grammar.AddRule(midRule);
                            rightHandSide.Add(anonymous);
                        }
                        else
                        {
                            action = body;
                        }

                        continue;
                    }

                    if (current == '%')
                    {
                        int precedenceLine = cursor.Line;
                        cursor.Advance(1);
                        string directive = cursor.ReadDeclarationName();
                        if (!string.Equals(directive, "prec", StringComparison.Ordinal))
                        {
                            throw new UnsupportedBisonFeatureException("%" + directive, precedenceLine);
                        }

                        cursor.SkipTrivia();
                        precedenceSymbol = cursor.ReadSymbolName();
                        continue;
                    }

                    string symbol = cursor.ReadSymbolName();
                    if (symbol == null)
                    {
                        throw new UnsupportedBisonFeatureException(
                            "an unrecognised character '" + current + "' in a rule body",
                            cursor.Line);
                    }

                    // A character literal is a terminal; anything else is left as a
                    // nonterminal until a %token declaration or a rule says otherwise.
                    grammar.Declare(
                        symbol,
                        symbol[0] == '\'' ? SymbolKind.CharacterLiteral : SymbolKind.Nonterminal);

                    rightHandSide.Add(symbol);
                }

                GrammarRule rule = new GrammarRule(ruleIndex++, leftHandSide, rightHandSide)
                {
                    ActionText = action,
                    Line = alternativeLine,
                    PrecedenceSymbol = precedenceSymbol,
                };

                grammar.AddRule(rule);

                if (cursor.AtEnd || cursor.Current == ';')
                {
                    if (!cursor.AtEnd)
                    {
                        cursor.Advance(1);
                    }

                    break;
                }

                // cursor.Current == '|': another alternative for the same left-hand side.
                cursor.Advance(1);
            }
        }

        // Every left-hand side seen is a nonterminal, whatever order it was first met in.
        foreach (GrammarRule rule in grammar.Rules)
        {
            GrammarSymbol symbol = grammar.Find(rule.LeftHandSide);
            if (symbol != null)
            {
                symbol.Kind = SymbolKind.Nonterminal;
            }
        }
    }

    /// <summary>
    /// Gives every rule a stable identity, which is what the hand-ported actions are
    /// keyed on.
    /// <para>
    /// Rule INDICES shift the moment anything is inserted above them, so an upstream
    /// re-sync that adds one rule near the top would appear to change every action
    /// below it. The identity is the production written out instead, with an ordinal
    /// only where the same production genuinely occurs more than once.
    /// </para>
    /// </summary>
    private static void AssignIdentities(BisonGrammar grammar)
    {
        Dictionary<string, int> seen = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (GrammarRule rule in grammar.Rules)
        {
            string text = rule.ToString();
            seen.TryGetValue(text, out int count);
            seen[text] = count + 1;

            rule.Identity = count == 0
                ? text
                : text + " #" + (count + 1).ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>A position in the source text, with the skipping rules Bison needs.</summary>
    private sealed class Cursor
    {
        private readonly string _source;
        private readonly int _end;

        internal Cursor(string source, int start = 0, int end = -1)
        {
            _source = source;
            _end = end < 0 ? source.Length : end;
            Position = start;
            Line = CountLines(source, start);
        }

        internal int Position { get; private set; }

        internal int Line { get; private set; }

        internal bool AtEnd => Position >= _end;

        internal char Current => Position < _end ? _source[Position] : '\0';

        internal bool AtLineStart => Position == 0 || _source[Position - 1] == '\n';

        internal bool Matches(string text)
            => Position + text.Length <= _end
               && string.CompareOrdinal(_source, Position, text, 0, text.Length) == 0;

        internal void Advance(int count)
        {
            for (int i = 0; i < count && Position < _end; i++)
            {
                if (_source[Position] == '\n')
                {
                    Line++;
                }

                Position++;
            }
        }

        /// <summary>Skips whitespace, comments and strings. Returns whether anything was skipped.</summary>
        internal bool SkipTrivia()
        {
            int before = Position;

            while (!AtEnd)
            {
                char c = Current;

                if (char.IsWhiteSpace(c))
                {
                    Advance(1);
                    continue;
                }

                if (Matches("/*"))
                {
                    Advance(2);
                    while (!AtEnd && !Matches("*/"))
                    {
                        Advance(1);
                    }

                    Advance(2);
                    continue;
                }

                if (Matches("//"))
                {
                    SkipToEndOfLine();
                    continue;
                }

                break;
            }

            return Position != before;
        }

        internal void SkipSpacesAndComments()
        {
            while (!AtEnd)
            {
                char c = Current;
                if (c == ' ' || c == '\t' || c == '\r')
                {
                    Advance(1);
                    continue;
                }

                if (Matches("/*"))
                {
                    Advance(2);
                    while (!AtEnd && !Matches("*/"))
                    {
                        Advance(1);
                    }

                    Advance(2);
                    continue;
                }

                break;
            }
        }

        /// <summary>
        /// Skips to the next declaration item, reporting when the next non-trivia thing
        /// is a new <c>%</c> declaration at the start of a line rather than another item
        /// of the current one.
        /// </summary>
        internal void SkipTriviaAndNewlines(out bool reachedNextDeclaration)
        {
            SkipTrivia();
            reachedNextDeclaration = AtEnd || (AtLineStart && Current == '%') || Current == '%';
        }

        internal void SkipToEndOfLine()
        {
            while (!AtEnd && Current != '\n')
            {
                Advance(1);
            }
        }

        internal string ReadToEndOfLine()
        {
            int start = Position;
            SkipToEndOfLine();
            return _source.Substring(start, Position - start);
        }

        /// <summary>Skips a <c>%{ ... %}</c> prologue block, contents untouched.</summary>
        internal void SkipPrologueBlock()
        {
            Advance(2);
            while (!AtEnd)
            {
                if (AtLineStart && Matches("%}"))
                {
                    Advance(2);
                    return;
                }

                Advance(1);
            }
        }

        internal string ReadDeclarationName()
        {
            int start = Position;
            while (!AtEnd && (char.IsLetterOrDigit(Current) || Current == '-' || Current == '_'))
            {
                Advance(1);
            }

            return _source.Substring(start, Position - start);
        }

        internal string ReadDefineName()
        {
            int start = Position;
            while (!AtEnd && (char.IsLetterOrDigit(Current) || Current == '.' || Current == '-' || Current == '_'))
            {
                Advance(1);
            }

            return _source.Substring(start, Position - start);
        }

        /// <summary>Reads an identifier, or a character literal such as <c>'{'</c>.</summary>
        internal string ReadSymbolName()
        {
            if (AtEnd)
            {
                return null;
            }

            if (Current == '\'')
            {
                int start = Position;
                Advance(1);

                if (Current == '\\')
                {
                    Advance(1);
                }

                Advance(1);

                if (Current != '\'')
                {
                    return null;
                }

                Advance(1);
                return _source.Substring(start, Position - start);
            }

            if (!char.IsLetter(Current) && Current != '_' && Current != '.')
            {
                return null;
            }

            int nameStart = Position;
            while (!AtEnd && (char.IsLetterOrDigit(Current) || Current == '_' || Current == '.'))
            {
                Advance(1);
            }

            return _source.Substring(nameStart, Position - nameStart);
        }

        internal bool TryReadInteger(out int value)
        {
            value = 0;
            if (AtEnd || !char.IsDigit(Current))
            {
                return false;
            }

            int start = Position;
            while (!AtEnd && char.IsDigit(Current))
            {
                Advance(1);
            }

            return int.TryParse(
                _source.Substring(start, Position - start),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out value);
        }

        internal string ReadQuotedString()
        {
            StringBuilder text = new StringBuilder();
            Advance(1);

            while (!AtEnd && Current != '"')
            {
                if (Current == '\\')
                {
                    text.Append(Current);
                    Advance(1);
                }

                text.Append(Current);
                Advance(1);
            }

            Advance(1);
            return text.ToString();
        }

        /// <summary>
        /// Reads a braced block and returns its contents WITHOUT the outer braces.
        /// <para>
        /// The block is C++, so the brace count has to ignore braces inside strings,
        /// character literals and comments — an action containing <c>'{'</c> or a string
        /// with a brace in it is not hypothetical in this grammar.
        /// </para>
        /// </summary>
        internal string ReadBracedText()
        {
            if (Current != '{')
            {
                return string.Empty;
            }

            Advance(1);
            int start = Position;
            int depth = 1;

            while (!AtEnd && depth > 0)
            {
                char c = Current;

                if (Matches("/*"))
                {
                    Advance(2);
                    while (!AtEnd && !Matches("*/"))
                    {
                        Advance(1);
                    }

                    Advance(2);
                    continue;
                }

                if (Matches("//"))
                {
                    SkipToEndOfLine();
                    continue;
                }

                if (c == '"' || c == '\'')
                {
                    char quote = c;
                    Advance(1);
                    while (!AtEnd && Current != quote)
                    {
                        if (Current == '\\')
                        {
                            Advance(1);
                        }

                        Advance(1);
                    }

                    Advance(1);
                    continue;
                }

                if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    depth--;
                    if (depth == 0)
                    {
                        string body = _source.Substring(start, Position - start);
                        Advance(1);
                        return body;
                    }
                }

                Advance(1);
            }

            return _source.Substring(start, Position - start);
        }

        private static int CountLines(string source, int position)
        {
            int line = 1;
            for (int i = 0; i < position && i < source.Length; i++)
            {
                if (source[i] == '\n')
                {
                    line++;
                }
            }

            return line;
        }
    }
}
