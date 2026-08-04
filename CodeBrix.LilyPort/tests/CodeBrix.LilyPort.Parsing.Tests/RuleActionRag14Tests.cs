// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyPort.Parsing.Actions;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyPort.Parsing.Lalr;
using CodeBrix.LilyPort.Parsing.Lexing;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// RULE ACTION GROUP 14 — chords and event chords, both of the things called "chord":
/// the written note chord <c>&lt;c e g&gt;</c> and the named chord <c>c:maj7</c>.
/// </summary>
public class RuleActionRag14Tests
{
    private static readonly ParseTables Tables = LalrGenerator.GenerateFromMirror();

    private static readonly IReadOnlyDictionary<int, RuleAction> Bound
        = LilyPondRuleActions.Create().Bind(Tables);

    private static RuleAction Action(string identity)
    {
        foreach (TableRule rule in Tables.Rules)
        {
            if (rule.Source != null
                && string.Equals(rule.Source.Identity, identity, StringComparison.Ordinal))
            {
                return Bound[rule.Index];
            }
        }

        throw new InvalidOperationException("no rule named " + identity);
    }

    private static ParseContext NewContext(object host)
        => new ParseContext(
            new LalrParser(Tables, new Dictionary<int, RuleAction>()),
            new TokenListInput())
        {
            UserState = host,
        };

    private static void NoErrors(LalrParser parser)
        => string.Join("; ", parser.Diagnostics).Should().BeEmpty();

    private static SourceSpan[] Spans(int count)
    {
        SourceSpan[] spans = new SourceSpan[count];
        for (int i = 0; i < count; i++)
        {
            spans[i] = new SourceSpan("<test>", 1, i + 1, 1, i + 2);
        }

        return spans;
    }

    /// <summary>Sets a run up over music, wrapped in <c>\notemode { }</c>.</summary>
    /// <param name="music">The music, without its braces.</param>
    /// <returns>The parser, the scanner and the host.</returns>
    private static (LalrParser Parser, ModalScanner Scanner, ScriptedParserHost Host) Setup(
        string music)
    {
        ScriptedParserHost host = new ScriptedParserHost
        {
            IsNoteState = true,
            MakeRealMusic = true,
        };

        host.Keywords["notemode"] = ("NOTEMODE", null);
        host.Globals.Bindings[Symbol.Intern("toplevel-music-handler")] = "music-proc";
        host.WordScans[Symbol.Intern("c")] = ("NOTENAME_PITCH", new Pitch(0, 0, Rational.Zero));
        host.WordScans[Symbol.Intern("e")] = ("NOTENAME_PITCH", new Pitch(0, 2, Rational.Zero));
        host.WordScans[Symbol.Intern("g")] = ("NOTENAME_PITCH", new Pitch(0, 4, Rational.Zero));

        ModalScanner scanner = new ModalScanner(
            LilyPondLexerRules.Create(host), "\\notemode { " + music + " }", "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);
        host.Scanner = scanner;

        return (new LalrParser(Tables, Bound), scanner, host);
    }

    // ------ event_chord ------

    [Fact]
    public void post_events_on_a_simple_element_become_its_articulations()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object note = host.MakeMusic("NoteEvent", Spans(1)[0]);
        object events = new Pair("second", new Pair("first", Nil.Instance));

        //Act
        object result = Action("event_chord: simple_element post_events %prec ':'")(
            context, new object[] { note, events }, Spans(2), Spans(1)[0]);

        //Assert
        result.Should().BeSameAs(note);

        object articulations = ((MadeMusic)note).Properties.Find(p => p.Name == "articulations").Value;
        Pair.ToList(articulations).Should().Equal("first", "second");
    }

    [Fact]
    public void a_simple_element_with_no_post_events_is_left_completely_alone()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object note = host.MakeMusic("NoteEvent", Spans(1)[0]);

        //Act
        object result = Action("event_chord: simple_element post_events %prec ':'")(
            context, new object[] { note, Nil.Instance }, Spans(2), Spans(1)[0]);

