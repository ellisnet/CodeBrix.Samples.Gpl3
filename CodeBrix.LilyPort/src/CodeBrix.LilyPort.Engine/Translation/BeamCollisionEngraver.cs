/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2011--2026 Mike Solomon <mike@mikesolomon.org>

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
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/beam-collision-engraver.cc;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port:
//   - the contexts_ set and derived_mark () are dropped: they exist only to keep
//     Contexts alive across Guile's collector, which the managed one does anyway
//   - std::sort is unstable; List.Sort is too, and grob_less compares only the
//     starting rank, so equal-ranked grobs may land in either order in both. The
//     ORDER of covered-grobs does not reach the page — the beam scorer takes their
//     union — so this is not a parity surface. Recorded in PORT-COVERAGE.

/// <summary>Helps beams avoid colliding with notes and clefs in other voices.</summary>
public sealed class BeamCollisionEngraver : Engraver
{
    private static readonly Symbol CollisionInterfacesSymbol
        = Symbol.Intern("collision-interfaces");
    private static readonly Symbol CollisionVoiceOnlySymbol
        = Symbol.Intern("collision-voice-only");
    private static readonly Symbol CoveredGrobsSymbol = Symbol.Intern("covered-grobs");
    private static readonly Symbol NormalStemsSymbol = Symbol.Intern("normal-stems");
    private static readonly Symbol BeamSymbol = Symbol.Intern("beam");
    private static readonly Symbol StemSymbol = Symbol.Intern("stem");
    private static readonly Symbol InlineAccidentalInterfaceSymbol
        = Symbol.Intern("inline-accidental-interface");

    private static readonly Symbol BeamInterfaceSymbol = Symbol.Intern("beam-interface");
    private static readonly Symbol StemInterfaceSymbol = Symbol.Intern("stem-interface");
    private static readonly Symbol NoteHeadInterfaceSymbol = Symbol.Intern("note-head-interface");
    private static readonly Symbol AccidentalInterfaceSymbol
        = Symbol.Intern("accidental-interface");
    private static readonly Symbol ClefInterfaceSymbol = Symbol.Intern("clef-interface");
    private static readonly Symbol ClefModifierInterfaceSymbol
        = Symbol.Intern("clef-modifier-interface");
    private static readonly Symbol KeySignatureInterfaceSymbol
        = Symbol.Intern("key-signature-interface");
    private static readonly Symbol TimeSignatureInterfaceSymbol
        = Symbol.Intern("time-signature-interface");
    private static readonly Symbol FlagInterfaceSymbol = Symbol.Intern("flag-interface");

    private readonly List<GrobWithContext> _beams = new List<GrobWithContext>();
    private readonly List<GrobWithContext> _coveredGrobs = new List<GrobWithContext>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public BeamCollisionEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Beam_collision_engraver";

    /// <summary>Collects every grob a beam might have to avoid.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        Grob g = info.Grob;

        if (g.HasInterface(NoteHeadInterfaceSymbol)
            || g.HasInterface(StemInterfaceSymbol)
            || g.HasInterface(ClefInterfaceSymbol)
            || g.HasInterface(KeySignatureInterfaceSymbol)
            || g.HasInterface(ClefModifierInterfaceSymbol)
            || g.HasInterface(TimeSignatureInterfaceSymbol)
            || g.HasInterface(FlagInterfaceSymbol))
        {
            _coveredGrobs.Add(CreateGrobWithContext(info));
        }
        else if (g.HasInterface(AccidentalInterfaceSymbol))
        {
            if (g.HasInterface(InlineAccidentalInterfaceSymbol))
            {
                _coveredGrobs.Add(CreateGrobWithContext(info));
            }
        }

