// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// GENERATED FILE - do not edit by hand.
// Regenerate with: python3 tools/snippetdata/gen-builtin-snippets.py
//
//was previously: frescobaldi/snippet/builtin.py
//
// The titles are the VERBATIM upstream msgids, so the i18n harvest tool
// (W-I18N) can map them to Frescobaldi's own catalogs.

using System.Collections.Generic;

namespace Fresco.Brix.Snippets;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

public static partial class BuiltinSnippets
{
    /// <summary>
    /// The 29 snippets upstream ships that are TEMPLATES.
    /// </summary>
    /// <remarks>
    /// Upstream ships 51 altogether; the 22 left out run
    /// Python code, which FR5.3 excludes:
    /// color_dialog, comment, double, last_note
    /// lowercase, markup_lines_selection, midi_tempo, next_blank_line
    /// next_blank_line_select, no_barnumbers, no_tagline, paper_a5
    /// previous_blank_line, previous_blank_line_select, quotes_d, quotes_s
    /// remove_matching_pair, removelines, staff_size, titlecase
    /// uncomment, uppercase
    /// </remarks>
    private static readonly IReadOnlyList<BuiltinSnippet> Data = new[]
    {
        new BuiltinSnippet(
            "1voice",
            "",
            "-*- name: 1v;\n\\oneVoice"),
        new BuiltinSnippet(
            "blankline",
            "Blank Line",
            "\n$CURSOR\n"),
        new BuiltinSnippet(
            "header",
            "Header Template",
            "-*- name: h; menu: blocks;\n\\header {\n  title = \"$CURSOR\"\n  composer = \"\"\n  tagline = \\markup {\n    Engraved at\n    \\simple #(strftime \"%Y-%m-%d\" (localtime (current-time)))\n    with \\with-url #\"http://lilypond.org/\"\n    \\line { LilyPond \\simple #(lilypond-version) (http://lilypond.org/) }\n  }\n}\n"),
        new BuiltinSnippet(
            "ly_version",
            "LilyPort Version",
            "-*- menu;\n\\version \"$LILYPOND_VERSION\"\n"),
        new BuiltinSnippet(
            "m22",
            "Modern 2/2 Time Signature",
            "-*- name: 22;\n\\numericTimeSignature\n\\time 2/2"),
        new BuiltinSnippet(
            "m44",
            "Modern 4/4 Time Signature",
            "-*- name: 44;\n\\numericTimeSignature\n\\time 4/4"),
        new BuiltinSnippet(
            "markup",
            "Markup",
            "-*- name: m; selection: strip;\n\\markup { $SELECTION }"),
        new BuiltinSnippet(
            "markup_column",
            "Markup column",
            "-*- name: c; selection: yes, keep, strip;\n\\column { $SELECTION }"),
        new BuiltinSnippet(
            "onceoverride",
            "",
            "-*- name: oo;\n\\once \\override "),
        new BuiltinSnippet(
            "relative",
            "Relative Music",
            "-*- name: rel;\n\\relative c$CURSOR'$ANCHOR {\n   $SELECTION\n}"),
        new BuiltinSnippet(
            "repeat",
            "Repeat",
            "-*- menu: blocks; name: rep; selection: strip; symbol: bar_repeat_start;\n\\repeat volta 2 { $SELECTION }"),
        new BuiltinSnippet(
            "repeatunfold",
            "Repeat unfold",
            "-*- menu: blocks; name: repunf; selection: strip;\n\\repeat unfold 2$CURSOR { $SELECTION }"),
        new BuiltinSnippet(
            "score",
            "",
            "-*- menu: blocks;\n\\score {\n  $SELECTION\n  \\layout {}\n  \\midi {}\n}\n"),
        new BuiltinSnippet(
            "stanza1",
            "",
            "-*- name: s1;\n\\set stanza = \"1.\"\n"),
        new BuiltinSnippet(
            "stanza2",
            "",
            "-*- name: s2;\n\\set stanza = \"2.\"\n"),
        new BuiltinSnippet(
            "stanza3",
            "",
            "-*- name: s3;\n\\set stanza = \"3.\"\n"),
        new BuiltinSnippet(
            "stanza4",
            "",
            "-*- name: s4;\n\\set stanza = \"4.\"\n"),
        new BuiltinSnippet(
            "stanza5",
            "",
            "-*- name: s5;\n\\set stanza = \"5.\"\n"),
        new BuiltinSnippet(
            "stanza6",
            "",
            "-*- name: s6;\n\\set stanza = \"6.\"\n"),
        new BuiltinSnippet(
            "tactus",
            "Tactus Time Signature (number with note)",
            "-*- name: tac;\n\\once \\override Staff.TimeSignature.style = #'numbered\n\\once \\override Staff.TimeSignature.stencil = #ly:text-interface::print\n\\once \\override Staff.TimeSignature.text = \\markup {\n  \\override #'(baseline-skip . 0.5)\n  \\column { \\number $CURSOR1$ANCHOR \\tiny \\note {2} #-.6 }\n}\n"),
        new BuiltinSnippet(
            "tagline_date_version",
            "Tagline with date and LilyPort version",
            "tagline = \\markup {\n  Engraved at\n  \\simple #(strftime \"%Y-%m-%d\" (localtime (current-time)))\n  with \\with-url #\"http://lilypond.org/\"\n  \\line { LilyPond \\simple #(lilypond-version) (http://lilypond.org/) }\n}\n"),
        new BuiltinSnippet(
            "template_blank_sheet_music_paper",
            "Blank Music Sheet",
            "-*- template; indent: no; template-run;\n\\version \"${LILYPOND_VERSION}\"\n\n{\n  \\repeat unfold 12${CURSOR}\n  {\n    s1\n    \\break\n  }\n}\n\n\\layout {\n  \\context {\n    \\Score\n    \\remove \"Bar_number_engraver\"\n  }\n  \\context {\n    \\Staff\n    \\remove \"Clef_engraver\"\n    \\remove \"Time_signature_engraver\"\n    \\remove \"Bar_engraver\"\n  }\n}\n\n\\paper {\n  indent = 0\n  ragged-last-bottom = ##f\n  top-system-spacing = #'((minimum-distance . 10))\n  last-bottom-spacing = #'((minimum-distance . 10))\n}\n\n\\header {\n  tagline = ##f\n}\n\n"),
        new BuiltinSnippet(
            "template_choir_hymn",
            "Choir Hymn",
            "-*- template; template-run;\n\\version \"$LILYPOND_VERSION\"\n\n\\header {\n  title = \"\"\n}\n\nglobal = {\n  \\time 4/4\n  \\key c \\major\n  \\tempo 4=100\n}\n\nsoprano = \\relative c'' {\n  \\global\n  $CURSORc4\n\n}\n\nalto = \\relative c' {\n  \\global\n  c4\n\n}\n\ntenor = \\relative c' {\n  \\global\n  c4\n\n}\n\nbass = \\relative c {\n  \\global\n  c4\n\n}\n\nverseOne = \\lyricmode {\n  \\set stanza = \"1.\"\n  hi\n\n}\n\nverseTwo = \\lyricmode {\n  \\set stanza = \"2.\"\n  ha\n\n}\n\nverseThree = \\lyricmode {\n  \\set stanza = \"3.\"\n  ho\n\n}\n\n\\score {\n  \\new ChoirStaff <<\n    \\new Staff \\with {\n      midiInstrument = \"choir aahs\"\n      instrumentName = \\markup \\center-column { S A }\n    } <<\n      \\new Voice = \"soprano\" { \\voiceOne \\soprano }\n      \\new Voice = \"alto\" { \\voiceTwo \\alto }\n    >>\n    \\new Lyrics \\with {\n      \\override VerticalAxisGroup.staff-affinity = #CENTER\n    } \\lyricsto \"soprano\" \\verseOne\n    \\new Lyrics \\with {\n      \\override VerticalAxisGroup.staff-affinity = #CENTER\n    } \\lyricsto \"soprano\" \\verseTwo\n    \\new Lyrics \\with {\n      \\override VerticalAxisGroup.staff-affinity = #CENTER\n    } \\lyricsto \"soprano\" \\verseThree\n    \\new Staff \\with {\n      midiInstrument = \"choir aahs\"\n      instrumentName = \\markup \\center-column { T B }\n    } <<\n      \\clef bass\n      \\new Voice = \"tenor\" { \\voiceOne \\tenor }\n      \\new Voice = \"bass\" { \\voiceTwo \\bass }\n    >>\n  >>\n  \\layout { }\n  \\midi { }\n}\n"),
        new BuiltinSnippet(
            "template_leadsheet",
            "Basic Leadsheet",
            "-*- template; template-run;\n\\version \"$LILYPOND_VERSION\"\n\n\\header {\n  title = \"\"\n}\n\nglobal = {\n  \\time 4/4\n  \\key c \\major\n  \\tempo 4=100\n}\n\nchordNames = \\chordmode {\n  \\global\n  c1\n\n}\n\nmelody = \\relative c'' {\n  \\global\n  c4 d e f\n  $CURSOR\n}\n\nwords = \\lyricmode {\n\n\n}\n\n\\score {\n  <<\n    \\new ChordNames \\chordNames\n    \\new FretBoards \\chordNames\n    \\new Staff { \\melody }\n    \\addlyrics { \\words }\n  >>\n  \\layout { }\n  \\midi { }\n}\n"),
        new BuiltinSnippet(
            "times23",
            "Tuplets",
            "-*- menu: blocks; selection: strip;\n\\tuplet 3/2 { $SELECTION }"),
        new BuiltinSnippet(
            "voice1",
            "",
            "-*- name: v1;\n\\voiceOne"),
        new BuiltinSnippet(
            "voice2",
            "",
            "-*- name: v2;\n\\voiceTwo"),
        new BuiltinSnippet(
            "voice3",
            "",
            "-*- name: v3;\n\\voiceThree"),
        new BuiltinSnippet(
            "voice4",
            "",
            "-*- name: v4;\n\\voiceFour"),
    };
}
