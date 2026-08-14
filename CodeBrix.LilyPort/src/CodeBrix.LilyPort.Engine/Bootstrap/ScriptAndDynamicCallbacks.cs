// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The script and dynamic entry points: scripts, dynamics, brackets, pedals, fingering, ledger lines
/// and the line spanner.
/// </summary>
/// <remarks>
/// <para>
/// Twenty-seven <c>MAKE_SCHEME_CALLBACK</c> names plus <c>script-column.cc</c>'s one
/// <c>LY_DEFINE</c>, <c>ly:grob-script-priority-less</c>, which the bindings rule
/// (standing rule 3) puts on whoever ports the type rather than on a later sweep.
/// </para>
/// <para>
/// <c>Horizontal_line_spanner</c> registers only THREE of the five names its sibling has:
/// upstream gives it <c>calc-left-bound-info</c>, <c>calc-left-bound-info-and-text</c> and
/// <c>calc-right-bound-info</c> and nothing else, because a horizontal spanner has no
/// <c>print</c> or <c>cross-staff</c> of its own — it uses <c>Line_spanner</c>'s.
/// </para>
/// </remarks>
public static class ScriptAndDynamicCallbacks
{
    /// <summary>Installs the callbacks, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        // ----- script-interface.cc -----

        interpreter.DefinePrimitive("ly:script-interface::calc-positioning-done", 1, 1, a =>
            ScriptInterface.CalcPositioningDone(
                AsGrob(a[0], "ly:script-interface::calc-positioning-done")));

        interpreter.DefinePrimitive("ly:script-interface::calc-cross-staff", 1, 1, a =>
            ScriptInterface.CalcCrossStaff(
                AsGrob(a[0], "ly:script-interface::calc-cross-staff")));

        // ----- script-column.cc -----

        interpreter.DefinePrimitive("ly:script-column::before-line-breaking", 1, 1, a =>
            ScriptColumn.BeforeLineBreaking(
                AsGrob(a[0], "ly:script-column::before-line-breaking")));

        interpreter.DefinePrimitive("ly:script-column::row-before-line-breaking", 1, 1, a =>
            ScriptColumn.RowBeforeLineBreaking(
                AsGrob(a[0], "ly:script-column::row-before-line-breaking")));

        interpreter.DefinePrimitive("ly:grob-script-priority-less", 2, 2, a =>
            ScriptInterface.ScriptPriorityLess(
                AsGrob(a[0], "ly:grob-script-priority-less"),
                AsGrob(a[1], "ly:grob-script-priority-less")));

        // ----- line-spanner.cc -----

        interpreter.DefinePrimitive("ly:line-spanner::print", 1, 1, a =>
            LineSpanner.Print(AsGrob(a[0], "ly:line-spanner::print")));

        interpreter.DefinePrimitive("ly:line-spanner::calc-cross-staff", 1, 1, a =>
            LineSpanner.CalcCrossStaff(AsGrob(a[0], "ly:line-spanner::calc-cross-staff")));

        interpreter.DefinePrimitive("ly:line-spanner::calc-left-bound-info", 1, 1, a =>
            LineSpanner.CalcLeftBoundInfo(
                AsSpanner(a[0], "ly:line-spanner::calc-left-bound-info")));

        interpreter.DefinePrimitive(
            "ly:line-spanner::calc-left-bound-info-and-text", 1, 1, a =>
                LineSpanner.CalcLeftBoundInfoAndText(
                    AsSpanner(a[0], "ly:line-spanner::calc-left-bound-info-and-text")));

        interpreter.DefinePrimitive("ly:line-spanner::calc-right-bound-info", 1, 1, a =>
            LineSpanner.CalcRightBoundInfo(
                AsSpanner(a[0], "ly:line-spanner::calc-right-bound-info")));

        interpreter.DefinePrimitive(
            "ly:horizontal-line-spanner::calc-left-bound-info", 1, 1, a =>
                LineSpanner.HorizontalCalcLeftBoundInfo(
                    AsSpanner(a[0], "ly:horizontal-line-spanner::calc-left-bound-info")));

