/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

  LilyPond is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  LilyPond is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Music; //was previously: lily/music.cc, lily/include/music.hh, lily/music-sequence.cc;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/*
  Music is anything that has (possibly zero) duration and supports
  both time compression and transposition.

  In Lily, everything that can be thought to have a length and a pitch
  (which has a duration which can be transposed) is considered "music".
*/

/// <summary>
/// A node in the music tree: a note, a rest, a sequence, a simultaneous block, an
/// articulation — anything with a length that can be transposed.
/// <para>
/// Named <c>MusicObject</c> rather than <c>Music</c> because the containing namespace
/// is already <c>Music</c>; a type of the same name as its namespace is legal C# but
/// forces every reference to be fully qualified. The divergence is recorded in
/// PORT-COVERAGE.
/// </para>
/// </summary>
public class MusicObject : Prob
{
    private static readonly Symbol MusicSymbol = Symbol.Intern("Music");
    private static readonly Symbol MusicTypeSymbol = Symbol.Intern("music-type?");
    private static readonly Symbol TypesSymbol = Symbol.Intern("types");
    private static readonly Symbol LengthSymbol = Symbol.Intern("length");
    private static readonly Symbol LengthCallbackSymbol = Symbol.Intern("length-callback");
    private static readonly Symbol StartCallbackSymbol = Symbol.Intern("start-callback");
    private static readonly Symbol DurationSymbol = Symbol.Intern("duration");
    private static readonly Symbol ElementSymbol = Symbol.Intern("element");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol ArticulationsSymbol = Symbol.Intern("articulations");
    private static readonly Symbol PitchSymbol = Symbol.Intern("pitch");
    private static readonly Symbol AbsoluteOctaveSymbol = Symbol.Intern("absolute-octave");
    private static readonly Symbol ToRelativeCallbackSymbol = Symbol.Intern("to-relative-callback");
    private static readonly Symbol OriginSymbol = Symbol.Intern("origin");
    private static readonly Symbol NameSymbol = Symbol.Intern("name");
    private static readonly Symbol MusicCauseSymbol = Symbol.Intern("music-cause");
    private static readonly Symbol MakeEventClassSymbol = Symbol.Intern("ly:make-event-class");

    /// <summary>Initializes a music object from its immutable property alist.</summary>
    /// <param name="immutableInit">
    /// The type's shared property alist, as built by <c>scm/define-music-types.scm</c>.
    /// </param>
    public MusicObject(object immutableInit)
        : base(MusicSymbol, immutableInit)
    {
        LengthCallback = GetProperty(LengthCallbackSymbol);
        if (!(LengthCallback is Procedure))
        {
            LengthCallback = null;
        }

        StartCallback = GetProperty(StartCallbackSymbol);
        if (!(StartCallback is Procedure))
        {
            StartCallback = null;
        }
    }

    /// <summary>Initializes a copy of another music object.</summary>
    /// <param name="source">The music to copy.</param>
    protected MusicObject(MusicObject source)
        : base(source)
    {
        LengthCallback = source.LengthCallback;
        StartCallback = source.StartCallback;
    }

    /// <summary>Gets the C++ class name this object corresponds to.</summary>
    public override string ClassName => "Music";

    /// <summary>
    /// Gets or sets the procedure that computes this music's length, or
    /// <see langword="null"/> to fall back on the duration.
    /// </summary>
    public object LengthCallback { get; set; }

    /// <summary>Gets or sets the procedure that computes this music's start moment.</summary>
    public object StartCallback { get; set; }

    /// <summary>Returns an independent copy of this music object.</summary>
    /// <returns>The clone.</returns>
    public virtual MusicObject Clone() => new MusicObject(this);

    /// <summary>Determines whether this music carries a given type tag.</summary>
    /// <param name="type">The type symbol to look for in the <c>types</c> property.</param>
    /// <returns><see langword="true"/> when the tag is present.</returns>
    public bool IsMusicType(Symbol type)
    {
        object types = GetProperty(TypesSymbol);
        object cursor = types;
        while (cursor is Pair pair)
        {
            if (ReferenceEquals(pair.Car, type))
            {
                return true;
            }

            cursor = pair.Cdr;
        }

        return false;
    }

