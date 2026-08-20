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

namespace Fresco.Brix.Ly.Lex.LilyPondMode; //was previously: ly/lex/lilypond.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.
//
// This file covers lilypond.py lines 397-890: the token classes Command through
// EqualSign.

/// <summary>A LilyPond music command, like <c>\relative</c>.</summary>
public class Command : IdentifierRef
{
    //upstream note: Command(_token.Item, IdentifierRef) - _token.Item is the first
    //base and carries the endArgument behaviour written out below; IdentifierRef is
    //kept as the C# base class for the isinstance relationship consumers use.

    /// <summary>The matching rule (the <see cref="IdentifierRef"/> pattern with the
    /// music-commands <c>test_match</c>).</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Command>(
            IdentifierRef.Pattern,
            (t, p) => new Command(t, p),
            m =>
            {
                string s = m.Value.Substring(1);
                if (!s.Contains('-'))
                {
                    return Words.Contains(Words.LilypondMusicCommands, s);
                }

                return false;
            });

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Command(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Ends one argument of the enclosing parser stack — the
    /// <c>_token.Item</c> behaviour.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => ((Lex.State)state).EndArgument();
}

/// <summary>A LilyPond keyword, like <c>\version</c>.</summary>
public class Keyword : IdentifierRef
{
    //upstream note: Keyword(_token.Item, IdentifierRef) - _token.Item is the first
    //base and carries the endArgument behaviour written out below; IdentifierRef is
    //kept as the C# base class for the isinstance relationship consumers use.

    /// <summary>The matching rule (the <see cref="IdentifierRef"/> pattern with the
    /// keywords <c>test_match</c>).</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<Keyword>(
            IdentifierRef.Pattern,
            (t, p) => new Keyword(t, p),
            m =>
            {
                string s = m.Value.Substring(1);
                if (!s.Contains('-'))
                {
                    return Words.Contains(Words.LilypondKeywords, s);
                }

                return false;
            });

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Keyword(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Ends one argument of the enclosing parser stack — the
    /// <c>_token.Item</c> behaviour.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => ((Lex.State)state).EndArgument();
}

/// <summary>A specifier of a command, e.g. the name of clef or repeat style.</summary>
public abstract class Specifier : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Specifier(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The <c>\score</c> keyword.</summary>
public class Score : Keyword
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\score" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Score>(Pattern, (t, p) => new Score(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Score(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the expect-score parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ExpectScore());
}

/// <summary>The <c>\book</c> keyword.</summary>
public class Book : Keyword
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\book" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Book>(Pattern, (t, p) => new Book(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Book(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the expect-book parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ExpectBook());
}

/// <summary>The <c>\bookpart</c> keyword.</summary>
public class BookPart : Keyword
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\bookpart" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<BookPart>(Pattern, (t, p) => new BookPart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal BookPart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the expect-bookpart parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ExpectBookPart());
}

/// <summary>The <c>\paper</c> keyword.</summary>
public class Paper : Keyword
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\paper" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Paper>(Pattern, (t, p) => new Paper(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Paper(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the expect-paper parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ExpectPaper());
}

/// <summary>The <c>\header</c> keyword.</summary>
public class Header : Keyword
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\header" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Header>(Pattern, (t, p) => new Header(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Header(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the expect-header parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ExpectHeader());
}

/// <summary>The <c>\layout</c> keyword.</summary>
public class Layout : Keyword
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\layout" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Layout>(Pattern, (t, p) => new Layout(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Layout(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the expect-layout parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ExpectLayout());
}

/// <summary>The <c>\midi</c> keyword.</summary>
public class Midi : Keyword
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\midi" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Midi>(Pattern, (t, p) => new Midi(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Midi(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the expect-midi parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ExpectMidi());
}

/// <summary>The <c>\with</c> keyword.</summary>
public class With : Keyword
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\with" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<With>(Pattern, (t, p) => new With(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal With(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the expect-with parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ExpectWith());
}

/// <summary>The <c>\context</c> keyword inside <c>\layout</c>.</summary>
public class LayoutContext : Keyword
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\context" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<LayoutContext>(Pattern, (t, p) => new LayoutContext(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LayoutContext(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the expect-context parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ExpectContext());
}

