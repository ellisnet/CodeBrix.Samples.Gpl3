// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <content>
/// The RAG15 additions: the four music operations the post-event bodies perform on
/// music the host itself made. They work over BOTH shapes this host produces — the
/// recording <see cref="MadeMusic"/> stand-in and, when
/// <see cref="ScriptedParserHost.MakeRealMusic"/> is on, the Engine's real
/// <see cref="MusicObject"/> — because the actions cannot tell the difference and
/// neither should the assertions.
/// </content>
internal sealed partial class ScriptedParserHost
{
    /// <summary>Gets the music types a <see cref="MadeMusic"/> stand-in answers to.</summary>
    /// <remarks>
    /// Real <see cref="MusicObject"/>s answer from their own <c>types</c> property, so
    /// this is only consulted for the stand-in. Keyed by type NAME, holding the
    /// stand-ins that carry it.
    /// </remarks>
    public Dictionary<string, HashSet<object>> MusicTypes { get; }
        = new Dictionary<string, HashSet<object>>();

    /// <summary>
    /// Gets each span handed to <see cref="SchemeLocation"/> paired with the origin it
    /// answered, in order — so a test can assert WHICH origin a grob was stamped with,
    /// not merely that one was.
    /// </summary>
    public List<(SourceSpan Span, Input Origin)> SchemeLocations { get; }
        = new List<(SourceSpan, Input)>();

    /// <summary>Gets the spots stamped by <see cref="SetMusicSpot"/>, in order.</summary>
    public List<(object Music, SourceSpan Location)> MusicSpots { get; }
        = new List<(object, SourceSpan)>();

    /// <inheritdoc/>
    // All three shapes this host produces are music; a SyntaxMark is whatever the
    // constructor returned, and every constructor the grammar names from a music
    // position returns music.
    public bool IsMusic(object value)
        => value is MusicObject || value is MadeMusic || value is SyntaxMark;

    /// <inheritdoc/>
    public bool IsMusicType(object music, string type)
    {
        if (music is MusicObject real)
        {
            return real.IsMusicType(type);
        }

        return MusicTypes.TryGetValue(type, out HashSet<object> carriers)
               && carriers.Contains(music);
    }

    /// <inheritdoc/>
    public object CloneMusic(object music)
    {
        if (music is MusicObject real)
        {
            return real.Clone();
        }

        MadeMusic copy = new MadeMusic { Name = (music as MadeMusic)?.Name };
        copy.Properties.AddRange(PropertyBag(music));
        MadeMusicObjects.Add(copy);

        // A clone carries its original's types — which is what makes a cloned
        // \dashDot still a post-event.
        foreach (KeyValuePair<string, HashSet<object>> entry in MusicTypes)
        {
            if (entry.Value.Contains(music))
            {
                entry.Value.Add(copy);
            }
        }

        return copy;
    }

    /// <inheritdoc/>
    public Input SchemeLocation(SourceSpan location)
    {
        // The REAL session resolves the span against the Sources it opened; this
        // scripted double has none, so every span becomes the location-less Input that
        // reports "position unknown" — which is what upstream's dummy_input_global is.
        // It still answers ly:input-location?, which is the property the rule actions
        // depend on.
        Input origin = new Input();
        SchemeLocations.Add((location, origin));
        return origin;
    }

    /// <inheritdoc/>
    public void SetMusicSpot(object music, SourceSpan location)
    {
        // The REAL session converts the span to an Input before stamping; this
        // scripted double has no Sources to build one from, so the raw span stands
        // in — the rule-action tests assert that a spot was SET, not its type.
        MusicSpots.Add((music, location));
        switch (music)
        {
            case MusicObject real:
                real.SetSpot(location);
                break;
            case Score score:
                score.SetSpot(location);
                break;
            case Book book:
                book.SetSpot(location);
                break;
            case OutputDef definition:
                definition.SetSpot(location);
                break;
        }
    }

    /// <summary>
    /// Declares that a value carries a music type, for the stand-in shape.
    /// </summary>
    /// <param name="music">The music.</param>
    /// <param name="type">The type's name.</param>
    /// <returns>The music, so a test can script and use it in one expression.</returns>
    public object WithMusicType(object music, string type)
    {
        if (music is MusicObject real)
        {
            real.SetProperty("types", new Pair(Symbol.Intern(type), Nil.Instance));
            return music;
        }

        if (!MusicTypes.TryGetValue(type, out HashSet<object> carriers))
        {
            carriers = new HashSet<object>();
            MusicTypes[type] = carriers;
        }

        carriers.Add(music);
        return music;
    }
}
