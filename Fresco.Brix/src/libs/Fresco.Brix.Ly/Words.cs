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

using System.Collections.Concurrent;
using System.Collections.Generic;

namespace Fresco.Brix.Ly; //was previously: ly/words.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.
// GENERATED from the python-ly v0.9.10 checkout by a mechanical conversion
// (each tuple emitted verbatim, in the source file's definition order);
// edit the generator, not this file, unless annotating.

/// <summary>
/// Words the tokenizer's word-list patterns and test predicates consult —
/// LilyPond keywords, music commands, markup commands, context names and the
/// rest, exactly python-ly's <c>ly/words.py</c> data.
/// </summary>
public static class Words
{
    /// <summary>The <c>lilypond_keywords</c> word list.</summary>
    public static readonly string[] LilypondKeywords =
    [
        "accepts", "alias", "book", "bookpart", "consists", "context", "defaultchild", "denies",
        "description", "etc", "header", "hide", "include", "inherit-acceptability", "language",
        "layout", "midi", "name", "omit", "once", "override", "paper", "remove", "revert",
        "score", "set", "sourcefileline", "sourcefilename", "tagGroup", "temporary", "type",
        "undo", "unset", "version", "with",
    ];

    /// <summary>The <c>lilypond_music_commands</c> word list.</summary>
    public static readonly string[] LilypondMusicCommands =
    [
        "absolute", "acciaccatura", "accidentalStyle", "addChordShape",
        "addInstrumentDefinition", "addlyrics", "addQuote", "after", "afterGrace", "aikenHeads",
        "aikenHeadsMinor", "aikenThinHeads", "aikenThinHeadsMinor", "allowBreak",
        "allowPageTurn", "allowVoltaHook", "alterBroken", "alternative", "ambitusAfter",
        "appendToTag", "applyContext", "applyMusic", "applyOutput", "appoggiatura",
        "arabicStringNumbers", "arpeggio", "arpeggioArrowDown", "arpeggioArrowUp",
        "arpeggioBracket", "arpeggioNormal", "arpeggioParenthesis", "arpeggioParenthesisDashed",
        "ascendens", "assertBeamQuant", "assertBeamSlope", "auctum", "aug", "augmentum",
        "autoAccidentals", "autoBeamOff", "autoBeamOn", "autoBreaksOff", "autoBreaksOn",
        "autoChange", "autoLineBreaksOff", "autoLineBreaksOn", "autoPageBreaksOff",
        "autoPageBreaksOn", "balloonGrobText", "balloonLengthOff", "balloonLengthOn",
        "balloonText", "bar", "barNumberCheck", "bassFigureExtendersOff",
        "bassFigureExtendersOn", "bassFigureStaffAlignmentDown",
        "bassFigureStaffAlignmentNeutral", "bassFigureStaffAlignmentUp", "beamExceptions",
        "bendAfter", "bendHold", "bendStartLevel", "blackTriangleMarkup", "bookOutputName",
        "bookOutputSuffix", "bp", "break", "breathe", "breve", "cadenzaOff", "cadenzaOn",
        "caesura", "cavum", "change", "chordmode", "chordRepeats", "chords", "clef", "cm",
        "codaMark", "compoundMeter", "compressEmptyMeasures", "compressMMRests", "context",
        "cr", "cresc", "crescHairpin", "crescTextCresc", "crossStaff", "cueClef",
        "cueClefUnset", "cueDuring", "cueDuringWithClef", "dashBang", "dashBar", "dashDash",
        "dashDot", "dashHat", "dashLarger", "dashPlus", "dashUnderscore", "deadNote",
        "deadNotesOff", "deadNotesOn", "decr", "decresc", "default", "defaultNoteHeads",
        "defaultTimeSignature", "defineBarLine", "deminutum", "denies", "deprecatedcresc",
        "deprecateddim", "deprecatedendcresc", "deprecatedenddim", "descendens", "dim",
        "dimHairpin", "dimTextDecr", "dimTextDecresc", "dimTextDim", "displayLilyMusic",
        "displayMusic", "displayScheme", "divisioMaior", "divisioMaxima", "divisioMinima",
        "dotsDown", "dotsNeutral", "dotsUp", "dropNote", "drummode", "drumPitchTable", "drums",
        "dynamicDown", "dynamicNeutral", "dynamicUp", "easyHeadsOff", "easyHeadsOn",
        "EnableGregorianDivisiones", "enablePolymeter", "endcr", "endcresc", "enddecr",
        "enddim", "endincipit", "endSkipNCs", "endSpanners", "episemFinis", "episemInitium",
        "eventChords", "expandEmptyMeasures", "expandFullBarRests", "f", "featherDurations",
        "fermataMarkup", "ff", "fff", "ffff", "fffff", "figuremode", "figures", "finalis",
        "fine", "finger", "fingeringOrientations", "fixed", "flexa", "footnote", "fp",
        "frenchChords", "fullJazzExceptions", "funkHeads", "funkHeadsMinor", "fz",
        "germanChords", "glide", "glissando", "grace", "graceSettings", "grobdescriptions",
        "harmonic", "harmonicByFret", "harmonicByRatio", "harmonicNote", "harmonicsOff",
        "harmonicsOn", "hideNotes", "hideSplitTiedTabNotes", "hideStaffSwitch", "huge",
        "ignatzekExceptionMusic", "ignatzekExceptions", "iij", "IIJ", "ij", "IJ",
        "improvisationOff", "improvisationOn", "in", "incipit", "inclinatum",
        "includePageLayoutFile", "indent", "inStaffSegno", "instrumentSwitch",
        "instrumentTransposition", "interscoreline", "inversion", "invertChords",
        "italianChords", "jump", "keepWithTag", "key", "kievanOff", "kievanOn", "killCues",
        "label", "laissezVibrer", "languageRestore", "languageSaveAndChange", "large",
        "ligature", "linea", "longa", "lyricmode", "lyrics", "lyricsto", "magnifyMusic",
        "magnifyStaff", "maininput", "maj", "majorSevenSymbol", "makeClusters",
        "makeDefaultStringTuning", "mark", "markLengthOff", "markLengthOn", "markup",
        "markuplines", "markuplist", "markupMap", "maxima", "medianChordGridStyle", "melisma",
        "melismaEnd", "mergeDifferentlyDottedOff", "mergeDifferentlyDottedOn",
        "mergeDifferentlyHeadedOff", "mergeDifferentlyHeadedOn", "mf", "mm", "modalInversion",
        "modalTranspose", "mp", "musicMap", "n", "neumeDemoLayout", "new", "newSpacingSection",
        "noBeam", "noBreak", "noPageBreak", "noPageTurn", "normalsize", "notemode",
        "numericTimeSignature", "octaveCheck", "offset", "oldaddlyrics", "oneVoice", "oriscus",
        "ottava", "override", "overrideProperty", "overrideTimeSignatureSettings", "p",
        "pageBreak", "pageTurn", "palmMute", "palmMuteOff", "palmMuteOn", "parallelMusic",
        "parenthesize", "partCombine", "partCombineApart", "partCombineAutomatic",
        "partCombineChords", "partCombineDown", "partCombineForce", "partCombineListener",
        "partCombineSoloI", "partCombineSoloII", "partCombineUnisono", "partCombineUp",
        "partial", "partialJazzExceptions", "partialJazzMusic", "pes", "phrasingSlurDashed",
        "phrasingSlurDashPattern", "phrasingSlurDotted", "phrasingSlurDown",
        "phrasingSlurHalfDashed", "phrasingSlurHalfSolid", "phrasingSlurNeutral",
        "phrasingSlurSolid", "phrasingSlurUp", "pitchedTrill", "pointAndClickOff",
        "pointAndClickOn", "pointAndClickTypes", "pp", "ppp", "pppp", "ppppp", "preBend",
        "preBendHold", "predefinedFretboardsOff", "predefinedFretboardsOn", "propertyOverride",
        "propertyRevert", "propertySet", "propertyTweak", "propertyUnset", "pt", "pushToTag",
        "quilisma", "quoteDuring", "raiseNote", "reduceChords", "relative",
        "RemoveAllEmptyStaves", "RemoveEmptyRhythmicStaffContext", "RemoveEmptyStaffContext",
        "RemoveEmptyStaves", "removeWithTag", "repeat", "repeatTie", "resetRelativeOctave",
        "responsum", "rest", "retrograde", "revert", "revertTimeSignatureSettings", "rfz",
        "rightHandFinger", "romanStringNumbers", "sacredHarpHeads", "sacredHarpHeadsMinor",
        "scaleDurations", "scoreTweak", "section", "sectionLabel", "segnoMark",
        "semiGermanChords", "set", "setDefaultDurationToQuarter", "settingsFrom", "sf", "sff",
        "sfp", "sfz", "shape", "shiftDurations", "shiftOff", "shiftOn", "shiftOnn", "shiftOnnn",
        "showSplitTiedTabNotes", "showStaffSwitch", "single", "skip", "skipNC", "skipNCs",
        "skipTypesetting", "slashedGrace", "slurDashed", "slurDashPattern", "slurDotted",
        "slurDown", "slurHalfDashed", "slurHalfSolid", "slurNeutral", "slurSolid", "slurUp",
        "small", "sostenutoOff", "sostenutoOn", "southernHarmonyHeads",
        "southernHarmonyHeadsMinor", "sp", "spacingTweaks", "spp", "staff-space",
        "staffHighlight", "startAcciaccaturaMusic", "startAppoggiaturaMusic", "startGraceMusic",
        "startGroup", "startMeasureCount", "startMeasureSpanner", "startSlashedGraceMusic",
        "startStaff", "startTextSpan", "startTrillSpan", "stemDown", "stemNeutral", "stemUp",
        "stopAcciaccaturaMusic", "stopAppoggiaturaMusic", "stopGraceMusic", "stopGroup",
        "stopMeasureCount", "stopMeasureSpanner", "stopSlashedGraceMusic", "stopStaff",
        "stopStaffHighlight", "stopTextSpan", "stopTrillSpan", "storePredefinedDiagram",
        "stringTuning", "strokeFingerOrientations", "stropha", "styledNoteHeads", "sustainOff",
        "sustainOn", "tabChordRepeats", "tabChordRepetition", "tabFullNotation", "tag", "teeny",
        "tempo", "tempoWholesPerMinute", "textEndMark", "textLengthOff", "textLengthOn",
        "textMark", "textSpannerDown", "textSpannerNeutral", "textSpannerUp", "tieDashed",
        "tieDashPattern", "tieDotted", "tieDown", "tieHalfDashed", "tieHalfSolid", "tieNeutral",
        "tieSolid", "tieUp", "time", "times", "timing", "tiny", "tocItem", "transpose",
        "transposedCueDuring", "transposition", "treCorde", "tuplet", "tupletDown",
        "tupletNeutral", "tupletSpan", "tupletUp", "tweak", "unaCorda", "unfolded",
        "unfoldRepeats", "unHideNotes", "unit", "unset", "versus", "virga", "virgula",
        "voiceFour", "voiceFourStyle", "voiceNeutralStyle", "voiceOne", "voiceOneStyle",
        "voices", "voiceThree", "voiceThreeStyle", "voiceTwo", "voiceTwoStyle", "void", "volta",
        "vshape", "walkerHeads", "walkerHeadsMinor", "whiteTriangleMarkup", "withMusicProperty",
        "xNote", "xNotesOff", "xNotesOn",
    ];

