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
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/global-context.cc, lily/include/global-context.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <summary>
/// The root of the context tree, and the thing that actually drives interpretation.
/// <para>
/// It is the only context that is not addressable from user music, and it owns the
/// main loop: ask the top iterator when its next event is due, advance to that moment,
/// let the iterator process it, run one timestep across every context, repeat. That
/// loop is where a music tree becomes a stream of events and, through the engravers,
/// a collection of grobs.
/// </para>
/// </summary>
public class GlobalContext : Context
{
    private static readonly Symbol GlobalSymbol = Symbol.Intern("Global");
    private static readonly Symbol MomentSymbol = Symbol.Intern("moment");
    private static readonly Symbol PrepareSymbol = Symbol.Intern("Prepare");
    private static readonly Symbol OneTimeStepSymbol = Symbol.Intern("OneTimeStep");
    private static readonly Symbol FinishSymbol = Symbol.Intern("Finish");
    private static readonly Symbol FinalizationsSymbol = Symbol.Intern("finalizations");

    // Moments the translation must stop at even when no iterator has an event due:
    // an engraver asks for one when it needs to act at a time the music does not name.
    private readonly SortedSet<Moment> _extraMoments = new SortedSet<Moment>();

    private static readonly Symbol AllGrobDescriptionsSymbol = Symbol.Intern("all-grob-descriptions");
    private static readonly Symbol PropertyDefaultsSymbol = Symbol.Intern("property-defaults");

    private Moment _previousMoment = -Moment.Infinity;

    /// <summary>Initializes the root context.</summary>
    public GlobalContext()
        : base(GlobalSymbol)
    {
    }

    /// <summary>
    /// Initializes the root context under an output definition, and installs the grob
    /// property defaults from it.
    /// </summary>
    /// <param name="layout">The output definition the score is laid out under.</param>
    public GlobalContext(Layout.OutputDef layout)
        : base(GlobalSymbol)
    {
        Layout = layout;
        RegisterContextListeners();
        InitializeGrobProperties();
    }

    /// <summary>
    /// Initializes the root context from the <c>Global</c> definition in an output
    /// definition — the real route, and what <c>ly:make-global-context</c> takes.
    /// <para>
    /// The definition is where <c>\accepts Score</c> and
    /// <c>\grobdescriptions #all-grob-descriptions</c> come from, so a Global built this
    /// way needs neither an acceptance list assembled by hand nor the grob-description
    /// shortcut <see cref="InitializeGrobProperties"/> exists for.
    /// </para>
    /// </summary>
    /// <param name="layout">The output definition the score is laid out under.</param>
    /// <param name="definition">The <c>Global</c> context definition.</param>
    public GlobalContext(Layout.OutputDef layout, ContextDef definition)
        : base(definition, Nil.Instance)
    {
        Layout = layout;
        RegisterContextListeners();

        // Upstream reaches the same values through the definition's own property
        // operations, which \grobdescriptions #all-grob-descriptions turns into one
        // `grob-descriptions' entry per grob. Applying them here is what
        // create_context_from_event does for every other context.
        definition.ApplyDefaultPropertyOperations(this);
        InstallGrobDescriptions(definition.GrobDescriptions);
    }

    /// <summary>
    /// Gets a value indicating whether user music may address this context. Always
    /// false: Global exists to drive the timesteps.
    /// </summary>
    public override bool IsAccessibleToUser => false;

    /// <summary>Gets or sets the output definition the score is laid out under.</summary>
    public Layout.OutputDef Layout { get; set; }

    /// <summary>Gets the output definition. Global is the only context that holds one.</summary>
    public override Layout.OutputDef OutputDef => Layout;

