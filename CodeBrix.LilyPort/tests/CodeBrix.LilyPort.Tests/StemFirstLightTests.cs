// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Reader;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// EPG6 first light: stems through the REAL context tree — the one
/// <c>ly/engraver-init.ly</c> declares, where the Voice's own <c>\consists</c> list
/// names <c>Stem_engraver</c>. The Engine-level tests drive the engraver through a
/// hand tree; this is the fence that the real <c>\consists</c> resolution reaches it
/// too, with real geometry coming out the other side.
/// </summary>
[Collection("engine-global-state")]
public class StemFirstLightTests
{
    private static readonly object LoadGate = new object();

    private static Interpreter _interpreter;

    private static Interpreter Loaded()
    {
        lock (LoadGate)
        {
            // The canonical per-class loader FirstLightTests uses: each class keeps its
            // own fully-loaded interpreter and rebuilds when another class replaced the
            // ambient one.
            if (_interpreter == null || !ReferenceEquals(LilyPondScheme.Current, _interpreter))
            {
                Interpreter interpreter = null;
                Interpreter.RunWithLargeStack(() =>
                {
                    interpreter = LilyPondScheme.CreateInterpreter();
                    LilyPondScheme.LoadViaLilyScm(interpreter);
                });

                _interpreter = interpreter;
            }

            return _interpreter;
        }
    }

    private static object Eval(string source)
    {
        Interpreter interpreter = Loaded();
        object result = null;
        Interpreter.RunWithLargeStack(() =>
        {
            foreach (object form in SchemeReader.ReadAll(source, "<stem-first-light>"))
            {
                result = interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule);
            }
        });

        return result;
    }

    private static EngraveResult Engrave(int durationLog)
    {
        MusicObject music = (MusicObject)Eval(
            "(make-music 'SequentialMusic 'elements (list (make-music 'NoteEvent"
            + " 'duration (ly:make-duration " + durationLog + ")"
            + " 'pitch (ly:make-pitch 0 0 0))))");
        EngraveResult result = null;
        Interpreter.RunWithLargeStack(() => result = LilyPortEngraver.Engrave(music));
        return result;
    }

    private static Grob FindGrob(EngraveResult result, string name)
    {
        foreach (Grob grob in result.System.AllElements)
        {
            if (string.Equals(grob.Name, name, StringComparison.Ordinal))
            {
                return grob;
            }
        }

        return null;
    }

    [Fact]
    public void the_real_voice_context_makes_a_stem_with_real_geometry()
    {
        //Arrange / Act
        EngraveResult result = Engrave(2);

        //Assert
        Grob stem = FindGrob(result, "Stem");
        stem.Should().NotBeNull();
        FindGrob(result, "StemStub").Should().NotBeNull();

        // The head found its stem through the announce round, not through a fixture.
        Grob head = FindGrob(result, "NoteHead");
        head.GetObject("stem").Should().BeSameAs(stem);

        // Real geometry: the printed stem is a rounded box with vertical extent, and
        // its stencil came off ly:stem::print through the property system.
        CodeBrix.LilyPort.Engine.Layout.Stencil? stencil = null;
        Interpreter.RunWithLargeStack(() => stencil = stem.GetStencil());
        stencil.HasValue.Should().BeTrue();
        stencil.Value.IsEmpty.Should().BeFalse();
        (stencil.Value.Extent(CodeBrix.LilyPort.Flower.Axis.Y).Length > 0).Should().BeTrue();
    }

    [Fact]
    public void an_eighth_note_prints_a_real_flag_glyph()
    {
        //Arrange / Act
        EngraveResult result = Engrave(3);

        //Assert
        Grob flag = FindGrob(result, "Flag");
        flag.Should().NotBeNull();

        object glyphName = null;
        CodeBrix.LilyPort.Engine.Layout.Stencil? stencil = null;
        Interpreter.RunWithLargeStack(() =>
        {
            glyphName = flag.GetProperty("glyph-name");
            stencil = flag.GetStencil();
        });

        // On the real tree the treble clef's middleCPosition puts c' at -6, low in the
        // staff, so the stem goes UP: flags.u3 out of the real Emmentaler.
        glyphName.ToString().Should().Be("flags.u3");
        stencil.HasValue.Should().BeTrue();
        stencil.Value.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void a_whole_note_makes_an_invisible_stem_on_the_real_tree()
    {
        //Arrange / Act
        EngraveResult result = Engrave(0);

        //Assert
        // The invisible stem must EXIST — downstream spacing measures against it.
        Grob stem = FindGrob(result, "Stem");
        stem.Should().NotBeNull();
        Stem.IsInvisible(stem).Should().BeTrue();
        FindGrob(result, "Flag").Should().BeNull();
    }
}
