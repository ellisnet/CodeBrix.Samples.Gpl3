// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Helpers;
using CodeBrix.Platform.Simple;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;

namespace Fresco.Brix;

public partial class App : Application
{
    //The size the window opens at when nothing is remembered, and the smallest size it
    //may be dragged to. The window's chrome (menu bar, the two toolbars on one row, the
    //document tabs and the status line) takes about 150 units of height before the dock
    //shell starts, and the shell puts the Music View beside the editor, so 1280 by 840
    //leaves an editor column and a whole engraved page side by side at a readable zoom.
    //The minimum is where the two toolbars stop fitting on one row and the widest thing
    //the application raises (a dialog whose content asks for 700) stops fitting.
    private const int LaunchWidth = 1280;
    private const int LaunchHeight = 840;
    private const int MinimumWidth = 900;
    private const int MinimumHeight = 620;

    public App()
    {
        //Set Roboto as the default font for all text in the application
        global::CodeBrix.Platform.UI.FeatureConfiguration.Font.DefaultTextFontFamily =
            "ms-appx:///CodeBrix.Platform.Fonts.Roboto/Fonts/Roboto.ttf";

        //Fonts consulted for characters the default font has no glyph for
        global::CodeBrix.Platform.UI.FeatureConfiguration.Font.FallbackFontFamilies =
        [
            "ms-appx:///CodeBrix.Platform.Fonts.Roboto/Fonts/NotoSansArmenian.ttf",
            "ms-appx:///CodeBrix.Platform.Fonts.Roboto/Fonts/NotoSansGeorgian.ttf",
        ];

        SimpleServiceResolver.CreateInstance(HostHelper.GetHost(), services =>
        {
            //Register the app's services here
            services.AddSingleton<Services.SettingsStore>();
            services.AddSingleton<Services.RecentFiles>();

            //One engine per process. It is a singleton because the engine's
            //state is process-global, not because one is convenient.
            services.AddSingleton<Engrave.LilyPortEngine>();
        });
        SimpleViewModel.SetIsDesignMode(false);

        //The size the first window opens at. ApplicationView.PreferredLaunchViewSize is
        //the only public seam an application has for its own launch size: every desktop
        //head reads it while it is creating the native window and falls back to the
        //platform's own 1024 by 640 when it is empty, so it has to be set before any
        //window exists. On the Linux X11 head the numbers are NATIVE pixels of the
        //window's CLIENT area, which on a display at scale 1 is the same as logical
        //units; how each of the other heads reads them is written up in the report that
        //accompanied this change. The value is set on every launch, unconditionally,
        //because the platform remembers it in its own settings file; setting it every
        //time keeps that file in step with this source file instead of letting an old
        //value linger. A size the user left behind still wins: the window's own store
        //remembers it, and MainPage.RestoreWindowLayout applies it once the page loads.
        Windows.UI.ViewManagement.ApplicationView.PreferredLaunchViewSize =
            new Windows.Foundation.Size(LaunchWidth, LaunchHeight);

        InitializeComponent();
    }

    protected Window MainWindow { get; private set; }

    /// <summary>
    /// The application's window, so the page can put the current document's
    /// name — and the engine's loading state — in its title bar.
    /// </summary>
    public static Window Shell { get; private set; }

    /// <summary>
    /// The documents named on the command line. Each head's Program.Main fills
    /// this in before the host is built; the window opens them at startup, and
    /// opens one empty document when there are none.
    /// </summary>
    public static System.Collections.Generic.IReadOnlyList<string> CommandLinePaths
    { get; set; } = Array.Empty<string>();

    /// <summary>
    /// The whole command line, including the encoding and the place to go to.
    /// </summary>
    /// <remarks>Upstream's <c>args</c>: the local startup path honours
    /// <c>--line</c>, <c>--column</c> and <c>--encoding</c> just as the
    /// single-instance handover does.</remarks>
    public static Services.CommandLineArguments CommandLine { get; set; }
        = Services.CommandLineArguments.Parse(null);

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new Window
        {
            Title = "Fresco.Brix"
        };
        Shell = MainWindow;

        //The smallest size the user may drag the window to. The presenter is already in
        //place here: constructing a Window builds its native window straight away once
        //the application has finished initializing, and the window's default presenter is
        //an OverlappedPresenter. Setting the minimum now, before Activate(), means the
        //window manager has the constraint before the window is ever shown; setting it
        //after Activate() also works, but the window has been mapped once by then. No
        //maximum is set, so the window can still be resized up and maximized.
        if (MainWindow.AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            presenter.PreferredMinimumWidth = MinimumWidth;
            presenter.PreferredMinimumHeight = MinimumHeight;
        }

        if (MainWindow.Content is not Frame rootFrame)
        {
            rootFrame = new Frame();
            MainWindow.Content = rootFrame;
            rootFrame.NavigationFailed += OnNavigationFailed;
        }

        if (rootFrame.Content == null)
        {
            rootFrame.Navigate(typeof(Views.MainPage), args.Arguments);
        }

        MainWindow.Activate();
    }

    void OnNavigationFailed(object sender, NavigationFailedEventArgs e)
    {
        throw new InvalidOperationException($"Failed to load {e.SourcePageType.FullName}: {e.Exception}");
    }

    // Called from each head's Program.Main BEFORE building the host.
    public static void InitializeLogging()
    {
#if DEBUG
        var factory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddFilter("CodeBrix.Platform", LogLevel.Warning);
            builder.AddFilter("Windows", LogLevel.Warning);
            builder.AddFilter("Microsoft", LogLevel.Warning);
        });

        global::CodeBrix.Platform.Extensions.LogExtensionPoint.AmbientLoggerFactory = factory;
        global::CodeBrix.Platform.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();
#endif
    }
}
