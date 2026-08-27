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

namespace Fresco.Brix.Ly.Lex.TexinfoMode; //was previously: ly/lex/texinfo.py (parser classes);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>The item-list helpers for the Texinfo mode parsers.</summary>
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

/// <summary>Parses Texinfo from the toplevel, recognizing LilyPond in Texinfo.</summary>
public class ParseTexinfo : Lex.Parser
{
    private static readonly TokenRule[] ParserItems =
    {
        LineComment.Rule,
        BlockCommentStart.Rule,
        Accent.Rule,
        EscapeChar.Rule,
        LilyPondBlockStart.Rule,
        LilyPondEnvStart.Rule,
        LilyPondFileStart.Rule,
        BlockStart.Rule,
        VerbatimStart.Rule,
        Keyword.Rule,
    };

    /// <summary>Initializes the parser.</summary>
    public ParseTexinfo()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseTexinfo(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the mode this parser begins.</summary>
    public override string Mode => "texinfo";

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Parses the inside of a <c>@ignore</c> block comment.</summary>
public class ParseComment : Lex.Parser
{
    private static readonly TokenRule[] ParserItems =
    {
        BlockCommentEnd.Rule,
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

/// <summary>Parses the inside of a brace block.</summary>
public class ParseBlock : Lex.Parser
{
    private static readonly TokenRule[] ParserItems =
    {
        BlockEnd.Rule,
        Accent.Rule,
        EscapeChar.Rule,
        BlockStart.Rule,
        Keyword.Rule,
    };

    /// <summary>Initializes the parser.</summary>
    public ParseBlock()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseBlock(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Parses the inside of a <c>@verbatim</c> environment.</summary>
public class ParseVerbatim : Lex.Parser
{
    private static readonly TokenRule[] ParserItems =
    {
        VerbatimEnd.Rule,
    };

    /// <summary>Initializes the parser.</summary>
    public ParseVerbatim()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseVerbatim(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the default token rule: <see cref="Verbatim"/>.</summary>
    public override TokenRule Default => Verbatim.Rule;

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Parses the attributes of a brace-style <c>@lilypond{ ... }</c> block.</summary>
public class ParseLilyPondBlockAttr : Lex.Parser
{
    private static readonly TokenRule[] ParserItems =
    {
        LilyPondAttrStart.Rule,
        LilyPondBlockStartBrace.Rule,
    };

    /// <summary>Initializes the parser.</summary>
    public ParseLilyPondBlockAttr()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseLilyPondBlockAttr(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Parses the attributes of a <c>@lilypond</c> environment, or falls through to it.</summary>
public class ParseLilyPondEnvAttr : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems =
    {
        LilyPondAttrStart.Rule,
    };

    /// <summary>Initializes the parser.</summary>
    public ParseLilyPondEnvAttr()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseLilyPondEnvAttr(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Replaces this parser with the LilyPond environment parser.</summary>
    /// <param name="state">The state to alter.</param>
    /// <returns><see langword="false"/> — parsing continues.</returns>
    public override bool Fallthrough(Slexing.State state)
    {
        state.Replace(new ParseLilyPondEnv());
        return false;
    }
}

/// <summary>Parses the inside of a LilyPond option attribute (<c>[ ... ]</c>).</summary>
public class ParseLilyPondAttr : Lex.Parser
{
    private static readonly TokenRule[] ParserItems =
    {
        LilyPondAttrEnd.Rule,
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

    /// <summary>Gets the default token rule: <see cref="Attribute"/>.</summary>
    public override TokenRule Default => Attribute.Rule;

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Parses a <c>@lilypondfile</c> block's attributes and opening brace.</summary>
public class ParseLilyPondFile : Lex.Parser
{
    private static readonly TokenRule[] ParserItems =
    {
        LilyPondAttrStart.Rule,
        LilyPondFileStartBrace.Rule,
    };

    /// <summary>Initializes the parser.</summary>
    public ParseLilyPondFile()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseLilyPondFile(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

//upstream note: the lilypond module is imported at this point in the source file.

/// <summary>Parses LilyPond inside a brace-style <c>@lilypond{ ... }</c> block.</summary>
public class ParseLilyPondBlock : LilyPondMode.ParseGlobal
{
    private static TokenRule[] _parserItems;

    /// <summary>Initializes the parser.</summary>
    public ParseLilyPondBlock()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseLilyPondBlock(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules: the block end first, then the upstream ParseGlobal items.</summary>
    protected override TokenRule[] Items
        => _parserItems ??= ItemLists.Join(new[] { LilyPondBlockEnd.Rule }, base.Items);
}

/// <summary>Parses LilyPond inside a <c>@lilypond ... @end lilypond</c> environment.</summary>
public class ParseLilyPondEnv : LilyPondMode.ParseGlobal
{
    private static TokenRule[] _parserItems;

    /// <summary>Initializes the parser.</summary>
    public ParseLilyPondEnv()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseLilyPondEnv(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules: the environment end first, then the upstream ParseGlobal items.</summary>
    protected override TokenRule[] Items
        => _parserItems ??= ItemLists.Join(new[] { LilyPondEnvEnd.Rule }, base.Items);
}
