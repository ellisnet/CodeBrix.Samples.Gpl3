// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The EPG15 entry points: line breaking and broken spanners.
/// </summary>
/// <remarks>
/// <para>
/// Two of these were the most demanded unported names in the whole project:
/// <c>ly:spanner::calc-normalized-endpoints</c> at 2,991 calls per sweep and
/// <c>ly:spanner::set-spacing-rods</c> at 961. Both were registered stubs before this
/// group, which is why they were demanded so loudly and so silently at the same time.
/// </para>
/// <para>
/// The five translators the group adds — <c>Break_align_engraver</c>,
/// <c>Forbid_line_break_engraver</c>, <c>Spanner_break_forbid_engraver</c>,
/// <c>Pure_from_neighbor_engraver</c> and <c>Keep_alive_together_engraver</c> — carry no
/// Scheme surface and are registered in <c>TranslatorCreator</c> instead.
/// </para>
/// <para>
/// <c>ly:hara-kiri-group-spanner::pure-height</c> takes its start and end columns as REAL
/// arguments, not ignored ones: the whole point of the callback is that a staff can be
/// empty on one candidate line and busy on another.
/// </para>
/// </remarks>
public static class Epg15Callbacks
{
    /// <summary>Installs the callbacks, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        // ----- spanner.cc -----

        interpreter.DefinePrimitive("ly:spanner::calc-normalized-endpoints", 1, 1, a =>
            Spanner.CalcNormalizedEndpoints(
                AsSpanner(a[0], "ly:spanner::calc-normalized-endpoints")));

        interpreter.DefinePrimitive("ly:spanner::set-spacing-rods", 1, 1, a =>
            Spanner.SetSpacingRods(AsSpanner(a[0], "ly:spanner::set-spacing-rods")));

        interpreter.DefinePrimitive("ly:spanner::bounds-width", 1, 1, a =>
            Spanner.BoundsWidth(AsSpanner(a[0], "ly:spanner::bounds-width")));

        interpreter.DefinePrimitive("ly:spanner::kill-zero-spanned-time", 1, 1, a =>
            Spanner.KillZeroSpannedTime(
                AsSpanner(a[0], "ly:spanner::kill-zero-spanned-time")));

        // ----- unpure-pure-container.cc -----

        interpreter.DefinePrimitive("ly:make-unpure-pure-container", 1, 2, a =>
            new UnpurePureContainer(a[0], a.Length > 1 ? a[1] : null));

        interpreter.DefinePrimitive("ly:unpure-pure-container-unpure-part", 1, 1, a =>
            AsUnpurePure(a[0], "ly:unpure-pure-container-unpure-part").Unpure);

        interpreter.DefinePrimitive("ly:unpure-pure-container-pure-part", 1, 1, a =>
            AsUnpurePure(a[0], "ly:unpure-pure-container-pure-part").PurePart());

        // ----- break-alignment-interface.cc -----

        interpreter.DefinePrimitive("ly:break-alignment-interface::calc-positioning-done", 1, 1, a =>
            BreakAlignmentInterface.CalcPositioningDone(
                AsItem(a[0], "ly:break-alignment-interface::calc-positioning-done")));

        interpreter.DefinePrimitive(
            "ly:break-alignment-interface::find-nonempty-break-align-group", 2, 2, a =>
            {
                Grob result = BreakAlignmentInterface.FindNonemptyBreakAlignGroup(
                    AsItem(a[0], "ly:break-alignment-interface::find-nonempty-break-align-group"),
                    a[1]);
                return result ?? (object)false;
            });

        interpreter.DefinePrimitive("ly:break-alignable-interface::find-parent", 1, 1, a =>
            {
                Item result = BreakAlignableInterface.FindParent(
                    AsGrob(a[0], "ly:break-alignable-interface::find-parent"));
                return result ?? (object)false;
            });

        interpreter.DefinePrimitive("ly:break-alignable-interface::self-align-callback", 1, 1, a =>
            BreakAlignableInterface.SelfAlignCallback(
                AsGrob(a[0], "ly:break-alignable-interface::self-align-callback")));

        interpreter.DefinePrimitive("ly:break-aligned-interface::calc-average-anchor", 1, 1, a =>
            BreakAlignedInterface.CalcAverageAnchor(
                AsGrob(a[0], "ly:break-aligned-interface::calc-average-anchor")));

        interpreter.DefinePrimitive(
            "ly:break-aligned-interface::calc-joint-anchor-alignment", 1, 1, a =>
            (long)BreakAlignedInterface.CalcJointAnchorAlignment(
                AsGrob(a[0], "ly:break-aligned-interface::calc-joint-anchor-alignment")).Value);

        interpreter.DefinePrimitive(
            "ly:break-aligned-interface::calc-extent-aligned-anchor", 1, 1, a =>
            BreakAlignedInterface.CalcExtentAlignedAnchor(
                AsGrob(a[0], "ly:break-aligned-interface::calc-extent-aligned-anchor")));

        interpreter.DefinePrimitive("ly:break-aligned-interface::calc-break-visibility", 1, 1, a =>
            BreakAlignedInterface.CalcBreakVisibility(
                AsGrob(a[0], "ly:break-aligned-interface::calc-break-visibility")));

