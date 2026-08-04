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
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/translator-group.cc, lily/include/translator-group.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

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
    private readonly List<Translator> _translators = new List<Translator>();

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
