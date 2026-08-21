// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly;
using Fresco.Brix.Ly.Data;
using Fresco.Brix.Ly.Pitching;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Completion; //was previously: frescobaldi/autocomplete/completiondata.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The fixed completion lists — everything that can be worked out from the
/// grammar and the engine's own data, with nothing read from the document.
/// </summary>
/// <remarks>
/// The lists are built once, on first use. Upstream builds them at import
/// time, which is the same thing in a language that has no static
/// initialization order to worry about; here it is a set of lazy statics so
/// that a test touching one list does not build all of them.
/// </remarks>
public static class CompletionData
{
    /// <summary>Markup commands that can stand at the top level.</summary>
    public static readonly IReadOnlyList<string> Markup = new[]
    {
        "markup", "markuplines", "markuplist",
        "pageBreak", "noPageBreak", "noPageTurn",
    };

    /// <summary>Commands that can occur almost everywhere.</summary>
    public static readonly IReadOnlyList<string> Everywhere = new[]
    {
        "pointAndClickOn", "pointAndClickOff", "include",
    };

    /// <summary>Commands that change the input mode.</summary>
    public static readonly IReadOnlyList<string> InputModes = new[]
    {
        "chords", "chordmode {", "drums", "drummode {", "figures",
        "figuremode {", "lyrics", "lyricmode {", "addlyrics {", "notemode {",
    };

    /// <summary>Commands that only occur at the global file level.</summary>
    public static readonly IReadOnlyList<string> TopLevel = new[]
    {
        "defineBarLine", "language", "version", "sourcefileline", "sourcefilename",
    };

    /// <summary>Other commands that can start a music expression.</summary>
    public static readonly IReadOnlyList<string> StartMusic = new[]
    {
        "repeat", "alternative {", "relative", "absolute", "fixed", "transpose",
        "partCombine", "keepWithTag #'", "removeWithTag #'", "new", "context",
        "with", "unfoldRepeats",
    };

    /// <summary>Tweak commands, which may be assigned at the top level.</summary>
    public static readonly IReadOnlyList<string> Tweaks = new[]
    {
        "once", "override", "revert", "set", "unset", "etc",
    };

    /// <summary>The book, bookpart and score modes.</summary>
    public static readonly IReadOnlyList<string> Modes = new[]
    {
        "book {", "bookpart {", "score {",
    };

    /// <summary>The paper, header and layout blocks.</summary>
    public static readonly IReadOnlyList<string> Blocks = new[]
    {
        "paper {", "header {", "layout {",
    };

    /// <summary>Commands used in context definitions.</summary>
    public static readonly IReadOnlyList<string> ContextCommands = new[]
    {
        "override", "consists", "remove", "RemoveEmptyStaves", "accepts",
        "alias", "defaultchild", "denies", "name",
    };

    /// <summary>The smaller set that makes sense inside <c>\with { }</c>.</summary>
    public static readonly IReadOnlyList<string> WithCommands
        = ContextCommands.Take(3).ToList();

    /// <summary>Variables that make sense at the top level.</summary>
    public static readonly IReadOnlyList<string> TopLevelVariables = new[]
    {
        "pipeSymbol", "showFirstLength", "showLastLength",
    };

    /// <summary>What belongs inside <c>\score { }</c>.</summary>
    public static readonly IReadOnlyList<string> Score = Sorted(
        Everywhere.Concat(InputModes).Concat(StartMusic)
            .Concat(Blocks.Skip(1)).Concat(new[] { "midi {" }));

    /// <summary>What belongs inside <c>\bookpart { }</c>.</summary>
    public static readonly IReadOnlyList<string> BookPart = Sorted(
        Everywhere.Concat(InputModes).Concat(Markup).Concat(StartMusic)
            .Concat(Modes.Skip(2)).Concat(Blocks));

    /// <summary>What belongs inside <c>\book { }</c>.</summary>
    public static readonly IReadOnlyList<string> Book = Sorted(
        Everywhere.Concat(InputModes).Concat(Markup).Concat(StartMusic)
            .Concat(Modes.Skip(1)).Concat(Blocks)
            .Concat(new[] { "bookOutputName", "bookOutputSuffix" }));