        //Assert
        result.Should().BeSameAs(note);
        ((MadeMusic)note).Properties.Should().BeEmpty();
    }

    [Fact]
    public void chord_repetition_and_multi_measure_rests_go_to_their_own_constructors()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        Duration quarter = new Duration(2, 0);

        //Act
        object repetition = Action(
            "event_chord: CHORD_REPETITION optional_notemode_duration post_events %prec ':'")(
            context,
            new object[] { null, quarter, new Pair("art", Nil.Instance) },
            Spans(3),
            Spans(1)[0]);

        object rest = Action(
            "event_chord: MULTI_MEASURE_REST optional_notemode_duration post_events %prec ':'")(
            context, new object[] { null, quarter, Nil.Instance }, Spans(3), Spans(1)[0]);

        //Assert
        SyntaxMark repeated = repetition.Should().BeOfType<SyntaxMark>().Which;
        repeated.Name.Should().Be("repetition-chord");
        repeated.Arguments[0].Should().Be(quarter);
        Pair.ToList(repeated.Arguments[1]).Should().Equal("art");

        SyntaxMark measures = rest.Should().BeOfType<SyntaxMark>().Which;
        measures.Name.Should().Be("multi-measure-rest");
        measures.Arguments[1].Should().Be(Nil.Instance);
    }

    // ------ note_chord_element: where a chord's duration comes from ------

    [Fact]
    public void the_chords_duration_reaches_every_element_and_the_post_events_join_them()
    {
        //Arrange
        // The duration is written ONCE, after the closing bracket, and applied to
        // elements that were parsed without one.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        object first = host.MakeMusic("NoteEvent", Spans(1)[0]);
        object second = host.MakeMusic("NoteEvent", Spans(1)[0]);
        object chord = host.MakeMusic("EventChord", Spans(1)[0]);
        host.SetMusicProperty(chord, "elements", new Pair(first, new Pair(second, Nil.Instance)));

        Duration quarter = new Duration(2, 0);
        SourceSpan whole = new SourceSpan("<test>", 1, 1, 1, 9);

        //Act
        object result = Action(
            "note_chord_element: chord_body optional_notemode_duration post_events %prec ':'")(
            context,
            new object[] { chord, quarter, new Pair("art", Nil.Instance) },
            Spans(3),
            whole);

        //Assert
        result.Should().BeSameAs(chord);
        ((MadeMusic)first).Properties.Should().Contain(("duration", (object)quarter));
        ((MadeMusic)second).Properties.Should().Contain(("duration", (object)quarter));

        // The post events join the SAME elements list, which is what lets `<c e>4->`
        // attach to the chord rather than to a note.
        object elements = ((MadeMusic)chord).Properties.FindLast(p => p.Name == "elements").Value;
        Pair.ToList(elements).Should().Equal(first, second, "art");

        host.MusicSpots.Should().ContainSingle();
        host.MusicSpots[0].Location.Should().Be(whole);
    }

    // ------ chord_body and its elements ------

    [Fact]
    public void an_angle_bracket_chord_becomes_an_event_chord_in_written_order()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MusicObject first = new MusicObject(Nil.Instance);
        MusicObject second = new MusicObject(Nil.Instance);

        //Act
        object result = Action("chord_body: ANGLE_OPEN chord_body_elements ANGLE_CLOSE")(
            context,
            new object[] { null, new Pair(second, new Pair(first, Nil.Instance)), null },
            Spans(3),
            Spans(1)[0]);

        //Assert
        SyntaxMark chord = result.Should().BeOfType<SyntaxMark>().Which;
        chord.Name.Should().Be("event-chord");
        Pair.ToList(chord.Arguments[0]).Should().Equal(first, second);
    }

    [Fact]
    public void a_figure_chord_is_a_plain_reverse_with_no_post_event_sorting()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("chord_body: FIGURE_OPEN figure_list FIGURE_CLOSE")(
            context,
            new object[] { null, new Pair("six", new Pair("four", Nil.Instance)), null },
            Spans(3),
            Spans(1)[0]);

        //Assert
        // A figure list cannot contain a post event, so reverse_music_list is not
        // involved and the values need not even be music.
        SyntaxMark chord = result.Should().BeOfType<SyntaxMark>().Which;
        chord.Name.Should().Be("event-chord");
        Pair.ToList(chord.Arguments[0]).Should().Equal("four", "six");
    }

    [Fact]
    public void chord_body_elements_accumulate_in_reverse_and_skip_non_music()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MusicObject note = new MusicObject(Nil.Instance);

        //Act
        object empty = Action("chord_body_elements: /* empty */")(
            context, new object[0], Spans(0), Spans(1)[0]);
        object one = Action("chord_body_elements: chord_body_elements chord_body_element")(
            context, new object[] { empty, note }, Spans(2), Spans(1)[0]);
        object skipped = Action("chord_body_elements: chord_body_elements chord_body_element")(
            context, new object[] { one, Unspecified.Instance }, Spans(2), Spans(1)[0]);

        //Assert
        // The "not a rhythmic event" case answers SCM_UNSPECIFIED, and is silently
        // skipped here — the error has already been given.
        Pair.ToList(one).Should().Equal(note);
        skipped.Should().BeSameAs(one);
    }

    [Fact]
    public void a_chord_note_takes_its_accidental_marks_and_octave_check_but_no_duration()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        Pitch middleC = new Pitch(0, 0, Rational.Zero);

        //Act
        object result = Action(
            "chord_body_element: pitch_or_tonic_pitch exclamations questions octave_check"
            + " post_events %prec ':'")(
            context,
            new object[] { middleC, DefaultArgument.Instance, true, 2L, Nil.Instance },
            Spans(5),
            Spans(1)[0]);

        //Assert
        MadeMusic note = result.Should().BeOfType<MadeMusic>().Which;
        note.Name.Should().Be("NoteEvent");
        note.Properties.Should().Contain(("pitch", (object)middleC));
        note.Properties.Should().Contain(("cautionary", (object)true));
        note.Properties.Should().Contain(("force-accidental", (object)true));
        note.Properties.Should().Contain(("absolute-octave", (object)1L));

        // Inside a chord the elements carry NO duration of their own —
        // note_chord_element gives them one.
        note.Properties.Should().NotContain(p => p.Name == "duration");
    }

    [Fact]
    public void a_chord_note_with_no_marks_stays_plain()
    {
        //Arrange
        // from_scm<bool> is exactly-#t, so the SCM_UNDEFINED "nothing written" value
        // must not make every chord note cautionary.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action(
            "chord_body_element: pitch_or_tonic_pitch exclamations questions octave_check"
            + " post_events %prec ':'")(
            context,
            new object[]
            {
                new Pitch(0, 0, Rational.Zero),
                DefaultArgument.Instance,
                DefaultArgument.Instance,
                Nil.Instance,
                Nil.Instance,
            },
            Spans(5),
            Spans(1)[0]);

        //Assert
        MadeMusic note = result.Should().BeOfType<MadeMusic>().Which;
        note.Properties.Should().NotContain(p => p.Name == "cautionary");
        note.Properties.Should().NotContain(p => p.Name == "force-accidental");
        note.Properties.Should().NotContain(p => p.Name == "absolute-octave");
    }

    [Fact]
    public void a_drum_pitch_inside_a_chord_carries_its_articulations()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("chord_body_element: DRUM_PITCH post_events %prec ':'")(
            context,
            new object[] { Symbol.Intern("bassdrum"), new Pair("art", Nil.Instance) },
            Spans(2),
            Spans(1)[0]);

        //Assert
        MadeMusic note = result.Should().BeOfType<MadeMusic>().Which;
        note.Properties.Should().Contain(("drum-type", (object)Symbol.Intern("bassdrum")));
        Pair.ToList(note.Properties.Find(p => p.Name == "articulations").Value)
            .Should().Equal("art");
    }

    // ------ music functions inside a chord ------

    [Fact]
    public void a_post_event_music_function_inside_a_chord_passes_untouched()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        MusicObject postEvent = new MusicObject(Nil.Instance);
        host.WithMusicType(postEvent, "post-event");
        ParseContext context = NewContext(host);

        //Act
        object result = Action("chord_body_element: music_function_chord_body")(
            context, new object[] { postEvent }, Spans(1), Spans(1)[0]);

        //Assert
        result.Should().BeSameAs(postEvent);
        host.ErrorLevel.Should().Be(0);
    }

    [Fact]
    public void a_wrapped_rhythmic_event_is_unwrapped_down_to_the_note()
    {
        //Arrange
        // A \tweak'd note contributes the NOTE, not the wrapper: the unwrapping
        // replaces $$ layer by layer.
        ScriptedParserHost host = new ScriptedParserHost();
        MusicObject note = new MusicObject(Nil.Instance);
        host.WithMusicType(note, "rhythmic-event");

        MusicObject inner = new MusicObject(Nil.Instance);
        host.WithMusicType(inner, "music-wrapper-music");
        inner.SetProperty("element", note);

        MusicObject outer = new MusicObject(Nil.Instance);
        host.WithMusicType(outer, "music-wrapper-music");
        outer.SetProperty("element", inner);

        ParseContext context = NewContext(host);

        //Act
        object result = Action("chord_body_element: music_function_chord_body")(
            context, new object[] { outer }, Spans(1), Spans(1)[0]);

        //Assert
        result.Should().BeSameAs(note);
        host.ErrorLevel.Should().Be(0);
    }

    [Fact]
    public void a_music_function_that_is_not_a_rhythmic_event_is_refused()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        MusicObject ordinary = new MusicObject(Nil.Instance);
        ParseContext context = NewContext(host);

        //Act
        object result = Action("chord_body_element: music_function_chord_body")(
            context, new object[] { ordinary }, Spans(1), Spans(1)[0]);

        //Assert
        result.Should().Be(Unspecified.Instance);
        host.ErrorLevel.Should().Be(1);
    }

    [Fact]
    public void an_event_function_goes_to_the_music_function_constructor()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("event_function_event: EVENT_FUNCTION function_arglist")(
            context, new object[] { "the-function", Nil.Instance }, Spans(2), Spans(1)[0]);

        //Assert
        SyntaxMark mark = result.Should().BeOfType<SyntaxMark>().Which;
        mark.Name.Should().Be("music-function");
        mark.Arguments.Should().Equal("the-function", Nil.Instance);
    }

    // ------ chord names ------

    [Fact]
    public void a_bare_chord_root_with_no_duration_stays_a_pitch()
    {
        //Arrange
        // "Can return a single pitch rather than a list" — RAG16's
        // `pitch_or_music: new_chord post_events` expands it only when it has to.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        Pitch root = new Pitch(0, 0, Rational.Zero);

        //Act
        object result = Action("new_chord: steno_tonic_pitch maybe_notemode_duration")(
            context, new object[] { root, DefaultArgument.Instance }, Spans(2), Spans(1)[0]);

        //Assert
        result.Should().BeSameAs(root);
        host.ChordElementCalls.Should().BeEmpty();
    }

    [Fact]
    public void a_chord_root_with_a_duration_is_expanded_immediately()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        Pitch root = new Pitch(0, 0, Rational.Zero);
        Duration quarter = new Duration(2, 0);

        //Act
        Action("new_chord: steno_tonic_pitch maybe_notemode_duration")(
            context, new object[] { root, quarter }, Spans(2), Spans(1)[0]);

        //Assert
        host.ChordElementCalls.Should().ContainSingle();
        host.ChordElementCalls[0].Pitch.Should().BeSameAs(root);
        host.ChordElementCalls[0].Duration.Should().Be(quarter);
        host.ChordElementCalls[0].Modifications.Should().Be(Nil.Instance);
    }

    [Fact]
    public void the_separator_leads_the_modification_list_and_the_items_follow_in_order()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        Pitch root = new Pitch(0, 0, Rational.Zero);

        //Act
        Action(
            "new_chord: steno_tonic_pitch optional_notemode_duration chord_separator"
            + " chord_items %prec ':'")(
            context,
            new object[]
            {
                root,
                new Duration(2, 0),
                Symbol.Intern("chord-colon"),
                new Pair("second", new Pair("first", Nil.Instance)),
            },
            Spans(4),
            Spans(1)[0]);

        //Assert
        // construct-chord-elements reads the separator first, to learn what KIND of
        // chord this is.
        host.ChordElementCalls.Should().ContainSingle();
        Pair.ToList(host.ChordElementCalls[0].Modifications)
            .Should().Equal(Symbol.Intern("chord-colon"), "first", "second");
    }

    [Fact]
    public void the_four_chord_separators_carry_their_own_shapes()
    {
        //Arrange
        ParseContext context = NewContext(new ScriptedParserHost());
        Pitch bass = new Pitch(0, 4, Rational.Zero);

        //Act
        object colon = Action("chord_separator: CHORD_COLON")(
            context, new object[] { null }, Spans(1), Spans(1)[0]);
        object caret = Action("chord_separator: CHORD_CARET")(
            context, new object[] { null }, Spans(1), Spans(1)[0]);
        object slash = Action("chord_separator: CHORD_SLASH steno_tonic_pitch")(
            context, new object[] { null, bass }, Spans(2), Spans(1)[0]);
        object bassSeparator = Action("chord_separator: CHORD_BASS steno_tonic_pitch")(
            context, new object[] { null, bass }, Spans(2), Spans(1)[0]);

        //Assert
        // The first two are bare symbols; the two that carry a pitch are lists.
        // construct-chord-elements dispatches on the head either way.
        colon.Should().Be(Symbol.Intern("chord-colon"));
        caret.Should().Be(Symbol.Intern("chord-caret"));
        Pair.ToList(slash).Should().Equal(Symbol.Intern("chord-slash"), bass);
        Pair.ToList(bassSeparator).Should().Equal(Symbol.Intern("chord-bass"), bass);
    }

    [Fact]
    public void a_dotted_step_group_is_restored_to_written_order()
    {
        //Arrange
        ParseContext context = NewContext(new ScriptedParserHost());

        //Act
        object one = Action("step_numbers: step_number")(
            context, new object[] { "five" }, Spans(1), Spans(1)[0]);
        object two = Action("step_numbers: step_numbers '.' step_number")(
            context, new object[] { one, '.', "nine" }, Spans(3), Spans(1)[0]);

        // Accumulated newest-first — asserted BEFORE the reverse, because
        // scm_reverse_x is destructive and re-points the very pairs `two` names.
        Pair.ToList(two).Should().Equal("nine", "five");

        object item = Action("chord_item: step_numbers")(
            context, new object[] { two }, Spans(1), Spans(1)[0]);

        //Assert
        Pair.ToList(item).Should().Equal("five", "nine");
    }

    [Fact]
    public void chord_items_accumulate_in_reverse()
    {
        //Arrange
        ParseContext context = NewContext(new ScriptedParserHost());

        //Act
        object empty = Action("chord_items: /* empty */")(
            context, new object[0], Spans(0), Spans(1)[0]);
        object one = Action("chord_items: chord_items chord_item")(
            context, new object[] { empty, "first" }, Spans(2), Spans(1)[0]);
        object two = Action("chord_items: chord_items chord_item")(
            context, new object[] { one, "second" }, Spans(2), Spans(1)[0]);

        //Assert
        empty.Should().Be(Nil.Instance);
        Pair.ToList(two).Should().Equal("second", "first");
    }

    [Fact]
    public void a_chord_separator_or_modifier_is_a_chord_item_unchanged()
    {
        //Arrange
        ParseContext context = NewContext(new ScriptedParserHost());
        Symbol separator = Symbol.Intern("chord-colon");

        //Act
        object fromSeparator = Action("chord_item: chord_separator")(
            context, new object[] { separator }, Spans(1), Spans(1)[0]);
        object fromModifier = Action("chord_item: CHORD_MODIFIER")(
            context, new object[] { "maj" }, Spans(1), Spans(1)[0]);

        //Assert
        fromSeparator.Should().BeSameAs(separator);
        fromModifier.Should().Be("maj");
    }

    // ------ step numbers ------

    [Fact]
    public void a_step_number_is_the_pitch_that_step_above_the_root()
    {
        //Arrange
        ParseContext context = NewContext(new ScriptedParserHost());

        //Act
        object third = Action("step_number: UNSIGNED")(
            context, new object[] { 3L }, Spans(1), Spans(1)[0]);
        object ninth = Action("step_number: UNSIGNED")(
            context, new object[] { 9L }, Spans(1), Spans(1)[0]);

        //Assert
        // One-based when written, zero-based as a scale index; the pitch normalizes
        // its own octave, so the ninth is a second an octave up.
        ((Pitch)third).NoteName.Should().Be(2);
        ((Pitch)third).Octave.Should().Be(0);
        ((Pitch)ninth).NoteName.Should().Be(1);
        ((Pitch)ninth).Octave.Should().Be(1);
    }

    [Fact]
    public void the_seventh_is_flattened_because_chord_naming_means_a_minor_seventh()
    {
        //Arrange
        ParseContext context = NewContext(new ScriptedParserHost());

        //Act
        object seventh = Action("step_number: UNSIGNED")(
            context, new object[] { 7L }, Spans(1), Spans(1)[0]);

        //Assert
        // get_notename () == 6 is the only special case in make_chord_step.
        ((Pitch)seventh).NoteName.Should().Be(6);
        ((Pitch)seventh).Alteration.Should().Be(new Rational(-1, 2));
    }

    [Fact]
    public void a_written_alteration_sharpens_or_flattens_the_step()
    {
        //Arrange
        ParseContext context = NewContext(new ScriptedParserHost());

        //Act
        object sharp = Action("step_number: UNSIGNED '+'")(
            context, new object[] { 5L, '+' }, Spans(2), Spans(1)[0]);
        object flat = Action("step_number: UNSIGNED CHORD_MINUS")(
            context, new object[] { 5L, null }, Spans(2), Spans(1)[0]);

        //Assert
        ((Pitch)sharp).NoteName.Should().Be(4);
        ((Pitch)sharp).Alteration.Should().Be(new Rational(1, 2));
        ((Pitch)flat).Alteration.Should().Be(new Rational(-1, 2));
    }

    // ------ real text ------

    [Fact]
    public void an_angle_bracket_chord_from_real_text_gives_every_note_the_written_duration()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup("<c e g>4");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);

        // Three NoteEvents, each with the one duration written after the bracket.
        List<MusicObject> notes = host.RealMusicObjects.FindAll(m => m.Name == "NoteEvent");
        notes.Should().HaveCount(3);

        foreach (MusicObject note in notes)
        {
            ((Duration)note.GetProperty("duration")).DurationLog.Should().Be(2);
        }
    }

    [Fact]
    public void the_notes_of_a_chord_keep_their_written_pitches_in_order()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup("<c e g>4");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);

        List<MusicObject> notes = host.RealMusicObjects.FindAll(m => m.Name == "NoteEvent");
        ((Pitch)notes[0].GetProperty("pitch")).NoteName.Should().Be(0);
        ((Pitch)notes[1].GetProperty("pitch")).NoteName.Should().Be(2);
        ((Pitch)notes[2].GetProperty("pitch")).NoteName.Should().Be(4);
    }

    [Fact]
    public void an_empty_chord_from_real_text_still_parses()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup("<>4");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        host.RealMusicObjects.Should().NotContain(m => m.Name == "NoteEvent");
    }
}
