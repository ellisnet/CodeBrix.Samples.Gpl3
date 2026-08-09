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

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/pure-from-neighbor-engraver.cc;

/// <summary>
/// Tells each grob that takes its pure height from its surroundings which items those
/// surroundings are.
/// <para>
/// Some grobs — a <c>BarNumber</c>, a <c>RehearsalMark</c> — have no useful height of their
/// own before layout, but should be as tall as whatever sits beside them. This engraver
/// collects every such grob at finalization, clumps them by column, and hands each clump
/// the items in the column immediately BEFORE and the column immediately AFTER it.
/// </para>
/// <para>
/// Items in the SAME column are excluded, which is the point: a grob is measured against
/// its neighbours, not against the things it shares a moment with.
/// </para>
/// </summary>
public class PureFromNeighborEngraver : Engraver
{
    private static readonly Symbol NeighborsSymbol = Symbol.Intern("neighbors");
    private static readonly Symbol PureFromNeighborInterfaceSymbol
        = Symbol.Intern("pure-from-neighbor-interface");

    private readonly List<Item> _pureRelevants = new List<Item>();
    private readonly List<Item> _needPureHeightsFromNeighbors = new List<Item>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public PureFromNeighborEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Pure_from_neighbor_engraver";

    /// <summary>Sorts each announced item into one of the two lists.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!(info.Grob is Item item))
        {
            return;
        }

        if (item.HasInterface(PureFromNeighborInterfaceSymbol))
        {
            _needPureHeightsFromNeighbors.Add(item);
        }
        else
        {
            _pureRelevants.Add(item);
        }
    }

    /// <summary>
    /// Attaches each clump's neighbours, once every grob for this context has been seen.
    /// </summary>
    public override void FinalizeTranslation()
    {
        if (_needPureHeightsFromNeighbors.Count == 0)
        {
            return;
        }

        _needPureHeightsFromNeighbors.Sort(static (a, b) => Grob.Less(a, b) ? -1 : (Grob.Less(b, a) ? 1 : 0));
        _pureRelevants.Sort(static (a, b) => Grob.Less(a, b) ? -1 : (Grob.Less(b, a) ? 1 : 0));

        /*
          first, clump needPureHeightsFromNeighbors into
          vectors of grobs that have the same column.
        */

        int l = 0;
        List<List<Grob>> needPureHeightsFromNeighbors = new List<List<Grob>>();
        do
        {
            List<Grob> temp = new List<Grob> { _needPureHeightsFromNeighbors[l] };
            for (; l < _needPureHeightsFromNeighbors.Count - 1
                   && _needPureHeightsFromNeighbors[l].SpannedColumnRankInterval().Left
                      == _needPureHeightsFromNeighbors[l + 1].SpannedColumnRankInterval().Left;
                 l++)
            {
                temp.Add(_needPureHeightsFromNeighbors[l + 1]);
            }

            needPureHeightsFromNeighbors.Add(temp);
            l++;
        }
        while (l < _needPureHeightsFromNeighbors.Count);

        /*
          then, loop through the pureRelevants list, adding the items
          to the elements of needPureHeightsFromNeighbors on either side.
        */

        // pos[0] is the clump BEFORE the current item and pos[1] the clump at or after it;
        // NoPosition on the first is upstream's VPOS, meaning "there is nothing before".
        int[] pos = { NoPosition, 0 };
        foreach (Item pureRelevant in _pureRelevants)
        {
            while (pos[1] < needPureHeightsFromNeighbors.Count
                   && pureRelevant.SpannedColumnRankInterval().Left
                      > needPureHeightsFromNeighbors[pos[1]][0].SpannedColumnRankInterval().Left)
            {
                pos[0] = pos[1];
                pos[1]++;
            }

            foreach (int p in pos)
            {
                if (p != NoPosition && p < needPureHeightsFromNeighbors.Count)
                {
                    for (int k = 0; k < needPureHeightsFromNeighbors[p].Count; k++)
                    {
                        if (!InSameColumn(needPureHeightsFromNeighbors[p][k], pureRelevant))
                        {
                            PointerGroupInterface.AddGrob(
                                needPureHeightsFromNeighbors[p][k], NeighborsSymbol, pureRelevant);
                        }
                    }
                }
            }
        }

        _needPureHeightsFromNeighbors.Clear();
        _pureRelevants.Clear();
    }

    /// <summary>The "no position" marker — upstream's <c>VPOS</c>.</summary>
    private const int NoPosition = -1;

    /// <summary>
    /// Whether two grobs occupy the same single column — a free function upstream, used
    /// only here.
    /// </summary>
    private static bool InSameColumn(Grob g1, Grob g2)
    {
        Slice a = g1.SpannedColumnRankInterval();
        Slice b = g2.SpannedColumnRankInterval();
        return a.Left == b.Left && a.Right == b.Right && a.Left == a.Right;
    }
}
