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
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/stem-tremolo.cc;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// The slashes across a stem that mean a tremolo: a stack of beam-like (or, against
/// flags and beams, rectangular) strokes, one per tremolo flag.
/// </summary>
public static class StemTremolo
{
    private static readonly Symbol StemSymbol = Symbol.Intern("stem");
    private static readonly Symbol CrossStaffSymbol = Symbol.Intern("cross-staff");
    private static readonly Symbol StyleSymbol = Symbol.Intern("style");
    private static readonly Symbol ConstantSymbol = Symbol.Intern("constant");
    private static readonly Symbol QuantizedPositionsSymbol = Symbol.Intern("quantized-positions");
    private static readonly Symbol LengthFractionSymbol = Symbol.Intern("length-fraction");
    private static readonly Symbol BeamThicknessSymbol = Symbol.Intern("beam-thickness");
    private static readonly Symbol BeamWidthSymbol = Symbol.Intern("beam-width");
    private static readonly Symbol BlotDiameterSymbol = Symbol.Intern("blot-diameter");
    private static readonly Symbol ShapeSymbol = Symbol.Intern("shape");
    private static readonly Symbol BeamLikeSymbol = Symbol.Intern("beam-like");
    private static readonly Symbol RectangleSymbol = Symbol.Intern("rectangle");
    private static readonly Symbol FlagCountSymbol = Symbol.Intern("flag-count");
    private static readonly Symbol SlopeSymbol = Symbol.Intern("slope");
    private static readonly Symbol NoteCollisionInterfaceSymbol
        = Symbol.Intern("note-collision-interface");

    /// <summary>The <c>cross-staff</c> callback: whatever the stem answers.</summary>
    /// <param name="me">The tremolo.</param>
    /// <returns>The stem's <c>cross-staff</c> value.</returns>
    public static object CalcCrossStaff(Grob me)
    {
        Grob stem = me.GetObject(StemSymbol) as Grob;
        return stem != null ? stem.GetProperty(CrossStaffSymbol) : Nil.Instance;
    }

    /// <summary>
    /// The <c>slope</c> callback: the beam's own slope under a beam, a steeper constant
    /// against a down-stem flag, a gentle one otherwise.
    /// </summary>
    /// <param name="me">The tremolo.</param>
    /// <returns>The slope.</returns>
    public static double CalcSlope(Grob me)
    {
        Grob stem = me.GetObject(StemSymbol) as Grob;
        Spanner beam = Stem.GetBeam(stem);

        object style = me.GetProperty(StyleSymbol);

        if (beam != null && !ReferenceEquals(style, ConstantSymbol))
        {
            double dy = 0;
            object s = beam.GetProperty(QuantizedPositionsSymbol);
            if (Grob.TryNumberPair(s, out Interval positions))
            {
                dy = -positions.Left + positions.Right;
            }

            Grob s2 = BeamHelpers.LastNormalStem(beam);
            Grob s1 = BeamHelpers.FirstNormalStem(beam);

            Grob common = s1.CommonRefpoint(s2, Axis.X);
            double dx = s2.RelativeCoordinate(common, Axis.X)
                        - s1.RelativeCoordinate(common, Axis.X);

            return dx != 0.0 ? dy / dx : 0;
        }
        else
        {
            /* down stems with flags should have more sloped trems (helps avoid
               flag/stem collisions without making the stem very long) */
            return Stem.DurationLog(stem) >= 3
                   && Stem.GetGrobDirection(me) == Direction.Negative && beam == null
                ? 0.40
                : 0.25;
        }
    }

    /// <summary>
    /// The <c>beam-width</c> callback: shorter strokes where a beam or an up-flag is in
    /// the way.
    /// </summary>
    /// <param name="me">The tremolo.</param>
    /// <returns>The width, in staff spaces.</returns>
    public static double CalcWidth(Grob me)
    {
        Grob stem = me.GetObject(StemSymbol) as Grob;
        Direction dir = Stem.GetGrobDirection(me);
        bool beam = Stem.GetBeam(stem) != null;
        bool flag = Stem.DurationLog(stem) >= 3 && !beam;

        /* beamed stems and up-stems with flags have shorter tremolos */
        return (dir == Direction.Positive && flag) || beam ? 1.0 : 1.5;
    }

