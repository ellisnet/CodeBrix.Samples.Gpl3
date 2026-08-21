// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using Windows.Foundation;
using Windows.UI;

namespace Fresco.Brix.Shell; //was previously: PyQt6 QSlider, as Frescobaldi's MIDI tool uses it

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A value picked by dragging along a groove: the MIDI panel's position,
/// tempo and volume controls.
/// </summary>
/// <remarks>
/// <para>
/// This is the port's <c>QSlider</c>, drawn rather than templated. The theme's
/// own <c>Thumb</c> paints nothing on the Skia heads (board trap 20, the same
/// sharp edge as trap 2's standalone <c>ScrollBar</c> and trap 40's tab
/// controls), and every part of a <c>Slider</c>'s template is built out of one,
/// so the house answer applies here as it did to <see cref="SplitContainer"/>'s
/// dividers: plain <see cref="Grid"/>s with their own pointer handling.
/// </para>
/// <para>
/// Upstream's two behaviours that matter, both kept:
/// <list type="bullet">
/// <item><description><c>tracking=False</c> on the position slider —
/// <see cref="ValueChanged"/> fires when the drag ENDS, while
/// <see cref="Moved"/> fires all the way through it. That is what lets the
/// panel's display follow the user's finger without the player seeking on
/// every pixel.</description></item>
/// <item><description>A value written from outside is ignored while the user
/// has hold of the bar (<see cref="IsDragging"/>), which is upstream's
/// <c>if not self._timeSlider.isSliderDown()</c> guard.</description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class TrackBar : Grid
{
    private const double GrooveThickness = 4.0;
    private const double ThumbLength = 12.0;
    private const double ThumbBreadth = 12.0;

    private readonly Grid _groove = new Grid();
    private readonly Grid _fill = new Grid();
    private readonly Grid _thumb = new Grid();

    private double _minimum;
    private double _maximum = 100;
    private double _value;
    private bool _tracking = true;

    /// <summary>Creates a track bar.</summary>
    public TrackBar()
    {
        Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0));
        MinHeight = ThumbBreadth + 4;
        MinWidth = ThumbBreadth + 4;

        _groove.CornerRadius = new CornerRadius(2);
        _fill.CornerRadius = new CornerRadius(2);
        _thumb.CornerRadius = new CornerRadius(3);
        Children.Add(_groove);
        Children.Add(_fill);
        Children.Add(_thumb);

        ApplyBrushes();

        //The whole bar is the target, not just the thumb: clicking anywhere on
        //the groove jumps there, which is what a QSlider with the "absolute
        //set buttons" style does and what a user expects of a position bar.
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCaptureLost += OnPointerReleased;
        SizeChanged += (_, _) => Arrange();
    }

    /// <summary>Raised when the value settles — on release, or when set.</summary>
    public event EventHandler ValueChanged;

    /// <summary>Raised continuously while the user drags.</summary>
    public event EventHandler Moved;

    /// <summary>Gets or sets which way the bar runs.</summary>
    public Orientation Orientation
    {
        get;
        set
        {
            if (field == value) { return; }

            field = value;
            Arrange();
        }
    } = Orientation.Horizontal;

    /// <summary>Gets or sets whether a bigger value is nearer the TOP.</summary>
    /// <remarks>A vertical Qt slider counts upwards from the bottom; the tempo
    /// control is the one that needs it.</remarks>
    public bool IsInverted
    {
        get;
        set
        {
            if (field == value) { return; }

            field = value;
            Arrange();
        }
    }

    /// <summary>Gets whether the user currently has hold of the bar.</summary>
    public bool IsDragging { get; private set; }

    /// <summary>Gets or sets whether the bar answers the pointer.</summary>
    /// <remarks>A <see cref="Grid"/> is not a <c>Control</c> and so has no
    /// <c>IsEnabled</c> of its own; this is the bar's own, and it greys the
    /// drawing as well as refusing the pointer.</remarks>
    public bool IsEnabled
    {
        get;
        set
        {
            if (field == value) { return; }

            field = value;
            ApplyBrushes();
        }
    } = true;

    /// <summary>Gets or sets whether dragging changes the value continuously.</summary>
    /// <remarks>False is Qt's <c>tracking=False</c>: <see cref="Moved"/> during
    /// the drag, <see cref="ValueChanged"/> once at the end.</remarks>
    public bool IsTracking
    {
        get => _tracking;
        set => _tracking = value;
    }

    /// <summary>Gets or sets the lowest value.</summary>
    public double Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            if (_maximum < _minimum) { _maximum = _minimum; }

            SetValue(_value, notify: false);
        }
    }

    /// <summary>Gets or sets the highest value.</summary>
    public double Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(value, _minimum);
            SetValue(_value, notify: false);
        }
    }

    /// <summary>Gets or sets the value.</summary>
    public double Value
    {
        get => _value;
        set => SetValue(value, notify: true);
    }

    /// <summary>
    /// Sets the value from outside without announcing it, and without
    /// disturbing a drag in progress.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <remarks>Upstream's <c>with qutil.signalsBlocked(...)</c> inside its
    /// <c>if not isSliderDown()</c> guard, in one call.</remarks>
    public void SetValueQuietly(double value)
    {
        if (IsDragging) { return; }

        SetValue(value, notify: false);
    }

    private void SetValue(double value, bool notify)
    {
        double clamped = Math.Clamp(value, _minimum, _maximum);
        bool changed = Math.Abs(clamped - _value) > double.Epsilon;
        _value = clamped;
        Arrange();

        if (changed && notify) { ValueChanged?.Invoke(this, EventArgs.Empty); }
    }

    private double Fraction
        => _maximum > _minimum ? (_value - _minimum) / (_maximum - _minimum) : 0.0;

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!IsEnabled) { return; }

        IsDragging = CapturePointer(e.Pointer);
        MoveTo(e.GetCurrentPoint(this).Position);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!IsDragging) { return; }

        MoveTo(e.GetCurrentPoint(this).Position);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!IsDragging) { return; }

        IsDragging = false;
        ReleasePointerCapture(e.Pointer);

        //With tracking off the value has been moving without being announced;
        //this is the announcement, and it is where a seek happens.
        if (!_tracking) { ValueChanged?.Invoke(this, EventArgs.Empty); }

        e.Handled = true;
    }

    private void MoveTo(Point position)
    {
        double fraction = Orientation == Orientation.Horizontal
            ? SafeFraction(position.X, ActualWidth)
            : SafeFraction(position.Y, ActualHeight);

        //A vertical bar's top is its HIGHEST value unless it is inverted,
        //because screen coordinates grow downwards and Qt's sliders do not.
        if (Orientation == Orientation.Vertical != IsInverted)
        {
            fraction = 1.0 - fraction;
        }

        double value = _minimum + (fraction * (_maximum - _minimum));
        bool changed = Math.Abs(value - _value) > double.Epsilon;
        _value = Math.Clamp(value, _minimum, _maximum);
        Arrange();

        if (!changed) { return; }

        Moved?.Invoke(this, EventArgs.Empty);
        if (_tracking) { ValueChanged?.Invoke(this, EventArgs.Empty); }
    }

    private static double SafeFraction(double offset, double extent)
    {
        double usable = extent - ThumbLength;
        return usable <= 0
            ? 0.0
            : Math.Clamp((offset - (ThumbLength / 2)) / usable, 0.0, 1.0);
    }

    private void Arrange()
    {
        double fraction = Fraction;
        if (Orientation == Orientation.Vertical != IsInverted)
        {
            fraction = 1.0 - fraction;
        }

        if (Orientation == Orientation.Horizontal)
        {
            double usable = Math.Max(0, ActualWidth - ThumbLength);
            double top = Math.Max(0, (ActualHeight - GrooveThickness) / 2);

            _groove.Height = GrooveThickness;
            _groove.Width = double.NaN;
            _groove.HorizontalAlignment = HorizontalAlignment.Stretch;
            _groove.VerticalAlignment = VerticalAlignment.Top;
            _groove.Margin = new Thickness(ThumbLength / 2, top, ThumbLength / 2, 0);

            _fill.Height = GrooveThickness;
            _fill.HorizontalAlignment = HorizontalAlignment.Left;
            _fill.VerticalAlignment = VerticalAlignment.Top;
            _fill.Width = fraction * usable;
            _fill.Margin = new Thickness(ThumbLength / 2, top, 0, 0);

            _thumb.Width = ThumbLength;
            _thumb.Height = ThumbBreadth;
            _thumb.HorizontalAlignment = HorizontalAlignment.Left;
            _thumb.VerticalAlignment = VerticalAlignment.Top;
            _thumb.Margin = new Thickness(
                fraction * usable, Math.Max(0, (ActualHeight - ThumbBreadth) / 2), 0, 0);
        }
        else
        {
            double usable = Math.Max(0, ActualHeight - ThumbLength);
            double left = Math.Max(0, (ActualWidth - GrooveThickness) / 2);

            _groove.Width = GrooveThickness;
            _groove.Height = double.NaN;
            _groove.HorizontalAlignment = HorizontalAlignment.Left;
            _groove.VerticalAlignment = VerticalAlignment.Stretch;
            _groove.Margin = new Thickness(left, ThumbLength / 2, 0, ThumbLength / 2);

            _fill.Width = GrooveThickness;
            _fill.HorizontalAlignment = HorizontalAlignment.Left;
            _fill.VerticalAlignment = VerticalAlignment.Top;
            _fill.Height = fraction * usable;
            _fill.Margin = new Thickness(left, ThumbLength / 2, 0, 0);

            _thumb.Width = ThumbBreadth;
            _thumb.Height = ThumbLength;
            _thumb.HorizontalAlignment = HorizontalAlignment.Left;
            _thumb.VerticalAlignment = VerticalAlignment.Top;
            _thumb.Margin = new Thickness(
                Math.Max(0, (ActualWidth - ThumbBreadth) / 2), fraction * usable, 0, 0);
        }
    }

    private void ApplyBrushes()
    {
        byte alpha = IsEnabled ? (byte)0xFF : (byte)0x60;
        _groove.Background = new SolidColorBrush(Color.FromArgb(0x50, 0x60, 0x60, 0x60));
        _fill.Background = new SolidColorBrush(Color.FromArgb(alpha, 0x40, 0x70, 0xC0));
        _thumb.Background = new SolidColorBrush(Color.FromArgb(alpha, 0x30, 0x50, 0x90));
    }
}
