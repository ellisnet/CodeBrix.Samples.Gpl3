/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2002--2026 Juergen Reuter <reuter@ipd.uka.de>

  Han-Wen Nienhuys <hanwen@xs4all.nl

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

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/ambitus-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Creates an ambitus: the lowest-to-highest range of a part, drawn as two note heads
/// joined by a line at the front of the staff.
/// <para>
/// The grobs are all made in the FIRST timestep and filled in at
/// <see cref="FinalizeTranslation"/>, once the whole part has been heard — which is why
/// the middle-C reference and the key signature are snapshotted at the first timestep
/// (<see cref="StopTranslationTimestep"/>): the ambitus prints at the front of the
/// piece, under the clef and key that are in force THERE, whatever changes later.
/// </para>
/// </summary>
public class AmbitusEngraver : Engraver
{
    private static readonly Symbol MiddleCPositionSymbol = Symbol.Intern("middleCPosition");
    private static readonly Symbol MiddleCCuePositionSymbol = Symbol.Intern("middleCCuePosition");
    private static readonly Symbol MiddleCClefPositionSymbol = Symbol.Intern("middleCClefPosition");
    private static readonly Symbol MiddleCOffsetSymbol = Symbol.Intern("middleCOffset");
    private static readonly Symbol OttavaStartNowSymbol = Symbol.Intern("ottavaStartNow");
    private static readonly Symbol KeyAlterationsSymbol = Symbol.Intern("keyAlterations");
    private static readonly Symbol StaffLineLayoutFunctionSymbol
        = Symbol.Intern("staffLineLayoutFunction");

    private static readonly Symbol PitchSymbol = Symbol.Intern("pitch");
    private static readonly Symbol NoteEventSymbol = Symbol.Intern("note-event");
    private static readonly Symbol IgnoreAmbitusSymbol = Symbol.Intern("ignore-ambitus");
    private static readonly Symbol AccidentalGrobSymbol = Symbol.Intern("accidental-grob");
    private static readonly Symbol CauseSymbol = Symbol.Intern("cause");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");
    private static readonly Symbol AlterationSymbol = Symbol.Intern("alteration");
    private static readonly Symbol NoteHeadsSymbol = Symbol.Intern("note-heads");
    private static readonly Symbol NoteHeadInterface = Symbol.Intern("note-head-interface");

    private Item _ambitus;
    private Item _group;
    private DrulArray<Item> _heads;
    private DrulArray<Item> _accidentals;
    private DrulArray<StreamEvent> _causes;
    private readonly PitchInterval _pitchInterval = new PitchInterval();
    private bool _isTypeset;
    private int _startC0;
    private object _startKeySig = Nil.Instance;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public AmbitusEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Ambitus_engraver";

    private void CreateAmbitus()
    {
        _ambitus = MakeItem("AmbitusLine", Nil.Instance);
        _group = MakeItem("Ambitus", Nil.Instance);
        foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
        {
            _heads[d] = MakeItem("AmbitusNoteHead", Nil.Instance);
            _accidentals[d] = MakeItem("AmbitusAccidental", Nil.Instance);
            _accidentals[d].YParent = _heads[d];
            _heads[d].SetObject(AccidentalGrobSymbol, _accidentals[d]);
            AxisGroupInterface.AddElement(_group, _heads[d]);
            AxisGroupInterface.AddElement(_group, _accidentals[d]);
        }

        _ambitus.XParent = _heads[Direction.Negative];
        AxisGroupInterface.AddElement(_group, _ambitus);

        _isTypeset = false;
    }

    /// <summary>
    /// Ensures that the ambitus is created in the very first timestep.
    /// </summary>
    public override void ProcessMusic()
    {
        if (_ambitus == null)
        {
            CreateAmbitus();
        }
    }

    /// <summary>
    /// Snapshots, once, the pitch reference and key signature in force when the
    /// ambitus was made.
    /// </summary>
    public override void StopTranslationTimestep()
    {
        if (_ambitus != null && !_isTypeset)
        {
            object cPos = GetProperty(MiddleCPositionSymbol);
            object cuePos = GetProperty(MiddleCCuePositionSymbol);

            /*
             * \ottava reads middleCClefPosition and overrides
             * middleCOffset and middleCPosition ignoring previously
             * set values. Therefore
             *  1. \ottava is incompatible with non-default offset and
             *     position values (is this a bug? TODO)
             *  2. we don't need to read these values and revert the
             *     changes \ottava made but we can just read the
             *     clef position.
             */
            if (GetProperty(OttavaStartNowSymbol) is bool ottava && ottava)
            {
                _startC0 = RobustInt(GetProperty(MiddleCClefPositionSymbol), 0);
            }
            else if (IsInteger(cPos) && !IsInteger(cuePos))
            {
                _startC0 = SchemeConvert.ToInt(cPos, "middleCPosition");
            }
            else
            {
                int clefPos = RobustInt(GetProperty(MiddleCClefPositionSymbol), 0);
                int offset = RobustInt(GetProperty(MiddleCOffsetSymbol), 0);
                _startC0 = clefPos + offset;
            }

            _startKeySig = GetProperty(KeyAlterationsSymbol);

            _isTypeset = true;
        }
    }

