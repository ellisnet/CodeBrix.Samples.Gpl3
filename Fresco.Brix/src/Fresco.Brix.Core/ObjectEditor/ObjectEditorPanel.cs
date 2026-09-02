// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Globalization;

namespace Fresco.Brix.ObjectEditor; //was previously: frescobaldi/objecteditor/

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Object Editor: the engraved object the user clicked on in the Music
/// View, and an offset to nudge it by, written into the source as a
/// <c>\once \override … .extra-offset</c>.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>objecteditor.ObjectEditor</c> panel and its widget, both of
/// which carry the note "This is only a very first stub" and "I think we will
/// work with individual editor objects for different types of objects". It is
/// loaded ONLY when the experimental-features preference is on — upstream's
/// <c>panelmanager</c> is where that test lives, and so it is here.
/// </para>
/// <para>
/// ⚠ Upstream connects to FOUR signals of its SVG view: the three phases of
/// DRAGGING an object with the mouse, and <c>cursor</c> — a click that points
/// into the source. Fresco.Brix has no graphical-editing SVG view (upstream's
/// <c>svgview</c> is itself experimental and edits through a WebEngine page,
/// which ruling FR8 puts out of this application); what it has is the Music
/// View's point-and-click, which is the same <c>cursor</c> signal by another
/// road. The three dragging signals therefore have no counterpart, and the
/// offsets are typed rather than dragged — which is the only way upstream's
/// own spin boxes can be used when nothing is being dragged either.
/// </para>
/// </remarks>
public sealed class ObjectEditorPanel : Shell.Panel
{
    /// <summary>The panel's stable name.</summary>
    public const string PanelName = "objecteditor";

    private readonly SettingsStore _settings;

    private TextBlock _elementLabel;
    private TextBlock _xOffsetLabel;
    private TextBlock _yOffsetLabel;
    private DecimalEntry _xOffset;
    private DecimalEntry _yOffset;
    private Button _insertButton;
    private DefineOffset _define;

    /// <summary>Creates the panel.</summary>
    /// <param name="settings">The settings store, or null.</param>
    public ObjectEditorPanel(SettingsStore settings = null)
        : base(PanelName, DockArea.Left)
    {
        _settings = settings;
        ToggleAction.WithShortcut("Meta+Alt+E");
    }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Object Editor");

    /// <summary>Gets the object the panel is showing, or null.</summary>
    public DefineOffset Define => _define;

    /// <summary>Gets the name of the object the panel is showing, or null.</summary>
    public string ElementName => _elementLabel?.Text;

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        ToggleAction.Text = I18n.Get("O&bject Editor");

        if (_xOffsetLabel == null) { return; }

        _xOffsetLabel.Text = I18n.Get("X Offset");
        ToolTipService.SetToolTip(_xOffset, I18n.Get("Display the X Offset"));
        _yOffsetLabel.Text = I18n.Get("Y Offset");
        ToolTipService.SetToolTip(_yOffset, I18n.Get("Display the Y Offset"));

