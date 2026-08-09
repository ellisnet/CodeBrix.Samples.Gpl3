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

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/translator-group.cc, lily/include/translator-group.hh;

// Modified by Jeremy Ellis on 2026-08-05 as part of the CodeBrix port.

/// <summary>
/// The set of translators living in one context, and the thing that drives them
/// through each timestep.
/// <para>
/// A context owns exactly one of these. It is the group, not the context, that knows
/// how to run <see cref="Translator.ProcessMusic"/> across everything in the context,
/// which is why the recursion helpers below take a context and reach the group
/// through it.
/// </para>
/// </summary>
public class TranslatorGroup
{
    private static readonly Symbol AnnounceNewContextSymbol = Symbol.Intern("AnnounceNewContext");
    private static readonly Symbol ContextSymbol = Symbol.Intern("context");

    private readonly List<Translator> _translators = new List<Translator>();
    private Listener _createChildListener;

    /// <summary>Gets the context this group belongs to.</summary>
    public Context Context { get; private set; }

    /// <summary>Gets the translators in this group, in run order.</summary>
    public IReadOnlyList<Translator> Translators => _translators;

    /// <summary>Gets the C++ class name this group corresponds to.</summary>
    public virtual string ClassName => "Translator_group";

    /// <summary>Adds a translator to the group.</summary>
    /// <param name="translator">The translator to add.</param>
    public void AddTranslator(Translator translator)
    {
        if (translator == null)
        {
            throw new ArgumentNullException(nameof(translator));
        }

        // Translators that must be last stay last, however many are added after.
        int index = _translators.Count;
        while (index > 0 && _translators[index - 1].MustBeLast && !translator.MustBeLast)
        {
            index--;
        }

        _translators.Insert(index, translator);
        translator.Context = Context;
    }

    /// <summary>
    /// Installs a computed translator list, replacing whatever the group held.
    /// <para>
    /// The order is upstream's, produced by <see cref="CreateChildTranslator"/>, and it
    /// is NOT the order <see cref="AddTranslator"/> would produce from the same names —
    /// upstream builds the list by inserting at the front from a reversed name list,
    /// which puts <c>must-be-last</c> translators at the end in REVERSE declaration
    /// order. Reproducing that exactly is why this setter exists.
    /// </para>
    /// </summary>
    /// <param name="translators">The translators, in run order.</param>
    internal void SetTranslators(List<Translator> translators)
    {
        _translators.Clear();
        _translators.AddRange(translators);
        foreach (Translator translator in _translators)
        {
            translator.Context = Context;
        }
    }

    /// <summary>Attaches the group and every translator in it to a context.</summary>
    /// <param name="context">The context to attach to.</param>
    public virtual void ConnectToContext(Context context)
    {
        if (Context != null)
        {
            Warn.ProgrammingError(
                "translator group is already connected to context " + Context.ContextName);
        }

        Context = context;

        // A group builds the translators of every context created BELOW its own. That
        // is the whole mechanism by which a context tree grows: Context::CreateContext
        // announces the infant, and this listener — on the PARENT — is what gives the
        // infant its engravers.
        if (context != null)
        {
            _createChildListener = context.EventSource.AddListener(
                this, CreateChildTranslator, AnnounceNewContextSymbol);
        }

        foreach (Translator translator in _translators)
        {
            translator.Context = context;
            translator.ConnectToContext();
        }
    }

    /// <summary>Detaches the group and every translator in it from its context.</summary>
    public virtual void DisconnectFromContext()
    {
        foreach (Translator translator in _translators)
        {
            translator.DisconnectFromContext();
        }

        if (Context != null && _createChildListener != null)
        {
            Context.EventSource.RemoveListener(_createChildListener, AnnounceNewContextSymbol);
        }

        _createChildListener = null;
        Context = null;
    }

    /// <summary>Runs <see cref="Translator.Initialize"/> on every translator.</summary>
    public virtual void Initialize()
    {
        foreach (Translator translator in _translators)
        {
            translator.Initialize();
        }
    }

    /// <summary>
    /// Runs <see cref="Translator.FinalizeTranslation"/> on every translator.
    /// <para>
    /// Not named <c>Finalize</c>: that is the C# destructor. See
    /// <see cref="Translator.FinalizeTranslation"/>.
    /// </para>
    /// </summary>
    public virtual void FinalizeTranslation()
    {
        foreach (Translator translator in _translators)
        {
            translator.FinalizeTranslation();
        }
    }

