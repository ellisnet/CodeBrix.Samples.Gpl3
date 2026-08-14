/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2000--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/tie-column.cc, lily/include/tie-column.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - Tie_column::add_tie first lived in a shared seam file, since dissolved; it comes
//     home here and the seam's tie section is deleted.

/// <summary>
/// The object that places every tie of a tied chord at once, so they nest instead of
/// crossing.
/// </summary>
public static class TieColumn
{
    private static readonly Symbol TiesSymbol = Symbol.Intern("ties");
    private static readonly Symbol TieColumnInterface = Symbol.Intern("tie-column-interface");
    private static readonly Symbol PositioningDoneSymbol = Symbol.Intern("positioning-done");
    private static readonly Symbol TieConfigurationSymbol = Symbol.Intern("tie-configuration");
    private static readonly Symbol ControlPointsSymbol = Symbol.Intern("control-points");

    /// <summary>Adds a tie to the column, widening the column to cover it.</summary>
    /// <param name="me">The column.</param>
    /// <param name="tie">The tie to add.</param>
    public static void AddTie(Spanner me, Spanner tie)
    {
        if (tie.YParent != null && tie.YParent.HasInterface(TieColumnInterface))
        {
            return;
        }

        if (me.GetBound(Direction.Negative) == null
            || (me.GetBound(Direction.Negative).GetColumn().Rank
                > tie.GetBound(Direction.Negative).GetColumn().Rank))
        {
            me.SetBound(Direction.Negative, Tie.Head(tie, Direction.Negative));
            me.SetBound(Direction.Positive, Tie.Head(tie, Direction.Positive));
        }

        tie.YParent = me;
        PointerGroupInterface.AddGrob(me, TiesSymbol, tie);
    }

    /// <summary>Extends the column over its constituent ties.</summary>
    /// <remarks>
    /// ⚠ THIS CALLBACK DOES NOTHING, UPSTREAM AND HERE, AND THAT IS DELIBERATE. Upstream
    /// reads <c>ties</c> with <c>get_property</c>, but <c>Pointer_group_interface</c>
    /// stores it with <c>set_object</c> — two different alists on the grob. The property
    /// read therefore answers the empty list and the loop body never runs. Making it read
    /// the OBJECT would start moving the column's bounds, which upstream never does, so
    /// "fixing" it is exactly the plausible improvement standing rule 2 forbids.
    /// </remarks>
    /// <param name="me">The column.</param>
    /// <returns>Unspecified.</returns>
    public static object BeforeLineBreaking(Spanner me)
    {
        object s = me.GetProperty(TiesSymbol);
        while (s is Pair pair)
        {
            if (pair.Car is Spanner tie)
            {
                foreach (Direction dir in Both)
                {
                    if ((int)dir * tie.GetBound(dir).GetColumn().Rank
                        > (int)dir * me.GetBound(dir).GetColumn().Rank)
                    {
                        me.SetBound(dir, Tie.Head(tie, dir));
                    }
                }
            }

            s = pair.Cdr;
        }

        return Unspecified.Instance;
    }

    /// <summary>Scores and places every tie in the column.</summary>
    /// <param name="me">The column.</param>
    /// <returns><see langword="true"/>, which is what marks positioning as done.</returns>
    public static object CalcPositioningDone(Grob me)
    {
        List<Spanner> ties = PointerGroupInterface.ExtractGrobSet<Spanner>(me, TiesSymbol);
        if (ties.Count == 0)
        {
            return true;
        }

        me.SetProperty(PositioningDoneSymbol, true);
        ties.Sort((a, b) => Tie.GetPosition(a).CompareTo(Tie.GetPosition(b)));

        TieFormattingProblem problem = new TieFormattingProblem();
        problem.FromTies(ties);

        object manualConfigs = me.GetProperty(TieConfigurationSymbol);
        problem.SetManualTieConfiguration(manualConfigs);

        TiesConfiguration baseConfig = problem.GenerateOptimalConfiguration();
        for (int i = 0; i < baseConfig.Count; i++)
        {
            object cp = Tie.GetControlPoints(
                ties[i], problem.CommonXRefpoint(), baseConfig[i], problem.Details);

            ties[i].SetProperty(ControlPointsSymbol, cp);
            DirectionalElementInterface.SetGrobDirection(ties[i], baseConfig[i].Dir);

            // upstream calls this inside the loop, once per tie, and so does the port.
            problem.SetDebugScoring(baseConfig);
        }

        return true;
    }

    private static readonly Direction[] Both = { Direction.Negative, Direction.Positive };
}
