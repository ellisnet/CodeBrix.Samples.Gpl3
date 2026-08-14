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

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/tie-details.cc, lily/include/tie-details.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Every tunable the tie scorer reads, lifted out of a tie grob's <c>details</c> alist
/// once so the scoring loop never touches Scheme.
/// </summary>
/// <remarks>
/// The defaults here are upstream's own second arguments to its <c>get_real_detail</c> /
/// <c>get_int_detail</c> macros, NOT the values <c>scm/define-grobs.scm</c> installs.
/// They differ, and they are supposed to: these apply when a caller has overridden the
/// <c>details</c> alist and dropped an entry.
/// </remarks>
public sealed class TieDetails
{
    /// <summary>The height a tie rises to asymptotically.</summary>
    public double HeightLimit;

    /// <summary>The initial height-to-width ratio of the tie's curve.</summary>
    public double Ratio;

    /// <summary>One staff space, in output units.</summary>
    public double StaffSpace;

    /// <summary>How far the tie's ends stand off from the note head's center line.</summary>
    public double XGap;

    /// <summary>How far the tie's ends stand off from a stem on the tie's own side.</summary>
    public double StemGap;

    /// <summary>Unused upstream, and kept so the details alist round-trips.</summary>
    public double BetweenLengthLimit;

    /// <summary>Penalty for a tie offset in the direction it should not be.</summary>
    public double WrongDirectionOffsetPenalty;

    /// <summary>Penalty for a tie on the same side as a stem.</summary>
    public double SameDirAsStemPenalty;

    /// <summary>Demerit factor for ties shorter than <see cref="MinLength"/>.</summary>
    public double MinLengthPenaltyFactor;

    /// <summary>The length below which a tie starts collecting a length penalty.</summary>
    public double MinLength;

    /// <summary>Padding added around the note-head skylines of a chord.</summary>
    public double SkylinePadding;

    /// <summary>Clearance the tie's tips want from a staff line, in half spaces.</summary>
    public double TipStaffLineClearance;

    /// <summary>Clearance the tie's center wants from a staff line, in half spaces.</summary>
    public double CenterStaffLineClearance;

    /// <summary>Demerit factor for coming close to a staff line.</summary>
    public double StaffLineCollisionPenalty;

    /// <summary>The distance at which a dot stops penalizing the tie.</summary>
    public double DotCollisionClearance;

    /// <summary>Demerit factor for coming close to a dot.</summary>
    public double DotCollisionPenalty;

    /// <summary>Penalty when a tie in a column sits below the previous one.</summary>
    public double TieColumnMonotonicityPenalty;

    /// <summary>Demerit factor for two ties in a column crowding each other.</summary>
    public double TieTieCollisionPenalty;

    /// <summary>The distance at which two ties stop penalizing each other.</summary>
    public double TieTieCollisionDistance;

    /// <summary>Demerit factor for horizontal distance from the note heads.</summary>
    public double HorizontalDistancePenaltyFactor;

    /// <summary>Demerit factor for vertical distance from the note heads.</summary>
    public double VerticalDistancePenaltyFactor;

    /// <summary>The height, in half spaces, below which a tie is fitted between lines.</summary>
    public double IntraSpaceThreshold;

    /// <summary>Demerit factor for asymmetric lengths among the outer ties of a chord.</summary>
    public double OuterTieLengthSymmetryPenaltyFactor;

    /// <summary>Demerit factor for asymmetric vertical offsets among the outer ties.</summary>
    public double OuterTieVerticalDistanceSymmetryPenaltyFactor;

    /// <summary>How far a tie is pushed off a note head that it nearly touches.</summary>
    public double OuterTieVerticalGap;

    /// <summary>The grob positions are measured against.</summary>
    public Grob StaffSymbolReferencerGrob;

    /// <summary>How many candidate positions to try for a lone tie.</summary>
    public int SingleTieRegionSize;

    /// <summary>How many candidate positions to try for the outer ties of a chord.</summary>
    public int MultiTieRegionSize;

    /// <summary>The direction a tie takes when nothing else decides.</summary>
    public Direction NeutralDirection;

    private static readonly Symbol DetailsSymbol = Symbol.Intern("details");
    private static readonly Symbol NeutralDirectionSymbol = Symbol.Intern("neutral-direction");

