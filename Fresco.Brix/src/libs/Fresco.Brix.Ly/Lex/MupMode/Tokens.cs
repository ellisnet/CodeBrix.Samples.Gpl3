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

namespace Fresco.Brix.Ly.Lex.MupMode; //was previously: ly/lex/mup.py (token classes);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.
// Parses and tokenizes MUP input.
// MUP (www.arkkra.com) is an open source music typesetter (formerly shareware).
// We add a tokenizer here, to enable a decent mup2ly conversion.

/// <summary>Base class for tokens that belong to a MUP comment.</summary>
public abstract class Comment : Lex.Comment
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Comment(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A <c>//</c> line comment.</summary>
public class LineComment : Comment //upstream note: unlike the other modes, does NOT derive _token.LineComment.
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"//.*$";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LineComment>(Pattern, (t, p) => new LineComment(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LineComment(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for tokens that belong to a MUP string.</summary>
public class StringBase : Lex.StringBase
{
    /// <summary>The default-token factory (no pattern); ParseString's default.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Factory<StringBase>((t, p) => new StringBase(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal StringBase(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Starts a quoted string.</summary>
public class StringQuotedStart : StringBase //upstream note: also derives _token.StringStart.
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"""";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<StringQuotedStart>(Pattern, (t, p) => new StringQuotedStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal StringQuotedStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the string parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseString());
}

/// <summary>Ends a quoted string.</summary>
public class StringQuotedEnd : StringBase //upstream note: also derives _token.StringEnd.
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"""";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<StringQuotedEnd>(Pattern, (t, p) => new StringQuotedEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal StringQuotedEnd(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Leaves the string parser and ends an argument.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state)
    {
        state.Leave();
        ((Lex.State)state).EndArgument();
    }
}

/// <summary>An escaped character inside a string (<c>\\</c> or <c>\"</c>).</summary>
public class StringQuoteEscape : Lex.Character
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\\[\\""]";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<StringQuoteEscape>(Pattern, (t, p) => new StringQuoteEscape(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal StringQuoteEscape(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A MUP macro name (all uppercase).</summary>
public class Macro : Lex.Token
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\b[A-Z][A-Z0-9_]*";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Macro>(Pattern, (t, p) => new Macro(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Macro(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A MUP preprocessor keyword or the <c>@</c> character.</summary>
public class Preprocessor : Lex.Token
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\b(if|then|else|endif|define|undef|ifdef|ifndef)\b|@";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Preprocessor>(Pattern, (t, p) => new Preprocessor(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Preprocessor(string text, int pos)
        : base(text, pos)
    {
    }
}