    /// <summary>Runs one phase of the timestep on every translator in the group.</summary>
    /// <param name="index">Which phase to run.</param>
    public void RunPhase(TranslatorPrecomputeIndex index)
    {
        // A snapshot, because a translator may create a context — and therefore
        // translators — while the phase is running.
        foreach (Translator translator in new List<Translator>(_translators))
        {
            switch (index)
            {
                case TranslatorPrecomputeIndex.StartTranslationTimestep:
                    translator.StartTranslationTimestep();
                    break;
                case TranslatorPrecomputeIndex.StopTranslationTimestep:
                    translator.StopTranslationTimestep();
                    break;
                case TranslatorPrecomputeIndex.PreProcessMusic:
                    translator.PreProcessMusic();
                    break;
                case TranslatorPrecomputeIndex.ProcessMusic:
                    translator.ProcessMusic();
                    break;
                case TranslatorPrecomputeIndex.ProcessAcknowledged:
                    translator.ProcessAcknowledged();
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The group's class and its translators.</returns>
    public override string ToString()
        => "#<TranslatorGroup " + ClassName + " " + string.Join(" ", _translators) + ">";

    /// <summary>
    /// Creates a new translator group for a newly created child context. Triggered by
    /// <c>AnnounceNewContext</c> events.
    /// <para>
    /// This is where a context definition's <c>\consists</c> list stops being data. Each
    /// name resolves through <see cref="TranslatorRegistry"/> to one of three things: a
    /// C#-side <see cref="TranslatorCreator"/>, a Scheme procedure that returns a
    /// definition alist when called with the context, or a definition alist outright —
    /// and the last two both end up as a <see cref="SchemeEngraver"/>. All 36 of
    /// LilyPond's Scheme-implemented translators arrive by that third route.
    /// </para>
    /// </summary>
    /// <param name="streamEvent">The <c>AnnounceNewContext</c> event.</param>
    public void CreateChildTranslator(StreamEvent streamEvent)
    {
        // get from AnnounceNewContext
        Context newContext = streamEvent.GetProperty(ContextSymbol) as Context;
        if (newContext == null)
        {
            return;
        }

        ContextDef definition = newContext.Definition;
        object ops = newContext.DefinitionMods;

        TranslatorGroup group = TranslatorRegistry.GetTranslatorGroup(
            definition.TranslatorGroupType);
        if (group == null)
        {
            return;
        }

        List<Translator> transList = new List<Translator>();

        // Upstream inserts into a std::list from the REVERSED name list: the first item
        // seen lands at the front and the cursor moves past it, every later ordinary
        // item is pushed in front of everything, and every must-be-last item goes where
        // the cursor is. The net effect is declaration order for ordinary translators
        // and REVERSE declaration order for the must-be-last ones, at the end. The
        // index below is that cursor; it advances on every insert because inserting at
        // the front shifts it too.
        int tail = 0;

        foreach (object name in Pair.ToList(definition.GetTranslatorNames(ops)))
        {
            Translator instance = Instantiate(name, newContext);
            if (instance == null)
            {
                continue;
            }

            if (instance.MustBeLast || transList.Count == 0)
            {
                transList.Insert(tail, instance);
            }
            else
            {
                transList.Insert(0, instance);
            }

            tail++;
        }

        /* Filter unwanted translator types. Required to make
           \with { \consists "..." } work. */
        if (group is EngraverGroup)
        {
            transList.RemoveAll(t => !t.IsLayout);
        }
        else if (group is PerformerGroup)
        {
            transList.RemoveAll(t => !t.IsMidi);
        }

        // TODO: scrap Context::Implementation
        newContext.Implementation = group;
        group.SetTranslators(transList);
        group.ConnectToContext(newContext);

        RecurseInitialize(newContext);
    }

    /// <summary>
    /// Runs <see cref="Translator.FinalizeTranslation"/> and
    /// <see cref="FinalizeTranslation"/> over a context and everything below it.
    /// </summary>
    /// <param name="context">The context to start from.</param>
    /// <param name="direction">Positive runs parents last, negative runs them first.</param>
    public static void RecurseFinalize(Context context, Direction direction)
    {
        if (context == null)
        {
            return;
        }

        TranslatorGroup group = context.Implementation;

        if (group != null && direction == Direction.Negative)
        {
            group.FinalizeTranslation();
        }

        foreach (Context child in new List<Context>(context.Children))
        {
            RecurseFinalize(child, direction);
        }

        if (group != null && direction == Direction.Positive)
        {
            group.FinalizeTranslation();
        }
    }

    /// <summary>
    /// Turns one entry of a resolved <c>\consists</c> list into a translator.
    /// </summary>
    private static Translator Instantiate(object name, Context newContext)
    {
        object creator = name;

        if (creator is Symbol symbol)
        {
            creator = TranslatorRegistry.GetTranslatorCreator(symbol);
            if (creator == null)
            {
                // GetTranslatorCreator already named the unknown translator.
                return null;
            }
        }

        if (creator is TranslatorCreator translatorCreator)
        {
            return translatorCreator.Call(newContext);
        }

        if (creator is Translator alreadyBuilt)
        {
            return alreadyBuilt;
        }

        if (SchemeUtilities.IsProcedure(creator))
        {
            creator = SchemeUtilities.CallCallback(creator, newContext);
        }

        if (creator is Pair || creator is Nil)
        {
            return new SchemeEngraver(creator, newContext);
        }

        Warn.Warning("cannot find: `" + name + "'");
        return null;
    }

    private static void RecurseInitialize(Context context)
    {
        if (context == null)
        {
            return;
        }

        context.Implementation?.Initialize();

        foreach (Context child in new List<Context>(context.Children))
        {
            RecurseInitialize(child);
        }
    }

    /// <summary>
    /// Runs one timestep phase over a context and everything below it.
    /// <para>
    /// The direction decides whether a parent acts before or after its children, and
    /// that ordering is load bearing: <c>ProcessMusic</c> runs downward so an outer
    /// context can set up what an inner one reads, while <c>StopTranslationTimestep</c>
    /// runs upward so children finish before their parent does.
    /// </para>
    /// </summary>
    /// <param name="context">The context to start from.</param>
    /// <param name="index">Which phase to run.</param>
    /// <param name="direction">Positive runs parents last, negative runs them first.</param>
    public static void RecurseOverTranslators(
        Context context,
        TranslatorPrecomputeIndex index,
        Direction direction)
    {
        if (context == null)
        {
            return;
        }

        TranslatorGroup group = context.Implementation;

        if (group != null && direction == Direction.Negative)
        {
            group.RunPhase(index);
        }

        foreach (Context child in new List<Context>(context.Children))
        {
            RecurseOverTranslators(child, index, direction);
        }

        if (group != null && direction == Direction.Positive)
        {
            group.RunPhase(index);
        }
    }
}
