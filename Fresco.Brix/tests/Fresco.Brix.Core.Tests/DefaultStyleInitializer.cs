// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.CommandBar;
using System.Runtime.CompilerServices;

namespace Fresco.Brix.Core.Tests;

// The pattern is the CommandBar add-in's own test suite's
// (CodeBrix.Platform/src/AddIns/Platform.UI.CommandBar.Tests/DefaultStyleInitializer.cs),
// re-namespaced. An application head calls the add-in's generated resource entry points at
// startup; a host-free test process has no head, so this calls them itself. Without it the
// add-in's Themes/Generic.xaml is never registered, DefaultStyleKey finds nothing, a
// ToolBar has no template and therefore no items panel, and every layout assertion would
// measure an empty control.
//
// MEASURED in the S-1 spike, 2026-09-13, with the framework consumed from NuGet rather
// than built from source: with no call, an empty `new ToolBar()` measured 0x0 and its
// Template was null after Measure(500x100), and so did a bar holding two ToolButtons; with
// the two calls below the empty bar measured 8x9 (its own chrome) and the two-button bar
// measured 217x41. That is the same measurement the add-in's own suite records against the
// from-source framework.

/// <summary>
/// Registers the CommandBar add-in's default styles with the framework so a host-free test
/// can give one of its controls a template.
/// </summary>
/// <remarks>
/// Both calls are idempotent - the generated code guards itself - so it does not matter how
/// many module initializers in this suite ask for them.
/// </remarks>
internal static class DefaultStyleInitializer
{
    /// <summary>Registers the add-in's default styles, once, before any test runs.</summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        //Module initializers run in an order nothing here controls, and registering a
        //resource dictionary creates one, which asks the dispatcher whether it has thread
        //access. The add-in's own suite measured a NullReferenceException in
        //NativeDispatcher.GetHasThreadAccess when the styles were registered before the
        //dispatcher bootstrap. DispatcherInitializer.Initialize only fills in what is still
        //null, so calling it here and letting the runtime call it again is harmless.
        DispatcherInitializer.Initialize();

        GlobalStaticResources.Initialize();
        GlobalStaticResources.RegisterDefaultStyles();
    }
}
