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

using Fresco.Brix.Ly.Slexing;

namespace Fresco.Brix.Ly.Lex.HtmlMode; //was previously: ly/lex/html.py (parser classes);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>The item-list helpers for the HTML mode parsers.</summary>
internal static class ItemLists
{
    /// <summary>Concatenates rule lists, preserving order — upstream's tuple addition.</summary>
    /// <param name="lists">The lists, in order.</param>
    /// <returns>The concatenation.</returns>
    internal static TokenRule[] Join(params TokenRule[][] lists)
    {
        int length = 0;
        foreach (TokenRule[] list in lists)
        {
            length += list.Length;
        }

        TokenRule[] joined = new TokenRule[length];
        int at = 0;
        foreach (TokenRule[] list in lists)
        {
            list.CopyTo(joined, at);
            at += list.Length;
        }

        return joined;
    }
}

/// <summary>Parses HTML from the toplevel, recognizing LilyPond in HTML.</summary>
public class ParseHTML : Lex.Parser
{
    private static readonly TokenRule[] ParserItems =
    {
        Lex.Space.Rule,
        LilyPondVersionTag.Rule,
        LilyPondFileTag.Rule,
        LilyPondInlineTag.Rule,
        CommentStart.Rule,
        TagStart.Rule,
        EntityRef.Rule,
    };

    /// <summary>Initializes the parser.</summary>
    public ParseHTML()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseHTML(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the mode this parser begins.</summary>
    public override string Mode => "html";

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Parses the attributes inside an HTML tag.</summary>
public class ParseAttr : Lex.Parser
{
    private static readonly TokenRule[] ParserItems =
    {
        Lex.Space.Rule,
        TagEnd.Rule,
        AttrName.Rule,
        EqualSign.Rule,
        StringDQStart.Rule,
        StringSQStart.Rule,
    };

    /// <summary>Initializes the parser.</summary>
    public ParseAttr()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseAttr(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Parses a double-quoted attribute string.</summary>
public class ParseStringDQ : Lex.Parser
{
    private static readonly TokenRule[] ParserItems =
    {
        StringDQEnd.Rule,
        EntityRef.Rule,
    };

    /// <summary>Initializes the parser.</summary>
    public ParseStringDQ()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseStringDQ(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the default token rule: <see cref="StringBase"/>.</summary>
    public override TokenRule Default => StringBase.Rule;

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Parses a single-quoted attribute string.</summary>
public class ParseStringSQ : Lex.Parser
{
    private static readonly TokenRule[] ParserItems =
    {
        StringSQEnd.Rule,
        EntityRef.Rule,
    };

    /// <summary>Initializes the parser.</summary>
    public ParseStringSQ()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseStringSQ(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the default token rule: <see cref="StringBase"/>.</summary>
    public override TokenRule Default => StringBase.Rule;

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Parses the inside of an HTML comment.</summary>
public class ParseComment : Lex.Parser
{
    private static readonly TokenRule[] ParserItems =
    {
        CommentEnd.Rule,
    };

    /// <summary>Initializes the parser.</summary>
    public ParseComment()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseComment(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the default token rule: <see cref="Comment"/>.</summary>
    public override TokenRule Default => Comment.Rule;

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Finds a value or drops back.</summary>
public class ParseValue : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems =
    {
        Lex.Space.Rule,
        Value.Rule,
    };

    /// <summary>Initializes the parser.</summary>
    public ParseValue()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseValue(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    //upstream note: this explicit fallthrough matches the FallthroughParser default; ported as written.
    /// <summary>Leaves the current parser.</summary>
    /// <param name="state">The state to alter.</param>
    /// <returns><see langword="false"/> — parsing continues.</returns>
    public override bool Fallthrough(Slexing.State state)
    {
        state.Leave();
        return false;
    }
}

/// <summary>Parses the attributes of an inline <c>&lt;lilypond&gt;</c> tag.</summary>
public class ParseLilyPondAttr : Lex.Parser
{
    private static readonly TokenRule[] ParserItems =
    {
        Lex.Space.Rule,
        AttrName.Rule,
        EqualSign.Rule,
        StringDQStart.Rule,
        StringSQStart.Rule,
        LilyPondTagEnd.Rule,
        SemiColon.Rule,
    };

    /// <summary>Initializes the parser.</summary>
    public ParseLilyPondAttr()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseLilyPondAttr(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Parses the options of a <c>&lt;lilypondfile&gt;</c> tag.</summary>
public class ParseLilyPondFileOptions : Lex.Parser
{
    private static readonly TokenRule[] ParserItems =
    {
        Lex.Space.Rule,
        AttrName.Rule,
        EqualSign.Rule,
        StringDQStart.Rule,
        StringSQStart.Rule,
        LilyPondFileTagEnd.Rule,
    };

    /// <summary>Initializes the parser.</summary>
    public ParseLilyPondFileOptions()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseLilyPondFileOptions(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Parses LilyPond between <c>&lt;lilypond&gt;</c> and <c>&lt;/lilypond&gt;</c> tags.</summary>
public class ParseLilyPond : LilyPondMode.ParseGlobal
{
    private static TokenRule[] _parserItems;

    /// <summary>Initializes the parser.</summary>
    public ParseLilyPond()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseLilyPond(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules: the close tag first, then the upstream ParseGlobal items.</summary>
    protected override TokenRule[] Items
        => _parserItems ??= ItemLists.Join(new[] { LilyPondCloseTag.Rule }, base.Items);
}

/// <summary>Parses inline LilyPond music inside a <c>&lt;lilypond: ... /&gt;</c> tag.</summary>
public class ParseLilyPondInline : LilyPondMode.ParseMusic
{
    private static TokenRule[] _parserItems;

    /// <summary>Initializes the parser.</summary>
    public ParseLilyPondInline()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseLilyPondInline(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules: the inline tag end first, then the upstream ParseMusic items.</summary>
    protected override TokenRule[] Items
        => _parserItems ??= ItemLists.Join(new[] { LilyPondInlineTagEnd.Rule }, base.Items);
}