        interpreter.DefinePrimitive(
            "ly:horizontal-line-spanner::calc-left-bound-info-and-text", 1, 1, a =>
                LineSpanner.HorizontalCalcLeftBoundInfoAndText(
                    AsSpanner(
                        a[0], "ly:horizontal-line-spanner::calc-left-bound-info-and-text")));

        interpreter.DefinePrimitive(
            "ly:horizontal-line-spanner::calc-right-bound-info", 1, 1, a =>
                LineSpanner.HorizontalCalcRightBoundInfo(
                    AsSpanner(a[0], "ly:horizontal-line-spanner::calc-right-bound-info")));

        // ----- ottava-bracket.cc -----

        interpreter.DefinePrimitive("ly:ottava-bracket::print", 1, 1, a =>
            OttavaBracket.Print(AsGrob(a[0], "ly:ottava-bracket::print")));

        // ----- hairpin.cc -----

        interpreter.DefinePrimitive("ly:hairpin::print", 1, 1, a =>
            Hairpin.Print(AsGrob(a[0], "ly:hairpin::print")));

        interpreter.DefinePrimitive("ly:hairpin::broken-bound-padding", 1, 1, a =>
            Hairpin.BrokenBoundPadding(AsGrob(a[0], "ly:hairpin::broken-bound-padding")));

        interpreter.DefinePrimitive("ly:hairpin::pure-height", 3, 3, a =>
            ToPair(Hairpin.PureHeight(
                AsGrob(a[0], "ly:hairpin::pure-height"),
                (int)SchemeConvert.ToLong(a[1], "ly:hairpin::pure-height"),
                (int)SchemeConvert.ToLong(a[2], "ly:hairpin::pure-height"))));

        // ----- piano-pedal-bracket.cc, sustain-pedal.cc -----

        interpreter.DefinePrimitive("ly:piano-pedal-bracket::print", 1, 1, a =>
            PianoPedalBracket.Print(AsGrob(a[0], "ly:piano-pedal-bracket::print")));

        interpreter.DefinePrimitive("ly:sustain-pedal::print", 1, 1, a =>
            SustainPedal.Print(AsGrob(a[0], "ly:sustain-pedal::print")));

        // ----- fingering-column.cc -----

        interpreter.DefinePrimitive("ly:fingering-column::calc-positioning-done", 1, 1, a =>
            FingeringColumn.CalcPositioningDone(
                AsGrob(a[0], "ly:fingering-column::calc-positioning-done")));

        // ----- horizontal-bracket.cc, enclosing-bracket.cc -----

        interpreter.DefinePrimitive("ly:horizontal-bracket::print", 1, 1, a =>
            HorizontalBracket.Print(AsGrob(a[0], "ly:horizontal-bracket::print")));

        interpreter.DefinePrimitive("ly:enclosing-bracket::print", 1, 1, a =>
            EnclosingBracket.Print(AsGrob(a[0], "ly:enclosing-bracket::print")));

        interpreter.DefinePrimitive("ly:enclosing-bracket::width", 1, 1, a =>
            EnclosingBracket.Width(AsGrob(a[0], "ly:enclosing-bracket::width")));

        // ----- balloon.cc -----

        interpreter.DefinePrimitive("ly:balloon-interface::print", 1, 1, a =>
            BalloonInterface.Print(AsGrob(a[0], "ly:balloon-interface::print")));

        interpreter.DefinePrimitive("ly:balloon-interface::width", 1, 1, a =>
            BalloonInterface.Width(AsGrob(a[0], "ly:balloon-interface::width")));

        interpreter.DefinePrimitive("ly:balloon-interface::pure-height", 3, 3, a =>
            BalloonInterface.PureHeight(
                AsGrob(a[0], "ly:balloon-interface::pure-height"),
                (int)SchemeConvert.ToLong(a[1], "ly:balloon-interface::pure-height"),
                (int)SchemeConvert.ToLong(a[2], "ly:balloon-interface::pure-height")));

