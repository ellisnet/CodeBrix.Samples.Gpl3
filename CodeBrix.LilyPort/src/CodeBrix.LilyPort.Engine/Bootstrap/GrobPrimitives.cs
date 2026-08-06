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
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The grob Scheme API: how LilyPond's own Scheme reads and writes graphical objects.
/// <para>
/// This is the single busiest interop surface in the engine. Almost every callback in
/// <c>scm/output-lib.scm</c> and <c>scm/define-grobs.scm</c> is a Scheme procedure
/// whose first act is <c>(ly:grob-property grob 'something)</c>, so an unported
/// <c>ly:grob-property</c> does not fail loudly — it hands back the inert placeholder
/// and the failure surfaces somewhere else entirely, as a wrong-type argument to
/// whatever was going to use the value.
/// </para>
/// <para>
/// The three-argument forms take a DEFAULT, and it is returned when the property is
/// unset — <c>'()</c> is a legitimate value, so "unset" and "set to nothing" are
/// different answers and the default only applies to the first.
/// </para>
/// </summary>
public static class GrobPrimitives
{
    /// <summary>Installs the grob primitives, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallProperties(interpreter);
        InstallObjects(interpreter);
        InstallGeometry(interpreter);
        InstallGrobArrays(interpreter);
    }

    private static void InstallProperties(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:grob-property", 2, 3, a =>
        {
            Grob grob = AsGrob(a[0], "ly:grob-property");
            Symbol symbol = AsSymbol(a[1], "ly:grob-property");

            object value = grob.GetProperty(symbol);
            return value is Nil && HasDefault(a, 2) ? a[2] : value;
        });

        interpreter.DefinePrimitive("ly:grob-property-data", 2, 2, a =>
            AsGrob(a[0], "ly:grob-property-data")
                .GetPropertyData(AsSymbol(a[1], "ly:grob-property-data")));

        interpreter.DefinePrimitive("ly:grob-set-property!", 3, 3, a =>
        {
            AsGrob(a[0], "ly:grob-set-property!")
                .SetProperty(AsSymbol(a[1], "ly:grob-set-property!"), a[2]);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:grob-set-nested-property!", 3, 3, a =>
        {
            //was previously: lily/nested-property.cc (set_nested_property);
            Grob grob = AsGrob(a[0], "ly:grob-set-nested-property!");
            if (!(a[1] is Pair path) || !(path.Car is Symbol head))
            {
                throw SchemeErrors.WrongType(
                    "ly:grob-set-nested-property!", "non-empty symbol list", a[1]);
            }

            object alist = grob.GetProperty(head);
            alist = NestedProperty.NestedPropertyAlist(alist, path.Cdr, a[2]);
            grob.SetProperty(head, alist);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:grob-basic-properties", 1, 1, a =>
            AsGrob(a[0], "ly:grob-basic-properties").ImmutablePropertyAlist);

        interpreter.DefinePrimitive("ly:grob-alist-chain", 1, 2, a =>
        {
            Grob grob = AsGrob(a[0], "ly:grob-alist-chain");
            object defaults = HasDefault(a, 1) ? a[1] : Nil.Instance;
            return grob.GetPropertyAlistChain(defaults);
        });

        interpreter.DefinePrimitive("ly:grob-interfaces", 1, 1, a =>
            AsGrob(a[0], "ly:grob-interfaces").Interfaces);

        interpreter.DefinePrimitive("ly:grob-original", 1, 1, a =>
        {
            Grob grob = AsGrob(a[0], "ly:grob-original");
            return (object)grob.Original ?? grob;
        });

        interpreter.DefinePrimitive("ly:grob-suicide!", 1, 1, a =>
        {
            AsGrob(a[0], "ly:grob-suicide!").Suicide();
            return Unspecified.Instance;
        });
    }

    private static void InstallObjects(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:grob-object", 2, 3, a =>
        {
            Grob grob = AsGrob(a[0], "ly:grob-object");
            object value = grob.GetObject(AsSymbol(a[1], "ly:grob-object"));
            return value is Nil && HasDefault(a, 2) ? a[2] : value;
        });

        interpreter.DefinePrimitive("ly:grob-set-object!", 3, 3, a =>
        {
            AsGrob(a[0], "ly:grob-set-object!")
                .SetObject(AsSymbol(a[1], "ly:grob-set-object!"), a[2]);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:grob-parent", 2, 3, a =>
        {
            Grob parent = AsGrob(a[0], "ly:grob-parent").GetParent(AsAxis(a[1], "ly:grob-parent"));
            return (object)parent ?? (HasDefault(a, 2) ? a[2] : false);
        });

        interpreter.DefinePrimitive("ly:grob-set-parent!", 3, 3, a =>
        {
            AsGrob(a[0], "ly:grob-set-parent!").SetParent(
                AsGrob(a[2], "ly:grob-set-parent!"),
                AsAxis(a[1], "ly:grob-set-parent!"));
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:grob-system", 1, 1, a =>
        {
            for (Grob grob = AsGrob(a[0], "ly:grob-system"); grob != null; grob = grob.GetParent(Axis.Y))
            {
                if (grob is SystemGrob system)
                {
                    return system;
                }
            }

            return false;
        });

        // item-scheme.cc, PULLED FORWARD out of EPG23 by EPG4's demand loop: the
        // moment a StaffSpacing grob exists, Staff_spacing::get_spacing reads the
        // space-alist off a break-aligned grob, and several of those entries are
        // break-alignment-list callbacks -- which ask an item which side of a break it
        // is on. Recorded in PORT-COVERAGE; the ledger row moved with it.
        interpreter.DefinePrimitive("ly:item-break-dir", 1, 1, a =>
            AsGrob(a[0], "ly:item-break-dir") is Item item
                ? (object)(long)item.BreakStatusDirection().Value
                : false);
    }

    private static void InstallGeometry(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:grob-extent", 3, 3, a =>
        {
            Interval extent = AsGrob(a[0], "ly:grob-extent").Extent(
                AsGrob(a[1], "ly:grob-extent"),
                AsAxis(a[2], "ly:grob-extent"));
            return new Pair(extent.Left, extent.Right);
        });

        interpreter.DefinePrimitive("ly:grob-robust-relative-extent", 3, 3, a =>
        {
            Interval extent = AsGrob(a[0], "ly:grob-robust-relative-extent").Extent(
                AsGrob(a[1], "ly:grob-robust-relative-extent"),
                AsAxis(a[2], "ly:grob-robust-relative-extent"));

            // "Robust" is the whole point of the name: an empty extent becomes the
            // degenerate (0 . 0) rather than being handed on as (+inf . -inf), which
            // would poison every arithmetic operation downstream.
            if (extent.IsEmpty)
            {
                extent = new Interval(0, 0);
            }

            return new Pair(extent.Left, extent.Right);
        });

        interpreter.DefinePrimitive("ly:grob-relative-coordinate", 3, 3, a =>
            AsGrob(a[0], "ly:grob-relative-coordinate").RelativeCoordinate(
                AsGrob(a[1], "ly:grob-relative-coordinate"),
                AsAxis(a[2], "ly:grob-relative-coordinate")));

        interpreter.DefinePrimitive("ly:grob-translate-axis!", 3, 3, a =>
        {
            AsGrob(a[0], "ly:grob-translate-axis!").TranslateAxis(
                SchemeConvert.ToDouble(a[1], "ly:grob-translate-axis!"),
                AsAxis(a[2], "ly:grob-translate-axis!"));
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:grob-common-refpoint", 3, 3, a =>
        {
            Grob common = AsGrob(a[0], "ly:grob-common-refpoint").CommonRefpoint(
                AsGrob(a[1], "ly:grob-common-refpoint"),
                AsAxis(a[2], "ly:grob-common-refpoint"));
            return (object)common ?? false;
        });

        interpreter.DefinePrimitive("ly:grob-common-refpoint-of-array", 3, 3, a =>
        {
            Grob start = AsGrob(a[0], "ly:grob-common-refpoint-of-array");
            Axis axis = AsAxis(a[2], "ly:grob-common-refpoint-of-array");

            List<Grob> elements = new List<Grob>();
            if (a[1] is GrobArray array)
            {
                foreach (Grob grob in array)
                {
                    elements.Add(grob);
                }
            }

            Grob common = AxisGroupInterface.CommonRefpointOfArray(elements, start, axis);
            return (object)common ?? false;
        });
    }

    private static void InstallGrobArrays(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:grob-array-length", 1, 1, a =>
            (long)AsGrobArray(a[0], "ly:grob-array-length").Count);

        interpreter.DefinePrimitive("ly:grob-array-ref", 2, 2, a =>
            AsGrobArray(a[0], "ly:grob-array-ref")[
                SchemeConvert.ToInt(a[1], "ly:grob-array-ref")]);

        interpreter.DefinePrimitive("ly:grob-array->list", 1, 1, a =>
        {
            List<object> grobs = new List<object>();
            foreach (Grob grob in AsGrobArray(a[0], "ly:grob-array->list"))
            {
                grobs.Add(grob);
            }

            return Pair.ListFrom(grobs);
        });

        interpreter.DefinePrimitive("ly:grob-list->grob-array", 1, 1, a =>
        {
            GrobArray array = new GrobArray();
            object cursor = a[0];
            while (cursor is Pair pair)
            {
                if (pair.Car is Grob grob)
                {
                    array.Add(grob);
                }

                cursor = pair.Cdr;
            }

            return array;
        });

        interpreter.DefinePrimitive("ly:grob-array-filter", 2, 2, a =>
        {
            GrobArray source = AsGrobArray(a[0], "ly:grob-array-filter");
            GrobArray result = new GrobArray { IsOrdered = source.IsOrdered };

            foreach (Grob grob in source)
            {
                if (SchemeUtilities.IsSchemeTrue(SchemeUtilities.CallCallback(a[1], grob)))
                {
                    result.Add(grob);
                }
            }

            return result;
        });
    }

    private static bool HasDefault(object[] arguments, int index)
        => arguments.Length > index && !(arguments[index] is DefaultArgument);

    private static Axis AsAxis(object value, string procedureName)
        => SchemeConvert.ToInt(value, procedureName) == 0 ? Axis.X : Axis.Y;

    private static Symbol AsSymbol(object value, string procedureName)
        => value as Symbol ?? throw SchemeErrors.WrongType(procedureName, "symbol", value);

    private static Grob AsGrob(object value, string procedureName)
        => value as Grob ?? throw SchemeErrors.WrongType(procedureName, "grob", value);

    private static GrobArray AsGrobArray(object value, string procedureName)
        => value as GrobArray ?? throw SchemeErrors.WrongType(procedureName, "grob array", value);
}
