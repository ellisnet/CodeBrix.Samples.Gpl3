/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2022--2026 Daniel Eble <nine.fierce.ballads@gmail.com>

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
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/caesura-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Notates a short break in sound that does not shorten the previous note.
/// <para>
/// Depending on the result of passing the value of <c>caesuraType</c> through
/// <c>caesuraTypeTransform</c>, this engraver may create a <c>BreathingSign</c> with
/// <c>CaesuraScript</c> grobs aligned to it, or it may create <c>CaesuraScript</c>
/// grobs and align them to a <c>BarLine</c>.
/// </para>
/// <para>
/// If this engraver observes a <c>BarLine</c>, it calls <c>caesuraTypeTransform</c>
/// again with the new information, and if necessary, recreates its grobs.
/// </para>
/// </summary>
public class CaesuraEngraver : Engraver
{
    private static readonly Symbol CaesuraTypeSymbol = Symbol.Intern("caesuraType");
    private static readonly Symbol CaesuraTypeTransformSymbol
        = Symbol.Intern("caesuraTypeTransform");

    private static readonly Symbol ArticulationsSymbol = Symbol.Intern("articulations");
    private static readonly Symbol ArticulationTypeSymbol = Symbol.Intern("articulation-type");
    private static readonly Symbol BarLineInterfaceSymbol = Symbol.Intern("bar-line-interface");
    private static readonly Symbol SpanBarInterfaceSymbol = Symbol.Intern("span-bar-interface");
    private static readonly Symbol BarLineWordSymbol = Symbol.Intern("bar-line");
    private static readonly Symbol BreathSymbol = Symbol.Intern("breath");
    private static readonly Symbol ScriptsSymbol = Symbol.Intern("scripts");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol OutsideStaffPrioritySymbol
        = Symbol.Intern("outside-staff-priority");

    private static readonly Symbol ScriptPrioritySymbol = Symbol.Intern("script-priority");

    // SCM_UNDEFINED stand-in: a value that equal? never matches, so the first
    // evaluation always reports a change, exactly as upstream's SCM_UNDEFINED does.
    private static readonly object Undefined = new object();

    private StreamEvent _caesuraEvent;

    // an optional BreathingSign notating the caesura
    private Item _breathingSign;

    // optional CaesuraScripts
    private readonly List<Item> _scripts = new List<Item>();

    // any BarLine observed
    private Item _barLine;

    // the X-parent of the grobs this engraver has created (if there is one)
    private Item _xParent;
    private bool _observationsChanged;

    // cached caesuraType context property value
    private object _caesuraType = Nil.Instance;

    // cached caesuraTypeTransform context property value
    private object _caesuraTypeTransform = Nil.Instance;

    // the requested breathing sign type; #f for none
    private object _confBreathingSignType = Undefined;

    // symbol list of the requested script types
    private object _confScriptTypes = Undefined;

    // symbol list of allowed articulations
    private object _confArticTypes = Undefined;

    // symbol list describing the user-provided articulations
    private object _userArticTypes = Nil.Instance;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public CaesuraEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Caesura_engraver";

    /// <summary>Starts listening for caesura events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo("caesura-event", ListenCaesura);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    private void ListenCaesura(StreamEvent ev)
        => StreamEvent.AssignEventOnce(ref _caesuraEvent, ev);

    /// <summary>Evaluates the caesura configuration and makes the grobs.</summary>
    public override void ProcessMusic()
    {
        if (_caesuraEvent != null)
        {
            _caesuraType = GetProperty(CaesuraTypeSymbol);
            _caesuraTypeTransform = GetProperty(CaesuraTypeTransformSymbol);

            // Form a symbol list describing the user-provided articulations.
            {
                List<object> types = new List<object>();
                object cursor = _caesuraEvent.GetProperty(ArticulationsSymbol);
                while (cursor is Pair pair)
                {
                    if (pair.Car is StreamEvent art
                        && art.GetProperty(ArticulationTypeSymbol) is Symbol articType)
                    {
                        types.Add(articType);
                    }

                    cursor = pair.Cdr;
                }

                _userArticTypes = Pair.ListFrom(types);
            }

            bool typeChanged = EvaluateCaesuraType();
            if (typeChanged)
            {
                MakeOrRemakeGrobs();
            }
        }
    }

