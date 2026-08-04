// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Music;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <content>
/// The RULE ACTION GROUP 2 additions: the parser's default-duration state — honest
/// like the scopes, a real <see cref="Duration"/> value initialized to the quarter
/// note upstream's <c>Lily_parser</c> constructor sets.
/// </content>
internal sealed partial class ScriptedParserHost
{
    /// <inheritdoc/>
    public Duration DefaultDuration { get; set; } = new Duration(2, 0);
}
