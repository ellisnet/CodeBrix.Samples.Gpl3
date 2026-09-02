// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;

namespace Fresco.Brix.Commands; //was previously: PyQt6 QAction, as Frescobaldi uses it

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One user-invocable command: the text a menu shows, the shortcuts that
/// trigger it, whether it is enabled, and (for toggles) whether it is checked.
/// <para>
/// This is the port's <c>QAction</c>. It is an <see cref="ICommand"/> so the
/// menus and toolbars bind to it directly, and it raises
/// <see cref="INotifyPropertyChanged"/> so a re-translation or an enable/
/// disable reaches the UI without rebuilding the menu.
/// </para>
/// </summary>
public sealed class AppAction : ICommand, INotifyPropertyChanged
{
    private IReadOnlyList<KeySequence> _shortcuts = Array.Empty<KeySequence>();

    /// <summary>Creates an action.</summary>
    /// <param name="name">The stable name it is stored and looked up under.</param>
    public AppAction(string name)
        => Name = name ?? throw new ArgumentNullException(nameof(name));

    /// <inheritdoc/>
    public event PropertyChangedEventHandler PropertyChanged;

    /// <inheritdoc/>
    public event EventHandler CanExecuteChanged;

    /// <summary>Raised when the action is invoked.</summary>
    /// <remarks>Upstream this is <c>QAction.triggered</c>.</remarks>
    public event EventHandler Triggered;

    /// <summary>Gets the stable name (e.g. <c>file_save_as</c>).</summary>
    public string Name { get; }

    /// <summary>Gets or sets the menu text, with its <c>&amp;</c> accelerator.</summary>
    public string Text
    {
        get;
        set => Set(ref field, value);
    } = string.Empty;

    /// <summary>Gets or sets the shorter text a toolbar button shows.</summary>
    /// <remarks>Upstream this is <c>QAction.iconText</c>; it falls back to
    /// <see cref="Text"/> when unset, as Qt's does.</remarks>
    public string IconText
    {
        get => field ?? Text;
        set => Set(ref field, value);
    }

    /// <summary>Gets or sets the tool tip.</summary>
    public string ToolTip
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>
    /// Gets or sets the icon name — an upstream icon-theme name such as
    /// <c>document-save</c>, resolved per head when icons land (W13 audits
    /// the icon assets; the name is recorded from the start).
    /// </summary>
    public string IconName
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>Gets or sets whether the action is a toggle.</summary>
    public bool IsCheckable
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>Gets or sets a toggle's state.</summary>
    public bool IsChecked
    {
        get;
        set => Set(ref field, value);
    }

    /// <summary>Gets or sets whether the action can be invoked.</summary>
    public bool IsEnabled
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                CanExecuteChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    } = true;

    /// <summary>Gets or sets the shortcuts that trigger the action.</summary>
    public IReadOnlyList<KeySequence> Shortcuts
    {
        get => _shortcuts;
        set
        {
            _shortcuts = value ?? Array.Empty<KeySequence>();
            NotifyPropertyChanged();
        }
    }

    /// <summary>Gets or sets what the action does.</summary>
    public Action Handler { get; set; }

    /// <summary>Gets or sets what the action does, asynchronously.</summary>
    /// <remarks>When both are set, the async handler runs after the sync one,
    /// which is how the file actions layer a state update over an await.</remarks>
    public Func<Task> AsyncHandler { get; set; }

    /// <summary>Sets the shortcuts and answers the action, for chaining.</summary>
    /// <param name="shortcuts">The shortcuts.</param>
    /// <returns>This action.</returns>
    public AppAction WithShortcuts(IReadOnlyList<KeySequence> shortcuts)
    {
        Shortcuts = shortcuts;
        return this;
    }

    /// <summary>Sets one shortcut and answers the action, for chaining.</summary>
    /// <param name="shortcut">The shortcut, in Qt's notation.</param>
    /// <returns>This action.</returns>
    public AppAction WithShortcut(string shortcut)
    {
        KeySequence key = KeySequence.Parse(shortcut);
        Shortcuts = key == null ? Array.Empty<KeySequence>() : new[] { key };
        return this;
    }

    /// <summary>Sets the icon name and answers the action, for chaining.</summary>
    /// <param name="iconName">The icon-theme name.</param>
    /// <returns>This action.</returns>
    public AppAction WithIcon(string iconName)
    {
        IconName = iconName;
        return this;
    }

    /// <summary>Marks the action as a toggle and answers it, for chaining.</summary>
    /// <param name="isChecked">The initial state.</param>
    /// <returns>This action.</returns>
    public AppAction AsToggle(bool isChecked = false)
    {
        IsCheckable = true;
        IsChecked = isChecked;
        return this;
    }

    /// <summary>Sets what the action does and answers it, for chaining.</summary>
    /// <param name="handler">The handler.</param>
    /// <returns>This action.</returns>
    public AppAction Does(Action handler)
    {
        Handler = handler;
        return this;
    }

    /// <summary>Sets what the action does and answers it, for chaining.</summary>
    /// <param name="handler">The asynchronous handler.</param>
    /// <returns>This action.</returns>
    public AppAction Does(Func<Task> handler)
    {
        AsyncHandler = handler;
        return this;
    }

    /// <summary>Invokes the action, as if the user had picked it.</summary>
    /// <remarks>Upstream this is <c>QAction.trigger()</c>.</remarks>
    public void Trigger()
    {
        if (!IsEnabled) { return; }

        if (IsCheckable)
        {
            IsChecked = !IsChecked;
        }

        Handler?.Invoke();
        Triggered?.Invoke(this, EventArgs.Empty);

        if (AsyncHandler != null)
        {
            //was previously: `_ = AsyncHandler();' — fire and forget. A command
            //whose handler FAILED then failed in silence: the fault stayed in
            //the returned Task, which nobody awaited, so neither the platform's
            //Application.UnhandledException nor the AppDomain's ever saw it and
            //the user's only clue was that nothing happened. The fault is
            //observed here and handed to whoever is listening — the
            //application's own "Internal Error" window (Shell/InternalErrorDialog)
            //— which is where a Python excepthook would have taken it.
            _ = RunAsync();
        }
    }

    /// <summary>
    /// Gets or sets what to do with a failure inside a command, or null to let
    /// it be re-thrown on the thread pool.
    /// </summary>
    /// <remarks>The application points this at its own crash reporter once the
    /// window can show one.</remarks>
    public static Action<Exception> FailureHandler { get; set; }

    /// <summary>Runs the asynchronous handler and reports what it threw.</summary>
    /// <returns>The task.</returns>
    private async Task RunAsync()
    {
        try
        {
            await AsyncHandler().ConfigureAwait(true);
        }
        catch (Exception failure)
        {
            Action<Exception> report = FailureHandler;
            if (report == null) { throw; }

            report(failure);
        }
    }

    /// <inheritdoc/>
    public bool CanExecute(object parameter) => IsEnabled;

    /// <inheritdoc/>
    public void Execute(object parameter) => Trigger();

    /// <summary>Announces that a bound property changed.</summary>
    /// <param name="propertyName">The property; supplied by the compiler.</param>
    private void NotifyPropertyChanged([CallerMemberName] string propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(storage, value))
        {
            return false;
        }

        storage = value;
        NotifyPropertyChanged(propertyName);
        return true;
    }
}
