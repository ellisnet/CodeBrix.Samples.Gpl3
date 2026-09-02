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