        //⚠ ODD BUT DELIBERATE, ported faithfully (standing rule 4b): upstream's
        //own translateUI() ends with `self.insertButton.setEnabled(False)', so
        //re-translating the panel puts the button back out of reach until the
        //user picks an object again. It is where upstream disables it, and it
        //is the only place it is disabled.
        _insertButton.IsEnabled = false;
    }

    /// <summary>
    /// Takes the object at a place in a document as the one being edited.
    /// </summary>
    /// <param name="document">The document the click pointed into.</param>
    /// <param name="offset">Where in it.</param>
    /// <remarks>Upstream's <c>setObjectFromCursor()</c>, which its SVG view
    /// calls with the text cursor a click resolved to.</remarks>
    public void SetObjectFromCursor(EditorDocument document, int offset)
    {
        if (document == null || _elementLabel == null) { return; }

        _define = new DefineOffset(document);
        _elementLabel.Text = _define.GetCurrentLilyObject(offset);
        _insertButton.IsEnabled = true;
    }

    /// <summary>Writes the offset into the source.</summary>
    /// <remarks>Upstream's <c>callInsert()</c>.</remarks>
    public void CallInsert()
        => _define?.InsertOverride(_xOffset.Value, _yOffset.Value, _settings);

    /// <inheritdoc/>
    protected override UIElement CreateWidget()
    {
        _elementLabel = new TextBlock { TextWrapping = TextWrapping.Wrap };

        //Upstream: QDoubleSpinBox, range -99..99, single step 0.1.
        _xOffset = new DecimalEntry(-99, 99, 0.1);
        _yOffset = new DecimalEntry(-99, 99, 0.1);
        _xOffsetLabel = new TextBlock();
        _yOffsetLabel = new TextBlock();

        //was previously: upstream passes this caption to QPushButton as a plain
        //literal, with no _() around it — the one string in the module that is
        //not a msgid. It goes through the lookup here so that a translation CAN
        //reach it; with no catalog installed the text is byte-for-byte the
        //same, so nothing on screen changes.
        _insertButton = new Button
        {
            Content = I18n.Get("insert offset in source"),
            IsEnabled = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _insertButton.Click += (_, _) => CallInsert();

        StackPanel panel = new StackPanel { Spacing = 1, Padding = new Thickness(6) };
        panel.Children.Add(_elementLabel);
        panel.Children.Add(_xOffsetLabel);
        panel.Children.Add(_xOffset);
        panel.Children.Add(_yOffsetLabel);
        panel.Children.Add(_yOffset);
        panel.Children.Add(_insertButton);

        TranslateUI();
        return new ScrollViewer
        {
            Content = panel,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
    }
}

/// <summary>
/// A decimal number typed or nudged, within a range and by a fixed step.
/// </summary>
/// <remarks>
/// //was previously: <c>QDoubleSpinBox</c>. The whole-number
/// <c>NumberEntry</c> the preferences pages use cannot carry a tenth, and the
/// platform's own number box is one more control that would have to be proved
/// on six heads; this is the same drawn shape, with a double behind it.
/// </remarks>
public sealed class DecimalEntry : Grid
{
    private readonly TextBox _box = new TextBox
    {
        Width = 96,
        TextAlignment = TextAlignment.Right,
    };

    private readonly Button _less = new Button
    {
        Content = "−",
        Padding = new Thickness(8, 2, 8, 2),
    };

    private readonly Button _more = new Button
    {
        Content = "+",
        Padding = new Thickness(8, 2, 8, 2),
    };

    private readonly double _minimum;
    private readonly double _maximum;
    private readonly double _step;

    private double _value;
    private bool _writing;
    private bool _typing;

    /// <summary>Creates the entry.</summary>
    /// <param name="minimum">The smallest value.</param>
    /// <param name="maximum">The largest value.</param>
    /// <param name="step">How much the buttons move it by.</param>
    public DecimalEntry(double minimum, double maximum, double step)
    {
        _minimum = minimum;
        _maximum = maximum;
        _step = step;

        ColumnSpacing = 2;
        for (int column = 0; column < 3; column++)
        {
            ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        Children.Add(_box);
        SetColumn(_less, 1);
        SetColumn(_more, 2);
        Children.Add(_less);
        Children.Add(_more);

        _less.Click += (_, _) => Value -= _step;
        _more.Click += (_, _) => Value += _step;
        _box.TextChanged += (_, _) => Parse();
        _box.LostFocus += (_, _) => Display();

        Display();
    }

    /// <summary>Raised when the value changed.</summary>
    public event EventHandler ValueChanged;

    /// <summary>Gets or sets the value, clamped to the range.</summary>
    public double Value
    {
        get => _value;
        set
        {
            double clamped = Math.Clamp(value, _minimum, _maximum);

            //No SetProperty overload takes a double, and this is not a view
            //model anyway; compare and notify by hand.
            if (_value.Equals(clamped)) { return; }

            _value = clamped;
            Display();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Parse()
    {
        if (_writing) { return; }

        if (!double.TryParse(
            _box.Text.Trim(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out double parsed))
        {
            return;
        }

        //The box must not be REWRITTEN while somebody is typing in it: a
        //half-typed "1.5" is the value 1 for one keystroke, and formatting
        //that back as "1.00" under the caret makes the next keystroke land in
        //the wrong place. The tidy form is written on LostFocus instead.
        _typing = true;
        try
        {
            Value = parsed;
        }
        finally
        {
            _typing = false;
        }
    }

    private void Display()
    {
        if (_typing) { return; }

        _writing = true;
        _box.Text = _value.ToString("0.00", CultureInfo.InvariantCulture);
        _writing = false;
    }
}
