// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// Pins the relative-octave MUSIC callbacks: the identity pair on
/// <c>RelativeOctaveMusic</c>, and the octave check that warns and CORRECTS the
/// running reference pitch so one wrong octave does not cascade. Also fences their
/// registration: the Scheme names must answer as real procedures, because
/// <c>define-music-types.scm</c> captures the bindings at load time.
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class RelativeOctaveMusicTests
{
    [Fact]
    public void the_relative_octave_music_callbacks_answer_the_pitch_unchanged()
    {
        //Arrange
        Pitch pitch = new Pitch(1, 2, Rational.Zero);

        //Act
        object viaRelative = RelativeOctaveMusic.RelativeCallback(null, pitch);
        object viaNoRelative = RelativeOctaveMusic.NoRelativeCallback(null, pitch);

        //Assert
        viaRelative.Should().BeSameAs(pitch);
        viaNoRelative.Should().BeSameAs(pitch);
    }

    [Fact]
    public void a_failed_octave_check_warns_and_corrects_the_reference_pitch()
    {
        //Arrange
        // The check note says c'' but a plain c after c' resolves to c' -- one octave
        // short. The callback must warn and answer the reference SHIFTED by the
        // difference.
        MusicObject music = new MusicObject(
            new Pair(new Pair(Symbol.Intern("pitch"), new Pitch(1, 0, Rational.Zero)), Nil.Instance));
        Pitch last = new Pitch(0, 0, Rational.Zero);

        Warn.ClearMessages();
        Warn.RecordMessages = true;
        try
        {
            //Act
            object corrected = RelativeOctaveCheck.RelativeCallback(music, last);

            //Assert
            Pitch result = corrected as Pitch;
            result.Should().NotBeNull();
            result.Octave.Should().Be(1);
            result.NoteName.Should().Be(0);

            string found = null;
            foreach (string message in Warn.Messages)
            {
                if (message.Contains("Failed octave check, got: ", System.StringComparison.Ordinal))
                {
                    found = message;
                    break;
                }
            }

            found.Should().NotBeNull();
            found.Should().Contain("c'");
        }
        finally
        {
            Warn.RecordMessages = false;
            Warn.ClearMessages();
        }
    }

    [Fact]
    public void a_passing_octave_check_leaves_the_reference_pitch_alone()
    {
        //Arrange
        MusicObject music = new MusicObject(
            new Pair(new Pair(Symbol.Intern("pitch"), new Pitch(0, 0, Rational.Zero)), Nil.Instance));
        Pitch last = new Pitch(0, 0, Rational.Zero);

        //Act
        object result = RelativeOctaveCheck.RelativeCallback(music, last);

        //Assert
        Pitch pitch = result as Pitch;
        pitch.Should().NotBeNull();
        pitch.Octave.Should().Be(0);
        pitch.NoteName.Should().Be(0);
    }

    [Fact]
    public void the_scheme_names_answer_as_real_procedures()
    {
        //Arrange
        // A bare interpreter carries the stubs plus the ported primitives; the
        // relative-octave names must already be REAL there, because the music-type
        // table captures whatever the bindings hold when the Scheme layer loads.
        Interpreter ambientBefore = LilyPondScheme.Current;
        object result = null;
        try
        {
            Interpreter.RunWithLargeStack(() =>
            {
                Interpreter interpreter = LilyPondScheme.CreateInterpreter();
                result = interpreter.EvalString(
                    "(ly:relative-octave-music::relative-callback #f (ly:make-pitch 1 2 0))",
                    "<epg9-test>");
            });
        }
        finally
        {
            LilyPondScheme.RestoreAmbient(ambientBefore);
        }

        //Assert
        Pitch pitch = result as Pitch;
        pitch.Should().NotBeNull();
        pitch.Octave.Should().Be(1);
        pitch.NoteName.Should().Be(2);
    }
}