    /// <summary>Initializes the details with upstream's constructed defaults.</summary>
    public TieDetails()
    {
        StaffSpace = 1.0;
        HeightLimit = 1.0;
        Ratio = .333;
    }

    /// <summary>Reads every detail off a tie grob.</summary>
    /// <param name="me">The tie (or semi-tie) grob.</param>
    public void FromGrob(Grob me)
    {
        StaffSymbolReferencerGrob = me;
        StaffSpace = StaffSymbolReferencer.StaffSpace(me);

        NeutralDirection = DirectionalElementInterface.FromScheme(
            me.GetProperty(NeutralDirectionSymbol), Direction.Center);
        if (NeutralDirection == Direction.Center)
        {
            NeutralDirection = Direction.Negative;
        }

        object details = me.GetProperty(DetailsSymbol);

        HeightLimit = GetRealDetail(details, "height-limit", 0.75);
        Ratio = GetRealDetail(details, "ratio", .333);
        BetweenLengthLimit = GetRealDetail(details, "between-length-limit", 1.0);

        WrongDirectionOffsetPenalty
            = GetRealDetail(details, "wrong-direction-offset-penalty", 10);

        MinLength = GetRealDetail(details, "min-length", 1.0);
        MinLengthPenaltyFactor = GetRealDetail(details, "min-length-penalty-factor", 1.0);

        // in half-space
        CenterStaffLineClearance = GetRealDetail(details, "center-staff-line-clearance", 0.4);
        TipStaffLineClearance = GetRealDetail(details, "tip-staff-line-clearance", 0.4);
        StaffLineCollisionPenalty = GetRealDetail(details, "staff-line-collision-penalty", 5);
        DotCollisionClearance = GetRealDetail(details, "dot-collision-clearance", 0.25);
        DotCollisionPenalty = GetRealDetail(details, "dot-collision-penalty", 0.25);
        XGap = GetRealDetail(details, "note-head-gap", 0.2);
        StemGap = GetRealDetail(details, "stem-gap", 0.3);
        TieColumnMonotonicityPenalty
            = GetRealDetail(details, "tie-column-monotonicity-penalty", 100);
        TieTieCollisionPenalty = GetRealDetail(details, "tie-tie-collision-penalty", 30);
        TieTieCollisionDistance = GetRealDetail(details, "tie-tie-collision-distance", .25);
        HorizontalDistancePenaltyFactor
            = GetRealDetail(details, "horizontal-distance-penalty-factor", 5);
        SameDirAsStemPenalty = GetRealDetail(details, "same-dir-as-stem-penalty", 20);
        VerticalDistancePenaltyFactor
            = GetRealDetail(details, "vertical-distance-penalty-factor", 5);
        IntraSpaceThreshold = GetRealDetail(details, "intra-space-threshold", 1.0);
        OuterTieLengthSymmetryPenaltyFactor
            = GetRealDetail(details, "outer-tie-length-symmetry-penalty-factor", 3.0);
        OuterTieVerticalDistanceSymmetryPenaltyFactor = GetRealDetail(
            details, "outer-tie-vertical-distance-symmetry-penalty-factor", 3.0);

        OuterTieVerticalGap = GetRealDetail(details, "outer-tie-vertical-gap", 0.15);

        SingleTieRegionSize = GetIntDetail(details, "single-tie-region-size", 3);
        SkylinePadding = GetRealDetail(details, "skyline-padding", 0.05);
        MultiTieRegionSize = GetIntDetail(details, "multi-tie-region-size", 1);
    }

    private static double GetRealDetail(object details, string name, double defaultValue)
    {
        Pair entry = SchemeUtilities.Assq(Symbol.Intern(name), details);
        return entry != null && SchemeConvert.IsNumber(entry.Cdr)
            ? SchemeConvert.ToDouble(entry.Cdr, name)
            : defaultValue;
    }

    private static int GetIntDetail(object details, string name, int defaultValue)
    {
        Pair entry = SchemeUtilities.Assq(Symbol.Intern(name), details);
        return entry != null && SchemeConvert.IsNumber(entry.Cdr)
            ? (int)SchemeConvert.ToLong(entry.Cdr, name)
            : defaultValue;
    }
}
