// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;

namespace Fresco.Brix.Commands; //was previously: frescobaldi/matcher.py (class Actions)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The View menu's two commands for the token pair around the caret: jump to
/// the partner, or select from here to it.
/// </summary>
/// <remarks>
/// The MATCHING itself was ported at W2 — <c>Editor/TokenMatcher</c> is what
/// draws the highlight — and these two commands are the other half of
/// upstream's <c>matcher.Matcher</c>: they move or select over an answer the
/// application already has. Upstream gives neither a default shortcut.
/// </remarks>
public sealed class MatchingPairActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "matchingpair";

    /// <summary>Creates the matching-pair commands.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public MatchingPairActions(SettingsStore settings = null)
        : base(CollectionName, settings)
        => Initialize();

    /// <summary>View &gt; Matching Pair.</summary>
    public AppAction ViewMatchingPair { get; private set; }

    /// <summary>View &gt; Select Matching Pair.</summary>
    public AppAction ViewMatchingPairSelect { get; private set; }

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        ViewMatchingPair = Add("view_matching_pair");
        ViewMatchingPairSelect = Add("view_matching_pair_select");
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        ViewMatchingPair.Text = I18n.Get("Matching Pai&r");
        ViewMatchingPairSelect.Text = I18n.Get("&Select Matching Pair");
    }
}