    /// <summary>The <c>articulations</c> word list.</summary>
    public static readonly string[] Articulations =
    [
        "accent", "espressivo", "marcato", "portato", "staccatissimo", "staccato", "tenuto",
    ];

    /// <summary>The <c>ornaments</c> word list.</summary>
    public static readonly string[] Ornaments =
    [
        "downmordent", "downprall", "haydnturn", "lineprall", "mordent", "prall", "pralldown",
        "prallmordent", "prallprall", "prallup", "reverseturn", "slashturn", "trill", "turn",
        "upmordent", "upprall",
    ];

    /// <summary>The <c>fermatas</c> word list.</summary>
    public static readonly string[] Fermatas =
    [
        "fermata", "henzelongfermata", "henzeshortfermata", "longfermata", "shortfermata",
        "verylongfermata", "veryshortfermata",
    ];

    /// <summary>The <c>instrument_scripts</c> word list.</summary>
    public static readonly string[] InstrumentScripts =
    [
        "downbow", "flageolet", "halfopen", "lheel", "ltoe", "open", "rheel", "rtoe",
        "snappizzicato", "stopped", "thumb", "upbow",
    ];

    /// <summary>The <c>repeat_scripts</c> word list.</summary>
    public static readonly string[] RepeatScripts =
    [
        "coda", "segno", "varcoda",
    ];

