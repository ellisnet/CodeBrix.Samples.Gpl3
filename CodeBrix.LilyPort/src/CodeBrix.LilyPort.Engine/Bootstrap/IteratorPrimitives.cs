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

            // The remaining iterators:
            ["ly:context-specced-music-iterator::constructor"] = () => new ContextSpeccedMusicIterator(),
            ["ly:initial-context-music-iterator::constructor"] = () => new InitialContextMusicIterator(),
            ["ly:change-iterator::constructor"] = () => new ChangeIterator(),
            ["ly:apply-context-iterator::constructor"] = () => new ApplyContextIterator(),
            ["ly:property-iterator::constructor"] = () => new PropertyIterator(),
            ["ly:property-unset-iterator::constructor"] = () => new PropertyUnsetIterator(),
            ["ly:push-property-iterator::constructor"] = () => new PushPropertyIterator(),
            ["ly:pop-property-iterator::constructor"] = () => new PopPropertyIterator(),
            ["ly:quote-iterator::constructor"] = () => new QuoteIterator(),
            ["ly:part-combine-iterator::constructor"] = () => new PartCombineIterator(),

            // The repeats/grace first slice: the four whose behaviour is entirely their
            // own.
            ["ly:fine-iterator::constructor"] = () => new FineIterator(),
            ["ly:grace-iterator::constructor"] = () => new GraceIterator(),
            ["ly:measure-remainder-iterator::constructor"] = () => new MeasureRemainderIterator(),
            ["ly:premeasure-iterator::constructor"] = () => new PremeasureIterator(),

            // The repeats/voltas remainder: the five built on Repeat_styler and on each
            // other. Volta_repeat owns the styler, Alternative_sequence borrows it, and
            // Volta_specced reads its bracket state off Alternative_sequence — which is
            // why they had to land together rather than one at a time.
            ["ly:alternative-sequence-iterator::constructor"] = () => new AlternativeSequenceIterator(),
            ["ly:percent-repeat-iterator::constructor"] = () => new PercentRepeatIterator(),
            ["ly:tuplet-iterator::constructor"] = () => new TupletIterator(),
            ["ly:volta-repeat-iterator::constructor"] = () => new VoltaRepeatIterator(),
            ["ly:volta-specced-music-iterator::constructor"] = () => new VoltaSpeccedMusicIterator(),

            // The lyrics group's, and the last one: with this entry the table holds all 28
            // constructors upstream declares, and NotYetPorted below is empty.
            ["ly:lyric-combine-music-iterator::constructor"] = () => new LyricCombineMusicIterator(),
        };

    /// <summary>
    /// The iterator constructors upstream declares that this port has NOT reached yet.
    /// <para>
    /// EMPTY, and it is meant to stay that way: all 28
    /// constructors upstream declares are registered above, which is gate G5. The list is
    /// kept rather than deleted because it is the honest shape of this mechanism — a music
    /// type whose iterator is not ported has NO constructor registered and falls through
    /// to a default that would silently drop its meaning, so anything that ever has to
    /// come out of <see cref="Constructors"/> belongs here, named, instead of vanishing.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> NotYetPorted { get; } = new string[0];

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
