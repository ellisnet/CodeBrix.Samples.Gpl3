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

using System.Collections.Generic;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/semi-tie-column.cc, lily/include/semi-tie-column.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The column that places a chord's worth of laissez-vibrer or repeat ties together.
/// </summary>
/// <remarks>Upstream's own note: cut &amp; paste from <c>tie-column.cc</c>.</remarks>
public static class SemiTieColumn
{
    private static readonly Symbol TiesSymbol = Symbol.Intern("ties");
    private static readonly Symbol PositioningDoneSymbol = Symbol.Intern("positioning-done");
    private static readonly Symbol TieConfigurationSymbol = Symbol.Intern("tie-configuration");
    private static readonly Symbol ControlPointsSymbol = Symbol.Intern("control-points");
    private static readonly Symbol HeadDirectionSymbol = Symbol.Intern("head-direction");

    /// <summary>Scores and places every semi-tie in the column.</summary>
    /// <param name="me">The column.</param>
    /// <returns><see langword="true"/>, which is what marks positioning as done.</returns>
    public static object CalcPositioningDone(Grob me)
    {
        me.SetProperty(PositioningDoneSymbol, true);

        List<Item> lvTies = PointerGroupInterface.ExtractGrobSet<Item>(me, TiesSymbol);
        lvTies.Sort((a, b) => SemiTie.GetPosition(a).CompareTo(SemiTie.GetPosition(b)));

        TieFormattingProblem problem = new TieFormattingProblem();

        problem.FromSemiTies(
            lvTies,
            DirectionalElementInterface.FromScheme(
                me.GetProperty(HeadDirectionSymbol), Direction.Center));

        object manualConfigs = me.GetProperty(TieConfigurationSymbol);
        problem.SetManualTieConfiguration(manualConfigs);

        TiesConfiguration baseConfig = problem.GenerateOptimalConfiguration();
        for (int i = 0; i < lvTies.Count; i++)
        {
            object cp = Tie.GetControlPoints(
                lvTies[i], problem.CommonXRefpoint(), baseConfig[i], problem.Details);

            lvTies[i].SetProperty(ControlPointsSymbol, cp);
            DirectionalElementInterface.SetGrobDirection(lvTies[i], baseConfig[i].Dir);

            // upstream calls this inside the loop, once per tie, and so does the port.
            problem.SetDebugScoring(baseConfig);
        }

        return true;
    }

    /// <summary>
    /// The <c>head-direction</c> callback: every semi-tie in a column must agree on which
    /// side of the note head it hangs off, and this reports what they agreed on.
    /// </summary>
    /// <param name="me">The column.</param>
    /// <returns>The direction.</returns>
    public static object CalcHeadDirection(Grob me)
    {
        IReadOnlyList<Grob> ties = PointerGroupInterface.ExtractGrobSet(me, TiesSymbol);
        Direction d = Direction.Negative;
        for (int i = 0; i < ties.Count; i++)
        {
            Direction thisD = DirectionalElementInterface.FromScheme(
                ties[i].GetProperty(HeadDirectionSymbol), Direction.Center);
            if (i > 0 && d != thisD)
            {
                Warn.ProgrammingError(
                    "all semi-ties in a semi-tie-column should have the same head-direction");
                return (long)(int)d;
            }

            d = thisD;
        }

        return (long)(int)d;
    }
}