    /// <summary>The <c>ancient_scripts</c> word list.</summary>
    public static readonly string[] AncientScripts =
    [
        "accentus", "circulus", "ictus", "semicirculus", "signumcongruentiae",
    ];

    /// <summary>The <c>modes</c> word list.</summary>
    public static readonly string[] Modes =
    [
        "aeolian", "dorian", "ionian", "locrian", "lydian", "major", "minor", "mixolydian",
        "phrygian",
    ];

    /// <summary>The <c>markupcommands_nargs</c> table: markup commands grouped by argument count (index 0..5).</summary>
    public static readonly string[][] MarkupcommandsNargs =
    [
        ["coda", "doubleflat", "doublesharp", "eyeglasses", "fermata", "flat", "natural", "null", "segno", "semiflat", "semisharp", "sesquiflat", "sesquisharp", "sharp", "strut", "table-of-contents", "varcoda"],
        ["accidental", "backslashed-digit", "bold", "box", "bracket", "caps", "center-align", "center-column", "char", "circle", "column", "compound-meter", "concat", "dir-column", "discant", "draw-dashed-line", "draw-dotted-line", "draw-hline", "draw-line", "dynamic", "ellipse", "fill-line", "figured-bass", "finger", "first-visible", "fontCaps", "freeBass", "fret-diagram", "fret-diagram-terse", "fret-diagram-verbose", "fromproperty", "harp-pedal", "hbracket", "hspace", "huge", "italic", "justify", "justify-field", "justify-line", "justify-string", "large", "larger", "left-align", "left-brace", "left-column", "line", "lookup", "markalphabet", "markletter", "medium", "multi-measure-rest-by-number", "musicglyph", "normalsize", "normal-size-sub", "normal-size-super", "normal-text", "number", "oval", "overlay", "overtie", "polygon", "postscript", "property-recursive", "rest", "right-align", "right-brace", "right-column", "roman", "rounded-box", "rhythm", "sans", "score", "score-lines", "simple", "slashed-digit", "small", "smallCaps", "smaller", "stdBass", "stdBassIV", "stdBassV", "stdBassVI", "stencil", "string-lines", "sub", "super", "teeny", "text", "tie", "tied-lyric", "tiny", "transparent", "triangle", "typewriter", "underline", "undertie", "upright", "vcenter", "verbatim-file", "vspace", "whiteout", "with-true-dimensions", "wordwrap", "wordwrap-field", "wordwrap-string"],
        ["abs-fontsize", "auto-footnote", "combine", "customTabClef", "fontsize", "footnote", "fraction", "halign", "hcenter-in", "if", "lower", "magnify", "note", "on-the-fly", "override", "pad-around", "pad-markup", "pad-x", "page-link", "path", "raise", "replace", "rest-by-number", "rotate", "scale", "table", "translate", "translate-scaled", "unless", "with-color", "with-dimensions-from", "with-link", "with-outline", "with-string-transformer", "with-true-dimension", "with-url", "woodwind-diagram"],
        ["arrow-head", "beam", "draw-circle", "draw-squiggle-line", "epsfile", "filled-box", "general-align", "note-by-number", "pad-to-box", "page-ref", "with-dimension", "with-dimension-from", "with-dimensions"],
        ["pattern", "put-adjacent"],
        ["align-on-other", "fill-with-pattern"],
    ];

