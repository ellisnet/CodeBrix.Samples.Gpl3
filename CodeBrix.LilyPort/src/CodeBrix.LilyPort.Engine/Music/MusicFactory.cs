// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Music;

/// <summary>
/// Builds a music object BY NAME, through Scheme's own <c>make-music</c>.
/// <para>
/// This is the managed stand-in for upstream's <c>Lily::make_music</c> — a cached
/// reference to the <c>(lily)</c> procedure of that name, which several iterators use to
/// manufacture the events they announce.
/// </para>
/// <para>
/// It matters that this goes through Scheme rather than calling <c>new MusicObject</c>.
/// <c>make-music</c> reads <c>scm/define-music-types.scm</c> and initialises the object's
/// <c>name</c> and <c>types</c>, and <see cref="MusicObject.ToEvent"/> derives the stream
/// event's CLASS from exactly those. A music object built directly carries neither, so the
/// event it becomes matches no engraver's listener — and nothing fails loudly, because a
/// broadcast nobody hears is silent by design.
/// </para>
/// <para>New-in-family; the derivation is recorded in <c>THIRD-PARTY-NOTICES.txt</c>.</para>
/// </summary>
public static class MusicFactory
{
    private static readonly Symbol MakeMusicSymbol = Symbol.Intern("make-music");

    /// <summary>
    /// Creates a music object of the named type, as <c>(make-music 'Name)</c> does.
    /// </summary>
    /// <param name="name">The music type name, e.g. <c>PartialEvent</c>.</param>
    /// <returns>The music object, or <see langword="null"/> when Scheme cannot supply one.</returns>
    public static MusicObject MakeMusic(Symbol name)
    {
        object procedure = LilyPondScheme.LookupProcedure(MakeMusicSymbol);
        Interpreter interpreter = LilyPondScheme.Current;
        if (procedure == null || interpreter == null)
        {
            Warn.ProgrammingError("make-music is not available");
            return null;
        }

        return interpreter.Evaluator.Apply(procedure, new object[] { name }) as MusicObject;
    }

    /// <summary>
    /// Creates a span event, as <c>(make-span-event 'Name dir)</c> does — a music object
    /// of the named type carrying a <c>span-direction</c>.
    /// </summary>
    /// <param name="name">The event type name, e.g. <c>TupletSpanEvent</c>.</param>
    /// <param name="spanDirection">The span direction.</param>
    /// <returns>The music object, or <see langword="null"/> when Scheme cannot supply one.</returns>
    public static MusicObject MakeSpanEvent(Symbol name, Direction spanDirection)
    {
        MusicObject result = MakeMusic(name);
        result?.SetProperty(Symbol.Intern("span-direction"), (long)(int)spanDirection);
        return result;
    }
}
