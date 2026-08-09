/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2002--2026 Juergen Reuter <reuter@ipd.uka.de>,
  Pal Benko <benkop@freestart.hu>

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
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/mensural-ligature-engraver.cc;

// Modified by Jeremy Ellis on 2026-08-09 as part of the CodeBrix port:
//   - propagate_properties and fold_up_primitives return the minimum length instead of
//     writing through a Real&; build_ligature accumulates the two the same way its
//     upstream counterpart does, because fold_up_primitives ADDS to what
//     propagate_properties left.

/*
 * TODO (upstream): accidentals are aligned with the first note; they must appear ahead.
 *
 * TODO (upstream): prohibit ligatures having notes differing only in accidentals
 * (like \[ a\breve g as \])
 *
 * TODO (upstream): do something with multiple voices within a ligature.
 *
 * TODO (upstream): enhance robustness: in case of an invalid ligature, automatically
 * break the ligature into smaller, valid pieces.
 */

/// <summary>
/// Glues special ligature heads together into the white-mensural ligature shape.
/// </summary>
/// <remarks>
/// <para>
/// The work is in <see cref="TransformHeads"/>, which decides what each head IS —
/// brevis, maxima, half of an obliqua, with which stems — from its duration, its pitch
/// relative to its neighbours, and the tweaks the user wrote. Everything it decides goes
/// into the <c>primitive</c> property, which <see cref="MensuralLigature"/> then draws.
/// </para>
/// <para>
/// A <c>MensuralLigature</c> grob is a bunch of <c>NoteHead</c> grobs glued together. It
/// does not make sense to change properties like <c>thickness</c> or <c>flexa-width</c>
/// from one head to the next within a ligature — that would totally screw up alignment —
/// and some of them are specific to the ligature rather than to a note head. So the user
/// controls them on the ligature grob, and <c>PropagateProperties</c> copies them down.
/// </para>
/// </remarks>
public sealed class MensuralLigatureEngraver : CoherentLigatureEngraver
{
    private static readonly Symbol AddJoinSymbol = Symbol.Intern("add-join");
    private static readonly Symbol DeltaPositionSymbol = Symbol.Intern("delta-position");
    private static readonly Symbol DotStencilSymbol = Symbol.Intern("dot-stencil");
    private static readonly Symbol FlexaIntervalSymbol = Symbol.Intern("flexa-interval");
    private static readonly Symbol FlexaWidthSymbol = Symbol.Intern("flexa-width");
    private static readonly Symbol HeadWidthSymbol = Symbol.Intern("head-width");
    private static readonly Symbol LigatureFlexaSymbol = Symbol.Intern("ligature-flexa");
    private static readonly Symbol LigaturePesSymbol = Symbol.Intern("ligature-pes");
    private static readonly Symbol LeftDownStemSymbol = Symbol.Intern("left-down-stem");
    private static readonly Symbol LineThicknessSymbol = Symbol.Intern("line-thickness");
    private static readonly Symbol MinimumLengthSymbol = Symbol.Intern("minimum-length");
    private static readonly Symbol NoteEventSymbol = Symbol.Intern("note-event");
    private static readonly Symbol PitchSymbol = Symbol.Intern("pitch");
    private static readonly Symbol PrimitiveSymbol = Symbol.Intern("primitive");
    private static readonly Symbol RightDownStemSymbol = Symbol.Intern("right-down-stem");
    private static readonly Symbol RightUpStemSymbol = Symbol.Intern("right-up-stem");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");
    private static readonly Symbol ThicknessSymbol = Symbol.Intern("thickness");

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public MensuralLigatureEngraver(Context context)
        : base(context)
    {
        BrewLigaturePrimitiveProc
            = LookupBrewProc("ly:mensural-ligature::brew-ligature-primitive");
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Mensural_ligature_engraver";

    /// <summary>Makes the <c>MensuralLigature</c> spanner.</summary>
    /// <returns>The ligature spanner.</returns>
    protected override Spanner CreateLigatureSpanner()
        => MakeSpanner("MensuralLigature", Nil.Instance);

    /// <summary>
    /// Decides what each head is, distributes the ligature's properties down onto the
    /// heads, folds them into one column, and claims the horizontal room the shape needs.
    /// </summary>
    /// <param name="ligature">The ligature spanner.</param>
    /// <param name="primitives">The heads, in time order.</param>
    protected override void BuildLigature(Spanner ligature, IReadOnlyList<Item> primitives)
    {
        /*
          the X extent of the actual graphics representing the ligature;
          less space than that means collision
        */
        TransformHeads(primitives);
        double minLength = PropagateProperties(ligature, primitives);
        minLength = FoldUpPrimitives(primitives, minLength);

        if (SchemeConvert.ToDouble(ligature.GetProperty(MinimumLengthSymbol), 0.0) < minLength)
        {
            ligature.SetProperty(MinimumLengthSymbol, minLength);
        }
    }

    private static void TransformHeads(IReadOnlyList<Item> primitives)
    {
        if (primitives.Count == 0)
        {
            Warn.Warning("empty ligature");
        }

        int prevPitch = 0;
        int prevPrim = 0;
        bool atBeginning = true;

        // needed so that we can check whether
        // the previous note can be turned into a flexa
        bool prevBrevisShape = false;

        bool prevSemibrevis = false;
        Item prevPrimitive = null;

        for (int i = 0, s = primitives.Count; i < s; i++)
        {
            Item primitive = primitives[i];
            int durationLog = RhythmicHead.DurationLog(primitive);

            StreamEvent nr = primitive.EventCause();

            /*
              ugh. why not simply check for pitch?
            */
            if (nr == null || !nr.IsInEventClass(NoteEventSymbol))
            {
                Epg8Support.EventWarning(
                    nr, "cannot determine pitch of ligature primitive; skipping");
                atBeginning = true;
                continue;
            }

            int pitch = PitchStepsOf(nr);
            int prim = 0;
            bool isLast = i == s - 1;
            Item nextPrimitive = isLast ? null : primitives[i + 1];
            int nextPitch = isLast ? 0 : PitchStepsOf(nextPrimitive.EventCause());
            int nextDur = isLast ? 0 : RhythmicHead.DurationLog(nextPrimitive);

            if (!atBeginning && pitch == prevPitch)
            {
                Epg8Support.EventWarning(nr, "unison within ligature");
            }

            bool generalCase = true;
            bool makeFlexa = false;
            bool isBrevis = durationLog == -1;
            bool isLonga = durationLog == -2;

            if (durationLog < -3 // is this possible at all???
                || durationLog > 0)
            {
                Epg8Support.EventWarning(
                    nr,
                    "mensural ligature:"
                    + " duration none of maxima, longa, breve, or semibreve");
                prim = MensuralLigature.Invalid;
            }
            else if (atBeginning && isLast)
            {
                Epg8Support.EventWarning(nr, "single note ligature");
            }

            // check descending cases
            // 1. at start
            else if (atBeginning && nextPitch < pitch && (isBrevis || isLonga))
            {
                int leftStem = isBrevis ? MensuralLigature.Down : 0;
                prim = leftStem | MensuralLigature.Brevis;
            }

            // 2. at end
            else if (isLast && pitch < prevPitch)
            {
                // brevis; should form a flexa with the previous note
                if (isBrevis)
                {
                    if (prevBrevisShape)
                    {
                        makeFlexa = true;
                        generalCase = false;
                    }
                    else
                    {
                        /*
                          flexa impossible;
                          instead of refusal, add right stem to the previous note
                        */
                        prim = MensuralLigature.Brevis | MensuralLigature.Down;
                    }
                }

                // longa
                else if (isLonga && !prevSemibrevis)
                {
                    prim = MensuralLigature.Brevis;
                }

                // else fall through to regular case below
            }

            if (SchemeUtilities.ToBool(primitive.GetProperty(LigaturePesSymbol)))
            {
                if (isLast && durationLog < -1 && prevPitch + 1 < pitch)
                {
                    prim = (isLonga ? MensuralLigature.Brevis : MensuralLigature.Maxima)
                        | MensuralLigature.Pes;
                }
                else
                {
                    Epg8Support.EventWarning(
                        nr,
                        "only a final longa higher at least by a third "
                        + "than the previous note\n"
                        + "can be drawn pes-like");
                }
            }

            if (generalCase && (prim & MensuralLigature.Any) == 0)
            {
                int[] shape =
                {
                    MensuralLigature.Maxima,
                    MensuralLigature.Brevis | MensuralLigature.JoinDown,
                    MensuralLigature.Brevis,
                    MensuralLigature.Brevis,
                };

                prim = shape[durationLog + 3];
                if (prevSemibrevis)
                {
                    if (isBrevis)
                    {
                        Epg8Support.EventWarning(
                            nr, "single semibreve must not be followed by a breve");

                        /*
                          nevertheless show breve by a left down stem
                        */
                        prim |= MensuralLigature.Down;
                    }
                }
                else if (durationLog == 0)
                {
                    /*
                      semibreve pairs are denoted by an upward tail on the left
                      (theoretical sources require them at the beginning, but
                      an upward tail is not used for anything else in the middle,
                      and a few codices use it to denote semibreves,
                      see e.g. Fayrfax's Aeternae laudis lilium in the
                      Lambeth Choirbook: fol. 58v, start of the fourth line)
                    */
                    prim |= MensuralLigature.Up;
                }
            }

            if (SchemeUtilities.ToBool(primitive.GetProperty(LeftDownStemSymbol)))
            {
                if (isBrevis)
                {
                    prim = MensuralLigature.Brevis | MensuralLigature.Down;
                    makeFlexa = false;
                }
                else
                {
                    Epg8Support.EventWarning(nr, "only a breve can have downward left stem");
                }
            }

            /*
              check whether this and the previous note
              may/must/can't be turned into flexa:
              - there should be a previous note
              - both of the notes must be of brevis shape
                (i.e. can't be maxima or flexa)
              - there mustn't be a stem between the two notes
              - if the next note is an ultimate descending breve,
                this note must form a flexa with that, not with the previous one
              - if the next note is a high enough pes-like final longa
                and the previous note is higher than the current one,
                then the three note must form a porrectus (i.e. flexa here)
                to avoid collision of the previous and the next note
            */
            bool flexaPossible = !atBeginning && prevBrevisShape
                && (prim & (MensuralLigature.Stem | MensuralLigature.Maxima
                    | MensuralLigature.Invalid)) == 0;

            bool flexaRequested
                = SchemeUtilities.ToBool(primitive.GetProperty(LigatureFlexaSymbol));

            if (flexaRequested && !flexaPossible && (prim & MensuralLigature.Invalid) == 0)
            {
                if (atBeginning)
                {
                    Epg8Support.EventWarning(
                        nr, "tweak ligature-flexa between the two required notes");
                }
                else if (((prevPrim | prim) & MensuralLigature.Maxima) != 0)
                {
                    Epg8Support.EventWarning(nr, "maxima cannot form part of a flexa");
                }
                else
                {
                    Epg8Support.EventWarning(nr, "flexa cannot have stem in the middle");
                }
            }

            if (flexaPossible && i == s - 2)
            {
                /*
                  penultimate note:
                  final note is to be checked for the must/can't conditions
                */
                if (nextDur == -1)
                {
                    /*
                      breve: check whether descending
                    */
                    if (nextPitch < pitch)
                    {
                        flexaPossible = false;
                        if (flexaRequested)
                        {
                            Epg8Support.EventWarning(
                                nr,
                                "this note must form a flexa with the next note,\n"
                                + "not the previous one");
                        }
                    }
                }
                else if (nextDur == -2)
                {
                    /*
                      longa: check whether ascending high enough
                      and pes is requested
                    */
                    if (nextPitch < prevPitch + 4 && pitch < prevPitch
                        && pitch + 1 < nextPitch
                        && SchemeUtilities.ToBool(nextPrimitive.GetProperty(LigaturePesSymbol)))
                    {
                        makeFlexa = true;
                    }
                }
            }

            if (!makeFlexa)
            {
                makeFlexa = flexaRequested && flexaPossible;
            }

            if (makeFlexa
                && (prim & (MensuralLigature.Pes | MensuralLigature.Invalid)) == 0)
            {
                /*
                  turn the note with the previous one into a flexa
                */
                prevPrimitive.SetProperty(
                    PrimitiveSymbol,
                    SchemeConvert.FromInt(
                        MensuralLigature.FlexaBegin
                        | (SchemeConvert.ToInt(prevPrimitive.GetProperty(PrimitiveSymbol), 0)
                            & MensuralLigature.Stem)));

                prevPrimitive.SetProperty(
                    FlexaIntervalSymbol, SchemeConvert.FromInt(pitch - prevPitch));
                prim &= ~MensuralLigature.Brevis;
                prim |= MensuralLigature.FlexaEnd;
                primitive.SetProperty(
                    FlexaIntervalSymbol, SchemeConvert.FromInt(pitch - prevPitch));

                if (isLonga)
                {
                    /*
                      flexa ending in a longa:
                      right stem needed explicitly even at descending end of ligature
                    */
                    prim |= MensuralLigature.JoinDown;
                }
            }

            if (SchemeUtilities.ToBool(primitive.GetProperty(RightDownStemSymbol)))
            {
                if (durationLog < -1)
                {
                    prim |= MensuralLigature.JoinDown;
                }
                else
                {
                    Epg8Support.EventWarning(nr, "only longae and maximae may have right stem");
                }
            }

            if (SchemeUtilities.ToBool(primitive.GetProperty(RightUpStemSymbol)))
            {
                if (durationLog < -1)
                {
                    if (i + 2 < s && nextDur > -2)
                    {
                        Epg8Support.EventWarning(
                            nr,
                            "in the middle of the ligature an upward stem\n"
                            + "belongs more often to the next note");
                    }

                    prim &= ~MensuralLigature.JoinDown;
                    prim |= MensuralLigature.JoinUp;
                }
                else
                {
                    Epg8Support.EventWarning(nr, "only longae and maximae may have right stem");
                }
            }

            // join_primitives replacement
            if (!(atBeginning || makeFlexa))
            {
                prevPrimitive.SetProperty(AddJoinSymbol, true);
            }

            atBeginning = false;
            prevPrimitive = primitive;
            prevPitch = pitch;
            prevPrim = prim;
            primitive.SetProperty(PrimitiveSymbol, SchemeConvert.FromInt(prim));
            prevBrevisShape
                = (prim & (MensuralLigature.Any | MensuralLigature.RightStem))
                    == MensuralLigature.Brevis;

            prevSemibrevis = (prim & MensuralLigature.Up) != 0;
        }
    }

    private static double PropagateProperties(Spanner ligature, IReadOnlyList<Item> primitives)
    {
        double thickness = SchemeConvert.ToDouble(ligature.GetProperty(ThicknessSymbol), 1.3);
        thickness *= ligature.Layout.GetDimension(LineThicknessSymbol);

        double headWidth = FontInterface.GetDefaultFont(ligature)
            .FindByName("noteheads.sM1mensural")
            .Extent(Axis.X)
            .Length;

        double maximaHeadWidth = FontInterface.GetDefaultFont(ligature)
            .FindByName("noteheads.sM3ligmensural")
            .Extent(Axis.X)
            .Length;

        /*
          start with the width of the first vertical edge,
          then let each note head add its own increment,
          considering that its left edge is taken account of
          (with either this initialization or the right edge of the previous head)
        */
        double minLength = thickness;

        Item prevPrimitive = null;
        int prevOutput = 0;
        foreach (Item primitive in primitives)
        {
            int output = SchemeConvert.ToInt(primitive.GetProperty(PrimitiveSymbol), 0);
            primitive.SetProperty(ThicknessSymbol, thickness);

            switch (output & MensuralLigature.Any)
            {
                case MensuralLigature.Invalid:
                case MensuralLigature.Brevis:
                    if ((output & MensuralLigature.Pes) == 0)
                    {
                        minLength += headWidth - thickness;
                    }

                    primitive.SetProperty(HeadWidthSymbol, headWidth);
                    break;
                case MensuralLigature.Maxima:
                    minLength += maximaHeadWidth - thickness;
                    primitive.SetProperty(HeadWidthSymbol, maximaHeadWidth);
                    break;
                case MensuralLigature.FlexaBegin:
                    /*
                      the next note (should be MLP_FLEXA_END) will handle this one
                    */
                    break;
                case MensuralLigature.FlexaEnd:
                {
                    object flexaScm = primitive.GetProperty(FlexaWidthSymbol);
                    double flexaWidth = SchemeConvert.ToDouble(flexaScm, 2.0);
                    minLength += flexaWidth;
                    object newHeadWidth = 0.5 * (flexaWidth + thickness);
                    primitive.SetProperty(HeadWidthSymbol, newHeadWidth);
                    prevPrimitive.SetProperty(HeadWidthSymbol, newHeadWidth);
                    prevPrimitive.SetProperty(FlexaWidthSymbol, flexaScm);
                    break;
                }

                default:
                    Warn.ProgrammingError("unexpected case fall-through");
                    break;
            }

            /*
              join to the previous notehead is handled with the previous note.
              let it know when this note has a left stem,
              as it mustn't be hidden by the join
            */
            if (prevPrimitive != null)
            {
                int stem = output & MensuralLigature.Stem;
                if (stem != 0)
                {
                    prevPrimitive.SetProperty(
                        PrimitiveSymbol,
                        SchemeConvert.FromInt(
                            prevOutput
                            | (stem * (MensuralLigature.JoinUp / MensuralLigature.Up))));
                }
            }

            prevPrimitive = primitive;
            prevOutput = output;
        }

        return minLength;
    }

    private static double FoldUpPrimitives(IReadOnlyList<Item> primitives, double minLength)
    {
        Item first = null;
        double distance = 0.0;
        double staffSpace = 0.0;
        double thickness = 0.0;

        for (int i = 0, pnum = primitives.Count; i < pnum; i++)
        {
            Item current = primitives[i];
            if (i == 0)
            {
                first = current;
                staffSpace = StaffSymbolReferencer.StaffSpace(first);
                thickness = SchemeConvert.ToDouble(current.GetProperty(ThicknessSymbol), 0.13);
            }

            MoveRelatedItemsToColumn(current, first.GetColumn(), distance);

            int prim = SchemeConvert.ToInt(current.GetProperty(PrimitiveSymbol), 0);
            double headWidth = SchemeConvert.ToDouble(current.GetProperty(HeadWidthSymbol), 0.0);
            if ((prim & MensuralLigature.Pes) == 0)
            {
                distance += headWidth - thickness;
            }

            int dotCount = RhythmicHead.DotCount(current);
            if (dotCount != 0)
            {
                /*
                  Move dots above/behind the ligature.
                  dots should also avoid staff lines.
                */
                Grob dotGrob = RhythmicHead.GetDots(current);

                bool onLine = StaffSymbolReferencer.OnLine(
                    current, SchemeConvert.ToInt(current.GetProperty(StaffPositionSymbol), 0));
                double vertShift = onLine ? staffSpace * 0.5 : 0.0;
                bool flexaBegin = (prim & MensuralLigature.FlexaBegin) != 0;

                if (i + 1 < pnum)
                {
                    /*
                      dot in the midst => avoid next note;
                      what to avoid and where depends on
                      being on a line or between lines
                    */
                    int delta = SchemeConvert.ToInt(
                        current.GetProperty(DeltaPositionSymbol), 0);
                    if (flexaBegin)
                    {
                        if ((0 < delta && delta < 3 + (2 * (onLine ? 1 : 0)))
                            || (!onLine && -3 < delta && delta < 0))
                        {
                            vertShift += delta < 0 ? staffSpace : -staffSpace;
                        }
                    }
                    else if (onLine)
                    {
                        if (0 < delta && delta < 3)
                        {
                            vertShift -= staffSpace;
                        }
                    }
                    else if (delta == 1 || delta == -1)
                    {
                        vertShift -= delta * staffSpace;
                    }
                }
                else
                {
                    minLength += headWidth * dotCount;
                }

                dotGrob.TranslateAxis(vertShift, Axis.Y);

                /*
                  move all dots behind head

                  This is ugly and should probably be handled by configuring
                  the DotColumn appropriately.  Note that these dots will
                  be disconnected from their dot column.  See
                  MoveRelatedItemsToColumn.

                  This also means the padding isn't configurable
                  as DotColumn.padding is.
                */
                double dotWidth = dotGrob.GetProperty(DotStencilSymbol) is Stencil stil
                    ? stil.Extent(Axis.X).Length
                    : 0.0;

                dotGrob.TranslateAxis(
                    (flexaBegin
                        ? staffSpace * 0.6
                        : (prim & MensuralLigature.Pes) != 0
                            ? 0.0
                            : headWidth - (0.2 * staffSpace))
                    - (2.0 * thickness) + dotWidth,
                    Axis.X);
            }
        }

        return minLength;
    }

    private static int PitchStepsOf(StreamEvent cause)
        => cause?.GetProperty(PitchSymbol) is Pitch pitch ? pitch.Steps() : 0;
}
