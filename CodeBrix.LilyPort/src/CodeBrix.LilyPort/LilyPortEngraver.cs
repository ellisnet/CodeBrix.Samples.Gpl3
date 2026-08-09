// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Backends;
using CodeBrix.LilyPort.Engine;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort;

/// <summary>
/// Engraves music end to end: a music tree in, an SVG document out.
/// <para>
/// This is the spine the whole engine hangs off — contexts, iterators, engravers,
/// paper columns, one system, stencils, backend — and it is the first place any of it
/// runs together rather than in pieces.
/// </para>
/// <para>
/// The context tree is built from REAL context definitions, read out of the output
/// definition the score is laid out under, which is where <c>ly/engraver-init.ly</c>
/// put them. The hand-written Score/Staff/Voice factory that stood in for those
/// definitions until EPG2 is gone: a Staff's translator list, its acceptance list and
/// its property defaults now all come from the file that declares them.
/// </para>
/// <para>
/// Translators the port has not reached yet are reported by name, once each, by
/// <c>TranslatorRegistry</c> — see <see cref="EngraveResult.MissingTranslators"/> for
/// what is measured. A named absence is the point: a caller comparing against real
/// LilyPond output can tell a missing FEATURE from a wrong one.
/// </para>
/// </summary>
public static class LilyPortEngraver
{
    private static readonly Symbol GlobalSymbol = Symbol.Intern("Global");

    /// <summary>
    /// Engraves a music tree and returns the system it produced.
    /// </summary>
    /// <param name="music">The music to engrave.</param>
    /// <param name="layout">
    /// The output definition, carrying the context definitions to build the tree from.
    /// <see langword="null"/> takes <see cref="LilyPondInit.DefaultLayout"/>, which is
    /// the <c>$defaultlayout</c> the <c>ly/</c> init layer builds.
    /// </param>
    /// <returns>The result, including the system and its stencil.</returns>
    public static EngraveResult Engrave(MusicObject music, OutputDef layout = null)
    {
        if (music == null)
        {
            throw new ArgumentNullException(nameof(music));
        }

        OutputDef paper = layout ?? LilyPondInit.DefaultLayout();

        ContextDef globalDef = ContextDef.FindContextDef(paper, GlobalSymbol);
        if (globalDef == null)
        {
            throw new InvalidOperationException(
                "the output definition carries no Global context definition; "
                + "ly/engraver-init.ly has not been read into it");
        }

        GlobalContext global = new GlobalContext(paper, globalDef);

        // Without this the tree still builds and nothing engraves: the group's
        // AnnounceNewContext listener is what gives every context below its translators.
        global.MakeGlobalTranslator();

        global.Iterate(music);

        ScoreEngraver scoreEngraver = FindScoreEngraver(global);
        SystemGrob system = scoreEngraver?.System;

        // Upstream's Paper_score::process, then get_paper_systems. PROCESS is what states
        // the horizontal spacing problem -- it reads every grob's springs-and-rods, which
        // is how the SpacingSpanner is reached -- and get_paper_systems is what chooses
        // the line breaks, clones the root system into one piece per line, and makes those
        // pieces independent. Skipping either one still produces a drawing, of every note
        // stacked at the origin.
        //
        // EPG15 (2026-08-08) replaced PlaceColumnsOnOneLine here. THE REPORTED SYSTEM IS
        // NOW THE FIRST BROKEN PIECE, NOT THE ROOT, and that is upstream's own shape
        // rather than a convenience: break substitution ends by calling
        // handle_broken_dependencies on the root, whose bounds by then belong to its
        // pieces, so the root SUICIDES. It is not drawn upstream either. A run that
        // produced no pieces at all -- degenerate music with nothing to break -- still
        // reports the root, so a probe against empty input behaves as it did.
        PaperScore paperScore = scoreEngraver?.PaperScore;
        Stencil stencil = Stencil.Empty;
        int lineCount = 0;

        if (paperScore != null)
        {
            paperScore.Process();
            IReadOnlyList<Prob> paperSystems = paperScore.GetPaperSystems();

            if (system != null && system.BrokenIntos.Count > 0)
            {
                system = system.BrokenSystems()[0];
            }

            // The stencil comes OUT of the paper system rather than being asked for a
            // second time: GetPaperSystemStencil runs PostProcessing, which TRANSLATES the
            // system, so calling it twice would move the music twice.
            //
            // EVERY line is drawn, not just the first (EPG15 close-out, 2026-08-08). Until
            // then this took paperSystems[0] and threw the rest away, which was invisible
            // while PlaceColumnsOnOneLine made every score one line and became the whole
            // of EPG15's visible effect the moment line breaking landed: the port CHOSE
            // three lines for break.ly and drew one of them.
            //
            // THE FIXED PADDING IS RETIRED (EPG16, 2026-08-09). Until now the lines were
            // stacked at a hardcoded 4.0 — an EPG15-era stand-in for page layout, from
            // before there was any. The offsets come from the REAL Page_layout_problem
            // now, so lines sit where their SKYLINES allow rather than at an invented
            // constant, and a line with nothing above it no longer reserves the same gap
            // as one carrying a row of dynamics.
            //
            // The problem is built with NO paper book and NO page, which is upstream's own
            // supported shape for a book-less layout problem and is the honest one here:
            // this API engraves ONE SCORE against an UNSCALED layout and has no page to
            // put it on, so there is nothing to read system-system-spacing off. Anything
            // that wants the paper's real spacing wants a book, and that path is
            // BatchRunner — which is what the regression harness and Lily.Shell both use.
            List<Stencil> lines = new List<Stencil>();
            foreach (Prob paperSystem in paperSystems)
            {
                lines.Add(PaperSystem.GetStencil(paperSystem));
            }

            stencil = StackLinesByLayoutProblem(paperSystems, lines);
            lineCount = paperSystems.Count;
        }
        else if (system != null)
        {
            stencil = system.GetPaperSystemStencil();
            lineCount = 1;
        }

        return new EngraveResult(global, scoreEngraver, system, stencil, lineCount);
    }

