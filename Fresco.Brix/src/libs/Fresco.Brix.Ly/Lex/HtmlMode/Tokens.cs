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

namespace Fresco.Brix.Ly.Lex.HtmlMode; //was previously: ly/lex/html.py (token classes);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.
// Parses and tokenizes HTML input, recognizing LilyPond in HTML.

/// <summary>Base class for tokens that belong to an HTML comment.</summary>
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

/// <summary>Starts an HTML comment (<c>&lt;!--</c>).</summary>
public class CommentStart : Comment //upstream note: also derives _token.BlockCommentStart.
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"<!--";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<CommentStart>(Pattern, (t, p) => new CommentStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal CommentStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the comment parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseComment());
}

/// <summary>Ends an HTML comment (<c>--&gt;</c>).</summary>
public class CommentEnd : Comment //upstream note: also derives _token.Leaver and _token.BlockCommentEnd.
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"-->";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<CommentEnd>(Pattern, (t, p) => new CommentEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal CommentEnd(string text, int pos)
        : base(text, pos)
    {
    }

    //upstream: also _token.Leaver
    /// <summary>Leaves the current parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}

/// <summary>Base class for tokens that belong to an HTML string.</summary>
public class StringBase : Lex.StringBase
{
    /// <summary>The default-token factory (no pattern); the string parsers' default.</summary>
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

/// <summary>Base class for HTML tag tokens.</summary>
public abstract class Tag : Lex.Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Tag(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Starts an HTML tag (<c>&lt;name</c> or <c>&lt;/name</c>).</summary>
public class TagStart : Tag
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"</?\w[-_:\w]*\b";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<TagStart>(Pattern, (t, p) => new TagStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal TagStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the attribute parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseAttr());
}

/// <summary>Ends an HTML tag (<c>&gt;</c> or <c>/&gt;</c>).</summary>
public class TagEnd : Tag //upstream note: also derives _token.Leaver.
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"/?>";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<TagEnd>(Pattern, (t, p) => new TagEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal TagEnd(string text, int pos)
        : base(text, pos)
    {
    }

    //upstream: also _token.Leaver
    /// <summary>Leaves the current parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}

/// <summary>An attribute name inside an HTML tag.</summary>
public class AttrName : Lex.Token
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\w+([-_:]\w+)?";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<AttrName>(Pattern, (t, p) => new AttrName(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal AttrName(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The equals sign between an attribute name and its value.</summary>
public class EqualSign : Lex.Token
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"=";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<EqualSign>(Pattern, (t, p) => new EqualSign(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal EqualSign(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the value parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseValue());
}

/// <summary>An unquoted attribute value; leaves the value parser.</summary>
public class Value : Lex.Leaver
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\w+";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Value>(Pattern, (t, p) => new Value(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Value(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Starts a double-quoted attribute string.</summary>
public class StringDQStart : StringBase //upstream note: also derives _token.StringStart.
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"""";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<StringDQStart>(Pattern, (t, p) => new StringDQStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal StringDQStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the double-quoted string parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseStringDQ());
}

/// <summary>Starts a single-quoted attribute string.</summary>
public class StringSQStart : StringBase //upstream note: also derives _token.StringStart.
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"'";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<StringSQStart>(Pattern, (t, p) => new StringSQStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal StringSQStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the single-quoted string parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseStringSQ());
}

/// <summary>Ends a double-quoted attribute string.</summary>
public class StringDQEnd : StringBase //upstream note: also derives _token.StringEnd and _token.Leaver.
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"""";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<StringDQEnd>(Pattern, (t, p) => new StringDQEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal StringDQEnd(string text, int pos)
        : base(text, pos)
    {
    }

    //upstream: also _token.Leaver
    /// <summary>Leaves the current parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}

/// <summary>Ends a single-quoted attribute string.</summary>
public class StringSQEnd : StringBase //upstream note: also derives _token.StringEnd and _token.Leaver.
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"'";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<StringSQEnd>(Pattern, (t, p) => new StringSQEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal StringSQEnd(string text, int pos)
        : base(text, pos)
    {
    }

    //upstream: also _token.Leaver
    /// <summary>Leaves the current parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}

/// <summary>An HTML entity reference (e.g. <c>&amp;amp;</c>).</summary>
public class EntityRef : Lex.Character
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\&(#\d+|#[xX][0-9A-Fa-f]+|[A-Za-z_:][\w.:_-]*);";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<EntityRef>(Pattern, (t, p) => new EntityRef(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal EntityRef(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for the LilyPond-in-HTML tag tokens.</summary>
public abstract class LilyPondTag : Tag
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected LilyPondTag(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The <c>&lt;lilypondversion/&gt;</c> tag.</summary>
public class LilyPondVersionTag : LilyPondTag
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"<lilypondversion/?>";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LilyPondVersionTag>(Pattern, (t, p) => new LilyPondVersionTag(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LilyPondVersionTag(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Starts a <c>&lt;lilypondfile&gt;</c> tag.</summary>
public class LilyPondFileTag : LilyPondTag
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"</?lilypondfile\b";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LilyPondFileTag>(Pattern, (t, p) => new LilyPondFileTag(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LilyPondFileTag(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the lilypondfile-options parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseLilyPondFileOptions());
}

/// <summary>Ends a <c>&lt;lilypondfile&gt;</c> tag.</summary>
public class LilyPondFileTagEnd : LilyPondTag //upstream note: also derives _token.Leaver.
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"/?>";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LilyPondFileTagEnd>(Pattern, (t, p) => new LilyPondFileTagEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LilyPondFileTagEnd(string text, int pos)
        : base(text, pos)
    {
    }

    //upstream: also _token.Leaver
    /// <summary>Leaves the current parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}

/// <summary>Starts an inline <c>&lt;lilypond</c> tag.</summary>
public class LilyPondInlineTag : LilyPondTag
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"<lilypond\b";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LilyPondInlineTag>(Pattern, (t, p) => new LilyPondInlineTag(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LilyPondInlineTag(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the lilypond-attribute parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseLilyPondAttr());
}

/// <summary>The closing <c>&lt;/lilypond&gt;</c> tag.</summary>
public class LilyPondCloseTag : LilyPondTag //upstream note: also derives _token.Leaver.
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"</lilypond>";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LilyPondCloseTag>(Pattern, (t, p) => new LilyPondCloseTag(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LilyPondCloseTag(string text, int pos)
        : base(text, pos)
    {
    }

    //upstream: also _token.Leaver
    /// <summary>Leaves the current parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}

/// <summary>The <c>&gt;</c> ending an opening <c>&lt;lilypond&gt;</c> tag; switches to LilyPond parsing.</summary>
public class LilyPondTagEnd : LilyPondTag
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @">";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LilyPondTagEnd>(Pattern, (t, p) => new LilyPondTagEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LilyPondTagEnd(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Replaces the current parser with the LilyPond parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Replace(new ParseLilyPond());
}

/// <summary>Ends an inline <c>&lt;lilypond&gt;</c> tag (<c>&gt;</c> or <c>/&gt;</c>).</summary>
public class LilyPondInlineTagEnd : LilyPondTag //upstream note: also derives _token.Leaver.
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"/?>";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LilyPondInlineTagEnd>(Pattern, (t, p) => new LilyPondInlineTagEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LilyPondInlineTagEnd(string text, int pos)
        : base(text, pos)
    {
    }

    //upstream: also _token.Leaver
    /// <summary>Leaves the current parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}

/// <summary>The colon in an inline <c>&lt;lilypond:</c> tag; switches to inline music parsing.</summary>
public class SemiColon : Lex.Token
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @":";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<SemiColon>(Pattern, (t, p) => new SemiColon(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal SemiColon(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Replaces the current parser with the inline-LilyPond parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Replace(new ParseLilyPondInline());
}
