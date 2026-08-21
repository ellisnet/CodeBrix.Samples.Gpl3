// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using SkiaSharp;
using SkiaSharp.Views.Windows;
using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Foundation;

namespace Fresco.Brix.MusicView; //was previously: qpageview/view.py + scrollarea.py + link.py + highlight.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>What the view says when a link is clicked or hovered.</summary>
public sealed class MusicLinkEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="page">The page the link is on.</param>
    /// <param name="link">The link.</param>
    /// <param name="properties">The pointer state at the time, when there was one.</param>
    public MusicLinkEventArgs(ScorePage page, Link link, PointerPointProperties properties = null)
    {
        ScorePage = page;
        Link = link;
        Properties = properties;
    }

    /// <summary>Gets the page the link is on.</summary>
    public ScorePage ScorePage { get; }

    /// <summary>Gets the link.</summary>
    public Link Link { get; }

    /// <summary>Gets the pointer state at the time, or null.</summary>
    public PointerPointProperties Properties { get; }

    /// <summary>Gets or sets whether Shift was held.</summary>
    public bool IsShiftDown { get; set; }
}

/// <summary>What the view says when the user asks for a context menu.</summary>
public sealed class MusicContextMenuEventArgs : EventArgs
{
    /// <summary>Creates the arguments.</summary>
    /// <param name="target">The element the menu should attach to.</param>
    /// <param name="position">Where in that element the menu goes.</param>
    /// <param name="page">The page under the pointer, or null.</param>
    /// <param name="link">The link under the pointer, or null.</param>
    public MusicContextMenuEventArgs(FrameworkElement target, Point position, ScorePage page, Link link)
    {
        Target = target;
        Position = position;
        Page = page;
        Link = link;
    }

    /// <summary>Gets the element the menu should attach to.</summary>
    public FrameworkElement Target { get; }

    /// <summary>Gets where in that element the menu goes.</summary>
    public Point Position { get; }

    /// <summary>Gets the page under the pointer, or null.</summary>
    public ScorePage Page { get; }

    /// <summary>Gets the link under the pointer, or null.</summary>
    public Link Link { get; }
}

/// <summary>
/// The paged view of an engraved score: pages laid out, drawn, scrolled and
/// zoomed, with the point-and-click anchors live under the mouse.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's View is a QAbstractScrollArea that rasterises pages into a tile
/// cache on background threads. This one keeps the same MODEL — a
/// <see cref="PageLayout"/> of <see cref="ScorePage"/>s, links in 0-to-1 page
/// coordinates, highlighters keyed by style — and throws the cache away,
/// because a page here is an SVG that Skia replays as vector geometry in about
/// a millisecond. Everything the cache existed to hide is simply not slow.
/// </para>
/// <para>
/// The scroll area is built by hand rather than taken from a
/// <c>ScrollViewer</c>: the drawing surface must stay the size of the VIEWPORT
/// (a twenty-page score at 400% is 90,000 pixels tall, and a surface that size
/// is not allocatable), so the view keeps its own offset and draws the pages
/// translated by it — which is exactly what a QAbstractScrollArea does.
/// The two scroll bars carry code-built templates, because a standalone
/// ScrollBar paints nothing under the theme templates on the Skia heads
/// (board trap 2); this is the same fix the editor already ships.
/// </para>
/// </remarks>
public sealed class MusicViewControl : Grid
{
    /// <summary>The zoom is never taken above this — upstream's own ceiling.</summary>
    public const double MaxZoom = 8.0;

    /// <summary>The zoom is never taken below this.</summary>
    public const double MinZoom = 0.05;

    /// <summary>The zooms the zoom-in and zoom-out steps walk through.</summary>
    public static readonly IReadOnlyList<double> ZoomFactors = new[]
    {
        0.05, 0.1, 0.25, 0.33, 0.5, 0.66, 0.75, 1.0,
        1.25, 1.5, 2.0, 2.5, 3.0, 4.0, 5.0, 6.0, 8.0,
    };

    private const double ScrollBarThickness = 12.0;
    private const int WheelStep = 60;

    private readonly SKXamlCanvas _canvas = new SKXamlCanvas();
    private readonly ScrollBar _verticalScrollBar = new ScrollBar { Orientation = Orientation.Vertical };
    private readonly ScrollBar _horizontalScrollBar = new ScrollBar { Orientation = Orientation.Horizontal };
    private readonly Border _cornerSpacer = new Border();
    private readonly Dictionary<Highlighter, HighlightSet> _highlights
        = new Dictionary<Highlighter, HighlightSet>();
    //Keyed by the SCORE OBJECT, not by its file name: a run on an unsaved
    //document writes into a scratch directory, so the name changes while the
    //score plainly does not.
    private readonly Dictionary<MusicDocument, StoredProperties> _properties
        = new Dictionary<MusicDocument, StoredProperties>();