    /// <summary>Re-evaluates the configuration when a bar line appears.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!(info.Grob is Item item) || !item.HasInterface(BarLineInterfaceSymbol))
        {
            return;
        }

        if (_caesuraEvent == null || _barLine != null)
        {
            return;
        }

        if (!item.HasInterface(SpanBarInterfaceSymbol))
        {
            _barLine = item;
            _observationsChanged = true;
        }
    }

    /// <summary>Remakes the grobs when the observations changed them.</summary>
    public override void ProcessAcknowledged()
    {
        if (_caesuraEvent == null)
        {
            return;
        }

        bool typeChanged = _observationsChanged && EvaluateCaesuraType();
        _observationsChanged = false;

        if (typeChanged || !ReferenceEquals(_xParent, ChooseXParent()))
        {
            MakeOrRemakeGrobs();
        }
    }

    private bool EvaluateCaesuraType()
    {
        object props = _caesuraType;

        // Add the user's articulations to the caesuraType value.
        props = new Pair(new Pair(ArticulationsSymbol, _userArticTypes), props);

        // Pass caesuraType through the transform function, if it is set.
        if (SchemeUtilities.IsProcedure(_caesuraTypeTransform))
        {
            object observations = _barLine != null
                ? Pair.List(BarLineWordSymbol)
                : Nil.Instance;

            props = SchemeUtilities.CallCallback(
                _caesuraTypeTransform, Context, props, observations);
        }

        object bType = AssqRef(props, BreathSymbol);
        Pair scriptsEntry = SchemeUtilities.Assq(ScriptsSymbol, props);
        object sTypes = scriptsEntry != null ? scriptsEntry.Cdr : Nil.Instance;
        object aTypes = AssqRef(props, ArticulationsSymbol);

        if (LyIsEqual(bType, _confBreathingSignType)
            && LyIsEqual(sTypes, _confScriptTypes)
            && LyIsEqual(aTypes, _confArticTypes))
        {
            return false; // no change
        }

        _confBreathingSignType = bType;
        _confScriptTypes = sTypes;
        _confArticTypes = aTypes;
        return true;
    }

    private Item ChooseXParent()
    {
        // more important because we created it
        if (_breathingSign != null)
        {
            return _breathingSign;
        }

        return _barLine;
    }

    private void MakeOrRemakeGrobs()
    {
        StreamEvent ev = _caesuraEvent;

        object bType = _confBreathingSignType;
        object sTypes = _confScriptTypes;
        object aTypes = _confArticTypes;

        // Discard the old grobs.

        _xParent = null;

        if (_breathingSign != null)
        {
            _breathingSign.Suicide();
            _breathingSign = null;
        }

        foreach (Item scr in _scripts)
        {
            scr.Suicide();
        }

        _scripts.Clear();

        // Create new grobs.

        if (bType is Symbol)
        {
            _breathingSign = MakeItem("BreathingSign", ev);
            BreathingSign.SetBreathProperties(_breathingSign, Context, bType);
        }

        _xParent = ChooseXParent();

        int scrIndex = 0; // count scripts for make_script_from_event

        object typeCursor = sTypes;
        while (typeCursor is Pair typePair)
        {
            object sType = typePair.Car;
            typeCursor = typePair.Cdr;

            if (!(sType is Symbol scriptType))
            {
                TranslatorSchemeHelpers.EventProgrammingError(
                    ev, "caesura script type must be a symbol: " + Printer.Write(sType));
                continue;
            }

            Item scr = MakeItem("CaesuraScript", ev);
            MakeScriptFromEvent(scr, scriptType, scrIndex);
            if (_xParent != null)
            {
                scr.XParent = _xParent;
            }

            _scripts.Add(scr);
            ++scrIndex;
        }

        object artCursor = ev.GetProperty(ArticulationsSymbol);
        while (artCursor is Pair artPair)
        {
            object artObject = artPair.Car;
            artCursor = artPair.Cdr;

            if (!(artObject is StreamEvent art)) // programming_error?
            {
                continue;
            }

            object aType = art.GetProperty(ArticulationTypeSymbol);

            // caesuraTypeTransform may have narrowed the set of acceptable
            // articulations.  Check whether this one is allowed.
            if (!SchemeUtilities.Memq(aType, aTypes))
            {
                continue;
            }

            Item scr = MakeItem("CaesuraScript", art);
            if (aType is Symbol articSymbol)
            {
                MakeScriptFromEvent(scr, articSymbol, scrIndex);
            }

            if (_xParent != null)
            {
                scr.XParent = _xParent;
            }

            // The event may override the default direction of the script.
            object dir = art.GetProperty(DirectionSymbol);
            if (SchemeConvert.IsNumber(dir)
                && SchemeConvert.ToLong(dir, "direction") != 0)
            {
                scr.SetProperty(DirectionSymbol, dir);
            }

            _scripts.Add(scr);
            ++scrIndex;
        }
    }

    /// <summary>Ranks the scripts and forgets the timestep's state.</summary>
    public override void StopTranslationTimestep()
    {
        // Script_column will set the outside-staff-priority of every script after
        // the first, so we set the first.
        if (_scripts.Count > 0)
        {
            // To determine which articulation will be first, we must sort them as
            // Script_column will.
            // TODO: There must be a better way -- and this still doesn't match
            // Script_column if some scripts are UP and some are DOWN, though that
            // should not be a problem for traditional caesura engraving.
            //
            // Script_interface::script_priority_less lives in script-interface.cc; the
            // comparator below is its two-line body, copied before that port landed.
            // ⚠ ScriptInterface.ScriptPriorityLess exists now; deduplicating this
            // copy has not been re-measured. Recorded under FINDINGS.
            List<(Item Script, int Order)> keyed = new List<(Item, int)>();
            for (int i = 0; i < _scripts.Count; i++)
            {
                keyed.Add((_scripts[i], i));
            }

            keyed.Sort((a, b) =>
            {
                long pa = TranslatorSchemeHelpers.ToLong(a.Script.GetProperty(ScriptPrioritySymbol), 0);
                long pb = TranslatorSchemeHelpers.ToLong(b.Script.GetProperty(ScriptPrioritySymbol), 0);
                return pa != pb ? pa.CompareTo(pb) : a.Order.CompareTo(b.Order);
            });

            _scripts.Clear();
            foreach ((Item Script, int Order) entry in keyed)
            {
                _scripts.Add(entry.Script);
            }

            _scripts[0].SetProperty(OutsideStaffPrioritySymbol, 0L);
        }

        _caesuraEvent = null;

        _breathingSign = null;
        _scripts.Clear();
        _barLine = null;
        _xParent = null;
        _observationsChanged = false;

        _caesuraType = Nil.Instance;
        _caesuraTypeTransform = Nil.Instance;

        _confBreathingSignType = Undefined;
        _confScriptTypes = Undefined;
        _confArticTypes = Undefined;

        _userArticTypes = Nil.Instance;
    }

    // make_script_from_event lives in lily/script-interface.cc. ⚠ This seam predates
    // its port (the function now lives with the script engravers) and has not been
    // re-routed or re-measured: a CaesuraScript is created but not configured from
    // scriptDefinitions, and the gap is reported LOUDLY on every occurrence rather
    // than silently producing a bare grob.
    private static void MakeScriptFromEvent(Item script, Symbol type, int index)
    {
        Warn.ProgrammingError(
            "Caesura_engraver: make_script_from_event (lily/script-interface.cc) is "
            + "not routed here; CaesuraScript `" + type.Name + "' left unconfigured");
    }

    private static object AssqRef(object alist, Symbol key)
    {
        Pair entry = SchemeUtilities.Assq(key, alist);
        return entry != null ? entry.Cdr : false;
    }

    // ly_is_equal, with the reference-never-equal semantics upstream's SCM_UNDEFINED
    // sentinel relies on.
    private bool LyIsEqual(object a, object b)
    {
        if (ReferenceEquals(a, Undefined) || ReferenceEquals(b, Undefined))
        {
            return ReferenceEquals(a, b);
        }

        return CorePrimitives.SchemeEqual(a, b);
    }
}
