// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit;
using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using CodeBrix.Platform.UI.AdvancedTextEdit.Highlighting;
using Fresco.Brix.Commands;
using Fresco.Brix.Documents;
using Fresco.Brix.Editor;
using Fresco.Brix.Engrave;
using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Windows.UI;
using FontWeights = Microsoft.UI.Text.FontWeights;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/log.py and frescobaldi/logtool/

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The LilyPond log: everything a run said, as it says it, with the file
/// locations in its error messages clickable.
/// </summary>
/// <remarks>
/// <para>
/// The panel follows the CURRENT DOCUMENT, not the current job: switching tabs
/// shows that document's last run. A log opened halfway through a run replays
/// what the run has already said, because the job keeps its own history.
/// </para>
/// <para>
/// Upstream's widget is a <c>QTextBrowser</c> with anchors. Here the log IS a
/// text editor, read-only — which gives selection, copying and correct
/// scrolling for free (a standalone scroll bar paints nothing on the Skia
/// heads, board trap 2, and the editor's own code-built one is the fix already
/// in the app) — and the clickable locations are recorded spans that a click
/// is mapped onto.
/// </para>
/// </remarks>
public sealed class LogPanel : Panel
{
    /// <summary>The setting deciding whether the log shows whole paths.</summary>
    public const string RawViewSettingKey = "log/rawview";

    /// <summary>The setting deciding whether the log appears when a run starts.</summary>
    public const string ShowOnStartSettingKey = "log/show_on_start";

    /// <summary>The setting hiding automatic engraves from the log.</summary>
    public const string HideAutoEngraveSettingKey = "log/hide_auto_engrave";

    private readonly DocumentManager _documents;
    private readonly SettingsStore _settings;
    private readonly List<(int Start, int End, string Url)> _errors
        = new List<(int, int, string)>();

    private AdvancedTextEdit _view;
    private LogHighlighter _highlighter;
    private ViewHighlighter _viewHighlighter;
    private EditorDocument _document;
    private EngraveJob _job;
    private MessageType _lastType = MessageType.None;
    private int _currentError = -1;
    private bool _showingLinkTip;

    /// <summary>Creates the log panel.</summary>
    /// <param name="documents">The open documents.</param>
    /// <param name="actions">The log's own commands.</param>
    /// <param name="settings">The settings store, or null.</param>
    public LogPanel(
        DocumentManager documents, LogActions actions, SettingsStore settings = null)
        : base("logtool", DockArea.Bottom)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _settings = settings;
        Actions = actions;

        ToggleAction.WithShortcut("Meta+Alt+L");

        if (actions != null)
        {
            actions.LogNextError.Handler = () => { Activate(); GotoError(1); };
            actions.LogPreviousError.Handler = () => { Activate(); GotoError(-1); };
        }

        _documents.CurrentDocumentChanged += (_, e) => SwitchDocument(e.Document);
        _documents.DocumentClosed += (_, e) =>
        {
            if (e.Document == _document) { Clear(); }
        };