        if (g.HasInterface(BeamInterfaceSymbol))
        {
            GrobWithContext gc = CreateGrobWithContext(info);
            _beams.Add(gc);
            _coveredGrobs.Add(gc);
        }
    }

    /// <summary>Hands each beam the grobs that overlap it horizontally and vertically.</summary>
    public override void FinalizeTranslation()
    {
        base.FinalizeTranslation();

        if (_coveredGrobs.Count == 0)
        {
            return;
        }

        _coveredGrobs.Sort(GrobLess);
        _beams.Sort(GrobLess);
        int start = 0;

        for (int i = 0; i < _beams.Count; i++)
        {
            Grob beamGrob = _beams[i].Grob;

            IReadOnlyList<Grob> stems
                = PointerGroupInterface.ExtractGrobSet(beamGrob, NormalStemsSymbol);
            Slice verticalSpan = Slice.Empty;
            for (int j = 0; j < stems.Count; j++)
            {
                int vag = SpanBarVerticalOrder.GetVerticalAxisGroupIndex(stems[j]);
                if (vag >= 0)
                {
                    verticalSpan.AddPoint(vag);
                }
            }

            Context beamContext = _beams[i].Context;

            Slice beamSpannedRank = beamGrob.SpannedColumnRankInterval();

            // Start considering grobs at the first grob whose
            // end falls at or after the beam's beginning.
            while (start < _coveredGrobs.Count
                   && _coveredGrobs[start].Grob.SpannedColumnRankInterval()[Direction.Positive]
                      < beamSpannedRank[Direction.Negative])
            {
                start++;
            }

            // Stop when the grob's beginning comes after the beam's end.
            for (int j = start; j < _coveredGrobs.Count; j++)
            {
                Grob coveredGrob = _coveredGrobs[j].Grob;
                int vag = SpanBarVerticalOrder.GetVerticalAxisGroupIndex(coveredGrob);
                if (!verticalSpan.Contains(vag))
                {
                    continue;
                }

                Context coveredGrobContext = _coveredGrobs[j].Context;

                Slice coveredGrobSpannedRank = coveredGrob.SpannedColumnRankInterval();

                if (coveredGrobSpannedRank[Direction.Negative]
                    > beamSpannedRank[Direction.Positive])
                {
                    break;
                }

                /*
                   Only consider grobs whose end falls at or after the beam's beginning.
                   If the grob is a beam, it cannot start before beams_[i].
                   Also, if the user wants to check for collisions only in the beam's voice,
                   then make sure the beam and the covered_grob are in the same voice.
                */
                if ((coveredGrobSpannedRank[Direction.Positive]
                     >= beamSpannedRank[Direction.Negative])
                    && !(SchemeUtilities.ToBool(beamGrob.GetProperty(CollisionVoiceOnlySymbol))
                         && !ReferenceEquals(coveredGrobContext, beamContext))
                    && !(coveredGrob.HasInterface(BeamInterfaceSymbol)
                         && (coveredGrobSpannedRank[Direction.Negative]
                             <= beamSpannedRank[Direction.Negative]))
                    && CoveredGrobHasInterface(coveredGrob, beamGrob))
                {
                    // Do not consider note heads attached to the beam.
                    if (coveredGrob.HasInterface(StemInterfaceSymbol))
                    {
                        if (coveredGrob.GetObject(BeamSymbol) is Grob)
                        {
                            continue;
                        }
                    }

                    if (coveredGrob.GetObject(StemSymbol) is Grob coveredStem)
                    {
                        if (coveredStem.GetObject(BeamSymbol) is Grob attachedBeam)
                        {
                            if (ReferenceEquals(attachedBeam, beamGrob))
                            {
                                continue;
                            }
                        }
                    }

                    PointerGroupInterface.AddGrob(beamGrob, CoveredGrobsSymbol, coveredGrob);
                }
            }
        }
    }

    private static bool CoveredGrobHasInterface(Grob coveredGrob, Grob beam)
    {
        object interfaces = beam.GetProperty(CollisionInterfacesSymbol);

        for (object l = interfaces; l is Pair pair; l = pair.Cdr)
        {
            if (pair.Car is Symbol name && coveredGrob.HasInterface(name))
            {
                return true;
            }
        }

        return false;
    }

    private GrobWithContext CreateGrobWithContext(GrobInfo i)
        => new GrobWithContext(i.Grob, i.OriginEngraver?.Context);

    private static int GrobLess(GrobWithContext a, GrobWithContext b)
    {
        if (Grob.Less(a.Grob, b.Grob))
        {
            return -1;
        }

        return Grob.Less(b.Grob, a.Grob) ? 1 : 0;
    }

    private readonly struct GrobWithContext
    {
        internal GrobWithContext(Grob grob, Context context)
        {
            Grob = grob;
            Context = context;
        }

        internal Grob Grob { get; }

        internal Context Context { get; }
    }
}
