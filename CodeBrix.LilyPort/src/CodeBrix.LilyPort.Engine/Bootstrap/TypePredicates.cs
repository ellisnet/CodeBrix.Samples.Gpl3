// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyScheme;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The smob type predicates for every type the port actually has.
/// <para>
/// These are the 36 predicates declared by a C++ class's <c>type_p_name_</c> member
/// rather than by <c>LY_DEFINE</c> — the ones the original entry-point extraction
/// missed entirely. They are stubbed to <see langword="false"/> by
/// <see cref="EnginePrimitives.InstallStubs"/>, and that answer is CORRECT for a type
/// the port does not have: no instance can exist, so nothing can be one.
/// </para>
/// <para>
/// The moment a type IS ported, though, the same stub becomes actively WRONG, and
/// wrong in the worst way: instances exist, every type check on them fails, and the
/// failure is a "Type check for `duration' failed" programming error pointing at the
/// property rather than at the predicate. That is exactly how this was found — the
/// iterator work asked the Scheme layer to build a note, and <c>ly:duration?</c> said
/// a perfectly good duration was not one.
/// </para>
/// <para>
/// So this file is the standing obligation that goes with porting a type: port the
/// type, register its predicate here, and the fence test in
/// <c>TypePredicateTests</c> will tell you if the two ever drift apart.
/// </para>
/// </summary>
public static class TypePredicates
{
    /// <summary>
    /// The predicates whose types the port does NOT have yet, and which therefore
    /// correctly stay stubbed to <see langword="false"/>.
    /// <para>
    /// Listing them is what makes the fence test possible: every declared predicate
    /// must be either implemented below or named here, so a newly ported type cannot
    /// quietly keep a predicate that says it does not exist.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> NotYetPorted { get; } = new[]
    {
        // The score/output layer -- milestone 6's later work. Paper_score and
        // Output_def landed with first light, and Book and Score with the parser's
        // book/score rule actions (RAG3), so those moved to Ported below; the page
        // layer above them has not been reached yet.
        "ly:page-marker?",
        "ly:paper-book?",

        // The parser and lexer themselves -- Track P. Input, Source_file and
        // Music_function moved to Ported with EPG1; these two host types have not.
        "ly:lily-lexer?",
        "ly:lily-parser?",

        // Not yet demanded by anything.
        "ly:tuplet-description?",
    };

    /// <summary>Gets the predicates implemented over a really-ported type.</summary>
    public static IReadOnlyList<string> Ported { get; } = new[]
    {
        "ly:book?",
        "ly:box?",
        "ly:context?",

        // The definition types arrived with rule action group 5 (\context/\with),
        // ahead of the ly/ files that will populate them.
        "ly:context-def?",
        "ly:context-mod?",

        "ly:music-output?",
        "ly:output-def?",
        "ly:score?",

        // These four are registered by the class that owns them rather than here --
        // ProbPrimitives, RegistryPrimitives and IteratorPrimitives -- but they are
        // still declared type predicates and still have to be accounted for.
        "ly:grob-properties?",
        "ly:iterator?",
        "ly:prob?",
        "ly:regex?",
        "ly:unpure-pure-container?",

        // EPG1: source locations, the files they point into, and music functions.
        "ly:input-location?",
        "ly:music-function?",
        "ly:source-file?",

        "ly:dispatcher?",
        "ly:duration?",
        "ly:font-metric?",
        "ly:grob?",
        "ly:grob-array?",
        "ly:listener?",
        "ly:moment?",
        "ly:note-scale?",
        "ly:pitch?",
        "ly:skyline?",
        "ly:spring?",
        "ly:stencil?",
        "ly:transform?",
        "ly:translator?",
        "ly:translator-group?",
    };

    /// <summary>Registers every predicate whose type the port has, replacing its stub.</summary>
    /// <param name="interpreter">The interpreter to register into.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        interpreter.DefinePrimitive("ly:input-location?", 1, 1, a => a[0] is Origins.Input);
        interpreter.DefinePrimitive("ly:source-file?", 1, 1, a => a[0] is Origins.SourceFile);
        interpreter.DefinePrimitive("ly:music-function?", 1, 1, a => a[0] is Music.MusicFunction);

        interpreter.DefinePrimitive("ly:book?", 1, 1, a => a[0] is Book);
        interpreter.DefinePrimitive("ly:box?", 1, 1, a => a[0] is Box);
        interpreter.DefinePrimitive("ly:context?", 1, 1, a => a[0] is Context);
        interpreter.DefinePrimitive("ly:context-def?", 1, 1, a => a[0] is ContextDef);
        interpreter.DefinePrimitive("ly:context-mod?", 1, 1, a => a[0] is ContextMod);
        interpreter.DefinePrimitive("ly:dispatcher?", 1, 1, a => a[0] is Dispatcher);
        interpreter.DefinePrimitive("ly:duration?", 1, 1, a => a[0] is Duration);
        // FontMetric, not OpenTypeFont: the font a grob actually holds is a
        // ModifiedFontMetric wrapping the OTF at the requested magnification, so
        // testing the concrete reader type would deny every font in real use.
        interpreter.DefinePrimitive("ly:font-metric?", 1, 1, a => a[0] is FontMetric);
        interpreter.DefinePrimitive("ly:grob?", 1, 1, a => a[0] is Grob);
        interpreter.DefinePrimitive("ly:grob-array?", 1, 1, a => a[0] is GrobArray);
        interpreter.DefinePrimitive("ly:listener?", 1, 1, a => a[0] is Listener);
        interpreter.DefinePrimitive("ly:moment?", 1, 1, a => a[0] is Moment);

        // Music_output is Paper_score's base class upstream; Paper_score is the only
        // subclass the port has, so the predicate is exact rather than approximate.
        interpreter.DefinePrimitive("ly:music-output?", 1, 1, a => a[0] is PaperScore);
        interpreter.DefinePrimitive("ly:output-def?", 1, 1, a => a[0] is OutputDef);
        interpreter.DefinePrimitive("ly:note-scale?", 1, 1, a => a[0] is Scale);
        interpreter.DefinePrimitive("ly:pitch?", 1, 1, a => a[0] is Pitch);
        interpreter.DefinePrimitive("ly:score?", 1, 1, a => a[0] is Score);
        interpreter.DefinePrimitive("ly:skyline?", 1, 1, a => a[0] is Skyline);
        interpreter.DefinePrimitive("ly:spring?", 1, 1, a => a[0] is Spring);
        interpreter.DefinePrimitive("ly:stencil?", 1, 1, a => a[0] is Stencil);

        interpreter.DefinePrimitive("ly:transform?", 1, 1, a => a[0] is Transform);

        // Translator_group is NOT a Translator upstream -- they are separate classes,
        // and a group is not a translator. Testing them the other way round would make
        // every group answer yes to both.
        interpreter.DefinePrimitive("ly:translator?", 1, 1, a => a[0] is Translator);
        interpreter.DefinePrimitive("ly:translator-group?", 1, 1, a => a[0] is TranslatorGroup);
    }
}