    /// <summary>The <c>markupcommands</c> word list.</summary>
    public static readonly string[] Markupcommands =
    [
        "coda", "doubleflat", "doublesharp", "eyeglasses", "fermata", "flat", "natural", "null",
        "segno", "semiflat", "semisharp", "sesquiflat", "sesquisharp", "sharp", "strut",
        "table-of-contents", "varcoda", "accidental", "backslashed-digit", "bold", "box",
        "bracket", "caps", "center-align", "center-column", "char", "circle", "column",
        "compound-meter", "concat", "dir-column", "discant", "draw-dashed-line",
        "draw-dotted-line", "draw-hline", "draw-line", "dynamic", "ellipse", "fill-line",
        "figured-bass", "finger", "first-visible", "fontCaps", "freeBass", "fret-diagram",
        "fret-diagram-terse", "fret-diagram-verbose", "fromproperty", "harp-pedal", "hbracket",
        "hspace", "huge", "italic", "justify", "justify-field", "justify-line",
        "justify-string", "large", "larger", "left-align", "left-brace", "left-column", "line",
        "lookup", "markalphabet", "markletter", "medium", "multi-measure-rest-by-number",
        "musicglyph", "normalsize", "normal-size-sub", "normal-size-super", "normal-text",
        "number", "oval", "overlay", "overtie", "polygon", "postscript", "property-recursive",
        "rest", "right-align", "right-brace", "right-column", "roman", "rounded-box", "rhythm",
        "sans", "score", "score-lines", "simple", "slashed-digit", "small", "smallCaps",
        "smaller", "stdBass", "stdBassIV", "stdBassV", "stdBassVI", "stencil", "string-lines",
        "sub", "super", "teeny", "text", "tie", "tied-lyric", "tiny", "transparent", "triangle",
        "typewriter", "underline", "undertie", "upright", "vcenter", "verbatim-file", "vspace",
        "whiteout", "with-true-dimensions", "wordwrap", "wordwrap-field", "wordwrap-string",
        "abs-fontsize", "auto-footnote", "combine", "customTabClef", "fontsize", "footnote",
        "fraction", "halign", "hcenter-in", "if", "lower", "magnify", "note", "on-the-fly",
        "override", "pad-around", "pad-markup", "pad-x", "page-link", "path", "raise",
        "replace", "rest-by-number", "rotate", "scale", "table", "translate",
        "translate-scaled", "unless", "with-color", "with-dimensions-from", "with-link",
        "with-outline", "with-string-transformer", "with-true-dimension", "with-url",
        "woodwind-diagram", "arrow-head", "beam", "draw-circle", "draw-squiggle-line",
        "epsfile", "filled-box", "general-align", "note-by-number", "pad-to-box", "page-ref",
        "with-dimension", "with-dimension-from", "with-dimensions", "pattern", "put-adjacent",
        "align-on-other", "fill-with-pattern",
    ];

