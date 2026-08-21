// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Ly;
using Fresco.Brix.Ly.Pitching;
using Fresco.Brix.Services;
using Fresco.Brix.Tools;
using SilverAssertions;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>Changing the pitches of the music.</summary>
public class PitchToolsTests
{
    [Fact]
    public void two_pitches_are_read_in_the_documents_language()
    {
        //Arrange, Act
        IReadOnlyList<Pitch> pitches = PitchTools.ReadPitches("c e", "nederlands");

        //Assert
        pitches.Count.Should().Be(2);
        pitches[0].Note.Should().Be(0);
        pitches[1].Note.Should().Be(2);
    }

    [Fact]
    public void the_octave_marks_are_read_too()
    {
        //Arrange, Act
        IReadOnlyList<Pitch> pitches = PitchTools.ReadPitches("c c''", "nederlands");

        //Assert
        pitches[0].Octave.Should().Be(0);
        pitches[1].Octave.Should().Be(2);
    }

    [Fact]
    public void a_name_that_is_no_pitch_is_skipped()
    {
        //Arrange, Act — upstream's readpitches keeps only what the language's
        //reader answers for, so the dialog's validator counts pitches, not words.
        PitchTools.IsTransposeInput("c zz", "nederlands").Should().BeFalse();
        PitchTools.IsTransposeInput("c d", "nederlands").Should().BeTrue();
        PitchTools.IsTransposeInput("c d e", "nederlands").Should().BeFalse();
    }

    [Fact]
    public void the_language_decides_what_a_name_means()
    {
        //Arrange, Act — "ees" is E flat in nederlands and no name at all in
        //english. ("es" is a name in both, and means opposite things: E flat
        //in nederlands, E sharp in english — which is exactly why a document's
        //language has to be read before its pitches are.)
        IReadOnlyList<Pitch> dutchFlat = PitchTools.ReadPitches("ees", "nederlands");
        IReadOnlyList<Pitch> englishFlat = PitchTools.ReadPitches("ees", "english");
        IReadOnlyList<Pitch> dutchEs = PitchTools.ReadPitches("es", "nederlands");
        IReadOnlyList<Pitch> englishEs = PitchTools.ReadPitches("es", "english");

        //Assert
        dutchFlat.Count.Should().Be(1);
        englishFlat.Count.Should().Be(0);
        dutchEs[0].Alter.Should().Be(new Fraction(-1, 2));
        englishEs[0].Alter.Should().Be(new Fraction(1, 2));
    }

    [Fact]
    public void a_modal_transpose_needs_a_number_and_a_key()
    {
        //Arrange, Act, Assert
        PitchTools.IsModalTransposeInput("5 F").Should().BeTrue();
        PitchTools.IsModalTransposeInput("-2 Bb").Should().BeTrue();
        PitchTools.IsModalTransposeInput("5").Should().BeFalse();
        PitchTools.IsModalTransposeInput("F 5").Should().BeFalse();
        PitchTools.IsModalTransposeInput("5 H").Should().BeFalse();
    }

    [Fact]
    public void a_mode_shift_needs_exactly_one_key()
    {
        //Arrange, Act, Assert — upstream lower-cases first, so a capital works.
        PitchTools.IsModeShiftKey("D", "nederlands").Should().BeTrue();
        PitchTools.IsModeShiftKey("d e", "nederlands").Should().BeFalse();
        PitchTools.IsModeShiftKey(string.Empty, "nederlands").Should().BeFalse();
    }

    [Fact]
    public void the_modes_are_upstreams_fifteen_sorted()
    {
        //Arrange, Act, Assert
        PitchModes.Names.Count.Should().Be(15);
        PitchModes.Names[0].Should().Be("Diminished (octatonic)");
        PitchModes.Names[^1].Should().Be("Yo (pentatonic)");
        PitchModes.Scale("Major").Count.Should().Be(7);
        PitchModes.Scale("Yo (pentatonic)").Count.Should().Be(5);
        PitchModes.Scale("no such mode").Should().BeNull();
    }

    [Fact]
    public void transposing_rewrites_the_notes()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\relative c' { c4 d e f }\n");

        //Act — c to d, which is up a whole tone.
        string failed = PitchTools.Transpose(
            document,
            PitchTools.TransposerFor("c d", "nederlands"),
            0,
            0);

