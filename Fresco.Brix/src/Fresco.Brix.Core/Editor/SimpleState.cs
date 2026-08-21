// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Linq;
using Lily = Fresco.Brix.Ly.Lex.LilyPondMode;
using Html = Fresco.Brix.Ly.Lex.HtmlMode;
using Scm = Fresco.Brix.Ly.Lex.SchemeMode;
using State = Fresco.Brix.Ly.Lex.State;

namespace Fresco.Brix.Editor; //was previously: frescobaldi/simplestate.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Says, in plain words, where in a document the cursor is — for example
/// <c>['lilypond', 'book', 'header', 'markup', 'scheme', 'string']</c> for a
/// cursor inside a Scheme string inside a markup inside a header inside a
/// book.
/// <para>
/// The first word is always the file's mode. Snippets use this to decide
/// whether they apply where the cursor is.
/// </para>
/// </summary>
public static class SimpleState
{
    private static readonly Dictionary<Type, string> ParserNames = BuildParserNames();

    /// <summary>Describes a lexer state as a list of names.</summary>
    /// <param name="state">The lexer state.</param>
    /// <returns>The names, outermost first; repeats are collapsed.</returns>
    public static IReadOnlyList<string> Describe(State state)
    {
        List<string> names = new List<string>();
        if (state == null) { return names; }

        //Upstream iterates the parser STACK bottom-to-top; Parsers() answers
        //it top-first, so it is reversed back here.
        foreach (var parser in state.Parsers().Reverse())
        {
            string name = null;
            if (ParserNames.TryGetValue(parser.GetType(), out var mapped))
            {
                name = mapped;
            }
            else if (parser is Fresco.Brix.Ly.Lex.Parser lexParser
                && lexParser.Mode != null)
            {
                name = lexParser.Mode;
            }

            if (name == null) { continue; }

            //Upstream collapses only ADJACENT repeats, so nesting the same
            //construct twice still shows once per level break.
            if (names.Count == 0
                || !string.Equals(names[names.Count - 1], name, StringComparison.Ordinal))
            {
                names.Add(name);
            }
        }

        return names;
    }

    private static Dictionary<Type, string> BuildParserNames()
        => new Dictionary<Type, string>
        {
            //lilypond
            { typeof(Lily.ParseMusic), "music" },
            { typeof(Lily.ParseChord), "chord" },
            { typeof(Lily.ParseLyricMode), "lyricmode" },
            { typeof(Lily.ParseChordMode), "chordmode" },
            { typeof(Lily.ParseFigureMode), "figuremode" },
            { typeof(Lily.ParseDrumMode), "drummode" },
            { typeof(Lily.ParsePaper), "paper" },
            { typeof(Lily.ParseHeader), "header" },
            { typeof(Lily.ParseLayout), "layout" },
            { typeof(Lily.ParseBook), "book" },
            { typeof(Lily.ParseBookPart), "bookpart" },
            { typeof(Lily.ParseScore), "score" },
            { typeof(Lily.ParseMidi), "midi" },
            { typeof(Lily.ParseContext), "context" },
            { typeof(Lily.ParseWith), "with" },
            { typeof(Lily.ParseTranslator), "translator" },
            { typeof(Lily.ParseMarkup), "markup" },
            { typeof(Lily.ParseOverride), "override" },
            { typeof(Lily.ParseString), "string" },

            //scheme
            { typeof(Scm.ParseScheme), "scheme" },
            { typeof(Scm.ParseString), "string" },

            //html
            { typeof(Html.ParseAttr), "htmlattribute" },
            { typeof(Html.ParseStringSQ), "single-quoted-string" },
            { typeof(Html.ParseStringDQ), "double-quoted-string" },
            { typeof(Html.ParseComment), "comment" },
        };
}
