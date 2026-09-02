// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Services;
using Fresco.Brix.Tools;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.UI;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/externalchanges/widget.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The window shown when documents are modified or deleted by other programs:
/// what changed, and what to do about each one.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>ChangedDocumentsListDialog</c>, with three platform
/// substitutions and nothing else changed:
/// </para>
/// <list type="bullet">
/// <item><description>the five action buttons live INSIDE the content rather
/// than in the button row — board trap 50 allows three buttons there, and
/// Close is the one upstream puts in its own button box;</description></item>
/// <item><description>Show Difference swaps the content for the comparison and
/// back, where upstream opens a SECOND non-modal window: a
/// <c>ContentDialog</c> cannot be shown while another is up;</description></item>
/// <item><description>a failure to reload or save is reported in a line under
/// the list rather than in a nested message box, for the same
/// reason.</description></item>
/// </list>
/// <para>
/// ⚠ Upstream's window is NON-MODAL (<c>WindowModality.NonModal</c>) and stays
/// up while the user works; a <c>ContentDialog</c> is modal, so this one is
/// answered and dismissed. Everything it offers is a decision about documents
/// the user is not editing at that moment, so nothing is lost by it.
/// </para>
/// </remarks>
public sealed class ChangedDocumentsDialog
{
    private static readonly Color ErrorColor = Color.FromArgb(255, 176, 0, 0);
    private static readonly Color AddedColor = Color.FromArgb(255, 0, 120, 0);
    private static readonly Color RemovedColor = Color.FromArgb(255, 176, 0, 0);

    private readonly ExternalChanges _service;
    private readonly List<EditorDocument> _documents = new List<EditorDocument>();
    private readonly Dictionary<TreeViewNode, EditorDocument> _nodes
        = new Dictionary<TreeViewNode, EditorDocument>();

    private TreeView _tree;
    private Button _reload;
    private Button _reloadAll;
    private Button _save;
    private Button _saveAll;
    private Button _showDiff;
    private CheckBox _watchingEnabled;
    private TextBlock _error;
    private StackPanel _listPage;
    private StackPanel _diffPage;
    private StackPanel _diffLines;
    private TextBlock _diffMessage;
    private ContentDialog _dialog;
    private StackPanel _content;

    /// <summary>Creates the window over the external-changes service.</summary>
    /// <param name="service">The service, whose enabled flag the tick box
    /// carries and whose watcher answers what was deleted.</param>
    public ChangedDocumentsDialog(ExternalChanges service)
        => _service = service ?? throw new ArgumentNullException(nameof(service));

    /// <summary>Gets the documents currently listed.</summary>
    public IReadOnlyList<EditorDocument> Documents => _documents;

    /// <summary>Puts the given documents in the list.</summary>
    /// <param name="documents">The documents.</param>
    /// <remarks>Upstream's <c>setDocuments()</c>, gathering by folder and
    /// selecting the single entry when there is only one.</remarks>
    public void SetDocuments(IReadOnlyList<EditorDocument> documents)
    {
        _documents.Clear();
        _documents.AddRange(documents ?? Array.Empty<EditorDocument>());
        Populate();
    }

    /// <summary>Removes a document from the list.</summary>
    /// <param name="document">The document.</param>
    /// <remarks>Upstream's <c>removeDocument()</c>, which the app's saved,
    /// closed, loaded and renamed signals all reach — and which HIDES the
    /// window once nothing is left.</remarks>
    public void RemoveDocument(EditorDocument document)
    {
        if (document == null || !_documents.Remove(document)) { return; }

        Populate();
        if (_documents.Count == 0) { _dialog?.Hide(); }
    }

    /// <summary>Puts the window in front of the user.</summary>
    /// <param name="xamlRoot">The root to attach it to.</param>
    /// <returns>The running task.</returns>
    public async Task ShowAsync(XamlRoot xamlRoot)
    {
        Build();
        ShowList();
        Populate();

        _dialog = new ContentDialog
        {
            Title = I18n.Get("Modified Files"),
            Content = _content,
            CloseButtonText = I18n.Get("Close"),
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = xamlRoot,
        };

        //Trap 43: the width comes from the RESOURCE, and something inside has
        //to ask for the room or the box comes up at its minimum.
        _dialog.Resources["ContentDialogMaxWidth"] = 1100.0;
        _dialog.Resources["ContentDialogMaxHeight"] = 700.0;

        //Upstream's `userguide.addButton(self.buttonBox(), "externalchanges")'.
        //A third button in the row would be the limit (board trap 50), so Help
        //goes inside the content, in the button row the list page carries.

        try
        {
            await _dialog.ShowAsync();
        }
        finally
        {
            _dialog = null;
        }
    }

