// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The collection every test class that builds a CodeBrix.Platform XAML object belongs to.
/// </summary>
/// <remarks>
/// <para>
/// The framework's object model is single-threaded, as a UI framework's is: the dependency
/// property registry, the default-style registry and the resource dictionaries are plain
/// dictionaries with no lock anywhere. A running application never notices, because
/// everything happens on the window's thread; a test assembly does, because xUnit runs test
/// CLASSES in parallel and the host-free dispatcher bootstrap tells every one of those
/// threads that it has thread access.
/// </para>
/// <para>
/// MEASURED, S-1 spike 2026-09-13: with two classes building toolbar controls at the same
/// time, the whole run failed differently every time - ArgumentException
/// "Argument_AddingDuplicate" from HashtableEx.Add, NullReferenceException in
/// DependencyObjectStore.InvokeCallbacks, and an
/// InvalidOperationException "Operations that change non-concurrent collections must have
/// exclusive access" raised inside a test that has nothing to do with XAML at all. Every one
/// of those classes passed when run on its own.
/// </para>
/// <para>
/// <c>DisableParallelization</c> makes this collection run on its own, not alongside any
/// other collection, so the XAML work is serialised while the other 3,500 tests keep running
/// in parallel with each other.
/// </para>
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public class XamlTestCollection
{
    /// <summary>The collection's name.</summary>
    public const string Name = "CodeBrix.Platform XAML";
}