    /// <summary>Engraves a music tree straight to an SVG document.</summary>
    /// <param name="music">The music to engrave.</param>
    /// <param name="layout">The output definition, or <see langword="null"/> for the default.</param>
    /// <returns>The SVG document text.</returns>
    public static string EngraveToSvg(MusicObject music, OutputDef layout = null)
        => new SvgBackend().RenderDocument(Engrave(music, layout).Stencil);

    /// <summary>
    /// Places a score's lines at the offsets <c>Page_layout_problem</c> chooses for them.
    /// </summary>
    /// <param name="paperSystems">The paper systems, in order.</param>
    /// <param name="lines">Their stencils, already extracted.</param>
    /// <returns>The combined drawing.</returns>
    private static Stencil StackLinesByLayoutProblem(
        IReadOnlyList<Prob> paperSystems, IReadOnlyList<Stencil> lines)
    {
        if (lines.Count == 0)
        {
            return Stencil.Empty;
        }

        Stencil combined = lines[0];
        if (lines.Count == 1)
        {
            return combined;
        }

        PageLayoutProblem problem
            = new PageLayoutProblem(null, null, Pair.ListFrom(paperSystems));

        // Ragged, because a book-less problem has no page to fill: the springs would
        // otherwise be stretched against the placeholder page height.
        List<object> offsets = Pair.ToList(problem.Solution(true));

        for (int i = 1; i < lines.Count; i++)
        {
            if (i >= offsets.Count)
            {
                // Fewer offsets than lines can only mean the solver refused the problem.
                // Falling back to the stand-in would hide that, so say so and stop.
                Flower.Warn.ProgrammingError(
                    "page layout answered fewer offsets than there are lines");
                break;
            }

            // The offsets are distances DOWN from the first line's reference point, which
            // is the direction Y decreases in.
            Stencil line = lines[i];
            line.TranslateAxis(
                -SchemeConvert.ToDouble(offsets[i], 0.0)
                + SchemeConvert.ToDouble(offsets[0], 0.0),
                Flower.Axis.Y);
            combined.AddStencil(line);
        }

        return combined;
    }

    private static ScoreEngraver FindScoreEngraver(Context context)
    {
        if (context == null)
        {
            return null;
        }

        if (context.Implementation is ScoreEngraver found)
        {
            return found;
        }

        foreach (Context child in context.Children)
        {
            ScoreEngraver result = FindScoreEngraver(child);
            if (result != null)
            {
                return result;
            }
        }

        return null;
    }
}

/// <summary>What one engraving run produced.</summary>
public sealed class EngraveResult
{
    /// <summary>Initializes a result.</summary>
    /// <param name="global">The root context the run used.</param>
    /// <param name="scoreEngraver">The score engraver, or null when none was created.</param>
    /// <param name="system">The system, or null when nothing was engraved.</param>
    /// <param name="stencil">The stencil of every line, stacked.</param>
    /// <param name="lineCount">How many lines the breaker chose.</param>
    public EngraveResult(
        GlobalContext global,
        ScoreEngraver scoreEngraver,
        SystemGrob system,
        Stencil stencil,
        int lineCount = 1)
    {
        Global = global;
        ScoreEngraver = scoreEngraver;
        System = system;
        Stencil = stencil;
        LineCount = lineCount;
    }

    /// <summary>Gets the root context the run used.</summary>
    public GlobalContext Global { get; }

    /// <summary>Gets the score engraver, which owns the paper score.</summary>
    public ScoreEngraver ScoreEngraver { get; }

    /// <summary>Gets the one system everything was typeset into.</summary>
    public SystemGrob System { get; }

    /// <summary>Gets the stencil of every line this score broke into, stacked.</summary>
    public Stencil Stencil { get; }

    /// <summary>
    /// Gets how many lines the breaker chose for this score. One for music that fits on
    /// a single line; more once <c>\break</c> or a full line forces a break.
    /// </summary>
    public int LineCount { get; }

    /// <summary>Gets the paper score, or null when nothing was engraved.</summary>
    public PaperScore PaperScore => ScoreEngraver?.PaperScore;

    /// <summary>
    /// Gets the translators <c>ly/engraver-init.ly</c> names that the port cannot yet
    /// make — COMPUTED against <c>Scheme/translators.tsv</c>, not remembered.
    /// <para>
    /// Named rather than merely absent, so a caller comparing against real LilyPond
    /// output can tell a missing FEATURE from a wrong one. Every entry is unported
    /// engine work, not a decision, and gate G4 closes when the list is empty.
    /// </para>
    /// </summary>
    /// <returns>The translator names still missing.</returns>
    public static IReadOnlyList<string> MissingTranslators()
        => TranslatorRegistry.MissingTranslators(
            Engine.Bootstrap.LilyPondScheme.Registries,
            TranslatorManifest.DeclaredNames());
}
