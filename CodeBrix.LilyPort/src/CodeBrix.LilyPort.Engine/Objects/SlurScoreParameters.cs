/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1996--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
  Jan Nieuwenhuizen <janneke@gnu.org>

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
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/slur-score-parameters.cc, lily/include/slur-score-parameters.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Every tunable the slur scorer reads, lifted out of a slur grob's <c>details</c> alist
/// once so the scoring loop never touches Scheme.
/// </summary>
/// <remarks>
/// Unlike <see cref="TieDetails"/>, a missing entry defaults to ZERO rather than to a
/// per-name value: upstream's <c>get_detail</c> here is a bare
/// <c>from_scm&lt;double&gt; (…, 0.0)</c>. The real values come from
/// <c>scm/define-grobs.scm</c>, so a zero means the alist was overridden and stripped.
/// </remarks>
public sealed class SlurScoreParameters
{
    /// <summary>How far, in staff spaces, endpoints are tried from the base attachment.</summary>
    public int RegionSize;

    /// <summary>Demerit for a note head colliding with the slur.</summary>
    public double HeadEncompassPenalty;

    /// <summary>Demerit for a stem colliding with the slur.</summary>
    public double StemEncompassPenalty;

    /// <summary>Factor for the distance between an endpoint and its base attachment.</summary>
    public double EdgeAttractionFactor;

    /// <summary>Demerit for a slur whose endpoints are horizontally aligned.</summary>
    public double SameSlopePenalty;

    /// <summary>Factor applied to steep slurs, only when the slur is not broken.</summary>
    public double SteeperSlopeFactor;

    /// <summary>Demerit for endpoints that are not horizontally aligned.</summary>
    public double NonHorizontalPenalty;

    /// <summary>The steepest slope the slur may take before being penalized.</summary>
    public double MaxSlope;

    /// <summary>Factor turning excess slope into demerits.</summary>
    public double MaxSlopeFactor;

    /// <summary>Demerit factor for encompassed objects other than heads and stems.</summary>
    public double ExtraObjectCollisionPenalty;

    /// <summary>The penalty accidentals take instead of the general one.</summary>
    public double AccidentalCollision;

    /// <summary>Free vertical space wanted between adjacent slurs (PhrasingSlur only).</summary>
    public double FreeSlurDistance;

    /// <summary>Free vertical space wanted between the slur and a note head.</summary>
    public double FreeHeadDistance;

    /// <summary>Unused upstream, kept so the details alist round-trips.</summary>
    public double ExtraEncompassCollisionDistance;

    /// <summary>Free vertical space wanted around encompassed objects.</summary>
    public double ExtraEncompassFreeDistance;

    /// <summary>Minimum gap inside the curve where it runs parallel to a staff line.</summary>
    public double GapToStafflineInside;

    /// <summary>Minimum gap outside the curve where it runs parallel to a staff line.</summary>
    public double GapToStafflineOutside;

    /// <summary>Softening term in the head-to-slur variance measure.</summary>
    public double AbsoluteClosenessMeasure;

    /// <summary>Exponent weighting the slope near an endpoint.</summary>
    public double EdgeSlopeExponent;

    /// <summary>Distance within which an object counts as close to the slur's edge.</summary>
    public double CloseToEdgeLength;

    /// <summary>The cap on the head-to-slur distance ratio.</summary>
    public double HeadSlurDistanceMaxRatio;

    /// <summary>Factor turning head-to-slur variance into demerits.</summary>
    public double HeadSlurDistanceFactor;

    /// <summary>How far the encompassed-object range is widened before fitting.</summary>
    public double EncompassObjectRangeOvershoot;

    /// <summary>How near a slur end may come to a tie end before being penalized.</summary>
    public double SlurTieExtremaMinDistance;

    /// <summary>The demerit charged when a slur end crowds a tie end.</summary>
    public double SlurTieExtremaMinDistancePenalty;

    private static readonly Symbol DetailsSymbol = Symbol.Intern("details");

    /// <summary>Reads every parameter off a slur grob.</summary>
    /// <param name="me">The slur.</param>
    public void Fill(Grob me)
    {
        object details = me.GetProperty(DetailsSymbol);

        RegionSize = (int)GetDetail(details, "region-size");
        HeadEncompassPenalty = GetDetail(details, "head-encompass-penalty");
        StemEncompassPenalty = GetDetail(details, "stem-encompass-penalty");
        EdgeAttractionFactor = GetDetail(details, "edge-attraction-factor");
        SameSlopePenalty = GetDetail(details, "same-slope-penalty");
        SteeperSlopeFactor = GetDetail(details, "steeper-slope-factor");
        NonHorizontalPenalty = GetDetail(details, "non-horizontal-penalty");
        MaxSlope = GetDetail(details, "max-slope");
        MaxSlopeFactor = GetDetail(details, "max-slope-factor");
        FreeHeadDistance = GetDetail(details, "free-head-distance");
        GapToStafflineInside = GetDetail(details, "gap-to-staffline-inside");
        GapToStafflineOutside = GetDetail(details, "gap-to-staffline-outside");
        AbsoluteClosenessMeasure = GetDetail(details, "absolute-closeness-measure");
        ExtraObjectCollisionPenalty = GetDetail(details, "extra-object-collision-penalty");
        AccidentalCollision = GetDetail(details, "accidental-collision");
        ExtraEncompassFreeDistance = GetDetail(details, "extra-encompass-free-distance");
        ExtraEncompassCollisionDistance
            = GetDetail(details, "extra-encompass-collision-distance");
        HeadSlurDistanceFactor = GetDetail(details, "head-slur-distance-factor");
        HeadSlurDistanceMaxRatio = GetDetail(details, "head-slur-distance-max-ratio");
        FreeSlurDistance = GetDetail(details, "free-slur-distance");
        EdgeSlopeExponent = GetDetail(details, "edge-slope-exponent");
        CloseToEdgeLength = GetDetail(details, "close-to-edge-length");
        EncompassObjectRangeOvershoot = GetDetail(details, "encompass-object-range-overshoot");
        SlurTieExtremaMinDistance = GetDetail(details, "slur-tie-extrema-min-distance");
        SlurTieExtremaMinDistancePenalty
            = GetDetail(details, "slur-tie-extrema-min-distance-penalty");
    }

    // upstream's free get_detail (slur-score-parameters.cc): scm_assq, then
    // from_scm<double> of the cdr with a ZERO default — not a per-name default.
    private static double GetDetail(object alist, string name)
    {
        Pair entry = SchemeUtilities.Assq(Symbol.Intern(name), alist);
        return entry != null && SchemeConvert.IsNumber(entry.Cdr)
            ? SchemeConvert.ToDouble(entry.Cdr, name)
            : 0.0;
    }
}