    /// <summary>Widens the range by every real note head heard.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!info.Grob.HasInterface(NoteHeadInterface))
        {
            return;
        }

        StreamEvent nr = info.EventCause;
        if (nr != null && nr.IsInEventClass(NoteEventSymbol)
            && !SchemeUtilities.ToBool(info.Grob.GetProperty(IgnoreAmbitusSymbol)))
        {
            object p = nr.GetProperty(PitchSymbol);

            /*
              If the engraver is added to a percussion context,
              filter out unpitched note heads.
            */
            if (!(p is Pitch pitch))
            {
                return;
            }

            DrulArray<bool> expands = _pitchInterval.AddPoint(pitch);
            if (expands.Positive)
            {
                _causes.Positive = nr;
            }

            if (expands.Negative)
            {
                _causes.Negative = nr;
            }
        }
    }

    /// <summary>
    /// Positions the two heads, prints or suppresses their accidentals against the
    /// snapshotted key signature, and packs everything — or removes it all when the
    /// part had no pitched note.
    /// </summary>
    public override void FinalizeTranslation()
    {
        if (_ambitus != null && !_pitchInterval.IsEmpty())
        {
            Grob accidentalPlacement
                = MakeItem("AccidentalPlacement", _accidentals[Direction.Negative]);

            object layoutProc = GetProperty(StaffLineLayoutFunctionSymbol);

            foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
            {
                Pitch p = _pitchInterval[d];

                int pos;
                if (SchemeUtilities.IsProcedure(layoutProc))
                {
                    object result = SchemeUtilities.CallCallback(layoutProc, p);
                    pos = SchemeConvert.IsNumber(result)
                        ? SchemeConvert.ToInt(result, "staffLineLayoutFunction")
                        : p.Steps();
                }
                else
                {
                    pos = p.Steps();
                }

                _heads[d].SetProperty(CauseSymbol, _causes[d]);
                _heads[d].SetProperty(StaffPositionSymbol, (long)(_startC0 + pos));

                Pair handle = AssocEqual(
                    new Pair((long)p.Octave, (long)p.NoteName), _startKeySig);

                if (handle == null)
                {
                    handle = AssocEqual((long)p.NoteName, _startKeySig);
                }

                Rational sigAlter = handle != null
                    ? RobustRational(handle.Cdr, Rational.Zero)
                    : Rational.Zero;

                Pitch other = _pitchInterval[-d];

                if (sigAlter == p.Alteration
                    && !((p.Steps() == other.Steps())
                         && (p.Alteration != other.Alteration)))
                {
                    _accidentals[d].Suicide();
                    _heads[d].SetObject(AccidentalGrobSymbol, Nil.Instance);
                }
                else
                {
                    _accidentals[d].SetProperty(
                        AlterationSymbol, SchemeConvert.FromRational(p.Alteration));
                }

                SeparationItem.AddConditionalItem(_heads[d], accidentalPlacement);
                AccidentalPlacement.AddAccidental(
                    accidentalPlacement, _accidentals[d], false, null);
                PointerGroupInterface.AddGrob(_ambitus, NoteHeadsSymbol, _heads[d]);
            }

            AxisGroupInterface.AddElement(_group, accidentalPlacement);
        }
        else
        {
            foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
            {
                _accidentals[d].Suicide();
                _heads[d].Suicide();
            }

            _ambitus.Suicide();
        }
    }

    private static bool IsInteger(object value) => value is long || value is int;

    private static int RobustInt(object value, int fallback)
        => SchemeConvert.IsNumber(value) ? SchemeConvert.ToInt(value, "ambitus") : fallback;

    private static Rational RobustRational(object value, Rational fallback)
    {
        if (value is Rational rational)
        {
            return rational;
        }

        if (SchemeConvert.IsNumber(value) && !(value is double))
        {
            return SchemeConvert.ToRational(value, "ambitus");
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
