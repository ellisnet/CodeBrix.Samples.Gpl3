// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Microsoft.UI.Xaml.Input;
using System.Reflection;

namespace Lily.Shell.TerminalView.Input;

/// <summary>
/// Reads the platform's composed character for a key event.
/// KeyRoutedEventArgs carries the layout-resolved character in its INTERNAL
/// UnicodeKey property (the Skia heads implement no CharacterReceived event),
/// so the only app-side path to correct punctuation — for example a shifted
/// digit-row '(' arrives under a keysym the VirtualKey mapping never sees —
/// is reading that property by reflection. Falls back to null when the
/// property is missing or empty; callers then use the US-QWERTY mapping.
/// </summary>
internal static class UnicodeKeyReader
{
    private static readonly PropertyInfo UnicodeKeyProperty =
        typeof(KeyRoutedEventArgs).GetProperty("UnicodeKey",
            BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>The composed character of the event, or null when none exists.</summary>
    public static char? GetUnicodeKey(KeyRoutedEventArgs args)
    {
        var value = UnicodeKeyProperty?.GetValue(args);
        return value is char c && c != '\0' ? c : null;
    }
}