    /// <summary>Just <c>\markup</c>.</summary>
    public static CompletionModel LilyPondMarkup { get; }
        = CompletionModel.Of(new[] { "\\markup" });

    /// <summary>Every markup command.</summary>
    public static CompletionModel MarkupCommands => _markupCommands
        ??= CompletionModel.OfCommands(Sorted(Words.Markupcommands));

    /// <summary>The header variables.</summary>
    /// <remarks>Upstream sorts these on their first THREE characters, which
    /// keeps the <c>piece</c>/<c>poet</c> and <c>title</c>/<c>subtitle</c>
    /// families together rather than strictly alphabetical.</remarks>
    public static CompletionModel HeaderVariables => _headerVariables
        ??= CompletionModel.OfVariables(
            Words.Headervariables
                .OrderBy(i => i.Length <= 3 ? i : i.Substring(0, 3),
                    StringComparer.Ordinal)
                .ToList());

    /// <summary>The paper variables.</summary>
    public static CompletionModel PaperVariables => _paperVariables
        ??= CompletionModel.OfVariables(Sorted(Words.Papervariables));

    /// <summary>What belongs inside <c>\layout { }</c>.</summary>
    public static CompletionModel LayoutVariables => _layoutVariables
        ??= CompletionModel.OfCommandsOrVariables(new[]
        {
            "\\context {", "\\override", "\\set", "\\hide", "\\omit",
            "\\accidentalStyle",
        }.Concat(Sorted(Words.Layoutvariables)));

    /// <summary>What belongs inside <c>\midi { }</c>.</summary>
    public static CompletionModel MidiVariables => _midiVariables
        ??= CompletionModel.OfCommandsOrVariables(new[]
        {
            "\\context {", "\\override", "\\set", "\\tempo",
        }.Concat(Sorted(Words.Midivariables)));

    /// <summary>The context names.</summary>
    public static CompletionModel Contexts => _contexts
        ??= CompletionModel.Of(Sorted(Words.Contexts));

    /// <summary>The grob names.</summary>
    public static CompletionModel Grobs => _grobs
        ??= CompletionModel.Of(LyData.Grobs());

    /// <summary>The context and grob names together.</summary>
    public static CompletionModel ContextsAndGrobs => _contextsAndGrobs
        ??= CompletionModel.Of(Sorted(Words.Contexts).Concat(LyData.Grobs()));

    /// <summary>The context properties.</summary>
    public static CompletionModel ContextProperties => _contextProperties
        ??= CompletionModel.Of(LyData.ContextProperties());

    /// <summary>The context names and properties together.</summary>
    public static CompletionModel ContextsAndProperties => _contextsAndProperties
        ??= CompletionModel.Of(
            Sorted(Words.Contexts).Concat(LyData.ContextProperties()));

    /// <summary>What belongs inside a <c>\context { }</c> block.</summary>
    public static CompletionModel ContextContents => _contextContents
        ??= CompletionModel.OfCommandsOrVariables(Sorted(
            Words.Contexts.Select(Command)
                .Concat(LyData.ContextProperties())
                .Concat(ContextCommands.Select(Command))));

    /// <summary>What belongs inside a <c>\with { }</c> block.</summary>
    public static CompletionModel WithContents => _withContents
        ??= CompletionModel.OfCommandsOrVariables(Sorted(
            LyData.ContextProperties().Concat(WithCommands.Select(Command))));

    /// <summary>What belongs at the top level of a document.</summary>
    public static CompletionModel TopLevelContents => _topLevelContents
        ??= CompletionModel.OfCommandsOrVariables(Sorted(
            TopLevel.Concat(Everywhere).Concat(InputModes).Concat(Markup)
                .Concat(StartMusic).Concat(Tweaks).Concat(Modes).Concat(Blocks)
                .Select(Command)
                .Concat(TopLevelVariables)));

    /// <summary>The engraver names.</summary>
    public static CompletionModel Engravers => _engravers
        ??= CompletionModel.Of(LyData.Engravers());

    /// <summary>Every grob property, as a scheme symbol.</summary>
    public static CompletionModel AllGrobProperties => _allGrobProperties
        ??= CompletionModel.OfSchemeSymbols(LyData.AllGrobProperties(), true);