    /// <summary>Determines whether this music carries a given type tag.</summary>
    /// <param name="type">The type name.</param>
    /// <returns><see langword="true"/> when the tag is present.</returns>
    public bool IsMusicType(string type) => IsMusicType(Symbol.Intern(type));

    /// <summary>
    /// Returns how long this music lasts: its <c>length</c> property when set, then
    /// its length callback, and zero if neither answers.
    /// </summary>
    /// <returns>The length.</returns>
    public Moment GetLength()
    {
        object stored = GetProperty(LengthSymbol);
        if (stored is Moment moment)
        {
            return moment;
        }

        if (LengthCallback is Procedure)
        {
            object result = CallCallback(LengthCallback, this);
            if (result is Moment callbackMoment)
            {
                return callbackMoment;
            }
        }
        else if (LengthCallback == null)
        {
            // Upstream installs ly:music::duration-length-callback as the default when
            // the type does not name one of its own.
            return DurationLengthCallback(this);
        }

        return new Moment(0);
    }

    /// <summary>Returns when this music starts relative to its own reference point.</summary>
    /// <returns>The start moment, which is non-zero only for grace music.</returns>
    public Moment StartMoment()
    {
        if (StartCallback is Procedure)
        {
            object result = CallCallback(StartCallback, this);
            if (result is Moment moment)
            {
                return moment;
            }
        }

        return new Moment(0);
    }

    /// <summary>
    /// Resolves relative octaves in this music against the previous pitch, returning
    /// the pitch that follows it.
    /// </summary>
    /// <param name="last">The pitch the previous music left off at.</param>
    /// <returns>The pitch this music leaves off at.</returns>
    public Pitch ToRelativeOctave(Pitch last)
    {
        object callback = GetProperty(ToRelativeCallbackSymbol);
        if (callback is Procedure)
        {
            object result = CallCallback(callback, this, last);
            if (result is Pitch pitch)
            {
                return pitch;
            }
        }

        return GenericToRelativeOctave(last);
    }

    /// <summary>
    /// The default relative-octave resolution: fix this music's own pitch, then its
    /// element, then its articulations and elements in turn.
    /// </summary>
    /// <param name="last">The pitch the previous music left off at.</param>
    /// <returns>The pitch this music leaves off at.</returns>
    public Pitch GenericToRelativeOctave(Pitch last)
    {
        object element = GetProperty(ElementSymbol);
        if (GetProperty(PitchSymbol) is Pitch oldPitch)
        {
            Pitch newPitch = oldPitch.ToRelativeOctave(last);

            object check = GetProperty(AbsoluteOctaveSymbol);
            if (IsNumber(check) && newPitch.Octave != SchemeConvert.ToInt(check, "absolute-octave"))
            {
                Pitch expected = new Pitch(
                    SchemeConvert.ToInt(check, "absolute-octave"),
                    newPitch.NoteName,
                    newPitch.Alteration);
                Warn.Warning(
                    "octave check failed; expected \""
                    + expected
                    + "\", found: \""
                    + newPitch
                    + "\"");
                newPitch = expected;
            }

            SetProperty(PitchSymbol, newPitch);
            last = newPitch;
        }

        if (element is MusicObject inner)
        {
            last = inner.ToRelativeOctave(last);
        }

        MusicListToRelative(GetProperty(ArticulationsSymbol), last, true);
        last = MusicListToRelative(GetProperty(ElementsSymbol), last, false);
        return last;
    }

    /// <summary>Records where in the source this music came from.</summary>
    /// <param name="origin">The source location.</param>
    public void SetSpot(object origin) => SetProperty(OriginSymbol, origin);

    /// <summary>Gets where in the source this music came from.</summary>
    public object Origin => GetProperty(OriginSymbol);

