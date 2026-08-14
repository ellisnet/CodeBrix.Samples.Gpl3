// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace CodeBrix.LilyPort.Parsing.Actions;

/// <summary>
/// The host members the FiguredBass group (figured bass) added, per the
/// partial-interface convention on the main file.
/// </summary>
public partial interface IParserHost
{
    /// <summary>
    /// Reads a property from a music object made by <see cref="MakeMusic"/> — the
    /// read side of <see cref="SetMusicProperty"/>.
    /// <para>Upstream: <c>get_property</c> on <c>Music</c>, as
    /// <c>bass_figure: bass_figure FIGURE_ALTERATION_EXPR</c> reads the
    /// <c>alteration</c> already accumulated. An unset property answers
    /// <c>SCM_EOL</c> upstream, so a host must answer
    /// <see cref="CodeBrix.LilyScheme.Values.Nil"/>.Instance for one.</para>
    /// </summary>
    /// <param name="music">The music object.</param>
    /// <param name="name">The property name.</param>
    /// <returns>The property's value, or
    /// <see cref="CodeBrix.LilyScheme.Values.Nil"/>.Instance when unset.</returns>
    object GetMusicProperty(object music, string name);

    /// <summary>
    /// Issues a warning at a music object's origin — a diagnostic that does NOT
    /// raise the error level, unlike <c>parser_error</c>.
    /// <para>Upstream: <c>Music::warning</c> — <c>Diagnostics::warning</c> over
    /// <c>Music::origin</c>. On the host because the music value came from
    /// <see cref="MakeMusic"/>, so only the host knows where its origin is stamped;
    /// the helper-layer <c>ParserActionHelpers.MusicWarning</c> covers the sibling
    /// call sites whose music is a raw Engine <c>MusicObject</c>.</para>
    /// </summary>
    /// <param name="music">The music object whose origin the warning points at.</param>
    /// <param name="message">The message.</param>
    void MusicWarning(object music, string message);
}