    private MusicDocument _document;
    private int _scrollX;
    private int _scrollY;
    private bool _updatingScrollBars;
    private bool _inLayoutUpdate;
    private Link _currentLink;
    private ScorePage _currentLinkPage;
    private int _currentPageNumber = 1;

    /// <summary>Creates an empty view.</summary>
    public MusicViewControl()
    {
        Layout = new PageLayout();
        Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x50, 0x50, 0x50));

        RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _canvas.HorizontalAlignment = HorizontalAlignment.Stretch;
        _canvas.VerticalAlignment = VerticalAlignment.Stretch;
        _canvas.PaintSurface += OnPaintSurface;
        SetRow(_canvas, 0);
        SetColumn(_canvas, 0);
        Children.Add(_canvas);

        _verticalScrollBar.Template = new ControlTemplate(() => BuildScrollBarTemplateRoot(Orientation.Vertical));
        _horizontalScrollBar.Template
            = new ControlTemplate(() => BuildScrollBarTemplateRoot(Orientation.Horizontal));
        _verticalScrollBar.Width = ScrollBarThickness;
        _horizontalScrollBar.Height = ScrollBarThickness;
        _verticalScrollBar.ValueChanged += (_, e) =>
        {
            if (_updatingScrollBars) { return; }

            _scrollY = (int)Math.Round(e.NewValue);
            Invalidate();
        };
        _horizontalScrollBar.ValueChanged += (_, e) =>
        {
            if (_updatingScrollBars) { return; }

            _scrollX = (int)Math.Round(e.NewValue);
            Invalidate();
        };
        SetRow(_verticalScrollBar, 0);
        SetColumn(_verticalScrollBar, 1);
        Children.Add(_verticalScrollBar);
        SetRow(_horizontalScrollBar, 1);
        SetColumn(_horizontalScrollBar, 0);
        Children.Add(_horizontalScrollBar);

        _cornerSpacer.Width = ScrollBarThickness;
        _cornerSpacer.Height = ScrollBarThickness;
        SetRow(_cornerSpacer, 1);
        SetColumn(_cornerSpacer, 1);
        Children.Add(_cornerSpacer);

        SizeChanged += (_, _) => UpdateViewport();
        //The CANVAS is what the pages are fitted to, and it learns its size
        //after the control does.
        _canvas.SizeChanged += (_, _) => UpdateViewport();
        PointerMoved += OnPointerMoved;
        PointerExited += OnPointerExited;
        PointerWheelChanged += OnPointerWheelChanged;
        //A click is only usable once the pointer has been RELEASED, and the
        //surface marks the press handled (board trap 26 in the editor's case);
        //taking the released event with handledEventsToo is the form that works
        //on every head.
        AddHandler(PointerReleasedEvent, new PointerEventHandler(OnPointerReleased), true);
        RightTapped += OnRightTapped;
    }

    /// <summary>Raised when a link is clicked.</summary>
    public event EventHandler<MusicLinkEventArgs> LinkClicked;

    /// <summary>Raised when the mouse comes to rest on a link.</summary>
    public event EventHandler<MusicLinkEventArgs> LinkHovered;

    /// <summary>Raised when the mouse leaves a link.</summary>
    public event EventHandler LinkLeft;

    /// <summary>Raised when the user asks for the context menu.</summary>
    public event EventHandler<MusicContextMenuEventArgs> ContextMenuRequested;

    /// <summary>Raised when the page under the middle of the view changes.</summary>
    public event EventHandler CurrentPageChanged;

    /// <summary>Raised when the zoom changes, however it was changed.</summary>
    public event EventHandler ZoomChanged;

    /// <summary>Raised when the view is scrolled or the layout re-flowed.</summary>
    public event EventHandler ViewChanged;

    /// <summary>Gets the layout holding the pages.</summary>
    public PageLayout Layout { get; }

    /// <summary>Gets or sets whether links are found and reported at all.</summary>
    public bool LinksEnabled { get; set; } = true;

    /// <summary>Gets or sets the highlighter used for the link under the mouse.</summary>
    public Highlighter LinkHighlighter { get; set; }

    /// <summary>Gets or sets whether a drop shadow is drawn behind each page.</summary>
    public bool DropShadowEnabled { get; set; } = true;

    /// <summary>Gets or sets the paper colour drawn behind every page.</summary>
    public SKColor PaperColor { get; set; } = SKColors.White;

    /// <summary>Gets the document being shown, or null.</summary>
    public MusicDocument Document => _document;

    /// <summary>Gets how many pages are shown.</summary>
    public int PageCount => Layout.Count;

    /// <summary>Gets or sets how the zoom follows the size of the view.</summary>
    public ViewMode ViewMode
    {
        get;
        set
        {
            if (field == value) { return; }

            field = value;
            UpdateViewport();
        }
    } = ViewMode.FitWidth;

    /// <summary>Gets or sets the zoom.</summary>
    public double ZoomFactor
    {
        get => Layout.ZoomFactor;
        set => SetZoomFactor(value, null);
    }

    /// <summary>Gets or sets whether all pages are shown, or one set at a time.</summary>
    public bool ContinuousMode
    {
        get => Layout.ContinuousMode;
        set
        {
            if (Layout.ContinuousMode == value) { return; }

            Layout.ContinuousMode = value;
            if (!value) { Layout.CurrentPageSet = Layout.PageSet(Math.Max(0, _currentPageNumber - 1)); }

            UpdateViewport();
        }
    }

    /// <summary>Gets or sets the rotation applied to every page.</summary>
    /// <remarks>
    /// Named for the PAGE rather than plainly <c>Rotation</c>, which every
    /// UIElement already has and means something else entirely.
    /// </remarks>
    public Rotation PageRotation
    {
        get => Layout.Rotation;
        set
        {
            if (Layout.Rotation == value) { return; }

            Layout.Rotation = value;
            UpdateViewport();
        }
    }

    /// <summary>Gets or sets the 1-based number of the page in view.</summary>
    public int CurrentPageNumber
    {
        get => _currentPageNumber;
        set => SetCurrentPageNumber(value);
    }

    /// <summary>Gets the scroll offset, in layout coordinates.</summary>
    public SKPointI ScrollOffset => new SKPointI(_scrollX, _scrollY);

    /// <summary>
    /// Gets what to subtract from a layout coordinate to get a view one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Three things go into it. The scroll offset; the centring that a layout
    /// smaller than the view gets, because a single page in a wide panel sits
    /// in the MIDDLE of it; and the layout's OWN origin, which is not the
    /// origin at all when the layout is showing one page set out of several —
    /// page three of four begins a long way down, and the view still has to
    /// draw it at the top.
    /// </para>
    /// </remarks>
    public SKPointI ViewOffset
    {
        get
        {
            SKSizeI viewport = ViewportSize;
            int x = Layout.X + _scrollX
                - (Layout.Width < viewport.Width ? (viewport.Width - Layout.Width) / 2 : 0);
            int y = Layout.Y + _scrollY
                - (Layout.Height < viewport.Height ? (viewport.Height - Layout.Height) / 2 : 0);
            return new SKPointI(x, y);
        }
    }

    /// <summary>Gets the size of the drawing area, in pixels.</summary>
    public SKSizeI ViewportSize
    {
        get
        {
            double width = _canvas.ActualWidth;
            double height = _canvas.ActualHeight;
            if (width < 1) { width = ActualWidth - ScrollBarThickness; }

            if (height < 1) { height = ActualHeight - ScrollBarThickness; }

            return new SKSizeI(Math.Max(1, (int)Math.Round(width)), Math.Max(1, (int)Math.Round(height)));
        }
    }

    /// <summary>
    /// Shows a document, putting it back where this view last had it.
    /// </summary>
    /// <param name="document">The document, or null to clear.</param>
    /// <remarks>
    /// The position, zoom and page layout are remembered against the score's
    /// name, so re-engraving while working on page five comes back to page
    /// five rather than to the title. That is upstream's document-property
    /// store, and the reason its music view does not lose your place every
    /// time you press Ctrl+M.
    /// </remarks>
    public void SetDocument(MusicDocument document)
    {
        RememberProperties();

        _document = document;
        ClearAllHighlights();
        _currentLink = null;
        _currentLinkPage = null;
        Layout.SetPages(document?.Pages);
        Layout.CurrentPageSet = 0;
        _scrollX = 0;
        _scrollY = 0;
        _currentPageNumber = Layout.Count > 0 ? 1 : 0;

        StoredProperties stored = null;
        if (document != null) { _properties.TryGetValue(document, out stored); }

        if (stored != null)
        {
            ViewMode = stored.Mode;
            Layout.ZoomFactor = stored.Zoom;
            Layout.CurrentPageSet = Math.Max(0, stored.PageSet);
        }

        UpdateViewport();

        if (stored != null && Layout.Count > 0)
        {
            var offset = stored.Position;
            if (offset.Index >= Layout.Count) { offset.Index = Layout.Count - 1; }

            ScrollToLayoutPoint(Layout.OffsetToPosition(offset));
        }

        CurrentPageChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Empties the view.</summary>
    public void Clear() => SetDocument(null);

    /// <summary>Reads every page's file again and redraws.</summary>
    public void Reload()
    {
        var (index, x, y) = Layout.PositionToOffset(new SKPoint(
            _scrollX + (ViewportSize.Width / 2f), _scrollY + (ViewportSize.Height / 2f)));
        _document?.Reload();
        UpdateViewport();
        CenterOn(Layout.OffsetToPosition((index, x, y)));
    }

    /// <summary>Steps the zoom up one notch, keeping the middle of the view still.</summary>
    public void ZoomIn()
    {
        foreach (double zoom in ZoomFactors)
        {
            if (zoom > ZoomFactor + 0.0001) { SetZoomFactor(zoom, ViewportCenter()); return; }
        }
    }

    /// <summary>Steps the zoom down one notch, keeping the middle of the view still.</summary>
    public void ZoomOut()
    {
        for (int i = ZoomFactors.Count - 1; i >= 0; i--)
        {
            if (ZoomFactors[i] < ZoomFactor - 0.0001)
            {
                SetZoomFactor(ZoomFactors[i], ViewportCenter());
                return;
            }
        }
    }

    /// <summary>Sets the zoom to 100%.</summary>
    public void ZoomOriginal() => SetZoomFactor(1.0, ViewportCenter());

    /// <summary>Sets the zoom, keeping a spot in the view still.</summary>
    /// <param name="zoom">The wanted zoom.</param>
    /// <param name="center">The spot to keep still, in layout coordinates, or null.</param>
    public void SetZoomFactor(double zoom, SKPoint? center)
    {
        zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        if (Math.Abs(zoom - Layout.ZoomFactor) < 0.00001) { return; }

        ViewMode = ViewMode.FixedScale;
        var offset = center.HasValue
            ? Layout.PositionToOffset(center.Value)
            : Layout.PositionToOffset(ViewportCenter());
        Layout.ZoomFactor = zoom;
        UpdateLayout(fit: false);
        CenterOn(Layout.OffsetToPosition(offset));
        ZoomChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows the page with the given 1-based number.</summary>
    /// <param name="number">The page number.</param>
    public void SetCurrentPageNumber(int number)
    {
        if (Layout.Count == 0) { return; }

        number = Math.Clamp(number, 1, Layout.Count);
        if (!Layout.ContinuousMode)
        {
            int set = Layout.PageSet(number - 1);
            if (set != Layout.CurrentPageSet)
            {
                Layout.CurrentPageSet = set;
                UpdateLayout(fit: Layout.ZoomsToFit && ViewMode != ViewMode.FixedScale);
            }
        }

        ScorePage page = Layout[number - 1];
        ScrollTo(new SKPointI(_scrollX, page.Y - Layout.Y - Layout.Margins.Top));
        SetCurrentPage(number);
    }

    /// <summary>Steps to the next page.</summary>
    public void NextPage() => SetCurrentPageNumber(_currentPageNumber + 1);

    /// <summary>Steps to the previous page.</summary>
    public void PreviousPage() => SetCurrentPageNumber(_currentPageNumber - 1);

    /// <summary>Scrolls so a rectangle of the layout is in view.</summary>
    /// <param name="rect">The rectangle, in layout coordinates.</param>
    /// <param name="margins">How much room to leave around it.</param>
    public void EnsureVisible(SKRectI rect, PageMargins margins = default)
    {
        SKSizeI viewport = ViewportSize;
        int x = _scrollX + Layout.X;
        int y = _scrollY + Layout.Y;

        int left = rect.Left - margins.Left;
        int right = rect.Right + margins.Right;
        int top = rect.Top - margins.Top;
        int bottom = rect.Bottom + margins.Bottom;

        if (right - left > viewport.Width) { x = left; }
        else if (right > x + viewport.Width) { x = right - viewport.Width; }
        else if (left < x) { x = left; }

        if (bottom - top > viewport.Height) { y = top; }
        else if (bottom > y + viewport.Height) { y = bottom - viewport.Height; }
        else if (top < y) { y = top; }

        ScrollToLayoutPoint(new SKPointI(x, y));
    }

    /// <summary>Scrolls so a point of the layout is in the middle of the view.</summary>
    /// <param name="point">The point, in layout coordinates.</param>
    public void CenterOn(SKPointI point)
    {
        SKSizeI viewport = ViewportSize;
        ScrollToLayoutPoint(new SKPointI(
            point.X - (viewport.Width / 2), point.Y - (viewport.Height / 2)));
    }

    /// <summary>Scrolls so a point of the layout is at the view's top-left.</summary>
    /// <param name="point">The point, in layout coordinates.</param>
    public void ScrollToLayoutPoint(SKPointI point)
        => ScrollTo(new SKPointI(point.X - Layout.X, point.Y - Layout.Y));

    /// <summary>
    /// Scrolls to an offset FROM THE LAYOUT'S OWN ORIGIN, clamped to it.
    /// </summary>
    /// <param name="offset">The wanted offset.</param>
    public void ScrollTo(SKPointI offset)
    {
        SKSizeI viewport = ViewportSize;
        int maxX = Math.Max(0, Layout.Width - viewport.Width);
        int maxY = Math.Max(0, Layout.Height - viewport.Height);
        int x = Math.Clamp(offset.X, 0, maxX);
        int y = Math.Clamp(offset.Y, 0, maxY);
        if (x == _scrollX && y == _scrollY) { return; }

        _scrollX = x;
        _scrollY = y;
        SyncScrollBars();
        UpdateCurrentPage();
        Invalidate();
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Returns the page and link at a point of the VIEW, if any.</summary>
    /// <param name="viewPoint">The point, relative to the drawing area.</param>
    /// <returns>The page and link, or nulls.</returns>
    public (ScorePage ScorePage, Link Link) LinkAt(SKPoint viewPoint)
    {
        if (!LinksEnabled) { return (null, null); }

        SKPointI offset = ViewOffset;
        SKPoint p = new SKPoint(viewPoint.X + offset.X, viewPoint.Y + offset.Y);
        ScorePage page = Layout.PageAt(p);
        if (page == null) { return (null, null); }

        IReadOnlyList<Link> links = page.LinksAt(new SKPoint(p.X - page.X, p.Y - page.Y));
        return links.Count > 0 ? (page, links[0]) : (null, null);
    }

    /// <summary>
    /// Highlights areas of pages, in the pages' own 0-to-1 coordinates.
    /// </summary>
    /// <param name="areas">Which rectangles on which pages.</param>
    /// <param name="highlighter">The style to draw them in.</param>
    /// <param name="milliseconds">How long to show them; 0 means until cleared.</param>
    public void Highlight(
        IReadOnlyDictionary<ScorePage, IReadOnlyList<SKRect>> areas, Highlighter highlighter, int milliseconds = 0)
    {
        if (highlighter == null || areas == null) { return; }

        if (_highlights.TryGetValue(highlighter, out HighlightSet existing)) { existing.Timer?.Stop(); }

        var set = new HighlightSet { Areas = areas };
        if (milliseconds > 0)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                ClearHighlight(highlighter);
            };
            set.Timer = timer;
            timer.Start();
        }

        _highlights[highlighter] = set;
        Invalidate();
    }

    /// <summary>Returns whether a highlighter is currently showing anything.</summary>
    /// <param name="highlighter">The style.</param>
    /// <returns>Whether it is showing.</returns>
    public bool IsHighlighting(Highlighter highlighter)
        => highlighter != null && _highlights.ContainsKey(highlighter);

    /// <summary>Removes one style's highlighting.</summary>
    /// <param name="highlighter">The style.</param>
    public void ClearHighlight(Highlighter highlighter)
    {
        if (highlighter == null || !_highlights.TryGetValue(highlighter, out HighlightSet set)) { return; }

        set.Timer?.Stop();
        _highlights.Remove(highlighter);
        Invalidate();
    }

    /// <summary>Removes all highlighting.</summary>
    public void ClearAllHighlights()
    {
        foreach (HighlightSet set in _highlights.Values) { set.Timer?.Stop(); }

        _highlights.Clear();
        Invalidate();
    }

    /// <summary>Returns the bounding rectangle of a set of page areas.</summary>
    /// <param name="areas">Which rectangles on which pages, in 0-to-1 coordinates.</param>
    /// <returns>The rectangle, in layout coordinates.</returns>
    public SKRectI HighlightRect(IReadOnlyDictionary<ScorePage, IReadOnlyList<SKRect>> areas)
    {
        bool any = false;
        float left = float.MaxValue;
        float top = float.MaxValue;
        float right = float.MinValue;
        float bottom = float.MinValue;
        foreach (var (page, rects) in areas)
        {
            SKMatrix map = page.MapToPage(1, 1);
            foreach (SKRect area in rects)
            {
                SKRect r = map.MapRect(area);
                left = Math.Min(left, r.Left + page.X);
                top = Math.Min(top, r.Top + page.Y);
                right = Math.Max(right, r.Right + page.X);
                bottom = Math.Max(bottom, r.Bottom + page.Y);
                any = true;
            }
        }

        return any
            ? new SKRectI((int)left, (int)top, (int)Math.Ceiling(right), (int)Math.Ceiling(bottom))
            : SKRectI.Empty;
    }

    /// <summary>Redraws the view.</summary>
    public void Invalidate() => _canvas.Invalidate();

    /// <summary>
    /// Records where the current document is being looked at, so showing it
    /// again puts it back.
    /// </summary>
    public void RememberProperties()
    {
        if (_document == null || Layout.Count == 0) { return; }

        SKPointI offset = ViewOffset;
        var point = new SKPoint(
            Math.Max(Layout.X, offset.X), Math.Max(Layout.Y, offset.Y));
        _properties[_document] = new StoredProperties
        {
            Position = Layout.PositionToOffset(point),
            Zoom = Layout.ZoomFactor,
            Mode = ViewMode,
            PageSet = Layout.CurrentPageSet,
        };
    }

    /// <summary>Re-fits and re-flows the layout for the current viewport.</summary>
    public void UpdateViewport()
    {
        UpdateLayout(fit: true);
        ViewChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Takes all the room the host offers, in both directions.
    /// </summary>
    /// <param name="availableSize">What the host is offering.</param>
    /// <returns>The size wanted.</returns>
    /// <remarks>
    /// A paged view has no natural size — it shows as much of the score as it
    /// is given room for. Left to its children it would ask for the height of
    /// the horizontal scroll bar and nothing more, because the drawing surface
    /// measures to nothing, and a host that hands out the DESIRED height (the
    /// dock's tab content does) would then give it twelve pixels.
    /// </remarks>
    protected override Size MeasureOverride(Size availableSize)
    {
        Size desired = base.MeasureOverride(availableSize);
        double width = double.IsInfinity(availableSize.Width)
            ? desired.Width
            : Math.Max(desired.Width, availableSize.Width);
        double height = double.IsInfinity(availableSize.Height)
            ? desired.Height
            : Math.Max(desired.Height, availableSize.Height);
        return new Size(width, height);
    }

    private static UIElement BuildScrollBarTemplateRoot(Orientation orientation)
    {
        //was previously: the theme's ScrollBar template, which paints nothing
        //for a standalone bar on the Skia heads (board trap 2). This is the
        //editor add-in's code-built tree, with the part names the control's own
        //track layout looks up.
        bool vertical = orientation == Orientation.Vertical;
        string prefix = vertical ? "Vertical" : "Horizontal";

        static RepeatButton CreateTrackButton(string name) => new RepeatButton
        {
            Name = name,
            IsTabStop = false,
            Template = new ControlTemplate(() => new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0, 0, 0, 0)),
            }),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            MinWidth = 0,
            MinHeight = 0,
        };

        var thumb = new Thumb
        {
            Name = prefix + "Thumb",
            IsTabStop = false,
            MinWidth = 0,
            MinHeight = 0,
            Template = new ControlTemplate(() => new Border
            {
                Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0xA0, 0x80, 0x80, 0x80)),
                CornerRadius = new CornerRadius(3),
                Margin = new Thickness(2),
            }),
        };

        var root = new Grid
        {
            Name = prefix + "Root",
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(0x14, 0x00, 0x00, 0x00)),
        };

        RepeatButton decrease = CreateTrackButton(prefix + "LargeDecrease");
        RepeatButton increase = CreateTrackButton(prefix + "LargeIncrease");
        if (vertical)
        {
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            SetRow(decrease, 0);
            SetRow(thumb, 1);
            SetRow(increase, 2);
        }
        else
        {
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            SetColumn(decrease, 0);
            SetColumn(thumb, 1);
            SetColumn(increase, 2);
        }

        root.Children.Add(decrease);
        root.Children.Add(thumb);
        root.Children.Add(increase);
        return root;
    }

    private SKPoint ViewportCenter()
    {
        SKSizeI viewport = ViewportSize;
        SKPointI offset = ViewOffset;
        return new SKPoint(offset.X + (viewport.Width / 2f), offset.Y + (viewport.Height / 2f));
    }

    private void UpdateLayout(bool fit)
    {
        if (_inLayoutUpdate) { return; }

        _inLayoutUpdate = true;
        try
        {
            SKSizeI viewport = ViewportSize;
            if (fit && Layout.Count > 0)
            {
                //The bars take space from the area the pages must fit into, and
                //whether they are needed depends on the fit — upstream settles
                //this by fitting, laying out and fitting again.
                Layout.Fit(viewport, ViewMode);
                Layout.ZoomFactor = Math.Clamp(Layout.ZoomFactor, MinZoom, MaxZoom);
                Layout.Update();
                viewport = AvailableViewport();
                Layout.Fit(viewport, ViewMode);
                Layout.ZoomFactor = Math.Clamp(Layout.ZoomFactor, MinZoom, MaxZoom);
            }

            Layout.Update();
            ScrollTo(new SKPointI(_scrollX, _scrollY));
            SyncScrollBars();
            UpdateCurrentPage();
            Invalidate();
        }
        finally
        {
            _inLayoutUpdate = false;
        }
    }

    private SKSizeI AvailableViewport()
    {
        SKSizeI viewport = ViewportSize;
        return new SKSizeI(Math.Max(1, viewport.Width), Math.Max(1, viewport.Height));
    }

    private void SyncScrollBars()
    {
        SKSizeI viewport = ViewportSize;
        _updatingScrollBars = true;
        try
        {
            _verticalScrollBar.Minimum = 0;
            _verticalScrollBar.Maximum = Math.Max(0, Layout.Height - viewport.Height);
            _verticalScrollBar.ViewportSize = viewport.Height;
            _verticalScrollBar.SmallChange = WheelStep;
            _verticalScrollBar.LargeChange = viewport.Height;
            _verticalScrollBar.Value = _scrollY;
            _verticalScrollBar.Visibility = Layout.Height > viewport.Height
                ? Visibility.Visible
                : Visibility.Collapsed;

            _horizontalScrollBar.Minimum = 0;
            _horizontalScrollBar.Maximum = Math.Max(0, Layout.Width - viewport.Width);
            _horizontalScrollBar.ViewportSize = viewport.Width;
            _horizontalScrollBar.SmallChange = WheelStep;
            _horizontalScrollBar.LargeChange = viewport.Width;
            _horizontalScrollBar.Value = _scrollX;
            _horizontalScrollBar.Visibility = Layout.Width > viewport.Width
                ? Visibility.Visible
                : Visibility.Collapsed;

            _cornerSpacer.Visibility
                = _verticalScrollBar.Visibility == Visibility.Visible
                    && _horizontalScrollBar.Visibility == Visibility.Visible
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
        finally
        {
            _updatingScrollBars = false;
        }
    }

    private void UpdateCurrentPage()
    {
        if (Layout.Count == 0) { return; }


        SKSizeI viewport = ViewportSize;
        SKPointI offset = ViewOffset;
        var probe = new SKPoint(
            offset.X + (viewport.Width / 2f), offset.Y + Math.Min(viewport.Height / 2f, 10f));
        ScorePage page = Layout.PageAt(probe) ?? Layout.NearestPageAt(probe);
        if (page == null) { return; }

        int number = Layout.IndexOf(page) + 1;
        if (number > 0) { SetCurrentPage(number); }
    }

    private void SetCurrentPage(int number)
    {
        if (number == _currentPageNumber) { return; }

        _currentPageNumber = number;
        CurrentPageChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        SKCanvas canvas = e.Surface.Canvas;
        //The staging buffer persists between frames, so a missing clear shows
        //the previous frame under this one (board trap 1).
        canvas.Clear(new SKColor(0x50, 0x50, 0x50));

        if (Layout.Count == 0) { return; }

        SKPointI offset = ViewOffset;
        var visible = new SKRect(
            offset.X, offset.Y, offset.X + e.Info.Width, offset.Y + e.Info.Height);
        foreach (ScorePage page in Layout.PagesAt(visible).OrderBy(Layout.IndexOf))
        {
            var geometry = new SKRect(page.X, page.Y, page.X + page.Width, page.Y + page.Height);
            SKRect onScreen = geometry;
            onScreen.Offset(-offset.X, -offset.Y);

            if (DropShadowEnabled)
            {
                using var shadow = new SKPaint { Color = new SKColor(0, 0, 0, 0x60) };
                canvas.DrawRect(
                    new SKRect(onScreen.Left + 3, onScreen.Top + 3, onScreen.Right + 3, onScreen.Bottom + 3),
                    shadow);
            }

            using (var paper = new SKPaint { Color = page.PaperColor ?? PaperColor })
            {
                canvas.DrawRect(onScreen, paper);
            }

            canvas.Save();
            canvas.Translate(onScreen.Left, onScreen.Top);
            canvas.ClipRect(new SKRect(0, 0, page.Width, page.Height));
            page.Paint(canvas, new SKRect(0, 0, page.Width, page.Height));
            canvas.Restore();
        }

        if (_highlights.Count == 0) { return; }

        canvas.Save();
        canvas.Translate(-offset.X, -offset.Y);
        foreach (var (highlighter, set) in _highlights)
        {
            var rects = new List<SKRect>();
            foreach (var (page, areas) in set.Areas)
            {
                if (Layout.IndexOf(page) < 0) { continue; }

                SKMatrix map = page.MapToPage(1, 1);
                foreach (SKRect area in areas)
                {
                    SKRect r = map.MapRect(area);
                    r.Offset(page.X, page.Y);
                    rects.Add(r);
                }
            }

            if (rects.Count > 0) { highlighter.PaintRects(canvas, rects); }
        }

        canvas.Restore();
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!LinksEnabled) { return; }

        Point position = e.GetCurrentPoint(_canvas).Position;
        var (page, link) = LinkAt(new SKPoint((float)position.X, (float)position.Y));
        if (ReferenceEquals(link, _currentLink)) { return; }

        if (_currentLink != null)
        {
            _currentLink = null;
            _currentLinkPage = null;
            if (LinkHighlighter != null) { ClearHighlight(LinkHighlighter); }

            LinkLeft?.Invoke(this, EventArgs.Empty);
        }

        if (link != null)
        {
            _currentLink = link;
            _currentLinkPage = page;
            if (LinkHighlighter != null)
            {
                Highlight(
                    new Dictionary<ScorePage, IReadOnlyList<SKRect>> { [page] = new[] { link.Rect() } },
                    LinkHighlighter,
                    3000);
            }

            LinkHovered?.Invoke(this, new MusicLinkEventArgs(page, link));
        }
    }

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        if (_currentLink == null) { return; }

        _currentLink = null;
        _currentLinkPage = null;
        if (LinkHighlighter != null) { ClearHighlight(LinkHighlighter); }

        LinkLeft?.Invoke(this, EventArgs.Empty);
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!LinksEnabled) { return; }

        PointerPoint point = e.GetCurrentPoint(_canvas);
        var (page, link) = LinkAt(new SKPoint((float)point.Position.X, (float)point.Position.Y));
        if (link == null) { return; }

        var args = new MusicLinkEventArgs(page, link, point.Properties)
        {
            IsShiftDown = e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift),
        };
        LinkClicked?.Invoke(this, args);
    }

    private void OnRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        Point position = e.GetPosition(_canvas);
        var (page, link) = LinkAt(new SKPoint((float)position.X, (float)position.Y));
        ContextMenuRequested?.Invoke(
            this, new MusicContextMenuEventArgs(_canvas, position, page, link));
        e.Handled = true;
    }

    private void OnPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(_canvas);
        int delta = point.Properties.MouseWheelDelta;
        if (delta == 0) { return; }

        if (e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control))
        {
            SKPointI offset = ViewOffset;
            SKPoint centre = new SKPoint(
                (float)point.Position.X + offset.X, (float)point.Position.Y + offset.Y);
            if (delta > 0)
            {
                foreach (double zoom in ZoomFactors)
                {
                    if (zoom > ZoomFactor + 0.0001) { SetZoomFactor(zoom, centre); break; }
                }
            }
            else
            {
                for (int i = ZoomFactors.Count - 1; i >= 0; i--)
                {
                    if (ZoomFactors[i] < ZoomFactor - 0.0001)
                    {
                        SetZoomFactor(ZoomFactors[i], centre);
                        break;
                    }
                }
            }

            e.Handled = true;
            return;
        }

        int steps = delta / 120;
        if (steps == 0) { steps = delta > 0 ? 1 : -1; }

        if (e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Shift))
        {
            ScrollTo(new SKPointI(_scrollX - (steps * WheelStep), _scrollY));
        }
        else
        {
            ScrollTo(new SKPointI(_scrollX, _scrollY - (steps * WheelStep)));
        }

        e.Handled = true;
    }

    /// <summary>What this view remembers about a score between showings.</summary>
    private sealed class StoredProperties
    {
        internal (int Index, double X, double Y) Position { get; set; }

        internal double Zoom { get; set; }

        internal ViewMode Mode { get; set; }

        internal int PageSet { get; set; }
    }

    /// <summary>One style's highlighted areas, and the timer that removes them.</summary>
    private sealed class HighlightSet
    {
        internal IReadOnlyDictionary<ScorePage, IReadOnlyList<SKRect>> Areas { get; set; }

        internal DispatcherTimer Timer { get; set; }
    }
}