    /// <summary>
    /// Installs one <c>Grob_properties</c> context property per grob type, built from
    /// the global grob descriptions with the layout's <c>property-defaults</c>
    /// appended.
    /// <para>
    /// This is what makes every later <c>\override</c> work: <c>Grob_property_info</c>
    /// finds the context property named after the grob and pushes onto it, and
    /// <c>Grob_property_info::updated</c> folds the stack back down when a grob is
    /// made. Without it there is nothing to push onto and no defaults to fall back to.
    /// </para>
    /// <para>
    /// It is also how <c>fonts</c> — the family-to-font-name mapping — reaches a grob's
    /// property alist chain, which is where font selection reads it.
    /// </para>
    /// <para>
    /// DIVERGENCE, recorded in PORT-COVERAGE: upstream takes the descriptions from the
    /// Global <c>Context_def</c>, which gets them from
    /// <c>\grobdescriptions #all-grob-descriptions</c> in <c>ly/engraver-init.ly</c>.
    /// The port reads the same <c>all-grob-descriptions</c> straight out of the Scheme
    /// layer, because context definitions arrive with the parser (Track P). The VALUE
    /// is identical; only the route differs.
    /// </para>
    /// </summary>
    public void InitializeGrobProperties()
    {
        Variable variable = Bootstrap.LilyPondScheme.Current
            ?.CurrentModule?.Lookup(AllGrobDescriptionsSymbol);
        object descriptions = variable != null && variable.IsBound ? variable.GetValue() : null;

        InstallGrobDescriptions(descriptions);
    }

    /// <summary>
    /// Installs one <c>Grob_properties</c> context property per grob type from a
    /// grob-description alist, with the layout's <c>property-defaults</c> appended.
    /// </summary>
    /// <param name="descriptions">The grob descriptions, keyed by grob name.</param>
    public void InstallGrobDescriptions(object descriptions)
    {
        object defaults = Layout?.LookupVariable(PropertyDefaultsSymbol) ?? Nil.Instance;

        object cursor = descriptions;
        while (cursor is Pair pair)
        {
            if (pair.Car is Pair entry && entry.Car is Symbol grobName)
            {
                SetProperty(
                    grobName,
                    new GrobProperties(AppendAlist(entry.Cdr, defaults), Nil.Instance));
            }

            cursor = pair.Cdr;
        }
    }

    /// <summary>
    /// Creates the root's translator group and connects it.
    /// <para>
    /// The group is EMPTY and still load bearing: connecting it is what registers
    /// <see cref="TranslatorGroup.CreateChildTranslator"/> on Global's event source, and
    /// that listener is what gives the Score its engravers when the Score is announced.
    /// Skip it and the whole tree below builds with no translators at all — contexts
    /// exist, nothing engraves, and nothing reports a problem.
    /// </para>
    /// <para>Upstream: <c>ly:make-global-translator</c>.</para>
    /// </summary>
    /// <returns>The group.</returns>
    public TranslatorGroup MakeGlobalTranslator()
    {
        TranslatorGroup group = new TranslatorGroup();
        group.ConnectToContext(this);
        Implementation = group;
        return group;
    }

    /// <summary>Gets the moment the previous timestep was at.</summary>
    public Moment PreviousMoment => _previousMoment;

    /// <summary>Gets the score context, which is Global's only child.</summary>
    public Context ScoreContext => Children.Count > 0 ? Children[0] : null;

    /// <summary>Gets how many extra moments are still queued.</summary>
    public int MomentsLeft => _extraMoments.Count;

    /// <summary>
    /// Requests that the translation stop at a moment, even if no music has an event
    /// there.
    /// </summary>
    /// <param name="moment">The moment to stop at.</param>
    public void AddMomentToProcess(Moment moment)
    {
        if (moment < NowMoment)
        {
            Warn.ProgrammingError("trying to freeze in time");
        }

        _extraMoments.Add(moment);
    }

    /// <summary>Registers a procedure to run before the next timestep.</summary>
    /// <param name="procedure">The procedure and its arguments, as a list.</param>
    public void AddFinalization(object procedure)
        => SetProperty(FinalizationsSymbol, new Pair(procedure, GetProperty(FinalizationsSymbol)));

    /// <summary>Runs and clears the queued finalizations.</summary>
    public void ApplyFinalizations()
    {
        object list = GetProperty(FinalizationsSymbol);
        SetProperty(FinalizationsSymbol, Nil.Instance);

        object cursor = list;
        while (cursor is Pair pair)
        {
            if (pair.Car is Pair call)
            {
                SchemeUtilities.CallCallback(call.Car, Pair.ToList(call.Cdr).ToArray());
            }

            cursor = pair.Cdr;
        }
    }