    /// <summary>
    /// Turns this music into the stream event the translation layer actually sees.
    /// <para>
    /// This is the bridge between the two halves of the engine. The event's class list
    /// comes from the music's <c>name</c> — <c>NoteEvent</c> becomes
    /// <c>note-event</c> — expanded through <c>ly:make-event-class</c> into the whole
    /// ancestry, which is what lets an engraver listen for <c>rhythmic-event</c> and
    /// hear a note. Articulations are converted recursively, because they are music in
    /// the tree and events in the stream.
    /// </para>
    /// </summary>
    /// <returns>The event.</returns>
    public StreamEvent ToEvent()
    {
        Symbol className = Misc.CamelCaseToLispIdentifier(GetProperty(NameSymbol) as Symbol);

        // Catch programming mistakes.
        if (className == null || !IsMusicType(className))
        {
            Warn.ProgrammingError("Not a music type");
        }

        object eventClass = className == null
            ? Nil.Instance
            : SchemeUtilities.CallCallback(MakeEventClassProcedure(), className);

        // The music's MUTABLE alist becomes the event's IMMUTABLE properties, shared
        // rather than copied -- upstream passes mutable_property_alist_ straight into
        // the Stream_event constructor's immutable_props parameter. It reads oddly
        // until you see why: the music is still free to change, but what the event
        // carries was decided the moment it was made, so everything written below
        // (length, converted articulations) lands in the event's own mutable alist and
        // SHADOWS the immutable copy rather than editing the music.
        StreamEvent result = new StreamEvent(eventClass, MutablePropertyAlist);

        Moment length = GetLength();
        if (length.IsNonZero)
        {
            result.SetProperty(LengthSymbol, length);
        }

        // Articulations as events.
        object articulations = result.GetProperty(ArticulationsSymbol);
        if (articulations is Pair)
        {
            List<object> events = new List<object>();
            object cursor = articulations;
            while (cursor is Pair pair)
            {
                if (pair.Car is MusicObject articulation)
                {
                    events.Add(articulation.ToEvent());
                }

                cursor = pair.Cdr;
            }

            result.SetProperty(ArticulationsSymbol, Pair.ListFrom(events));
        }

        /*
          ES TODO: This is a temporary fix. Stream_events should not be
          aware of music.
        */
        result.SetProperty(MusicCauseSymbol, this);

        return result;
    }

    /// <summary>Turns this music into an event and broadcasts it at a context.</summary>
    /// <param name="context">The context to broadcast at.</param>
    public void SendToContext(Translation.Context context)
    {
        if (context == null)
        {
            Warn.ProgrammingError("cannot send an event without a context");
            return;
        }

        context.EventSource.Broadcast(ToEvent());
    }

    /// <summary>
    /// The default length callback: the music's duration expressed as a moment, or
    /// zero when it has none.
    /// </summary>
    /// <param name="music">The music to measure.</param>
    /// <returns>The length.</returns>
    public static Moment DurationLengthCallback(MusicObject music)
    {
        if (music == null)
        {
            throw new ArgumentNullException(nameof(music));
        }

        object duration = music.GetProperty(DurationSymbol);
        return duration is Duration d ? new Moment(d.ToWholeNotes()) : new Moment(0);
    }

    /// <summary>
    /// Deep-copies a music value: music objects are cloned, lists are rebuilt, and
    /// anything else is shared.
    /// </summary>
    /// <param name="value">The value to copy.</param>
    /// <returns>The copy.</returns>
    public static object MusicDeepCopy(object value)
    {
        if (value is MusicObject music)
        {
            return music.Clone();
        }

        if (value is Pair)
        {
            List<object> items = new List<object>();
            object cursor = value;
            while (cursor is Pair pair)
            {
                items.Add(MusicDeepCopy(pair.Car));
                cursor = pair.Cdr;
            }

            object result = MusicDeepCopy(cursor);
            for (int i = items.Count - 1; i >= 0; i--)
            {
                result = new Pair(items[i], result);
            }

            return result;
        }

        return value;
    }

    /// <summary>Records a source location on every music object in a structure.</summary>
    /// <param name="value">The music value to walk.</param>
    /// <param name="origin">The source location.</param>
    public static void SetOrigin(object value, object origin)
    {
        object cursor = value;
        while (cursor is Pair pair)
        {
            SetOrigin(pair.Car, origin);
            cursor = pair.Cdr;
        }

        if (cursor is MusicObject music)
        {
            music.SetProperty(OriginSymbol, origin);
        }
    }