    /// <summary>The <c>markuplistcommands</c> word list.</summary>
    public static readonly string[] Markuplistcommands =
    [
        "column-lines", "justified-lines", "map-commands", "override-lines",
        "wordwrap-internal", "wordwrap-lines", "wordwrap-string-internal",
    ];

    /// <summary>The <c>contexts</c> word list.</summary>
    public static readonly string[] Contexts =
    [
        "ChoirStaff", "ChordGrid", "ChordGridScore", "ChordNames", "CueVoice", "Devnull",
        "DrumStaff", "DrumVoice", "Dynamics", "FiguredBass", "FretBoards", "Global",
        "GrandStaff", "GregorianTranscriptionLyrics", "GregorianTranscriptionStaff",
        "GregorianTranscriptionVoice", "InternalGregorianStaff", "KievanStaff", "KievanVoice",
        "Lyrics", "MensuralStaff", "MensuralVoice", "NoteNames", "NullVoice", "OneStaff",
        "PetrucciStaff", "PetrucciVoice", "PianoStaff", "RhythmicStaff", "Score", "Staff",
        "StaffGroup", "StandaloneRhythmScore", "StandaloneRhythmStaff", "StandaloneRhythmVoice",
        "TabStaff", "TabVoice", "Timing", "VaticanaLyrics", "VaticanaStaff", "VaticanaVoice",
        "Voice",
    ];

