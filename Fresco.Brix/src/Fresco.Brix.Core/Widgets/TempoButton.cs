// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using Microsoft.UI.Xaml.Controls;
using System;

namespace Fresco.Brix.Widgets; //was previously: frescobaldi/widgets/tempobutton.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A button the user taps a tempo on: it answers the average speed of the
/// taps, and forgets what it was counting after three seconds of silence.
/// </summary>
/// <remarks>
/// <para>
/// //was previously: not ported. The LOGIC it feeds was — the Score Wizard's
/// <c>ScoreProperties.SetMetronomeValue</c>, with its rounding rule and the
/// "Round tap tempo value" checkbox that governs it — so the wizard showed a
/// checkbox for a tempo the user had no way to tap.
/// </para>
/// <para>
/// Upstream's button is icon-only (<c>media-record</c>) and counts on
/// <c>pressed</c>; there is no icon set here, so it carries a caption, and it
/// counts on <c>Click</c>, which is the platform's press-and-release. The
/// arithmetic is upstream's, interval bounds included: a gap under a tenth of a
/// second is not a tap and a gap over three seconds starts again.
/// </para>
/// </remarks>
public sealed class TempoButton : Button
{
    private DateTime _tapStart;
    private DateTime _tapTime;
    private int _tapCount;

    /// <summary>Creates the button.</summary>
    public TempoButton()
    {
        Content = I18n.Get("Tap");
        ToolTipService.SetToolTip(
            this, I18n.Get("The tempo is set as you click this button."));
        Click += (_, _) => Tap();
    }

    /// <summary>Raised with a tempo in beats per minute.</summary>
    public event EventHandler<int> Tempo;

    /// <summary>Records one tap.</summary>
    /// <remarks>Upstream's <c>slotPressed</c>, arithmetic for arithmetic.</remarks>
    public void Tap()
    {
        DateTime previous = _tapTime;
        _tapTime = DateTime.UtcNow;

        double seconds = (_tapTime - previous).TotalSeconds;
        if (seconds > 0.1 && seconds < 3.0)
        {
            _tapCount++;
            double elapsed = (_tapTime - _tapStart).TotalSeconds;
            if (elapsed > 0)
            {
                Tempo?.Invoke(this, (int)(60.0 * _tapCount / elapsed));
            }

            return;
        }

        _tapStart = _tapTime;
        _tapCount = 0;
    }
}