        //Assert
        failed.Should().BeNull();
        document.Text.Should().Be("\\relative c' { d4 e fis g }\n");
    }

    [Fact]
    public void only_the_selection_is_transposed_when_there_is_one()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "music = { c4 d }\nother = { c4 d }\n");
        int start = document.Text.IndexOf("{");
        int end = document.Text.IndexOf("}") + 1;

        //Act
        PitchTools.Transpose(
            document, PitchTools.TransposerFor("c d", "nederlands"), start, end);

        //Assert
        document.Text.Should().Be("music = { d4 e }\nother = { c4 d }\n");
    }

    [Fact]
    public void a_transposition_the_language_cannot_spell_is_reported()
    {
        //Arrange — svenska has names for flats and sharps but none for
        //quarter tones, and a quarter-tone transposer produces them.
        //(english is not the example: its qs/qf names cover them.)
        EditorDocument document = ToolDocument.Open(
            "\\language \"svenska\"\n\\relative c' { c4 d }\n");
        Transposer quarterTone = new Transposer(
            new Pitch(0), new Pitch(0, new Fraction(1, 4)));

        //Act
        string failed = PitchTools.Transpose(document, quarterTone, 0, 0);

        //Assert
        failed.Should().Be("svenska");
    }

    [Fact]
    public void relative_becomes_absolute()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\relative c' { c4 d e f }\n");

        //Act
        PitchTools.RelativeToAbsolute(document, 0, 0, firstPitchAbsolute: false);

        //Assert
        document.Text.Should().Be("{ c'4 d' e' f' }\n");
    }

    [Fact]
    public void absolute_becomes_relative_with_a_startpitch()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("{ c'4 d' e' f' }\n");

        //Act
        PitchTools.AbsoluteToRelative(
            document, 0, 0, writeStartPitch: true, firstPitchAbsolute: false);

        //Assert
        document.Text.Should().Be("\\relative c' { c4 d e f }\n");
    }

    [Fact]
    public void absolute_becomes_relative_without_one_when_asked()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("{ c'4 d' e' f' }\n");

        //Act
        PitchTools.AbsoluteToRelative(
            document, 0, 0, writeStartPitch: false, firstPitchAbsolute: true);

        //Assert
        document.Text.Should().Be("\\relative { c'4 d e f }\n");
    }

    [Fact]
    public void changing_the_language_rewrites_the_names_and_the_command()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\language \"nederlands\"\n\\relative c' { c4 cis d }\n");

        //Act
        LanguageChange result = PitchTools.ChangeLanguage(document, "english", 0, 0);

        //Assert
        result.Should().Be(LanguageChange.Changed);
        document.Text.Should().Be(
            "\\language \"english\"\n\\relative c' { c4 cs d }\n");
    }

    [Fact]
    public void a_document_with_no_language_command_gains_one()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\version \"2.24.0\"\n\\relative c' { c4 cis d }\n");

        //Act
        LanguageChange result = PitchTools.ChangeLanguage(document, "english", 0, 0);

        //Assert
        result.Should().Be(LanguageChange.CommandInserted);
        document.Text.Should().Contain("\\language \"english\"");
        document.Text.Should().Contain("cs");
    }

    [Fact]
    public void a_selection_with_no_language_command_asks_the_user_to_add_one()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\version \"2.24.0\"\nmusic = { cis4 d }\n");
        int start = document.Text.IndexOf("{");
        int end = document.Text.IndexOf("}") + 1;

        //Act
        LanguageChange result
            = PitchTools.ChangeLanguage(document, "english", start, end);

        //Assert — the notes changed, but nothing says so in the file.
        result.Should().Be(LanguageChange.CommandNeeded);
        document.Text.Should().Contain("cs4");
        document.Text.Should().NotContain("\\language");
    }

    [Fact]
    public void the_first_pitch_rule_follows_the_documents_version()
    {
        //Arrange — LilyPond changed this at 2.18, and the port reads the
        //DOCUMENT's declared version rather than the engine's.
        EditorDocument old = ToolDocument.Open("\\version \"2.14.0\"\n");
        EditorDocument recent = ToolDocument.Open("\\version \"2.24.0\"\n");

        //Act, Assert
        PitchTools.FirstPitchAbsolute(old, false).Should().BeFalse();
        PitchTools.FirstPitchAbsolute(recent, false).Should().BeTrue();
        PitchTools.FirstPitchAbsolute(old, true).Should().BeTrue();
    }

    [Fact]
    public void a_document_says_which_pitch_language_it_is_in()
    {
        //Arrange, Act, Assert
        PitchTools.LanguageOf(ToolDocument.Open("\\language \"english\"\n"))
            .Should().Be("english");
        PitchTools.LanguageOf(ToolDocument.Open("{ c4 }\n"))
            .Should().Be("nederlands");
    }
}