    /// <summary>The <c>midi_instruments</c> word list.</summary>
    public static readonly string[] MidiInstruments =
    [
        "acoustic grand", "bright acoustic", "electric grand", "honky-tonk", "electric piano 1",
        "electric piano 2", "harpsichord", "clav", "celesta", "glockenspiel", "music box",
        "vibraphone", "marimba", "xylophone", "tubular bells", "dulcimer", "drawbar organ",
        "percussive organ", "rock organ", "church organ", "reed organ", "accordion",
        "harmonica", "concertina", "acoustic guitar (nylon)", "acoustic guitar (steel)",
        "electric guitar (jazz)", "electric guitar (clean)", "electric guitar (muted)",
        "overdriven guitar", "distorted guitar", "guitar harmonics", "acoustic bass",
        "electric bass (finger)", "electric bass (pick)", "fretless bass", "slap bass 1",
        "slap bass 2", "synth bass 1", "synth bass 2", "violin", "viola", "cello", "contrabass",
        "tremolo strings", "pizzicato strings", "orchestral harp", "timpani",
        "string ensemble 1", "string ensemble 2", "synthstrings 1", "synthstrings 2",
        "choir aahs", "voice oohs", "synth voice", "orchestra hit", "trumpet", "trombone",
        "tuba", "muted trumpet", "french horn", "brass section", "synthbrass 1", "synthbrass 2",
        "soprano sax", "alto sax", "tenor sax", "baritone sax", "oboe", "english horn",
        "bassoon", "clarinet", "piccolo", "flute", "recorder", "pan flute", "blown bottle",
        "shakuhachi", "whistle", "ocarina", "lead 1 (square)", "lead 2 (sawtooth)",
        "lead 3 (calliope)", "lead 4 (chiff)", "lead 5 (charang)", "lead 6 (voice)",
        "lead 7 (fifths)", "lead 8 (bass+lead)", "pad 1 (new age)", "pad 2 (warm)",
        "pad 3 (polysynth)", "pad 4 (choir)", "pad 5 (bowed)", "pad 6 (metallic)",
        "pad 7 (halo)", "pad 8 (sweep)", "fx 1 (rain)", "fx 2 (soundtrack)", "fx 3 (crystal)",
        "fx 4 (atmosphere)", "fx 5 (brightness)", "fx 6 (goblins)", "fx 7 (echoes)",
        "fx 8 (sci-fi)", "sitar", "banjo", "shamisen", "koto", "kalimba", "bagpipe", "fiddle",
        "shanai", "tinkle bell", "agogo", "steel drums", "woodblock", "taiko drum",
        "melodic tom", "synth drum", "reverse cymbal", "guitar fret noise", "breath noise",
        "seashore", "bird tweet", "telephone ring", "helicopter", "applause", "gunshot",
        "standard kit", "standard drums", "drums", "room kit", "room drums", "power kit",
        "power drums", "rock drums", "electronic kit", "electronic drums", "tr-808 kit",
        "tr-808 drums", "jazz kit", "jazz drums", "brush kit", "brush drums", "orchestra kit",
        "orchestra drums", "classical drums", "sfx kit", "sfx drums", "mt-32 kit",
        "mt-32 drums", "cm-64 kit", "cm-64 drums",
    ];

    /// <summary>The <c>string_tunings</c> word list.</summary>
    public static readonly string[] StringTunings =
    [
        "guitar-tuning", "guitar-seven-string-tuning", "guitar-drop-d-tuning",
        "guitar-drop-c-tuning", "guitar-open-g-tuning", "guitar-open-d-tuning",
        "guitar-dadgad-tuning", "guitar-lute-tuning", "guitar-asus4-tuning", "bass-tuning",
        "bass-four-string-tuning", "bass-drop-d-tuning", "bass-five-string-tuning",
        "bass-six-string-tuning", "mandolin-tuning", "banjo-open-g-tuning", "banjo-c-tuning",
        "banjo-modal-tuning", "banjo-open-d-tuning", "banjo-open-dm-tuning",
        "banjo-double-c-tuning", "banjo-double-d-tuning", "ukulele-tuning", "ukulele-d-tuning",
        "tenor-ukulele-tuning", "baritone-ukulele-tuning", "violin-tuning", "viola-tuning",
        "cello-tuning", "double-bass-tuning",
    ];

