// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;

namespace Lily.Shell.Services;

/// <summary>
/// Routes window-chrome changes from the view model to the App, which owns
/// the Window (the Pinta.Brix chrome pattern: the App subscribes and assigns
/// MainWindow.Title). Raise only from the UI thread.
/// </summary>
public static class WindowChrome
{
    /// <summary>Raised with the new window title.</summary>
    public static event Action<string> TitleChanged;

    /// <summary>Sets the window title.</summary>
    public static void SetTitle(string title) => TitleChanged?.Invoke(title);
}
