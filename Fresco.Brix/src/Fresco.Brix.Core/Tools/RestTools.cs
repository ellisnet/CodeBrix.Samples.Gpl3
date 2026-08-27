// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Ly;
using Fresco.Brix.Services;

namespace Fresco.Brix.Tools; //was previously: frescobaldi/rest/rest.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The three commands that swap one kind of rest for another: full-measure
/// rests for spacers, spacers for full-measure rests, and positioned rests for
/// plain ones.
/// </summary>
/// <remarks>Each works over the selection, or over the whole document when
/// there is none — which is what the menu entries promise.</remarks>
public static class RestTools
{
    /// <summary>Turns <c>R</c> into <c>s</c>.</summary>
    /// <param name="cursor">The range.</param>
    public static void FullMeasureRestToSpacer(Cursor cursor)
        => Rests.ReplaceFmRest(cursor, "s");

    /// <summary>Turns <c>s</c> into <c>R</c>.</summary>
    /// <param name="cursor">The range.</param>
    public static void SpacerToFullMeasureRest(Cursor cursor)
        => Rests.ReplaceSpacer(cursor, "R");

    /// <summary>Turns <c>c\rest</c> into <c>r</c>.</summary>
    /// <param name="cursor">The range.</param>
    public static void PositionedRestToRest(Cursor cursor)
        => Rests.ReplaceRestComm(cursor, "r");
}

/// <summary>The Rest menu's commands.</summary>
public sealed class RestActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "rest";

    /// <summary>Creates the collection.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public RestActions(SettingsStore settings = null)
        : base(CollectionName, settings) => Initialize();

    /// <summary>Gets the full-measure-rests-to-spacers command.</summary>
    public AppAction RestFmRestToSpacer { get; private set; }

    /// <summary>Gets the spacers-to-full-measure-rests command.</summary>
    public AppAction RestSpacerToFmRest { get; private set; }

    /// <summary>Gets the positioned-rests-to-plain-rests command.</summary>
    public AppAction RestCommToRest { get; private set; }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Rest");

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        RestFmRestToSpacer = Add("rest_fmrest2spacer");
        RestSpacerToFmRest = Add("rest_spacer2fmrest");
        RestCommToRest = Add("rest_restcomm2rest");
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        RestFmRestToSpacer.Text
            = I18n.Get("Replace full measure rests with spacer rests");
        RestFmRestToSpacer.ToolTip = I18n.Get(
            "Change all R to s in this document or in the selection.");
        RestSpacerToFmRest.Text
            = I18n.Get("Replace spacer rests with full measure rests");
        RestSpacerToFmRest.ToolTip = I18n.Get(
            "Change all s to R in this document or in the selection.");
        RestCommToRest.Text = I18n.Get("Replace positioned rests with plain rests");
        RestCommToRest.ToolTip = I18n.Get(
            "Change all \\rest with r in this document or in the selection.");
    }
}
