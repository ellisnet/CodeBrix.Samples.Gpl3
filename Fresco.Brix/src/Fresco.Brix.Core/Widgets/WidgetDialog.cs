// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Fresco.Brix.Widgets; //was previously: frescobaldi/widgets/dialog.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A dialog with a message above whatever the caller puts in it, and OK/Cancel
/// below.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>widgets.dialog.Dialog</c> is a <c>QDialog</c> that lays out an
/// icon, a message label, a main widget, a separator and a
/// <c>QDialogButtonBox</c>. A <c>ContentDialog</c> already draws the title, the
/// separator and the buttons, so what is left to port is the message-over-content
/// arrangement and the button set — and the icon column, which is dropped
/// because there are no icon assets until W13 and a blank column is worse than
/// none.
/// </para>
/// <para>
/// ⚠ A <c>ContentDialog</c>'s width is the <c>ContentDialogMaxWidth</c>
/// RESOURCE, not its <c>MaxWidth</c> (board trap 43); the content carries a
/// <c>MinWidth</c> so a short message does not collapse the box. Three buttons
/// is the limit (trap 50), which is what upstream's OK/Cancel/Help needs — so
/// Help goes INSIDE the content, under whatever the caller put there, and only
/// when <see cref="HelpPage"/> names a page.
/// </para>
/// </remarks>
public class WidgetDialog
{
    private readonly StackPanel _panel = new StackPanel { Spacing = 8, MinWidth = 320 };
    private readonly TextBlock _message = new TextBlock { TextWrapping = TextWrapping.Wrap };

    private UIElement _mainElement;
    private StackPanel _help;

    /// <summary>Creates a dialog.</summary>
    /// <param name="title">The window title, without the application name.</param>
    /// <param name="message">The message shown above the content.</param>
    public WidgetDialog(string title = null, string message = null)
    {
        Title = title;
        Message = message;
        _panel.Children.Add(_message);
    }

    /// <summary>Gets or sets the dialog's title.</summary>
    public string Title { get; set; }

    /// <summary>Gets or sets the message shown above the content.</summary>
    public string Message
    {
        get => _message.Text;
        set
        {
            _message.Text = value ?? string.Empty;
            _message.Visibility = string.IsNullOrEmpty(value)
                ? Visibility.Collapsed
                : Visibility.Visible;
        }
    }

    /// <summary>Gets or sets the affirmative button's caption.</summary>
    public string AcceptText { get; set; }

    /// <summary>Gets or sets the dismissive button's caption.</summary>
    public string RejectText { get; set; }

    /// <summary>
    /// Gets or sets the user-guide page this dialog's Help button opens; null
    /// means the dialog has no Help button.
    /// </summary>
    /// <remarks>Upstream's <c>Dialog(help=…)</c> argument, which its
    /// constructor turns into <c>userguide.addButton(self._buttonBox,
    /// help)</c>.</remarks>
    public string HelpPage
    {
        get;
        set
        {
            field = value;
            if (_help != null) { _panel.Children.Remove(_help); }

            _help = value == null
                ? null
                : new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                };
            if (_help != null)
            {
                _help.Children.Add(UserGuide.GuideHelp.Button(value));
                _panel.Children.Add(_help);
            }
        }
    }

    /// <summary>Gets or sets whether the affirmative button can be pressed.</summary>
    public bool IsAcceptEnabled
    {
        get;
        set
        {
            field = value;
            if (Dialog != null) { Dialog.IsPrimaryButtonEnabled = value; }
        }
    } = true;

    /// <summary>Gets the dialog while it is on screen, or null.</summary>
    protected ContentDialog Dialog { get; private set; }

    /// <summary>Sets the element shown under the message.</summary>
    /// <param name="element">The element; null removes the current one.</param>
    /// <remarks>Upstream's <c>setMainWidget</c>.</remarks>
    public void SetMainElement(UIElement element)
    {
        if (_mainElement != null) { _panel.Children.Remove(_mainElement); }

        _mainElement = element;
        if (element == null) { return; }

        //The Help row, when there is one, stays at the bottom.
        int at = _help != null ? _panel.Children.IndexOf(_help) : -1;
        if (at < 0)
        {
            _panel.Children.Add(element);
        }
        else
        {
            _panel.Children.Insert(at, element);
        }
    }

    /// <summary>Puts the dialog in front of the user.</summary>
    /// <param name="xamlRoot">The root to attach it to.</param>
    /// <returns>Whether the user accepted.</returns>
    /// <remarks>Upstream's <c>exec()</c>.</remarks>
    public async Task<bool> ShowAsync(XamlRoot xamlRoot)
    {
        Dialog = new ContentDialog
        {
            Title = Title,
            Content = _panel,
            PrimaryButtonText = AcceptText ?? StandardButtons.Ok,
            CloseButtonText = RejectText ?? StandardButtons.Cancel,
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = IsAcceptEnabled,
            XamlRoot = xamlRoot,
        };

        try
        {
            return await Dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            Dialog = null;
        }
    }
}

/// <summary>
/// A dialog asking for one line of text, with optional validation that greys
/// out OK while what is typed is not acceptable.
/// </summary>
/// <remarks>Upstream's <c>widgets.dialog.TextDialog</c>.</remarks>
public sealed class TextDialog : WidgetDialog
{
    private readonly StackPanel _panel = new StackPanel
    {
        Spacing = 8,
        MinWidth = 320,
    };

    private readonly TextBox _box = new TextBox
    {
        AcceptsReturn = false,
        MinWidth = 320,
    };

    private Func<string, bool> _validate;

    /// <summary>Creates a text dialog.</summary>
    /// <param name="title">The window title.</param>
    /// <param name="message">The question.</param>
    public TextDialog(string title = null, string message = null)
        : base(title, message)
    {
        SetMainElement(_panel);
        _panel.Children.Add(_box);
        _box.TextChanged += (_, _) => Revalidate();
    }

    /// <summary>
    /// Adds a control under the text box — upstream's own way of extending
    /// this dialog: <c>TemplateDialog</c> replaces the main widget with a
    /// panel holding the line edit and one checkbox.
    /// </summary>
    /// <param name="element">The control.</param>
    public void AddUnderBox(UIElement element)
    {
        if (element != null) { _panel.Children.Add(element); }
    }

    /// <summary>Gets or sets the text in the box.</summary>
    public string Text
    {
        get => _box.Text;
        set
        {
            _box.Text = value ?? string.Empty;
            Revalidate();
        }
    }

    /// <summary>
    /// Sets a test the text must pass before OK becomes available; null removes
    /// an earlier one.
    /// </summary>
    /// <param name="validate">The test.</param>
    public void SetValidateFunction(Func<string, bool> validate)
    {
        _validate = validate;
        Revalidate();
    }

    /// <summary>
    /// Sets a regular expression the WHOLE text must match; null removes an
    /// earlier one.
    /// </summary>
    /// <param name="pattern">The expression.</param>
    /// <remarks>Upstream anchors by using a <c>QRegularExpressionValidator</c>,
    /// which always attempts an exact match; the anchors are explicit here.</remarks>
    public void SetValidateExpression(string pattern)
    {
        if (pattern == null)
        {
            SetValidateFunction(null);
            return;
        }

        Regex expression = new Regex("^(?:" + pattern + ")$");
        SetValidateFunction(text => !string.IsNullOrEmpty(text) && expression.IsMatch(text));
    }

    private void Revalidate()
        => IsAcceptEnabled = _validate == null || _validate(_box.Text);
}
