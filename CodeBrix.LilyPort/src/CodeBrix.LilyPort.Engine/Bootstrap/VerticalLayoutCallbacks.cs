// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The vertical-organization Scheme surface — alignment, side
/// positioning, self alignment, system-start delimiters — plus the
/// <c>axis-group-interface-scheme.cc</c> bindings and the small pull-forwards the
/// alignment path cannot run without (<c>ly:grob::*-parent-positioning</c>,
/// <c>ly:unpure-call</c>/<c>ly:pure-call</c>, and the vertical-organization halves of
/// <c>system.cc</c> and <c>hara-kiri-group-spanner.cc</c>).
/// <para>
/// The pure-height family of <c>axis-group-interface.cc</c>
/// (<c>adjacent-pure-heights</c>, <c>pure-height</c>,
/// <c>calc-pure-relevant-grobs</c>, <c>calc-pure-y-common</c>,
/// <c>calc-pure-staff-staff-spacing</c>) deliberately KEEPS ITS STUBS: it needs
/// the line-breaking group's pure/broken machinery, and a stub that reports demand is worth more than a
/// stand-in that hides it.
/// </para>
/// </summary>
public static class VerticalLayoutCallbacks
{
    /// <summary>Installs the callbacks, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallAlignInterface(interpreter);
        InstallSelfAlignment(interpreter);
        InstallSidePosition(interpreter);
        InstallSystemStartDelimiter(interpreter);
        InstallAxisGroupBindings(interpreter);
        InstallAxisGroupCallbacks(interpreter);
        InstallPullForwards(interpreter);
    }

    private static void InstallAlignInterface(Interpreter interpreter)
    {
        interpreter.DefinePrimitive(
            "ly:align-interface::align-to-minimum-distances", 1, 1, a =>
            {
                AlignInterface.AlignToMinimumDistances(
                    AsGrob(a[0], "ly:align-interface::align-to-minimum-distances"));
                return true;
            });

        interpreter.DefinePrimitive(
            "ly:align-interface::align-to-ideal-distances", 1, 1, a =>
            {
                AlignInterface.AlignToIdealDistances(
                    AsGrob(a[0], "ly:align-interface::align-to-ideal-distances"));
                return true;
            });
    }

    private static void InstallSelfAlignment(Interpreter interpreter)
    {
        interpreter.DefinePrimitive(
            "ly:self-alignment-interface::x-aligned-on-self", 1, 1, a =>
                SelfAlignmentInterface.XAlignedOnSelf(
                    AsGrob(a[0], "ly:self-alignment-interface::x-aligned-on-self")));

        interpreter.DefinePrimitive(
            "ly:self-alignment-interface::y-aligned-on-self", 1, 1, a =>
                SelfAlignmentInterface.YAlignedOnSelf(
                    AsGrob(a[0], "ly:self-alignment-interface::y-aligned-on-self")));

        interpreter.DefinePrimitive(
            "ly:self-alignment-interface::pure-y-aligned-on-self", 3, 3, a =>
                SelfAlignmentInterface.PureYAlignedOnSelf(
                    AsGrob(a[0], "ly:self-alignment-interface::pure-y-aligned-on-self"),
                    IntOr(a[1], 0),
                    IntOr(a[2], int.MaxValue)));

        interpreter.DefinePrimitive(
            "ly:self-alignment-interface::centered-on-x-parent", 1, 1, a =>
                SelfAlignmentInterface.CenteredOnXParent(
                    AsGrob(a[0], "ly:self-alignment-interface::centered-on-x-parent")));

        interpreter.DefinePrimitive(
            "ly:self-alignment-interface::centered-on-y-parent", 1, 1, a =>
                SelfAlignmentInterface.CenteredOnYParent(
                    AsGrob(a[0], "ly:self-alignment-interface::centered-on-y-parent")));

        interpreter.DefinePrimitive(
            "ly:self-alignment-interface::aligned-on-x-parent", 1, 1, a =>
                SelfAlignmentInterface.AlignedOnParent(
                    AsGrob(a[0], "ly:self-alignment-interface::aligned-on-x-parent"),
                    Axis.X));

        interpreter.DefinePrimitive(
            "ly:self-alignment-interface::aligned-on-y-parent", 1, 1, a =>
                SelfAlignmentInterface.AlignedOnParent(
                    AsGrob(a[0], "ly:self-alignment-interface::aligned-on-y-parent"),
                    Axis.Y));
    }

    private static void InstallSidePosition(Interpreter interpreter)
    {
        interpreter.DefinePrimitive(
            "ly:side-position-interface::x-aligned-side", 1, 2, a =>
                SidePositionInterface.XAlignedSide(
                    AsGrob(a[0], "ly:side-position-interface::x-aligned-side"),
                    OptionalArgument(a, 1)));

        interpreter.DefinePrimitive(
            "ly:side-position-interface::y-aligned-side", 1, 2, a =>
                SidePositionInterface.YAlignedSide(
                    AsGrob(a[0], "ly:side-position-interface::y-aligned-side"),
                    OptionalArgument(a, 1)));

        interpreter.DefinePrimitive(
            "ly:side-position-interface::pure-y-aligned-side", 3, 4, a =>
                SidePositionInterface.PureYAlignedSide(
                    AsGrob(a[0], "ly:side-position-interface::pure-y-aligned-side"),
                    IntOr(a[1], 0),
                    IntOr(a[2], int.MaxValue),
                    OptionalArgument(a, 3)));

        interpreter.DefinePrimitive(
            "ly:side-position-interface::calc-cross-staff", 1, 1, a =>
                SidePositionInterface.CalcCrossStaff(
                    AsGrob(a[0], "ly:side-position-interface::calc-cross-staff")));

        interpreter.DefinePrimitive(
            "ly:side-position-interface::set-axis!", 2, 2, a =>
            {
                Grob grob = AsGrob(a[0], "ly:side-position-interface::set-axis!");
                SidePositionInterface.SetAxis(
                    grob, AsAxis(a[1], "ly:side-position-interface::set-axis!"));
                return Unspecified.Instance;
            });

        interpreter.DefinePrimitive(
            "ly:side-position-interface::move-to-extremal-staff", 1, 1, a =>
                SidePositionInterface.MoveToExtremalStaff(
                    AsGrob(a[0], "ly:side-position-interface::move-to-extremal-staff")));
    }

    private static void InstallSystemStartDelimiter(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:system-start-delimiter::print", 1, 1, a =>
        {
            Grob grob = AsGrob(a[0], "ly:system-start-delimiter::print");
            if (!(grob is Spanner spanner))
            {
                throw SchemeErrors.WrongType(
                    "ly:system-start-delimiter::print", "spanner", a[0]);
            }

            Layout.Stencil? stencil = SystemStartDelimiter.Print(spanner);
            return stencil.HasValue ? (object)stencil.Value : Unspecified.Instance;
        });
    }

    private static void InstallAxisGroupBindings(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:axis-group-interface::add-element", 2, 2, a =>
        {
            Grob group = AsGrob(a[0], "ly:axis-group-interface::add-element");
            Grob element = AsGrob(a[1], "ly:axis-group-interface::add-element");
            AxisGroupInterface.AddElement(group, element);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:generic-bound-extent", 2, 2, a =>
            ToPair(AxisGroupInterfaceVertical.GenericBoundExtent(
                AsGrob(a[0], "ly:generic-bound-extent"),
                AsGrob(a[1], "ly:generic-bound-extent"),
                Axis.X)));

        interpreter.DefinePrimitive("ly:relative-group-extent", 3, 3, a =>
        {
            IReadOnlyList<Grob> elements = AsGrobList(a[0], "ly:relative-group-extent");
            Grob common = AsGrob(a[1], "ly:relative-group-extent");
            Axis axis = AsAxis(a[2], "ly:relative-group-extent");
            return ToPair(AxisGroupInterfaceVertical.RelativeMaybeBoundGroupExtent(
                elements, common, axis, false));
        });
    }

    /// <summary>
    /// The <c>axis-group-interface.cc</c> callbacks whose stubs this group's landing makes
    /// implementable, plus the hara-kiri skyline callback — registered as its
    /// non-suicide half, the same treatment its y-extent already gets in
    /// <c>GrobCallbacks</c>.
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallAxisGroupCallbacks(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:axis-group-interface::calc-x-common", 1, 1, a =>
            (object)AxisGroupInterfaceVertical.CalcCommon(
                AsGrob(a[0], "ly:axis-group-interface::calc-x-common"), Axis.X)
            ?? Nil.Instance);

        interpreter.DefinePrimitive("ly:axis-group-interface::calc-y-common", 1, 1, a =>
            (object)AxisGroupInterfaceVertical.CalcCommon(
                AsGrob(a[0], "ly:axis-group-interface::calc-y-common"), Axis.Y)
            ?? Nil.Instance);

        interpreter.DefinePrimitive("ly:axis-group-interface::combine-skylines", 1, 1, a =>
            AxisGroupInterfaceVertical.CombineSkylines(
                AsGrob(a[0], "ly:axis-group-interface::combine-skylines")).ToScheme());

        interpreter.DefinePrimitive("ly:axis-group-interface::calc-skylines", 1, 1, a =>
            AxisGroupInterfaceVertical.SkylineSpacing(
                AsGrob(a[0], "ly:axis-group-interface::calc-skylines")).ToScheme());

        interpreter.DefinePrimitive(
            "ly:axis-group-interface::calc-staff-staff-spacing", 1, 1, a =>
                AxisGroupInterfaceVertical.CalcMaybePureStaffStaffSpacing(
                    AsGrob(a[0], "ly:axis-group-interface::calc-staff-staff-spacing"),
                    false,
                    0,
                    int.MaxValue));

        // Hara_kiri_group_spanner::calc_skylines is consider_suicide () followed by
        // Axis_group_interface::calc_skylines. consider_suicide — which removes an
        // EMPTY staff — is deliberately unported (the output-pipeline note), so this registers
        // the rest, which is what a non-empty staff gets either way.
        interpreter.DefinePrimitive(
            "ly:hara-kiri-group-spanner::calc-skylines", 1, 1, a =>
                AxisGroupInterfaceVertical.SkylineSpacing(
                    AsGrob(a[0], "ly:hara-kiri-group-spanner::calc-skylines")).ToScheme());
    }

    /// <summary>
    /// The pull-forwards: bindings from files owed by OTHER groups that this group's
    /// alignment path cannot run without. Each is a faithful port of the upstream
    /// function it names; the ledger rows for their files are NOT flipped.
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallPullForwards(Interpreter interpreter)
    {
        // lily/grob.cc — what Align_interface::add_element plants as each element's
        // offset callback.
        interpreter.DefinePrimitive("ly:grob::x-parent-positioning", 1, 1, a =>
            GrobClosure.XParentPositioning(AsGrob(a[0], "ly:grob::x-parent-positioning")));

        interpreter.DefinePrimitive("ly:grob::y-parent-positioning", 1, 1, a =>
            GrobClosure.YParentPositioning(AsGrob(a[0], "ly:grob::y-parent-positioning")));

        // lily/system.cc — the System grob definition names both in its
        // object-callbacks, so they must answer once a VerticalAlignment exists.
        interpreter.DefinePrimitive("ly:system::get-vertical-alignment", 1, 1, a =>
            (object)SystemGrobVertical.GetVerticalAlignment(
                AsGrob(a[0], "ly:system::get-vertical-alignment"))
            ?? Nil.Instance);

        interpreter.DefinePrimitive("ly:system::vertical-skyline-elements", 1, 1, a =>
            SystemGrobVertical.VerticalSkylineElements(
                AsGrob(a[0], "ly:system::vertical-skyline-elements")));

        // lily/unpure-pure-container.cc — scm/output-lib.scm's grob::compose-function
        // and grob::offset-function route every chained offset callback through these
        // two, and Side_position_interface::set-axis! chains one.
        interpreter.DefinePrimitive("ly:unpure-call", 2, -1, a =>
        {
            object data = a[0] is UnpurePureContainer container ? container.Unpure : a[0];
            if (SchemeUtilities.IsProcedure(data))
            {
                object[] args = new object[a.Length - 1];
                Array.Copy(a, 1, args, 0, args.Length);
                return interpreter.Evaluator.Apply(data, args);
            }

            return data;
        });

        interpreter.DefinePrimitive("ly:pure-call", 4, -1, a =>
        {
            object data = a[0];
            if (data is UnpurePureContainer container)
            {
                // Avoid gratuitous creation of an Unpure_pure_call
                if (container.IsPureOmitted)
                {
                    data = container.Unpure;
                }
                else
                {
                    data = container.Pure;
                    if (SchemeUtilities.IsProcedure(data))
                    {
                        // (data grob start end . rest)
                        object[] pureArgs = new object[a.Length - 1];
                        Array.Copy(a, 1, pureArgs, 0, pureArgs.Length);
                        return interpreter.Evaluator.Apply(data, pureArgs);
                    }

                    return data;
                }
            }

            if (SchemeUtilities.IsProcedure(data))
            {
                // (data grob . rest) — start and end are dropped, upstream's
                // scm_apply_1 (data, grob, rest).
                object[] args = new object[a.Length - 3];
                args[0] = a[1];
                Array.Copy(a, 4, args, 1, a.Length - 4);
                return interpreter.Evaluator.Apply(data, args);
            }

            return data;
        });
    }

    private static object OptionalArgument(object[] args, int index)
        => args.Length > index && !(args[index] is DefaultArgument)
            ? args[index]
            : Nil.Instance;

    private static int IntOr(object value, int fallback)
        => SchemeConvert.IsNumber(value)
            ? (int)Math.Min(int.MaxValue, SchemeConvert.ToDouble(value, "column rank"))
            : fallback;

    private static IReadOnlyList<Grob> AsGrobList(object value, string procedureName)
    {
        if (value is GrobArray grobArray)
        {
            return grobArray.Array;
        }

        List<Grob> grobs = new List<Grob>();
        object cursor = value;
        while (cursor is Pair pair)
        {
            if (pair.Car is Grob grob)
            {
                grobs.Add(grob);
            }

            cursor = pair.Cdr;
        }

        if (!(value is Pair) && !(value is Nil))
        {
            throw SchemeErrors.WrongType(procedureName, "list or Grob_array", value);
        }

        return grobs;
    }

    private static Axis AsAxis(object value, string procedureName)
    {
        long axis = value is long l ? l : value is int i ? i : -1;
        if (axis != 0 && axis != 1)
        {
            throw SchemeErrors.WrongType(procedureName, "axis", value);
        }

        return axis == 0 ? Axis.X : Axis.Y;
    }

    private static object ToPair(Interval interval) => new Pair(interval.Left, interval.Right);

    private static Grob AsGrob(object value, string procedureName)
        => value as Grob ?? throw SchemeErrors.WrongType(procedureName, "grob", value);
}
