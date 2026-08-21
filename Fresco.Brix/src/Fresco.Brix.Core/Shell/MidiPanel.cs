// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Documents;
using Fresco.Brix.Engrave;
using Fresco.Brix.Midi;
using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System;
using System.Globalization;
using Windows.UI;
using FontWeights = Microsoft.UI.Text.FontWeights;

namespace Fresco.Brix.Shell; //was previously: frescobaldi/miditool/widget.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The MIDI player: the document's engraved <c>.midi</c> output, a transport,
/// a position bar, and the little screen that shows where in the music the
/// player has got to.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's widget, control for control, with ruling FR6's two consequences.
/// There is no output-port machinery — no <c>openOutput</c>, no
/// <c>closeOutput</c> timer, no <c>midihub.aboutToRestart</c>, and no "No
/// output found!" empty state, because the synthesizer is in this process; and
/// there IS a volume control, which upstream has nowhere to put because the
/// volume belonged to whatever external synthesizer the user had running. (The
/// board's W9 row names volume as part of this panel; W12's MIDI preferences
/// page reads the same setting.)
/// </para>
/// <para>
/// The panel docks at the bottom rather than upstream's top edge — the shell
/// (board §5.4) has left, right and bottom areas, and a wide, short panel
/// belongs with the log.
/// </para>
/// </remarks>
public sealed class MidiPanel : Panel
{
    private readonly DocumentManager _documents;
    private readonly IMidiPlayer _player;
    private readonly SettingsStore _settings;

    private ComboBox _fileSelector;
    private Button _stopButton;
    private Button _playButton;
    private TrackBar _timeBar;
    private TrackBar _tempoBar;
    private TrackBar _volumeBar;
    private MidiDisplay _display;
    private EditorDocument _document;
    private bool _fillingSelector;

    /// <summary>Creates the MIDI panel.</summary>
    /// <param name="documents">The open documents.</param>
    /// <param name="actions">The transport commands.</param>
    /// <param name="player">The player the panel drives.</param>
    /// <param name="settings">The settings store, or null.</param>
    public MidiPanel(
        DocumentManager documents,
        MidiActions actions,
        IMidiPlayer player,
        SettingsStore settings = null)
        : base("miditool", DockArea.Bottom)
    {
        _documents = documents ?? throw new ArgumentNullException(nameof(documents));
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _settings = settings;
        Actions = actions;

        ToggleAction.WithShortcut("Meta+Alt+P");

        if (actions != null)
        {
            //Each command builds the panel first, because upstream's do: its
            //MidiTool slots go through self.widget(), and a panel's contents
            //are built on the first request for them.
            actions.MidiPlay.Handler = () => { Widget(); Play(); };

            //⚠ Upstream's Pause and Stop BOTH call the widget's stop(), which
            //keeps the position — its Restart is what rewinds. Ported as it is:
            //this is a design decision, not a slip.
            actions.MidiPause.Handler = () => _player.Pause();
            actions.MidiStop.Handler = () => _player.Pause();
            actions.MidiRestart.Handler = () => { Widget(); Restart(); };
        }

        _player.StateChanged += (_, _) => OnPlayerStateChanged();
        _player.PositionChanged += (_, _) => OnPositionChanged();

        if (_player is MidiPlayerService service)
        {
            service.LoadFailed += (_, message) => _display?.ShowStatus(message);
        }

        //⚠ NOTHING BELOW TOUCHES THE AUDIO DEVICE UNTIL THE PANEL IS OPENED.
        //Loading a file opens the shared output, so every one of these follows
        //the panel's own laziness: upstream makes these connections INSIDE its
        //widget's constructor, which is to say on first show.
        _documents.CurrentDocumentChanged += (_, e) =>
        {
            if (IsInstantiated) { LoadResults(e.Document); }
        };
        _documents.DocumentClosed += (_, e) =>
        {
            if (IsInstantiated) { OnDocumentClosed(e.Document); }
        };

        JobManager.AnyJobFinished += (_, e) =>
        {
            if (IsInstantiated) { OnJobFinished(e); }
        };
    }

    /// <summary>Gets the transport commands.</summary>
    public MidiActions Actions { get; }

