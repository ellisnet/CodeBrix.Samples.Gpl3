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

namespace Fresco.Brix.Ly.Lex.LilyPondMode; //was previously: ly/lex/lilypond.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.
//
// This file covers lilypond.py lines 31-394: the module-level pattern constants
// (re_identifier .. re_scaling) and the token classes Identifier through Dynamic.

/// <summary>The module-level pattern constants of ly/lex/lilypond.py.</summary>
internal static class Rx
{
    /// <summary>An identifier allowing letters and single hyphens in between —
    /// upstream <c>re_identifier</c>.</summary>
    internal const string ReIdentifier = @"[^\W\d_]+([_-][^\W\d_]+)*";

    /// <summary>The lookahead pattern for the end of an identifier (ref) —
    /// upstream <c>re_identifier_end</c>.</summary>
    internal const string ReIdentifierEnd = @"(?![_-]?[^\W\d])";

    /// <summary>Upstream <c>re_articulation</c>.</summary>
    internal const string ReArticulation = @"[-_^][_.>|!+^-]";

    /// <summary>Upstream <c>re_dynamic</c>.</summary>
    internal const string ReDynamic =
        @"\\[<!>]|"
        + @"\\(f{1,5}|p{1,5}"
        + @"|mf|mp|fp|spp?|sff?|sfz|rfz|n"
        + @"|cresc|decresc|dim|cr|decr"
        + @")(?![A-Za-z])";

    /// <summary>Upstream <c>re_duration</c>.</summary>
    internal const string ReDuration =
        @"(\\(maxima|longa|breve)" + ReIdentifierEnd
        + @"|(1|2|4|8|16|32|64|128|256|512|1024|2048)(?!\d))";

    /// <summary>Upstream <c>re_dot</c>.</summary>
    internal const string ReDot = @"\.";

    /// <summary>Upstream <c>re_scaling</c>.</summary>
    internal const string ReScaling = @"\*[\t ]*\d+(/\d+)?";
}

