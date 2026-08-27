// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Fresco.Brix.ScoreWizard; //was previously: the QWidgets frescobaldi/scorewiz/parts/*.py build in createWidgets()

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One adjustable setting of a part type — a check box, a number, a text
/// entry, a list to pick from, a notice, or a group of those.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's part types ARE Qt widgets: <c>createWidgets</c> builds a
/// <c>QSpinBox</c> and <c>build()</c> reads <c>self.voices.value()</c> back
/// out of it. That welds the document a part produces to a live window, and it
/// is why Frescobaldi's score wizard cannot be tested without one.
/// </para>
/// <para>
/// //was previously: those widgets. Here a part DESCRIBES its settings and
/// reads their values; the dialog renders the description. The user sees the
/// same controls in the same order with the same labels and tooltips, and the
/// whole wizard — every part type, the builder, the preview — runs and is
/// tested with no window at all. <see cref="Key"/> is upstream's own widget
/// attribute name, which is what lets the parity fixtures name a setting.
/// </para>
/// </remarks>
public abstract class PartSetting
{
    private bool _isEnabled = true;

    /// <summary>Initializes the setting.</summary>
    /// <param name="key">Upstream's widget attribute name.</param>
    protected PartSetting(string key) => Key = key;

    /// <summary>Raised when the value or the enabled state changed.</summary>
    public event EventHandler Changed;

    /// <summary>Gets the setting's key — upstream's widget attribute name.</summary>
    public string Key { get; }

    /// <summary>Gets or sets what names the setting on screen.</summary>
    /// <remarks>A function, not a string: the label is translated when it is
    /// shown, so a language change re-reads it (standing rule 7).</remarks>
    public Func<string> Label { get; set; }

    /// <summary>Gets or sets the setting's tooltip, or null.</summary>
    public Func<string> ToolTip { get; set; }

    /// <summary>Gets or sets whether the user may change the setting.</summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) { return; }

            _isEnabled = value;
            RaiseChanged();
        }
    }

    /// <summary>Gets the label's text, or the empty string.</summary>
    /// <returns>The text.</returns>
    public string LabelText() => Label?.Invoke() ?? string.Empty;

    /// <summary>Gets the tooltip's text, or null.</summary>
    /// <returns>The text.</returns>
    public string ToolTipText() => ToolTip?.Invoke();

    /// <summary>Announces a change.</summary>
    protected void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>A check box.</summary>
public sealed class BoolSetting : PartSetting
{
    private bool _value;

    /// <summary>Initializes the setting.</summary>
    /// <param name="key">Upstream's widget attribute name.</param>
    /// <param name="value">The starting value.</param>
    public BoolSetting(string key, bool value = false)
        : base(key)
        => _value = value;

    /// <summary>Gets or sets whether the box is ticked.</summary>
    public bool Value
    {
        get => _value;
        set
        {
            if (_value == value) { return; }

            _value = value;
            RaiseChanged();
        }
    }
}

/// <summary>A number entry with a range.</summary>
public sealed class NumberSetting : PartSetting
{
    private int _minimum;
    private int _maximum;
    private int _value;

    /// <summary>Initializes the setting.</summary>
    /// <param name="key">Upstream's widget attribute name.</param>
    /// <param name="minimum">The smallest allowed value.</param>
    /// <param name="maximum">The largest allowed value.</param>
    /// <param name="value">The starting value.</param>
    public NumberSetting(string key, int minimum, int maximum, int value)
        : base(key)
    {
        _minimum = minimum;
        _maximum = maximum;
        _value = Math.Max(minimum, Math.Min(maximum, value));
    }

    /// <summary>Gets or sets the smallest allowed value.</summary>
    /// <remarks>Lowering or raising it pulls the value along, which is what
    /// <c>QSpinBox.setMinimum</c> does and what the piano-staff voice-count
    /// interlock relies on.</remarks>
    public int Minimum
    {
        get => _minimum;
        set
        {
            if (_minimum == value) { return; }

            _minimum = value;
            if (_value < value) { Value = value; } else { RaiseChanged(); }
        }
    }

    /// <summary>Gets or sets the largest allowed value.</summary>
    public int Maximum
    {
        get => _maximum;
        set
        {
            if (_maximum == value) { return; }

            _maximum = value;
            if (_value > value) { Value = value; } else { RaiseChanged(); }
        }
    }

    /// <summary>Gets or sets the value, clamped to the range.</summary>
    public int Value
    {
        get => _value;
        set
        {
            int clamped = Math.Max(_minimum, Math.Min(_maximum, value));
            if (_value == clamped) { return; }

            _value = clamped;
            RaiseChanged();
        }
    }
}

/// <summary>A single-line text entry.</summary>
public sealed class TextSetting : PartSetting
{
    private string _value = string.Empty;

    /// <summary>Initializes the setting.</summary>
    /// <param name="key">Upstream's widget attribute name.</param>
    public TextSetting(string key)
        : base(key)
    {
    }

    /// <summary>Gets or sets the text.</summary>
    public string Value
    {
        get => _value;
        set
        {
            string text = value ?? string.Empty;
            if (string.Equals(_value, text, StringComparison.Ordinal)) { return; }

            _value = text;
            RaiseChanged();
        }
    }

    /// <summary>Gets or sets the greyed-out hint shown while empty.</summary>
    public Func<string> PlaceholderText { get; set; }
}