/// <summary>Changing the durations of the music.</summary>
public class RhythmToolsTests
{
    [Fact]
    public void the_durations_double()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("{ c4 d8 e16 }\n");

        //Act
        RhythmTools.Double(ToolDocument.Range(document));

        //Assert
        document.Text.Should().Be("{ c2 d4 e8 }\n");
    }

    [Fact]
    public void the_durations_halve()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("{ c4 d8 e16 }\n");

        //Act
        RhythmTools.Halve(ToolDocument.Range(document));

        //Assert
        document.Text.Should().Be("{ c8 d16 e32 }\n");
    }

    [Fact]
    public void a_dot_goes_on_and_comes_off()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("{ c4 d4 }\n");

        //Act
        RhythmTools.Dot(ToolDocument.Range(document));
        string dotted = document.Text;
        RhythmTools.Undot(ToolDocument.Range(document));

        //Assert
        dotted.Should().Be("{ c4. d4. }\n");
        document.Text.Should().Be("{ c4 d4 }\n");
    }

    [Fact]
    public void the_scalings_go()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("{ c4*2 d4*2/3 }\n");

        //Act
        RhythmTools.RemoveScaling(ToolDocument.Range(document));

        //Assert
        document.Text.Should().Be("{ c4 d4 }\n");
    }

    [Fact]
    public void only_the_fractional_scalings_go_when_asked()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("{ c4*2 d4*2/3 }\n");

        //Act
        RhythmTools.RemoveFractionScaling(ToolDocument.Range(document));

        //Assert
        document.Text.Should().Be("{ c4*2 d4 }\n");
    }

    [Fact]
    public void the_durations_go_entirely()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("{ c4 d8 e16 }\n");

        //Act
        RhythmTools.Remove(ToolDocument.Range(document));

        //Assert
        document.Text.Should().Be("{ c d e }\n");
    }

    [Fact]
    public void a_repeated_duration_becomes_implicit_and_explicit_again()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("{ c4 d4 e4 }\n");

        //Act
        RhythmTools.Implicit(ToolDocument.Range(document));
        string implicitText = document.Text;
        RhythmTools.Explicit(ToolDocument.Range(document));

        //Assert
        implicitText.Should().Be("{ c4 d e }\n");
        document.Text.Should().Be("{ c4 d4 e4 }\n");
    }

    [Fact]
    public void making_implicit_per_line_keeps_the_first_of_each_line()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("{ c4 d4\n  e4 f4 }\n");

        //Act
        RhythmTools.ImplicitPerLine(ToolDocument.Range(document));

        //Assert
        document.Text.Should().Be("{ c4 d\n  e4 f }\n");
    }

    [Fact]
    public void a_typed_rhythm_is_written_over_the_notes_and_remembered()
    {
        //Arrange
        RhythmTools.Reset();
        EditorDocument document = ToolDocument.Open("{ c d e f }\n");

        //Act
        RhythmTools.Apply(ToolDocument.Range(document), "4 8 8");

        //Assert — the rhythm repeats when the music outlasts it, and a
        //duration equal to the one before it is not written again, which is
        //why the third note comes out bare.
        document.Text.Should().Be("{ c4 d8 e f4 }\n");
        RhythmTools.TypedRhythms.Count.Should().Be(1);
        RhythmTools.TypedRhythms[0].Should().Be("4 8 8");
    }

    [Fact]
    public void a_rhythm_is_copied_from_one_place_and_pasted_in_another()
    {
        //Arrange
        RhythmTools.Reset();
        EditorDocument source = ToolDocument.Open("{ c4 d8 e8 }\n");
        EditorDocument target = ToolDocument.Open("{ g a b }\n");

        //Act
        RhythmTools.Copy(ToolDocument.Range(source));
        RhythmTools.Paste(ToolDocument.Range(target));

        //Assert
        RhythmTools.CopiedRhythm.Count.Should().Be(3);
        target.Text.Should().Be("{ g4 a8 b }\n");
    }

    [Fact]
    public void every_rhythm_command_needs_a_selection()
    {
        //Arrange, Act, Assert — upstream turns the whole collection off
        //together, so the list the window walks has to be the whole list.
        RhythmActions actions = new RhythmActions();
        RhythmActions.SelectionActionNames.Count.Should().Be(13);
        foreach (var name in RhythmActions.SelectionActionNames)
        {
            actions.Action(name).Should().NotBeNull();
        }

        actions.Actions.Count.Should().Be(RhythmActions.SelectionActionNames.Count);
    }
}

