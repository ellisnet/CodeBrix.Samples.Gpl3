// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Documentation;
using Fresco.Brix.Midi;
using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using Fresco.Brix.Snippets;
using Fresco.Brix.Widgets;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Fresco.Brix.Preferences; //was previously: frescobaldi/preferences/__init__.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Everything the preference pages need from the running window: the settings
/// store they read and write, the objects whose state they configure, and the
/// head's file pickers.
/// </summary>
/// <remarks>
/// Upstream's pages reach out to module-level singletons — <c>app</c>,
/// <c>actioncollectionmanager.manager(win)</c>, <c>snippet.snippets</c>. There
/// are none here, so the window hands the dialog what it owns and the pages
/// take what they need.
/// </remarks>
public sealed class PreferencesContext
{
    /// <summary>Gets or sets the settings store every page reads and writes.</summary>
    public SettingsStore Settings { get; set; }

    /// <summary>Gets or sets the window's commands, for the Shortcuts page.</summary>
    public ActionCollectionManager Actions { get; set; }

    /// <summary>Gets or sets the snippet library, for the General page's
    /// list of templates a new document can start from.</summary>
    public SnippetLibrary Snippets { get; set; }

    /// <summary>Gets or sets the MIDI player, so a volume change is heard at
    /// once rather than at the next launch.</summary>
    public IMidiPlayer MidiPlayer { get; set; }

    /// <summary>Gets or sets the bundled manuals, for the Documentation page.</summary>
    public ManualLibrary Manuals { get; set; }

    /// <summary>Gets or sets the named sessions, for the General page's
    /// startup-session choice.</summary>
    public Sessions.SessionStore SessionStore { get; set; }

    /// <summary>
    /// Gets or sets how the head opens a picker: given what is wanted and the
    /// path to start at, it answers the chosen path or null.
    /// </summary>
    /// <remarks>Null on a head with no picker; every page then falls back to a
    /// typed path, which is what keeps the frame-buffer head usable.</remarks>
    public Func<UrlRequesterMode, string, Task<string>> PickAsync { get; set; }

    /// <summary>
    /// Gets or sets how a file of a named kind is picked — the MIDI page's
    /// instrument chooser, which wants <c>.sf2</c> rather than any file.
    /// </summary>
    public Func<IReadOnlyList<string>, Task<string>> PickFileAsync { get; set; }
}

/// <summary>
/// One page of the Preferences dialog: an entry in the list on the left and
/// the panel it shows on the right.
/// </summary>
/// <remarks>
/// <para>
/// Upstream splits this into a <c>QListWidgetItem</c> subclass carrying the
/// title, the icon and the <c>help</c> identifier, and a <c>QWidget</c>
/// subclass carrying <c>loadSettings</c>/<c>saveSettings</c>. One class says
/// the same thing here, because the list entry is data rather than a widget.
/// </para>
/// <para>
/// The panel is built on FIRST USE, exactly as upstream's <c>activate()</c>
/// builds it: a dialog opened and closed on the General page never constructs
/// the shortcut tree.
/// </para>
/// </remarks>
public abstract class PreferencesPage
{
    private UIElement _content;

    /// <summary>Creates a page.</summary>
    /// <param name="context">What the page configures.</param>
    protected PreferencesPage(PreferencesContext context)
        => Context = context ?? throw new ArgumentNullException(nameof(context));

    /// <summary>Raised when the user changed something on the page.</summary>
    public event EventHandler Changed;

    /// <summary>Gets the entry the page list shows.</summary>
    public abstract string Title { get; }

    /// <summary>
    /// Gets the user-guide page this preferences page documents itself with.
    /// </summary>
    /// <remarks>
    /// ⚠ Upstream's <c>help = "prefs_fontscolors"</c>, recorded here from the
    /// start and resolving to NOTHING until W12B ports the user guide. That is
    /// expected; the identifiers are what W12B wires up.
    /// </remarks>
    public abstract string Help { get; }

    /// <summary>
    /// Gets the icon-theme name of the page's icon, recorded the way every
    /// other command in the application records one; nothing draws it until
    /// W13 audits the icon assets.
    /// </summary>
    public abstract string IconName { get; }

    /// <summary>Gets whether the page has unsaved changes.</summary>
    public bool HasChanges { get; internal set; }

    /// <summary>Gets whether the page's panel has been built.</summary>
    public bool IsBuilt => _content != null;

    /// <summary>Gets or sets the root the page's own dialogs attach to.</summary>
    public XamlRoot DialogRoot { get; set; }

    /// <summary>Gets what the page configures.</summary>
    protected PreferencesContext Context { get; }

    /// <summary>Gets the settings store, for brevity in the pages.</summary>
    protected SettingsStore Settings => Context.Settings;

