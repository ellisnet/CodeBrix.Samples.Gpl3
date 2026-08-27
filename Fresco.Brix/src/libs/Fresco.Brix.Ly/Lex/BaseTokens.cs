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

namespace Fresco.Brix.Ly.Lex; //was previously: ly/lex/_token.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>The base token class every mode's tokens derive from.</summary>
public abstract class Token : Slexing.Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Token(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Represents an unparsed piece of input text.</summary>
public class Unparsed : Token
{
    /// <summary>The default-token factory (no pattern).</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Factory<Unparsed>((t, p) => new Unparsed(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Unparsed(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A token that decreases the argument count of the current parser.</summary>
public abstract class Item : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Item(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Ends one argument of the enclosing parser stack.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => ((State)state).EndArgument();
}

/// <summary>A token that leaves the current parser.</summary>
public abstract class Leaver : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Leaver(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Leaves the current parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}

/// <summary>A token containing whitespace.</summary>
public class Space : Token
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\s+";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Space>(Pattern, (t, p) => new Space(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Space(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A token that is a single newline.</summary>
public class Newline : Space
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\n";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Newline>(Pattern, (t, p) => new Newline(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Newline(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for tokens that belong to a comment.</summary>
public abstract class Comment : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Comment(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for items that are a whole line comment.</summary>
public abstract class LineComment : Comment
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected LineComment(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for tokens that belong to a block/multiline comment.</summary>
public abstract class BlockComment : Comment
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected BlockComment(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for tokens that start a block/multiline comment.</summary>
public abstract class BlockCommentStart : BlockComment
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected BlockCommentStart(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for tokens that end a block/multiline comment.</summary>
public abstract class BlockCommentEnd : BlockComment
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected BlockCommentEnd(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for tokens that belong to a quote-delimited string.</summary>
public abstract class StringBase : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected StringBase(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for tokens that start a quote-delimited string.</summary>
public abstract class StringStart : StringBase
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected StringStart(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for tokens that end a quote-delimited string.</summary>
public abstract class StringEnd : StringBase
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected StringEnd(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for tokens that are an (escaped) character.</summary>
public abstract class Character : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Character(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for tokens that are a numerical value.</summary>
public abstract class Numeric : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Numeric(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for tokens that represent erroneous input.</summary>
public abstract class ErrorBase : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected ErrorBase(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>
/// Mixin for tokens that have a matching token FORWARD in the text; the
/// <see cref="MatchName"/> gives a unique name per matching pair kind.
/// </summary>
public interface IMatchStart
{
    /// <summary>Gets the unique pair name (e.g. "bracket", "slur").</summary>
    string MatchName { get; }
}

/// <summary>Mixin for tokens that have a matching token BACKWARD in the text.</summary>
public interface IMatchEnd
{
    /// <summary>Gets the unique pair name (e.g. "bracket", "slur").</summary>
    string MatchName { get; }
}

/// <summary>Mixin for tokens that make the next line indent MORE.</summary>
public interface IIndent
{
}

/// <summary>Mixin for tokens that make the next line indent LESS.</summary>
public interface IDedent
{
}