    /// <summary>
    /// The <c>shape</c> callback: rectangles against beams and up-flags, beam-like
    /// strokes otherwise.
    /// </summary>
    /// <param name="me">The tremolo.</param>
    /// <returns><c>rectangle</c> or <c>beam-like</c>.</returns>
    public static object CalcShape(Grob me)
    {
        Grob stem = me.GetObject(StemSymbol) as Grob;
        Direction dir = Stem.GetGrobDirection(me);
        bool beam = Stem.GetBeam(stem) != null;
        bool flag = Stem.DurationLog(stem) >= 3 && !beam;
        object style = me.GetProperty(StyleSymbol);

        return !ReferenceEquals(style, ConstantSymbol)
               && ((dir == Direction.Positive && flag) || beam)
            ? RectangleSymbol
            : BeamLikeSymbol;
    }

    /// <summary>
    /// Returns the vertical distance between two strokes: the beam's own translation
    /// under a live beam, a fraction of the staff space otherwise.
    /// </summary>
    /// <param name="me">The tremolo.</param>
    /// <returns>The translation.</returns>
    public static double GetBeamTranslation(Grob me)
    {
        Grob stem = me.GetObject(StemSymbol) as Grob;
        Spanner beam = Stem.GetBeam(stem);

        return beam != null && beam.IsLive
            ? BeamHelpers.GetBeamTranslation(beam)
            : StaffSymbolReferencer.StaffSpace(me)
              * Stem.ToDouble(me.GetProperty(LengthFractionSymbol), 1.0)
              * 0.81;
    }

    /// <summary>Builds the stack of strokes, centred on the origin.</summary>
    /// <param name="me">The tremolo.</param>
    /// <param name="slope">The strokes' slope.</param>
    /// <param name="dir">Which way the stack grows.</param>
    /// <returns>The stencil.</returns>
    public static Stencil RawStencil(Grob me, double slope, Direction dir)
    {
        double ss = StaffSymbolReferencer.StaffSpace(me);
        double thick = Stem.ToDouble(me.GetProperty(BeamThicknessSymbol), 1);
        double width = Stem.ToDouble(me.GetProperty(BeamWidthSymbol), 1);
        double blot = me.Layout == null ? 0.0 : me.Layout.GetDimension(BlotDiameterSymbol);
        object shape = me.GetProperty(ShapeSymbol);
        if (!(shape is Symbol))
        {
            shape = BeamLikeSymbol;
        }

        width *= ss;
        thick *= ss;

        Stencil a;
        if (ReferenceEquals(shape, RectangleSymbol))
        {
            a = Lookup.RotatedBox(slope, width, thick, blot);
        }
        else
        {
            a = Lookup.Beam(slope, width, thick, blot);
        }

        a.AlignTo(Axis.X, Direction.Center.Value);
        a.AlignTo(Axis.Y, Direction.Center.Value);

        object flagValue = me.GetProperty(FlagCountSymbol);
        int tremoloFlags = SchemeConvert.IsNumber(flagValue)
            ? SchemeConvert.ToInt(flagValue, "flag-count")
            : 0;
        if (tremoloFlags == 0)
        {
            Warn.ProgrammingError("no tremolo flags");

            me.Suicide();
            return Stencil.Empty;
        }

        double beamTranslation = GetBeamTranslation(me);

        Stencil mol = Stencil.Empty;
        for (int i = 0; i < tremoloFlags; i++)
        {
            Stencil b = a;
            b.TranslateAxis(beamTranslation * i * dir.Value * -1, Axis.Y);
            mol.AddStencil(b);
        }

        return mol;
    }