    /// <summary>Gets the page's panel, building it the first time.</summary>
    /// <returns>The panel.</returns>
    public UIElement Panel()
    {
        if (_content != null) { return _content; }

        _content = Build();
        LoadSettings();
        HasChanges = false;
        return _content;
    }

    /// <summary>Reads the settings into the page's controls.</summary>
    public abstract void LoadSettings();

    /// <summary>Writes the page's controls back into the settings.</summary>
    public abstract void SaveSettings();

    /// <summary>Builds the page's panel.</summary>
    /// <returns>The panel.</returns>
    protected abstract UIElement Build();

    /// <summary>Announces that the user changed something.</summary>
    protected void MarkChanged()
    {
        HasChanges = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Puts a titled box round a group of settings, as upstream's
    /// <c>QGroupBox</c> does.</summary>
    /// <param name="title">The group's title.</param>
    /// <param name="content">What goes in it.</param>
    /// <returns>The box.</returns>
    protected static UIElement Group(string title, UIElement content)
        => SettingsEditor.Wrap(title, content);

    /// <summary>Stacks elements down a page.</summary>
    /// <param name="children">The elements.</param>
    /// <returns>The stack.</returns>
    protected static StackPanel Stack(params UIElement[] children)
    {
        StackPanel panel = new StackPanel { Spacing = 10 };
        foreach (var child in children)
        {
            if (child != null) { panel.Children.Add(child); }
        }

        return panel;
    }

    /// <summary>Stacks elements tightly, as one group's contents.</summary>
    /// <param name="children">The elements.</param>
    /// <returns>The stack.</returns>
    protected static StackPanel Rows(params UIElement[] children)
    {
        StackPanel panel = new StackPanel { Spacing = 4 };
        foreach (var child in children)
        {
            if (child != null) { panel.Children.Add(child); }
        }

        return panel;
    }

    /// <summary>Puts a label in front of a control.</summary>
    /// <param name="label">The label, with its accelerator marker.</param>
    /// <param name="control">The control.</param>
    /// <param name="labelWidth">How wide the label column is.</param>
    /// <returns>The row.</returns>
    protected static UIElement Labelled(
        string label, UIElement control, double labelWidth = 200)
    {
        Grid row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(labelWidth) });
        row.ColumnDefinitions.Add(new ColumnDefinition
        {
            Width = new GridLength(1, GridUnitType.Star),
        });

        TextBlock text = new TextBlock
        {
            Text = MenuBuilder.Display(label),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        row.Children.Add(text);
        Grid.SetColumn(control, 1);
        row.Children.Add(control);
        return row;
    }

    /// <summary>Builds a tick box that announces its own changes.</summary>
    /// <param name="label">The caption, with its accelerator marker.</param>
    /// <param name="toolTip">The tool tip, or null.</param>
    /// <returns>The box.</returns>
    protected CheckBox Tick(string label, string toolTip = null)
    {
        CheckBox box = new CheckBox { Content = MenuBuilder.Display(label) };
        if (!string.IsNullOrEmpty(toolTip)) { ToolTipService.SetToolTip(box, toolTip); }

        box.Checked += (_, _) => MarkChanged();
        box.Unchecked += (_, _) => MarkChanged();
        return box;
    }

    /// <summary>Builds a whole-number entry that announces its own changes.</summary>
    /// <param name="minimum">The smallest value.</param>
    /// <param name="maximum">The largest value.</param>
    /// <param name="specialText">What the smallest value is CALLED, or null —
    /// upstream's <c>setSpecialValueText</c>, which is how "0 spaces" reads as
    /// "Tab".</param>
    /// <param name="suffix">A word after the number, or null.</param>
    /// <returns>The entry.</returns>
    protected NumberEntry Number(
        int minimum, int maximum, string specialText = null, string suffix = null)
    {
        NumberEntry entry = new NumberEntry(minimum, maximum, specialText, suffix);
        entry.ValueChanged += (_, _) => MarkChanged();
        return entry;
    }

    /// <summary>Builds a text entry that announces its own changes.</summary>
    /// <param name="width">Its width, or 0 to stretch.</param>
    /// <returns>The entry.</returns>
    protected TextBox Entry(double width = 0)
    {
        TextBox box = new TextBox();
        if (width > 0)
        {
            box.Width = width;
        }
        else
        {
            box.HorizontalAlignment = HorizontalAlignment.Stretch;
        }

        box.TextChanged += (_, _) => MarkChanged();
        return box;
    }