    /// <inheritdoc/>
    public override string Title => I18n.Get("MIDI");

    /// <inheritdoc/>
    public override void TranslateUI() => ToggleAction.Text = I18n.Get("MIDI &Player");

    /// <summary>Starts playing, loading the current file if nothing is loaded.</summary>
    public void Play()
    {
        //Upstream's own guard, and its has_events() is FALSE at the end of a
        //sequence — which is what makes this rewind a finished song rather
        //than resume it where it stopped.
        if (_player.State != MidiPlayerState.Playing && !_player.HasEvents)
        {
            Restart();
        }

        _player.Play();
    }

    /// <summary>
    /// Rewinds — and reloads, when the selected file is not the loaded one or a
    /// run has replaced it.
    /// </summary>
    public void Restart()
    {
        _player.Seek(0);
        UpdateTimeBar();
        _display?.Reset();

        if (_document == null) { return; }

        MidiFiles files = MidiFiles.For(_document);
        int index = _fileSelector?.SelectedIndex ?? files.Current;
        if (files.Any && files.DisplayName(index) != FileNameOf(_player.FileName))
        {
            LoadSong(index);
        }
    }

    /// <summary>Shows a document's MIDI output.</summary>
    /// <param name="document">The document.</param>
    public void LoadResults(EditorDocument document)
    {
        if (document == null) { return; }

        MidiFiles files = MidiFiles.For(document);
        if (!files.Update()) { return; }

        _document = document;
        FillSelector(files);
        if (_player.State != MidiPlayerState.Playing) { LoadSong(files.Current); }
    }

    /// <summary>Loads one of the document's MIDI files into the player.</summary>
    /// <param name="index">Which file.</param>
    public void LoadSong(int index)
    {
        if (_document == null) { return; }

        MidiFiles files = MidiFiles.For(_document);
        string file = index >= 0 && index < files.Files.Count ? files.Files[index] : null;
        if (file == null)
        {
            _player.Clear();
            UpdateTimeBar();
            _display?.Reset();
            return;
        }

        if (!_player.Load(file, files.Song(index))) { return; }

        UpdateTimeBar();
        _display?.Reset();

        long seconds = _player.TotalTime / 1000;
        _display?.ShowStatus(
            I18n.Get("midi lcd screen", "LOADED"),
            files.DisplayName(index),
            I18n.Get("midi lcd screen", "TOTAL"),
            string.Format(
                CultureInfo.InvariantCulture,
                "{0}:{1:00}",
                seconds / 60,
                seconds % 60));
    }

