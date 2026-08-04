// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Music;

namespace CodeBrix.LilyPort.Parsing.Actions;

/// <content>
/// The members RULE ACTION GROUP 2 added: the parser's default-duration state.
/// </content>
public partial interface IParserHost
{
    /// <summary>
    /// Gets or sets the duration a note written without one receives. Assigned by
    /// <c>embedded_lilypond: duration post_events</c> here; the note-mode duration
    /// rules (RAG16) read and assign it too when they land.
    /// <para>Upstream: <c>Lily_parser::default_duration_</c>, initialized to a
    /// quarter note (<c>Duration (2, 0)</c>) when the parser is made. Upstream
    /// assigns a COPY (<c>*unsmob&lt;Duration&gt;</c>); <see cref="Duration"/> is a
    /// value type, so assignment through this property copies the same way.</para>
    /// </summary>
    Duration DefaultDuration { get; set; }
}