        // ----- ledger-line-spanner.cc -----

        interpreter.DefinePrimitive("ly:ledger-line-spanner::print", 1, 1, a =>
            LedgerLineSpanner.Print(AsGrob(a[0], "ly:ledger-line-spanner::print")));

        interpreter.DefinePrimitive("ly:ledger-line-spanner::set-spacing-rods", 1, 1, a =>
            LedgerLineSpanner.SetSpacingRods(
                AsGrob(a[0], "ly:ledger-line-spanner::set-spacing-rods")));

        // ----- line-interface-scheme.cc: PULLED FORWARD from the long-tail pool -----
        //
        // Forced by the demand loop, not chosen. `line-interface.cc` itself has been
        // ported early and the volta/tuplet group closed its last divergence, but its ONE Scheme
        // binding sat in the leaf-bindings group — and scm/output-lib.scm's
        // `flared-hairpin`, which `\override Hairpin.stencil` names, calls it directly.
        // With this group's type-check fix letting that override reach the grob at last, the
        // stub became the thing that killed the page.
        interpreter.DefinePrimitive("ly:line-interface::line", 5, 5, a =>
            Layout.LineInterface.Line(
                AsGrob(a[0], "ly:line-interface::line"),
                new Offset(
                    SchemeConvert.ToDouble(a[1], "ly:line-interface::line"),
                    SchemeConvert.ToDouble(a[2], "ly:line-interface::line")),
                new Offset(
                    SchemeConvert.ToDouble(a[3], "ly:line-interface::line"),
                    SchemeConvert.ToDouble(a[4], "ly:line-interface::line"))));

