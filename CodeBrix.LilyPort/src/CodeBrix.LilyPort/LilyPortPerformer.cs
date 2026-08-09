// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort;

// NEW IN FAMILY — no upstream file. This is the MIDI-side twin of LilyPortEngraver, and it
// exists for the same reason that one does: upstream reaches performance through
// Score::book_rendering -> Paper_book::output -> write-performances-midis, and the port's
// batch runner takes D20's score-level short-circuit instead. When EPG16 moves the runner
// onto the real ly:book-process path, this collapses into that path exactly as
// LilyPortEngraver does.

/// <summary>
/// Performs music end to end: a music tree in, a <see cref="Performance"/> out.
/// </summary>
/// <remarks>
/// <para>
/// The context tree is built from the <c>\midi</c> output definition, which is where
/// <c>ly/performer-init.ly</c> put the performer-side context definitions. That is the
/// whole difference from the layout path: the same music, iterated through a tree whose
/// <c>\consists</c> lists name performers instead of engravers, produces audio elements
/// instead of grobs.
/// </para>
/// </remarks>
public static class LilyPortPerformer
{
    private static readonly Symbol GlobalSymbol = Symbol.Intern("Global");

    /// <summary>Performs a music tree and returns the performance it produced.</summary>
    /// <param name="music">The music to perform.</param>
    /// <param name="midi">
    /// The <c>\midi</c> output definition, carrying the context definitions to build the
    /// tree from.
    /// </param>
    /// <returns>The finished performance, or <see langword="null"/> when none was made.</returns>
    public static Performance Perform(MusicObject music, OutputDef midi)
    {
        if (music == null)
        {
            throw new ArgumentNullException(nameof(music));
        }

        if (midi == null)
        {
            throw new ArgumentNullException(nameof(midi));
        }

        ContextDef globalDef = ContextDef.FindContextDef(midi, GlobalSymbol);
        if (globalDef == null)
        {
            throw new InvalidOperationException(
                "the \\midi definition carries no Global context definition; "
                + "ly/performer-init.ly has not been read into it");
        }

        GlobalContext global = new GlobalContext(midi, globalDef);

        // Without this the tree still builds and nothing performs: the group's
        // AnnounceNewContext listener is what gives every context below its performers.
        global.MakeGlobalTranslator();

        global.Iterate(music);

        ScorePerformer scorePerformer = FindScorePerformer(global);
        Performance performance = scorePerformer?.Performance;

        // Upstream reaches this through ly:format-output, which asks the finished context
        // for its `output' and calls process () on it. Performance::process is what finds
        // the moment the piece starts, so every track begins on the same tick; skipping
        // it silently shifts every track that does not begin at zero.
        performance?.Process();

        return performance;
    }

    private static ScorePerformer FindScorePerformer(Context context)
    {
        if (context == null)
        {
            return null;
        }

        if (context.Implementation is ScorePerformer found)
        {
            return found;
        }

        foreach (Context child in context.Children)
        {
            ScorePerformer result = FindScorePerformer(child);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}