/// <summary>Base class for all markup commands.</summary>
public abstract class Markup : Item
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Markup(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The <c>\markup</c> command.</summary>
public class MarkupStart : Markup
{
    //upstream note: MarkupStart(Markup, Command) - derives the first base; the
    //Command relationship is not carried in C#.

    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\\markup" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<MarkupStart>(Pattern, (t, p) => new MarkupStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal MarkupStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the markup parser with one argument.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseMarkup(1));
}

/// <summary>The <c>\markuplines</c> command.</summary>
public class MarkupLines : Markup
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\\markuplines" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<MarkupLines>(Pattern, (t, p) => new MarkupLines(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal MarkupLines(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the markup parser with one argument.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseMarkup(1));
}

/// <summary>The <c>\markuplist</c> command.</summary>
public class MarkupList : Markup
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\\markuplist" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<MarkupList>(Pattern, (t, p) => new MarkupList(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal MarkupList(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the markup parser with one argument.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseMarkup(1));
}

/// <summary>A markup command.</summary>
public class MarkupCommand : Markup
{
    //upstream note: MarkupCommand(Markup, IdentifierRef) - derives the first base;
    //the IdentifierRef relationship supplies the pattern below.

    /// <summary>The matching rule (the <see cref="IdentifierRef"/> pattern with the
    /// markup-commands <c>test_match</c>).</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<MarkupCommand>(
            IdentifierRef.Pattern,
            (t, p) => new MarkupCommand(t, p),
            m => Words.Contains(Words.Markupcommands, m.Value.Substring(1)));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal MarkupCommand(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Ends the argument for a no-argument markup command, or enters the
    /// markup parser with the command's argument count.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state)
    {
        string command = Text.Substring(1);
        if (Words.Contains(Words.MarkupcommandsNargs[0], command))
        {
            ((Lex.State)state).EndArgument();
        }
        else
        {
            //upstream note: python's for/else - argcount stays 1 only when the loop
            //did NOT break on 2, 3, 4 or 5.
            int argcount = 1;
            foreach (int n in new[] { 2, 3, 4, 5 })
            {
                if (Words.Contains(Words.MarkupcommandsNargs[n], command))
                {
                    argcount = n;
                    break;
                }
            }

            state.Enter(new ParseMarkup(argcount));
        }
    }
}

/// <summary>The <c>\score</c> command inside markup.</summary>
public class MarkupScore : Markup
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\\score" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<MarkupScore>(Pattern, (t, p) => new MarkupScore(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal MarkupScore(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the expect-score parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ExpectScore());
}

/// <summary>A user-defined markup (i.e. not in the words markupcommands list).</summary>
public class MarkupUserCommand : Markup
{
    //upstream note: MarkupUserCommand(Markup, IdentifierRef) - derives the first
    //base; the IdentifierRef relationship supplies the pattern below.

