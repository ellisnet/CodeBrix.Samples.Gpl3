// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;

namespace Fresco.Brix.Shell;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A request for keyboard focus that outlives the moment it was made.
/// </summary>
/// <remarks>
/// <para>
/// Qt remembers <c>QWidget.setFocus()</c> on a widget that has not been shown
/// yet and gives it the keyboard the moment it is: that is what lets
/// Frescobaldi's <c>viewmanager.py</c> focus the active view
/// (<c>focusActiveView</c>, line 260) immediately after <c>showDocument</c>
/// built it a few lines earlier.
/// </para>
/// <para>
/// A XAML control has no such memory. An element that is not yet in the live
/// visual tree answers <see langword="false"/> to <c>Focus</c> and the request
/// is simply lost, so a control built and focused in the same breath opens
/// unfocused. This carries the request across that gap: it is asked again when
/// the control loads — and ONLY if it is still waiting, so a control that
/// loads with nothing pending never pulls the keyboard away from wherever the
/// user has since put it.
/// </para>
/// </remarks>
public sealed class DeferredFocus
{
    /// <summary>
    /// Gets whether a request has been made that nothing has honoured yet.
    /// </summary>
    public bool IsPending { get; private set; }

    /// <summary>Asks for focus now, remembering the ask if it is refused.</summary>
    /// <param name="focus">Tries to take focus; answers whether it did.</param>
    /// <returns>Whether focus was taken.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="focus"/> is null.</exception>
    public bool Request(Func<bool> focus)
    {
        if (focus == null) { throw new ArgumentNullException(nameof(focus)); }

        bool taken = focus();
        IsPending = !taken;
        return taken;
    }

    /// <summary>
    /// Makes a waiting request again, now that the control can take focus.
    /// </summary>
    /// <param name="focus">Tries to take focus; answers whether it did.</param>
    /// <returns>Whether a waiting request was honoured.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="focus"/> is null.</exception>
    /// <remarks>With nothing waiting this does nothing at all, which is the
    /// whole point: it is called from an event that fires for reasons of its
    /// own.</remarks>
    public bool Honour(Func<bool> focus)
    {
        if (focus == null) { throw new ArgumentNullException(nameof(focus)); }

        return IsPending && Request(focus);
    }
}
