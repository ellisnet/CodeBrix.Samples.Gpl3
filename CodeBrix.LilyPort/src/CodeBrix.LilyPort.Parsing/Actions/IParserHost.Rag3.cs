// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace CodeBrix.LilyPort.Parsing.Actions;

/// <content>
/// The members RULE ACTION GROUP 3 (book, bookpart and score blocks) added to the
/// host seam.
/// </content>
public partial interface IParserHost
{
    /// <summary>
    /// Preprocesses a music expression and encapsulates it into a score.
    /// <para>Upstream: <c>Lily::scorify_music</c> — the C++ binding of
    /// <c>scorify-music</c> in <c>scm/lily-library.scm</c> (already vendored under
    /// <c>CodeBrix.LilyPort.Engine/Scheme/lily/</c>), which folds the toplevel music
    /// functions over the music and hands the result to <c>ly:make-score</c>. On the
    /// host because the toplevel-music-functions list lives in the Scheme
    /// layer.</para>
    /// </summary>
    /// <param name="music">The music expression.</param>
    /// <returns>The score.</returns>
    object ScorifyMusic(object music);
}