/// <summary>Swapping one kind of rest for another.</summary>
public class RestToolsTests
{
    [Fact]
    public void full_measure_rests_become_spacers()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("{ R1 R1*2 c4 }\n");

        //Act
        RestTools.FullMeasureRestToSpacer(ToolDocument.Range(document));

        //Assert
        document.Text.Should().Be("{ s1 s1*2 c4 }\n");
    }

    [Fact]
    public void spacers_become_full_measure_rests()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("{ s1 s1*2 c4 }\n");

        //Act
        RestTools.SpacerToFullMeasureRest(ToolDocument.Range(document));

        //Assert
        document.Text.Should().Be("{ R1 R1*2 c4 }\n");
    }

    [Fact]
    public void a_positioned_rest_becomes_a_plain_one()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("{ c4\\rest d4 }\n");

        //Act
        RestTools.PositionedRestToRest(ToolDocument.Range(document));

        //Assert — the note, its duration and the \rest are ONE thing to
        //ly.rests, and all three are replaced by the bare r. Upstream answers
        //the same, which is what makes this the right expectation rather than
        //the tidier-looking "r4".
        document.Text.Should().Be("{ r d4 }\n");
    }
}

/// <summary>Hyphenating and de-hyphenating lyrics.</summary>
public class LyricsToolsTests
{
    [Fact]
    public void the_words_of_a_lyrics_block_are_found()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\lyricmode { Als ik het brood breek }\n");

        //Act
        IReadOnlyList<LyricWord> words = LyricsTools.FindWords(document, 0, 0);

        //Assert
        string.Join(" ", words.Select(w => w.Text))
            .Should().Be("Als ik het brood breek");
        document.Text.Substring(words[0].Start, words[0].End - words[0].Start)
            .Should().Be("Als");
    }

    [Fact]
    public void a_selection_that_is_not_a_lyrics_block_yet_is_read_as_one()
    {
        //Arrange — upstream's second attempt: the user typed the words and has
        //not wrapped them in \lyricmode.
        EditorDocument document = ToolDocument.Open("Als ik het brood breek\n");

        //Act
        IReadOnlyList<LyricWord> words = LyricsTools.FindWords(
            document, 0, document.Text.Length);

        //Assert
        words.Count.Should().Be(5);
        words[4].Text.Should().Be("breek");
    }

    [Fact]
    public void the_music_of_a_document_is_not_taken_for_lyrics()
    {
        //Arrange — with no selection only the first attempt runs, so a
        //document of pure music offers nothing to hyphenate.
        EditorDocument document = ToolDocument.Open("\\relative c' { c4 d e f }\n");

        //Act
        IReadOnlyList<LyricWord> words = LyricsTools.FindWords(document, 0, 0);

        //Assert
        words.Count.Should().Be(0);
    }

    [Fact]
    public void the_words_are_hyphenated_with_the_lyric_hyphen()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\lyricmode { lettergrepen }\n");
        Editor.Hyphenator hyphenator = new Editor.Hyphenator(
            System.IO.Path.Combine(
                HyphenDictionaries.BundledDirectory, "hyph_nl_NL.dic"));

        //Act
        int changed = LyricsTools.Hyphenate(
            document, LyricsTools.FindWords(document, 0, 0), hyphenator);

        //Assert
        changed.Should().Be(1);
        document.Text.Should().Be(
            "\\lyricmode { let -- ter -- gre -- pen }\n");
    }

    [Fact]
    public void several_words_are_hyphenated_in_one_pass()
    {
        //Arrange — the edits are applied in document order while the text is
        //growing under them, so this is the case that catches a port that
        //forgets to let the document track its own positions.
        EditorDocument document = ToolDocument.Open(
            "\\lyricmode { lettergrepen woordafbreking }\n");
        Editor.Hyphenator hyphenator = new Editor.Hyphenator(
            System.IO.Path.Combine(
                HyphenDictionaries.BundledDirectory, "hyph_nl_NL.dic"));

        //Act
        LyricsTools.Hyphenate(
            document, LyricsTools.FindWords(document, 0, 0), hyphenator);

        //Assert
        document.Text.Should().Be(
            "\\lyricmode { let -- ter -- gre -- pen woord -- af -- bre -- king }\n");
    }

    [Fact]
    public void the_hyphenation_comes_back_out()
    {
        //Arrange, Act, Assert
        LyricsTools.RemoveHyphens("let -- ter -- gre -- pen")
            .Should().Be("lettergrepen");
        LyricsTools.RemoveHyphens("aaa __ bbb").Should().Be("aaa bbb");
        LyricsTools.RemoveHyphens("aaa _ _ bbb").Should().Be("aaa bbb");
        LyricsTools.RemoveHyphens("a_b").Should().Be("a b");
        LyricsTools.RemoveHyphens("a~b").Should().Be("a b");
    }

    [Fact]
    public void text_with_no_hyphenation_is_left_alone()
    {
        //Arrange, Act, Assert — upstream's own cheap test, and it is why an
        //extender on its own is not touched.
        LyricsTools.HasHyphens("aaa bbb").Should().BeFalse();
        LyricsTools.HasHyphens("aaa -- bbb").Should().BeTrue();
    }
}