/// <summary>A variable name, like <c>some-variable</c>.</summary>
public abstract class Identifier : Token
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"(?<![^\W\d])" + Rx.ReIdentifier + Rx.ReIdentifierEnd;

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Identifier(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A reference to an identifier, e.g. <c>\some-variable</c>.</summary>
public abstract class IdentifierRef : Token
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\\" + Rx.ReIdentifier + Rx.ReIdentifierEnd;

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected IdentifierRef(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A variable name (base class for specific variables).</summary>
public abstract class Variable : Identifier
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Variable(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A user-defined variable name.</summary>
public class UserVariable : Identifier
{
    /// <summary>The matching rule (inherits the <see cref="Identifier"/> pattern).</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<UserVariable>(Identifier.Pattern, (t, p) => new UserVariable(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal UserVariable(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for numeric values that end an argument.</summary>
public abstract class Value : Numeric
{
    //upstream note: Value(_token.Item, _token.Numeric) - _token.Item is the first
    //base and carries the endArgument behaviour; _token.Numeric is kept as the C#
    //base class for the isinstance relationship consumers use.

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Value(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Ends one argument of the enclosing parser stack — the
    /// <c>_token.Item</c> behaviour.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => ((Lex.State)state).EndArgument();
}

/// <summary>A decimal value.</summary>
public class DecimalValue : Value
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"-?\d+(\.\d+)?";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<DecimalValue>(Pattern, (t, p) => new DecimalValue(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal DecimalValue(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>An integer value.</summary>
public class IntegerValue : DecimalValue
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\d+";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<IntegerValue>(Pattern, (t, p) => new IntegerValue(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal IntegerValue(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A fraction value.</summary>
public class Fraction : Value
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\d+/\d+";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Fraction>(Pattern, (t, p) => new Fraction(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Fraction(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for delimiter tokens.</summary>
public abstract class Delimiter : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Delimiter(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A dot in dotted path notation.</summary>
public class DotPath : Delimiter
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\.";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<DotPath>(Pattern, (t, p) => new DotPath(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal DotPath(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Erroneous input in LilyPond mode.</summary>
public class Error : ErrorBase
{
    /// <summary>The default-token factory (no pattern) — used as
    /// <c>default = Error</c> by <see cref="ExpectOpenBracket"/>.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Factory<Error>((t, p) => new Error(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Error(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for comment tokens in LilyPond mode.</summary>
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

/// <summary>The start of a block comment, <c>%{</c>.</summary>
public class BlockCommentStart : Lex.BlockCommentStart
{
    //upstream note: BlockCommentStart(Comment, _token.BlockCommentStart) - the
    //_token base is kept as the C# base class; the mode-local Comment base is noted.

    /// <summary>The pattern.</summary>
    internal const string Pattern = @"%{";

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

/// <summary>The end of a block comment, <c>%}</c>.</summary>
public class BlockCommentEnd : Lex.BlockCommentEnd
{
    //upstream note: BlockCommentEnd(Comment, _token.BlockCommentEnd, _token.Leaver)
    //- the leave behaviour is written out below; the mode-local Comment base is noted.

    /// <summary>The pattern.</summary>
    internal const string Pattern = @"%}";

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

/// <summary>A line comment, from <c>%</c> to the end of the line.</summary>
public class LineComment : Lex.LineComment
{
    //upstream note: LineComment(Comment, _token.LineComment) - the _token base is
    //kept as the C# base class; the mode-local Comment base is noted.

    /// <summary>The pattern.</summary>
    internal const string Pattern = @"%.*$";

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

/// <summary>A note, rest, spacer, <c>\skip</c> or <c>q</c>.</summary>
public abstract class MusicItem : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected MusicItem(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The <c>\skip</c> command.</summary>
public class Skip : MusicItem
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\\skip" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Skip>(Pattern, (t, p) => new Skip(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Skip(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A spacer rest, <c>s</c>.</summary>
public class Spacer : MusicItem
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"s(?![A-Za-z])";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Spacer>(Pattern, (t, p) => new Spacer(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Spacer(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A rest, <c>r</c> or <c>R</c>.</summary>
public class Rest : MusicItem
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"[Rr](?![A-Za-z])";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Rest>(Pattern, (t, p) => new Rest(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Rest(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A note name.</summary>
public class Note : MusicItem
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"[a-x]+(?![A-Za-z])";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Note>(Pattern, (t, p) => new Note(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Note(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The chord repetition <c>q</c>.</summary>
public class Q : MusicItem
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"q(?![A-Za-z])";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Q>(Pattern, (t, p) => new Q(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Q(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A drum note name.</summary>
public class DrumNote : MusicItem
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"[a-z]+(?![A-Za-z])";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<DrumNote>(Pattern, (t, p) => new DrumNote(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal DrumNote(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>An octave mark, commas or apostrophes.</summary>
public class Octave : Token
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @",+|'+";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Octave>(Pattern, (t, p) => new Octave(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Octave(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>An octave check, <c>=</c> plus optional octave marks.</summary>
public class OctaveCheck : Token
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"=(,+|'+)?";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<OctaveCheck>(Pattern, (t, p) => new OctaveCheck(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal OctaveCheck(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for accidental tokens.</summary>
public abstract class Accidental : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Accidental(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The reminder accidental, <c>!</c>.</summary>
public class AccidentalReminder : Accidental
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"!";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<AccidentalReminder>(Pattern, (t, p) => new AccidentalReminder(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal AccidentalReminder(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The cautionary accidental, <c>?</c>.</summary>
public class AccidentalCautionary : Accidental
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\?";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<AccidentalCautionary>(Pattern, (t, p) => new AccidentalCautionary(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal AccidentalCautionary(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for duration tokens.</summary>
public abstract class Duration : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Duration(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A duration length, like <c>4</c> or <c>\breve</c>.</summary>
public class Length : Duration
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = Rx.ReDuration;

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Length>(Pattern, (t, p) => new Length(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Length(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the duration parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseDuration());
}

/// <summary>An augmentation dot.</summary>
public class Dot : Duration
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = Rx.ReDot;

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

/// <summary>A duration scaling, like <c>*1/2</c>.</summary>
public class Scaling : Duration
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = Rx.ReScaling;

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Scaling>(Pattern, (t, p) => new Scaling(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Scaling(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>An open bracket, does not enter different parser, subclass or
/// reimplement Parser.update_state().</summary>
public class OpenBracket : Delimiter, IMatchStart, IIndent
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\{";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<OpenBracket>(Pattern, (t, p) => new OpenBracket(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal OpenBracket(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>The match-pair name.</summary>
    public string MatchName => "bracket";
}

/// <summary>A close bracket.</summary>
public class CloseBracket : Delimiter, IMatchEnd, IDedent
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\}";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<CloseBracket>(Pattern, (t, p) => new CloseBracket(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal CloseBracket(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>The match-pair name.</summary>
    public string MatchName => "bracket";

    /// <summary>Leaves the current parser, then ends one argument.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state)
    {
        state.Leave();
        ((Lex.State)state).EndArgument();
    }
}

/// <summary>An open double French quote, does not enter different parser, subclass
/// or reimplement Parser.update_state().</summary>
public class OpenSimultaneous : Delimiter, IMatchStart, IIndent
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"<<";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<OpenSimultaneous>(Pattern, (t, p) => new OpenSimultaneous(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal OpenSimultaneous(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>The match-pair name.</summary>
    public string MatchName => "simultaneous";
}

/// <summary>A close double French quote.</summary>
public class CloseSimultaneous : Delimiter, IMatchEnd, IDedent
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @">>";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<CloseSimultaneous>(Pattern, (t, p) => new CloseSimultaneous(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal CloseSimultaneous(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>The match-pair name.</summary>
    public string MatchName => "simultaneous";

    /// <summary>Leaves the current parser, then ends one argument.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state)
    {
        state.Leave();
        ((Lex.State)state).EndArgument();
    }
}

/// <summary>An open bracket starting a sequential music expression.</summary>
public class SequentialStart : OpenBracket
{
    /// <summary>The matching rule (inherits the <see cref="OpenBracket"/> pattern).</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<SequentialStart>(OpenBracket.Pattern, (t, p) => new SequentialStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal SequentialStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the music parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseMusic());
}

/// <summary>A close bracket ending a sequential music expression.</summary>
public class SequentialEnd : CloseBracket
{
    /// <summary>The matching rule (inherits the <see cref="CloseBracket"/> pattern).</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<SequentialEnd>(CloseBracket.Pattern, (t, p) => new SequentialEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal SequentialEnd(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A <c>&lt;&lt;</c> starting a simultaneous music expression.</summary>
public class SimultaneousStart : OpenSimultaneous
{
    /// <summary>The matching rule (inherits the <see cref="OpenSimultaneous"/> pattern).</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<SimultaneousStart>(
            OpenSimultaneous.Pattern, (t, p) => new SimultaneousStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal SimultaneousStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the music parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseMusic());
}

/// <summary>A <c>&gt;&gt;</c> ending a simultaneous music expression.</summary>
public class SimultaneousEnd : CloseSimultaneous
{
    /// <summary>The matching rule (inherits the <see cref="CloseSimultaneous"/> pattern).</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<SimultaneousEnd>(
            CloseSimultaneous.Pattern, (t, p) => new SimultaneousEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal SimultaneousEnd(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The pipe symbol (bar check).</summary>
public class PipeSymbol : Delimiter
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\|";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<PipeSymbol>(Pattern, (t, p) => new PipeSymbol(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal PipeSymbol(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for articulation things.</summary>
public abstract class Articulation : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Articulation(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>An articulation given as a backslashed command.</summary>
public class ArticulationCommand : Articulation
{
    //upstream note: ArticulationCommand(Articulation, IdentifierRef) - derives the
    //first base; the IdentifierRef relationship supplies the pattern below.

    /// <summary>The matching rule (the <see cref="IdentifierRef"/> pattern with the
    /// articulation-words <c>test_match</c>).</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<ArticulationCommand>(
            IdentifierRef.Pattern,
            (t, p) => new ArticulationCommand(t, p),
            m =>
            {
                string s = m.Value.Substring(1);
                if (!s.Contains('-'))
                {
                    return Words.Contains(Words.Articulations, s)
                        || Words.Contains(Words.Ornaments, s)
                        || Words.Contains(Words.Fermatas, s)
                        || Words.Contains(Words.InstrumentScripts, s)
                        || Words.Contains(Words.RepeatScripts, s)
                        || Words.Contains(Words.AncientScripts, s);
                }

                return false;
            });

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal ArticulationCommand(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A direction prefix, <c>-</c>, <c>_</c> or <c>^</c>.</summary>
public class Direction : Token
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"[-_^]";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Direction>(Pattern, (t, p) => new Direction(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Direction(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the script-abbreviation-or-fingering parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state)
        => state.Enter(new ParseScriptAbbreviationOrFingering());
}

/// <summary>A script abbreviation character.</summary>
public class ScriptAbbreviation : Articulation
{
    //upstream note: ScriptAbbreviation(Articulation, _token.Leaver) - the leave
    //behaviour is written out below.

    /// <summary>The pattern.</summary>
    internal const string Pattern = @"[+|!>._^-]";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<ScriptAbbreviation>(Pattern, (t, p) => new ScriptAbbreviation(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal ScriptAbbreviation(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Leaves the current parser — the <c>_token.Leaver</c> behaviour.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}

/// <summary>A fingering number.</summary>
public class Fingering : Articulation
{
    //upstream note: Fingering(Articulation, _token.Leaver) - the leave behaviour is
    //written out below.

    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\d+";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Fingering>(Pattern, (t, p) => new Fingering(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Fingering(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Leaves the current parser — the <c>_token.Leaver</c> behaviour.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}

/// <summary>A string number, like <c>\3</c>.</summary>
public class StringNumber : Articulation
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\\\d+";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<StringNumber>(Pattern, (t, p) => new StringNumber(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal StringNumber(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for slur tokens.</summary>
public abstract class Slur : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Slur(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A slur start, <c>(</c>.</summary>
public class SlurStart : Slur, IMatchStart
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\(";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<SlurStart>(Pattern, (t, p) => new SlurStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal SlurStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>The match-pair name.</summary>
    public virtual string MatchName => "slur";
}

/// <summary>A slur end, <c>)</c>.</summary>
public class SlurEnd : Slur, IMatchEnd
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\)";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<SlurEnd>(Pattern, (t, p) => new SlurEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal SlurEnd(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>The match-pair name.</summary>
    public virtual string MatchName => "slur";
}

/// <summary>A phrasing slur start, <c>\(</c>.</summary>
public class PhrasingSlurStart : SlurStart
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\\(";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<PhrasingSlurStart>(Pattern, (t, p) => new PhrasingSlurStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal PhrasingSlurStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>The match-pair name.</summary>
    public override string MatchName => "phrasingslur";
}

/// <summary>A phrasing slur end, <c>\)</c>.</summary>
public class PhrasingSlurEnd : SlurEnd
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\\)";

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<PhrasingSlurEnd>(Pattern, (t, p) => new PhrasingSlurEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal PhrasingSlurEnd(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>The match-pair name.</summary>
    public override string MatchName => "phrasingslur";
}

/// <summary>A tie, <c>~</c>.</summary>
public class Tie : Slur
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"~";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Tie>(Pattern, (t, p) => new Tie(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Tie(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for beam tokens.</summary>
public abstract class Beam : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Beam(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A beam start, <c>[</c>.</summary>
public class BeamStart : Beam, IMatchStart
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\[";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<BeamStart>(Pattern, (t, p) => new BeamStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal BeamStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>The match-pair name.</summary>
    public string MatchName => "beam";
}

/// <summary>A beam end, <c>]</c>.</summary>
public class BeamEnd : Beam, IMatchEnd
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\]";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<BeamEnd>(Pattern, (t, p) => new BeamEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal BeamEnd(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>The match-pair name.</summary>
    public string MatchName => "beam";
}

/// <summary>Base class for ligature tokens.</summary>
public abstract class Ligature : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Ligature(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A ligature start, <c>\[</c>.</summary>
public class LigatureStart : Ligature, IMatchStart
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\\\[";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LigatureStart>(Pattern, (t, p) => new LigatureStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LigatureStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>The match-pair name.</summary>
    public string MatchName => "ligature";
}

/// <summary>A ligature end, <c>\]</c>.</summary>
public class LigatureEnd : Ligature, IMatchEnd
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\\\]";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LigatureEnd>(Pattern, (t, p) => new LigatureEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LigatureEnd(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>The match-pair name.</summary>
    public string MatchName => "ligature";
}

/// <summary>Base class for tremolo tokens.</summary>
public abstract class Tremolo : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Tremolo(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The colon starting a tremolo.</summary>
public class TremoloColon : Tremolo
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @":";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<TremoloColon>(Pattern, (t, p) => new TremoloColon(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal TremoloColon(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the tremolo parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseTremolo());
}

/// <summary>A tremolo duration.</summary>
public class TremoloDuration : Tremolo
{
    //upstream note: TremoloDuration(Tremolo, _token.Leaver) - the leave behaviour
    //is written out below.

    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\b(8|16|32|64|128|256|512|1024|2048)(?!\d)";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<TremoloDuration>(Pattern, (t, p) => new TremoloDuration(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal TremoloDuration(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Leaves the current parser — the <c>_token.Leaver</c> behaviour.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}

/// <summary>Base class for chordmode items.</summary>
public abstract class ChordItem : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected ChordItem(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A chord modifier, like <c>maj</c>.</summary>
public class ChordModifier : ChordItem
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"((?<![a-z])|^)(aug|dim|sus|min|maj|m)(?![a-z])";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<ChordModifier>(Pattern, (t, p) => new ChordModifier(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal ChordModifier(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A chord separator, like <c>:</c> or <c>/</c>.</summary>
public class ChordSeparator : ChordItem
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @":|\^|/\+?";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<ChordSeparator>(Pattern, (t, p) => new ChordSeparator(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal ChordSeparator(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A chord step number.</summary>
public class ChordStepNumber : ChordItem
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\d+[-+]?";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<ChordStepNumber>(Pattern, (t, p) => new ChordStepNumber(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal ChordStepNumber(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A dot between chord step numbers.</summary>
public class DotChord : ChordItem
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\.";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<DotChord>(Pattern, (t, p) => new DotChord(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal DotChord(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The voice separator, <c>\\</c>.</summary>
public class VoiceSeparator : Delimiter
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\\\\";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<VoiceSeparator>(Pattern, (t, p) => new VoiceSeparator(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal VoiceSeparator(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A dynamic mark, like <c>\ff</c>.</summary>
public class Dynamic : Token
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = Rx.ReDynamic;

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Dynamic>(Pattern, (t, p) => new Dynamic(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Dynamic(string text, int pos)
        : base(text, pos)
    {
    }
}
