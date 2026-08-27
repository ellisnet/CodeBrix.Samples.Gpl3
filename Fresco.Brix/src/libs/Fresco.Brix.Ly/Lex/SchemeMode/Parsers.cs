// This file is part of python-ly, https://pypi.python.org/pypi/python-ly
//
// Copyright (c) 2008 - 2015 by Wilbert Berendsen
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation; either version 3
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

namespace Fresco.Brix.Ly.Lex.SchemeMode; //was previously: ly/lex/scheme.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.
//
// This file covers scheme.py lines 177-224: the parser classes ParseScheme through
// ParseLilyPond.

/// <summary>Parses Scheme input.</summary>
public class ParseScheme : Parser
{
    private static readonly TokenRule[] ParserItems =
    {
        Space.Rule, // _token.Space
        OpenParen.Rule,
        CloseParen.Rule,
        LineComment.Rule,
        BlockCommentStart.Rule,
        LilyPondStart.Rule,
        VectorStart.Rule,
        Dot.Rule,
        Bool.Rule,
        Char.Rule,
        Quote.Rule,
        Fraction.Rule,
        Float.Rule,
        Number.Rule,
        Constant.Rule,
        Keyword.Rule,
        Function.Rule,
        Variable.Rule,
        Word.Rule,
        StringQuotedStart.Rule,
    };

    /// <summary>Initializes the parser.</summary>
    public ParseScheme()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseScheme(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the mode this parser begins: <c>scheme</c>.</summary>
    public override string Mode => "scheme";

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Parses the inside of a quoted string.</summary>
public class ParseString : Parser
{
    private static readonly TokenRule[] ParserItems =
    {
        StringQuotedEnd.Rule,
        StringQuoteEscape.Rule,
    };

    /// <summary>Initializes the parser.</summary>
    public ParseString()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseString(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the default rule: <see cref="StringBase"/> — upstream's
    /// <c>default = String</c>.</summary>
    public override TokenRule Default => StringBase.Rule;

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Parses the inside of a block comment.</summary>
public class ParseBlockComment : Parser
{
    private static readonly TokenRule[] ParserItems =
    {
        BlockCommentEnd.Rule,
    };

    /// <summary>Initializes the parser.</summary>
    public ParseBlockComment()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseBlockComment(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the default rule: <see cref="BlockComment"/> — upstream's
    /// <c>default = BlockComment</c>.</summary>
    public override TokenRule Default => BlockComment.Rule;

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Parses LilyPond input between <c>#{</c> and <c>#}</c>.</summary>
public class ParseLilyPond : LilyPondMode.ParseMusic
{
    private static readonly new TokenRule[] ParserItems = LilyPondMode.ItemLists.Join(
        new[]
        {
            LilyPondEnd.Rule,
        },
        LilyPondMode.ParseMusic.ParserItems); // upstream: lilypond.ParseMusic.items

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

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}
