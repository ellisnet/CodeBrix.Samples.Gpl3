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
// This file covers lilypond.py lines 893-1657: the ParseLilyPond base parser, the
// shared item tuples (space_items .. music_chord_items) and every parser class,
// ParseGlobal through ParseDecimalValue.

/// <summary>Base class for the parsers that parse LilyPond input.</summary>
public abstract class ParseLilyPond : Parser
{
    /// <summary>Initializes the parser.</summary>
    protected ParseLilyPond()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    protected ParseLilyPond(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the mode this parser begins: <c>lilypond</c>.</summary>
    public override string Mode => "lilypond";
}

/// <summary>The shared item tuples of ly/lex/lilypond.py, in upstream order. ORDER
/// IS SEMANTICS: it is the alternation precedence of the combined patterns.</summary>
internal static class ItemLists
{
    /// <summary>Joins item lists — the tuple concatenation upstream writes
    /// with <c>+</c>.</summary>
    /// <param name="lists">The lists, in order.</param>
    /// <returns>One list holding all the rules in order.</returns>
    internal static TokenRule[] Join(params TokenRule[][] lists)
    {
        int total = 0;
        foreach (TokenRule[] list in lists)
        {
            total += list.Length;
        }

        TokenRule[] joined = new TokenRule[total];
        int index = 0;
        foreach (TokenRule[] list in lists)
        {
            foreach (TokenRule rule in list)
            {
                joined[index] = rule;
                index += 1;
            }
        }

        return joined;
    }

    /// <summary>Upstream <c>space_items</c>: basic stuff that can appear everywhere.</summary>
    internal static readonly TokenRule[] SpaceItems =
    {
        Space.Rule, // _token.Space
        BlockCommentStart.Rule,
        LineComment.Rule,
    };

    /// <summary>Upstream <c>base_items</c>.</summary>
    internal static readonly TokenRule[] BaseItems = Join(
        SpaceItems,
        new[]
        {
            SchemeStart.Rule,
            StringQuotedStart.Rule,
        });

    /// <summary>Upstream <c>command_items</c>: items that represent commands in both
    /// toplevel and music mode.</summary>
    internal static readonly TokenRule[] CommandItems =
    {
        Repeat.Rule,
        PitchCommand.Rule,
        Override.Rule, Revert.Rule,
        Set.Rule, Unset.Rule,
        Hide.Rule, Omit.Rule,
        Tweak.Rule,
        New.Rule, Context.Rule, Change.Rule,
        With.Rule,
        Clef.Rule,
        Tempo.Rule,
        Partial.Rule,
        KeySignatureMode.Rule,
        AccidentalStyle.Rule,
        AlterBroken.Rule,
        SimultaneousOrSequentialCommand.Rule,
        ChordMode.Rule, DrumMode.Rule, FigureMode.Rule, LyricMode.Rule, NoteMode.Rule,
        MarkupStart.Rule, MarkupLines.Rule, MarkupList.Rule,
        ArticulationCommand.Rule,
        Keyword.Rule,
        Command.Rule,
        //upstream note: SimultaneousOrSequentialCommand appears TWICE upstream; the
        //duplicate is kept here and uniq'd by the machinery exactly as upstream does.
        SimultaneousOrSequentialCommand.Rule,
        UserCommand.Rule,
    };

    /// <summary>Upstream <c>toplevel_base_items</c>: items that occur in toplevel,
    /// book, bookpart or score (no Leave-tokens!).</summary>
    internal static readonly TokenRule[] ToplevelBaseItems = Join(
        BaseItems,
        new[]
        {
            SequentialStart.Rule,
            SimultaneousStart.Rule,
        },
        CommandItems);

    /// <summary>Upstream <c>music_items</c>: items that occur in music expressions.</summary>
    internal static readonly TokenRule[] MusicItems = Join(
        BaseItems,
        new[]
        {
            Dynamic.Rule,
            Skip.Rule,
            Spacer.Rule,
            Q.Rule,
            Rest.Rule,
            Note.Rule,
            Fraction.Rule,
            Length.Rule,
            Octave.Rule,
            OctaveCheck.Rule,
            AccidentalCautionary.Rule,
            AccidentalReminder.Rule,
            PipeSymbol.Rule,
            VoiceSeparator.Rule,
            SequentialStart.Rule, SequentialEnd.Rule,
            SimultaneousStart.Rule, SimultaneousEnd.Rule,
            ChordStart.Rule,
            ContextName.Rule,
            GrobName.Rule,
            SlurStart.Rule, SlurEnd.Rule,
            PhrasingSlurStart.Rule, PhrasingSlurEnd.Rule,
            Tie.Rule,
            BeamStart.Rule, BeamEnd.Rule,
            LigatureStart.Rule, LigatureEnd.Rule,
            Direction.Rule,
            StringNumber.Rule,
            IntegerValue.Rule,
        },
        CommandItems);