    /// <summary>
    /// The <c>Y-extent</c> callback's pure half: the stroke stack's own height, pushed
    /// clear of the beams when the stem has any.
    /// </summary>
    /// <param name="me">The tremolo.</param>
    /// <returns>The extent.</returns>
    public static Interval PureHeight(Grob me)
    {
        /*
          Cannot use the real slope, since it looks at the Beam.
         */
        Stencil s1 = UntranslatedStencil(me, 0.35);
        Item stem = me.GetObject(StemSymbol) as Item;
        if (stem == null)
        {
            return s1.Extent(Axis.Y);
        }

        Direction dir = Stem.GetGrobDirection(me);

        Spanner beam = Stem.GetBeam(stem);

        if (beam == null)
        {
            return s1.Extent(Axis.Y);
        }

        Interval ph = Stem.PureYExtent(stem);
        if (ph.IsEmpty) // This should not really happen but does
        {
            return s1.Extent(Axis.Y);
        }

        StemInfo si = Stem.GetStemInfo(stem);
        ph[-dir] = si.ShortestY;
        if (ph.IsEmpty) // This should not really happen either
        {
            return s1.Extent(Axis.Y);
        }

        int beamCount = Stem.BeamMultiplicity(stem).Length + 1;
        double beamTranslation = GetBeamTranslation(me);

        ph = ph - dir.Value * Math.Max(beamCount, 1) * beamTranslation;
        ph = ph - ph.Center; // TODO: this nullifies the previous line?!?

        return ph;
    }

    /// <summary>The <c>X-extent</c> callback: the stroke stack's own width.</summary>
    /// <param name="me">The tremolo.</param>
    /// <returns>The extent.</returns>
    public static Interval Width(Grob me)
    {
        /*
          Cannot use the real slope, since it looks at the Beam.
         */
        Stencil s1 = UntranslatedStencil(me, 0.35);

        return s1.Extent(Axis.X);
    }

    /// <summary>Returns how tall the whole stroke stack is.</summary>
    /// <param name="me">The tremolo.</param>
    /// <returns>The height.</returns>
    public static double VerticalLength(Grob me)
        => UntranslatedStencil(me, 0.35).Extent(Axis.Y).Length;

    /// <summary>Builds the stroke stack, oriented for the stem it sits on.</summary>
    /// <param name="me">The tremolo.</param>
    /// <param name="slope">The strokes' slope.</param>
    /// <returns>The stencil.</returns>
    public static Stencil UntranslatedStencil(Grob me, double slope)
    {
        Grob stem = me.GetObject(StemSymbol) as Grob;
        if (stem == null)
        {
            Warn.ProgrammingError("no stem for stem-tremolo");
            return Stencil.Empty;
        }

        Direction dir = Stem.GetGrobDirection(me);

        bool wholeNote = Stem.DurationLog(stem) <= 0;

        /* for a whole note, we position relative to the notehead, so we want the
           stencil aligned on the flag closest to the head */
        Direction stencilDir = wholeNote ? -dir : dir;
        return RawStencil(me, slope, stencilDir);
    }

    /// <summary>The <c>Y-offset</c> callback.</summary>
    /// <param name="me">The tremolo.</param>
    /// <returns>The offset.</returns>
    public static double CalcYOffset(Grob me)
        => YOffset(me, false);

    /// <summary>The <c>Y-offset</c> callback's pure half.</summary>
    /// <param name="me">The tremolo.</param>
    /// <returns>The offset.</returns>
    public static double PureCalcYOffset(Grob me)
        => YOffset(me, true);

    /// <summary>
    /// The <c>direction</c> callback: the stem's direction, re-decided for whole-note
    /// tremolos that would collide with simultaneous notes.
    /// </summary>
    /// <param name="me">The tremolo.</param>
    /// <returns>The direction.</returns>
    public static Direction CalcDirection(Grob me)
    {
        Item stem = me.GetObject(StemSymbol) as Item;
        if (stem == null)
        {
            return Direction.Center;
        }

        Direction stemdir = Stem.GetGrobDirection(stem);

        List<int> nhp = Stem.NoteHeadPositions(stem);

        /*
         * We re-decide stem-dir if there may be collisions with other
         * note heads in the staff.
         */
        Grob maybeNc = stem.XParent?.XParent;
        bool wholeNote = Stem.DurationLog(stem) <= 0;
        if (wholeNote && maybeNc != null && maybeNc.HasInterface(NoteCollisionInterfaceSymbol))
        {
            DrulArray<bool> avoidMe = new DrulArray<bool>(false, false);
            List<int> allNhps = NoteCollisionNoteHeadPositions(maybeNc);
            if (allNhps[0] < nhp[0])
            {
                avoidMe[Direction.Negative] = true;
            }

            if (allNhps[allNhps.Count - 1] > nhp[nhp.Count - 1])
            {
                avoidMe[Direction.Positive] = true;
            }

            if (avoidMe[stemdir])
            {
                stemdir = -stemdir;
                if (avoidMe[stemdir])
                {
                    Warn.Warning(
                        "Whole-note tremolo may collide with simultaneous notes.");
                    stemdir = -stemdir;
                }
            }
        }

        return stemdir;
    }