        InstallDemandedLeafBindings(interpreter);
    }

    // skyline-scheme.cc and stencil-scheme.cc, PULLED FORWARD from the long-tail pool —
    // forced, not chosen. This group's own first sweep regressed 46 files, caused by making
    // PROGRESS: with ly:line-spanner::print ported, every trill spanner, text spanner and
    // glissando produces a real stencil for the first time, so the skyline machinery runs
    // on it — and `ly:skylines-for-stencil`, which scm/define-grobs.scm names on those
    // grobs, was still a stub. Thirty of the forty-six died on it or on one of the five
    // stencil leaves beside it. Every underlying type is already ported (skyline.cc,
    // stencil.cc, lookup.cc); these are thin wrappers and nothing else.
    private static void InstallDemandedLeafBindings(Interpreter interpreter)
    {
        // .ToScheme(): upstream's to_scm (Skyline_pair) makes a CONS of two skylines, and
        // the property's own ly:skyline-pair? predicate tests for exactly that. Handing
        // back the C# object instead is the mistake the spacing group recorded and it fails the same
        // way — `car' applied to something that is not a pair.
        interpreter.DefinePrimitive("ly:skylines-for-stencil", 2, 2, a =>
            Layout.StencilIntegral.SkylinesFromStencil(
                AsStencilOrNull(a[0]),
                Nil.Instance,
                AsAxis(a[1], "ly:skylines-for-stencil")).ToScheme());

        // The rest of skyline-scheme.cc comes with it. Taking the file WHOLE rather than
        // one binding at a time is deliberate: fixing ly:skylines-for-stencil alone moved
        // the failure straight on to ly:skyline-max-height, which the same scm/ code path
        // calls one line later. Every one is a thin wrapper over a Skyline method that
        // Layout/Skyline.cs already has.
        interpreter.DefinePrimitive("ly:skyline-touching-point", 2, 3, a =>
            AsSkyline(a[0], "ly:skyline-touching-point").TouchingPoint(
                AsSkyline(a[1], "ly:skyline-touching-point"),
                a.Length > 2 ? SchemeConvert.ToDouble(a[2], "ly:skyline-touching-point") : 0.0));

        interpreter.DefinePrimitive("ly:skyline-distance", 2, 3, a =>
            AsSkyline(a[0], "ly:skyline-distance").Distance(
                AsSkyline(a[1], "ly:skyline-distance"),
                a.Length > 2 ? SchemeConvert.ToDouble(a[2], "ly:skyline-distance") : 0.0));

        interpreter.DefinePrimitive("ly:skyline-max-height", 1, 1, a =>
            AsSkyline(a[0], "ly:skyline-max-height").MaxHeight());

        interpreter.DefinePrimitive("ly:skyline-max-height-position", 1, 1, a =>
            AsSkyline(a[0], "ly:skyline-max-height-position").MaxHeightPosition());

        interpreter.DefinePrimitive("ly:skyline-height", 2, 2, a =>
            AsSkyline(a[0], "ly:skyline-height")
                .Height(SchemeConvert.ToDouble(a[1], "ly:skyline-height")));

        interpreter.DefinePrimitive("ly:skyline-empty?", 1, 1, a =>
            AsSkyline(a[0], "ly:skyline-empty?").IsEmpty);

        interpreter.DefinePrimitive("ly:skyline-pad", 2, 2, a =>
            AsSkyline(a[0], "ly:skyline-pad")
                .Padded(SchemeConvert.ToDouble(a[1], "ly:skyline-pad")));

        interpreter.DefinePrimitive("ly:skyline->points", 2, 2, a =>
        {
            List<Offset> points = AsSkyline(a[0], "ly:skyline->points")
                .ToPoints(AsAxis(a[1], "ly:skyline->points"));
            object result = Nil.Instance;
            for (int i = points.Count - 1; i >= 0; i--)
            {
                result = new Pair(new Pair(points[i].X, points[i].Y), result);
            }

            return result;
        });

        interpreter.DefinePrimitive("ly:skyline-merge", 2, 2, a =>
        {
            Skyline first = AsSkyline(a[0], "ly:skyline-merge");
            Skyline second = AsSkyline(a[1], "ly:skyline-merge");
            if (first.Sky != second.Sky)
            {
                throw SchemeErrors.MiscError(
                    "ly:skyline-merge", "expecting skylines with the same direction");
            }

            Skyline merged = first.Copy();
            merged.Merge(second);
            return merged;
        });

        interpreter.DefinePrimitive("ly:make-skyline", 3, 3, a =>
        {
            List<DrulArray<Offset>> offs = new List<DrulArray<Offset>>();
            for (object cursor = a[0]; cursor is Pair outer; cursor = outer.Cdr)
            {
                List<object> segment = Pair.ToList(outer.Car);
                if (segment.Count != 4)
                {
                    throw SchemeErrors.WrongType(
                        "ly:make-skyline", "list of 4 numbers", outer.Car);
                }

                double[] v = new double[4];
                for (int i = 0; i < 4; i++)
                {
                    if (!SchemeConvert.IsNumber(segment[i]))
                    {
                        throw SchemeErrors.WrongType(
                            "ly:make-skyline", "real number", segment[i]);
                    }

                    v[i] = SchemeConvert.ToDouble(segment[i], "ly:make-skyline");
                }

                if ((double.IsInfinity(v[0]) || double.IsInfinity(v[2])) && v[1] != v[3])
                {
                    throw SchemeErrors.MiscError(
                        "ly:make-skyline",
                        "building with infinite bound must be horizontal");
                }

                offs.Add(new DrulArray<Offset>(
                    new Offset(v[0], v[1]), new Offset(v[2], v[3])));
            }

            return new Skyline(
                offs,
                AsAxis(a[1], "ly:make-skyline"),
                new Direction(SchemeConvert.ToLong(a[2], "ly:make-skyline")));
        });

        // note-head-scheme.cc, PULLED FORWARD from the long-tail pool for the same reason: note-head.cc
        // is ported and Note_head::get_stem_attachment is there, but its binding was not.
        // \markup \note reaches it while building its stencil, which is only visible now
        // that the batch runner keeps the parser current across engraving.
        interpreter.DefinePrimitive("ly:note-head::stem-attachment", 2, 3, a =>
        {
            if (!(a[0] is Fonts.FontMetric font))
            {
                throw SchemeErrors.WrongType(
                    "ly:note-head::stem-attachment", "font metric", a[0]);
            }

            // MAKE_SCHEME_CALLBACK (…, 2, 1, 0): direction DEFAULTS TO UP when absent.
            Direction direction = a.Length > 2
                ? new Direction(SchemeConvert.ToLong(a[2], "ly:note-head::stem-attachment"))
                : Direction.Positive;

            Offset attachment = NoteHead.GetStemAttachment(
                font,
                CodeBrix.LilyScheme.Primitives.StringPrimitives.Text(
                    a[1], "ly:note-head::stem-attachment"),
                direction);

            return new Pair(attachment.X, attachment.Y);
        });

        interpreter.DefinePrimitive("ly:item-get-column", 1, 1, a =>
        {
            Grob item = AsGrob(a[0], "ly:item-get-column");
            if (!(item is Item it))
            {
                throw SchemeErrors.WrongType("ly:item-get-column", "item", a[0]);
            }

            return (object)it.GetColumn() ?? Nil.Instance;
        });

        interpreter.DefinePrimitive("ly:stencil-outline", 2, 2, a =>
            AsStencil(a[0], "ly:stencil-outline")
                .WithOutline(AsStencil(a[1], "ly:stencil-outline")));

        // stencil-scheme.cc. Rotate around an offset given RELATIVE to the stencil's own
        // extent — (-1, 1) is its upper-left corner — where ly:stencil-rotate-absolute
        // below takes a point in absolute coordinates. The two differ only in that
        // conversion; both mutate a COPY, which is safe here because Stencil is a struct
        // and AsStencil unboxes one (upstream copies explicitly, for the same reason).
        //
        // Live surface, not a leaf: scm/define-woodwind-diagrams.scm builds every
        // woodwind key diagram through this, so as a stub it returned the inert
        // placeholder for each one.
        interpreter.DefinePrimitive("ly:stencil-rotate", 4, 4, a =>
        {
            Stencil s = AsStencil(a[0], "ly:stencil-rotate");
            s.RotateDegrees(
                SchemeConvert.ToDouble(a[1], "ly:stencil-rotate"),
                new Offset(
                    SchemeConvert.ToDouble(a[2], "ly:stencil-rotate"),
                    SchemeConvert.ToDouble(a[3], "ly:stencil-rotate")));
            return s;
        });

        interpreter.DefinePrimitive("ly:stencil-rotate-absolute", 4, 4, a =>
        {
            Stencil s = AsStencil(a[0], "ly:stencil-rotate-absolute");
            s.RotateDegreesAbsolute(
                SchemeConvert.ToDouble(a[1], "ly:stencil-rotate-absolute"),
                new Offset(
                    SchemeConvert.ToDouble(a[2], "ly:stencil-rotate-absolute"),
                    SchemeConvert.ToDouble(a[3], "ly:stencil-rotate-absolute")));
            return s;
        });

        // MAKE_SCHEME_CALLBACK (…, 2, 2, 0): two required, two optional. `filled`
        // defaults to TRUE, which is not the C# default for a bool — get it wrong and
        // every rounded polygon comes out hollow.
        interpreter.DefinePrimitive("ly:round-polygon", 2, 4, a =>
        {
            List<Offset> points = new List<Offset>();
            for (object cursor = a[0]; cursor is Pair pair; cursor = pair.Cdr)
            {
                if (pair.Car is Pair point)
                {
                    points.Add(new Offset(
                        SchemeConvert.ToDouble(point.Car, "ly:round-polygon"),
                        SchemeConvert.ToDouble(point.Cdr, "ly:round-polygon")));
                }

                // TODO: Print out warning
            }

            double blot = SchemeConvert.ToDouble(a[1], "ly:round-polygon");
            double ext = a.Length > 2 && SchemeConvert.IsNumber(a[2])
                ? SchemeConvert.ToDouble(a[2], "ly:round-polygon")
                : 0.0;
            bool fill = !(a.Length > 3 && a[3] is bool given) || given;

            return Layout.Lookup.RoundPolygon(points, blot, ext, fill);
        });

        interpreter.DefinePrimitive("ly:bracket", 4, 4, a =>
        {
            if (!Grob.TryNumberPair(a[1], out Interval extent))
            {
                throw SchemeErrors.WrongType("ly:bracket", "number pair", a[1]);
            }

            if (double.IsInfinity(extent.Left) || double.IsInfinity(extent.Right))
            {
                Warn.ProgrammingError("bracket extent may not be infinite");
                return Stencil.Empty;
            }

            double thickness = SchemeConvert.ToDouble(a[2], "ly:bracket");
            return Layout.Lookup.Bracket(
                AsAxis(a[0], "ly:bracket"),
                extent,
                thickness,
                SchemeConvert.ToDouble(a[3], "ly:bracket"),
                0.95 * thickness);
        });

        interpreter.DefinePrimitive("ly:length", 1, 2, a =>
        {
            if (a.Length > 1 && SchemeConvert.IsNumber(a[1]))
            {
                return new Offset(
                    SchemeConvert.ToDouble(a[0], "ly:length"),
                    SchemeConvert.ToDouble(a[1], "ly:length")).Length;
            }

            if (!Grob.TryNumberPair(a[0], out Interval pair))
            {
                throw SchemeErrors.WrongType("ly:length", "number pair", a[0]);
            }

            return new Offset(pair.Left, pair.Right).Length;
        });

        // stencil-scheme.cc's ly:angle, ly:length's twin: the vector's angle in DEGREES,
        // taken either from a number pair or from two coordinates. The one-argument form
        // is the common one; the two-argument form is what makes the optional second
        // parameter meaningful, and reading arity the other way round would silently
        // treat (ly:angle 3 4) as "the angle of the pair 3" and raise a wrong-type.
        interpreter.DefinePrimitive("ly:angle", 1, 2, a =>
        {
            if (a.Length > 1 && SchemeConvert.IsNumber(a[1]))
            {
                return new Offset(
                    SchemeConvert.ToDouble(a[0], "ly:angle"),
                    SchemeConvert.ToDouble(a[1], "ly:angle")).AngleDegrees();
            }

            if (!Grob.TryNumberPair(a[0], out Interval pair))
            {
                throw SchemeErrors.WrongType("ly:angle", "number pair", a[0]);
            }

            return new Offset(pair.Left, pair.Right).AngleDegrees();
        });
    }

    private static Skyline AsSkyline(object value, string procedureName)
        => value as Skyline
           ?? throw SchemeErrors.WrongType(procedureName, "skyline", value);

    private static Stencil AsStencil(object value, string procedureName)
        => value is Stencil stencil
            ? stencil
            : throw SchemeErrors.WrongType(procedureName, "stencil", value);

    private static Stencil? AsStencilOrNull(object value)
        => value is Stencil stencil ? stencil : (Stencil?)null;

    private static Axis AsAxis(object value, string procedureName)
    {
        long axis = SchemeConvert.ToLong(value, procedureName);
        if (axis != 0 && axis != 1)
        {
            throw SchemeErrors.WrongType(procedureName, "axis", value);
        }

        return (Axis)axis;
    }

    private static Grob AsGrob(object value, string procedureName)
        => value as Grob ?? throw SchemeErrors.WrongType(procedureName, "grob", value);

    private static Spanner AsSpanner(object value, string procedureName)
        => value as Spanner ?? throw SchemeErrors.WrongType(procedureName, "spanner", value);

    private static object ToPair(Interval interval) => new Pair(interval.Left, interval.Right);
}
