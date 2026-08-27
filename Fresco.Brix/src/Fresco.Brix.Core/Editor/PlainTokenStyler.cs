// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Highlighting;
using Fresco.Brix.Ly.Slexing;

namespace Fresco.Brix.Editor;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A styler that colours nothing — what View &gt; Syntax Highlighting turns
/// the editor over to when a user switches highlighting off.
/// </summary>
/// <remarks>
/// The tokenization is NOT switched off with it: the matcher, folding,
/// autocomplete, the outline and every ported ly tool read the same tokens,
/// and they go on working. Upstream does the same — its
/// <c>Highlighter.setHighlighting(False)</c> stops the FORMATS being applied
/// and leaves the tokens alone.
/// </remarks>
public sealed class PlainTokenStyler : ITokenStyler
{
    /// <inheritdoc/>
    public HighlightingColor ColorFor(Token token) => null;
}
