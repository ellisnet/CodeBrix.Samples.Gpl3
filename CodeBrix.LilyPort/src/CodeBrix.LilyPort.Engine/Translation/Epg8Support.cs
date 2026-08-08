// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// Small conversion and diagnostic helpers shared by the EPG8 translators — the C#
/// spellings of upstream's <c>from_scm&lt;T&gt; (value, fallback)</c> readers, the
/// origin-carrying <c>Stream_event::warning</c>, and the workaround for setting a
/// context property to the empty list.
/// <para>
/// This file is binding glue, not a port of an upstream file: every routine here is a
/// one-line idiom in C++ that C# cannot spell inline.
/// </para>
/// </summary>
internal static class Epg8Support
{
    /// <summary>Reads a boolean the way <c>from_scm&lt;bool&gt;</c> does: only <c>#t</c> is true.</summary>
    /// <param name="value">The Scheme value.</param>
    /// <returns><see langword="true"/> only for <c>#t</c>.</returns>
    internal static bool ToBool(object value) => value is bool flag && flag;

    /// <summary>Reads a long with a fallback, the way <c>from_scm (value, fallback)</c> does.</summary>
    /// <param name="value">The Scheme value.</param>
    /// <param name="fallback">The answer when the value is not a number.</param>
    /// <returns>The number, or the fallback.</returns>
    internal static long ToLong(object value, long fallback)
        => SchemeConvert.IsNumber(value) ? SchemeConvert.ToLong(value, "epg8") : fallback;

    /// <summary>Reads a double with a fallback.</summary>
    /// <param name="value">The Scheme value.</param>
    /// <param name="fallback">The answer when the value is not a number.</param>
    /// <returns>The number, or the fallback.</returns>
    internal static double ToDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value) ? SchemeConvert.ToDouble(value, "epg8") : fallback;

    /// <summary>Reads a rational with a fallback.</summary>
    /// <param name="value">The Scheme value.</param>
    /// <param name="fallback">The answer when the value is not a number.</param>
    /// <returns>The rational, or the fallback.</returns>
    internal static Rational ToRational(object value, Rational fallback)
        => SchemeConvert.IsNumber(value) ? SchemeConvert.ToRational(value, "epg8") : fallback;

    /// <summary>Reads a moment with a fallback.</summary>
    /// <param name="value">The Scheme value.</param>
    /// <param name="fallback">The answer when the value is not a moment.</param>
    /// <returns>The moment, or the fallback.</returns>
    internal static Moment ToMoment(object value, Moment fallback)
        => value is Moment moment ? moment : fallback;

    /// <summary>
    /// Determines whether a value is an EXACT Scheme rational — upstream's
    /// <c>is_scm&lt;Rational&gt;</c>, which a real does not satisfy.
    /// </summary>
    /// <param name="value">The Scheme value.</param>
    /// <returns><see langword="true"/> for exact integers and ratios.</returns>
    internal static bool IsExactRational(object value)
        => value is long || value is int
           || value is System.Numerics.BigInteger
           || value is CodeBrix.LilyScheme.Numeric.Ratio;

    /// <summary>Reports a warning at an event's origin, as <c>Stream_event::warning</c> does.</summary>
    /// <param name="streamEvent">The event carrying the origin.</param>
    /// <param name="message">The warning text.</param>
    internal static void EventWarning(StreamEvent streamEvent, string message)
    {
        if (streamEvent?.Origin is Input origin)
        {
            origin.Warning(message);
        }
        else
        {
            Warn.Warning(message);
        }
    }

    /// <summary>Reports an internal error at an event's origin.</summary>
    /// <param name="streamEvent">The event carrying the origin.</param>
    /// <param name="message">The error text.</param>
    internal static void EventProgrammingError(StreamEvent streamEvent, string message)
    {
        if (streamEvent?.Origin is Input origin)
        {
            origin.ProgrammingError(message);
        }
        else
        {
            Warn.ProgrammingError(message);
        }
    }

    /// <summary>
    /// Builds a <see cref="GrobArray"/> out of a Scheme list of grobs — upstream's
    /// <c>grob_list_to_grob_array</c> from grob-array.cc, carried here because the
    /// ported <c>GrobArray.cs</c> does not have it yet (recorded under FINDINGS).
    /// </summary>
    /// <param name="list">The Scheme list.</param>
    /// <returns>A new array holding every grob in the list, in order.</returns>
    internal static GrobArray GrobListToGrobArray(object list)
    {
        GrobArray array = new GrobArray();
        object cursor = list;
        while (cursor is Pair pair)
        {
            if (pair.Car is Grob grob)
            {
                array.Add(grob);
            }

            cursor = pair.Cdr;
        }

        return array;
    }

    /// <summary>
    /// An <c>assoc</c> using <c>equal?</c> — upstream's <c>ly_assoc</c>, needed where
    /// <c>SchemeUtilities.Assq</c>'s identity compare is not enough.
    /// </summary>
    /// <param name="key">The key to find.</param>
    /// <param name="alist">The association list.</param>
    /// <returns>The matching entry, or <see langword="null"/>.</returns>
    internal static Pair Assoc(object key, object alist)
    {
        object cursor = alist;
        while (cursor is Pair pair)
        {
            if (pair.Car is Pair entry
                && CodeBrix.LilyScheme.Primitives.CorePrimitives.SchemeEqual(entry.Car, key))
            {
                return entry;
            }

            cursor = pair.Cdr;
        }

        return null;
    }
}

/// <summary>
/// Records that an event class was heard — upstream's <c>Boolean_event_listener</c>
/// from <c>lily/include/simple-event-listener.hh</c>, used by the delegate-listener
/// registrations in <c>Bar_engraver</c>.
/// </summary>
internal sealed class BooleanEventListener
{
    private bool _heard;

    /// <summary>Records that an event arrived. The event itself is not kept.</summary>
    /// <param name="streamEvent">The event; only its arrival matters.</param>
    internal void Listen(StreamEvent streamEvent) => _heard = true;

    /// <summary>Gets a value indicating whether anything was heard since the last reset.</summary>
    internal bool Heard => _heard;

    /// <summary>Forgets what was heard.</summary>
    internal void Reset() => _heard = false;

    /// <summary>Marks the listener as having heard, without an event.</summary>
    /// <param name="value">The new state.</param>
    internal void SetHeard(bool value = true) => _heard = value;
}
