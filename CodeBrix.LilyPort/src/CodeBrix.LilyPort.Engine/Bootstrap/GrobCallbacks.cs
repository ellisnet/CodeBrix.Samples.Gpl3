// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The grob property callbacks and font entry points the printing path needs.
/// <para>
/// A grob's definition in <c>scm/define-grobs.scm</c> names its callbacks by SCHEME
/// NAME — <c>ly:staff-symbol::print</c>, <c>ly:note-head::select-glyph</c> — so a
/// ported callback that is not registered here is not reached at all, and the grob
/// silently keeps the stub's answer. That is the same defect class the Track T session
/// found three times over; the fence in <c>GrobCallbackTests</c> exists because of it.
/// </para>
/// </summary>
public static class GrobCallbacks
{
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");

    /// <summary>Installs the callbacks, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallGrobExtents(interpreter);
        InstallStaffSymbol(interpreter);
        InstallClef(interpreter);
        InstallNoteHead(interpreter);
        InstallFonts(interpreter);
        InstallOutputDef(interpreter);
    }

    private static void InstallGrobExtents(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:grob::stencil-height", 1, 1, a =>
            StencilExtent(AsGrob(a[0], "ly:grob::stencil-height"), Axis.Y));

        interpreter.DefinePrimitive("ly:grob::stencil-width", 1, 1, a =>
            StencilExtent(AsGrob(a[0], "ly:grob::stencil-width"), Axis.X));

        interpreter.DefinePrimitive("ly:axis-group-interface::width", 1, 1, a =>
            ToPair(AxisGroupInterface.GenericGroupExtent(
                AsGrob(a[0], "ly:axis-group-interface::width"), Axis.X)));

        interpreter.DefinePrimitive("ly:axis-group-interface::height", 1, 1, a =>
            ToPair(AxisGroupInterface.GenericGroupExtent(
                AsGrob(a[0], "ly:axis-group-interface::height"), Axis.Y)));

        // System::height is Axis_group_interface::height, verbatim, upstream.
        interpreter.DefinePrimitive("ly:system::height", 1, 1, a =>
            ToPair(AxisGroupInterface.GenericGroupExtent(
                AsGrob(a[0], "ly:system::height"), Axis.Y)));

        // Hara_kiri_group_spanner::y_extent is consider_suicide() followed by
        // Axis_group_interface::height, and the force-hara-kiri callback is
        // consider_suicide() followed by a plain zero. consider_suicide is what removes
        // an EMPTY staff, and it is deliberately not ported yet -- so these register the
        // rest, which is what a NON-empty staff gets either way. A staff that should
        // have vanished therefore stays; that is visible, not silent.
        // Recorded in PORT-COVERAGE.
        interpreter.DefinePrimitive("ly:hara-kiri-group-spanner::y-extent", 1, 1, a =>
            ToPair(AxisGroupInterface.GenericGroupExtent(
                AsGrob(a[0], "ly:hara-kiri-group-spanner::y-extent"), Axis.Y)));

        interpreter.DefinePrimitive(
            "ly:hara-kiri-group-spanner::force-hara-kiri-callback", 1, 1, a => 0.0);

        interpreter.DefinePrimitive("ly:grob-layout", 1, 1, a =>
            (object)AsGrob(a[0], "ly:grob-layout").Layout ?? false);

        interpreter.DefinePrimitive("ly:paper-column::break-align-width", 2, 2, a =>
            ToPair(PaperColumn.BreakAlignWidth(
                AsGrob(a[0], "ly:paper-column::break-align-width"), a[1])));
    }

    private static void InstallStaffSymbol(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:staff-symbol::print", 1, 1, a =>
            StaffSymbol.Print(AsGrob(a[0], "ly:staff-symbol::print")));

        interpreter.DefinePrimitive("ly:staff-symbol::calc-line-positions", 1, 1, a =>
            StaffSymbol.CalcLinePositions(AsGrob(a[0], "ly:staff-symbol::calc-line-positions")));

        interpreter.DefinePrimitive("ly:staff-symbol::height", 1, 1, a =>
            ToPair(StaffSymbol.Height(AsGrob(a[0], "ly:staff-symbol::height"))));

        interpreter.DefinePrimitive("ly:staff-symbol-referencer::callback", 1, 1, a =>
            StaffSymbolReferencer.Callback(AsGrob(a[0], "ly:staff-symbol-referencer::callback")));

        interpreter.DefinePrimitive("ly:staff-symbol-staff-space", 1, 1, a =>
            StaffSymbolReferencer.StaffSpace(AsGrob(a[0], "ly:staff-symbol-staff-space")));

        interpreter.DefinePrimitive("ly:staff-symbol-line-thickness", 1, 1, a =>
            StaffSymbolReferencer.LineThickness(AsGrob(a[0], "ly:staff-symbol-line-thickness")));

        interpreter.DefinePrimitive("ly:grob-staff-position", 1, 1, a =>
            StaffSymbolReferencer.GetPosition(AsGrob(a[0], "ly:grob-staff-position")));

        interpreter.DefinePrimitive("ly:position-on-line?", 2, 2, a =>
            StaffSymbolReferencer.OnLine(
                AsGrob(a[0], "ly:position-on-line?"),
                SchemeConvert.ToInt(a[1], "ly:position-on-line?")));
    }

    private static void InstallClef(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:clef::print", 1, 1, a =>
            Clef.Print(AsGrob(a[0], "ly:clef::print")));

        interpreter.DefinePrimitive("ly:clef::calc-glyph-name", 1, 1, a =>
            Clef.CalcGlyphName(AsGrob(a[0], "ly:clef::calc-glyph-name")));
    }

    private static void InstallNoteHead(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:note-head::select-glyph", 1, 1, a =>
            NoteHead.SelectGlyph(AsGrob(a[0], "ly:note-head::select-glyph")));

        interpreter.DefinePrimitive("ly:note-head::stem-x-shift", 1, 1, a =>
            NoteHead.StemXShift(AsGrob(a[0], "ly:note-head::stem-x-shift")));

        interpreter.DefinePrimitive("ly:note-head::include-ledger-line-height", 1, 1, a =>
            ToPair(NoteHead.IncludeLedgerLineHeight(
                AsGrob(a[0], "ly:note-head::include-ledger-line-height"))));

        interpreter.DefinePrimitive("ly:note-head::calc-stem-attachment", 1, 1, a =>
            NoteHead.CalcStemAttachment(AsGrob(a[0], "ly:note-head::calc-stem-attachment")));
    }

    private static void InstallFonts(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:grob-default-font", 1, 1, a =>
            (object)FontInterface.GetDefaultFont(AsGrob(a[0], "ly:grob-default-font")) ?? false);

        interpreter.DefinePrimitive("ly:font-get-glyph", 2, 2, a =>
        {
            if (!(a[0] is FontMetric font))
            {
                throw SchemeErrors.WrongType("ly:font-get-glyph", "font metric", a[0]);
            }

            return font.FindByName(StringPrimitives.Text(a[1], "ly:font-get-glyph"));
        });

        interpreter.DefinePrimitive("ly:font-name", 1, 1, a =>
            a[0] is FontMetric font ? (object)new MutableString(font.FontName) : false);

        interpreter.DefinePrimitive("ly:font-file-name", 1, 1, a =>
            a[0] is OpenTypeFontMetric font
                ? (object)new MutableString(font.Font.FileName)
                : a[0] is ModifiedFontMetric scaled && scaled.OriginalFont is OpenTypeFontMetric original
                    ? new MutableString(original.Font.FileName)
                    : false);

        interpreter.DefinePrimitive("ly:reset-all-fonts", 0, 0, a =>
        {
            AllFontMetrics.ResetAllFonts();
            FontInterface.ResetScaledFonts();
            return Unspecified.Instance;
        });
    }

    private static void InstallOutputDef(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:output-def-lookup", 2, 3, a =>
        {
            if (!(a[0] is OutputDef definition))
            {
                throw SchemeErrors.WrongType("ly:output-def-lookup", "output definition", a[0]);
            }

            Symbol symbol = a[1] as Symbol
                ?? throw SchemeErrors.WrongType("ly:output-def-lookup", "symbol", a[1]);

            object value = definition.LookupVariable(symbol);
            if (value != null)
            {
                return value;
            }

            // Upstream answers the caller's default, or '() when none was supplied.
            return a.Length > 2 && !(a[2] is DefaultArgument) ? a[2] : Nil.Instance;
        });

        interpreter.DefinePrimitive("ly:output-def-clone", 1, 1, a =>
            a[0] is OutputDef definition ? (object)definition.Clone() : false);
    }

    private static object StencilExtent(Grob grob, Axis axis)
    {
        Stencil? stencil = grob.GetStencil();
        Interval extent = stencil.HasValue ? stencil.Value.Extent(axis) : Interval.Empty;
        return ToPair(extent);
    }

    private static object ToPair(Interval interval) => new Pair(interval.Left, interval.Right);

    private static Grob AsGrob(object value, string procedureName)
        => value as Grob ?? throw SchemeErrors.WrongType(procedureName, "grob", value);
}
