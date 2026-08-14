/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2005--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/pitched-trill-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Prints the bracketed auxiliary note head after a note head with a pitched trill —
/// <c>\pitchedTrill c'4 \startTrillSpan d'</c>.
/// <para>
/// The accidental decision reuses the <c>localAlterations</c> record the
/// <see cref="AccidentalEngraver"/> maintains: the auxiliary head prints its accidental
/// unless the very same alteration is already in force in this measure. Everything
/// heard in the timestep — heads, dots, stems, flags — becomes side support, so the
/// bracket clears the music it annotates.
/// </para>
/// </summary>
public class PitchedTrillEngraver : Engraver
{
    private static readonly Symbol PitchSymbol = Symbol.Intern("pitch");
    private static readonly Symbol SpanDirectionSymbol = Symbol.Intern("span-direction");
    private static readonly Symbol TrillSpanEventSymbol = Symbol.Intern("trill-span-event");
    private static readonly Symbol LocalAlterationsSymbol = Symbol.Intern("localAlterations");
    private static readonly Symbol ForceAccidentalSymbol = Symbol.Intern("force-accidental");
    private static readonly Symbol MiddleCPositionSymbol = Symbol.Intern("middleCPosition");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");
    private static readonly Symbol AlterationSymbol = Symbol.Intern("alteration");
    private static readonly Symbol AccidentalGrobSymbol = Symbol.Intern("accidental-grob");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol NoteHeadInterface = Symbol.Intern("note-head-interface");
    private static readonly Symbol DotsInterface = Symbol.Intern("dots-interface");
    private static readonly Symbol StemInterface = Symbol.Intern("stem-interface");
    private static readonly Symbol FlagInterface = Symbol.Intern("flag-interface");
    private static readonly Symbol TrillSpannerInterface = Symbol.Intern("trill-spanner-interface");

    private Item _trillHead;
    private Item _trillGroup;
    private Item _trillAccidental;
    private Item _trillParentheses;

    private readonly List<Grob> _heads = new List<Grob>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public PitchedTrillEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Pitched_trill_engraver";

    /// <summary>
    /// Collects supports (heads, dots, stems, flags) and reacts to the start of a
    /// pitched trill spanner.
    /// </summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        Grob grob = info.Grob;
        if (grob == null)
        {
            return;
        }

        if (grob.HasInterface(NoteHeadInterface)
            || grob.HasInterface(DotsInterface)
            || grob.HasInterface(StemInterface)
            || grob.HasInterface(FlagInterface))
        {
            _heads.Add(grob);
        }

        if (grob.HasInterface(TrillSpannerInterface))
        {
            AcknowledgeTrillSpanner(info);
        }
    }

    private void AcknowledgeTrillSpanner(GrobInfo info)
    {
        StreamEvent ev = info.EventCause;
        if (ev != null && ev.IsInEventClass(TrillSpanEventSymbol)
            && ReadDirection(ev.GetProperty(SpanDirectionSymbol)) == Direction.Negative
            && ev.GetProperty(PitchSymbol) is Pitch)
        {
            MakeTrill(ev);
        }
    }

    private void MakeTrill(StreamEvent ev)
    {
        object scmPitch = ev.GetProperty(PitchSymbol);
        Pitch p = (Pitch)scmPitch;

        object keysig = GetProperty(LocalAlterationsSymbol);

        object key = new Pair((long)p.Octave, (long)p.NoteName);

        int bn = MeasureCounting.MeasureNumber(Context);

        Pair handle = AssocEqual(key, keysig);
        if (handle != null)
        {
            bool sameBar = bn == RobustInt(Caddr(handle), 0);
            bool sameAlt = p.Alteration == RobustRational(Cadr(handle), Rational.Zero);

            if (!sameBar || (sameBar && !sameAlt))
            {
                handle = null;
            }
        }

        bool printAcc = handle == null
                        || p.Alteration == Rational.Zero
                        || SchemeUtilities.ToBool(ev.GetProperty(ForceAccidentalSymbol));

        if (_trillHead != null)
        {
            Warn.ProgrammingError("already have a trill head.");
            _trillHead = null;
        }

        _trillHead = MakeItem("TrillPitchHead", ev);
        object c0scm = GetProperty(MiddleCPositionSymbol);

        int c0 = SchemeConvert.IsNumber(c0scm)
            ? SchemeConvert.ToInt(c0scm, "middleCPosition")
            : 0;

        _trillHead.SetProperty(
            StaffPositionSymbol, (long)(((Pitch)scmPitch).Steps() + c0));

        _trillGroup = MakeItem("TrillPitchGroup", ev);

        AxisGroupInterface.AddElement(_trillGroup, _trillHead);

        if (printAcc)
        {
            _trillAccidental = MakeItem("TrillPitchAccidental", ev);

            // fixme: naming -> alterations
            _trillAccidental.SetProperty(
                AlterationSymbol, SchemeConvert.FromRational(p.Alteration));
            SidePositionInterface.AddSupport(_trillAccidental, _trillHead);

            _trillHead.SetObject(AccidentalGrobSymbol, _trillAccidental);
            _trillAccidental.YParent = _trillHead;
            AxisGroupInterface.AddElement(_trillGroup, _trillAccidental);
        }

        _trillParentheses = MakeItem("TrillPitchParentheses", _trillHead);
        PointerGroupInterface.AddGrob(_trillParentheses, ElementsSymbol, _trillHead);
        _trillParentheses.XParent = _trillHead;
        _trillParentheses.YParent = _trillHead;
        AxisGroupInterface.AddElement(_trillGroup, _trillParentheses);
    }

    /// <summary>Hands the trill group its supports and forgets the timestep.</summary>
    public override void StopTranslationTimestep()
    {
        if (_trillGroup != null)
        {
            for (int i = 0; i < _heads.Count; i++)
            {
                SidePositionInterface.AddSupport(_trillGroup, _heads[i]);
            }
        }

        _heads.Clear();
        _trillHead = null;
        _trillGroup = null;
        _trillAccidental = null;
        _trillParentheses = null;
    }

    private static Direction ReadDirection(object value)
        => SchemeConvert.IsNumber(value)
            ? new Direction(SchemeConvert.ToLong(value, "span-direction"))
            : Direction.Zero;

    private static object Cadr(Pair pair) => pair.Cdr is Pair rest ? rest.Car : Nil.Instance;

    private static object Caddr(Pair pair)
        => pair.Cdr is Pair rest && rest.Cdr is Pair third ? third.Car : Nil.Instance;

    private static int RobustInt(object value, int fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToInt(value, "pitched trill")
            : fallback;

    private static Rational RobustRational(object value, Rational fallback)
    {
        if (value is Rational rational)
        {
            return rational;
        }

        if (SchemeConvert.IsNumber(value) && !(value is double))
        {
            return SchemeConvert.ToRational(value, "pitched trill");
        }

        return fallback;
    }

    private static Pair AssocEqual(object key, object alist)
    {
        object cursor = alist;
        while (cursor is Pair pair)
        {
            if (pair.Car is Pair entry && SchemeUtilities.IsEqual(entry.Car, key))
            {
                return entry;
            }

            cursor = pair.Cdr;
        }

        return null;
    }
}
