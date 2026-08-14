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

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/score-engraver.cc, lily/include/score-engraver.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The top-level engraver: it owns the <see cref="PaperScore"/> and the one
/// <see cref="SystemGrob"/> everything is typeset into.
/// <para>
/// It is where the timestep actually happens. <see cref="OneTimeStep"/> runs
/// pre-process-music, process-music, the announce/acknowledge round and
/// stop-translation-timestep across the whole context tree — so the interpretation loop
/// above only has to say "a timestep occurred" and every engraver in every context is
/// driven from here, in the right order.
/// </para>
/// <para>
/// It also adopts every grob anyone announces: <see cref="AddGrobToAnnounceLocallyOnly"/>
/// typesets it into the root system on the way past, and
/// <see cref="TypesetAll"/> gives anything still lacking a vertical parent to the
/// system. That is why an engraver never has to know about the system it draws on.
/// </para>
/// </summary>
public class ScoreEngraver : EngraverGroup
{
    private static readonly Symbol OneTimeStepSymbol = Symbol.Intern("OneTimeStep");
    private static readonly Symbol PrepareSymbol = Symbol.Intern("Prepare");
    private static readonly Symbol FinishSymbol = Symbol.Intern("Finish");
    private static readonly Symbol SystemSymbol = Symbol.Intern("System");
    private static readonly Symbol OutputSymbol = Symbol.Intern("output");
    private static readonly Symbol RootSystemSymbol = Symbol.Intern("rootSystem");
    private static readonly Symbol SkipTypesettingSymbol = Symbol.Intern("skipTypesetting");

    private readonly List<Grob> _elements = new List<Grob>();

    private SystemGrob _system;
    private PaperScore _paperScore;

    private Listener _oneTimeStepListener;
    private Listener _prepareListener;
    private Listener _finishListener;

    /// <summary>Gets the C++ class name this group corresponds to.</summary>
    public override string ClassName => "Score_engraver";

    /// <summary>Gets the paper score this engraver built.</summary>
    public PaperScore PaperScore => _paperScore;

    /// <summary>Gets the one system everything is typeset into.</summary>
    public SystemGrob System => _system;

    /// <summary>
    /// Creates the paper score and the root system, before any music is interpreted.
    /// </summary>
    public override void Initialize()
    {
        _paperScore = new PaperScore(Context?.OutputDef);
        Context?.SetProperty(OutputSymbol, _paperScore);

        object properties = new GrobPropertyInfo(Context, SystemSymbol).Updated();
        _paperScore.TypesetSystem(new SystemGrob(properties));

        _system = _paperScore.RootSystem;
        Context?.SetProperty(RootSystemSymbol, _system);

        base.Initialize();
    }

    /// <summary>
    /// Attaches to a context, and starts listening to the TOP context for the three
    /// events that drive the score.
    /// </summary>
    /// <param name="context">The context to attach to.</param>
    public override void ConnectToContext(Context context)
    {
        base.ConnectToContext(context);

        Dispatcher source = context?.Root?.EventSource;
        if (source == null)
        {
            return;
        }

        _oneTimeStepListener = source.AddListener(this, OneTimeStep, OneTimeStepSymbol);
        _prepareListener = source.AddListener(this, Prepare, PrepareSymbol);
        _finishListener = source.AddListener(this, Finish, FinishSymbol);
    }

    /// <summary>Detaches from the context and stops listening.</summary>
    public override void DisconnectFromContext()
    {
        Dispatcher source = Context?.Root?.EventSource;
        if (source != null)
        {
            if (_oneTimeStepListener != null)
            {
                source.RemoveListener(_oneTimeStepListener, OneTimeStepSymbol);
            }

            if (_prepareListener != null)
            {
                source.RemoveListener(_prepareListener, PrepareSymbol);
            }

            if (_finishListener != null)
            {
                source.RemoveListener(_finishListener, FinishSymbol);
            }
        }

        _oneTimeStepListener = null;
        _prepareListener = null;
        _finishListener = null;

        base.DisconnectFromContext();
    }

    /// <summary>Finishes the score and typesets whatever is still outstanding.</summary>
    public override void FinalizeTranslation()
    {
        base.FinalizeTranslation();
        TypesetAll();
    }

    /// <summary>
    /// Adopts every announced grob into the root system as it goes past.
    /// </summary>
    /// <param name="info">The announcement record.</param>
    /// <param name="startEnd">Positive to announce a start, negative an end.</param>
    public override void AddGrobToAnnounceLocallyOnly(GrobInfo info, Direction startEnd)
    {
        base.AddGrobToAnnounceLocallyOnly(info, startEnd);

        if (startEnd == Direction.Positive)
        {
            _paperScore?.RootSystem?.TypesetGrob(info.Grob);
            _elements.Add(info.Grob);
        }
    }

    /// <summary>
    /// Runs one timestep across the whole context tree.
    /// </summary>
    /// <param name="streamEvent">The <c>OneTimeStep</c> event.</param>
    public void OneTimeStep(StreamEvent streamEvent)
    {
        /* Do pre-process-music even in skipTypesetting mode:
        start/stop-translation-timestep + listeners happen too, and some
        engravers need paper columns (created during pre-process-music) in
        stop-translation-timestep.  pre-process-music is not used to create
        grobs (except paper columns), so that's not a problem. */
        TranslatorGroup.RecurseOverTranslators(
            Context, TranslatorPrecomputeIndex.PreProcessMusic, Direction.Positive);

        if (!SchemeUtilities.ToBool(Context?.GetProperty(SkipTypesettingSymbol)))
        {
            TranslatorGroup.RecurseOverTranslators(
                Context, TranslatorPrecomputeIndex.ProcessMusic, Direction.Positive);
            DoAnnounces();
        }

        TranslatorGroup.RecurseOverTranslators(
            Context, TranslatorPrecomputeIndex.StopTranslationTimestep, Direction.Positive);
        TypesetAll();
    }

    /// <summary>Starts a timestep across the whole context tree.</summary>
    /// <param name="streamEvent">The <c>Prepare</c> event.</param>
    public void Prepare(StreamEvent streamEvent)
        => TranslatorGroup.RecurseOverTranslators(
            Context, TranslatorPrecomputeIndex.StartTranslationTimestep, Direction.Negative);

    /// <summary>Finalizes every translator, children before parents.</summary>
    /// <param name="streamEvent">The <c>Finish</c> event.</param>
    public void Finish(StreamEvent streamEvent) => FinalizeRecursively(Context);

    /// <summary>
    /// Gives every grob that has no vertical parent yet to the system, then forgets
    /// them.
    /// </summary>
    public void TypesetAll()
    {
        foreach (Grob element in _elements)
        {
            if (element.YParent == null)
            {
                AxisGroupInterface.AddElement(_system, element);
            }
        }

        _elements.Clear();
    }

    private static void FinalizeRecursively(Context context)
    {
        if (context == null)
        {
            return;
        }

        foreach (Context child in new List<Context>(context.Children))
        {
            FinalizeRecursively(child);
        }

        context.Implementation?.FinalizeTranslation();
    }
}
