// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Fresco.Brix.Core.Tests;

// The pattern is the CommandBar add-in's own test suite's
// (CodeBrix.Platform/src/AddIns/Platform.UI.CommandBar.Tests/DispatcherInitializer.cs),
// re-namespaced and with the framework's nullable annotations removed because this
// application does not enable them. Reflection is used deliberately, for the reason that
// suite records: a test-only concern should not widen the framework's internal surface,
// and this application has no way to grant itself one anyway.

/// <summary>
/// Installs host-free dispatcher overrides so a CodeBrix.Platform XAML control can be
/// constructed and measured in a test process that has no application head.
/// </summary>
/// <remarks>
/// <para>
/// Every thread reports that it has dispatcher access, and dispatched work runs inline.
/// That is the right semantic for this suite, which is synchronous and asserts on
/// immediate effects: a property set in a test is seen by the control's changed handler
/// before the setter returns, rather than on some later dispatcher turn that never comes.
/// </para>
/// </remarks>
internal static class DispatcherInitializer
{
    /// <summary>
    /// Fills in the framework's dispatcher overrides, once, before any test runs.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The framework no longer carries the type or the two fields this reflects on, so the
    /// bootstrap here needs updating to match it.
    /// </exception>
    [ModuleInitializer]
    internal static void Initialize()
    {
        var dispatcherType = Type.GetType(
            "CodeBrix.Platform.UI.Dispatching.NativeDispatcher, CodeBrix.Platform.UI.Dispatching");
        if (dispatcherType is null)
        {
            throw new InvalidOperationException(
                "Could not find NativeDispatcher in CodeBrix.Platform.UI.Dispatching. The "
                + "test project's dispatcher bootstrap needs updating to match the framework.");
        }

        var hasAccessField = dispatcherType.GetField(
            "HasThreadAccessOverride", BindingFlags.NonPublic | BindingFlags.Static);
        var dispatchField = dispatcherType.GetField(
            "DispatchOverride", BindingFlags.NonPublic | BindingFlags.Static);
        if (hasAccessField is null || dispatchField is null)
        {
            throw new InvalidOperationException(
                "NativeDispatcher no longer exposes HasThreadAccessOverride/DispatchOverride. "
                + "The test project's dispatcher bootstrap needs updating to match the framework.");
        }

        if (hasAccessField.GetValue(null) is null)
        {
            hasAccessField.SetValue(null, (Func<bool>)(static () => true));
        }

        if (dispatchField.GetValue(null) is null)
        {
            //DispatchOverride is Action<Action, NativeDispatcherPriority>; the enumeration
            //is internal to the framework, so the delegate is bound through a generic
            //method instantiated with it.
            var priorityType = dispatchField.FieldType.GetGenericArguments()[1];
            var dispatchMethod = typeof(DispatcherInitializer)
                .GetMethod(nameof(DispatchInline), BindingFlags.NonPublic | BindingFlags.Static)
                .MakeGenericMethod(priorityType);
            dispatchField.SetValue(
                null, Delegate.CreateDelegate(dispatchField.FieldType, dispatchMethod));
        }
    }

    /// <summary>Runs dispatched work on the calling thread.</summary>
    /// <typeparam name="TPriority">The framework's own priority enumeration.</typeparam>
    /// <param name="action">The work.</param>
    /// <param name="priority">The priority, which inline dispatch ignores.</param>
    private static void DispatchInline<TPriority>(Action action, TPriority priority) => action();
}