        JobManager.AnyJobStarted += (_, e) => OnJobStarted(e);
        JobManager.AnyJobFinished += (_, e) => OnJobFinished(e);
    }

    /// <summary>Gets the log's own commands.</summary>
    public LogActions Actions { get; }

    /// <summary>Gets or sets what to do with a clicked error location.</summary>
    public Action<ErrorReference> ShowReference { get; set; }

    /// <inheritdoc/>
    //was previously: "LilyPond Log" / "LilyPond &Log" — the panel shows LilyPort's log.
    public override string Title => I18n.Get("LilyPort Log");

    /// <inheritdoc/>
    public override void TranslateUI() => ToggleAction.Text = I18n.Get("LilyPort &Log");

    /// <summary>Gets whether whole paths are shown rather than file names.</summary>
    public bool RawView => _settings?.GetBool(RawViewSettingKey, true) ?? true;

    /// <summary>Shows a document's log.</summary>
    /// <param name="document">The document.</param>
    public void SwitchDocument(EditorDocument document)
    {
        if (document == null) { return; }

        EngraveJob job = JobManager.JobFor(document);
        if (job == null) { return; }

        if (JobAttributes.For(job).Hidden
            && (_settings?.GetBool(HideAutoEngraveSettingKey) ?? false))
        {
            return;
        }

        DisconnectJob();
        _document = document;
        Clear();
        ConnectJob(job);
    }

    /// <summary>Empties the log.</summary>
    public void Clear()
    {
        _errors.Clear();
        _currentError = -1;
        _lastType = MessageType.None;
        _highlighter?.Clear();
        _viewHighlighter?.Clear("current-error");
        if (_view != null) { _view.Document.Text = string.Empty; }
    }

    /// <summary>Follows a job's output, replaying what it has already said.</summary>
    /// <param name="job">The job.</param>
    public void ConnectJob(EngraveJob job)
    {
        if (job == null) { return; }

        _job = job;
        foreach (var message in job.History())
        {
            Write(message.Text, message.Type);
        }

        job.Output += OnJobOutput;
    }

    /// <summary>Stops following the current job.</summary>
    public void DisconnectJob()
    {
        if (_job != null) { _job.Output -= OnJobOutput; }

        _job = null;
    }

    /// <summary>Jumps to the next or previous error message.</summary>
    /// <param name="direction">1 for the next, -1 for the previous.</param>
    public void GotoError(int direction)
    {
        if (_errors.Count == 0) { return; }

        int index = _currentError + direction;
        if (index < 0) { index = _errors.Count - 1; }
        else if (index >= _errors.Count) { index = 0; }

        HighlightError(index);
    }

    /// <summary>Marks an error message and goes to where it points.</summary>
    /// <param name="index">Which error.</param>
    public void HighlightError(int index)
    {
        if (index < 0 || index >= _errors.Count || _view == null) { return; }

        _currentError = index;
        (int start, int end, string url) = _errors[index];

        _viewHighlighter?.Highlight(
            "current-error",
            new[] { (start, end - start) },
            Color.FromArgb(0x50, 0x40, 0x80, 0xC0),
            priority: 10,
            fullWidth: true);

        //Scroll the log so the message is on screen, then follow the link.
        DocumentLine line = _view.Document.GetLineByOffset(start);
        _view.ScrollTo(line.LineNumber, 1);
        _view.CaretOffset = start;

        ErrorReference reference = _document == null
            ? null
            : EngraveErrors.For(_document).Reference(url);
        if (reference != null) { ShowReference?.Invoke(reference); }
    }

    /// <inheritdoc/>
    protected override UIElement CreateWidget()
    {
        _view = new AdvancedTextEdit
        {
            IsReadOnly = true,
            ShowLineNumbers = false,
            WordWrap = true,
            FontSize = 12,
        };

        FontFamily monospace = MonospaceFont();
        if (monospace != null) { _view.FontFamily = monospace; }

        _highlighter = new LogHighlighter(_view.Document);
        _view.TextArea.TextView.LineTransformers.Add(
            new HighlightingColorizer(_highlighter));
        _viewHighlighter = new ViewHighlighter(_view.TextArea.TextView);

        //⚠ TWO SHARP EDGES IN ONE LINE. First, on PointerPressed the caret has
        //NOT moved yet — the editor sets it in its own handler, which runs
        //after this one, so the click would be tested against wherever the
        //caret happened to be already. Second, the editor marks the pointer
        //event handled, so an ordinary "+=" subscription never sees it at all;
        //the handler has to be added with handledEventsToo.
        _view.TextArea.AddHandler(
            UIElement.PointerReleasedEvent,
            new PointerEventHandler(OnPointerReleased),
            handledEventsToo: true);

        //Upstream tooltips each clickable `file:line:col' run with
        //_("Click to edit this file") (logtool/logwidget.py). A run inside an
        //editor document has no element of its own to hang a tooltip on, so the
        //hint follows the POINTER: it appears while the pointer is over a link
        //and is taken away again when it leaves one.
        //was previously: the click worked and nothing said it could be clicked.
        _view.TextArea.AddHandler(
            UIElement.PointerMovedEvent,
            new PointerEventHandler(OnPointerMoved),
            handledEventsToo: true);

        //A document may already have a finished run when the panel is first
        //opened; upstream connects on creation for the same reason.
        SwitchDocument(_documents.CurrentDocument);
        return _view;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_view == null) { return; }

        bool overLink = false;
        if (_errors.Count > 0)
        {
            Windows.Foundation.Point point
                = e.GetCurrentPoint(_view.TextArea.TextView).Position;
            TextViewPosition? position = _view.TextArea.TextView.GetPosition(point);
            if (position != null)
            {
                int offset = _view.Document.GetOffset(position.Value.Location);
                foreach (var (start, end, _) in _errors)
                {
                    if (offset >= start && offset <= end) { overLink = true; break; }
                }
            }
        }

        if (overLink == _showingLinkTip) { return; }

        _showingLinkTip = overLink;
        ToolTipService.SetToolTip(
            _view, overLink ? I18n.Get("Click to edit this file") : null);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_view == null || _errors.Count == 0) { return; }

        int offset = _view.CaretOffset;
        for (int index = 0; index < _errors.Count; index++)
        {
            (int start, int end, _) = _errors[index];
            if (offset >= start && offset <= end)
            {
                HighlightError(index);
                return;
            }
        }
    }

    private void OnJobOutput(object sender, JobMessage message)
        => Write(message.Text, message.Type);

    private void OnJobStarted(JobEventArgs e)
    {
        bool hidden = JobAttributes.For(e.Job).Hidden;
        if (e.Document != _documents.CurrentDocument) { return; }

        DisconnectJob();
        _document = e.Document;
        Clear();
        ConnectJob(e.Job);

        if (!hidden && (_settings?.GetBool(ShowOnStartSettingKey, true) ?? true))
        {
            Activate();
        }
    }

    private void OnJobFinished(JobEventArgs e)
    {
        //A failed run the user asked for raises the log even if they had it
        //closed: the message is the point of the failure.
        if (!e.Success
            && !e.Job.IsAborted
            && !JobAttributes.For(e.Job).Hidden
            && e.Document == _documents.CurrentDocument)
        {
            Activate();
        }
    }

    private void Write(string message, MessageType type)
    {
        if (_view == null || string.IsNullOrEmpty(message)) { return; }

        //Two kinds of message must not run into each other on one line: a
        //status line has no newline of its own, and output may have several.
        bool changed = type != _lastType;
        _lastType = type;
        if (changed
            && _view.Document.TextLength > 0
            && !EndsWithNewline()
            && !message.StartsWith("\n", StringComparison.Ordinal))
        {
            Append("\n", MessageType.None);
        }

        if (type == MessageType.StdErr)
        {
            WriteWithLinks(message);
        }
        else
        {
            Append(message, type);
        }

        ScrollToEnd();
    }

    private void WriteWithLinks(string message)
    {
        int position = 0;
        foreach (Match match in EngraveErrors.MessagePattern.Matches(message))
        {
            if (match.Index > position)
            {
                Append(message.Substring(position, match.Index - position),
                    MessageType.StdErr);
            }

            string url = match.Groups[1].Value;
            string path = match.Groups[2].Value;
            string shown = RawView ? url : LinkLabel(url, path);

            int start = _view.Document.TextLength;
            Append(shown, MessageType.StdErr, asLink: true);
            _errors.Add((start, _view.Document.TextLength, url));

            position = match.Index + match.Length;
        }

        if (position < message.Length)
        {
            Append(message.Substring(position), MessageType.StdErr);
        }
    }

    private static string LinkLabel(string url, string path)
    {
        //The compact form keeps the line:column, which is the part the reader
        //is actually navigating by, and drops only the directories.
        string name = Path.GetFileName(path);
        return string.IsNullOrEmpty(name)
            ? url
            : name + url.Substring(path.Length);
    }

    private void Append(string text, MessageType type, bool asLink = false)
    {
        int start = _view.Document.TextLength;
        _view.Document.Insert(start, text);
        _highlighter.Add(start, text.Length, type, asLink);
    }

    private bool EndsWithNewline()
    {
        int length = _view.Document.TextLength;
        return length == 0 || _view.Document.GetCharAt(length - 1) == '\n';
    }

    private void ScrollToEnd()
    {
        //Only follow the run when the reader is already at the end; a reader
        //who has scrolled up to look at an earlier error stays there.
        if (_view.Document.LineCount > 0)
        {
            _view.ScrollToEnd();
        }
    }

    private static FontFamily MonospaceFont()
        => Microsoft.UI.Xaml.Application.Current?.Resources
            .TryGetValue("RobotoMonoFont", out var font) == true
            ? font as FontFamily
            : null;
}