    /// <summary>
    /// Reloads documents from disk, answering the ones that could not be read.
    /// </summary>
    /// <param name="documents">The documents.</param>
    /// <returns>The failures, each with its reason.</returns>
    /// <remarks>Upstream's <c>reloadDocuments()</c>: <c>keepUndo=True</c>, so
    /// the previous contents are still one Undo away.</remarks>
    public static IReadOnlyList<(EditorDocument Document, string Reason)> ReloadDocuments(
        IEnumerable<EditorDocument> documents)
    {
        List<(EditorDocument, string)> failures = new List<(EditorDocument, string)>();
        foreach (var document in documents ?? Array.Empty<EditorDocument>())
        {
            try
            {
                document.Load(keepUndo: true);
            }
            catch (IOException error)
            {
                failures.Add((document, error.Message));
            }
            catch (UnauthorizedAccessException error)
            {
                failures.Add((document, error.Message));
            }
        }

        return failures;
    }

    /// <summary>
    /// Writes documents to disk, answering the ones that could not be written.
    /// </summary>
    /// <param name="documents">The documents.</param>
    /// <returns>The failures, each with its reason.</returns>
    /// <remarks>Upstream's <c>saveDocuments()</c>.</remarks>
    public static IReadOnlyList<(EditorDocument Document, string Reason)> SaveDocuments(
        IEnumerable<EditorDocument> documents)
    {
        List<(EditorDocument, string)> failures = new List<(EditorDocument, string)>();
        foreach (var document in documents ?? Array.Empty<EditorDocument>())
        {
            try
            {
                document.Save();
            }
            catch (IOException error)
            {
                failures.Add((document, error.Message));
            }
            catch (UnauthorizedAccessException error)
            {
                failures.Add((document, error.Message));
            }
        }

        return failures;
    }

