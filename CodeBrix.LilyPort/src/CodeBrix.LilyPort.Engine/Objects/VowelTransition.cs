/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2020--2026 David Stephen Grant <david@davidgrant.no>

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
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/vowel-transition.cc, lily/include/vowel-transition.hh;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port:
//   - ly_assoc_get is read through SchemeUtilities.Assq. Every key this file looks up in
//     bound-details is a SYMBOL, for which upstream's assoc and assq agree exactly.
//   - Interval_t<Moment>::length () has no counterpart on MomentInterval, so the one call
//     is written out as right - left, which is what that method is.

/// <summary>
/// The arrow between two vowels of one syllable — the grob itself is drawn by the line
/// spanner; this is the spacing half, which is what makes room for it.
/// <para>
/// It sets THREE rods, not one, because a vowel transition that crosses a line break has
/// to be wide enough on both sides of the break independently: one before the break, one
/// after it, and one for the unbroken case. The after-break rod is skipped when the
/// transition ends on the first note of the new line, since nothing is drawn there.
/// </para>
/// </summary>
public static class VowelTransition
{
    private static readonly Symbol AfterLineBreakingSymbol = Symbol.Intern("after-line-breaking");
    private static readonly Symbol BoundDetailsSymbol = Symbol.Intern("bound-details");
    private static readonly Symbol LeftSymbol = Symbol.Intern("left");
    private static readonly Symbol LeftBrokenSymbol = Symbol.Intern("left-broken");
    private static readonly Symbol MinimumLengthSymbol = Symbol.Intern("minimum-length");
    private static readonly Symbol MinimumLengthAfterBreakSymbol
        = Symbol.Intern("minimum-length-after-break");

    private static readonly Symbol PaddingSymbol = Symbol.Intern("padding");
    private static readonly Symbol RightSymbol = Symbol.Intern("right");
    private static readonly Symbol RightBrokenSymbol = Symbol.Intern("right-broken");

    /// <summary>The <c>springs-and-rods</c> callback: the minimum widths the transition
    /// needs, before a break, after a break, and unbroken.</summary>
    /// <param name="me">The vowel-transition spanner.</param>
    /// <returns>The unspecified value.</returns>
    public static object SetSpacingRods(Spanner me)
    {
        object minimumLength = me.GetProperty(MinimumLengthSymbol);
        object brokenLength = me.GetProperty(MinimumLengthAfterBreakSymbol);
        if (SchemeConvert.IsNumber(minimumLength) || SchemeConvert.IsNumber(brokenLength))
        {
            SystemGrob root = SystemGrob.GetRootSystem(me);
            DrulArray<Item> bounds = me.GetBounds();
            Item lb = bounds[Direction.Negative];
            Item rb = bounds[Direction.Positive];
            if (lb == null || rb == null)
            {
                return Unspecified.Instance;
            }

            List<PaperColumn> cols = root.BrokenColumnRange(lb.GetColumn(), rb.GetColumn());
            DrulArray<double> padding = new DrulArray<double>(0.0, 0.0);
            DrulArray<double> paddingBroken = new DrulArray<double>(0.0, 0.0);
            foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
            {
                object boundDetails = me.GetProperty(BoundDetailsSymbol);
                object details = AssocGet(
                    d == Direction.Negative ? LeftSymbol : RightSymbol, boundDetails);

                object detailsBroken = AssocGet(
                    d == Direction.Negative ? LeftBrokenSymbol : RightBrokenSymbol, boundDetails);

                if (SchemeUtilities.IsSchemeTrue(details))
                {
                    padding[d] = RobustDouble(AssocGet(PaddingSymbol, details), 0.0);
                }

                if (SchemeUtilities.IsSchemeTrue(detailsBroken))
                {
                    paddingBroken[d] = RobustDouble(AssocGet(PaddingSymbol, detailsBroken), 0.0);
                }
            }

            if (cols.Count > 0)
            {
                /* Before line break */
                Rod rodBeforeBreak = default;
                rodBeforeBreak.ItemDrul[Direction.Negative] = lb;
                rodBeforeBreak.ItemDrul[Direction.Positive]
                    = cols[0].FindPrebrokenPiece(Direction.Negative);

                rodBeforeBreak.Distance = RobustDouble(minimumLength, 0);
                rodBeforeBreak.Distance += padding[Direction.Negative];
                rodBeforeBreak.Distance += paddingBroken[Direction.Positive];
                rodBeforeBreak.Distance += rodBeforeBreak.BoundsProtrusion();
                rodBeforeBreak.AddToColumns();

                /* After line break */
                Rod rodAfterBreak = default;
                rodAfterBreak.ItemDrul[Direction.Negative]
                    = cols[cols.Count - 1].FindPrebrokenPiece(Direction.Positive);

                rodAfterBreak.ItemDrul[Direction.Positive] = rb;
                MomentInterval segmentTime = Item.SpannedTimeInterval(
                    rodAfterBreak.ItemDrul[Direction.Negative],
                    rodAfterBreak.ItemDrul[Direction.Positive]);

                segmentTime.Left = new Moment(segmentTime.Left.MainPart, Rational.Zero);

                /*
                  Calculate and add space only if the vowel transition is to be drawn.
                  I.e., either it does not end on the first note after breaking,
                  or property after-line-breaking is set to #t.
                */
                if (segmentTime.Right - segmentTime.Left != new Moment(Rational.Zero, Rational.Zero)
                    || SchemeUtilities.ToBool(me.GetPropertyData(AfterLineBreakingSymbol)))
                {
                    rodAfterBreak.Distance = SchemeConvert.IsNumber(brokenLength)
                        ? RobustDouble(brokenLength, 0)
                        : RobustDouble(minimumLength, 0);

                    rodAfterBreak.Distance += paddingBroken[Direction.Negative];
                    rodAfterBreak.Distance += padding[Direction.Positive];
                    rodAfterBreak.Distance += rodAfterBreak.BoundsProtrusion();
                    rodAfterBreak.AddToColumns();
                }
            }

            Rod rod = default;
            rod.Distance = RobustDouble(minimumLength, 0);
            rod.ItemDrul = new DrulArray<Item>(lb, rb);
            foreach (Direction d in new[] { Direction.Negative, Direction.Positive })
            {
                rod.Distance += padding[d];
            }

            rod.Distance += rod.BoundsProtrusion();
            rod.AddToColumns();

            Item leftPbp = rb.FindPrebrokenPiece(Direction.Negative);
            if (leftPbp != null)
            {
                rod.ItemDrul[Direction.Positive] = leftPbp;
                rod.AddToColumns();
            }
        }

        return Unspecified.Instance;
    }

    private static object AssocGet(object key, object alist)
    {
        Pair entry = SchemeUtilities.Assq(key, alist);
        return entry != null ? entry.Cdr : (object)false;
    }

    private static double RobustDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "vowel transition")
            : fallback;
}
