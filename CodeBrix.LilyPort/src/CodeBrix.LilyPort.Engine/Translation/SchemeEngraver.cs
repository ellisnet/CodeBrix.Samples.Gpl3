/*
  scheme-engraver.cc -- implement Scheme_engraver

  source file of the GNU LilyPond music typesetter

  Copyright (c) 2009--2026 Han-Wen Nienhuys <hanwen@lilypond.org>

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
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/scheme-engraver.cc, lily/include/scheme-engraver.hh (and, for the interface-keyed acknowledger lookup, lily/translator-dispatch-list.cc);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// An engraver whose whole behaviour is a Scheme alist: what
/// <c>ly:register-translator</c> hands the engine, and what every one of LilyPond's
/// Scheme-implemented translators is.
/// <para>
/// The alist names procedures under well-known keys — <c>process-music</c>,
/// <c>acknowledgers</c>, <c>listeners</c> and the rest — and this type is the bridge
/// that lets those be driven by exactly the same timestep protocol as a C# engraver.
/// Without it, every <c>\consists</c> of a Scheme engraver resolves to a list nobody can
/// run, which is silent: the context is built, the engraver is "there", and it does
/// nothing.
/// </para>
/// </summary>
public sealed class SchemeEngraver : Engraver
{
    private static readonly Symbol StartTranslationTimestepSymbol
        = Symbol.Intern("start-translation-timestep");

    private static readonly Symbol StopTranslationTimestepSymbol
        = Symbol.Intern("stop-translation-timestep");

    private static readonly Symbol PreProcessMusicSymbol = Symbol.Intern("pre-process-music");
    private static readonly Symbol ProcessMusicSymbol = Symbol.Intern("process-music");
    private static readonly Symbol ProcessAcknowledgedSymbol = Symbol.Intern("process-acknowledged");
    private static readonly Symbol InitializeSymbol = Symbol.Intern("initialize");
    private static readonly Symbol FinalizeSymbol = Symbol.Intern("finalize");
    private static readonly Symbol IsMidiSymbol = Symbol.Intern("is-midi");
    private static readonly Symbol IsLayoutSymbol = Symbol.Intern("is-layout");
    private static readonly Symbol MustBeLastSymbol = Symbol.Intern("must-be-last");
    private static readonly Symbol ListenersSymbol = Symbol.Intern("listeners");
    private static readonly Symbol AcknowledgersSymbol = Symbol.Intern("acknowledgers");
    private static readonly Symbol EndAcknowledgersSymbol = Symbol.Intern("end-acknowledgers");
    private static readonly Symbol NameSymbol = Symbol.Intern("name");

    private readonly object _startTranslationTimestep;
    private readonly object _stopTranslationTimestep;
    private readonly object _preProcessMusic;
    private readonly object _processMusic;
    private readonly object _processAcknowledged;
    private readonly object _initialize;
    private readonly object _finalize;
    private readonly bool _isMidi;
    private readonly bool _isLayout;
    private readonly bool _mustBeLast;
    private readonly List<KeyValuePair<Symbol, object>> _perInstanceListeners
        = new List<KeyValuePair<Symbol, object>>();

    private readonly Dictionary<Symbol, object> _acknowledgers
        = new Dictionary<Symbol, object>(ReferenceComparer.Instance);

    private readonly Dictionary<Symbol, object> _endAcknowledgers
        = new Dictionary<Symbol, object>(ReferenceComparer.Instance);

    private readonly string _name;

    /// <summary>Initializes an engraver from its definition alist.</summary>
    /// <param name="definition">The alist <c>ly:register-translator</c> was given.</param>
    /// <param name="context">The context the engraver lives in.</param>
    public SchemeEngraver(object definition, Context context)
        : base(context)
    {
        _startTranslationTimestep = Callable(StartTranslationTimestepSymbol, definition);
        _stopTranslationTimestep = Callable(StopTranslationTimestepSymbol, definition);
        _preProcessMusic = Callable(PreProcessMusicSymbol, definition);
        _processMusic = Callable(ProcessMusicSymbol, definition);
        _processAcknowledged = Callable(ProcessAcknowledgedSymbol, definition);
        _initialize = Callable(InitializeSymbol, definition);
        _finalize = Callable(FinalizeSymbol, definition);

        _isMidi = SchemeUtilities.ToBool(AssocGet(IsMidiSymbol, definition, false));

        // The default for is-layout is the NEGATION of is-midi, which is the one place
        // the two flags are not independent: a translator that says nothing at all is a
        // layout translator, and one that declares itself MIDI is not.
        //was previously: _isLayout = SchemeUtilities.IsSchemeTrue(
        // Upstream reads is-layout with from_scm<bool>, exactly as it reads is-midi two
        // lines up; truthiness here would accept any non-#f value the two flags do not.
        _isLayout = SchemeUtilities.ToBool(
            AssocGet(IsLayoutSymbol, definition, !_isMidi));

        _mustBeLast = SchemeUtilities.ToBool(AssocGet(MustBeLastSymbol, definition, false));

        for (object p = AssocGet(ListenersSymbol, definition, Nil.Instance);
             p is Pair pair;
             p = pair.Cdr)
        {
            if (!(pair.Car is Pair entry))
            {
                continue;
            }

            // We should check the arity of the function?
            if (entry.Car is Symbol eventClass && SchemeUtilities.IsProcedure(entry.Cdr))
            {
                _perInstanceListeners.Add(
                    new KeyValuePair<Symbol, object>(eventClass, entry.Cdr));
            }
        }

        InitAcknowledgers(AssocGet(AcknowledgersSymbol, definition, Nil.Instance), _acknowledgers);
        InitAcknowledgers(
            AssocGet(EndAcknowledgersSymbol, definition, Nil.Instance), _endAcknowledgers);

        _name = AssocGet(NameSymbol, definition, Nil.Instance) is Symbol name
            ? name.Name
            : "Scheme_engraver";
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Scheme_engraver";

    /// <summary>Gets the translator's name, as its definition alist declares it.</summary>
    public override string Name => _name;

    /// <summary>Gets a value indicating whether this translator must run last.</summary>
    public override bool MustBeLast => _mustBeLast;

    /// <summary>Gets a value indicating whether this translator contributes to MIDI.</summary>
    public override bool IsMidi => _isMidi;

    /// <summary>Gets a value indicating whether this translator contributes to layout.</summary>
    public override bool IsLayout => _isLayout;

    /// <summary>Registers the definition's per-instance listeners.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();

        foreach (KeyValuePair<Symbol, object> entry in _perInstanceListeners)
        {
            object procedure = entry.Value;
            ListenTo(entry.Key, e => SchemeUtilities.CallCallback(procedure, this, e));
        }
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Calls the definition's <c>initialize</c>, when it has one.</summary>
    public override void Initialize() => Call(_initialize);

    /// <summary>Calls the definition's <c>finalize</c>, when it has one.</summary>
    public override void FinalizeTranslation() => Call(_finalize);

    /// <summary>Calls the definition's <c>start-translation-timestep</c>.</summary>
    public override void StartTranslationTimestep() => Call(_startTranslationTimestep);

    /// <summary>Calls the definition's <c>stop-translation-timestep</c>.</summary>
    public override void StopTranslationTimestep() => Call(_stopTranslationTimestep);

    /// <summary>Calls the definition's <c>pre-process-music</c>.</summary>
    public override void PreProcessMusic() => Call(_preProcessMusic);

    /// <summary>Calls the definition's <c>process-music</c>.</summary>
    public override void ProcessMusic() => Call(_processMusic);

    /// <summary>Calls the definition's <c>process-acknowledged</c>.</summary>
    public override void ProcessAcknowledged() => Call(_processAcknowledged);

    /// <summary>
    /// Runs every acknowledger whose interface the announced grob carries.
    /// <para>
    /// DIVERGENCE, recorded in PORT-COVERAGE. Upstream memoises this lookup per
    /// engraver GROUP in an <c>Engraver_dispatch_list</c> keyed by the grob's name, so
    /// that the interface walk happens once per grob type rather than once per
    /// announcement. The port does the walk each time: the port's C# engravers filter
    /// inside their own <c>AcknowledgeGrob</c> overrides rather than registering
    /// per-interface acknowledgers, so there is no group-wide table to key. The set of
    /// procedures that RUN, and the order they run in, is the same.
    /// </para>
    /// </summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info) => Acknowledge(_acknowledgers, info);

    /// <summary>Runs every end-acknowledger whose interface the grob carries.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeEndGrob(GrobInfo info) => Acknowledge(_endAcknowledgers, info);

    /// <summary>
    /// Extracts a value from the definition if callable; otherwise answers
    /// <see langword="null"/> — upstream's <c>SCM_UNDEFINED</c>.
    /// </summary>
    private static object Callable(Symbol symbol, object definition)
    {
        object value = AssocGet(symbol, definition, false);
        return SchemeUtilities.IsProcedure(value) ? value : null;
    }

    private static object AssocGet(Symbol key, object alist, object fallback)
    {
        Pair entry = SchemeUtilities.Assq(key, alist);
        return entry != null ? entry.Cdr : fallback;
    }

    private static void InitAcknowledgers(object alist, Dictionary<Symbol, object> table)
    {
        for (object p = alist; p is Pair pair; p = pair.Cdr)
        {
            if (pair.Car is Pair entry
                && entry.Car is Symbol interfaceName
                && SchemeUtilities.IsProcedure(entry.Cdr))
            {
                table[interfaceName] = entry.Cdr;
            }
        }
    }

    private void Acknowledge(Dictionary<Symbol, object> table, GrobInfo info)
    {
        if (table.Count == 0 || info.Grob == null)
        {
            return;
        }

        // A grob's interface list depends on its definition, but also on its class
        // (e.g., System adds system-interface and spanner-interface).
        Symbol grobClass = Symbol.Intern(info.Grob.ClassName);
        if (table.TryGetValue(grobClass, out object classHandler))
        {
            SchemeUtilities.CallCallback(classHandler, this, info.Grob, info.OriginEngraver);
        }

        for (object p = info.Grob.Interfaces; p is Pair pair; p = pair.Cdr)
        {
            if (pair.Car is Symbol interfaceName
                && table.TryGetValue(interfaceName, out object handler))
            {
                SchemeUtilities.CallCallback(handler, this, info.Grob, info.OriginEngraver);
            }
        }
    }

    private void Call(object procedure)
    {
        if (procedure != null)
        {
            SchemeUtilities.CallCallback(procedure, this);
        }
    }
}
