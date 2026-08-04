// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The <c>ly:*-iterator::constructor</c> entry points, and the iterator accessors
/// <c>scm/</c> reaches for.
/// <para>
/// Upstream declares each constructor with the <c>IMPLEMENT_CTOR_CALLBACK</c> macro,
/// which makes a zero-argument Scheme procedure returning a fresh iterator of one
/// class. <c>scm/define-music-types.scm</c> then stores one of those procedures in
/// each music type's <c>iterator-ctor</c> property, and
/// <c>Music_iterator::create_iterator</c> calls it.
/// </para>
/// <para>
/// Registering them as real primitives is what keeps the port on upstream's own
/// dispatch path rather than on a lookup table of our own: a music type whose
/// iterator is not ported yet simply has no constructor registered, and falls through
/// to exactly the defaults upstream falls through to. That is why the unported types
/// below are listed explicitly rather than silently omitted — the list IS the
/// remaining worklist for this family.
/// </para>
/// <para>
/// New-in-family code; the derivation is recorded in <c>THIRD-PARTY-NOTICES.txt</c>.
/// </para>
/// </summary>
public static class IteratorPrimitives
{
    /// <summary>
    /// The music-iterator classes ported so far, keyed by the Scheme name of their
    /// constructor entry point.
    /// </summary>
    private static readonly Dictionary<string, Func<MusicIterator>> Constructors
        = new Dictionary<string, Func<MusicIterator>>(StringComparer.Ordinal)
        {
            ["ly:music-iterator::constructor"] = () => new MusicIterator(),
            ["ly:simple-music-iterator::constructor"] = () => new SimpleMusicIterator(),
            ["ly:event-iterator::constructor"] = () => new EventIterator(),
            ["ly:rhythmic-music-iterator::constructor"] = () => new RhythmicMusicIterator(),
            ["ly:event-chord-iterator::constructor"] = () => new EventChordIterator(),
            ["ly:music-wrapper-iterator::constructor"] = () => new MusicWrapperIterator(),
            ["ly:sequential-iterator::constructor"] = () => new SequentialIterator(),
            ["ly:simultaneous-music-iterator::constructor"] = () => new SimultaneousMusicIterator(),
        };

    /// <summary>
    /// The iterator constructors upstream declares that this port has NOT reached yet.
    /// <para>
    /// Each corresponds to a music type whose behaviour needs machinery beyond the
    /// iterator itself — repeats and volta brackets, property push/pop, part
    /// combining, lyric alignment, context changes. Their stubs stay in place, so a
    /// music type that needs one is REPORTED as new demand rather than silently
    /// iterated by a default that would drop its meaning.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> NotYetPorted { get; } = new[]
    {
        "ly:alternative-sequence-iterator::constructor",
        "ly:apply-context-iterator::constructor",
        "ly:change-iterator::constructor",
        "ly:context-specced-music-iterator::constructor",
        "ly:fine-iterator::constructor",
        "ly:grace-iterator::constructor",
        "ly:initial-context-music-iterator::constructor",
        "ly:lyric-combine-music-iterator::constructor",
        "ly:measure-remainder-iterator::constructor",
        "ly:part-combine-iterator::constructor",
        "ly:percent-repeat-iterator::constructor",
        "ly:pop-property-iterator::constructor",
        "ly:premeasure-iterator::constructor",
        "ly:property-iterator::constructor",
        "ly:property-unset-iterator::constructor",
        "ly:push-property-iterator::constructor",
        "ly:quote-iterator::constructor",
        "ly:tuplet-iterator::constructor",
        "ly:volta-repeat-iterator::constructor",
        "ly:volta-specced-music-iterator::constructor",
    };

    /// <summary>Gets the constructor entry points this port implements.</summary>
    public static IReadOnlyCollection<string> Ported => Constructors.Keys;

    /// <summary>Registers the iterator primitives, replacing their stubs.</summary>
    /// <param name="interpreter">The interpreter to register into.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        foreach (KeyValuePair<string, Func<MusicIterator>> entry in Constructors)
        {
            Func<MusicIterator> create = entry.Value;
            interpreter.DefinePrimitive(entry.Key, 0, 0, a => create());
        }

        // The smob type predicate, declared upstream by Music_iterator's type_p_name_
        // member. Nothing else is registered here: upstream exposes no other iterator
        // accessor to Scheme, and inventing one would put a name in the ly: namespace
        // that a later reader would take for a port of something real.
        interpreter.DefinePrimitive("ly:iterator?", 1, 1, a => a[0] is MusicIterator);
    }
}
