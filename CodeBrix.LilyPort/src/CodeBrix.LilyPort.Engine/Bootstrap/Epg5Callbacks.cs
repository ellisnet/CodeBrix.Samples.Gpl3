// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The Scheme entry points EPG5's files declare: columns, rests, dots and collisions.
/// <para>
/// Each <c>DefinePrimitive</c> here replaces the pre-registered stub for the same name,
/// so a callback that is ported but NOT registered here is silently never reached —
/// the grob keeps the stub's answer. See <see cref="GrobCallbacks"/> for the defect
/// class this guards against.
/// </para>
/// <para>
/// Four entry points from OTHER groups' files are pulled forward here because EPG5's
/// code paths are the first to need them, the same precedent as EPG4's
/// <c>ly:item-break-dir</c>: <c>ly:grob::x-parent-positioning</c> and
/// <c>ly:grob::y-parent-positioning</c> (lily/grob.cc — Dot_column::add_head and
/// Note_collision_interface::add_column store them as offset callbacks), and
/// <c>ly:unpure-call</c> / <c>ly:pure-call</c> (lily/unpure-pure-container.cc, EPG15 —
/// the vendored <c>grob::compose-function</c> calls them, and Rest_collision's chained
/// Y-offset goes through it).
/// </para>
/// </summary>
public static class Epg5Callbacks
{

    /// <summary>Installs the callbacks, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallNoteColumn(interpreter);
        InstallNoteCollision(interpreter);
        InstallRest(interpreter);
        InstallRestCollision(interpreter);
        InstallDotColumn(interpreter);
        InstallMultiMeasureRest(interpreter);
    }

    private static void InstallNoteColumn(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:note-column::calc-main-extent", 1, 1, a =>
            ToPair(NoteColumn.CalcMainExtent(AsGrob(a[0], "ly:note-column::calc-main-extent"))));

        interpreter.DefinePrimitive("ly:note-column-accidentals", 1, 1, a =>
            (object)NoteColumn.Accidentals(AsGrob(a[0], "ly:note-column-accidentals"))
                ?? Nil.Instance);

        interpreter.DefinePrimitive("ly:note-column-dot-column", 1, 1, a =>
            (object)NoteColumn.GetDotColumn(AsGrob(a[0], "ly:note-column-dot-column"))
                ?? Nil.Instance);
    }

    private static void InstallNoteCollision(Interpreter interpreter)
    {
        interpreter.DefinePrimitive(
            "ly:note-collision-interface::calc-positioning-done", 1, 1, a =>
                NoteCollisionInterface.CalcPositioningDone(
                    AsGrob(a[0], "ly:note-collision-interface::calc-positioning-done")));
    }

    private static void InstallRest(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:rest::calc-cross-staff", 1, 1, a =>
            Rest.CalcCrossStaff(AsGrob(a[0], "ly:rest::calc-cross-staff")));

        interpreter.DefinePrimitive("ly:rest::height", 1, 1, a =>
            ToPair(Rest.Height(AsGrob(a[0], "ly:rest::height"))));

        interpreter.DefinePrimitive("ly:rest::print", 1, 1, a =>
            Rest.Print(AsGrob(a[0], "ly:rest::print")));

        // Three arguments: the grob and the begin and end columns of the pure range,
        // which upstream's own body ignores.
        interpreter.DefinePrimitive("ly:rest::pure-height", 3, 3, a =>
            ToPair(Rest.PureHeight(AsGrob(a[0], "ly:rest::pure-height"))));

        interpreter.DefinePrimitive("ly:rest::width", 1, 1, a =>
            ToPair(Rest.Width(AsGrob(a[0], "ly:rest::width"))));

        interpreter.DefinePrimitive("ly:rest::y-offset-callback", 1, 1, a =>
            Rest.YOffsetCallback(AsGrob(a[0], "ly:rest::y-offset-callback")));
    }

    private static void InstallRestCollision(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:rest-collision::calc-positioning-done", 1, 1, a =>
            RestCollision.CalcPositioningDone(
                AsGrob(a[0], "ly:rest-collision::calc-positioning-done")));

        // The second argument is optional upstream (MAKE_SCHEME_CALLBACK_WITH_OPTARGS
        // 2, 1): the offset the chained-over callback computed.
        interpreter.DefinePrimitive(
            "ly:rest-collision::force-shift-callback-rest", 1, 2, a =>
                RestCollision.ForceShiftCallbackRest(
                    AsGrob(a[0], "ly:rest-collision::force-shift-callback-rest"),
                    a.Length > 1 ? a[1] : Nil.Instance));
    }

    private static void InstallDotColumn(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:dot-column::calc-positioning-done", 1, 1, a =>
            DotColumn.CalcPositioningDone(
                AsGrob(a[0], "ly:dot-column::calc-positioning-done")));
    }

    private static void InstallMultiMeasureRest(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:multi-measure-rest::height", 1, 1, a =>
            ToPair(MultiMeasureRest.Height(
                AsSpanner(a[0], "ly:multi-measure-rest::height"))));

        interpreter.DefinePrimitive("ly:multi-measure-rest::print", 1, 1, a =>
            MultiMeasureRest.Print(AsSpanner(a[0], "ly:multi-measure-rest::print")));

        interpreter.DefinePrimitive("ly:multi-measure-rest::set-spacing-rods", 1, 1, a =>
        {
            MultiMeasureRest.SetSpacingRods(
                AsSpanner(a[0], "ly:multi-measure-rest::set-spacing-rods"));
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:multi-measure-rest::set-text-rods", 1, 1, a =>
        {
            MultiMeasureRest.SetTextRods(
                AsSpanner(a[0], "ly:multi-measure-rest::set-text-rods"));
            return Unspecified.Instance;
        });
    }

    private static object ToPair(Interval interval) => new Pair(interval.Left, interval.Right);

    private static Grob AsGrob(object value, string procedureName)
        => value as Grob ?? throw SchemeErrors.WrongType(procedureName, "grob", value);

    private static Spanner AsSpanner(object value, string procedureName)
        => value as Spanner ?? throw SchemeErrors.WrongType(procedureName, "spanner", value);
}
