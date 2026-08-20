// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.Simple;
using CodeBrix.Platform.UI.AdvancedTextEdit.Highlighting;
using Fresco.Brix.Documents;
using Fresco.Brix.Editor;
using Fresco.Brix.ViewModels;
using Microsoft.UI.Xaml.Controls;
using System; //Required: the IAsyncOperation GetAwaiter extension (awaiting the pickers) lives here
using System.Threading.Tasks;
using Windows.Storage.Pickers;

namespace Fresco.Brix.Views;

public sealed partial class MainPage : Page, IEditorBridge
{
    private LyHighlighter _highlighter;
    private AteLyDocument _lyDocument;
    private MatchHighlightRenderer _matchRenderer;

    public MainPage()
    {
        DataContextChanged += (_, _) =>
        {
            //Give the view model's SimpleDialog helpers a XamlRoot to attach dialogs to
            (DataContext as IXamlRootGetter)?.SetXamlRootGetter(() => XamlRoot);

            //Wire the head capabilities the view model drives
            if (DataContext is MainViewModel vm)
            {
                GetEditorText = () => Editor.Document.Text;
                SetEditorText = text =>
                {
                    Editor.Document.Text = text ?? string.Empty;

                    //Upstream opens a document at its start (the remembered
                    //cursor position arrives with metainfo); setting the text
                    //otherwise leaves the caret at the end.
                    Editor.CaretOffset = 0;
                    Editor.ScrollToHome();
                };
                PickOpenPathAsync = PickOpenAsync;
                PickSavePathAsync = PickSaveAsync;
                RefreshMode = () => _highlighter?.SetMode(null);
                vm.EditorBridge = this;
            }
        };

        this.InitializeComponent(); //Leave this line last

        //The parity highlighter: ly.lex tokens drawn through the editor's
        //colorizer pipeline; the same object is the app-wide token cache.
        _highlighter = new LyHighlighter(Editor.Document);
        Editor.TextArea.TextView.LineTransformers.Add(
            new HighlightingColorizer(_highlighter));

        //The ly-document bridge (every ported ly tool edits through this) and
        //the matching-pair highlight fed from it.
        _lyDocument = new AteLyDocument(Editor.Document, _highlighter);
        _matchRenderer = new MatchHighlightRenderer(Editor.TextArea.TextView);

        Editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            _matchRenderer.SetRanges(
                TokenMatcher.Matches(_lyDocument, Editor.TextArea.Caret.Offset));
            UpdateStatus();
        };
        Editor.Document.TextChanged += (_, _) =>
        {
            if (ViewModel != null)
            {
                ViewModel.IsModified = true;
            }

            UpdateStatus();
        };
        UpdateStatus();
    }

    private MainViewModel ViewModel => DataContext as MainViewModel;

    #region | IEditorBridge |

    public Func<string> GetEditorText { get; set; }

    public Action<string> SetEditorText { get; set; }

    public Func<Task<string>> PickOpenPathAsync { get; set; }

    public Func<string, Task<string>> PickSavePathAsync { get; set; }

    public Action RefreshMode { get; set; }

    #endregion

    private void UpdateStatus()
    {
        if (ViewModel == null)
        {
            return;
        }

        var caret = Editor.TextArea.Caret;
        ViewModel.StatusText =
            $"Line {caret.Line}, Col {caret.Column}    {_highlighter?.Mode}";
    }

    private async Task<string> PickOpenAsync()
    {
        var picker = new FileOpenPicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        };
        picker.FileTypeFilter.Add(".ly");
        picker.FileTypeFilter.Add(".ily");
        picker.FileTypeFilter.Add("*");

        var file = await picker.PickSingleFileAsync();
        return file?.Path;
    }

    private async Task<string> PickSaveAsync(string suggestedName)
    {
        var picker = new FileSavePicker
        {
            SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
            SuggestedFileName = suggestedName,
        };
        picker.FileTypeChoices.Add("LilyPond source", new[] { ".ly" });

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
}