    /// <inheritdoc/>
    protected override UIElement CreateWidget()
    {
        _fileSelector = new ComboBox
        {
            MinWidth = 160,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        _fileSelector.SelectionChanged += (_, _) => OnFileSelected();

        _stopButton = TransportButton();
        _playButton = TransportButton();

        _timeBar = new TrackBar
        {
            Orientation = Orientation.Horizontal,
            Minimum = 0,
            Maximum = 1,

            //Upstream's tracking=False: the display follows the drag, the
            //player only seeks when it ends.
            IsTracking = false,
            VerticalAlignment = VerticalAlignment.Center,
            Height = 18,
        };
        _timeBar.Moved += (_, _) => ShowTimeOnDisplay((long)_timeBar.Value);
        _timeBar.ValueChanged += (_, _) =>
        {
            _player.Seek((long)_timeBar.Value);
            ShowTimeOnDisplay((long)_timeBar.Value);
        };

        //-50..50, converted with 2**(v/50) — a half to double speed, which is
        //upstream's own range and its own curve.
        _tempoBar = new TrackBar
        {
            Orientation = Orientation.Vertical,
            Minimum = -50,
            Maximum = 50,
            Value = 0,
            Width = 18,
        };
        ToolTipService.SetToolTip(_tempoBar, I18n.Get("Tempo"));
        _tempoBar.ValueChanged += (_, _) => OnTempoChanged();

        _volumeBar = new TrackBar
        {
            Orientation = Orientation.Vertical,
            Minimum = 0,
            Maximum = 100,
            Value = Math.Clamp(_player.Volume * 100, 0, 100),
            Width = 18,
        };
        ToolTipService.SetToolTip(_volumeBar, I18n.Get("Volume"));
        _volumeBar.ValueChanged += (_, _)
            => _player.Volume = (float)(_volumeBar.Value / 100.0);

        _display = new MidiDisplay();

        //Upstream's grid: the file chooser across the top, the transport and
        //the position bar under it, the screen under that, and the tempo down
        //the right-hand side across all three rows.
        Grid grid = new Grid { Padding = new Thickness(4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(
            new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        Add(grid, _fileSelector, 0, 0, columnSpan: 3);
        Add(grid, _stopButton, 1, 0);
        Add(grid, _playButton, 1, 1);
        Add(grid, _timeBar, 1, 2);
        Add(grid, _display, 2, 0, columnSpan: 3);
        Add(grid, LabelledBar(_tempoBar, I18n.Get("Tempo")), 0, 3, rowSpan: 3);
        Add(grid, LabelledBar(_volumeBar, I18n.Get("Volume")), 0, 4, rowSpan: 3);

        OnPlayerStateChanged();
        if (_documents.CurrentDocument != null) { LoadResults(_documents.CurrentDocument); }

        //The panel measures to nothing without this: its children scroll and a
        //dock tab hands out the DESIRED height (board trap 30).
        return new FillGrid { Children = { grid } };
    }

    private static void Add(
        Grid grid, UIElement element, int row, int column,
        int rowSpan = 1, int columnSpan = 1)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        if (rowSpan > 1) { Grid.SetRowSpan(element, rowSpan); }

        if (columnSpan > 1) { Grid.SetColumnSpan(element, columnSpan); }

        grid.Children.Add(element);
    }

    private static UIElement LabelledBar(TrackBar bar, string caption)
    {
        StackPanel column = new StackPanel
        {
            Orientation = Orientation.Vertical,
            Margin = new Thickness(6, 0, 0, 0),
        };
        column.Children.Add(new TextBlock
        {
            Text = caption,
            FontSize = 9,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        bar.VerticalAlignment = VerticalAlignment.Stretch;
        bar.MinHeight = 60;
        column.Children.Add(bar);
        return column;
    }

    private static Button TransportButton()
        => new Button
        {
            Padding = new Thickness(10, 2, 10, 2),
            MinWidth = 0,
            Margin = new Thickness(0, 2, 4, 2),
        };

    private static string FileNameOf(string path)
        => string.IsNullOrEmpty(path)
            ? string.Empty
            : System.IO.Path.GetFileName(path);

    private void FillSelector(MidiFiles files)
    {
        if (_fileSelector == null) { return; }

        _fillingSelector = true;
        try
        {
            _fileSelector.Items.Clear();
            for (int index = 0; index < files.Files.Count; index++)
            {
                _fileSelector.Items.Add(files.DisplayName(index));
            }

            _fileSelector.SelectedIndex = files.Files.Count == 0 ? -1 : files.Current;
            _fileSelector.IsEnabled = files.Files.Count > 1;
        }
        finally
        {
            _fillingSelector = false;
        }
    }

    private void OnFileSelected()
    {
        if (_fillingSelector || _document == null || _fileSelector == null) { return; }

        _player.Pause();
        MidiFiles files = MidiFiles.For(_document);
        if (!files.Any) { return; }

        files.Current = Math.Max(0, _fileSelector.SelectedIndex);
        Restart();
    }

    private void OnJobFinished(JobEventArgs e)
    {
        //Only the document the panel is showing, which is upstream's own guard
        //against another window's run rewriting this one's file list.
        if (e.Document == null || e.Document != _documents.CurrentDocument) { return; }

        LoadResults(e.Document);
    }

    private void OnDocumentClosed(EditorDocument document)
    {
        if (document != _document) { return; }

        _document = null;
        _fileSelector?.Items.Clear();
        _player.Pause();
        _player.Clear();
        UpdateTimeBar();
        _display?.Reset();
    }

    private void OnPlayerStateChanged()
    {
        if (_playButton == null || Actions == null) { return; }

        bool playing = _player.State == MidiPlayerState.Playing;

        //Upstream swaps which ACTION each of the two buttons carries, so the
        //same two buttons are Stop/Pause while playing and Restart/Play when
        //not. Kept: it is what a user of Frescobaldi reaches for.
        Bind(_stopButton, playing ? Actions.MidiStop : Actions.MidiRestart);
        Bind(_playButton, playing ? Actions.MidiPause : Actions.MidiPlay);

        if (!playing) { UpdateTimeBar(); }
    }

    private void Bind(Button button, AppAction action)
    {
        //Board trap 18 reaches button captions too (trap 50): the accelerator
        //marker is stripped at the point of DISPLAY, never out of the string.
        button.Content = MenuBuilder.Display(action.Text);
        ToolTipService.SetToolTip(button, MenuBuilder.Display(action.ToolTip ?? action.Text));
        button.Command = action;
    }

    private void OnPositionChanged()
    {
        UpdateTimeBar();
        ShowTimeOnDisplay(_player.CurrentTime);
    }

    private void UpdateTimeBar()
    {
        if (_timeBar == null || _timeBar.IsDragging) { return; }

        _timeBar.Maximum = Math.Max(1, _player.TotalTime);
        _timeBar.SetValueQuietly(_player.CurrentTime);
        _timeBar.IsEnabled = _player.HasSong;
    }

    private void ShowTimeOnDisplay(long milliseconds)
    {
        if (_display == null) { return; }

        _display.SetTime(milliseconds);
        MidiSong song = _player.Song;
        if (song != null) { _display.SetBeat(song.Beat(milliseconds)); }
    }

    private void OnTempoChanged()
    {
        //Upstream's own conversion: -50..50 becomes 0.5..2.0.
        double factor = Math.Pow(2, _tempoBar.Value / 50.0);
        _player.TempoFactor = factor;
        _display?.ShowTempo(
            ((int)(factor * 100)).ToString(CultureInfo.InvariantCulture) + "%");
    }
}

/// <summary>
/// The MIDI panel's little screen: two captioned readings side by side, showing
/// the time and the beat, the tempo just after it is changed, or a status
/// message just after something happens.
/// </summary>
/// <remarks>
/// Upstream builds this out of an HTML table in a <c>QLabel</c> with a
/// stylesheet that makes it look like an LCD. There is no rich-text label here,
/// so it is the same two-by-two arrangement built out of four
/// <see cref="TextBlock"/>s, which also makes the alignment switch between the
/// reading form (right) and the message forms (left) plain to read.
/// </remarks>
public sealed class MidiDisplay : Grid
{
    private readonly TextBlock _leftCaption = Caption();
    private readonly TextBlock _rightCaption = Caption();
    private readonly TextBlock _leftValue = Value();
    private readonly TextBlock _rightValue = Value();
    private readonly DispatcherTimer _tempoTimer = new DispatcherTimer
    {
        Interval = TimeSpan.FromMilliseconds(1500),
    };

    private readonly DispatcherTimer _statusTimer = new DispatcherTimer
    {
        Interval = TimeSpan.FromMilliseconds(2000),
    };

    private long _time;
    private SongBeat _beat;
    private string _tempo;
    private string[] _status;

    /// <summary>Creates the display.</summary>
    public MidiDisplay()
    {
        Padding = new Thickness(6, 3, 6, 3);
        Margin = new Thickness(0, 4, 0, 0);
        CornerRadius = new CornerRadius(3);
        Background = new SolidColorBrush(Color.FromArgb(0xFF, 0x10, 0x18, 0x10));

        ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(
            new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Place(_leftCaption, 0, 0);
        Place(_rightCaption, 0, 1);
        Place(_leftValue, 1, 0);
        Place(_rightValue, 1, 1);

        _tempoTimer.Tick += (_, _) => { _tempoTimer.Stop(); ShowTempo(null); };
        _statusTimer.Tick += (_, _) => { _statusTimer.Stop(); ShowStatus(); };

        Reset();
    }

    /// <summary>Sets everything back to zero.</summary>
    public void Reset()
    {
        _time = 0;
        _beat = default;
        Update();
    }

    /// <summary>Shows a position.</summary>
    /// <param name="milliseconds">The position.</param>
    public void SetTime(long milliseconds)
    {
        _time = milliseconds;
        Update();
    }

    /// <summary>Shows which beat the position is on.</summary>
    /// <param name="beat">The beat.</param>
    public void SetBeat(SongBeat beat)
    {
        _beat = beat;
        Update();
    }

    /// <summary>Shows the tempo for a moment, in place of the beat.</summary>
    /// <param name="text">The tempo text, or null to stop showing it.</param>
    public void ShowTempo(string text)
    {
        _tempo = text;
        _tempoTimer.Stop();
        if (!string.IsNullOrEmpty(text)) { _tempoTimer.Start(); }

        Update();
    }

    /// <summary>Shows a message for a moment, in place of everything else.</summary>
    /// <param name="message">One to four parts: caption, value, caption,
    /// value. No parts at all clears the message.</param>
    public void ShowStatus(params string[] message)
    {
        _status = message is { Length: > 0 } ? message : null;
        _statusTimer.Stop();
        if (_status != null) { _statusTimer.Start(); }

        Update();
    }

    private static TextBlock Caption()
        => new TextBlock
        {
            FontSize = 9,
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x60, 0xC0, 0x60)),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

    private static TextBlock Value()
        => new TextBlock
        {
            FontSize = 20,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x80, 0xF0, 0x80)),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };

    private void Place(TextBlock block, int row, int column)
    {
        Grid.SetRow(block, row);
        Grid.SetColumn(block, column);
        Children.Add(block);
    }

    private void Update()
    {
        string timeText = string.Format(
            CultureInfo.InvariantCulture,
            "{0}:{1:00}",
            _time / 1000 / 60,
            _time / 1000 % 60);

        if (_status != null)
        {
            //Upstream's four message shapes, in its own order: one part is a
            //value with no caption, two are a caption and a value, three put an
            //uncaptioned value on the right, four fill both pairs.
            switch (_status.Length)
            {
                case 1: Show(string.Empty, _status[0], null, null, left: true); break;
                case 2: Show(_status[0], _status[1], null, null, left: true); break;
                case 3:
                    Show(_status[0], _status[1], string.Empty, _status[2], left: true);
                    break;
                default:
                    Show(_status[0], _status[1], _status[2], _status[3], left: true);
                    break;
            }

            return;
        }

        if (!string.IsNullOrEmpty(_tempo))
        {
            Show(
                I18n.Get("midi lcd screen", "TIME"),
                timeText,
                I18n.Get("midi lcd screen", "TEMPO"),
                _tempo,
                left: false);
            return;
        }

        string beatText = string.Format(
            CultureInfo.InvariantCulture, "{0}.{1,2}", _beat.Measure, _beat.Beat);
        string signature = _beat.Numerator == 0
            ? string.Empty
            : string.Format(
                CultureInfo.InvariantCulture,
                " {0}/{1}",
                _beat.Numerator,
                1L << Math.Clamp(_beat.Denominator, 0, 30));

        Show(
            I18n.Get("midi lcd screen", "TIME"),
            timeText,
            I18n.Get("midi lcd screen", "BEAT") + signature,
            beatText,
            left: false);
    }

    private void Show(
        string leftCaption, string leftValue,
        string rightCaption, string rightValue, bool left)
    {
        HorizontalAlignment alignment
            = left ? HorizontalAlignment.Left : HorizontalAlignment.Right;

        _leftCaption.Text = leftCaption ?? string.Empty;
        _leftValue.Text = leftValue ?? string.Empty;
        _leftCaption.HorizontalAlignment = alignment;
        _leftValue.HorizontalAlignment = alignment;

        _rightCaption.Text = rightCaption ?? string.Empty;
        _rightValue.Text = rightValue ?? string.Empty;
        _rightCaption.HorizontalAlignment
            = left ? HorizontalAlignment.Right : HorizontalAlignment.Right;
        _rightValue.HorizontalAlignment
            = left ? HorizontalAlignment.Right : HorizontalAlignment.Right;
    }
}