/// <summary>Tidying the whitespace without touching the music.</summary>
public class ReformattingTests
{
    [Fact]
    public void the_trailing_whitespace_goes()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("{ c4   \n  d4\t\n}\n");

        //Act
        Reformatting.RemoveTrailingWhitespace(document, 0, 0);

        //Assert
        document.Text.Should().Be("{ c4\n  d4\n}\n");
    }

    [Fact]
    public void a_brace_that_is_not_closed_on_its_line_gets_a_newline()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("music = { c4 d4\ne4 f4 }\n");

        //Act
        Reformatting.Reformat(document, null, 0, 0);

        //Assert
        document.Text.Should().Be("music = {\n  c4 d4\n  e4 f4\n}\n");
    }

    [Fact]
    public void a_block_closed_on_the_same_line_is_left_alone()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("music = { c4 d4 }\n");

        //Act
        Reformatting.Reformat(document, null, 0, 0);

        //Assert
        document.Text.Should().Be("music = { c4 d4 }\n");
    }
}

/// <summary>The commands themselves: names, texts and shortcuts.</summary>
public class MusicToolActionsTests
{
    [Fact]
    public void the_pitch_languages_are_the_eleven_sorted()
    {
        //Arrange, Act, Assert
        PitchActions.Languages.Count.Should().Be(11);
        PitchActions.Languages[0].Should().Be("catalan");
        PitchActions.Languages[^1].Should().Be("vlaams");
    }

    [Fact]
    public void hyphenate_keeps_upstreams_shortcut()
    {
        //Arrange
        LyricsActions actions = new LyricsActions();

        //Act, Assert — Ctrl+L, and a shortcut string that does not parse is
        //silently dropped (board trap 37), so this asserts it survived.
        actions.LyricsHyphenate.Shortcuts.Count.Should().Be(1);
        actions.LyricsHyphenate.Shortcuts[0].ToString().Should().Be("Ctrl+L");
    }

    [Fact]
    public void every_music_command_has_a_text()
    {
        //Arrange
        List<Commands.ActionCollection> collections = new List<Commands.ActionCollection>
        {
            new PitchActions(), new RestActions(), new RhythmActions(),
            new LyricsActions(),
        };

        //Act
        List<string> untitled = collections
            .SelectMany(c => c.Actions.Values)
            .Where(a => string.IsNullOrEmpty(a.Text))
            .Select(a => a.Name)
            .ToList();

        //Assert
        string.Join(",", untitled).Should().Be(string.Empty);
    }

    [Fact]
    public void no_music_command_names_the_engine_in_its_chrome()
    {
        //Arrange — FR13: menus, tooltips and picker labels never say
        //"LilyPond", and standing rule 5 keeps this application from
        //presenting as Frescobaldi.
        List<Commands.ActionCollection> collections = new List<Commands.ActionCollection>
        {
            new PitchActions(), new RestActions(), new RhythmActions(),
            new LyricsActions(),
        };

        //Act
        List<string> offenders = collections
            .SelectMany(c => c.Actions.Values)
            .Where(a => Names(a.Text) || Names(a.ToolTip))
            .Select(a => a.Name)
            .ToList();

        //Assert
        string.Join(",", offenders).Should().Be(string.Empty);

        static bool Names(string text)
            => text != null
                && (text.Contains("LilyPond", System.StringComparison.Ordinal)
                    || text.Contains("Frescobaldi", System.StringComparison.Ordinal));
    }
}