    /// <summary>Upstream <c>music_chord_items</c>: items that occur inside chords.</summary>
    internal static readonly TokenRule[] MusicChordItems = Join(
        new[]
        {
            ErrorInChord.Rule,
            ChordEnd.Rule,
        },
        MusicItems);
}

/// <summary>Parses LilyPond from the toplevel of a file.</summary>
public class ParseGlobal : ParseLilyPond
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        new[]
        {
            Book.Rule,
            BookPart.Rule,
            Score.Rule,
            MarkupStart.Rule, MarkupLines.Rule, MarkupList.Rule,
            Paper.Rule, Header.Rule, Layout.Rule,
        },
        ItemLists.ToplevelBaseItems,
        new[]
        {
            Name.Rule,
            DotPath.Rule,
            EqualSign.Rule,
            Fraction.Rule,
            DecimalValue.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ParseGlobal()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseGlobal(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Enters the global-assignment parser on an equals sign.</summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public override void UpdateState(Slexing.State state, Slexing.Token token)
    {
        if (token is EqualSign)
        {
            state.Enter(new ParseGlobalAssignment());
        }
    }
}

/// <summary>Parses the value assigned to a toplevel variable.</summary>
public class ParseGlobalAssignment : Lex.FallthroughParser
{
    //upstream note: ParseGlobalAssignment(FallthroughParser, ParseLilyPond) - the
    //ParseLilyPond base contributes mode 'lilypond', declared below.

    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            Skip.Rule,
            Spacer.Rule,
            Q.Rule,
            Rest.Rule,
            Note.Rule,
            Length.Rule,
            Fraction.Rule,
            DecimalValue.Rule,
            Direction.Rule,
            StringNumber.Rule,
            Dynamic.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ParseGlobalAssignment()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseGlobalAssignment(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the mode this parser begins: <c>lilypond</c>.</summary>
    public override string Mode => "lilypond";

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Waits for an OpenBracket and then replaces the parser with the class
/// set in the replace attribute. Subclass this to set the destination for the
/// OpenBracket.</summary>
public abstract class ExpectOpenBracket : Lex.FallthroughParser
{
    //upstream note: ExpectOpenBracket(FallthroughParser, ParseLilyPond) - the
    //ParseLilyPond base contributes mode 'lilypond', declared below.

    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            OpenBracket.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    protected ExpectOpenBracket()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    protected ExpectOpenBracket(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the mode this parser begins: <c>lilypond</c>.</summary>
    public override string Mode => "lilypond";

    /// <summary>Gets the default rule: <see cref="Error"/> — upstream's
    /// <c>default = Error</c>.</summary>
    public override TokenRule Default => Error.Rule;

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Makes the parser to replace this one with — upstream's
    /// <c>replace</c> class attribute.</summary>
    /// <returns>The replacement parser.</returns>
    protected abstract Slexing.Parser Replacement();

    /// <summary>Replaces this parser with the replacement on an open bracket.</summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public override void UpdateState(Slexing.State state, Slexing.Token token)
    {
        if (token is OpenBracket)
        {
            state.Replace(Replacement());
        }
    }
}

/// <summary>Waits for an OpenBracket or &lt;&lt; and then replaces the parser with
/// the class set in the replace attribute. Subclass this to set the destination for
/// the OpenBracket.</summary>
public abstract class ExpectMusicList : Lex.FallthroughParser
{
    //upstream note: ExpectMusicList(FallthroughParser, ParseLilyPond) - the
    //ParseLilyPond base contributes mode 'lilypond', declared below.

    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            OpenBracket.Rule,
            OpenSimultaneous.Rule,
            SimultaneousOrSequentialCommand.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    protected ExpectMusicList()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    protected ExpectMusicList(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the mode this parser begins: <c>lilypond</c>.</summary>
    public override string Mode => "lilypond";

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Makes the parser to replace this one with — upstream's
    /// <c>replace</c> class attribute.</summary>
    /// <returns>The replacement parser.</returns>
    protected abstract Slexing.Parser Replacement();

    /// <summary>Replaces this parser with the replacement on an open bracket
    /// or <c>&lt;&lt;</c>.</summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public override void UpdateState(Slexing.State state, Slexing.Token token)
    {
        if (token is OpenBracket || token is OpenSimultaneous)
        {
            state.Replace(Replacement());
        }
    }
}

/// <summary>Parses the expression after <c>\score {</c>, leaving at <c>}</c>.</summary>
public class ParseScore : ParseLilyPond
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        new[]
        {
            CloseBracket.Rule,
            Header.Rule, Layout.Rule, Midi.Rule, With.Rule,
        },
        ItemLists.ToplevelBaseItems);

    /// <summary>Initializes the parser.</summary>
    public ParseScore()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseScore(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Expects the open bracket of a <c>\score</c> expression.</summary>
public class ExpectScore : ExpectOpenBracket
{
    /// <summary>Initializes the parser.</summary>
    public ExpectScore()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ExpectScore(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Makes the replacement parser: <see cref="ParseScore"/>.</summary>
    /// <returns>The replacement parser.</returns>
    protected override Slexing.Parser Replacement() => new ParseScore();
}

/// <summary>Parses the expression after <c>\book {</c>, leaving at <c>}</c>.</summary>
public class ParseBook : ParseLilyPond
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        new[]
        {
            CloseBracket.Rule,
            MarkupStart.Rule, MarkupLines.Rule, MarkupList.Rule,
            BookPart.Rule,
            Score.Rule,
            Paper.Rule, Header.Rule, Layout.Rule,
        },
        ItemLists.ToplevelBaseItems);

    /// <summary>Initializes the parser.</summary>
    public ParseBook()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseBook(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Expects the open bracket of a <c>\book</c> expression.</summary>
public class ExpectBook : ExpectOpenBracket
{
    /// <summary>Initializes the parser.</summary>
    public ExpectBook()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ExpectBook(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Makes the replacement parser: <see cref="ParseBook"/>.</summary>
    /// <returns>The replacement parser.</returns>
    protected override Slexing.Parser Replacement() => new ParseBook();
}

/// <summary>Parses the expression after <c>\bookpart {</c>, leaving at <c>}</c>.</summary>
public class ParseBookPart : ParseLilyPond
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        new[]
        {
            CloseBracket.Rule,
            MarkupStart.Rule, MarkupLines.Rule, MarkupList.Rule,
            Score.Rule,
            Paper.Rule, Header.Rule, Layout.Rule,
        },
        ItemLists.ToplevelBaseItems);

    /// <summary>Initializes the parser.</summary>
    public ParseBookPart()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseBookPart(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Expects the open bracket of a <c>\bookpart</c> expression.</summary>
public class ExpectBookPart : ExpectOpenBracket
{
    /// <summary>Initializes the parser.</summary>
    public ExpectBookPart()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ExpectBookPart(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Makes the replacement parser: <see cref="ParseBookPart"/>.</summary>
    /// <returns>The replacement parser.</returns>
    protected override Slexing.Parser Replacement() => new ParseBookPart();
}

/// <summary>Parses the expression after <c>\paper {</c>, leaving at <c>}</c>.</summary>
public class ParsePaper : ParseLilyPond
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.BaseItems,
        new[]
        {
            CloseBracket.Rule,
            MarkupStart.Rule, MarkupLines.Rule, MarkupList.Rule,
            PaperVariable.Rule,
            UserVariable.Rule,
            EqualSign.Rule,
            DotPath.Rule,
            DecimalValue.Rule,
            Unit.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ParsePaper()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParsePaper(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Expects the open bracket of a <c>\paper</c> expression.</summary>
public class ExpectPaper : ExpectOpenBracket
{
    /// <summary>Initializes the parser.</summary>
    public ExpectPaper()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ExpectPaper(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Makes the replacement parser: <see cref="ParsePaper"/>.</summary>
    /// <returns>The replacement parser.</returns>
    protected override Slexing.Parser Replacement() => new ParsePaper();
}

/// <summary>Parses the expression after <c>\header {</c>, leaving at <c>}</c>.</summary>
public class ParseHeader : ParseLilyPond
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        new[]
        {
            CloseBracket.Rule,
            MarkupStart.Rule, MarkupLines.Rule, MarkupList.Rule,
            HeaderVariable.Rule,
            UserVariable.Rule,
            EqualSign.Rule,
            DotPath.Rule,
        },
        ItemLists.ToplevelBaseItems);

    /// <summary>Initializes the parser.</summary>
    public ParseHeader()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseHeader(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Expects the open bracket of a <c>\header</c> expression.</summary>
public class ExpectHeader : ExpectOpenBracket
{
    /// <summary>Initializes the parser.</summary>
    public ExpectHeader()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ExpectHeader(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Makes the replacement parser: <see cref="ParseHeader"/>.</summary>
    /// <returns>The replacement parser.</returns>
    protected override Slexing.Parser Replacement() => new ParseHeader();
}

/// <summary>Parses the expression after <c>\layout {</c>, leaving at <c>}</c>.</summary>
public class ParseLayout : ParseLilyPond
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.BaseItems,
        new[]
        {
            CloseBracket.Rule,
            LayoutContext.Rule,
            LayoutVariable.Rule,
            UserVariable.Rule,
            EqualSign.Rule,
            DotPath.Rule,
            DecimalValue.Rule,
            Unit.Rule,
            ContextName.Rule,
            GrobName.Rule,
        },
        ItemLists.CommandItems);

    /// <summary>Initializes the parser.</summary>
    public ParseLayout()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseLayout(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Expects the open bracket of a <c>\layout</c> expression.</summary>
public class ExpectLayout : ExpectOpenBracket
{
    /// <summary>Initializes the parser.</summary>
    public ExpectLayout()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ExpectLayout(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Makes the replacement parser: <see cref="ParseLayout"/>.</summary>
    /// <returns>The replacement parser.</returns>
    protected override Slexing.Parser Replacement() => new ParseLayout();
}

/// <summary>Parses the expression after <c>\midi {</c>, leaving at <c>}</c>.</summary>
public class ParseMidi : ParseLilyPond
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.BaseItems,
        new[]
        {
            CloseBracket.Rule,
            LayoutContext.Rule,
            LayoutVariable.Rule,
            UserVariable.Rule,
            EqualSign.Rule,
            DotPath.Rule,
            DecimalValue.Rule,
            Unit.Rule,
            ContextName.Rule,
            GrobName.Rule,
        },
        ItemLists.CommandItems);

    /// <summary>Initializes the parser.</summary>
    public ParseMidi()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseMidi(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Expects the open bracket of a <c>\midi</c> expression.</summary>
public class ExpectMidi : ExpectOpenBracket
{
    /// <summary>Initializes the parser.</summary>
    public ExpectMidi()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ExpectMidi(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Makes the replacement parser: <see cref="ParseMidi"/>.</summary>
    /// <returns>The replacement parser.</returns>
    protected override Slexing.Parser Replacement() => new ParseMidi();
}

/// <summary>Parses the expression after <c>\with {</c>, leaving at <c>}</c>.</summary>
public class ParseWith : ParseLilyPond
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        new[]
        {
            CloseBracket.Rule,
            ContextName.Rule,
            GrobName.Rule,
            ContextProperty.Rule,
            EqualSign.Rule,
            DotPath.Rule,
        },
        ItemLists.ToplevelBaseItems);

    /// <summary>Initializes the parser.</summary>
    public ParseWith()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseWith(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Expects the open bracket of a <c>\with</c> expression.</summary>
public class ExpectWith : ExpectOpenBracket
{
    /// <summary>Initializes the parser.</summary>
    public ExpectWith()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ExpectWith(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Makes the replacement parser: <see cref="ParseWith"/>.</summary>
    /// <returns>The replacement parser.</returns>
    protected override Slexing.Parser Replacement() => new ParseWith();
}

/// <summary>Parses the expression after (<c>\layout {</c>) <c>\context {</c>,
/// leaving at <c>}</c>.</summary>
public class ParseContext : ParseLilyPond
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        new[]
        {
            CloseBracket.Rule,
            BackSlashedContextName.Rule,
            ContextProperty.Rule,
            EqualSign.Rule,
            DotPath.Rule,
        },
        ItemLists.ToplevelBaseItems);

    /// <summary>Initializes the parser.</summary>
    public ParseContext()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseContext(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Expects the open bracket of a <c>\context</c> expression.</summary>
public class ExpectContext : ExpectOpenBracket
{
    /// <summary>Initializes the parser.</summary>
    public ExpectContext()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ExpectContext(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Makes the replacement parser: <see cref="ParseContext"/>.</summary>
    /// <returns>The replacement parser.</returns>
    protected override Slexing.Parser Replacement() => new ParseContext();
}

/// <summary>Parses LilyPond music expressions.</summary>
public class ParseMusic : ParseLilyPond
{
    /// <summary>The items — internal so the Scheme mode can prepend to them, as
    /// upstream's <c>lilypond.ParseMusic.items</c> reference does.</summary>
    internal static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.MusicItems,
        new[]
        {
            TremoloColon.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ParseMusic()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseMusic(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>LilyPond inside chords <c>&lt; &gt;</c>.</summary>
public class ParseChord : ParseMusic
{
    private static readonly TokenRule[] ChordParserItems = ItemLists.MusicChordItems;

    /// <summary>Initializes the parser.</summary>
    public ParseChord()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseChord(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ChordParserItems;
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

/// <summary>Parses markup text.</summary>
public class ParseMarkup : Parser
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        new[]
        {
            MarkupScore.Rule,
            MarkupCommand.Rule,
            MarkupUserCommand.Rule,
            OpenBracketMarkup.Rule,
            CloseBracketMarkup.Rule,
            MarkupWord.Rule,
        },
        ItemLists.BaseItems);

    /// <summary>Initializes the parser.</summary>
    public ParseMarkup()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseMarkup(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Parses the arguments of <c>\repeat</c>.</summary>
public class ParseRepeat : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            RepeatSpecifier.Rule,
            StringQuotedStart.Rule,
            RepeatCount.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ParseRepeat()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseRepeat(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Parses the arguments of <c>\tempo</c>.</summary>
public class ParseTempo : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            MarkupStart.Rule,
            StringQuotedStart.Rule,
            SchemeStart.Rule,
            Length.Rule,
            EqualSign.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ParseTempo()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseTempo(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Replaces this parser after an equals sign.</summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public override void UpdateState(Slexing.State state, Slexing.Token token)
    {
        if (token is EqualSign)
        {
            state.Replace(new ParseTempoAfterEqualSign());
        }
    }
}

/// <summary>Parses the metronome value after <c>=</c> in <c>\tempo</c>.</summary>
public class ParseTempoAfterEqualSign : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            IntegerValue.Rule,
            TempoSeparator.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ParseTempoAfterEqualSign()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseTempoAfterEqualSign(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Parses the dots of a duration.</summary>
public class ParseDuration : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            Dot.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ParseDuration()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseDuration(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Replaces this parser with the duration-scaling parser.</summary>
    /// <param name="state">The state to alter.</param>
    /// <returns><see langword="false"/> — parsing continues.</returns>
    public override bool Fallthrough(Slexing.State state)
    {
        state.Replace(new ParseDurationScaling());
        return false;
    }
}

/// <summary>Parses the scaling of a duration.</summary>
public class ParseDurationScaling : ParseDuration
{
    private static readonly TokenRule[] ScalingParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            Scaling.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ParseDurationScaling()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseDurationScaling(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ScalingParserItems;

    /// <summary>Leaves the current parser.</summary>
    /// <param name="state">The state to alter.</param>
    /// <returns><see langword="false"/> — parsing continues.</returns>
    public override bool Fallthrough(Slexing.State state)
    {
        state.Leave();
        return false;
    }
}

/// <summary>Parses the arguments of <c>\override</c>.</summary>
public class ParseOverride : ParseLilyPond
{
    //upstream note: argcount = 0 (the class default) is declared explicitly upstream.

    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        new[]
        {
            ContextName.Rule,
            DotPath.Rule,
            GrobName.Rule,
            GrobProperty.Rule,
            EqualSign.Rule,
        },
        ItemLists.BaseItems);

    /// <summary>Initializes the parser.</summary>
    public ParseOverride()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseOverride(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Replaces this parser with the decimal-value parser after an equals
    /// sign.</summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public override void UpdateState(Slexing.State state, Slexing.Token token)
    {
        if (token is EqualSign)
        {
            state.Replace(new ParseDecimalValue());
        }
    }
}

/// <summary>Parse the arguments of <c>\revert</c>.</summary>
public class ParseRevert : Lex.FallthroughParser
{
    // allow both the old scheme syntax but also the dotted 2.18+ syntax
    // allow either a dot between the GrobName and the property path or not
    // correctly fall through when one property path has been parsed
    // (uses ParseGrobPropertyPath and ExpectGrobProperty)
    // (When the old scheme syntax is used this parser also falls through,
    // assuming that the previous parser will handle it)

    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            ContextName.Rule,
            DotPath.Rule,
            GrobName.Rule,
            GrobProperty.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ParseRevert()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseRevert(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Replaces this parser with the grob-property-path parser after a
    /// grob property.</summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public override void UpdateState(Slexing.State state, Slexing.Token token)
    {
        if (token is GrobProperty)
        {
            state.Replace(new ParseGrobPropertyPath());
        }
    }
}

/// <summary>Parses a dotted grob property path.</summary>
public class ParseGrobPropertyPath : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            DotPath.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ParseGrobPropertyPath()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseGrobPropertyPath(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Enters the expect-grob-property parser after a dot.</summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public override void UpdateState(Slexing.State state, Slexing.Token token)
    {
        if (token is DotPath)
        {
            state.Enter(new ExpectGrobProperty());
        }
    }
}

/// <summary>Expects one grob property after a dot.</summary>
public class ExpectGrobProperty : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            GrobProperty.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ExpectGrobProperty()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ExpectGrobProperty(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Leaves this parser after a grob property.</summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public override void UpdateState(Slexing.State state, Slexing.Token token)
    {
        if (token is GrobProperty)
        {
            state.Leave();
        }
    }
}

/// <summary>Parses the arguments of <c>\set</c>.</summary>
public class ParseSet : ParseLilyPond
{
    //upstream note: argcount = 0 (the class default) is declared explicitly upstream.

    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        new[]
        {
            ContextName.Rule,
            DotPath.Rule,
            ContextProperty.Rule,
            EqualSign.Rule,
            Name.Rule,
        },
        ItemLists.BaseItems);

    /// <summary>Initializes the parser.</summary>
    public ParseSet()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseSet(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Replaces this parser with the decimal-value parser after an equals
    /// sign.</summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public override void UpdateState(Slexing.State state, Slexing.Token token)
    {
        if (token is EqualSign)
        {
            state.Replace(new ParseDecimalValue());
        }
    }
}

/// <summary>Parses the arguments of <c>\unset</c>.</summary>
public class ParseUnset : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            ContextName.Rule,
            DotPath.Rule,
            ContextProperty.Rule,
            Name.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ParseUnset()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseUnset(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Leaves this parser after a context property or a lowercase-starting
    /// token — upstream's <c>token[:1].islower()</c>.</summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public override void UpdateState(Slexing.State state, Slexing.Token token)
    {
        if (token is ContextProperty
            || (token.Text.Length > 0 && char.IsLower(token.Text[0])))
        {
            state.Leave();
        }
    }
}

/// <summary>Parses the arguments of <c>\tweak</c>.</summary>
public class ParseTweak : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            GrobName.Rule,
            DotPath.Rule,
            GrobProperty.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ParseTweak()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseTweak(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Replaces this parser with the tweak-grob-property parser after a
    /// grob property.</summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public override void UpdateState(Slexing.State state, Slexing.Token token)
    {
        if (token is GrobProperty)
        {
            state.Replace(new ParseTweakGrobProperty());
        }
    }
}

/// <summary>Parses the property path and value of <c>\tweak</c>.</summary>
public class ParseTweakGrobProperty : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            DotPath.Rule,
            DecimalValue.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ParseTweakGrobProperty()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseTweakGrobProperty(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Enters the expect-grob-property parser after a dot, or leaves after
    /// a decimal value.</summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public override void UpdateState(Slexing.State state, Slexing.Token token)
    {
        if (token is DotPath)
        {
            state.Enter(new ExpectGrobProperty());
        }
        else if (token is DecimalValue)
        {
            state.Leave();
        }
    }
}

/// <summary>Parses the context name of <c>\new</c>, <c>\context</c> or
/// <c>\change</c>.</summary>
public class ParseTranslator : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            ContextName.Rule,
            Name.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ParseTranslator()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseTranslator(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Replaces this parser with the expect-translator-id parser after a
    /// name or context name.</summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public override void UpdateState(Slexing.State state, Slexing.Token token)
    {
        if (token is Name || token is ContextName)
        {
            state.Replace(new ExpectTranslatorId());
        }
    }
}

/// <summary>Expects the equals sign before a translator id.</summary>
public class ExpectTranslatorId : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            EqualSign.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ExpectTranslatorId()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ExpectTranslatorId(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Replaces this parser with the translator-id parser on <c>=</c>.</summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public override void UpdateState(Slexing.State state, Slexing.Token token)
    {
        if (token.Text == "=")
        {
            state.Replace(new ParseTranslatorId());
        }
    }
}

/// <summary>Parses the id after <c>\new Context =</c>.</summary>
public class ParseTranslatorId : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            Name.Rule,
            StringQuotedStart.Rule,
        });

    /// <summary>Initializes the parser with the upstream class default
    /// <c>argcount = 1</c>.</summary>
    public ParseTranslatorId()
    {
        Argcount = 1;
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseTranslatorId(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Leaves this parser after a name.</summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public override void UpdateState(Slexing.State state, Slexing.Token token)
    {
        if (token is Name)
        {
            state.Leave();
        }
    }
}

/// <summary>Parses the argument of <c>\clef</c>.</summary>
public class ParseClef : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            ClefSpecifier.Rule,
            StringQuotedStart.Rule,
        });

    /// <summary>Initializes the parser with the upstream class default
    /// <c>argcount = 1</c>.</summary>
    public ParseClef()
    {
        Argcount = 1;
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseClef(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Parses the arguments of <c>\hide</c> and <c>\omit</c>.</summary>
public class ParseHideOmit : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            ContextName.Rule,
            DotPath.Rule,
            GrobName.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ParseHideOmit()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseHideOmit(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Leaves this parser after a grob name.</summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public override void UpdateState(Slexing.State state, Slexing.Token token)
    {
        if (token is GrobName)
        {
            state.Leave();
        }
    }
}

/// <summary>Parses the argument of <c>\accidentalStyle</c>.</summary>
public class ParseAccidentalStyle : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            ContextName.Rule,
            DotPath.Rule,
            AccidentalStyleSpecifier.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ParseAccidentalStyle()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseAccidentalStyle(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Leaves this parser after an accidental style name.</summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public override void UpdateState(Slexing.State state, Slexing.Token token)
    {
        if (token is AccidentalStyleSpecifier)
        {
            state.Leave();
        }
    }
}

/// <summary>Parses the arguments of <c>\alterBroken</c>.</summary>
public class ParseAlterBroken : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            GrobProperty.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ParseAlterBroken()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseAlterBroken(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Replaces this parser with the grob-property-path parser after a
    /// grob property.</summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public override void UpdateState(Slexing.State state, Slexing.Token token)
    {
        if (token is GrobProperty)
        {
            state.Replace(new ParseGrobPropertyPath());
        }
    }
}

/// <summary>Parses one script abbreviation or fingering after a direction.</summary>
public class ParseScriptAbbreviationOrFingering : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            ScriptAbbreviation.Rule,
            Fingering.Rule,
        });

    /// <summary>Initializes the parser with the upstream class default
    /// <c>argcount = 1</c>.</summary>
    public ParseScriptAbbreviationOrFingering()
    {
        Argcount = 1;
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseScriptAbbreviationOrFingering(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Base class for parser for mode-changing music commands.</summary>
public abstract class ParseInputMode : ParseLilyPond
{
    /// <summary>Initializes the parser.</summary>
    protected ParseInputMode()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    protected ParseInputMode(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Enters a new parser of the same (runtime) class on an open bracket
    /// or <c>&lt;&lt;</c> — upstream's classmethod entering <c>cls()</c>.</summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public override void UpdateState(Slexing.State state, Slexing.Token token)
    {
        if (token is OpenSimultaneous || token is OpenBracket)
        {
            state.Enter((Slexing.Parser)System.Activator.CreateInstance(GetType()));
        }
    }
}

/// <summary>Parser for <c>\lyrics</c>, <c>\lyricmode</c>, <c>\addlyrics</c>, etc.</summary>
public class ParseLyricMode : ParseInputMode
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.BaseItems,
        new[]
        {
            CloseBracket.Rule,
            CloseSimultaneous.Rule,
            OpenBracket.Rule,
            OpenSimultaneous.Rule,
            PipeSymbol.Rule,
            LyricHyphen.Rule,
            LyricExtender.Rule,
            LyricSkip.Rule,
            LyricText.Rule,
            Dynamic.Rule,
            Skip.Rule,
            Length.Rule,
            MarkupStart.Rule, MarkupLines.Rule, MarkupList.Rule,
        },
        ItemLists.CommandItems);

    /// <summary>Initializes the parser.</summary>
    public ParseLyricMode()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseLyricMode(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Expects the music list of a lyric mode expression.</summary>
public class ExpectLyricMode : ExpectMusicList
{
    private static readonly TokenRule[] LyricParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            OpenBracket.Rule,
            OpenSimultaneous.Rule,
            SchemeStart.Rule,
            StringQuotedStart.Rule,
            Name.Rule,
            SimultaneousOrSequentialCommand.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ExpectLyricMode()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ExpectLyricMode(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => LyricParserItems;

    /// <summary>Makes the replacement parser: <see cref="ParseLyricMode"/>.</summary>
    /// <returns>The replacement parser.</returns>
    protected override Slexing.Parser Replacement() => new ParseLyricMode();
}

/// <summary>Parser for <c>\chords</c> and <c>\chordmode</c>.</summary>
public class ParseChordMode : ParseInputMode
{
    //upstream note: ParseChordMode(ParseInputMode, ParseMusic) - derives the first
    //base; the ParseMusic relationship is not carried in C#.

    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        new[]
        {
            OpenBracket.Rule,
            OpenSimultaneous.Rule,
        },
        ItemLists.MusicItems,
        new[] // TODO(upstream): specify items exactly, e.g. < > is not allowed
        {
            ChordSeparator.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ParseChordMode()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseChordMode(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Enters the chord-items parser on a chord separator; otherwise the
    /// input-mode behaviour applies.</summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public override void UpdateState(Slexing.State state, Slexing.Token token)
    {
        if (token is ChordSeparator)
        {
            state.Enter(new ParseChordItems());
        }
        else
        {
            base.UpdateState(state, token);
        }
    }
}

/// <summary>Expects the music list of a chord mode expression.</summary>
public class ExpectChordMode : ExpectMusicList
{
    /// <summary>Initializes the parser.</summary>
    public ExpectChordMode()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ExpectChordMode(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Makes the replacement parser: <see cref="ParseChordMode"/>.</summary>
    /// <returns>The replacement parser.</returns>
    protected override Slexing.Parser Replacement() => new ParseChordMode();
}

/// <summary>Parser for <c>\notes</c> and <c>\notemode</c>. Same as Music itself.</summary>
public class ParseNoteMode : ParseMusic
{
    /// <summary>Initializes the parser.</summary>
    public ParseNoteMode()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseNoteMode(int argcount)
        : base(argcount)
    {
    }
}

/// <summary>Expects the music list of a note mode expression.</summary>
public class ExpectNoteMode : ExpectMusicList
{
    /// <summary>Initializes the parser.</summary>
    public ExpectNoteMode()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ExpectNoteMode(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Makes the replacement parser: <see cref="ParseNoteMode"/>.</summary>
    /// <returns>The replacement parser.</returns>
    protected override Slexing.Parser Replacement() => new ParseNoteMode();
}

/// <summary>LilyPond inside chords in drummode <c>&lt; &gt;</c>.</summary>
public class ParseDrumChord : ParseMusic
{
    private static readonly TokenRule[] DrumChordParserItems = ItemLists.Join(
        ItemLists.BaseItems,
        new[]
        {
            ErrorInChord.Rule,
            DrumChordEnd.Rule,
            Dynamic.Rule,
            Skip.Rule,
            Spacer.Rule,
            Q.Rule,
            Rest.Rule,
            DrumNote.Rule,
            Fraction.Rule,
            Length.Rule,
            PipeSymbol.Rule,
            VoiceSeparator.Rule,
            SequentialStart.Rule, SequentialEnd.Rule,
            SimultaneousStart.Rule, SimultaneousEnd.Rule,
            ChordStart.Rule,
            ContextName.Rule,
            GrobName.Rule,
            SlurStart.Rule, SlurEnd.Rule,
            PhrasingSlurStart.Rule, PhrasingSlurEnd.Rule,
            Tie.Rule,
            BeamStart.Rule, BeamEnd.Rule,
            LigatureStart.Rule, LigatureEnd.Rule,
            Direction.Rule,
            StringNumber.Rule,
            IntegerValue.Rule,
        },
        ItemLists.CommandItems);

    /// <summary>Initializes the parser.</summary>
    public ParseDrumChord()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseDrumChord(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => DrumChordParserItems;
}

/// <summary>Parser for <c>\drums</c> and <c>\drummode</c>.</summary>
public class ParseDrumMode : ParseInputMode
{
    //upstream note: ParseDrumMode(ParseInputMode, ParseMusic) - derives the first
    //base; the ParseMusic relationship is not carried in C#.

    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        new[]
        {
            OpenBracket.Rule,
            OpenSimultaneous.Rule,
        },
        ItemLists.BaseItems,
        new[]
        {
            Dynamic.Rule,
            Skip.Rule,
            Spacer.Rule,
            Q.Rule,
            Rest.Rule,
            DrumNote.Rule,
            Fraction.Rule,
            Length.Rule,
            PipeSymbol.Rule,
            VoiceSeparator.Rule,
            SequentialStart.Rule, SequentialEnd.Rule,
            SimultaneousStart.Rule, SimultaneousEnd.Rule,
            DrumChordStart.Rule,
            ContextName.Rule,
            GrobName.Rule,
            SlurStart.Rule, SlurEnd.Rule,
            PhrasingSlurStart.Rule, PhrasingSlurEnd.Rule,
            Tie.Rule,
            BeamStart.Rule, BeamEnd.Rule,
            LigatureStart.Rule, LigatureEnd.Rule,
            Direction.Rule,
            StringNumber.Rule,
            IntegerValue.Rule,
        },
        ItemLists.CommandItems);

    /// <summary>Initializes the parser.</summary>
    public ParseDrumMode()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseDrumMode(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Expects the music list of a drum mode expression.</summary>
public class ExpectDrumMode : ExpectMusicList
{
    /// <summary>Initializes the parser.</summary>
    public ExpectDrumMode()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ExpectDrumMode(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Makes the replacement parser: <see cref="ParseDrumMode"/>.</summary>
    /// <returns>The replacement parser.</returns>
    protected override Slexing.Parser Replacement() => new ParseDrumMode();
}

/// <summary>Parser for <c>\figures</c> and <c>\figuremode</c>.</summary>
public class ParseFigureMode : ParseInputMode
{
    //upstream note: ParseFigureMode(ParseInputMode, ParseMusic) - derives the first
    //base; the ParseMusic relationship is not carried in C#.

    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.BaseItems,
        new[]
        {
            CloseBracket.Rule,
            CloseSimultaneous.Rule,
            OpenBracket.Rule,
            OpenSimultaneous.Rule,
            PipeSymbol.Rule,
            FigureStart.Rule,
            Skip.Rule, Spacer.Rule, Rest.Rule,
            Length.Rule,
        },
        ItemLists.CommandItems);

    /// <summary>Initializes the parser.</summary>
    public ParseFigureMode()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseFigureMode(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Parse inside <c>&lt; &gt;</c> in figure mode.</summary>
public class ParseFigure : Parser
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.BaseItems,
        new[]
        {
            FigureEnd.Rule,
            FigureBracket.Rule,
            FigureStep.Rule,
            FigureAccidental.Rule,
            FigureModifier.Rule,
            MarkupStart.Rule, MarkupLines.Rule, MarkupList.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ParseFigure()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseFigure(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Expects the music list of a figure mode expression.</summary>
public class ExpectFigureMode : ExpectMusicList
{
    /// <summary>Initializes the parser.</summary>
    public ExpectFigureMode()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ExpectFigureMode(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Makes the replacement parser: <see cref="ParseFigureMode"/>.</summary>
    /// <returns>The replacement parser.</returns>
    protected override Slexing.Parser Replacement() => new ParseFigureMode();
}

/// <summary>Parses the pitch arguments of a pitch command.</summary>
public class ParsePitchCommand : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            Note.Rule,
            Octave.Rule,
        });

    /// <summary>Initializes the parser with the upstream class default
    /// <c>argcount = 1</c>.</summary>
    public ParsePitchCommand()
    {
        Argcount = 1;
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParsePitchCommand(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;

    /// <summary>Counts down on notes; leaves on whitespace once the count is
    /// used up.</summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public override void UpdateState(Slexing.State state, Slexing.Token token)
    {
        if (token is Note)
        {
            Argcount -= 1;
        }
        else if (token is Space && Argcount <= 0) // _token.Space
        {
            state.Leave();
        }
    }
}

/// <summary>Parses a tremolo duration after the colon.</summary>
public class ParseTremolo : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems =
    {
        TremoloDuration.Rule,
    };

    /// <summary>Initializes the parser.</summary>
    public ParseTremolo()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseTremolo(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Parses chordmode items after a chord separator.</summary>
public class ParseChordItems : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems =
    {
        ChordSeparator.Rule,
        ChordModifier.Rule,
        ChordStepNumber.Rule,
        DotChord.Rule,
        Note.Rule,
    };

    /// <summary>Initializes the parser.</summary>
    public ParseChordItems()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseChordItems(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}

/// <summary>Parses a decimal value without a # before it (if present).</summary>
public class ParseDecimalValue : Lex.FallthroughParser
{
    private static readonly TokenRule[] ParserItems = ItemLists.Join(
        ItemLists.SpaceItems,
        new[]
        {
            Fraction.Rule,
            DecimalValue.Rule,
        });

    /// <summary>Initializes the parser.</summary>
    public ParseDecimalValue()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseDecimalValue(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}
