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
using System.Diagnostics;
using System.Threading.Tasks;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/exception.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// What the application says when something inside it fails: the error, the
/// stack it failed on, and an invitation to report it.
/// </summary>
/// <remarks>
/// <para>
/// Upstream installs <c>exception.ExceptionDialog</c> as the process's
/// <c>sys.excepthook</c>, so an unhandled exception puts a window in front of
/// the user rather than vanishing. Nothing here did that at all: an unhandled
/// exception on the UI thread was silent, and the user's next clue was that a
/// command had stopped working.
/// </para>
/// <para>
/// ⚠ ONE DELIBERATE OMISSION. Upstream's dialog exists to feed
/// <c>bugreport.py</c>'s "Send Bug Report..." button, which opens a prefilled
/// GitHub issue or an email. <c>bugreport.py</c> is post-v1 on the board, so
/// there is no button — the dialog says what went wrong, shows the stack so it
/// can be copied, and asks the user to report it by hand. That sentence is a
/// Fresco.Brix-original msgid and is in the harvest tool's renamed-string
/// table; the other two strings are upstream's own.
/// </para>
/// <para>
/// The exception is never swallowed silently: it reaches
/// <see cref="Debug"/> and the console before any dialog is attempted, so a
/// failure while the window is being torn down — when no dialog can be
/// shown — still leaves a trace. Marking the platform's event handled is
/// upstream's own behaviour: Frescobaldi keeps running after an unhandled
/// exception, because losing the user's unsaved documents to a bug in a menu
/// command would be worse than the bug.
/// </para>
/// </remarks>
public static class InternalErrorDialog
{
    private static Func<XamlRoot> _root;
    private static Action<Action> _toUiThread;
    private static bool _installed;
    private static bool _showing;

    /// <summary>
    /// Starts catching what nothing else caught.
    /// </summary>
    /// <param name="xamlRoot">Reads the root a dialog can be attached to.
    /// ⚠ A FUNCTION, not a root: a Page has no XamlRoot until it is IN the
    /// visual tree, and this is installed while the window is still being
    /// built. Capturing the value there captured null, and every failure was
    /// then written to the log and shown to nobody.</param>
    /// <param name="toUiThread">How to get back onto the UI thread, for a
    /// failure raised on another one.</param>
    /// <remarks>Called once, by the window, as soon as it has a root to show a
    /// dialog on. Both hooks are installed: the platform's own
    /// <c>Application.UnhandledException</c> is what catches a UI-thread
    /// failure, and <c>AppDomain.CurrentDomain.UnhandledException</c> is the
    /// fallback for a background thread, where the process is usually going
    /// down and the most that can be done is to say so.</remarks>
    public static void Install(Func<XamlRoot> xamlRoot, Action<Action> toUiThread)
    {
        _root = xamlRoot;
        _toUiThread = toUiThread;
        if (_installed) { return; }

        _installed = true;

        Application application = Application.Current;
        if (application != null)
        {
            application.UnhandledException += (_, e) =>
            {
                e.Handled = true;
                Report(e.Exception);
            };
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Report(e.ExceptionObject as Exception);

        //A command's ASYNCHRONOUS handler is the common case, and a fault in
        //one never reaches either hook above: it stays in a Task nobody
        //awaited. Commands/AppAction observes it and hands it here.
        Commands.AppAction.FailureHandler = Report;

        //And anything else that faults a Task nobody looked at: the CLR raises
        //this when the Task is collected, which is late but is better than
        //never — and marking it observed stops it taking the process down.
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            e.SetObserved();
            Report(e.Exception);
        };
    }

    /// <summary>Says that something failed, and shows the stack.</summary>
    /// <param name="error">The failure, or null when there is no object.</param>
    public static void Report(Exception error)
    {
        //FIRST, and whatever happens next: a failure that cannot be shown must
        //still be findable.
        string text = error?.ToString() ?? I18n.Get("An internal error has occurred:");
        Debug.WriteLine("Fresco.Brix internal error: " + text);
        Console.Error.WriteLine("Fresco.Brix internal error: " + text);

        if (_root == null || _showing) { return; }

        Action show = () => _ = ShowAsync(error);
        if (_toUiThread != null) { _toUiThread(show); } else { show(); }
    }

    /// <summary>Puts the dialog on the screen.</summary>
    /// <param name="error">The failure.</param>
    /// <returns>The task.</returns>
    private static async Task ShowAsync(Exception error)
    {
        //One at a time: a bug that raises again while the dialog is up would
        //otherwise stack dialogs until the process died of it.
        XamlRoot root = _root?.Invoke();
        if (_showing || root == null) { return; }

        _showing = true;
        try
        {
            StackPanel panel = new StackPanel { Spacing = 8, MinWidth = 520 };
            panel.Children.Add(new TextBlock
            {
                Text = I18n.Get("An internal error has occurred:"),
                TextWrapping = TextWrapping.Wrap,
            });
            panel.Children.Add(new TextBox
            {
                Text = error?.ToString() ?? string.Empty,
                IsReadOnly = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.NoWrap,
                Height = 260,
                FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(
                    "ms-appx:///CodeBrix.Platform.Fonts.RobotoMono/Fonts/RobotoMono.ttf"),
            });
            panel.Children.Add(new TextBlock
            {
                //was previously (upstream): a paragraph asking the user to open
                //a GitHub issue, beside a "Send Bug Report..." button. There is
                //no such button here — see the class remarks.
                Text = I18n.Format(
                    I18n.Get(
                        "This is a bug in {appname}. The text above says where "
                        + "it happened; please report it, with what you were "
                        + "doing at the time."),
                    ("appname", AppInfo.AppName)),
                TextWrapping = TextWrapping.Wrap,
                Opacity = 0.85,
            });

            ContentDialog dialog = new ContentDialog
            {
                Title = I18n.Get("Internal Error"),
                Content = new ScrollViewer
                {
                    Content = panel,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                },
                CloseButtonText = StandardButtons.Ok,
                DefaultButton = ContentDialogButton.Close,
                XamlRoot = root,
            };
            DialogSizing.Clamp(dialog, 720, 520);
            await dialog.ShowAsync();
        }
        catch (Exception failure)
        {
            //A dialog that cannot be shown must not become the next unhandled
            //exception; the failure has already been written out above.
            Debug.WriteLine("Fresco.Brix could not show the error dialog: " + failure);
        }
        finally
        {
            _showing = false;
        }
    }
}
