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

        // Upstream's Paper_score::process, then the placement half of
        // get_paper_systems. PROCESS is what states the horizontal spacing problem --
        // it reads every grob's springs-and-rods, which is how the SpacingSpanner is
        // reached -- and the placement is what moves the columns off x = 0. Skipping
        // either one still produces a drawing, of every note stacked at the origin.
        PaperScore paperScore = scoreEngraver?.PaperScore;
        if (paperScore != null)
        {
            paperScore.Process();
            paperScore.PlaceColumnsOnOneLine();
        }

        Stencil stencil = system == null ? Stencil.Empty : system.GetPaperSystemStencil();

        return new EngraveResult(global, scoreEngraver, system, stencil);
    }

    /// <summary>Engraves a music tree straight to an SVG document.</summary>
    /// <param name="music">The music to engrave.</param>
    /// <param name="layout">The output definition, or <see langword="null"/> for the default.</param>
    /// <returns>The SVG document text.</returns>
    public static string EngraveToSvg(MusicObject music, OutputDef layout = null)
        => new SvgBackend().RenderDocument(Engrave(music, layout).Stencil);

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
    /// <param name="stencil">The system's stencil.</param>
    public EngraveResult(
        GlobalContext global,
        ScoreEngraver scoreEngraver,
        SystemGrob system,
        Stencil stencil)
    {
        Global = global;
        ScoreEngraver = scoreEngraver;
        System = system;
        Stencil = stencil;
    }

    /// <summary>Gets the root context the run used.</summary>
    public GlobalContext Global { get; }

    /// <summary>Gets the score engraver, which owns the paper score.</summary>
    public ScoreEngraver ScoreEngraver { get; }

    /// <summary>Gets the one system everything was typeset into.</summary>
    public SystemGrob System { get; }

    /// <summary>Gets the system's stencil.</summary>
    public Stencil Stencil { get; }

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
