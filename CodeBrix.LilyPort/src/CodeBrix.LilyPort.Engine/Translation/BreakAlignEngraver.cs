/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/break-align-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - derived_mark is not carried: it exists to keep column_alist_ from being collected,
//     and the port's alist is an ordinary managed reference.

/// <summary>
/// Collects everything breakable at a moment — clefs, key signatures, time signatures, bar
/// lines — into groups by <c>break-align-symbol</c>, and hands them to a
/// <c>BreakAlignment</c> to be ordered and spaced.
/// <para>
/// It is what makes the elements at a bar line appear in a consistent order without any
/// engraver knowing about any other: each announces its grob with a symbol, and the
/// grouping and ordering happen here and in
/// <see cref="BreakAlignmentInterface.CalcPositioningDone"/>.
/// </para>
/// <para>
/// Everything is reset at the end of each timestep — a <c>BreakAlignment</c> belongs to one
/// moment, not to the score.
/// </para>
/// </summary>
public class BreakAlignEngraver : Engraver
{
    private static readonly Symbol BreakAlignSymbolSymbol = Symbol.Intern("break-align-symbol");
    private static readonly Symbol CreateSpacingSymbol = Symbol.Intern("createSpacing");
    private static readonly Symbol BreakAlignedInterfaceSymbol
        = Symbol.Intern("break-aligned-interface");

    private static readonly Symbol BreakAlignableInterfaceSymbol
        = Symbol.Intern("break-alignable-interface");

    private Item _align;
    private object _columnAlist = Nil.Instance;
    private Item _leftEdge;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public BreakAlignEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Break_align_engraver";

    /// <summary>Forgets this moment's alignment.</summary>
    public override void StopTranslationTimestep()
    {
        _columnAlist = Nil.Instance;

        _align = null;
        _leftEdge = null;
    }

    /// <summary>
    /// Parents a break-ALIGNABLE grob (a rehearsal mark, say) on the alignment, so that
    /// <see cref="BreakAlignableInterface.FindParent"/> can later pick which group inside
    /// it to sit over.
    /// <para>
    /// Musical items are left alone deliberately: they may need to line up with note heads
    /// rather than with the bar line, and upstream leaves that to other engravers while
    /// noting it could perhaps be done here.
    /// </para>
    /// </summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!(info.Grob is Item item))
        {
            return;
        }

        if (item.HasInterface(BreakAlignedInterfaceSymbol))
        {
            AcknowledgeBreakAligned(info, item);
        }

        if (item.HasInterface(BreakAlignableInterfaceSymbol))
        {
            AcknowledgeBreakAlignable(item);
        }
    }

    private void AcknowledgeBreakAlignable(Item item)
    {
        if (item.GetParent(Axis.X) != null)
        {
            return;
        }

        // Handling musical items is more involved because they might need to be
        // aligned with notation (note heads, etc.). We currently leave that to
        // other engravers, but maybe it could be done here.
        if (!Item.IsNonMusical(item))
        {
            return;
        }

        if (_align == null)
        {
            CreateAlignment();
        }

        item.SetParent(_align, Axis.X);
    }

    // Clef, BarLine, etc. are break-aligned grobs
    private void AcknowledgeBreakAligned(GrobInfo info, Item item)
    {
        /*
          Removed check for item->empty (X_AXIS). --hwn 20/1/04
        */
        if (item.GetParent(Axis.X) != null)
        {
            return;
        }

        if (!Item.IsNonMusical(item))
        {
            return;
        }

        object alignName = item.GetProperty(BreakAlignSymbolSymbol);
        if (!(alignName is Symbol))
        {
            return;
        }

        CreateAlignment();

        // Create a single LeftEdge that appears to come from the same engraver
        // as the first staff-resident, break-aligned grob that we see. This is
        // questionable and may contribute to problems discussed in issue #5385.
        // Practically, this is probably fine for single-staff scores.
        //
        // Break-aligned grobs can originate outside of a Staff context, but we
        // don't want to create a LeftEdge then (issue #6134). The createSpacing
        // property tells us whether the grob originated within a Staff or
        // similar context.
        if (_leftEdge == null)
        {
            Engraver eng = info.OriginEngraver as Engraver;
            if (eng != null && SchemeUtilities.ToBool(eng.GetProperty(CreateSpacingSymbol)))
            {
                _leftEdge = eng.MakeItem("LeftEdge", Nil.Instance);
                AddToGroup(_leftEdge.GetProperty(BreakAlignSymbolSymbol), _leftEdge);
            }
        }

        AddToGroup(alignName, item);
    }

    private void CreateAlignment()
    {
        _align ??= MakeItem("BreakAlignment", Nil.Instance);
    }

    private void AddToGroup(object alignName, Item item)
    {
        object s = SchemeUtilities.LyAssoc(alignName, _columnAlist);
        Item group = null;

        if (s is Pair found)
        {
            group = found.Cdr as Item;
        }

        if (group == null)
        {
            group = MakeItem("BreakAlignGroup", item);

            group.SetProperty(BreakAlignSymbolSymbol, alignName);
            group.SetParent(_align, Axis.Y);

            _columnAlist = SchemeUtilities.AssocSet(_columnAlist, alignName, group);

            BreakAlignmentInterface.AddElement(_align, group);
        }

        AxisGroupInterface.AddElement(group, item);
    }
}
