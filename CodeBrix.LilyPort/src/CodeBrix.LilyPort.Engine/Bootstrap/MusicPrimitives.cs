// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The Scheme entry points of the ported musical value types: pitch, moment, duration and
/// scale.
/// <para>
/// Each of these replaces a stub installed by <see cref="EnginePrimitives"/>. Installing
/// them after the stubs is what turns an entry on the porting worklist into a working
/// primitive, and the stub call counts are what said these were worth porting first --
/// <c>ly:make-pitch</c> alone is reached over nine hundred times while LilyPond's Scheme
/// layer loads.
/// </para>
/// </summary>
public static class MusicPrimitives
{
    /// <summary>Installs the primitives, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallScale(interpreter);
        InstallPitch(interpreter);
        InstallMoment(interpreter);
        InstallDuration(interpreter);
    }

    private static void InstallScale(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:make-scale", 1, 1, a =>
        {
            if (!(a[0] is object[] steps))
            {
                throw SchemeErrors.WrongType("ly:make-scale", "vector of rational", a[0]);
            }

            List<Rational> tones = new List<Rational>(steps.Length);
            foreach (object step in steps)
            {
                tones.Add(SchemeConvert.ToRational(step, "ly:make-scale"));
            }

            return new Scale(tones);
        });

        interpreter.DefinePrimitive("ly:default-scale", 0, 0, a => Scale.DefaultGlobal);
    }

    private static void InstallPitch(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:make-pitch", 2, 3, a => new Pitch(
            SchemeConvert.ToInt(a[0], "ly:make-pitch"),
            SchemeConvert.ToInt(a[1], "ly:make-pitch"),
            a.Length > 2 && !(a[2] is DefaultArgument)
                ? SchemeConvert.ToRational(a[2], "ly:make-pitch")
                : Rational.Zero,
            CurrentScale(interpreter)));

        interpreter.DefinePrimitive("ly:pitch-octave", 1, 1, a =>
            (long)AsPitch(a[0], "ly:pitch-octave").Octave);

        interpreter.DefinePrimitive("ly:pitch-notename", 1, 1, a =>
            (long)AsPitch(a[0], "ly:pitch-notename").NoteName);

        interpreter.DefinePrimitive("ly:pitch-alteration", 1, 1, a =>
            SchemeConvert.FromRational(AsPitch(a[0], "ly:pitch-alteration").Alteration));

        interpreter.DefinePrimitive("ly:pitch-steps", 1, 1, a =>
            (long)AsPitch(a[0], "ly:pitch-steps").Steps());

        interpreter.DefinePrimitive("ly:pitch-tones", 1, 1, a =>
            SchemeConvert.FromRational(AsPitch(a[0], "ly:pitch-tones").TonePitch()));

        interpreter.DefinePrimitive("ly:pitch-semitones", 1, 1, a =>
            (long)AsPitch(a[0], "ly:pitch-semitones").RoundedSemitonePitch());

        interpreter.DefinePrimitive("ly:pitch-quartertones", 1, 1, a =>
            (long)AsPitch(a[0], "ly:pitch-quartertones").RoundedQuartertonePitch());

        interpreter.DefinePrimitive("ly:pitch-transpose", 2, 2, a =>
            AsPitch(a[0], "ly:pitch-transpose").Transposed(AsPitch(a[1], "ly:pitch-transpose")));

        interpreter.DefinePrimitive("ly:pitch-diff", 2, 2, a =>
            Pitch.Interval(AsPitch(a[1], "ly:pitch-diff"), AsPitch(a[0], "ly:pitch-diff")));

        interpreter.DefinePrimitive("ly:pitch-negate", 1, 1, a =>
            AsPitch(a[0], "ly:pitch-negate").Negated());

        interpreter.DefinePrimitive("ly:pitch<?", 2, 2, a =>
            Pitch.Compare(AsPitch(a[0], "ly:pitch<?"), AsPitch(a[1], "ly:pitch<?")) < 0);
    }

    private static void InstallMoment(Interpreter interpreter)
    {
        // The compatibility forms matter: (ly:make-moment 1 4) is a quarter note, not a
        // main-and-grace pair, and upstream disambiguates on the SIGN of the second
        // argument -- a positive second argument can only be a denominator.
        interpreter.DefinePrimitive("ly:make-moment", 1, 4, a =>
        {
            Rational main = SchemeConvert.ToRational(a[0], "ly:make-moment");
            if (a.Length < 2 || a[1] is DefaultArgument)
            {
                return new Moment(main);
            }

            Rational second = SchemeConvert.ToRational(a[1], "ly:make-moment");
            if (a.Length < 3 || a[2] is DefaultArgument)
            {
                if (!second.IsNegative && second.IsNonZero)
                {
                    return new Moment(new Rational(
                        SchemeConvert.ToLong(a[0], "ly:make-moment"),
                        SchemeConvert.ToLong(a[1], "ly:make-moment")));
                }

                return new Moment(main, second);
            }

            long graceNumerator = SchemeConvert.ToLong(a[2], "ly:make-moment");
            long graceDenominator = a.Length > 3 && !(a[3] is DefaultArgument)
                ? SchemeConvert.ToLong(a[3], "ly:make-moment")
                : 1;

            return new Moment(
                new Rational(
                    SchemeConvert.ToLong(a[0], "ly:make-moment"),
                    SchemeConvert.ToLong(a[1], "ly:make-moment")),
                new Rational(graceNumerator, graceDenominator));
        });

        interpreter.DefinePrimitive("ly:moment-add", 2, 2, a =>
            AsMoment(a[0], "ly:moment-add") + AsMoment(a[1], "ly:moment-add"));

        interpreter.DefinePrimitive("ly:moment-sub", 2, 2, a =>
            AsMoment(a[0], "ly:moment-sub") - AsMoment(a[1], "ly:moment-sub"));

        interpreter.DefinePrimitive("ly:moment-mul", 2, 2, a =>
            AsMoment(a[0], "ly:moment-mul") * AsMoment(a[1], "ly:moment-mul").MainPart);

        interpreter.DefinePrimitive("ly:moment-div", 2, 2, a =>
            AsMoment(a[0], "ly:moment-div") / AsMoment(a[1], "ly:moment-div").MainPart);

        interpreter.DefinePrimitive("ly:moment-mod", 2, 2, a =>
        {
            Moment left = AsMoment(a[0], "ly:moment-mod");
            Moment right = AsMoment(a[1], "ly:moment-mod");
            return new Moment(
                left.MainPart % right.MainPart,
                left.GracePart % right.GracePart);
        });

        interpreter.DefinePrimitive("ly:moment-main", 1, 1, a =>
            SchemeConvert.FromRational(AsMoment(a[0], "ly:moment-main").MainPart));

        interpreter.DefinePrimitive("ly:moment-grace", 1, 1, a =>
            SchemeConvert.FromRational(AsMoment(a[0], "ly:moment-grace").GracePart));

        interpreter.DefinePrimitive("ly:moment-main-numerator", 1, 1, a =>
            AsMoment(a[0], "ly:moment-main-numerator").MainPart.Numerator);

        interpreter.DefinePrimitive("ly:moment-main-denominator", 1, 1, a =>
            AsMoment(a[0], "ly:moment-main-denominator").MainPart.Denominator);

        interpreter.DefinePrimitive("ly:moment-grace-numerator", 1, 1, a =>
            AsMoment(a[0], "ly:moment-grace-numerator").GracePart.Numerator);

        interpreter.DefinePrimitive("ly:moment-grace-denominator", 1, 1, a =>
            AsMoment(a[0], "ly:moment-grace-denominator").GracePart.Denominator);

        interpreter.DefinePrimitive("ly:moment<?", 2, 2, a =>
            AsMoment(a[0], "ly:moment<?") < AsMoment(a[1], "ly:moment<?"));
    }

    private static void InstallDuration(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:make-duration", 1, 4, a =>
        {
            int length = SchemeConvert.ToInt(a[0], "ly:make-duration");
            int dots = a.Length > 1 && !(a[1] is DefaultArgument)
                ? SchemeConvert.ToInt(a[1], "ly:make-duration")
                : 0;

            bool compress = false;
            Rational numerator = Rational.One;
            if (a.Length > 2 && !(a[2] is DefaultArgument))
            {
                numerator = SchemeConvert.ToRational(a[2], "ly:make-duration");
                compress = true;
            }

            Rational denominator = Rational.One;
            if (a.Length > 3 && !(a[3] is DefaultArgument))
            {
                denominator = SchemeConvert.ToRational(a[3], "ly:make-duration");
                compress = true;
            }

            Duration duration = new Duration(length, dots);
            return compress ? duration.Compressed(numerator / denominator) : duration;
        });

        interpreter.DefinePrimitive("ly:duration-log", 1, 1, a =>
            (long)AsDuration(a[0], "ly:duration-log").DurationLog);

        interpreter.DefinePrimitive("ly:duration-dot-count", 1, 1, a =>
            (long)AsDuration(a[0], "ly:duration-dot-count").DotCount);

        interpreter.DefinePrimitive("ly:duration-factor", 1, 1, a =>
        {
            Rational factor = AsDuration(a[0], "ly:duration-factor").Factor;
            return new Pair(factor.Numerator, factor.Denominator);
        });

        interpreter.DefinePrimitive("ly:duration-length", 1, 1, a =>
            new Moment(AsDuration(a[0], "ly:duration-length").ToWholeNotes()));

        interpreter.DefinePrimitive("ly:duration-scale", 1, 1, a =>
            SchemeConvert.FromRational(AsDuration(a[0], "ly:duration-scale").Factor));

        interpreter.DefinePrimitive("ly:duration<?", 2, 2, a =>
            AsDuration(a[0], "ly:duration<?") < AsDuration(a[1], "ly:duration<?"));

        interpreter.DefinePrimitive("ly:number->duration", 1, 1, a =>
            Duration.FromWholeNotes(SchemeConvert.ToRational(a[0], "ly:number->duration"), true));

        interpreter.DefinePrimitive("ly:intlog2", 1, 1, a =>
        {
            long value = SchemeConvert.ToLong(a[0], "ly:intlog2");
            if (value <= 0)
            {
                throw SchemeErrors.WrongType("ly:intlog2", "positive integer", a[0]);
            }

            long result = 0;
            while (value > 1)
            {
                value >>= 1;
                result++;
            }

            return result;
        });
    }

    /// <summary>
    /// Gets the scale new pitches are built against.
    /// <para>
    /// Upstream reads <c>default-global-scale</c> out of the <c>(lily)</c> module at
    /// every pitch construction, because a score may change it. This does the same, and
    /// falls back to the built-in default while <c>lily.scm</c> is still loading and the
    /// binding does not exist yet.
    /// </para>
    /// </summary>
    /// <param name="interpreter">The interpreter to read from.</param>
    /// <returns>The current default scale.</returns>
    public static Scale CurrentScale(Interpreter interpreter)
    {
        Variable variable = interpreter.CurrentModule?.Lookup(Symbol.Intern("default-global-scale"));
        if (variable != null && variable.IsBound && variable.GetValue() is Scale scale)
        {
            return scale;
        }

        return Scale.DefaultGlobal;
    }

    private static Pitch AsPitch(object value, string procedureName)
        => value as Pitch ?? throw SchemeErrors.WrongType(procedureName, "pitch", value);

    private static Moment AsMoment(object value, string procedureName)
        => value is Moment moment ? moment : throw SchemeErrors.WrongType(procedureName, "moment", value);

    private static Duration AsDuration(object value, string procedureName)
        => value is Duration duration
            ? duration
            : throw SchemeErrors.WrongType(procedureName, "duration", value);
}
