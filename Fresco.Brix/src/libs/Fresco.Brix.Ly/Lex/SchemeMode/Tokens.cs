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

using Fresco.Brix.Ly.Data;
using Fresco.Brix.Ly.Slexing;

namespace Fresco.Brix.Ly.Lex.SchemeMode; //was previously: ly/lex/scheme.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.
//
// This file covers scheme.py lines 30-174: the token classes Scheme through
// LilyPondEnd.

/// <summary>Baseclass for Scheme tokens.</summary>
public abstract class Scheme : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Scheme(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Text inside a quoted string — upstream class <c>String</c>, renamed
/// for System.String.</summary>
public class StringBase : Lex.StringBase
{
    /// <summary>The default-token factory (no pattern) — used as
    /// <c>default = String</c> by <see cref="ParseString"/>.</summary>
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

/// <summary>The double quote starting a string.</summary>
public class StringQuotedStart : StringStart
{
    //upstream note: StringQuotedStart(String, _token.StringStart) - the _token base
    //is kept as the C# base class; the mode-local String base is noted.

    /// <summary>The pattern.</summary>
    internal const string Pattern = "\"";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
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

/// <summary>The double quote ending a string.</summary>
public class StringQuotedEnd : StringEnd
{
    //upstream note: StringQuotedEnd(String, _token.StringEnd) - the _token base is
    //kept as the C# base class; the mode-local String base is noted.

    /// <summary>The pattern.</summary>
    internal const string Pattern = "\"";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<StringQuotedEnd>(Pattern, (t, p) => new StringQuotedEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal StringQuotedEnd(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Leaves the string parser, then ends one argument.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state)
    {
        state.Leave();
        ((Lex.State)state).EndArgument();
    }
}

/// <summary>An escaped character inside a string.</summary>
public class StringQuoteEscape : Character
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

/// <summary>Base class for comment tokens in Scheme mode.</summary>
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

/// <summary>A line comment, from <c>;</c> to the end of the line.</summary>
public class LineComment : Lex.LineComment
{
    //upstream note: LineComment(Comment, _token.LineComment) - the _token base is
    //kept as the C# base class; the mode-local Comment base is noted.

    /// <summary>The pattern.</summary>
    internal const string Pattern = @";.*$";

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

/// <summary>The start of a block comment, <c>#!</c>.</summary>
public class BlockCommentStart : Lex.BlockCommentStart
{
    //upstream note: BlockCommentStart(Comment, _token.BlockCommentStart) - the
    //_token base is kept as the C# base class; the mode-local Comment base is noted.

    /// <summary>The pattern.</summary>
    internal const string Pattern = @"#!";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<BlockCommentStart>(Pattern, (t, p) => new BlockCommentStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal BlockCommentStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the block-comment parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseBlockComment());
}

/// <summary>The end of a block comment, <c>!#</c>.</summary>
public class BlockCommentEnd : Lex.BlockCommentEnd
{
    //upstream note: BlockCommentEnd(Comment, _token.BlockCommentEnd, _token.Leaver)
    //- the leave behaviour is written out below; the mode-local Comment base is noted.

    /// <summary>The pattern.</summary>
    internal const string Pattern = @"!#";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<BlockCommentEnd>(Pattern, (t, p) => new BlockCommentEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal BlockCommentEnd(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Leaves the current parser — the <c>_token.Leaver</c> behaviour.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}

/// <summary>Text inside a block comment.</summary>
public class BlockComment : Lex.BlockComment
{
    //upstream note: BlockComment(Comment, _token.BlockComment) - the _token base is
    //kept as the C# base class; the mode-local Comment base is noted.

    /// <summary>The default-token factory (no pattern) — used as
    /// <c>default = BlockComment</c> by <see cref="ParseBlockComment"/>.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Factory<BlockComment>((t, p) => new BlockComment(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal BlockComment(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>An open parenthesis.</summary>
public class OpenParen : Scheme, IMatchStart, IIndent
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\(";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<OpenParen>(Pattern, (t, p) => new OpenParen(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal OpenParen(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>The match-pair name.</summary>
    public string MatchName => "schemeparen";

    /// <summary>Enters a new Scheme parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseScheme());
}

/// <summary>A close parenthesis.</summary>
public class CloseParen : Scheme, IMatchEnd, IDedent
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\)";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<CloseParen>(Pattern, (t, p) => new CloseParen(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal CloseParen(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>The match-pair name.</summary>
    public string MatchName => "schemeparen";

    /// <summary>Leaves the current parser, then ends one argument.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state)
    {
        state.Leave();
        ((Lex.State)state).EndArgument();
    }
}

/// <summary>A quote, quasiquote or unquote character.</summary>
public class Quote : Scheme
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"['`,]";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Quote>(Pattern, (t, p) => new Quote(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Quote(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The dot in a dotted pair.</summary>
public class Dot : Scheme
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\.(?!\S)";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Dot>(Pattern, (t, p) => new Dot(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Dot(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A boolean, <c>#t</c> or <c>#f</c>.</summary>
public class Bool : Scheme
{
    //upstream note: Bool(Scheme, _token.Item) - the endArgument behaviour is
    //written out below.

    /// <summary>The pattern.</summary>
    internal const string Pattern = @"#[tf]\b";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Bool>(Pattern, (t, p) => new Bool(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Bool(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Ends one argument of the enclosing parser stack — the
    /// <c>_token.Item</c> behaviour.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => ((Lex.State)state).EndArgument();
}

/// <summary>A character constant, like <c>#\space</c>.</summary>
public class Char : Scheme
{
    //upstream note: Char(Scheme, _token.Item) - the endArgument behaviour is
    //written out below.

    /// <summary>The pattern.</summary>
    internal const string Pattern = @"#\\([a-z]+|.)";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Char>(Pattern, (t, p) => new Char(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Char(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Ends one argument of the enclosing parser stack — the
    /// <c>_token.Item</c> behaviour.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => ((Lex.State)state).EndArgument();
}

/// <summary>A Scheme word.</summary>
public class Word : Scheme
{
    //upstream note: Word(Scheme, _token.Item) - the endArgument behaviour is
    //written out below.

    /// <summary>The pattern.</summary>
    internal const string Pattern = @"[^()""{}\s]+";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Word>(Pattern, (t, p) => new Word(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Word(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Ends one argument of the enclosing parser stack — the
    /// <c>_token.Item</c> behaviour.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => ((Lex.State)state).EndArgument();
}

/// <summary>A Scheme keyword.</summary>
public class Keyword : Word
{
    /// <summary>The matching rule (the <see cref="Word"/> pattern with the
    /// scheme-keywords <c>test_match</c>).</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Keyword>(
            Word.Pattern,
            (t, p) => new Keyword(t, p),
            m => Words.Contains(LyData.SchemeKeywords(), m.Value));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Keyword(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A Scheme function name.</summary>
public class Function : Word
{
    /// <summary>The matching rule (the <see cref="Word"/> pattern with the
    /// scheme-functions <c>test_match</c>).</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Function>(
            Word.Pattern,
            (t, p) => new Function(t, p),
            m => Words.Contains(LyData.SchemeFunctions(), m.Value));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Function(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A Scheme variable name.</summary>
public class Variable : Word
{
    /// <summary>The matching rule (the <see cref="Word"/> pattern with the
    /// scheme-variables <c>test_match</c>).</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Variable>(
            Word.Pattern,
            (t, p) => new Variable(t, p),
            m => Words.Contains(LyData.SchemeVariables(), m.Value));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Variable(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A Scheme constant.</summary>
public class Constant : Word
{
    /// <summary>The matching rule (the <see cref="Word"/> pattern with the
    /// scheme-constants <c>test_match</c>).</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Constant>(
            Word.Pattern,
            (t, p) => new Constant(t, p),
            m => Words.Contains(LyData.SchemeConstants(), m.Value));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Constant(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A number.</summary>
public class Number : Numeric
{
    //upstream note: Number(_token.Item, _token.Numeric) - _token.Item is the first
    //base and carries the endArgument behaviour written out below; _token.Numeric is
    //kept as the C# base class for the isinstance relationship consumers use.

    /// <summary>The pattern.</summary>
    internal const string Pattern =
        @"("
        + @"-?\d+|"
        + @"#(b[0-1]+|o[0-7]+|x[0-9a-fA-F]+)|"
        + @"[-+]inf.0|[-+]?nan.0"
        + @")(?=$|[)\s])";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Number>(Pattern, (t, p) => new Number(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Number(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Ends one argument of the enclosing parser stack — the
    /// <c>_token.Item</c> behaviour.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => ((Lex.State)state).EndArgument();
}

/// <summary>A fraction number.</summary>
public class Fraction : Number
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"-?\d+/\d+(?=$|[)\s])";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Fraction>(Pattern, (t, p) => new Fraction(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Fraction(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A floating point number.</summary>
public class Float : Number
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"-?((\d+(\.\d*)|\.\d+)(E\d+)?)(?=$|[)\s])";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Float>(Pattern, (t, p) => new Float(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Float(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The <c>#(</c> starting a vector.</summary>
public class VectorStart : OpenParen
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"#\(";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<VectorStart>(Pattern, (t, p) => new VectorStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal VectorStart(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for LilyPond-inside-Scheme tokens.</summary>
public abstract class LilyPond : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected LilyPond(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The <c>#{</c> starting LilyPond input inside Scheme.</summary>
public class LilyPondStart : LilyPond, IMatchStart, IIndent
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"#{";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LilyPondStart>(Pattern, (t, p) => new LilyPondStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LilyPondStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>The match-pair name.</summary>
    public string MatchName => "schemelily";

    /// <summary>Enters the LilyPond-inside-Scheme parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseLilyPond());
}

/// <summary>The <c>#}</c> ending LilyPond input inside Scheme.</summary>
public class LilyPondEnd : LilyPond, IMatchEnd, IDedent
{
    //upstream note: LilyPondEnd(LilyPond, _token.Leaver, _token.MatchEnd,
    //_token.Dedent) - the leave behaviour is written out below.

    /// <summary>The pattern.</summary>
    internal const string Pattern = @"#}";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LilyPondEnd>(Pattern, (t, p) => new LilyPondEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LilyPondEnd(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>The match-pair name.</summary>
    public string MatchName => "schemelily";

    /// <summary>Leaves the current parser — the <c>_token.Leaver</c> behaviour.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}
