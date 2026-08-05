// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using SkiaSharp;

namespace Lily.Shell.TerminalView.Rendering;

/// <summary>
/// The resolved drawing style of one terminal cell (or run of cells sharing
/// an attribute): concrete colors plus the type-face and decoration flags.
/// Produced by <see cref="AttributeDecoder"/>.
/// </summary>
public readonly record struct CellStyle(
    SKColor Foreground,
    SKColor Background,
    bool Bold,
    bool Italic,
    bool Underline,
    bool CrossedOut)
{
    /// <summary>True when the background differs from the terminal default and needs a fill rect.</summary>
    public bool HasVisibleBackground(SKColor defaultBackground) => Background != defaultBackground;
}