        // ----- hara-kiri-group-spanner.cc -----

        interpreter.DefinePrimitive("ly:hara-kiri-group-spanner::y-extent", 1, 1, a =>
            HaraKiriGroupSpanner.YExtent(AsGrob(a[0], "ly:hara-kiri-group-spanner::y-extent")));

        interpreter.DefinePrimitive("ly:hara-kiri-group-spanner::calc-skylines", 1, 1, a =>
            HaraKiriGroupSpanner.CalcSkylines(
                AsGrob(a[0], "ly:hara-kiri-group-spanner::calc-skylines")));

        interpreter.DefinePrimitive("ly:hara-kiri-group-spanner::pure-height", 3, 3, a =>
            HaraKiriGroupSpanner.PureHeight(
                AsGrob(a[0], "ly:hara-kiri-group-spanner::pure-height"),
                AsRank(a[1], 0),
                AsRank(a[2], int.MaxValue)));

        interpreter.DefinePrimitive(
            "ly:hara-kiri-group-spanner::force-hara-kiri-callback", 1, 1, a =>
            HaraKiriGroupSpanner.ForceHaraKiriCallback(
                AsGrob(a[0], "ly:hara-kiri-group-spanner::force-hara-kiri-callback")));

        interpreter.DefinePrimitive(
            "ly:hara-kiri-group-spanner::force-hara-kiri-in-y-parent-callback", 1, 1, a =>
            HaraKiriGroupSpanner.ForceHaraKiriInYParentCallback(
                AsGrob(
                    a[0],
                    "ly:hara-kiri-group-spanner::force-hara-kiri-in-y-parent-callback")));

        // ----- pure-from-neighbor-interface.cc -----

        interpreter.DefinePrimitive(
            "ly:pure-from-neighbor-interface::calc-pure-relevant-grobs", 1, 1, a =>
            PureFromNeighborInterface.CalcPureRelevantGrobs(
                AsGrob(a[0], "ly:pure-from-neighbor-interface::calc-pure-relevant-grobs")));

        // ----- axis-group-interface.cc, the pure half -----
        //
        // These are not new names — they were registered stubs — but their bodies had never
        // been carried; see AxisGroupInterfacePure.cs.

        interpreter.DefinePrimitive("ly:axis-group-interface::pure-height", 3, 3, a =>
            {
                Interval height = AxisGroupInterfacePure.PureGroupHeight(
                    AsGrob(a[0], "ly:axis-group-interface::pure-height"),
                    AsRank(a[1], 0),
                    AsRank(a[2], int.MaxValue));
                return new Pair(height.Left, height.Right);
            });

        interpreter.DefinePrimitive("ly:axis-group-interface::calc-pure-y-common", 1, 1, a =>
            AxisGroupInterfacePure.CalcPureYCommon(
                AsGrob(a[0], "ly:axis-group-interface::calc-pure-y-common")));

        interpreter.DefinePrimitive(
            "ly:axis-group-interface::calc-pure-relevant-grobs", 1, 1, a =>
            AxisGroupInterfacePure.CalcPureRelevantGrobs(
                AsGrob(a[0], "ly:axis-group-interface::calc-pure-relevant-grobs")));

        interpreter.DefinePrimitive("ly:axis-group-interface::adjacent-pure-heights", 1, 1, a =>
            AxisGroupInterfacePure.AdjacentPureHeights(
                AsGrob(a[0], "ly:axis-group-interface::adjacent-pure-heights")));

        // ----- system.cc, the pure half -----

        interpreter.DefinePrimitive("ly:system::calc-pure-relevant-grobs", 1, 1, a =>
            SystemGrob.CalcPureRelevantGrobs(
                AsGrob(a[0], "ly:system::calc-pure-relevant-grobs")));

        interpreter.DefinePrimitive("ly:system::calc-pure-height", 3, 3, a =>
            {
                SystemGrob me = AsGrob(a[0], "ly:system::calc-pure-height") as SystemGrob;
                if (me == null)
                {
                    throw SchemeErrors.WrongType("ly:system::calc-pure-height", "system", a[0]);
                }

                Interval begin = me.BeginOfLinePureHeight(AsRank(a[1], 0), AsRank(a[2], 0));
                Interval rest = me.RestOfLinePureHeight(AsRank(a[1], 0), AsRank(a[2], 0));
                begin.Unite(rest);
                return new Pair(begin.Left, begin.Right);
            });
    }

    private static Grob AsGrob(object value, string procedureName)
        => value as Grob ?? throw SchemeErrors.WrongType(procedureName, "grob", value);

    private static Item AsItem(object value, string procedureName)
        => value as Item ?? throw SchemeErrors.WrongType(procedureName, "item", value);

    private static Spanner AsSpanner(object value, string procedureName)
        => value as Spanner ?? throw SchemeErrors.WrongType(procedureName, "spanner", value);

    private static UnpurePureContainer AsUnpurePure(object value, string procedureName)
        => value as UnpurePureContainer
           ?? throw SchemeErrors.WrongType(procedureName, "unpure-pure-container", value);

    private static int AsRank(object value, int fallback)
        => SchemeConvert.IsNumber(value)
            ? (int)SchemeConvert.ToDouble(value, "column rank")
            : fallback;
}
