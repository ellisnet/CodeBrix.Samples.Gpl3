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
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <summary>
/// RULE ACTION GROUP 16 — pitches, octaves and durations. These are the rules every
/// written note and every written rhythm goes through, so the emphasis is on REAL TEXT:
/// <c>c'4.</c> is lexed, parsed and reduced through the actual tables, and what comes
/// out is inspected. The rules whose surrounding grammar wants values a scripted host
/// cannot lex (a <c>DURATION_IDENTIFIER</c>, a <c>PITCH_IDENTIFIER</c>) are invoked
/// directly by identity.
/// </summary>
public class RuleActionRag16Tests
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

    // Asserts a clean parse, and says WHAT went wrong when it was not — an ErrorCount
    // on its own leaves the reader guessing at which token the tables refused.
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

    /// <summary>
    /// Sets a run up over the given music, wrapped in <c>\notemode { }</c>.
    /// <para>
    /// The wrapper is not decoration. These rules are lexed in the NOTES start
    /// condition, where — unlike INITIAL — there is no <c>{REAL}</c> rule (so
    /// <c>4.</c> is an <c>UNSIGNED</c> and a dot, which is what makes a dotted
    /// duration parse at all) and <c>[rs]</c> is a <c>RESTNAME</c> rather than a bare
    /// word. So the host is given the scanner to drive, and the mode is entered the
    /// way a real file enters it.
    /// </para>
    /// </summary>
    /// <param name="music">The music, without its braces.</param>
    /// <returns>The parser, the scanner and the host.</returns>
    private static (LalrParser Parser, ModalScanner Scanner, ScriptedParserHost Host) Setup(
        string music)
    {
        // MakeRealMusic: the surrounding grammar reduces music_list through
        // reverse_music_list, which is ported over the Engine's MusicObject directly,
        // so a recording stand-in cannot travel through it.
        ScriptedParserHost host = new ScriptedParserHost
        {
            IsNoteState = true,
            MakeRealMusic = true,
        };

        host.Keywords["notemode"] = ("NOTEMODE", null);
        host.Globals.Bindings[Symbol.Intern("toplevel-music-handler")] = "music-proc";

        // The note table the NOTES lexer mode consults, scripted the way a real host
        // would answer from ly/<language>.ly.
        host.WordScans[Symbol.Intern("c")] = ("NOTENAME_PITCH", new Pitch(0, 0, Rational.Zero));
        host.WordScans[Symbol.Intern("d")] = ("NOTENAME_PITCH", new Pitch(0, 1, Rational.Zero));
        host.WordScans[Symbol.Intern("bd")] = ("DRUM_PITCH", Symbol.Intern("bassdrum"));

        ModalScanner scanner = new ModalScanner(
            LilyPondLexerRules.Create(host), "\\notemode { " + music + " }", "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);
        host.Scanner = scanner;

        LalrParser parser = new LalrParser(Tables, Bound);
        return (parser, scanner, host);
    }

    // ------ quotes, sub_quotes, sup_quotes, octave_check, erroneous_quotes ------

    [Fact]
    public void no_octave_marks_is_the_fixnum_zero_and_erroneous_quotes_turns_it_undefined()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object none = Action("quotes: /* empty */ %prec ':'")(
            context, new object[0], Spans(0), Spans(1)[0]);
        object erroneous = Action("erroneous_quotes: quotes")(
            context, new object[] { none }, Spans(1), Spans(1)[0]);

        //Assert
        // Zero has to stay tellable from "some quotes": pitch_or_music decides whether
        // to complain about misplaced octave marks with SCM_UNBNDP on this value.
        none.Should().Be(0L);
        erroneous.Should().Be(DefaultArgument.Instance);
    }

    [Fact]
    public void written_octave_marks_survive_erroneous_quotes_unchanged()
    {
        //Arrange
        ParseContext context = NewContext(new ScriptedParserHost());

        //Act
        object erroneous = Action("erroneous_quotes: quotes")(
            context, new object[] { 2L }, Spans(1), Spans(1)[0]);

        //Assert
        erroneous.Should().Be(2L);
    }

    [Fact]
    public void apostrophes_count_up_and_commas_count_down()
    {
        //Arrange
        ParseContext context = NewContext(new ScriptedParserHost());

        //Act
        object oneUp = Action("sup_quotes: '\\''")(context, new object[0], Spans(0), Spans(1)[0]);
        object twoUp = Action("sup_quotes: sup_quotes '\\''")(
            context, new object[] { oneUp, '\'' }, Spans(2), Spans(1)[0]);
        object oneDown = Action("sub_quotes: ','")(context, new object[0], Spans(0), Spans(1)[0]);
        object twoDown = Action("sub_quotes: sub_quotes ','")(
            context, new object[] { oneDown, ',' }, Spans(2), Spans(1)[0]);

        //Assert
        oneUp.Should().Be(1L);
        twoUp.Should().Be(2L);
        oneDown.Should().Be(-1L);
        twoDown.Should().Be(-2L);
    }

    [Fact]
    public void an_absent_octave_check_is_the_empty_list_not_zero()
    {
        //Arrange
        ParseContext context = NewContext(new ScriptedParserHost());

        //Act
        object absent = Action("octave_check: /* empty */")(
            context, new object[0], Spans(0), Spans(1)[0]);
        object written = Action("octave_check: '=' quotes")(
            context, new object[] { '=', 0L }, Spans(2), Spans(1)[0]);

        //Assert
        // `=` with no quotes IS the number zero and means the unquoted octave; no check
        // at all is SCM_EOL. Collapsing the two would make every note carry an
        // absolute-octave.
        absent.Should().Be(Nil.Instance);
        written.Should().Be(0L);
    }

    // ------ the three quoted-pitch bodies ------

    [Fact]
    public void quoting_a_pitch_transposes_it_by_whole_octaves()
    {
        //Arrange
        ParseContext context = NewContext(new ScriptedParserHost());
        Pitch middleC = new Pitch(0, 0, Rational.Zero);

        //Act
        object up = Action("steno_pitch: NOTENAME_PITCH quotes")(
            context, new object[] { middleC, 2L }, Spans(2), Spans(1)[0]);
        object down = Action("steno_pitch: NOTENAME_PITCH quotes")(
            context, new object[] { middleC, -1L }, Spans(2), Spans(1)[0]);
        object unquoted = Action("steno_pitch: NOTENAME_PITCH quotes")(
            context, new object[] { middleC, 0L }, Spans(2), Spans(1)[0]);

        //Assert
        ((Pitch)up).Octave.Should().Be(2);
        ((Pitch)down).Octave.Should().Be(-1);

        // Zero quotes must hand back the SAME value, not a transposed copy: the
        // scm_is_eq guard is what keeps a PITCH_IDENTIFIER's identity intact.
        unquoted.Should().BeSameAs(middleC);
    }

    [Fact]
    public void all_three_quotable_pitch_tokens_transpose_the_same_way()
    {
        //Arrange
        // Upstream's "ugh. duplication": three identical bodies, and the port writes
        // them out three times. This is the test that says they stayed identical.
        ParseContext context = NewContext(new ScriptedParserHost());
        Pitch middleC = new Pitch(0, 0, Rational.Zero);

        string[] identities =
        {
            "steno_pitch: NOTENAME_PITCH quotes",
            "steno_tonic_pitch: TONICNAME_PITCH quotes",
            "pitch: PITCH_IDENTIFIER quotes",
        };

        foreach (string identity in identities)
        {
            //Act
            object result = Action(identity)(
                context, new object[] { middleC, 1L }, Spans(2), Spans(1)[0]);

            //Assert
            ((Pitch)result).Octave.Should().Be(1);
            ((Pitch)result).NoteName.Should().Be(0);
        }
    }

    // ------ dots, steno_duration, duration, multipliers ------

    [Fact]
    public void a_duration_from_real_text_carries_its_log_and_its_dots()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup("c4.");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);

        MusicObject note = host.RealMusicObjects.Should().ContainSingle().Which;
        note.Name.Should().Be("NoteEvent");

        Duration duration = (Duration)note.GetProperty("duration");
        duration.DurationLog.Should().Be(2);
        duration.DotCount.Should().Be(1);
    }

    [Fact]
    public void a_duration_that_is_not_a_power_of_two_is_refused()
    {
        //Arrange
        // make_duration answers SCM_UNDEFINED for anything but a positive power of
        // two, and steno_duration turns that into the diagnostic — `c3` is the case.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup("c3");

        //Act
        parser.Parse(scanner, host);

        //Assert
        parser.ErrorCount.Should().BeGreaterThan(0);
        host.ErrorLevel.Should().Be(1);
    }

    [Fact]
    public void a_multiplier_scales_the_duration_and_a_fraction_stays_exact()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup("c4*2/3");

        //Act
        parser.Parse(scanner, host);

        //Assert
        // The FRACTION token is scan_fraction's (2 . 3) PAIR and scm_divide of two
        // exacts stays exact, so the factor is exactly 2/3 rather than 0.666...
        NoErrors(parser);

        MusicObject note = host.RealMusicObjects.Should().ContainSingle().Which;
        Duration duration = (Duration)note.GetProperty("duration");
        duration.Factor.Should().Be(new Rational(2, 3));
    }

    [Fact]
    public void multipliers_accumulate_left_to_right()
    {
        //Arrange
        ParseContext context = NewContext(new ScriptedParserHost());

        //Act
        object none = Action("multipliers: /* empty */")(
            context, new object[0], Spans(0), Spans(1)[0]);
        object first = Action("multipliers: multipliers '*' UNSIGNED")(
            context, new object[] { none, '*', 3L }, Spans(3), Spans(1)[0]);
        object second = Action("multipliers: multipliers '*' UNSIGNED")(
            context, new object[] { first, '*', 5L }, Spans(3), Spans(1)[0]);

        //Assert
        // SCM_UNDEFINED until the first one arrives — that is what lets `duration`
        // hand "no multiplier at all" to make_duration as its factor argument.
        none.Should().Be(DefaultArgument.Instance);
        first.Should().Be(3L);
        second.Should().Be(15L);
    }

    [Fact]
    public void a_scheme_multiplier_that_is_not_a_scale_is_refused_and_dropped()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object kept = Action("multipliers: multipliers '*' multiplier_scm")(
            context, new object[] { 4L, '*', "not a scale" }, Spans(3), Spans(1)[0]);
        object accepted = Action("multipliers: multipliers '*' multiplier_scm")(
            context, new object[] { 4L, '*', new Pair(1L, 2L) }, Spans(3), Spans(1)[0]);

        //Assert
        // The error branch assigns nothing, so the accumulated value rides Bison's
        // implicit $$ = $1 and the bad factor is simply dropped.
        kept.Should().Be(4L);
        host.ErrorLevel.Should().Be(1);
        SchemeNumber.NumericEquals(accepted, 2L).Should().BeTrue();
    }

    [Fact]
    public void dots_count_up_from_zero()
    {
        //Arrange
        ParseContext context = NewContext(new ScriptedParserHost());

        //Act
        object none = Action("dots: /* empty */")(context, new object[0], Spans(0), Spans(1)[0]);
        object one = Action("dots: dots '.'")(
            context, new object[] { none, '.' }, Spans(2), Spans(1)[0]);

        //Assert
        none.Should().Be(0L);
        one.Should().Be(1L);
    }

    // ------ the sticky duration ------

    [Fact]
    public void a_written_duration_sticks_and_a_later_note_without_one_inherits_it()
    {
        //Arrange
        // THE STICKY DURATION, the behaviour every LilyPond file depends on: `c4 d`
        // is two quarter notes.
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup("c4 d");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        host.RealMusicObjects.Should().HaveCount(2);

        foreach (MusicObject note in host.RealMusicObjects)
        {
            Duration duration = (Duration)note.GetProperty("duration");
            duration.DurationLog.Should().Be(2);
            duration.DotCount.Should().Be(0);
        }

        host.DefaultDuration.DurationLog.Should().Be(2);
    }

    [Fact]
    public void the_read_side_of_the_sticky_duration_hands_back_a_fresh_copy()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost { DefaultDuration = new Duration(3, 1) };
        ParseContext context = NewContext(host);

        //Act
        object inherited = Action("optional_notemode_duration: maybe_notemode_duration")(
            context, new object[] { DefaultArgument.Instance }, Spans(1), Spans(1)[0]);
        object written = Action("optional_notemode_duration: maybe_notemode_duration")(
            context, new object[] { new Duration(1, 0) }, Spans(1), Spans(1)[0]);

        //Assert
        // smobbed_copy: a fresh box per read, never the parser's own storage.
        ((Duration)inherited).DurationLog.Should().Be(3);
        ((Duration)inherited).DotCount.Should().Be(1);
        ((Duration)written).DurationLog.Should().Be(1);
    }

    // ------ tremolo_type ------

    [Fact]
    public void a_written_tremolo_type_sticks_and_a_bare_colon_repeats_it()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object bareBefore = Action("tremolo_type: ':'")(
            context, new object[] { ':' }, Spans(1), Spans(1)[0]);
        object written = Action("tremolo_type: ':' UNSIGNED")(
            context, new object[] { ':', 16L }, Spans(2), Spans(1)[0]);
        object bareAfter = Action("tremolo_type: ':'")(
            context, new object[] { ':' }, Spans(1), Spans(1)[0]);

        //Assert
        bareBefore.Should().Be(8L);
        written.Should().Be(16L);
        bareAfter.Should().Be(16L);
        host.DefaultTremoloType.Should().Be(16);
    }

    [Fact]
    public void a_tremolo_type_that_is_not_a_duration_is_refused_and_does_not_stick()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object refused = Action("tremolo_type: ':' UNSIGNED")(
            context, new object[] { ':', 6L }, Spans(2), Spans(1)[0]);

        //Assert
        // make_duration is called only as a validity test; the WRITTEN number is what
        // would have travelled on, and 6 is not a power of two.
        refused.Should().Be(8L);
        host.DefaultTremoloType.Should().Be(8);
        host.ErrorLevel.Should().Be(1);
    }

    // ------ optional_rest ------

    [Fact]
    public void optional_rest_is_a_plain_flag()
    {
        //Arrange
        ParseContext context = NewContext(new ScriptedParserHost());

        //Act
        object absent = Action("optional_rest: /* empty */")(
            context, new object[0], Spans(0), Spans(1)[0]);
        object written = Action("optional_rest: REST")(
            context, new object[] { null }, Spans(1), Spans(1)[0]);

        //Assert
        absent.Should().Be(false);
        written.Should().Be(true);
    }

    // ------ pitch_or_music ------

    [Fact]
    public void a_bare_pitch_with_nothing_attached_stays_a_pitch()
    {
        //Arrange
        // $$ is pre-set to $1 and the guard condition is false, so pitch_or_music
        // answers the PITCH — which is what lets the same nonterminal serve as
        // "a note" and as "a pitch" (identifier_init, chord roots).
        ScriptedParserHost host = new ScriptedParserHost { IsNoteState = true };
        ParseContext context = NewContext(host);
        Pitch middleC = new Pitch(0, 0, Rational.Zero);

        //Act
        object result = PitchOrMusic(
            context,
            middleC,
            DefaultArgument.Instance,
            DefaultArgument.Instance,
            Nil.Instance,
            DefaultArgument.Instance,
            DefaultArgument.Instance,
            false,
            Nil.Instance);

        //Assert
        result.Should().BeSameAs(middleC);
        host.MadeMusicObjects.Should().BeEmpty();
    }

    [Fact]
    public void a_pitch_outside_note_mode_is_refused()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost { IsNoteState = false };
        ParseContext context = NewContext(host);

        //Act
        PitchOrMusic(
            context,
            new Pitch(0, 0, Rational.Zero),
            DefaultArgument.Instance,
            DefaultArgument.Instance,
            Nil.Instance,
            DefaultArgument.Instance,
            DefaultArgument.Instance,
            false,
            Nil.Instance);

        //Assert
        host.ErrorLevel.Should().Be(1);
    }

    [Fact]
    public void accidental_marks_reach_the_note_and_undefined_reads_as_false()
    {
        //Arrange
        // from_scm<bool> is scm_is_eq (s, SCM_BOOL_T), NOT Scheme truthiness — so the
        // SCM_UNDEFINED "no marks were written" value must read as false. Reading it
        // as true would make every plain note cautionary.
        ScriptedParserHost host = new ScriptedParserHost { IsNoteState = true };
        ParseContext context = NewContext(host);

        //Act
        PitchOrMusic(
            context,
            new Pitch(0, 0, Rational.Zero),
            DefaultArgument.Instance,
            true,
            Nil.Instance,
            DefaultArgument.Instance,
            DefaultArgument.Instance,
            false,
            Nil.Instance);

        //Assert
        MadeMusic note = host.MadeMusicObjects.Should().ContainSingle().Which;
        note.Name.Should().Be("NoteEvent");
        note.Properties.Should().Contain(("cautionary", (object)true));
        note.Properties.Should().Contain(("force-accidental", (object)true));
    }

    [Fact]
    public void an_exclamation_alone_forces_the_accidental_without_making_it_cautionary()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost { IsNoteState = true };
        ParseContext context = NewContext(host);

        //Act
        PitchOrMusic(
            context,
            new Pitch(0, 0, Rational.Zero),
            true,
            DefaultArgument.Instance,
            Nil.Instance,
            DefaultArgument.Instance,
            DefaultArgument.Instance,
            false,
            Nil.Instance);

        //Assert
        MadeMusic note = host.MadeMusicObjects.Should().ContainSingle().Which;
        note.Properties.Should().Contain(("force-accidental", (object)true));
        note.Properties.Should().NotContain(("cautionary", (object)true));
    }

    [Fact]
    public void the_rest_flag_chooses_RestEvent_over_NoteEvent()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost { IsNoteState = true };
        ParseContext context = NewContext(host);

        //Act
        PitchOrMusic(
            context,
            new Pitch(0, 0, Rational.Zero),
            DefaultArgument.Instance,
            DefaultArgument.Instance,
            Nil.Instance,
            DefaultArgument.Instance,
            DefaultArgument.Instance,
            true,
            Nil.Instance);

        //Assert
        host.MadeMusicObjects.Should().ContainSingle().Which.Name.Should().Be("RestEvent");
    }

    [Fact]
    public void an_octave_check_becomes_absolute_octave_one_lower_than_written()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost { IsNoteState = true };
        ParseContext context = NewContext(host);

        //Act
        PitchOrMusic(
            context,
            new Pitch(0, 0, Rational.Zero),
            DefaultArgument.Instance,
            DefaultArgument.Instance,
            2L,
            DefaultArgument.Instance,
            DefaultArgument.Instance,
            false,
            Nil.Instance);

        //Assert
        // to_scm (q - 1): the check counts from the written quote count, the property
        // from the unquoted octave.
        host.MadeMusicObjects.Should().ContainSingle().Which
            .Properties.Should().Contain(("absolute-octave", (object)1L));
    }

    [Fact]
    public void octave_marks_after_a_duration_are_reported_and_folded_into_the_pitch()
    {
        //Arrange
        // `a1''` — the note-entry error erroneous_quotes exists for. With no octave
        // check to absorb them, the quotes are folded into the pitch instead.
        ScriptedParserHost host = new ScriptedParserHost { IsNoteState = true };
        ParseContext context = NewContext(host);

        //Act
        PitchOrMusic(
            context,
            new Pitch(0, 0, Rational.Zero),
            DefaultArgument.Instance,
            DefaultArgument.Instance,
            Nil.Instance,
            new Duration(0, 0),
            2L,
            false,
            Nil.Instance);

        //Assert
        host.ErrorLevel.Should().Be(1);

        MadeMusic note = host.MadeMusicObjects.Should().ContainSingle().Which;
        Pitch pitch = (Pitch)note.Properties.Find(p => p.Name == "pitch").Value;
        pitch.Octave.Should().Be(2);
    }

    [Fact]
    public void misplaced_octave_marks_are_added_to_an_octave_check_when_there_is_one()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost { IsNoteState = true };
        ParseContext context = NewContext(host);

        //Act
        PitchOrMusic(
            context,
            new Pitch(0, 0, Rational.Zero),
            DefaultArgument.Instance,
            DefaultArgument.Instance,
            1L,
            new Duration(0, 0),
            2L,
            false,
            Nil.Instance);

        //Assert
        // "Try sorting the quotes to where they likely belong": the check absorbs
        // them, so the pitch is left alone and absolute-octave carries 1 + 2 - 1.
        MadeMusic note = host.MadeMusicObjects.Should().ContainSingle().Which;
        note.Properties.Should().Contain(("absolute-octave", (object)2L));
        ((Pitch)note.Properties.Find(p => p.Name == "pitch").Value).Octave.Should().Be(0);
    }

    [Fact]
    public void post_events_become_articulations_in_written_order()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost { IsNoteState = true };
        ParseContext context = NewContext(host);
        object events = new Pair("second", new Pair("first", Nil.Instance));

        //Act
        PitchOrMusic(
            context,
            new Pitch(0, 0, Rational.Zero),
            DefaultArgument.Instance,
            DefaultArgument.Instance,
            Nil.Instance,
            DefaultArgument.Instance,
            DefaultArgument.Instance,
            false,
            events);

        //Assert
        // post_events accumulates in reverse; scm_reverse_x restores document order.
        MadeMusic note = host.MadeMusicObjects.Should().ContainSingle().Which;
        object articulations = note.Properties.Find(p => p.Name == "articulations").Value;
        Pair.ToList(articulations).Should().Equal("first", "second");
    }

    [Fact]
    public void a_chord_root_outside_chord_mode_is_refused()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost { IsChordState = false };
        ParseContext context = NewContext(host);

        //Act
        object result = Action("pitch_or_music: new_chord post_events %prec ':'")(
            context,
            new object[] { new Pitch(0, 0, Rational.Zero), Nil.Instance },
            Spans(2),
            Spans(1)[0]);

        //Assert
        host.ErrorLevel.Should().Be(1);

        // A mere pitch with no post events still drops through unchanged.
        result.Should().BeOfType<Pitch>();
    }

    [Fact]
    public void a_chord_root_with_post_events_is_expanded_into_an_event_chord()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost { IsChordState = true };
        MusicObject element = new MusicObject(Nil.Instance);
        host.ChordElementsResult = new Pair(element, Nil.Instance);
        ParseContext context = NewContext(host);

        //Act
        object result = Action("pitch_or_music: new_chord post_events %prec ':'")(
            context,
            new object[]
            {
                new Pitch(0, 0, Rational.Zero),
                new Pair("articulation", Nil.Instance),
            },
            Spans(2),
            Spans(1)[0]);

        //Assert
        // A bare root becomes chord elements first, then the post events are appended
        // after them and the whole thing is wrapped as an event-chord.
        host.ChordElementCalls.Should().ContainSingle();

        SyntaxMark chord = result.Should().BeOfType<SyntaxMark>().Which;
        chord.Name.Should().Be("event-chord");
        Pair.ToList(chord.Arguments[0]).Should().Equal(element, "articulation");
    }

    [Fact]
    public void an_already_expanded_chord_with_no_post_events_is_still_wrapped()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost { IsChordState = true };
        ParseContext context = NewContext(host);
        object elements = new Pair(new MusicObject(Nil.Instance), Nil.Instance);

        //Act
        object result = Action("pitch_or_music: new_chord post_events %prec ':'")(
            context, new object[] { elements, Nil.Instance }, Spans(2), Spans(1)[0]);

        //Assert
        host.ChordElementCalls.Should().BeEmpty();
        result.Should().BeOfType<SyntaxMark>().Which.Name.Should().Be("event-chord");
    }

    // ------ simple_element ------

    [Fact]
    public void a_drum_pitch_from_real_text_becomes_a_note_event_with_a_drum_type()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup("bd4");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);

        MusicObject note = host.RealMusicObjects.Should().ContainSingle().Which;
        note.Name.Should().Be("NoteEvent");
        note.GetProperty("drum-type").Should().Be(Symbol.Intern("bassdrum"));
    }

    [Fact]
    public void r_is_a_rest_and_s_is_a_skip()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        Action("simple_element: RESTNAME optional_notemode_duration")(
            context, new object[] { "r", new Duration(2, 0) }, Spans(2), Spans(1)[0]);
        Action("simple_element: RESTNAME optional_notemode_duration")(
            context, new object[] { "s", new Duration(2, 0) }, Spans(2), Spans(1)[0]);

        //Assert
        // RESTNAME is the [rs] lexer class, so the test really is exactly "s or r".
        host.MadeMusicObjects.Should().HaveCount(2);
        host.MadeMusicObjects[0].Name.Should().Be("RestEvent");
        host.MadeMusicObjects[1].Name.Should().Be("SkipEvent");
    }

    [Fact]
    public void a_rest_from_real_text_carries_the_sticky_duration()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup("c8 r");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        host.RealMusicObjects.Should().HaveCount(2);

        MusicObject rest = host.RealMusicObjects[1];
        rest.Name.Should().Be("RestEvent");
        ((Duration)rest.GetProperty("duration")).DurationLog.Should().Be(3);
    }

    // Invokes pitch_or_music's eight-symbol alternative with named arguments, which is
    // the only way its combinations stay readable.
    private static object PitchOrMusic(
        ParseContext context,
        object pitch,
        object exclamations,
        object questions,
        object octaveCheck,
        object duration,
        object erroneousQuotes,
        object rest,
        object postEvents)
        => Action(
            "pitch_or_music: pitch exclamations questions octave_check"
            + " maybe_notemode_duration erroneous_quotes optional_rest post_events %prec ':'")(
            context,
            new[]
            {
                pitch, exclamations, questions, octaveCheck, duration, erroneousQuotes, rest,
                postEvents,
            },
            Spans(8),
            Spans(1)[0]);
}
