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
/// RULE ACTION GROUP 15 — post events, scripts and text attachments: everything
/// written after a note. The direction machinery is the substance of the group, so the
/// tests turn mostly on WHICH of <c>^</c>, <c>_</c> and <c>-</c> writes a direction
/// property and which deliberately leaves it alone.
/// </summary>
public class RuleActionRag15Tests
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

        ModalScanner scanner = new ModalScanner(
            LilyPondLexerRules.Create(host), "\\notemode { " + music + " }", "<test>");
        scanner.UseSymbols(Tables.Symbols, Tables.TerminalCount);
        host.Scanner = scanner;

        return (new LalrParser(Tables, Bound), scanner, host);
    }

    private static MusicObject Articulation(ScriptedParserHost host, int index)
    {
        MusicObject note = null;
        foreach (MusicObject candidate in host.RealMusicObjects)
        {
            if (candidate.Name == "NoteEvent")
            {
                note = candidate;
            }
        }

        note.Should().NotBeNull();
        List<object> articulations = Pair.ToList(note.GetProperty("articulations"));
        return (MusicObject)articulations[index];
    }

    // ------ post_events ------

    [Fact]
    public void post_events_accumulate_in_reverse_onto_the_empty_list()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        MusicObject first = new MusicObject(Nil.Instance);
        MusicObject second = new MusicObject(Nil.Instance);

        //Act
        object empty = Action("post_events: /* empty */")(
            context, new object[0], Spans(0), Spans(1)[0]);
        object one = Action("post_events: post_events post_event")(
            context, new object[] { empty, first }, Spans(2), Spans(1)[0]);
        object two = Action("post_events: post_events post_event")(
            context, new object[] { one, second }, Spans(2), Spans(1)[0]);

        //Assert
        // Newest first — every consumer finishes with scm_reverse_x.
        empty.Should().Be(Nil.Instance);
        Pair.ToList(two).Should().Equal(second, first);
    }

    [Fact]
    public void a_non_music_post_event_is_dropped_rather_than_consed()
    {
        //Arrange
        // post_event_cons answers the tail unchanged for anything that is not music.
        ParseContext context = NewContext(new ScriptedParserHost());

        //Act
        object result = Action("post_events: post_events post_event")(
            context, new object[] { Nil.Instance, Unspecified.Instance }, Spans(2), Spans(1)[0]);

        //Assert
        result.Should().Be(Nil.Instance);
    }

    // ------ script_dir: the direction the rest of the group turns on ------

    [Fact]
    public void the_three_script_directions_are_down_up_and_no_direction_at_all()
    {
        //Arrange
        ParseContext context = NewContext(new ScriptedParserHost());

        //Act
        object down = Action("script_dir: '_'")(context, new object[] { '_' }, Spans(1), Spans(1)[0]);
        object up = Action("script_dir: '^'")(context, new object[] { '^' }, Spans(1), Spans(1)[0]);
        object neither = Action("script_dir: '-'")(
            context, new object[] { '-' }, Spans(1), Spans(1)[0]);

        //Assert
        // DOWN and UP are Direction's -1 and 1. '-' is SCM_UNDEFINED, NOT zero: zero
        // would be a written "centre", and what upstream means is "nothing was
        // written, let the engraver choose".
        down.Should().Be(-1L);
        up.Should().Be(1L);
        neither.Should().Be(DefaultArgument.Instance);
    }

    [Fact]
    public void a_written_direction_reaches_the_event_and_a_dash_leaves_it_alone()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object directed = host.MakeMusic("TextScriptEvent", Spans(1)[0]);
        object undirected = host.MakeMusic("TextScriptEvent", Spans(1)[0]);

        //Act
        Action("post_event_nofinger: script_dir direction_reqd_event")(
            context, new object[] { 1L, directed }, Spans(2), Spans(1)[0]);
        Action("post_event_nofinger: script_dir direction_reqd_event")(
            context, new object[] { DefaultArgument.Instance, undirected }, Spans(2), Spans(1)[0]);

        //Assert
        ((MadeMusic)directed).Properties.Should().Contain(("direction", (object)1L));
        ((MadeMusic)undirected).Properties.Should().NotContain(p => p.Name == "direction");
    }

    [Fact]
    public void every_directed_post_event_is_restamped_with_the_whole_span()
    {
        //Arrange
        // The location includes the script_dir, which is why the event is re-stamped
        // rather than keeping the span it was made with.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object music = host.MakeMusic("TextScriptEvent", Spans(1)[0]);
        SourceSpan whole = new SourceSpan("<test>", 1, 1, 1, 9);

        //Act
        Action("post_event_nofinger: script_dir direction_less_event")(
            context, new object[] { 1L, music }, Spans(2), whole);

        //Assert
        host.MusicSpots.Should().ContainSingle();
        host.MusicSpots[0].Location.Should().Be(whole);
    }

    [Fact]
    public void a_non_music_direction_reqd_event_is_passed_through_untouched()
    {
        //Arrange
        // The null test is reachable: script_abbreviation answers SCM_UNSPECIFIED
        // when the shorthand names no post event.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("post_event_nofinger: script_dir direction_reqd_event")(
            context, new object[] { 1L, Unspecified.Instance }, Spans(2), Spans(1)[0]);

        //Assert
        result.Should().Be(Unspecified.Instance);
        host.MusicSpots.Should().BeEmpty();
    }

    // ------ fingerings ------

    [Fact]
    public void a_fingering_carries_the_written_digit()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("fingering: UNSIGNED")(
            context, new object[] { 3L }, Spans(1), Spans(1)[0]);

        //Assert
        MadeMusic fingering = result.Should().BeOfType<MadeMusic>().Which;
        fingering.Name.Should().Be("FingeringEvent");
        fingering.Properties.Should().Contain(("digit", (object)3L));
    }

    [Fact]
    public void a_fingering_takes_its_direction_unconditionally_but_only_when_one_is_written()
    {
        //Arrange
        // '^' and '_' ARE the rule here rather than a script_dir, so there is no
        // SCM_UNBNDP guard — while `-3` is a fingering with no direction at all.
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);
        object above = host.MakeMusic("FingeringEvent", Spans(1)[0]);
        object below = host.MakeMusic("FingeringEvent", Spans(1)[0]);
        object neither = host.MakeMusic("FingeringEvent", Spans(1)[0]);

        //Act
        Action("post_event_nofinger: '^' fingering")(
            context, new object[] { '^', above }, Spans(2), Spans(1)[0]);
        Action("post_event_nofinger: '_' fingering")(
            context, new object[] { '_', below }, Spans(2), Spans(1)[0]);
        Action("post_event: '-' fingering")(
            context, new object[] { '-', neither }, Spans(2), Spans(1)[0]);

        //Assert
        ((MadeMusic)above).Properties.Should().Contain(("direction", (object)1L));
        ((MadeMusic)below).Properties.Should().Contain(("direction", (object)(-1L)));
        ((MadeMusic)neither).Properties.Should().NotContain(p => p.Name == "direction");

        // All three are still located.
        host.MusicSpots.Should().HaveCount(3);
    }

    // ------ string numbers and tremolos ------

    [Fact]
    public void an_escaped_number_becomes_a_string_number_event()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("string_number_event: E_UNSIGNED")(
            context, new object[] { 5L }, Spans(1), Spans(1)[0]);

        //Assert
        MadeMusic stringNumber = result.Should().BeOfType<MadeMusic>().Which;
        stringNumber.Name.Should().Be("StringNumberEvent");
        stringNumber.Properties.Should().Contain(("string-number", (object)5L));
    }

    [Fact]
    public void a_tremolo_type_becomes_a_tremolo_event()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("direction_less_event: tremolo_type")(
            context, new object[] { 16L }, Spans(1), Spans(1)[0]);

        //Assert
        MadeMusic tremolo = result.Should().BeOfType<MadeMusic>().Which;
        tremolo.Name.Should().Be("TremoloEvent");
        tremolo.Properties.Should().Contain(("tremolo-type", (object)16L));
    }

    [Fact]
    public void an_event_identifier_passes_straight_through()
    {
        //Arrange
        ParseContext context = NewContext(new ScriptedParserHost());
        MusicObject stored = new MusicObject(Nil.Instance);

        //Act
        object result = Action("direction_less_event: EVENT_IDENTIFIER")(
            context, new object[] { stored }, Spans(1), Spans(1)[0]);

        //Assert
        result.Should().BeSameAs(stored);
    }

    // ------ text attachments ------

    [Fact]
    public void every_text_attachment_shape_makes_a_text_script_event()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        (string Identity, object Value)[] cases =
        {
            ("gen_text_def: full_markup", new Pair(Symbol.Intern("markup"), Nil.Instance)),
            ("gen_text_def: STRING", "allegro"),
            ("gen_text_def: SYMBOL", "allegro"),
        };

        foreach ((string identity, object value) in cases)
        {
            //Act
            object result = Action(identity)(
                context, new object[] { value }, Spans(1), Spans(1)[0]);

            //Assert
            // make_simple_markup is the identity upstream — a string IS a markup — so
            // the value reaches the property unchanged in all three.
            MadeMusic text = result.Should().BeOfType<MadeMusic>().Which;
            text.Name.Should().Be("TextScriptEvent");
            text.Properties.Should().Contain(("text", value));
        }
    }

    [Fact]
    public void an_embedded_scheme_text_attachment_goes_through_create_script()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object result = Action("gen_text_def: embedded_scm")(
            context, new object[] { "the-script" }, Spans(1), Spans(1)[0]);

        //Assert
        // "Could be using this for every gen_text_def but for speed": the Scheme side
        // decides what the value means.
        SyntaxMark mark = result.Should().BeOfType<SyntaxMark>().Which;
        mark.Name.Should().Be("create-script");
        mark.Arguments.AsText().Should().Equal("the-script");
    }

    // ------ script abbreviations ------

    [Fact]
    public void each_script_abbreviation_names_its_dash_identifier()
    {
        //Arrange
        ParseContext context = NewContext(new ScriptedParserHost());

        (string Identity, string Name)[] cases =
        {
            ("script_abbreviation: '^'", "Hat"),
            ("script_abbreviation: '+'", "Plus"),
            ("script_abbreviation: '-'", "Dash"),
            ("script_abbreviation: '!'", "Bang"),
            ("script_abbreviation: ANGLE_CLOSE", "Larger"),
            ("script_abbreviation: '.'", "Dot"),
            ("script_abbreviation: '_'", "Underscore"),
        };

        foreach ((string identity, string name) in cases)
        {
            //Act
            object result = Action(identity)(context, new object[] { null }, Spans(1), Spans(1)[0]);

            //Assert
            // The NAME is the lookup: `-.` gives "Dot", which finds \dashDot.
            result.Should().Be(name);
        }
    }

    [Fact]
    public void a_script_abbreviation_clones_the_dash_identifier_it_names()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        MusicObject dashDot = new MusicObject(Nil.Instance);
        host.WithMusicType(dashDot, "post-event");
        dashDot.SetProperty("articulation-type", Symbol.Intern("staccato"));
        host.Globals.Bindings[Symbol.Intern("dashDot")] = dashDot;
        ParseContext context = NewContext(host);

        //Act
        object result = Action("direction_reqd_event: script_abbreviation")(
            context, new object[] { "Dot" }, Spans(1), Spans(1)[0]);

        //Assert
        // CLONED, because the same \dashDot serves every `-.` in the file and each
        // needs its own location and direction.
        result.Should().BeOfType<MusicObject>().And.NotBeSameAs(dashDot);
        ((MusicObject)result).GetProperty("articulation-type")
            .AsText().Should().Be(Symbol.Intern("staccato"));

        // The location is deliberately NOT set here — post_event_nofinger, which knows
        // the span including the script_dir, sets it.
        host.MusicSpots.Should().BeEmpty();
    }

    [Fact]
    public void a_script_abbreviation_naming_no_post_event_is_refused()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        ParseContext context = NewContext(host);

        //Act
        object undefined = Action("direction_reqd_event: script_abbreviation")(
            context, new object[] { "Dot" }, Spans(1), Spans(1)[0]);

        // And a \dashDot that IS defined but is not a post event.
        host.Globals.Bindings[Symbol.Intern("dashDot")] = new MusicObject(Nil.Instance);
        object notAPostEvent = Action("direction_reqd_event: script_abbreviation")(
            context, new object[] { "Dot" }, Spans(1), Spans(1)[0]);

        //Assert
        undefined.Should().Be(Unspecified.Instance);
        notAPostEvent.Should().Be(Unspecified.Instance);
        host.ErrorLevel.Should().Be(1);
    }

    // ------ lyric-only post events ------

    [Fact]
    public void hyphens_and_extenders_are_refused_outside_lyric_mode_but_still_made()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost { IsLyricState = false };
        ParseContext context = NewContext(host);

        //Act
        object hyphen = Action("post_event_nofinger: HYPHEN")(
            context, new object[] { null }, Spans(1), Spans(1)[0]);
        object extender = Action("post_event_nofinger: EXTENDER")(
            context, new object[] { null }, Spans(1), Spans(1)[0]);

        //Assert
        // The diagnostic raises the error level but the event is made either way, so
        // the rest of the file still gets checked.
        host.ErrorLevel.Should().Be(1);
        ((MadeMusic)hyphen).Name.Should().Be("HyphenEvent");
        ((MadeMusic)extender).Name.Should().Be("ExtenderEvent");
    }

    [Fact]
    public void hyphens_and_extenders_are_accepted_in_lyric_mode()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost { IsLyricState = true };
        ParseContext context = NewContext(host);

        //Act
        Action("post_event_nofinger: HYPHEN")(context, new object[] { null }, Spans(1), Spans(1)[0]);
        Action("post_event_nofinger: EXTENDER")(
            context, new object[] { null }, Spans(1), Spans(1)[0]);

        //Assert
        host.ErrorLevel.Should().Be(0);
    }

    // ------ music-function post events ------

    [Fact]
    public void a_music_function_used_as_a_post_event_must_be_one()
    {
        //Arrange
        ScriptedParserHost host = new ScriptedParserHost();
        MusicObject postEvent = new MusicObject(Nil.Instance);
        host.WithMusicType(postEvent, "post-event");
        MusicObject ordinary = new MusicObject(Nil.Instance);
        ParseContext context = NewContext(host);

        //Act
        object accepted = Action("post_event_nofinger: script_dir music_function_call")(
            context, new object[] { 1L, postEvent }, Spans(2), Spans(1)[0]);
        object refused = Action("post_event_nofinger: script_dir music_function_call")(
            context, new object[] { 1L, ordinary }, Spans(2), Spans(1)[0]);

        //Assert
        accepted.Should().BeSameAs(postEvent);
        postEvent.GetProperty("direction").Should().Be(1L);
        refused.Should().Be(Unspecified.Instance);
        host.ErrorLevel.Should().Be(1);
    }

    // ------ real text ------

    [Fact]
    public void a_text_attachment_from_real_text_hangs_off_the_note_with_its_direction()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("c4^\"allegro\"");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);

        MusicObject text = Articulation(host, 0);
        text.Name.Should().Be("TextScriptEvent");
        text.GetProperty("text").AsText().Should().Be("allegro");
        text.GetProperty("direction").Should().Be(1L);
    }

    [Fact]
    public void a_fingering_from_real_text_hangs_off_the_note()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup("c4-3");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);

        MusicObject fingering = Articulation(host, 0);
        fingering.Name.Should().Be("FingeringEvent");
        fingering.GetProperty("digit").Should().Be(3L);

        // `-3` writes no direction at all, so the property was never set — and an
        // unset property reads as SCM_EOL, exactly as upstream's get_property does.
        fingering.GetProperty("direction").Should().Be(Nil.Instance);
    }

    [Fact]
    public void several_post_events_reach_the_note_in_written_order()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host)
            = Setup("c4^\"one\"_\"two\"");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);
        Articulation(host, 0).GetProperty("text").AsText().Should().Be("one");
        Articulation(host, 1).GetProperty("text").AsText().Should().Be("two");
        Articulation(host, 0).GetProperty("direction").Should().Be(1L);
        Articulation(host, 1).GetProperty("direction").Should().Be(-1L);
    }

    [Fact]
    public void a_string_number_from_real_text_hangs_off_the_note()
    {
        //Arrange
        (LalrParser parser, ModalScanner scanner, ScriptedParserHost host) = Setup("c4\\5");

        //Act
        parser.Parse(scanner, host);

        //Assert
        NoErrors(parser);

        MusicObject stringNumber = Articulation(host, 0);
        stringNumber.Name.Should().Be("StringNumberEvent");
        stringNumber.GetProperty("string-number").Should().Be(5L);
    }
}