    /// <summary>The <c>scheme_values</c> word list.</summary>
    public static readonly string[] SchemeValues =
    [
        "UP", "DOWN", "LEFT", "RIGHT", "CENTER", "minimum-distance", "basic-distance",
        "padding", "stretchability",
    ];

    /// <summary>The <c>headervariables</c> word list.</summary>
    public static readonly string[] Headervariables =
    [
        "arranger", "breakbefore", "composer", "copyright", "date", "dedication", "enteredby",
        "footer", "instrument", "lastupdated", "maintainer", "maintainerEmail", "maintainerWeb",
        "meter", "moreInfo", "mutopiacomposer", "mutopiainstrument", "mutopiaopus",
        "mutopiapoet", "mutopiatitle", "opus", "piece", "poet", "source", "style",
        "subsubtitle", "subtitle", "tagline", "texidoc", "title",
    ];

    /// <summary>The <c>papervariables</c> word list.</summary>
    public static readonly string[] Papervariables =
    [
        "paper-height", "top-margin", "bottom-margin", "ragged-bottom", "ragged-last-bottom",
        "paper-width", "line-width", "left-margin", "right-margin", "check-consistency",
        "ragged-right", "ragged-last", "two-sided", "inner-margin", "outer-margin",
        "binding-offset", "horizontal-shift", "indent", "short-indent", "markup-system-spacing",
        "score-markup-spacing", "score-system-spacing", "system-system-spacing",
        "markup-markup-spacing", "last-bottom-spacing", "top-system-spacing",
        "top-markup-spacing", "max-systems-per-page", "min-systems-per-page", "system-count",
        "systems-per-page", "blank-after-score-page-force", "blank-last-page-force",
        "blank-page-force", "page-breaking", "page-breaking-system-system-spacing",
        "page-count", "auto-first-page-number", "first-page-number", "print-first-page-number",
        "print-page-number", "footnote-separator-markup", "page-spacing-weight",
        "print-all-headers", "system-separator-markup", "annotate-spacing", "bookTitleMarkup",
        "evenFooterMarkup", "evenHeaderMarkup", "oddFooterMarkup", "oddHeaderMarkup",
        "scoreTitleMarkup", "tocItemMarkup", "tocTitleMarkup", "fonts",
    ];

    /// <summary>The <c>layoutvariables</c> word list.</summary>
    public static readonly string[] Layoutvariables =
    [
        "indent", "line-width", "ragged-last", "ragged-right", "short-indent", "system-count",
    ];

    /// <summary>The <c>midivariables</c> word list.</summary>
    public static readonly string[] Midivariables =
    [
    ];

    /// <summary>The <c>repeat_types</c> word list.</summary>
    public static readonly string[] RepeatTypes =
    [
        "percent", "segno", "tremolo", "unfold", "volta",
    ];

    /// <summary>The <c>accidentalstyles</c> word list.</summary>
    public static readonly string[] Accidentalstyles =
    [
        "choral", "choral-cautionary", "default", "dodecaphonic", "dodecaphonic-no-repeat",
        "dodecaphonic-first", "forget", "modern", "modern-cautionary", "modern-voice",
        "modern-voice-cautionary", "neo-modern", "neo-modern-cautionary", "neo-modern-voice",
        "neo-modern-voice-cautionary", "no-reset", "piano", "piano-cautionary", "teaching",
        "voice",
    ];

