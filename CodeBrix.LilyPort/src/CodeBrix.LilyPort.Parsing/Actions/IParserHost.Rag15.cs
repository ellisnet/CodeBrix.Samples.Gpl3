// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Parsing.Driver;

namespace CodeBrix.LilyPort.Parsing.Actions;

/// <content>
/// The members RULE ACTION GROUP 15 needs. Every one of them acts on music the HOST
/// made — a post event comes back from <see cref="MakeMusic"/>, from
/// <see cref="MakeSyntax"/>, or from an identifier lookup — which is why they sit on
/// the seam beside RAG17's <see cref="GetMusicProperty"/> rather than being reached
/// through a direct <c>MusicObject</c> cast.
/// </content>
public partial interface IParserHost
{
    /// <summary>
    /// Answers whether a value is a music object at all.
    /// <para>Upstream: <c>unsmob&lt;Music&gt; (x)</c> used as a null test, which
    /// several post-event bodies do explicitly — <c>if (Music *m = unsmob&lt;Music&gt;
    /// ($2))</c>.</para>
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> for music.</returns>
    bool IsMusic(object value);

    /// <summary>
    /// Answers whether a music object is of a given music type — <c>post-event</c>,
    /// <c>rhythmic-event</c>, <c>music-wrapper-music</c>.
    /// <para>Upstream: <c>Music::is_mus_type</c>, which reads the <c>types</c>
    /// property the music-descriptions table filled in.</para>
    /// </summary>
    /// <param name="music">The music object.</param>
    /// <param name="type">The type's name.</param>
    /// <returns><see langword="true"/> when the music carries the type.</returns>
    bool IsMusicType(object music, string type);

    /// <summary>
    /// Copies a music object, deeply enough that the copy can be given its own
    /// properties.
    /// <para>Upstream: <c>Music::clone</c>.</para>
    /// </summary>
    /// <param name="music">The music object.</param>
    /// <returns>The copy.</returns>
    object CloneMusic(object music);

    /// <summary>
    /// Stamps a music object with the place it came from.
    /// <para>Upstream: <c>m-&gt;set_spot (parser-&gt;lexer_-&gt;override_input
    /// (loc))</c>, which is the ONLY form the grammar uses on already-made music. The
    /// <c>override_input</c> indirection — the lexer answers its own override when one
    /// is installed, and the given location otherwise — is FOLDED IN here, exactly as
    /// <see cref="MakeMusic"/> folds it for <c>MY_MAKE_MUSIC</c>; a host that
    /// implemented one without the other would locate parser-built music
    /// inconsistently.</para>
    /// </summary>
    /// <param name="music">The music object.</param>
    /// <param name="location">The span to stamp.</param>
    void SetMusicSpot(object music, SourceSpan location);
}