    /// <summary>The matching rule (the <see cref="IdentifierRef"/> pattern; the last
    /// class of its shared-pattern group, so no <c>test_match</c>).</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<MarkupUserCommand>(
            IdentifierRef.Pattern, (t, p) => new MarkupUserCommand(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal MarkupUserCommand(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Ends one argument of the enclosing parser stack.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => ((Lex.State)state).EndArgument();
}

/// <summary>A word in markup text.</summary>
public class MarkupWord : Item
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"[^{}""\\\s#%]+";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<MarkupWord>(Pattern, (t, p) => new MarkupWord(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal MarkupWord(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>An open bracket inside markup.</summary>
public class OpenBracketMarkup : OpenBracket
{
    /// <summary>The matching rule (inherits the <see cref="OpenBracket"/> pattern).</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<OpenBracketMarkup>(
            OpenBracket.Pattern, (t, p) => new OpenBracketMarkup(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal OpenBracketMarkup(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the markup parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseMarkup());
}

/// <summary>A close bracket inside markup.</summary>
public class CloseBracketMarkup : CloseBracket
{
    /// <summary>The matching rule (inherits the <see cref="CloseBracket"/> pattern).</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<CloseBracketMarkup>(
            CloseBracket.Pattern, (t, p) => new CloseBracketMarkup(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal CloseBracketMarkup(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Goes back to the opening bracket — the ParseMarkup parser with the
    /// 0 argcount — then leaves it and ends one argument.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state)
    {
        // go back to the opening bracket, this is the ParseMarkup
        // parser with the 0 argcount
        while (((Lex.Parser)state.CurrentParser()).Argcount > 0)
        {
            state.Leave();
        }

        state.Leave();
        ((Lex.State)state).EndArgument();
    }
}

/// <summary>The <c>\repeat</c> command.</summary>
public class Repeat : Command
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\repeat" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Repeat>(Pattern, (t, p) => new Repeat(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Repeat(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the repeat parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseRepeat());
}

/// <summary>A repeat type specifier, like <c>volta</c>.</summary>
public class RepeatSpecifier : Specifier
{
    /// <summary>The matching rule, over the repeat-types word list (upstream's
    /// <c>patternproperty</c> rx).</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Lazy<RepeatSpecifier>(
            () => string.Format(
                @"\b({0})(?![A-Za-z])", string.Join("|", Words.RepeatTypes)),
            (t, p) => new RepeatSpecifier(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal RepeatSpecifier(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A repeat count.</summary>
public class RepeatCount : IntegerValue
{
    //upstream note: RepeatCount(IntegerValue, _token.Leaver) - in python's MRO the
    //_token.Item base (via Value) resolves update_state BEFORE _token.Leaver, so
    //the behaviour is endArgument, not leave; here that endArgument behaviour is
    //inherited from Value.

    /// <summary>The matching rule (inherits the <see cref="IntegerValue"/> pattern).</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<RepeatCount>(IntegerValue.Pattern, (t, p) => new RepeatCount(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal RepeatCount(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The <c>\tempo</c> command.</summary>
public class Tempo : Command
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\tempo" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Tempo>(Pattern, (t, p) => new Tempo(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Tempo(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the tempo parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseTempo());
}

/// <summary>A separator in a tempo range or approximation.</summary>
public class TempoSeparator : Delimiter
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"[-~](?=\s*\d)";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<TempoSeparator>(Pattern, (t, p) => new TempoSeparator(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal TempoSeparator(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The <c>\partial</c> command.</summary>
public class Partial : Command
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\partial" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Partial>(Pattern, (t, p) => new Partial(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Partial(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The <c>\override</c> keyword.</summary>
public class Override : Keyword
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\override" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Override>(Pattern, (t, p) => new Override(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Override(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the override parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseOverride());
}

/// <summary>The <c>\set</c> keyword.</summary>
public class Set : Override
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\set" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Set>(Pattern, (t, p) => new Set(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Set(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the set parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseSet());
}

/// <summary>The <c>\revert</c> keyword.</summary>
public class Revert : Override
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\revert" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Revert>(Pattern, (t, p) => new Revert(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Revert(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the revert parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseRevert());
}

/// <summary>The <c>\unset</c> keyword.</summary>
public class Unset : Keyword
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\unset" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Unset>(Pattern, (t, p) => new Unset(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Unset(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the unset parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseUnset());
}

/// <summary>The <c>\tweak</c> keyword.</summary>
public class Tweak : Keyword
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\tweak" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Tweak>(Pattern, (t, p) => new Tweak(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Tweak(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the tweak parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseTweak());
}

/// <summary>Base class for the commands that name a translator.</summary>
public abstract class Translator : Command
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Translator(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the translator parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseTranslator());
}

/// <summary>The <c>\new</c> command.</summary>
public class New : Translator
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\new" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<New>(Pattern, (t, p) => new New(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal New(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The <c>\context</c> command.</summary>
public class Context : Translator
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\context" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Context>(Pattern, (t, p) => new Context(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Context(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The <c>\change</c> command.</summary>
public class Change : Translator
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\change" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Change>(Pattern, (t, p) => new Change(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Change(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The <c>\accidentalStyle</c> command.</summary>
public class AccidentalStyle : Command
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\accidentalStyle" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<AccidentalStyle>(Pattern, (t, p) => new AccidentalStyle(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal AccidentalStyle(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the accidental-style parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state)
        => state.Enter(new ParseAccidentalStyle());
}

/// <summary>An accidental style name.</summary>
public class AccidentalStyleSpecifier : Specifier
{
    /// <summary>The matching rule, over the accidental-styles word list (upstream's
    /// <c>patternproperty</c> rx).</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Lazy<AccidentalStyleSpecifier>(
            () => string.Format(
                @"\b({0})(?!-?\w)", string.Join("|", Words.Accidentalstyles)),
            (t, p) => new AccidentalStyleSpecifier(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal AccidentalStyleSpecifier(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The <c>\alterBroken</c> command.</summary>
public class AlterBroken : Command
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\alterBroken" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<AlterBroken>(Pattern, (t, p) => new AlterBroken(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal AlterBroken(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the alter-broken parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseAlterBroken());
}

/// <summary>The <c>\clef</c> command.</summary>
public class Clef : Command
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\clef" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Clef>(Pattern, (t, p) => new Clef(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Clef(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the clef parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseClef());
}

/// <summary>A plain clef name.</summary>
public class ClefSpecifier : Specifier
{
    /// <summary>The matching rule, over the plain-clefs word list (upstream's
    /// <c>patternproperty</c> rx).</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Lazy<ClefSpecifier>(
            () => string.Format(@"\b({0})\b", string.Join("|", Words.ClefsPlain)),
            (t, p) => new ClefSpecifier(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal ClefSpecifier(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Leaves the current parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}

/// <summary>A pitch-related command, like <c>\relative</c> or <c>\transpose</c>.</summary>
public class PitchCommand : Command
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern
        = @"\\(relative|transpose|transposition|key|octaveCheck)" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<PitchCommand>(Pattern, (t, p) => new PitchCommand(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal PitchCommand(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the pitch-command parser with 2 arguments for
    /// <c>\transpose</c>, 1 otherwise.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state)
    {
        int argcount = Text == @"\transpose" ? 2 : 1;
        state.Enter(new ParsePitchCommand(argcount));
    }
}

/// <summary>A key signature mode command, like <c>\major</c>.</summary>
public class KeySignatureMode : Command
{
    /// <summary>The matching rule, over the modes word list (upstream's
    /// <c>patternproperty</c> rx).</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Lazy<KeySignatureMode>(
            () => string.Format(@"\\({0})(?![A-Za-z])", string.Join("|", Words.Modes)),
            (t, p) => new KeySignatureMode(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal KeySignatureMode(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The <c>\hide</c> keyword.</summary>
public class Hide : Keyword
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\hide" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Hide>(Pattern, (t, p) => new Hide(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Hide(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the hide/omit parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseHideOmit());
}

/// <summary>The <c>\omit</c> keyword.</summary>
public class Omit : Keyword
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\omit" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Omit>(Pattern, (t, p) => new Omit(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Omit(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the hide/omit parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseHideOmit());
}

/// <summary>A unit command, like <c>\mm</c> or <c>\cm</c>.</summary>
public class Unit : Command
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\(mm|cm|in|pt|bp)" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Unit>(Pattern, (t, p) => new Unit(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Unit(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for input-mode-changing commands.</summary>
public abstract class InputMode : Command
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected InputMode(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A lyric mode command, like <c>\lyricmode</c> or <c>\addlyrics</c>.</summary>
public class LyricMode : InputMode
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern
        = @"\\(lyricmode|((old)?add)?lyrics|lyricsto)" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<LyricMode>(Pattern, (t, p) => new LyricMode(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LyricMode(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the expect-lyric-mode parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ExpectLyricMode());
}

/// <summary>Base class for Lyric items.</summary>
public abstract class Lyric : Item
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Lyric(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A word of lyric text.</summary>
public class LyricText : Lyric
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"[^\\\s\d\""]+";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LyricText>(Pattern, (t, p) => new LyricText(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LyricText(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A lyric hyphen, <c>--</c>.</summary>
public class LyricHyphen : Lyric
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"--(?=($|[\s\\]))";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LyricHyphen>(Pattern, (t, p) => new LyricHyphen(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LyricHyphen(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A lyric extender, <c>__</c>.</summary>
public class LyricExtender : Lyric
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"__(?=($|[\s\\]))";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LyricExtender>(Pattern, (t, p) => new LyricExtender(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LyricExtender(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A lyric skip, <c>_</c>.</summary>
public class LyricSkip : Lyric
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"_(?=($|[\s\\]))";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LyricSkip>(Pattern, (t, p) => new LyricSkip(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LyricSkip(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for Figure items.</summary>
public abstract class Figure : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Figure(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The <c>&lt;</c> starting a figure.</summary>
public class FigureStart : Figure
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"<";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<FigureStart>(Pattern, (t, p) => new FigureStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal FigureStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the figure parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseFigure());
}

/// <summary>The <c>&gt;</c> ending a figure.</summary>
public class FigureEnd : Figure
{
    //upstream note: FigureEnd(Figure, _token.Leaver) - the leave behaviour is
    //written out below.

    /// <summary>The pattern.</summary>
    internal const string Pattern = @">";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<FigureEnd>(Pattern, (t, p) => new FigureEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal FigureEnd(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Leaves the current parser — the <c>_token.Leaver</c> behaviour.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}

/// <summary>A square bracket inside a figure.</summary>
public class FigureBracket : Figure
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"[][]";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<FigureBracket>(Pattern, (t, p) => new FigureBracket(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal FigureBracket(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A step figure number or the underscore.</summary>
public class FigureStep : Figure
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"_|\d+";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<FigureStep>(Pattern, (t, p) => new FigureStep(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal FigureStep(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A figure accidental.</summary>
public class FigureAccidental : Figure
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"[-+!]+";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<FigureAccidental>(Pattern, (t, p) => new FigureAccidental(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal FigureAccidental(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A figure modifier.</summary>
public class FigureModifier : Figure
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"\\[\\!+]|/";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<FigureModifier>(Pattern, (t, p) => new FigureModifier(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal FigureModifier(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A note mode command, <c>\notes</c> or <c>\notemode</c>.</summary>
public class NoteMode : InputMode
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\(notes|notemode)" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<NoteMode>(Pattern, (t, p) => new NoteMode(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal NoteMode(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the expect-note-mode parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ExpectNoteMode());
}

/// <summary>A chord mode command, <c>\chords</c> or <c>\chordmode</c>.</summary>
public class ChordMode : InputMode
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\(chords|chordmode)" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<ChordMode>(Pattern, (t, p) => new ChordMode(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal ChordMode(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the expect-chord-mode parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ExpectChordMode());
}

/// <summary>A drum mode command, <c>\drums</c> or <c>\drummode</c>.</summary>
public class DrumMode : InputMode
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\(drums|drummode)" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<DrumMode>(Pattern, (t, p) => new DrumMode(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal DrumMode(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the expect-drum-mode parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ExpectDrumMode());
}

/// <summary>A figure mode command, <c>\figures</c> or <c>\figuremode</c>.</summary>
public class FigureMode : InputMode
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\(figures|figuremode)" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<FigureMode>(Pattern, (t, p) => new FigureMode(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal FigureMode(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the expect-figure-mode parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ExpectFigureMode());
}

/// <summary>A reference to a user-defined command.</summary>
public class UserCommand : IdentifierRef
{
    /// <summary>The matching rule (the <see cref="IdentifierRef"/> pattern; the last
    /// class of its shared-pattern group, so no <c>test_match</c>).</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<UserCommand>(IdentifierRef.Pattern, (t, p) => new UserCommand(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal UserCommand(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The <c>\simultaneous</c> or <c>\sequential</c> keyword.</summary>
public class SimultaneousOrSequentialCommand : Keyword
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\\(simultaneous|sequential)" + Rx.ReIdentifierEnd;

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<SimultaneousOrSequentialCommand>(
            Pattern, (t, p) => new SimultaneousOrSequentialCommand(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal SimultaneousOrSequentialCommand(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The <c>#</c> or <c>$</c> starting Scheme input.</summary>
public class SchemeStart : Item
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"[#$](?![{}])";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<SchemeStart>(Pattern, (t, p) => new SchemeStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal SchemeStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the Scheme parser with one argument.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state)
        => state.Enter(new SchemeMode.ParseScheme(1));
}

/// <summary>A context name, like <c>Staff</c>.</summary>
public class ContextName : Token
{
    /// <summary>The matching rule, over the contexts word list (upstream's
    /// <c>patternproperty</c> rx).</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Lazy<ContextName>(
            () => string.Format(@"\b({0})\b", string.Join("|", Words.Contexts)),
            (t, p) => new ContextName(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal ContextName(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A backslashed context name, like <c>\Staff</c>.</summary>
public class BackSlashedContextName : ContextName
{
    /// <summary>The matching rule, over the contexts word list (upstream's
    /// <c>patternproperty</c> rx).</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Lazy<BackSlashedContextName>(
            () => string.Format(@"\\({0})\b", string.Join("|", Words.Contexts)),
            (t, p) => new BackSlashedContextName(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal BackSlashedContextName(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A grob (graphical object) name, like <c>NoteHead</c>.</summary>
public class GrobName : Token
{
    /// <summary>The matching rule, over the grobs data list (upstream's
    /// <c>patternproperty</c> rx).</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Lazy<GrobName>(
            () => string.Format(@"\b({0})\b", string.Join("|", LyData.Grobs())),
            (t, p) => new GrobName(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal GrobName(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A grob property path component.</summary>
public class GrobProperty : Variable
{
    /// <summary>The pattern.</summary>
    internal new const string Pattern = @"\b([a-z]+|[XY])(-([a-z]+|[XY]))*(?![\w])";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<GrobProperty>(Pattern, (t, p) => new GrobProperty(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal GrobProperty(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A context property name.</summary>
public class ContextProperty : Variable
{
    /// <summary>The matching rule, over the context-properties data list (upstream's
    /// <c>patternproperty</c> rx).</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Lazy<ContextProperty>(
            () => string.Format(@"\b({0})\b", string.Join("|", LyData.ContextProperties())),
            (t, p) => new ContextProperty(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal ContextProperty(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A variable inside Paper. Always follow this one by UserVariable.</summary>
public class PaperVariable : Variable
{
    /// <summary>The matching rule (the <see cref="Identifier"/> pattern with the
    /// paper-variables <c>test_match</c>).</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<PaperVariable>(
            Identifier.Pattern,
            (t, p) => new PaperVariable(t, p),
            m => Words.Contains(Words.Papervariables, m.Value));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal PaperVariable(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A variable inside Header. Always follow this one by UserVariable.</summary>
public class HeaderVariable : Variable
{
    /// <summary>The matching rule (the <see cref="Identifier"/> pattern with the
    /// header-variables <c>test_match</c>).</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<HeaderVariable>(
            Identifier.Pattern,
            (t, p) => new HeaderVariable(t, p),
            m => Words.Contains(Words.Headervariables, m.Value));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal HeaderVariable(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A variable inside Layout (upstream docstring says Header). Always
/// follow this one by UserVariable.</summary>
public class LayoutVariable : Variable
{
    /// <summary>The matching rule (the <see cref="Identifier"/> pattern with the
    /// layout-variables <c>test_match</c>).</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<LayoutVariable>(
            Identifier.Pattern,
            (t, p) => new LayoutVariable(t, p),
            m => Words.Contains(Words.Layoutvariables, m.Value));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal LayoutVariable(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Base class for Chord delimiters.</summary>
public abstract class Chord : Token
{
    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Chord(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The <c>&lt;</c> starting a chord.</summary>
public class ChordStart : Chord
{
    /// <summary>The pattern.</summary>
    internal const string Pattern = @"<";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<ChordStart>(Pattern, (t, p) => new ChordStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal ChordStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the chord parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseChord());
}

/// <summary>The <c>&gt;</c> ending a chord.</summary>
public class ChordEnd : Chord
{
    //upstream note: ChordEnd(Chord, _token.Leaver) - the leave behaviour is written
    //out below.

    /// <summary>The pattern.</summary>
    internal const string Pattern = @">";

    /// <summary>The matching rule.</summary>
    internal static readonly TokenRule Rule
        = TokenRule.Of<ChordEnd>(Pattern, (t, p) => new ChordEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal ChordEnd(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Leaves the current parser — the <c>_token.Leaver</c> behaviour.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Leave();
}

/// <summary>The <c>&lt;</c> starting a drum chord.</summary>
public class DrumChordStart : ChordStart
{
    /// <summary>The matching rule (inherits the <see cref="ChordStart"/> pattern).</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<DrumChordStart>(ChordStart.Pattern, (t, p) => new DrumChordStart(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal DrumChordStart(string text, int pos)
        : base(text, pos)
    {
    }

    /// <summary>Enters the drum-chord parser.</summary>
    /// <param name="state">The state to update.</param>
    public override void UpdateState(Slexing.State state) => state.Enter(new ParseDrumChord());
}

/// <summary>The <c>&gt;</c> ending a drum chord.</summary>
public class DrumChordEnd : ChordEnd
{
    /// <summary>The matching rule (inherits the <see cref="ChordEnd"/> pattern).</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<DrumChordEnd>(ChordEnd.Pattern, (t, p) => new DrumChordEnd(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal DrumChordEnd(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>Input that is erroneous inside a chord.</summary>
public class ErrorInChord : Error
{
    /// <summary>The pattern.</summary>
    internal const string Pattern =
        Rx.ReArticulation // articulation
        + "|" + @"<<|>>" // double french quotes
        + "|" + @"\\[\\\]\[\(\)()]" // slurs beams
        + "|" + Rx.ReDuration // duration
        + "|" + Rx.ReScaling; // scaling

    /// <summary>The matching rule.</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<ErrorInChord>(Pattern, (t, p) => new ErrorInChord(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal ErrorInChord(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>A variable name without <c>\</c> prefix.</summary>
public class Name : UserVariable
{
    /// <summary>The matching rule (inherits the <see cref="Identifier"/> pattern).</summary>
    internal static readonly new TokenRule Rule
        = TokenRule.Of<Name>(Identifier.Pattern, (t, p) => new Name(t, p));

    /// <summary>Initializes the token.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    internal Name(string text, int pos)
        : base(text, pos)
    {
    }
}

/// <summary>The equals sign.</summary>
public class EqualSign : Token
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
}