    /// <summary>
    /// Resolves relative octaves across a list of music, returning either the first
    /// element's resulting pitch or the last's.
    /// </summary>
    /// <param name="list">The music list.</param>
    /// <param name="pitch">The pitch to start from.</param>
    /// <param name="returnFirst">
    /// <see langword="true"/> to return the first element's pitch, which is what
    /// chords need; <see langword="false"/> for the last, which is what sequences need.
    /// </param>
    /// <returns>The resulting pitch.</returns>
    public static Pitch MusicListToRelative(object list, Pitch pitch, bool returnFirst)
    {
        Pitch first = pitch;
        int count = 0;

        Pitch last = pitch;
        object cursor = list;
        while (cursor is Pair pair)
        {
            if (pair.Car is MusicObject music)
            {
                last = music.ToRelativeOctave(last);
                if (count++ == 0)
                {
                    first = last;
                }
            }

            cursor = pair.Cdr;
        }

        return returnFirst ? first : last;
    }

    /// <summary>Copies the mutable property alist, cloning any music it holds.</summary>
    /// <returns>The copied alist.</returns>
    protected override object CopyMutableProperties() => MusicDeepCopy(MutablePropertyAlist);

    /// <summary>Checks an assignment against the music property type table.</summary>
    /// <param name="symbol">The property being set.</param>
    /// <param name="value">The value being assigned.</param>
    protected override void TypeCheckAssignment(Symbol symbol, object value)
        => SchemeUtilities.TypeCheckAssignment(symbol, value, MusicTypeSymbol);

    /// <summary>Calls a Scheme callback with this object as its first argument.</summary>
    /// <param name="callback">The procedure to call.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The result, or the empty list when there is no interpreter.</returns>
    protected static object CallCallback(object callback, params object[] arguments)
    {
        Interpreter interpreter = LilyPondScheme.Current;
        if (interpreter == null || !(callback is Procedure))
        {
            return Nil.Instance;
        }

        return interpreter.Evaluator.Apply(callback, arguments);
    }

    /// <summary>
    /// Looks up <c>ly:make-event-class</c>, which <c>scm/define-event-classes.scm</c>
    /// defines in Scheme rather than in C++ — it reads the ancestor table that file
    /// builds, so there is nothing to port and nothing to cache across interpreters.
    /// </summary>
    private static object MakeEventClassProcedure()
    {
        Interpreter interpreter = LilyPondScheme.Current;
        if (interpreter == null)
        {
            return null;
        }

        Variable variable = interpreter.CurrentModule.Lookup(MakeEventClassSymbol);
        return variable != null && variable.IsBound ? variable.GetValue() : null;
    }

    private static bool IsNumber(object value)
        => value is long || value is int || value is double || value is System.Numerics.BigInteger;
}

/// <summary>
/// The length and start calculations shared by every kind of music that holds a list
/// of elements.
/// </summary>
public static class MusicSequence
{
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol DurationSymbol = Symbol.Intern("duration");

    /// <summary>Returns the total length of a music list, laid end to end.</summary>
    /// <param name="list">The music list.</param>
    /// <returns>The summed length.</returns>
    public static Moment CumulativeLength(object list)
    {
        Moment length = Moment.Zero;

        object cursor = list;
        while (cursor is Pair pair)
        {
            if (pair.Car is MusicObject music)
            {
                length += music.GetLength();
            }
            else
            {
                Warn.ProgrammingError("Music sequence should have music elements");
            }

            cursor = pair.Cdr;
        }

        return length;
    }