/// <summary>
/// Colours the log: the spans the panel records as it writes them.
/// </summary>
/// <remarks>
/// Nothing is parsed here — the panel already knows which channel each run of
/// characters came from, because it wrote it. This only turns that record into
/// the editor's colouring pipeline.
/// </remarks>
public sealed class LogHighlighter : IHighlighter
{
    private static readonly HighlightingColor StdOutColor = Make(
        Color.FromArgb(0xFF, 0x60, 0x30, 0x80));

    private static readonly HighlightingColor StdErrColor = Make(
        Color.FromArgb(0xFF, 0x20, 0x20, 0x20));

    private static readonly HighlightingColor NeutralColor = Make(
        Color.FromArgb(0xFF, 0x20, 0x20, 0x20), bold: true);

    private static readonly HighlightingColor SuccessColor = Make(
        Color.FromArgb(0xFF, 0x00, 0x70, 0x00), bold: true);

    private static readonly HighlightingColor FailureColor = Make(
        Color.FromArgb(0xFF, 0xA0, 0x00, 0x00), bold: true);

    private static readonly HighlightingColor LinkColor = Make(
        Color.FromArgb(0xFF, 0x00, 0x30, 0xB0), underline: true);

    private readonly TextDocument _document;
    private readonly List<(int Start, int Length, HighlightingColor Color)> _spans
        = new List<(int, int, HighlightingColor)>();

