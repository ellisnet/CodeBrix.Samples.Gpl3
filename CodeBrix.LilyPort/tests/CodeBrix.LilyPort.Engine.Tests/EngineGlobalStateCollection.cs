// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// Serialises every test class that touches the engine's process-global state.
/// <para>
/// The registries, the stub call counts, the reader's hash extensions and the ambient
/// interpreter are all process-wide, exactly as they are in the C++. Plan risk 7
/// records the consequence, and this is where it is enforced: xUnit runs test classes
/// in parallel by default, and a class that reads
/// <c>LilyPondScheme.Current</c> while another is still bootstrapping one sees a
/// half-loaded Scheme layer. That is not hypothetical — it silently made every
/// context property assignment fail the type check.
/// </para>
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class EngineGlobalStateCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "engine-global-state";
}
