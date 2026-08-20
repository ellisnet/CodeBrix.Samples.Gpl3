// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.Simple;
using Fresco.Brix.Services;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace Fresco.Brix.ViewModels;

/// <summary>
/// The head-capability bridge the page fills in: the editor's document text,
/// the file dialogs, and the mode refresh. The view model behaves sensibly
/// when a delegate is null (the FrameBuffer head has no dialogs).
/// </summary>
public interface IEditorBridge
{
    /// <summary>Gets or sets the getter for the editor's whole text.</summary>
    Func<string> GetEditorText { get; set; }

    /// <summary>Gets or sets the setter for the editor's whole text (resets
    /// undo history and re-highlights for the new content).</summary>
    Action<string> SetEditorText { get; set; }

    /// <summary>Gets or sets the "pick a file to open" dialog; answers the
    /// chosen path or <see langword="null"/>.</summary>
    Func<Task<string>> PickOpenPathAsync { get; set; }

    /// <summary>Gets or sets the "pick a save path" dialog, seeded with a
    /// suggested name; answers the chosen path or <see langword="null"/>.</summary>
    Func<string, Task<string>> PickSavePathAsync { get; set; }

    /// <summary>Gets or sets the action re-guessing the tokenizer mode after a
    /// load (the highlighter re-reads the document).</summary>
    Action RefreshMode { get; set; }
}

[Microsoft.UI.Xaml.Data.Bindable]
public class MainViewModel : SimpleViewModel
{
    public MainViewModel()
    {
        if (IsDesignMode(true)) { return; } //Leave as the first line of constructor

        Debug.WriteLine("Fresco.Brix main view model startup.");

        _recentFiles = GetService<RecentFiles>();
        ReloadRecentFiles();
    }

    private readonly RecentFiles _recentFiles;

    /// <summary>The bridge the page wires; stays null in design mode.</summary>
    public IEditorBridge EditorBridge { get; set; }

    #region | Bindable properties |

    /// <summary>The open file's full path, or null for a new document.</summary>
    public string CurrentFilePath
    {
        get;
        private set
        {
            SetProperty(ref field, value);
            NotifyPropertyChanged(nameof(WindowTitle));
        }
    }

    /// <summary>Whether the document changed since the last save.</summary>
    public bool IsModified
    {
        get;
        set
        {
            SetProperty(ref field, value);
            NotifyPropertyChanged(nameof(WindowTitle));
        }
    }

    /// <summary>The title: file name (or Untitled), a modified star, the app name.</summary>
    public string WindowTitle
    {
        get
        {
            var name = string.IsNullOrEmpty(CurrentFilePath)
                ? "Untitled"
                : Path.GetFileName(CurrentFilePath);
            var star = IsModified ? "*" : string.Empty;
            return $"{name}{star} - Fresco.Brix";
        }
    }

    /// <summary>The status bar text (caret position, mode), set by the page.</summary>
    public string StatusText
    {
        get;
        set => SetProperty(ref field, value ?? string.Empty);
    } = string.Empty;

    /// <summary>The recently-opened documents, most recent first — the same
    /// list upstream shows under File &gt; Open recent (the real menu arrives
    /// with the shell's command system; this is its first-light surface).</summary>
    public ObservableCollection<string> RecentFilePaths { get; }
        = new ObservableCollection<string>();

    /// <summary>The recent entry the user picked; setting it opens that
    /// document and clears back to null so the same entry can be picked
    /// again.</summary>
    public string SelectedRecentPath
    {
        get;
        set
        {
            SetProperty(ref field, value);
            if (!string.IsNullOrEmpty(value))
            {
                _ = OpenRecentAsync(value);
            }
        }
    }

    #endregion

    #region | Commands and their implementations |

    public SimpleCommand NewCommand => field ??= new SimpleCommand(DoNew);

    public SimpleCommand OpenCommand => field ??=
        new SimpleCommand((Func<object, Task>)(_ => DoOpenAsync()));

    public SimpleCommand SaveCommand => field ??=
        new SimpleCommand((Func<object, Task>)(_ => DoSaveAsync(saveAs: false)));

    public SimpleCommand SaveAsCommand => field ??=
        new SimpleCommand((Func<object, Task>)(_ => DoSaveAsync(saveAs: true)));

    private void DoNew()
    {
        EditorBridge?.SetEditorText?.Invoke(string.Empty);
        CurrentFilePath = null;
        IsModified = false;
        EditorBridge?.RefreshMode?.Invoke();
    }

    private async Task DoOpenAsync()
    {
        var pick = EditorBridge?.PickOpenPathAsync;
        if (pick == null) { return; }

        var path = await pick();
        if (string.IsNullOrEmpty(path)) { return; }

        await LoadFileAsync(path);
    }

    /// <summary>Loads a file into the editor (also the open-a-path entry the
    /// command line and recent-files list will use).</summary>
    /// <param name="path">The file to load.</param>
    public async Task LoadFileAsync(string path)
    {
        var text = (await File.ReadAllTextAsync(path)).Replace("\r", string.Empty);
        EditorBridge?.SetEditorText?.Invoke(text);
        CurrentFilePath = path;
        IsModified = false;
        EditorBridge?.RefreshMode?.Invoke();
        Remember(path);
    }

    private async Task OpenRecentAsync(string path)
    {
        if (File.Exists(path))
        {
            await LoadFileAsync(path);
        }
        else
        {
            //Upstream forgets entries that no longer resolve
            _recentFiles?.Remove(path);
            ReloadRecentFiles();
        }

        SelectedRecentPath = null;
    }

    /// <summary>Adds a path to the persisted recent list and refreshes the
    /// bound copy.</summary>
    /// <param name="path">The document path.</param>
    private void Remember(string path)
    {
        if (_recentFiles == null) { return; }

        _recentFiles.Add(path);
        ReloadRecentFiles();
    }

    private void ReloadRecentFiles()
    {
        RecentFilePaths.Clear();
        if (_recentFiles == null) { return; }

        foreach (var path in _recentFiles.Paths())
        {
            RecentFilePaths.Add(path);
        }
    }

    private async Task DoSaveAsync(bool saveAs)
    {
        var path = CurrentFilePath;
        if (saveAs || string.IsNullOrEmpty(path))
        {
            var pick = EditorBridge?.PickSavePathAsync;
            if (pick == null) { return; }

            var suggested = string.IsNullOrEmpty(CurrentFilePath)
                ? "Untitled.ly"
                : Path.GetFileName(CurrentFilePath);
            path = await pick(suggested);
            if (string.IsNullOrEmpty(path)) { return; }
        }

        var text = EditorBridge?.GetEditorText?.Invoke() ?? string.Empty;
        await File.WriteAllTextAsync(path, text);
        CurrentFilePath = path;
        IsModified = false;
        Remember(path);
    }

    #endregion
}
