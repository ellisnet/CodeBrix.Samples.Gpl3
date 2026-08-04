// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Objects;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <content>
/// The RULE ACTION GROUP 3 half of the scripted host: <c>ScorifyMusic</c>, honest in
/// the same way the scope stack is — it really builds a <see cref="Score"/> holding
/// the music, because the score_items accumulator branches on exactly that — while
/// recording what it was asked to wrap.
/// </content>
internal sealed partial class ScriptedParserHost
{
    /// <summary>Gets the music values <see cref="ScorifyMusic"/> wrapped, in order.</summary>
    public List<object> ScorifiedMusic { get; } = new List<object>();

    /// <summary>
    /// Wraps music into a fresh <see cref="Score"/>, the way the vendored
    /// <c>scorify-music</c> ends in <c>ly:make-score</c>; the
    /// toplevel-music-functions preprocessing is the Scheme layer's business and is
    /// deliberately not imitated here.
    /// </summary>
    /// <param name="music">The music expression.</param>
    /// <returns>The score.</returns>
    public object ScorifyMusic(object music)
    {
        ScorifiedMusic.Add(music);
        Score score = new Score();
        score.SetMusic(music);
        return score;
    }
}