    /// <summary>The <c>clefs_plain</c> word list.</summary>
    public static readonly string[] ClefsPlain =
    [
        "alto", "altovarC", "baritone", "baritonevarC", "baritonevarF", "bass",
        "blackmensural-c1", "blackmensural-c2", "blackmensural-c3", "blackmensural-c4",
        "blackmensural-c5", "C", "F", "french", "G", "GG", "G2", "hufnagel-do-fa",
        "hufnagel-do1", "hufnagel-do2", "hufnagel-do3", "hufnagel-fa1", "hufnagel-fa2",
        "kievan-do", "medicaea-do1", "medicaea-do2", "medicaea-do3", "medicaea-fa1",
        "medicaea-fa2", "mensural-c1", "mensural-c2", "mensural-c3", "mensural-c4",
        "mensural-c5", "mensural-f", "mensural-g", "mezzosoprano", "moderntab",
        "neomensural-c1", "neomensural-c2", "neomensural-c3", "neomensural-c4",
        "neomensural-c5", "percussion", "petrucci-c1", "petrucci-c2", "petrucci-c3",
        "petrucci-c4", "petrucci-c5", "petrucci-f", "petrucci-f2", "petrucci-f3", "petrucci-f4",
        "petrucci-f5", "petrucci-g", "petrucci-g1", "petrucci-g2", "soprano", "subbass", "tab",
        "tenor", "tenorG", "tenorvarC", "treble", "varbaritone", "varC", "varpercussion",
        "vaticana-do1", "vaticana-do2", "vaticana-do3", "vaticana-fa1", "vaticana-fa2",
        "violin",
    ];

    /// <summary>The <c>clefs</c> word list.</summary>
    public static readonly string[] Clefs =
    [
        "alto", "altovarC", "baritone", "baritonevarC", "baritonevarF", "bass",
        "blackmensural-c1", "blackmensural-c2", "blackmensural-c3", "blackmensural-c4",
        "blackmensural-c5", "C", "F", "french", "G", "GG", "G2", "hufnagel-do-fa",
        "hufnagel-do1", "hufnagel-do2", "hufnagel-do3", "hufnagel-fa1", "hufnagel-fa2",
        "kievan-do", "medicaea-do1", "medicaea-do2", "medicaea-do3", "medicaea-fa1",
        "medicaea-fa2", "mensural-c1", "mensural-c2", "mensural-c3", "mensural-c4",
        "mensural-c5", "mensural-f", "mensural-g", "mezzosoprano", "moderntab",
        "neomensural-c1", "neomensural-c2", "neomensural-c3", "neomensural-c4",
        "neomensural-c5", "percussion", "petrucci-c1", "petrucci-c2", "petrucci-c3",
        "petrucci-c4", "petrucci-c5", "petrucci-f", "petrucci-f2", "petrucci-f3", "petrucci-f4",
        "petrucci-f5", "petrucci-g", "petrucci-g1", "petrucci-g2", "soprano", "subbass", "tab",
        "tenor", "tenorG", "tenorvarC", "treble", "varbaritone", "varC", "varpercussion",
        "vaticana-do1", "vaticana-do2", "vaticana-do3", "vaticana-fa1", "vaticana-fa2",
        "violin", "bass_8", "treble_8",
    ];

    /// <summary>The <c>break_visibility</c> word list.</summary>
    public static readonly string[] BreakVisibility =
    [
        "all-invisible", "all-visible", "begin-of-line-invisible", "begin-of-line-visible",
        "center-invisible", "end-of-line-invisible", "end-of-line-visible",
    ];

    /// <summary>The <c>mark_formatters</c> word list.</summary>
    public static readonly string[] MarkFormatters =
    [
        "format-mark-alphabet", "format-mark-barnumbers", "format-mark-box-alphabet",
        "format-mark-box-barnumbers", "format-mark-box-letters", "format-mark-box-numbers",
        "format-mark-circle-alphabet", "format-mark-circle-barnumbers",
        "format-mark-circle-letters", "format-mark-circle-numbers", "format-mark-letters",
        "format-mark-numbers",
    ];

    private static readonly ConcurrentDictionary<string[], HashSet<string>> Sets
        = new ConcurrentDictionary<string[], HashSet<string>>();

    /// <summary>
    /// Membership over one of the word lists — python's <c>s in words.x</c>, backed
    /// by a per-list set so a <c>test_match</c> predicate stays cheap.
    /// </summary>
    /// <param name="list">One of this class's word lists.</param>
    /// <param name="word">The word to look for.</param>
    /// <returns>Whether the list contains the word.</returns>
    public static bool Contains(string[] list, string word)
        => Sets.GetOrAdd(list, l => new HashSet<string>(l)).Contains(word);
}
