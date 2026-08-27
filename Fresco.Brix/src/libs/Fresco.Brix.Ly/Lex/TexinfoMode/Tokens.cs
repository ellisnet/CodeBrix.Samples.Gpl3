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

namespace Fresco.Brix.Ly.Lex.TexinfoMode; //was previously: ly/lex/texinfo.py (token classes);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.
// Parses and tokenizes Texinfo input, recognizing LilyPond in Texinfo.

/// <summary>Base class for tokens that belong to a Texinfo comment.</summary>
public class Comment : Lex.Comment
{
    /// <summary>The default-token factory (no pattern); ParseComment's default.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Factory<Comment>((t, p) => new Comment(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Comment(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A <c>@c</c> line comment.</summary>
public class LineComment : Comment //upstream note: also derives _token.LineComment.
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"@c\b.*$";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<LineComment>(Pattern, (t, p) => new LineComment(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LineComment(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Starts a <c>@ignore</c> block comment.</summary>
public class BlockCommentStart : Comment //upstream note: also derives _token.BlockCommentStart.
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"@ignore\b";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<BlockCommentStart>(Pattern, (t, p) => new BlockCommentStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal BlockCommentStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the comment parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseComment());
}

/// <summary>Ends a block comment (<c>@end ignore</c>).</summary>
public class BlockCommentEnd : Comment //upstream note: also derives _token.Leaver and _token.BlockCommentEnd.
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"@end\s+ignore\b";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<BlockCommentEnd>(Pattern, (t, p) => new BlockCommentEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal BlockCommentEnd(string text, int pos)
        : base(text, pos)
    {
    }

    //upstream: also _token.Leaver
    /// <summary>Leaves the current parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}

/// <summary>Base class for tokens inside a LilyPond option attribute.</summary>
public class Attribute : Lex.Token
{
    /// <summary>The default-token factory (no pattern); ParseLilyPondAttr's default.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Factory<Attribute>((t, p) => new Attribute(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Attribute(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A Texinfo keyword (<c>@word</c>).</summary>
public class Keyword : Lex.Token
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"@[a-zA-Z]+";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Keyword>(Pattern, (t, p) => new Keyword(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Keyword(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for Texinfo block tokens.</summary>
public abstract class Block : Lex.Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Block(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Starts a brace block (<c>@word{</c>).</summary>
public class BlockStart : Block
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"@[a-zA-Z]+\{";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<BlockStart>(Pattern, (t, p) => new BlockStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal BlockStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the block parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseBlock());
}

/// <summary>Ends a brace block (<c>}</c>).</summary>
public class BlockEnd : Block //upstream note: also derives _token.Leaver.
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\}";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<BlockEnd>(Pattern, (t, p) => new BlockEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal BlockEnd(string text, int pos)
        : base(text, pos)
    {
    }

    //upstream: also _token.Leaver
    /// <summary>Leaves the current parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}

/// <summary>An escaped character (<c>@@</c>, <c>@{</c> or <c>@}</c>).</summary>
public class EscapeChar : Lex.Character
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"@[@{}]";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<EscapeChar>(Pattern, (t, p) => new EscapeChar(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal EscapeChar(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>An accent command (e.g. <c>@'e</c> or <c>@'{e}</c>).</summary>
public class Accent : EscapeChar
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"@['""',=^`~](\{[a-zA-Z]\}|[a-zA-Z]\b)"; //upstream note: the duplicate apostrophe in the class is upstream's.

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Accent>(Pattern, (t, p) => new Accent(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Accent(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Verbatim text inside a <c>@verbatim</c> environment.</summary>
public class Verbatim : Lex.Token
{
    /// <summary>The default-token factory (no pattern); ParseVerbatim's default.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Factory<Verbatim>((t, p) => new Verbatim(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Verbatim(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Starts a <c>@verbatim</c> environment.</summary>
public class VerbatimStart : Keyword
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"@verbatim\b";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<VerbatimStart>(Pattern, (t, p) => new VerbatimStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal VerbatimStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the verbatim parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseVerbatim());
}

/// <summary>Ends a <c>@verbatim</c> environment (<c>@end verbatim</c>).</summary>
public class VerbatimEnd : Keyword //upstream note: also derives _token.Leaver.
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"@end\s+verbatim\b";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<VerbatimEnd>(Pattern, (t, p) => new VerbatimEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal VerbatimEnd(string text, int pos)
        : base(text, pos)
    {
    }

    //upstream: also _token.Leaver
    /// <summary>Leaves the current parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}

/// <summary>Starts a brace-style <c>@lilypond{ ... }</c> block.</summary>
public class LilyPondBlockStart : Block
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"@lilypond(?=(\[[a-zA-Z,=0-9\\\s]+\])?\{)";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LilyPondBlockStart>(Pattern, (t, p) => new LilyPondBlockStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LilyPondBlockStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the lilypond-block-attribute parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseLilyPondBlockAttr());
}

/// <summary>The opening brace of a <c>@lilypond{ ... }</c> block.</summary>
public class LilyPondBlockStartBrace : Block
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\{";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LilyPondBlockStartBrace>(Pattern, (t, p) => new LilyPondBlockStartBrace(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LilyPondBlockStartBrace(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Replaces the current parser with the LilyPond block parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Replace(new ParseLilyPondBlock());
}

/// <summary>The closing brace of a <c>@lilypond{ ... }</c> block.</summary>
public class LilyPondBlockEnd : Block //upstream note: also derives _token.Leaver.
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\}";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LilyPondBlockEnd>(Pattern, (t, p) => new LilyPondBlockEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LilyPondBlockEnd(string text, int pos)
        : base(text, pos)
    {
    }

    //upstream: also _token.Leaver
    /// <summary>Leaves the current parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}

/// <summary>Starts a <c>@lilypond ... @end lilypond</c> environment.</summary>
public class LilyPondEnvStart : Keyword
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"@lilypond\b";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<LilyPondEnvStart>(Pattern, (t, p) => new LilyPondEnvStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LilyPondEnvStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the lilypond-environment-attribute parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseLilyPondEnvAttr());
}

/// <summary>Ends a LilyPond environment (<c>@end lilypond</c>).</summary>
public class LilyPondEnvEnd : Keyword //upstream note: also derives _token.Leaver.
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"@end\s+lilypond\b";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<LilyPondEnvEnd>(Pattern, (t, p) => new LilyPondEnvEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LilyPondEnvEnd(string text, int pos)
        : base(text, pos)
    {
    }

    //upstream: also _token.Leaver
    /// <summary>Leaves the current parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}

/// <summary>Starts a <c>@lilypondfile</c> block.</summary>
public class LilyPondFileStart : Block
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"@lilypondfile\b";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LilyPondFileStart>(Pattern, (t, p) => new LilyPondFileStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LilyPondFileStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the lilypondfile parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseLilyPondFile());
}

/// <summary>The opening brace of a <c>@lilypondfile{ ... }</c> block.</summary>
public class LilyPondFileStartBrace : Block
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\{";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LilyPondFileStartBrace>(Pattern, (t, p) => new LilyPondFileStartBrace(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LilyPondFileStartBrace(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Replaces the current parser with the block parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Replace(new ParseBlock());
}

/// <summary>Starts a LilyPond option attribute (<c>[</c>).</summary>
public class LilyPondAttrStart : Attribute
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\[";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<LilyPondAttrStart>(Pattern, (t, p) => new LilyPondAttrStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LilyPondAttrStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the lilypond-attribute parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseLilyPondAttr());
}

/// <summary>Ends a LilyPond option attribute (<c>]</c>).</summary>
public class LilyPondAttrEnd : Attribute //upstream note: also derives _token.Leaver.
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\]";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<LilyPondAttrEnd>(Pattern, (t, p) => new LilyPondAttrEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LilyPondAttrEnd(string text, int pos)
        : base(text, pos)
    {
    }

    //upstream: also _token.Leaver
    /// <summary>Leaves the current parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}
