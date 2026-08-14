/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1996--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/paper-score.cc, lily/include/paper-score.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// What a score becomes once it is being laid out on paper: the root system, the
/// output definition it is laid out under, and the break candidates found in it.
/// <para>
/// The <see cref="RootSystem"/> is created ONCE, before any music is interpreted, and
/// every grob an engraver makes is typeset into it. Line breaking later clones that
/// one system into one per line — which is why the singular root exists at all.
/// </para>
/// </summary>
public class PaperScore : MusicOutput
{
    private readonly List<PaperColumn> _columns = new List<PaperColumn>();
    private readonly List<int> _breakIndices = new List<int>();
    private readonly List<int> _breakRanks = new List<int>();

    private static readonly CodeBrix.LilyScheme.Values.Symbol LineWidthSymbol
        = CodeBrix.LilyScheme.Values.Symbol.Intern("line-width");

    private static readonly CodeBrix.LilyScheme.Values.Symbol IndentSymbol
        = CodeBrix.LilyScheme.Values.Symbol.Intern("indent");

    private static readonly CodeBrix.LilyScheme.Values.Symbol RaggedRightSymbol
        = CodeBrix.LilyScheme.Values.Symbol.Intern("ragged-right");

    private static readonly CodeBrix.LilyScheme.Values.Symbol SystemCountSymbol
        = CodeBrix.LilyScheme.Values.Symbol.Intern("system-count");

    private SystemGrob _system;
    private List<Prob> _paperSystems;

    /// <summary>Initializes a paper score under an output definition.</summary>
    /// <param name="layout">The output definition to lay the score out under.</param>
    public PaperScore(OutputDef layout)
    {
        Layout = layout;
        _system = null;
    }

    /// <summary>Gets the output definition the score is laid out under.</summary>
    public OutputDef Layout { get; }

    /// <summary>Gets the root system: the single unbroken line everything is typeset into.</summary>
    public SystemGrob RootSystem => _system;

    /// <summary>
    /// Adopts a system. The FIRST one becomes the root; later ones are merely told
    /// which score and layout they belong to, which is what happens to the pieces line
    /// breaking produces.
    /// </summary>
    /// <param name="system">The system to adopt.</param>
    public void TypesetSystem(SystemGrob system)
    {
        if (system == null)
        {
            throw new ArgumentNullException(nameof(system));
        }

        _system ??= system;

        system.PaperScore = this;
        system.Layout = Layout;
    }

    /// <summary>
    /// Gets the usable columns on the root system, computing them on first ask.
    /// </summary>
    /// <returns>The columns.</returns>
    public IReadOnlyList<PaperColumn> GetColumns()
    {
        if (_columns.Count == 0)
        {
            FindBreakIndices();
        }

        return _columns;
    }

    /// <summary>
    /// Gets the indices into <see cref="GetColumns"/> at which a line may be broken.
    /// </summary>
    /// <returns>The break indices.</returns>
    public IReadOnlyList<int> GetBreakIndices()
    {
        if (_breakIndices.Count == 0)
        {
            FindBreakIndices();
        }

        return _breakIndices;
    }

    /// <summary>Gets the column ranks at which a line may be broken.</summary>
    /// <returns>The break ranks.</returns>
    public IReadOnlyList<int> GetBreakRanks()
    {
        if (_breakRanks.Count == 0)
        {
            FindBreakIndices();
        }

        return _breakRanks;
    }

    /// <summary>Gets the C++ class name this output corresponds to.</summary>
    public override string ClassName => "Paper_score";

    /// <summary>
    /// Prepares the grobs for line breaking: every breakable item gets its prebroken
    /// copies.
    /// </summary>
    public override void Process()
    {
        if (_system == null)
        {
            Warn.ProgrammingError("no system to process");
            return;
        }

        Warn.Message("Preprocessing graphical objects...");
        _system.PreProcessing();
    }