    /// <summary>
    /// Interprets a piece of music: the main translation loop.
    /// </summary>
    /// <param name="music">The music to interpret.</param>
    /// <param name="forceFoundMusic">Run the loop even when the music has no length.</param>
    /// <returns><see langword="true"/> when there was music to interpret.</returns>
    public bool Iterate(MusicObject music, bool forceFoundMusic = false)
    {
        if (music == null)
        {
            throw new ArgumentNullException(nameof(music));
        }

        MusicIterator iterator = MusicIterator.CreateTopIterator(music);

        bool foundMusic = forceFoundMusic;
        if (!forceFoundMusic)
        {
            Moment length = iterator.MusicLength - iterator.MusicStartMoment;
            foundMusic = length.IsNonZero && iterator.Ok;
        }

        if (!foundMusic)
        {
            return false;
        }

        _previousMoment = -Moment.Infinity;
        NowMoment = -Moment.Infinity;

        Moment finalMoment = iterator.MusicLength;
        if (finalMoment.MainPart.IsInfinite)
        {
            // Top-level music of indefinite length -- LyricCombineMusic or similar.
            // There is no use case for it at the top level, and the loop below cannot
            // be trusted to terminate on it, so cut it short.
            Warn.Warning("cannot determine music length");
            finalMoment = Moment.Zero;
        }

        // Force at least one full pass so contexts are initialised even when the
        // iterator has nothing to process.
        AddMomentToProcess(Moment.Zero);

        for (bool first = true; true; first = false)
        {
            Moment when = iterator.PendingMoment;

            // Written out rather than asking Ok, to save a second PendingMoment call --
            // it is a virtual walk over the whole iterator tree.
            bool ok = when < Moment.Infinity || iterator.RunAlways;

            when = SneakyInsertExtraMoment(when);
            if (when > finalMoment)
            {
                break;
            }

            if (when == _previousMoment)
            {
                Warn.ProgrammingError("Moment is not increasing.  Aborting interpretation.");
                break;
            }

            SendPrepare(when);

            if (first)
            {
                iterator.InitContext(this);
            }

            if (ok)
            {
                iterator.Process(when);
            }

            SendStreamEvent(OneTimeStepSymbol);
            ApplyFinalizations();
        }

        iterator.Quit();
        SendStreamEvent(FinishSymbol);
        return true;
    }

    /// <summary>Advances the clock, as the <c>Prepare</c> event does upstream.</summary>
    /// <param name="moment">The moment being prepared.</param>
    public void Prepare(Moment moment)
    {
        if (_previousMoment.MainPart.IsInfinite && _previousMoment < Moment.Zero)
        {
            _previousMoment = moment;
        }
        else
        {
            _previousMoment = NowMoment;
        }

        NowMoment = moment;
    }

    private static object AppendAlist(object head, object tail)
    {
        List<object> entries = Pair.ToList(head);
        object result = tail ?? Nil.Instance;
        for (int i = entries.Count - 1; i >= 0; i--)
        {
            result = new Pair(entries[i], result);
        }

        return result;
    }

    private Moment SneakyInsertExtraMoment(Moment when)
    {
        while (_extraMoments.Count > 0 && _extraMoments.Min <= when)
        {
            when = _extraMoments.Min;
            _extraMoments.Remove(when);
        }

        return when;
    }

    private void SendPrepare(Moment when)
    {
        // Upstream sends a Prepare stream event carrying the moment, which
        // Global_context::prepare handles. The port advances the clock directly AND
        // broadcasts, so a translator listening for Prepare still hears it while the
        // clock is guaranteed to move even before any translator exists.
        Prepare(when);

        StreamEvent prepare = new StreamEvent(Pair.List(PrepareSymbol), Nil.Instance);
        prepare.SetProperty(MomentSymbol, when);
        SendStreamEvent(prepare);
    }

    private void SendStreamEvent(Symbol className)
        => SendStreamEvent(new StreamEvent(Pair.List(className), Nil.Instance));
}