    /// <summary>Creates the highlighter over the log's text store.</summary>
    /// <param name="document">The text store.</param>
    public LogHighlighter(TextDocument document)
        => _document = document ?? throw new ArgumentNullException(nameof(document));

    /// <inheritdoc/>
    public event HighlightingStateChangedEventHandler HighlightingStateChanged;

    /// <inheritdoc/>
    public HighlightingColor DefaultTextColor => null;

    /// <inheritdoc/>
    public IDocument Document => _document;

    /// <summary>Records the colour of a run of characters.</summary>
    /// <param name="start">Where it starts.</param>
    /// <param name="length">How long it is.</param>
    /// <param name="type">Which channel it came from.</param>
    /// <param name="asLink">Whether it is a clickable location.</param>
    public void Add(int start, int length, MessageType type, bool asLink)
    {
        if (length <= 0) { return; }

        HighlightingColor color = asLink ? LinkColor : type switch
        {
            MessageType.StdOut => StdOutColor,
            MessageType.StdErr => StdErrColor,
            MessageType.Success => SuccessColor,
            MessageType.Failure => FailureColor,
            MessageType.Neutral => NeutralColor,
            _ => null,
        };

        if (color != null) { _spans.Add((start, length, color)); }
    }

    /// <summary>Forgets every recorded colour.</summary>
    public void Clear() => _spans.Clear();

    /// <inheritdoc/>
    public HighlightedLine HighlightLine(int lineNumber)
    {
        DocumentLine line = _document.GetLineByNumber(lineNumber);
        HighlightedLine result = new HighlightedLine(_document, line);
        int lineStart = line.Offset;
        int lineEnd = line.Offset + line.Length;

        foreach (var (start, length, color) in _spans)
        {
            int from = Math.Max(start, lineStart);
            int to = Math.Min(start + length, lineEnd);
            if (to > from)
            {
                result.Sections.Add(new HighlightedSection
                {
                    Offset = from,
                    Length = to - from,
                    Color = color,
                });
            }
        }

        return result;
    }

    /// <inheritdoc/>
    public IEnumerable<HighlightingColor> GetColorStack(int lineNumber)
        => Array.Empty<HighlightingColor>();

    /// <inheritdoc/>
    public void UpdateHighlightingState(int lineNumber)
    {
    }

    /// <inheritdoc/>
    public void BeginHighlighting()
    {
    }

    /// <inheritdoc/>
    public void EndHighlighting()
    {
    }

    /// <inheritdoc/>
    public HighlightingColor GetNamedColor(string name) => null;

    /// <summary>Announces that everything must be redrawn.</summary>
    public void InvalidateHighlighting()
        => HighlightingStateChanged?.Invoke(1, Math.Max(1, _document.LineCount));

    /// <inheritdoc/>
    public void Dispose()
    {
    }

    private static HighlightingColor Make(
        Color foreground, bool bold = false, bool underline = false)
    {
        HighlightingColor color = new HighlightingColor
        {
            Foreground = new SimpleHighlightingBrush(foreground),
        };
        if (bold) { color.FontWeight = FontWeights.Bold; }

        if (underline) { color.Underline = true; }

        return color;
    }
}