    /// <summary>
    /// Chooses where the score's lines end — <c>Paper_score::calc_breaking</c>.
    /// <para>
    /// When the layout names a <c>system-count</c>, that count is solved for exactly;
    /// otherwise the breaker picks the count with the lowest total demerits.
    /// </para>
    /// </summary>
    /// <returns>One solved configuration per line.</returns>
    public List<ColumnXPositions> CalcBreaking()
    {
        ConstrainedBreaking algorithm = new ConstrainedBreaking(this);

        Warn.Message("Calculating line breaks...");

        object systemCountValue = LookupLayout(SystemCountSymbol);
        int systemCount = Bootstrap.SchemeConvert.IsNumber(systemCountValue)
            ? (int)Bootstrap.SchemeConvert.ToDouble(systemCountValue, "system-count")
            : 0;

        if (systemCount != 0)
        {
            return algorithm.Solve(0, ConstrainedBreaking.NoPosition, systemCount);
        }

        return algorithm.BestSolution(0, ConstrainedBreaking.NoPosition);
    }

    /// <summary>
    /// Returns the laid-out lines as <c>paper-system</c> probs, computing them once.
    /// <para>
    /// The three steps are upstream's and the order is load-bearing: choose the breaks,
    /// clone the root system into one piece per line and move each line's columns, then
    /// make those pieces independent of one another by re-pointing every internal link and
    /// every parent at the piece on the same line.
    /// </para>
    /// <para>
    /// Line breaking replaced <c>PlaceColumnsOnOneLine</c> here. That stand-in
    /// spaced the whole score as a single unbroken line — right for music that fits on
    /// one, visibly wrong for anything longer — and its recorded divergence in
    /// PORT-COVERAGE is retired with it.
    /// </para>
    /// </summary>
    /// <returns>The paper systems, in order.</returns>
    public IReadOnlyList<Prob> GetPaperSystems()
    {
        if (_paperSystems != null)
        {
            return _paperSystems;
        }

        List<Prob> systems = new List<Prob>();
        if (_system != null)
        {
            List<ColumnXPositions> breaking = CalcBreaking();
            _system.BreakIntoPieces(breaking);
            Warn.Message("Drawing systems...");
            _system.DoBreakSubstitutionAndFixupRefpoints();
            systems.AddRange(_system.GetPaperSystemsPerLine());
        }

        _paperSystems = systems;
        return _paperSystems;
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description of the score.</returns>
    public override string ToString()
        => "#<Paper_score " + (_system == null ? "empty" : _system.ElementCount + " elements") + ">";

    /// <summary>
    /// Reads a paper dimension off the layout the score is laid out under, falling back
    /// when it is unset — which happens in hand-built fixtures rather than in the real
    /// pipeline, where <c>ly/paper-defaults-init.ly</c> has set them all.
    /// </summary>
    private double Dimension(CodeBrix.LilyScheme.Values.Symbol symbol, double fallback)
    {
        object value = LookupLayout(symbol);
        return Bootstrap.SchemeConvert.IsNumber(value)
            ? Bootstrap.SchemeConvert.ToDouble(value, "paper dimension")
            : fallback;
    }

    private object LookupLayout(CodeBrix.LilyScheme.Values.Symbol symbol)
        => Layout?.LookupVariable(symbol);

    private void FindBreakIndices()
    {
        _columns.Clear();
        _breakIndices.Clear();
        _breakRanks.Clear();

        if (_system == null)
        {
            return;
        }

        _columns.AddRange(_system.UsedColumns());

        for (int i = 0; i < _columns.Count; i++)
        {
            PaperColumn column = _columns[i];

            // The first and last columns are break candidates without needing a
            // prebroken piece on the side that faces off the end of the score.
            if (PaperColumn.IsBreakable(column)
                && (i == 0 || column.FindPrebrokenPiece(Direction.Negative) != null)
                && (i == _columns.Count - 1 || column.FindPrebrokenPiece(Direction.Positive) != null))
            {
                _breakIndices.Add(i);
                _breakRanks.Add(column.GetColumn().Rank);
            }
        }
    }
}
