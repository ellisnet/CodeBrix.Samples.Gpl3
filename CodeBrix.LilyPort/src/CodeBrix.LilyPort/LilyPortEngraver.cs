// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Backends;
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
/// The context tree it builds is a STAND-IN for <c>ly/engraver-init.ly</c>, which is
/// where a Staff's translator list and acceptance list really live and which arrives
/// with the parser. <see cref="Engrave"/> installs a
/// <see cref="Context.ContextFactory"/> describing the minimum a single-staff score
/// needs, and nothing more; see <see cref="EngraveResult.MissingTranslators"/> for
/// what that leaves out. Once Track P lands, the factory is replaced by real context
/// definitions and this type keeps its shape.
/// </para>
/// </summary>
public static class LilyPortEngraver
{
    /// <summary>
    /// Engraves a music tree and returns the system it produced.
    /// </summary>
    /// <param name="music">The music to engrave.</param>
    /// <param name="layout">
    /// The output definition, or <see langword="null"/> to use
    /// <see cref="PaperDefaults"/>.
    /// </param>
    /// <returns>The result, including the system and its stencil.</returns>
    public static EngraveResult Engrave(MusicObject music, OutputDef layout = null)
    {
        if (music == null)
        {
            throw new ArgumentNullException(nameof(music));
        }

        OutputDef paper = layout ?? PaperDefaults.Create();

        GlobalContext global = new GlobalContext(paper);
        global.AcceptedContexts.Add(Symbol.Intern("Score"));

        ScoreEngraver scoreEngraver = null;

        Func<Symbol, string, Context> previousFactory = Context.ContextFactory;
        try
        {
            Context.ContextFactory = (type, id) =>
            {
                Context context = new Context(type, id);

                switch (type.Name)
                {
                    case "Score":
                        context.AcceptedContexts.Add(Symbol.Intern("Staff"));
                        scoreEngraver = new ScoreEngraver();
                        scoreEngraver.AddTranslator(new PaperColumnEngraver(context));
                        context.Implementation = scoreEngraver;
                        break;

                    case "Staff":
                        context.AcceptedContexts.Add(Symbol.Intern("Voice"));
                        ApplyStaffDefaults(context);
                        EngraverGroup staff = new EngraverGroup();
                        staff.AddTranslator(new StaffSymbolEngraver(context));
                        staff.AddTranslator(new ClefEngraver(context));
                        staff.AddTranslator(new AxisGroupEngraver(context));
                        context.Implementation = staff;
                        break;

                    default:
                        EngraverGroup voice = new EngraverGroup();
                        voice.AddTranslator(new NoteHeadsEngraver(context));
                        context.Implementation = voice;
                        break;
                }

                return context;
            };

            global.Iterate(music);
        }
        finally
        {
            Context.ContextFactory = previousFactory;
        }

        SystemGrob system = scoreEngraver?.System;
        Stencil stencil = system == null ? Stencil.Empty : system.GetPaperSystemStencil();

        return new EngraveResult(global, scoreEngraver, system, stencil);
    }

    /// <summary>
    /// Sets the Staff context properties <c>ly/engraver-init.ly</c> sets, for the ones
    /// the engravers in this stand-in tree read.
    /// <para>
    /// These are DATA, not behaviour: <c>clefGlyph = "clefs.G"</c> and
    /// <c>clefPosition = -2</c> are what make the default clef a treble clef in the
    /// default place, and <c>middleCPosition = -6</c> is what puts middle C on the
    /// first ledger line below it. Without them the clef engraver builds a clef with no
    /// glyph, which then correctly kills itself — an empty staff with no error anywhere.
    /// </para>
    /// <para>
    /// Copied from the <c>\context { \name Staff … }</c> block at
    /// <c>ly/engraver-init.ly:822-826</c>, and they go away when the parser can read
    /// that file for itself.
    /// </para>
    /// </summary>
    /// <param name="context">The Staff context to set them on.</param>
    private static void ApplyStaffDefaults(Context context)
    {
        context.SetProperty("clefGlyph", new MutableString("clefs.G"));
        context.SetProperty("clefPosition", -2L);
        context.SetProperty("middleCClefPosition", -6L);
        context.SetProperty("middleCPosition", -6L);
        context.SetProperty("clefTransposition", 0L);
        context.SetProperty("firstClef", true);
        context.SetProperty("localAlterations", Nil.Instance);
        context.SetProperty("createSpacing", true);
    }

    /// <summary>Engraves a music tree straight to an SVG document.</summary>
    /// <param name="music">The music to engrave.</param>
    /// <param name="layout">The output definition, or <see langword="null"/> for the defaults.</param>
    /// <returns>The SVG document text.</returns>
    public static string EngraveToSvg(MusicObject music, OutputDef layout = null)
        => new SvgBackend().RenderDocument(Engrave(music, layout).Stencil);
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
    /// Gets the translators a full <c>ly/engraver-init.ly</c> Score would carry that
    /// this stand-in does not.
    /// <para>
    /// Named rather than merely absent, so a caller comparing against real LilyPond
    /// output can tell a missing FEATURE from a wrong one. Every entry is unported
    /// engine work, not a decision.
    /// </para>
    /// </summary>
    public static IReadOnlyList<string> MissingTranslators { get; } = new[]
    {
        "Timing_translator",         // bar numbers, measure positions, time signatures
        "Spacing_engraver",          // Spacing_spanner: the springs that space columns
        "Bar_engraver",              // bar lines
        "Stem_engraver",             // stems and flags
        "Rest_engraver",             // rests
        "Accidental_engraver",       // accidentals and key signatures
        "Ledger_line_engraver",      // ledger lines
        "Break_align_engraver",      // break alignment of the non-musical column
        "Font_size_engraver",        // fontSize handling
        "Separating_line_group_engraver", // the rest of Separation_item
    };
}