    private void Build()
    {
        if (_content != null) { return; }

        _tree = new TreeView
        {
            SelectionMode = TreeViewSelectionMode.Multiple,
            CanDragItems = false,
            CanReorderItems = false,
            Height = 320,
            MinWidth = 480,
        };
        _tree.SelectionChanged += (_, _) => UpdateButtons();
        _tree.ItemInvoked += (_, _) => UpdateButtons();

        _reload = Action(I18n.Get("Reload"), I18n.Get(
            "Reloads the selected documents from disk. "
            + "(You can still reach the previous state of the document "
            + "using the Undo command.)"));
        _reloadAll = Action(I18n.Get("Reload All"), I18n.Get(
            "Reloads all externally modified documents from disk. "
            + "(You can still reach the previous state of the document "
            + "using the Undo command.)"));
        _save = Action(I18n.Get("Save"), I18n.Get(
            "Saves the selected documents to disk, overwriting the "
            + "modifications by another program."));
        _saveAll = Action(I18n.Get("Save All"), I18n.Get(
            "Saves all documents to disk, overwriting the modifications by "
            + "another program."));
        _showDiff = Action(I18n.Get("Show Difference..."), I18n.Get(
            "Shows the differences between the current document "
            + "and the file on disk."));

        _reload.Click += (_, _) => Reload(SelectedDocuments());
        _reloadAll.Click += (_, _) => Reload(_documents.ToList());
        _save.Click += (_, _) => Save(SelectedDocuments());
        _saveAll.Click += (_, _) => Save(_documents.ToList());
        _showDiff.Click += (_, _) => ShowDifference();

        _watchingEnabled = new CheckBox
        {
            Content = I18n.Get("Enable watching documents for external changes"),
            IsChecked = _service.Enabled,
        };
        //was previously: "…Frescobaldi will warn you…". FR9: the application is
        //not Frescobaldi, and its own name goes in the sentence.
        ToolTipService.SetToolTip(_watchingEnabled, I18n.Format(
            I18n.Get("If checked, {appname} will warn you when opened files are "
                + "modified or deleted by other applications."),
            ("appname", AppInfo.AppName)));
        _watchingEnabled.Checked += (_, _) => _service.SetEnabled(true);
        _watchingEnabled.Unchecked += (_, _) => _service.SetEnabled(false);

        _error = new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ErrorColor),
            FontWeight = FontWeights.SemiBold,
            Visibility = Visibility.Collapsed,
        };

        StackPanel buttons = new StackPanel { Spacing = 6, MinWidth = 180 };
        buttons.Children.Add(_reload);
        buttons.Children.Add(_reloadAll);
        buttons.Children.Add(_save);
        buttons.Children.Add(_saveAll);
        buttons.Children.Add(_showDiff);

        //Upstream's `userguide.addButton(self.buttonBox(), "externalchanges")'
        //(externalchanges/widget.py). The button box carries Close and nothing
        //else here (board trap 50), so Help joins the column of actions.
        buttons.Children.Add(UserGuide.GuideHelp.Button("externalchanges"));

        Grid row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition
            { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(buttons, 1);
        row.Children.Add(_tree);
        row.Children.Add(buttons);

        _listPage = new StackPanel { Spacing = 10, MinWidth = 700 };
        _listPage.Children.Add(new TextBlock
        {
            Text = I18n.Get(
                "The following files were modified or deleted by other "
                + "applications:"),
            TextWrapping = TextWrapping.Wrap,
        });
        _listPage.Children.Add(row);
        _listPage.Children.Add(_error);
        _listPage.Children.Add(_watchingEnabled);

        _diffMessage = new TextBlock { TextWrapping = TextWrapping.Wrap };
        _diffLines = new StackPanel { Spacing = 0 };
        Button back = new Button { Content = I18n.Get("Close") };
        back.Click += (_, _) => ShowList();

        _diffPage = new StackPanel
        {
            Spacing = 10,
            MinWidth = 700,
            Visibility = Visibility.Collapsed,
        };
        _diffPage.Children.Add(_diffMessage);
        _diffPage.Children.Add(new ScrollViewer
        {
            Content = _diffLines,
            Height = 320,
            HorizontalScrollMode = ScrollMode.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        });
        _diffPage.Children.Add(back);

        _content = new StackPanel { MinWidth = 700 };
        _content.Children.Add(_listPage);
        _content.Children.Add(_diffPage);
    }

    private static Button Action(string caption, string toolTip)
    {
        Button button = new Button
        {
            Content = MenuBuilder.Display(caption),
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        ToolTipService.SetToolTip(button, toolTip);
        return button;
    }

    private void ShowList()
    {
        if (_listPage == null) { return; }

        _listPage.Visibility = Visibility.Visible;
        _diffPage.Visibility = Visibility.Collapsed;
    }

    private void Populate()
    {
        if (_tree == null) { return; }

        _tree.RootNodes.Clear();
        _nodes.Clear();

        //Upstream groups by directory and sorts both levels naturally.
        Dictionary<string, List<EditorDocument>> byFolder
            = new Dictionary<string, List<EditorDocument>>(StringComparer.Ordinal);
        foreach (var document in _documents)
        {
            string path = document.Path;
            if (string.IsNullOrEmpty(path)) { continue; }

            string folder = Path.GetDirectoryName(path) ?? string.Empty;
            if (!byFolder.TryGetValue(folder, out var list))
            {
                list = new List<EditorDocument>();
                byFolder[folder] = list;
            }

            list.Add(document);
        }

        TreeViewNode onlyFile = null;
        foreach (var folder in byFolder.Keys.OrderBy(f => f, NaturalOrder.Instance))
        {
            TreeViewNode folderNode = new TreeViewNode
            {
                Content = PathUtil.Homify(folder),
                IsExpanded = true,
            };
            _tree.RootNodes.Add(folderNode);

            foreach (var document in byFolder[folder]
                .OrderBy(d => Path.GetFileName(d.Path), NaturalOrder.Instance))
            {
                TreeViewNode fileNode = new TreeViewNode
                {
                    Content = Path.GetFileName(document.Path) + "   "
                        + (DocumentWatcher.For(document).IsDeleted()
                            ? I18n.Get("[deleted]")
                            : I18n.Get("[modified]")),
                };
                folderNode.Children.Add(fileNode);
                _nodes[fileNode] = document;
                onlyFile = fileNode;
            }
        }

        //Upstream selects the entry when there is exactly one.
        if (_nodes.Count == 1 && onlyFile != null)
        {
            _tree.SelectedNodes.Clear();
            _tree.SelectedNodes.Add(onlyFile);
        }

        UpdateButtons();
    }

    private IReadOnlyList<EditorDocument> SelectedDocuments()
        => _tree == null
            ? Array.Empty<EditorDocument>()
            : _tree.SelectedNodes
                .Where(n => _nodes.ContainsKey(n))
                .Select(n => _nodes[n])
                .ToList();

    private void UpdateButtons()
    {
        if (_reload == null) { return; }

        IReadOnlyList<EditorDocument> selected = SelectedDocuments();
        IReadOnlyList<EditorDocument> all = _documents;

        //Upstream's `all(...)' over an EMPTY sequence is True, which is why an
        //empty selection disables Reload as well as Save.
        bool allDeletedSelected = selected.All(d => DocumentWatcher.For(d).IsDeleted());
        bool allDeleted = all.All(d => DocumentWatcher.For(d).IsDeleted());

        _save.IsEnabled = selected.Count > 0;
        _saveAll.IsEnabled = all.Count > 0;
        _reload.IsEnabled = !allDeletedSelected;
        _reloadAll.IsEnabled = !allDeleted;
        _showDiff.IsEnabled = selected.Count == 1 && !allDeletedSelected;
    }

    private void Reload(IReadOnlyList<EditorDocument> documents)
    {
        IReadOnlyList<(EditorDocument Document, string Reason)> failures
            = ReloadDocuments(documents);
        Report(I18n.Get("Could not reload:"), failures);
        foreach (var document in documents)
        {
            if (!failures.Any(f => f.Document == document)) { RemoveDocument(document); }
        }
    }

    private void Save(IReadOnlyList<EditorDocument> documents)
    {
        IReadOnlyList<(EditorDocument Document, string Reason)> failures
            = SaveDocuments(documents);
        Report(
            I18n.Get("Could not save:"),
            failures,
            //Upstream's own sentence, and the one message in the whole
            //application that has a PLURAL form: a save that failed leaves the
            //document unwritten, and "Save As..." is the way out.
            I18n.Get(
                "Please save the document using the \"Save As...\" dialog.",
                "Please save the documents using the \"Save As...\" dialog.",
                failures.Count));
        foreach (var document in documents)
        {
            if (!failures.Any(f => f.Document == document)) { RemoveDocument(document); }
        }
    }

    private void Report(
        string heading,
        IReadOnlyList<(EditorDocument Document, string Reason)> failures,
        string advice = null)
    {
        if (failures.Count == 0)
        {
            _error.Visibility = Visibility.Collapsed;
            _error.Text = string.Empty;
            return;
        }

        _error.Text = heading + " " + string.Join(
            "; ", failures.Select(f => f.Document.Path + ": " + f.Reason))
            + (string.IsNullOrEmpty(advice) ? string.Empty : " " + advice);
        _error.Visibility = Visibility.Visible;
    }

    private void ShowDifference()
    {
        IReadOnlyList<EditorDocument> selected = SelectedDocuments();
        EditorDocument document = selected.Count > 0 ? selected[0] : null;
        if (document == null || DocumentWatcher.For(document).IsDeleted()) { return; }

        string diskText;
        try
        {
            diskText = EditorDocument.LoadData(document.Path);
        }
        catch (IOException)
        {
            return;
        }
        catch (UnauthorizedAccessException)
        {
            return;
        }

        _diffLines.Children.Clear();
        foreach (DiffRow row in TextDiff.Unified(
            document.Text,
            diskText,
            I18n.Get("Current Document"),
            I18n.Get("Document on Disk"),
            context: 5))
        {
            _diffLines.Children.Add(UnifiedLine(row));
        }

        _diffMessage.Text = I18n.Format(
            I18n.Get("Document: {url}\n"
                + "Difference between the current document and the file on disk:"),
            ("url", document.Path));

        _listPage.Visibility = Visibility.Collapsed;
        _diffPage.Visibility = Visibility.Visible;
    }

    private static UIElement UnifiedLine(DiffRow row)
    {
        string text;
        Color? colour;
        if (row.Kind == DiffKind.Added)
        {
            text = (row.Right.StartsWith("+++", StringComparison.Ordinal)
                ? string.Empty : "+") + row.Right;
            colour = AddedColor;
        }
        else if (row.Kind == DiffKind.Removed)
        {
            text = (row.Left.StartsWith("---", StringComparison.Ordinal)
                ? string.Empty : "-") + row.Left;
            colour = RemovedColor;
        }
        else
        {
            text = " " + row.Left;
            colour = null;
        }

        TextBlock block = new TextBlock
        {
            Text = text,
            FontFamily = MonospaceFont(),
            TextWrapping = TextWrapping.NoWrap,
            IsTextSelectionEnabled = true,
        };

        if (colour != null)
        {
            block.Foreground = new SolidColorBrush(colour.Value);
            block.FontWeight = FontWeights.SemiBold;
        }

        return block;
    }

    private static FontFamily MonospaceFont()
        => Application.Current?.Resources
            .TryGetValue("RobotoMonoFont", out object font) == true
            ? font as FontFamily
            : null;

    /// <summary>Orders names the way a person reads them.</summary>
    /// <remarks>Upstream sorts both levels of the list with
    /// <c>util.naturalsort</c>.</remarks>
    private sealed class NaturalOrder : IComparer<string>
    {
        internal static readonly NaturalOrder Instance = new NaturalOrder();

        public int Compare(string x, string y) => PathUtil.CompareNaturally(x, y);
    }
}
