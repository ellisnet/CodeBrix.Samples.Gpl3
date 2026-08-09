/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2003--2026 Juergen Reuter <reuter@ipd.uka.de>

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

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/coherent-ligature-engraver.cc, lily/include/coherent-ligature-engraver.hh;

// Modified by Jeremy Ellis on 2026-08-09 as part of the CodeBrix port:
//   - upstream's free function calc_delta_pitches has a declaration in the header but no
//     caller outside this file, so it is a private static here.
//   - upstream's `if constexpr (false)' block inside move_related_items_to_column (an
//     experimental spacing collapse) is dead by construction and is not ported; the
//     comment that names it is kept.

/*
 * This abstract class serves as common superclass for all ligature
 * engravers thet produce a single connected graphical object of fixed
 * width, consisting of noteheads and other primitives (see class
 * Ligature_engraver for more information on the interaction between
 * this class and its superclass).  In particular, it cares for the
 * following tasks:
 *
 * - provide a function for putting all grobs of the ligature into a
 * single paper column,
 *
 * - delegate actual creation of ligature to concrete subclass,
 *
 * - except in Kievan notation, collect all accidentals that occur
 * within the ligature and put them at the left side of the ligature
 * (TODO; see function collect_accidentals ()),
 *
 * - collapse superflous space after each ligature (TODO).
 */

/// <summary>
/// The common superclass of every ligature engraver that produces a SINGLE connected
/// graphical object of fixed width: it lines the heads up into one paper column and
/// leaves the shape itself to a concrete style.
/// </summary>
/// <remarks>
/// <para>
/// TODO (upstream): local accidentals — collect accidentals that occur within a ligature
/// and put them before the ligature. If an accidental changes within a ligature, print a
/// warning and ignore any further accidental for that pitch within that ligature.
/// </para>
/// <para>
/// TODO (upstream): make spacing more robust; do not screw up spacing if a user
/// erroneously puts a rest in a ligature.
/// </para>
/// </remarks>
public abstract class CoherentLigatureEngraver : LigatureEngraver
{
    private static readonly Symbol DeltaPositionSymbol = Symbol.Intern("delta-position");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol PitchSymbol = Symbol.Intern("pitch");

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    protected CoherentLigatureEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Coherent_ligature_engraver";

    /*
      FIXME (upstream): this is very ugly.  Instead of moving items around between
      columns, we should set exact spacing constraints between these columns.

      It also changes some grob relationships by setting the parent of all these
      grobs to the paper column, e.g. disconnecting Dots from their DotColumn.
      This is actually relied upon by the engravers.
     */

    /// <summary>
    /// Moves an item — and everything in its column that belongs to the same staff — into
    /// a target column, offset horizontally.
    /// </summary>
    /// <param name="item">The item whose column is emptied.</param>
    /// <param name="targetColumn">The column everything is moved into.</param>
    /// <param name="offset">How far right to shift what is moved.</param>
    protected static void MoveRelatedItemsToColumn(
        Item item, PaperColumn targetColumn, double offset)
    {
        PaperColumn sourceColumn = item.GetColumn();
        Grob staffSymbol = StaffSymbolReferencer.GetStaffSymbol(item);
        IReadOnlyList<Grob> elements
            = PointerGroupInterface.ExtractGrobSet(sourceColumn, ElementsSymbol);
        for (int i = elements.Count; i-- > 0;)
        {
            Grob sibling = elements[i];

            if (!ReferenceEquals(StaffSymbolReferencer.GetStaffSymbol(sibling), staffSymbol))
            {
                // sibling is from a staff different than that of the item of
                // interest
                continue;
            }

            // Upstream has an `if constexpr (false)' block here that would set
            // `forced-spacing' on the sibling's X parent -- an experimental collapse of
            // the spacing after a ligature. It is dead by construction upstream.
            sibling.XParent = targetColumn;
            sibling.TranslateAxis(offset, Axis.X);
        }
    }

    /// <summary>
    /// Computes the pitch step from each head to the next, and hands the shape to the
    /// concrete style.
    /// </summary>
    /// <param name="ligature">The ligature spanner.</param>
    /// <param name="primitives">The heads, in time order.</param>
    protected override void TypesetLigature(Spanner ligature, IReadOnlyList<Item> primitives)
    {
        // compute some commonly needed context info stored as grob
        // properties
        CalcDeltaPitches(primitives);

        // prepare ligature for typesetting
        BuildLigature(ligature, primitives);
        CollectAccidentals(ligature, primitives);
    }

    /// <summary>Builds the ligature by transforming the array of note heads.</summary>
    /// <param name="ligature">The ligature spanner.</param>
    /// <param name="primitives">The heads, in time order.</param>
    protected abstract void BuildLigature(Spanner ligature, IReadOnlyList<Item> primitives);

    /*
     * TODO (upstream): This function should collect all accidentals that occur
     * within the ligature (by scanning through the primitives array) and
     * place all of them at the left of the ligature.  If there is an
     * alteration within the ligature, issue a warning.
     */

    /// <summary>
    /// Would put the ligature's accidentals in front of it. ⚠ Upstream's body is a bare
    /// TODO, so this does nothing — a divergence would be to make it do something.
    /// </summary>
    /// <param name="ligature">The ligature spanner.</param>
    /// <param name="primitives">The heads, in time order.</param>
    /// <remarks>
    /// NOTE (upstream): if implementing such a function, note that in Kievan notation the
    /// B-flat accidental should not be "collected", but rather prints immediately before
    /// the note head as usual.
    /// </remarks>
    protected virtual void CollectAccidentals(Spanner ligature, IReadOnlyList<Item> primitives)
    {
        /* TODO */
    }

    private static void CalcDeltaPitches(IReadOnlyList<Item> primitives)
    {
        if (primitives.Count == 0)
        {
            return;
        }

        int prevPitch = PitchStepsOf(primitives[0]);
        for (int i = 1; i < primitives.Count; ++i)
        {
            int pitch = PitchStepsOf(primitives[i]);

            Item prevItem = primitives[i - 1];
            prevItem.SetProperty(DeltaPositionSymbol, SchemeConvert.FromInt(pitch - prevPitch));

            prevPitch = pitch;
        }

        primitives[primitives.Count - 1]
            .SetProperty(DeltaPositionSymbol, SchemeConvert.FromInt(0));
    }

    private static int PitchStepsOf(Item primitive)
    {
        StreamEvent cause = primitive.EventCause();
        return cause?.GetProperty(PitchSymbol) is Pitch pitch ? pitch.Steps() : 0;
    }
}
