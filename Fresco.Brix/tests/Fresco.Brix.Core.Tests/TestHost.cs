// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.CommandBar;
using System;
using System.Reflection;

namespace Fresco.Brix.Core.Tests;

// The pattern is the CommandBar add-in's own test suite's
// (CodeBrix.Platform/src/AddIns/Platform.UI.CommandBar.Tests/TestHost.cs), re-namespaced.

/// <summary>
/// The two things an application head would have done before a control is measured, done
/// here instead: register the add-in's default styles, and load the text engine's native
/// library.
/// </summary>
/// <remarks>
/// <para>
/// Only the tests that MEASURE need this. Everything else - command binding, tooltip
/// composition, which button carries which flyout - is about properties rather than pixels
/// and runs against a bare control. Keeping the two apart means a failure in the measuring
/// tests points at layout rather than at the harness.
/// </para>
/// <para>
/// Reflection is used for the text engine deliberately, for the reason already recorded in
/// <see cref="DispatcherInitializer"/>.
/// </para>
/// </remarks>
internal static class TestHost
{
    /// <summary>Guards the one-time preparation against a second test thread.</summary>
    private static readonly object Gate = new object();

    /// <summary>Whether the process has already been prepared.</summary>
    private static bool _ready;

    /// <summary>Prepares the process to measure a templated control, once.</summary>
    /// <remarks>
    /// The flag is set AFTER the work, under a lock, not before it. xUnit runs test classes
    /// in parallel, and a caller that returned early while another thread was still half way
    /// through the registration measured a control whose template was not there yet -
    /// MEASURED as InvalidOperationException "ResourceDictionary was registered as style
    /// provider for ToolBarOverflowButton but doesn't contain matching style", raised only
    /// when the whole suite ran and never when one class ran on its own.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The framework no longer exposes the text engine's initialization entry point, so the
    /// bootstrap here needs updating to match it.
    /// </exception>
    internal static void EnsureReady()
    {
        lock (Gate)
        {
            if (_ready)
            {
                return;
            }

            RegisterDefaultStyles();
            InitializeTextEngine();

            _ready = true;
        }
    }

    /// <summary>
    /// Brings the add-in's default styles into a process that has no application of its own.
    /// </summary>
    /// <remarks>
    /// In an application the XAML source generator writes the application's own
    /// <c>GlobalStaticResources</c>, whose <c>Initialize</c> calls
    /// <c>RegisterDefaultStyles</c> on every referenced assembly that has any - which is how
    /// a toolbar button in the running Fresco.Brix window finds the template in the add-in's
    /// Themes/Generic.xaml. A test project has no XAML of its own, so nothing generates that
    /// call. <see cref="DefaultStyleInitializer"/> already made the first two calls from a
    /// module initializer; this method adds the third, which that initializer does not make.
    /// </remarks>
    private static void RegisterDefaultStyles()
    {
        DefaultStyleInitializer.Initialize();

        GlobalStaticResources.RegisterResourceDictionariesBySource();
    }

    /// <summary>Loads the native library the text engine needs to lay a string out.</summary>
    /// <remarks>
    /// An application head loads it from a generated module initializer. Without it,
    /// measuring a TextBlock throws deep inside the bidi pass and the framework swallows it,
    /// so the text silently measures as nothing - which would make every "does the label take
    /// space?" assertion pass for the wrong reason.
    /// </remarks>
    private static void InitializeTextEngine()
    {
        var unicodeText = Type.GetType(
            "Microsoft.UI.Xaml.Documents.UnicodeText, CodeBrix.Platform.UI");
        var initialize = unicodeText?.GetMethod(
            "EnsureEngineInitialized",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);

        if (initialize is null)
        {
            throw new InvalidOperationException(
                "Could not find UnicodeText.EnsureEngineInitialized in CodeBrix.Platform.UI. "
                + "The test project's text-engine bootstrap needs updating to match the "
                + "framework.");
        }

        initialize.Invoke(null, null);
    }
}