    /// <summary>Builds a list to choose from that announces its own changes.</summary>
    /// <param name="items">The captions, in order.</param>
    /// <returns>The list.</returns>
    protected ComboBox Choice(params string[] items)
    {
        ComboBox list = new ComboBox { HorizontalAlignment = HorizontalAlignment.Stretch };
        foreach (var item in items)
        {
            list.Items.Add(new ComboBoxItem { Content = MenuBuilder.Display(item) });
        }

        list.SelectionChanged += (_, _) => MarkChanged();
        return list;
    }

    /// <summary>Builds a path entry with a Browse button.</summary>
    /// <param name="mode">What it picks.</param>
    /// <returns>The entry.</returns>
    protected UrlRequester Path(UrlRequesterMode mode = UrlRequesterMode.Directory)
    {
        UrlRequester requester = new UrlRequester(mode)
        {
            PickAsync = Context.PickAsync,
        };
        requester.Changed += (_, _) => MarkChanged();
        return requester;
    }

    /// <summary>Builds a wrapped explanatory paragraph.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The paragraph.</returns>
    protected static TextBlock Note(string text)
        => new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.85,
        };
}

/// <summary>
/// A whole number typed or nudged, with an optional name for its smallest
/// value.
/// </summary>
/// <remarks>
/// //was previously: <c>QSpinBox</c>. The platform's own number box is one more
/// control that would have to be proved on six heads; this is the same shape
/// the Score Wizard's settings editor already uses, and it paints everywhere.
/// </remarks>
public sealed class NumberEntry : Grid
{
    private readonly TextBox _box = new TextBox
    {
        Width = 96,
        TextAlignment = TextAlignment.Right,
    };

    //was previously: these two were locals in the constructor. They are fields
    //so that IsEntryEnabled can reach them: a Grid has no IsEnabled of its own,
    //and upstream's Music View page greys its fixed-scale controls out while a
    //fit mode is chosen (musicviewers.PageScaling.toggleFixedScaleControls).
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

    private readonly int _minimum;
    private readonly int _maximum;
    private readonly string _specialText;
    private readonly string _suffix;

    private int _value;
    private bool _writing;
    private bool _typing;

    /// <summary>Creates the entry.</summary>
    /// <param name="minimum">The smallest value.</param>
    /// <param name="maximum">The largest value.</param>
    /// <param name="specialText">What the smallest value is called, or null.</param>
    /// <param name="suffix">A word after the number, or null.</param>
    public NumberEntry(
        int minimum, int maximum, string specialText = null, string suffix = null)
    {
        _minimum = minimum;
        _maximum = maximum;
        _specialText = specialText;
        _suffix = suffix;
        _value = minimum;

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

        _less.Click += (_, _) => Value--;
        _more.Click += (_, _) => Value++;
        _box.TextChanged += (_, _) => Parse();
        _box.LostFocus += (_, _) => Display();

        Display();
    }

    /// <summary>Raised when the value changed.</summary>
    public event EventHandler ValueChanged;

    /// <summary>Gets or sets whether the value can be changed.</summary>
    /// <remarks>
    /// Upstream's <c>setEnabled</c> on a spin box. The entry is a
    /// <see cref="Grid"/> rather than a <c>Control</c>, so it has no
    /// <c>IsEnabled</c> of its own; the three parts that DO carry one are told
    /// instead, which is the same thing said out loud.
    /// </remarks>
    public bool IsEntryEnabled
    {
        get;
        set
        {
            field = value;
            _box.IsEnabled = value;
            _less.IsEnabled = value;
            _more.IsEnabled = value;
        }
    } = true;

    /// <summary>Gets or sets the value, clamped to the range.</summary>
    public int Value
    {
        get => _value;
        set
        {
            int clamped = Math.Clamp(value, _minimum, _maximum);
            if (_value == clamped) { return; }

            _value = clamped;
            Display();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Sets the value without announcing it — what loading uses, so
    /// that reading the settings in does not look like the user typing.</summary>
    /// <param name="value">The value.</param>
    public void SetValueQuietly(int value)
    {
        _value = Math.Clamp(value, _minimum, _maximum);
        Display();
    }

    private void Parse()
    {
        if (_writing) { return; }

        string text = _box.Text;
        if (!string.IsNullOrEmpty(_suffix) && text.EndsWith(_suffix, StringComparison.Ordinal))
        {
            text = text.Substring(0, text.Length - _suffix.Length);
        }

        if (!int.TryParse(
                text.Trim(),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out int parsed))
        {
            return;
        }

        //was previously: `Value = parsed;' on its own, which let the setter
        //REWRITE the box under the caret on every keystroke — so typing "15"
        //became the value 1, redisplayed as "1", and the 5 then landed in
        //front of it. The tidy form is written on LostFocus instead, which is
        //when a spin box normally reformats.
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
        _box.Text = Value == _minimum && _specialText != null
            ? _specialText
            : Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                + (_suffix ?? string.Empty);
        _writing = false;
    }
}
