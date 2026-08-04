// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <content>
/// The RAG17 additions: the music-property read side is honest — it answers the
/// last value <see cref="ScriptedParserHost.SetMusicProperty"/> recorded on the
/// <see cref="MadeMusic"/>, or <see cref="Nil"/> for an unset property, exactly as
/// <c>Music::get_property</c> answers <c>SCM_EOL</c> — and music warnings are
/// recorded for assertion.
/// </content>
internal sealed partial class ScriptedParserHost
{
    /// <summary>Gets the music warnings received, as (music, message).</summary>
    public List<(object Music, string Message)> MusicWarnings { get; }
        = new List<(object, string)>();

    /// <inheritdoc/>
    public object GetMusicProperty(object music, string name)
    {
        if (music is CodeBrix.LilyPort.Engine.Music.MusicObject real)
        {
            return real.GetProperty(name);
        }

        List<(string Name, object Value)> properties = PropertyBag(music);
        for (int i = properties.Count - 1; i >= 0; i--)
        {
            if (string.Equals(properties[i].Name, name, StringComparison.Ordinal))
            {
                return properties[i].Value;
            }
        }

        return Nil.Instance;
    }

    /// <inheritdoc/>
    public void MusicWarning(object music, string message)
        => MusicWarnings.Add((music, message));
}