    /// <summary>Every grob property and grob name.</summary>
    public static CompletionModel AllGrobPropertiesAndGrobNames
        => _allGrobPropertiesAndNames ??= CompletionModel.Of(
            LyData.AllGrobProperties().Concat(LyData.Grobs()));

    /// <summary>
    /// The properties <c>\markup \override</c> understands — the union of the
    /// three interfaces the LilyPond documentation names for it.
    /// </summary>
    public static CompletionModel MarkupProperties => _markupProperties
        ??= CompletionModel.Of(Sorted(new[]
        {
            "font-interface", "text-interface",
            "instrument-specific-markup-interface",
        }.SelectMany(LyData.GrobInterfaceProperties).Distinct()));

    /// <summary>The key modes.</summary>
    public static CompletionModel PitchModes => _pitchModes
        ??= CompletionModel.OfCommands(Words.Modes);

    /// <summary>The clef names.</summary>
    public static CompletionModel Clefs => _clefs
        ??= CompletionModel.Of(Words.ClefsPlain);

    /// <summary>The accidental styles.</summary>
    public static CompletionModel AccidentalStyles => _accidentalStyles
        ??= CompletionModel.Of(Words.Accidentalstyles);

    /// <summary>The accidental styles and the contexts they can apply to.</summary>
    public static CompletionModel AccidentalStylesAndContexts
        => _accidentalStylesAndContexts ??= CompletionModel.Of(
            Words.Contexts.Concat(Words.Accidentalstyles));

    /// <summary>The repeat types.</summary>
    public static CompletionModel RepeatTypes => _repeatTypes
        ??= CompletionModel.Of(Words.RepeatTypes);

    /// <summary>The music-font glyph names.</summary>
    public static CompletionModel MusicGlyphs => _musicGlyphs
        ??= CompletionModel.Of(LyData.MusicGlyphs());

    /// <summary>The MIDI instrument names.</summary>
    public static CompletionModel MidiInstruments => _midiInstruments
        ??= CompletionModel.Of(Words.MidiInstruments);

    /// <summary>The note-name language names.</summary>
    public static CompletionModel LanguageNames => _languageNames
        ??= CompletionModel.Of(Sorted(Pitches.Languages));

    /// <summary>Gets one grob's properties.</summary>
    /// <param name="grob">The grob name.</param>
    /// <param name="hashQuote">Whether to show them as <c>#'</c> symbols.</param>
    /// <returns>The model.</returns>
    public static CompletionModel GrobProperties(string grob, bool hashQuote = true)
        => CompletionModel.OfSchemeSymbols(LyData.GrobProperties(grob), hashQuote);

    private static CompletionModel _markupCommands;
    private static CompletionModel _headerVariables;
    private static CompletionModel _paperVariables;
    private static CompletionModel _layoutVariables;
    private static CompletionModel _midiVariables;
    private static CompletionModel _contexts;
    private static CompletionModel _grobs;
    private static CompletionModel _contextsAndGrobs;
    private static CompletionModel _contextProperties;
    private static CompletionModel _contextsAndProperties;
    private static CompletionModel _contextContents;
    private static CompletionModel _withContents;
    private static CompletionModel _topLevelContents;
    private static CompletionModel _engravers;
    private static CompletionModel _allGrobProperties;
    private static CompletionModel _allGrobPropertiesAndNames;
    private static CompletionModel _markupProperties;
    private static CompletionModel _pitchModes;
    private static CompletionModel _clefs;
    private static CompletionModel _accidentalStyles;
    private static CompletionModel _accidentalStylesAndContexts;
    private static CompletionModel _repeatTypes;
    private static CompletionModel _musicGlyphs;
    private static CompletionModel _midiInstruments;
    private static CompletionModel _languageNames;

    /// <summary>Prepends a backslash — upstream's <c>util.command</c>.</summary>
    /// <param name="word">The word.</param>
    /// <returns>The command.</returns>
    public static string Command(string word) => "\\" + word;

    private static IReadOnlyList<string> Sorted(IEnumerable<string> words)
        => words.OrderBy(w => w, StringComparer.Ordinal).ToList();
}