/// <summary>One row of a <see cref="ChoiceSetting"/>.</summary>
public sealed class ChoiceItem
{
    /// <summary>Initializes the row.</summary>
    /// <param name="label">What the row reads as, translated on demand.</param>
    /// <param name="tag">The value the row stands for, or null.</param>
    /// <param name="toolTip">The row's tooltip, or null.</param>
    public ChoiceItem(Func<string> label, object tag = null, Func<string> toolTip = null)
    {
        Label = label;
        Tag = tag;
        ToolTip = toolTip;
    }

    /// <summary>Initializes a row that reads as itself.</summary>
    /// <param name="text">The text, which is also the value.</param>
    public ChoiceItem(string text)
        : this(() => text, text)
    {
    }

    /// <summary>Gets what the row reads as.</summary>
    public Func<string> Label { get; }

    /// <summary>Gets the value the row stands for.</summary>
    public object Tag { get; }

    /// <summary>Gets the row's tooltip, or null.</summary>
    public Func<string> ToolTip { get; }

    /// <summary>Gets the row's text.</summary>
    /// <returns>The text.</returns>
    public string LabelText() => Label?.Invoke() ?? string.Empty;
}

/// <summary>A list to pick from, optionally with a typed-in value.</summary>
public sealed class ChoiceSetting : PartSetting
{
    private readonly List<ChoiceItem> _items = new List<ChoiceItem>();
    private int _selectedIndex = -1;
    private string _editText;

    /// <summary>Initializes the setting.</summary>
    /// <param name="key">Upstream's widget attribute name.</param>
    /// <param name="items">The rows.</param>
    /// <param name="selectedIndex">The row that starts out picked.</param>
    /// <param name="isEditable">Whether the user may type a value of their own.</param>
    public ChoiceSetting(
        string key,
        IEnumerable<ChoiceItem> items,
        int selectedIndex = 0,
        bool isEditable = false)
        : base(key)
    {
        if (items != null) { _items.AddRange(items); }

        IsEditable = isEditable;
        _selectedIndex = _items.Count == 0 ? -1 : selectedIndex;
    }

    /// <summary>Gets the rows.</summary>
    public IReadOnlyList<ChoiceItem> Items => new ReadOnlyCollection<ChoiceItem>(_items);

    /// <summary>Gets whether the user may type a value of their own.</summary>
    public bool IsEditable { get; }

    /// <summary>Gets or sets the picked row, or -1.</summary>
    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (_selectedIndex == value && _editText == null) { return; }

            _selectedIndex = value;
            _editText = null;
            RaiseChanged();
        }
    }

    /// <summary>Gets the value the picked row stands for, or null.</summary>
    public object SelectedTag
        => _selectedIndex >= 0 && _selectedIndex < _items.Count
            ? _items[_selectedIndex].Tag
            : null;

    /// <summary>
    /// Gets the current text: what the user typed, or the picked row's text.
    /// </summary>
    public string Text
        => _editText
            ?? (_selectedIndex >= 0 && _selectedIndex < _items.Count
                ? _items[_selectedIndex].LabelText()
                : string.Empty);

    /// <summary>
    /// Sets the text the way a combo box does: a row that reads exactly like
    /// this becomes the picked one, and anything else is typed-in text.
    /// </summary>
    /// <param name="text">The text.</param>
    public void SetText(string text)
    {
        string wanted = text ?? string.Empty;
        for (int index = 0; index < _items.Count; index++)
        {
            if (string.Equals(_items[index].LabelText(), wanted, StringComparison.Ordinal))
            {
                SelectedIndex = index;
                return;
            }
        }

        _editText = wanted;
        RaiseChanged();
    }
}

/// <summary>A paragraph of explanation, with no value of its own.</summary>
public sealed class NoticeSetting : PartSetting
{
    /// <summary>Initializes the notice.</summary>
    /// <param name="key">A name for it; nothing reads it.</param>
    /// <param name="text">The text.</param>
    public NoticeSetting(string key, Func<string> text)
        : base(key)
        => Label = text;
}

/// <summary>A group of settings, optionally with a tick of its own.</summary>
public sealed class GroupSetting : PartSetting
{
    private readonly List<PartSetting> _children = new List<PartSetting>();
    private bool _isChecked;

    /// <summary>Initializes the group.</summary>
    /// <param name="key">Upstream's widget attribute name.</param>
    /// <param name="isCheckable">Whether the group has a tick of its own.</param>
    /// <param name="isChecked">Whether that tick starts out ticked.</param>
    public GroupSetting(string key, bool isCheckable = false, bool isChecked = true)
        : base(key)
    {
        IsCheckable = isCheckable;
        _isChecked = isChecked;
    }

    /// <summary>Gets whether the group has a tick of its own.</summary>
    public bool IsCheckable { get; }

    /// <summary>Gets or sets whether the group's own tick is ticked.</summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) { return; }

            _isChecked = value;
            RaiseChanged();
        }
    }

    /// <summary>Gets the settings inside the group.</summary>
    public IReadOnlyList<PartSetting> Children
        => new ReadOnlyCollection<PartSetting>(_children);

    /// <summary>Adds a setting to the group.</summary>
    /// <typeparam name="T">The setting's type.</typeparam>
    /// <param name="setting">The setting.</param>
    /// <returns>The setting, so a field can be assigned from the call.</returns>
    public T Add<T>(T setting)
        where T : PartSetting
    {
        _children.Add(setting);
        setting.Changed += (_, _) => RaiseChanged();
        return setting;
    }
}