    /// <summary>
    /// Computes the tremolo's vertical position: at the stem's end, clear of any beams
    /// or flags, or beside the note head when the stem is invisible.
    /// </summary>
    /// <param name="me">The tremolo.</param>
    /// <param name="pure">Whether to answer the before-line-breaking value.</param>
    /// <returns>The offset.</returns>
    public static double YOffset(Grob me, bool pure)
    {
        Item stem = me.GetObject(StemSymbol) as Item;
        if (stem == null)
        {
            return 0.0;
        }

        Direction dir = Stem.GetGrobDirection(me);

        Spanner beam = Stem.GetBeam(stem);
        double beamTranslation = GetBeamTranslation(me);

        int beamCount = beam != null ? Stem.BeamMultiplicity(stem).Length + 1 : 0;

        if (pure && beam != null)
        {
            Interval ph = Stem.PureYExtent(stem);
            StemInfo si = Stem.GetStemInfo(stem);
            ph[-dir] = si.ShortestY;

            return (ph - dir.Value * Math.Max(beamCount, 1) * beamTranslation)[dir]
                   - dir.Value * 0.5 * Stem.PureYExtent(me).Length;
        }

        double endY = (pure ? Stem.PureYExtent(stem) : stem.Extent(stem, Axis.Y))[dir]
                      - dir.Value * Math.Max(beamCount, 1) * beamTranslation
                      - Stem.BeamEndCorrective(stem);

        if (beam == null && Stem.DurationLog(stem) >= 3)
        {
            endY -= dir.Value * (Stem.DurationLog(stem) - 2) * beamTranslation;
            if (dir == Direction.Positive)
            {
                endY -= dir.Value * beamTranslation * 0.5;
            }
        }

        bool wholeNote = Stem.DurationLog(stem) <= 0;
        if (wholeNote || double.IsInfinity(endY))
        {
            /* we shouldn't position relative to the end of the stem since the stem
               is invisible */
            double ss = StaffSymbolReferencer.StaffSpace(me);
            List<int> nhp = Stem.NoteHeadPositions(stem);
            if (nhp.Count == 0)
            {
                Warn.Warning("stem tremolo has no note heads");
                endY = 0.0;
            }
            else
            {
                double noteHead
                    = (dir == Direction.Positive ? nhp[nhp.Count - 1] : nhp[0]) * ss / 2;
                endY = noteHead + dir.Value * 1.5;
            }
        }

        return endY;
    }

    /// <summary>The <c>stencil</c> callback.</summary>
    /// <param name="me">The tremolo.</param>
    /// <returns>The stencil.</returns>
    public static object Print(Grob me)
    {
        Stencil s = UntranslatedStencil(
            me, Stem.ToDouble(me.GetProperty(SlopeSymbol), 0.25));
        return s;
    }

    /*
      Note_collision_interface::note_head_positions, pulled forward from
      lily/note-collision.cc: every read in it is a generic grob read, and the
      whole-note-tremolo direction decision above needs it. EPG5's port of
      note-collision.cc landed in the same wave, so the private copy was retired
      at integration (2026-08-07) for a delegation to the canonical home.
    */
    private static List<int> NoteCollisionNoteHeadPositions(Grob me)
        => NoteCollisionInterface.NoteHeadPositions(me);
}