    /// <summary>
    /// Returns the length of the longest music in a list.
    /// <para>
    /// An empty set has zero length. In a set with mixed definite- and
    /// indefinite-length music, the indefinite-length music is assumed to depend on
    /// the definite-length music and is ignored — so an indefinite length propagates
    /// only when EVERY element is indefinite.
    /// </para>
    /// </summary>
    /// <param name="list">The music list.</param>
    /// <returns>The longest length.</returns>
    public static Moment MaximumLength(object list)
    {
        Moment duration = Moment.Zero;
        bool definite = false;
        bool indefinite = false;

        object cursor = list;
        while (cursor is Pair pair)
        {
            if (!(pair.Car is MusicObject music))
            {
                Warn.ProgrammingError("Music sequence should have music elements");
                definite = true; // damage control, hopefully
            }
            else
            {
                Moment length = music.GetLength();
                if (length < Moment.Infinity)
                {
                    definite = true;
                    if (length > duration)
                    {
                        duration = length;
                    }
                }
                else
                {
                    indefinite = true;
                }
            }

            cursor = pair.Cdr;
        }

        return definite || !indefinite ? duration : Moment.Infinity;
    }

    /// <summary>Returns the earliest start moment in a music list.</summary>
    /// <param name="list">The music list.</param>
    /// <returns>The minimum start.</returns>
    public static Moment MinimumStart(object list)
    {
        Moment result = Moment.Zero;

        object cursor = list;
        while (cursor is Pair pair)
        {
            if (pair.Car is MusicObject music)
            {
                Moment start = music.StartMoment();
                if (start < result)
                {
                    result = start;
                }
            }
            else
            {
                Warn.ProgrammingError("Music sequence should have music elements");
            }

            cursor = pair.Cdr;
        }

        return result;
    }

    /// <summary>
    /// Returns the accumulated grace time before the first element that actually
    /// occupies time.
    /// </summary>
    /// <param name="list">The music list.</param>
    /// <returns>The accumulated start.</returns>
    public static Moment FirstStart(object list)
    {
        Moment accumulated = Moment.Zero;

        // Accumulate grace time until finding the first element with non-grace time.
        object cursor = list;
        while (cursor is Pair pair)
        {
            if (!(pair.Car is MusicObject music))
            {
                Warn.ProgrammingError("Music sequence should have music elements");
                break;
            }

            accumulated = new Moment(
                accumulated.MainPart,
                accumulated.GracePart + music.StartMoment().GracePart);

            if (music.GetLength().IsNonZero)
            {
                break;
            }

            cursor = pair.Cdr;
        }

        return accumulated;
    }

    /// <summary>The length callback for simultaneous music: the longest element.</summary>
    /// <param name="music">The music to measure.</param>
    /// <returns>The length.</returns>
    public static Moment MaximumLengthCallback(MusicObject music)
        => MaximumLength(Property(music, ElementsSymbol));

    /// <summary>The length callback for sequential music: the sum of its elements.</summary>
    /// <param name="music">The music to measure.</param>
    /// <returns>The length.</returns>
    public static Moment CumulativeLengthCallback(MusicObject music)
        => CumulativeLength(Property(music, ElementsSymbol));

    /// <summary>
    /// The length callback for an event chord: its own duration when it has one — that
    /// is how chord repetitions carry a length — otherwise its longest element.
    /// </summary>
    /// <param name="music">The music to measure.</param>
    /// <returns>The length.</returns>
    public static Moment EventChordLengthCallback(MusicObject music)
    {
        object duration = Property(music, DurationSymbol);
        if (duration is Duration d)
        {
            return new Moment(d.ToWholeNotes());
        }

        return MaximumLength(Property(music, ElementsSymbol));
    }

    /// <summary>The start callback for simultaneous music: the earliest element start.</summary>
    /// <param name="music">The music to measure.</param>
    /// <returns>The start moment.</returns>
    public static Moment MinimumStartCallback(MusicObject music)
        => MinimumStart(Property(music, ElementsSymbol));

    /// <summary>The start callback for sequential music: the first element's start.</summary>
    /// <param name="music">The music to measure.</param>
    /// <returns>The start moment.</returns>
    public static Moment FirstStartCallback(MusicObject music)
        => FirstStart(Property(music, ElementsSymbol));

    private static object Property(MusicObject music, Symbol symbol)
    {
        if (music == null)
        {
            throw new ArgumentNullException(nameof(music));
        }

        return music.GetProperty(symbol);
    }
}
