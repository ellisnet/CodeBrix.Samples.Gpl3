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

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

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

    private SystemGrob _system;
    private List<Prob> _paperSystems;
    private bool _columnsPlaced;

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
    /// Solves the spacing problem for the whole score as ONE line and moves every column
    /// to where the solution puts it.
    /// <para>
    /// STAND-IN, recorded in PORT-COVERAGE. Upstream runs <c>calc_breaking</c> and
    /// <c>System::break_into_pieces</c>, which clone the root system once per line and
    /// place each line's columns. Line breaking is EPG15 and page layout EPG16; until
    /// they land the score is spaced as a single unbroken line, which is the right
    /// answer for music that fits on one and a visibly wrong one — not a silently
    /// plausible one — for anything longer.
    /// </para>
    /// <para>
    /// The part that IS upstream's, verbatim in shape, is what happens per column:
    /// translate by the solved configuration, record the system, then drape the loose
    /// columns back around the solved ones.
    /// </para>
    /// </summary>
    public void PlaceColumnsOnOneLine()
    {
        if (_system == null || _columnsPlaced)
        {
            return;
        }

        _columnsPlaced = true;

        List<PaperColumn> columns = _system.UsedColumns();
        if (columns.Count < 2)
        {
            return;
        }

        double lineWidth = Dimension(LineWidthSymbol, 100.0);
        double indent = Dimension(IndentSymbol, 0.0);
        bool ragged = SchemeUtilities.ToBool(LookupLayout(RaggedRightSymbol));

        ColumnXPositions positions = LineSpacing.GetLineConfiguration(
            columns, lineWidth, indent, ragged);

        for (int j = 0; j < positions.Columns.Count && j < positions.Configuration.Count; j++)
        {
            PaperColumn column = positions.Columns[j];
            column.TranslateAxis(positions.Configuration[j], Axis.X);
            column.System = _system;

            // The solver's first and last entries are PREBROKEN PIECES — that is what
            // begins and ends a line. Upstream can translate those alone because
            // break_into_pieces builds a system bounded by them and break substitution
            // then re-points every grob at the piece it belongs with. Neither exists
            // yet (EPG15), so the grobs are still hanging off the ORIGINAL columns, and
            // moving only the clone would leave the music where it was. The original
            // moves with its piece until break substitution can do the real job.
            // Recorded in PORT-COVERAGE with the rest of the single-line stand-in.
            PaperColumn original = column.Original;
            if (original != null && !positions.Columns.Contains(original))
            {
                original.TranslateAxis(positions.Configuration[j], Axis.X);
                original.System = _system;
            }
        }

        LooseColumns.SetLooseColumns(_system, positions);
    }

    /// <summary>
    /// Returns the laid-out lines as <c>paper-system</c> probs, computing them once.
    /// <para>
    /// DIVERGENCE, recorded in PORT-COVERAGE, and a load-bearing one. Upstream runs
    /// <c>calc_breaking</c> and <c>System::break_into_pieces</c> first, so the answer is
    /// one paper system PER LINE. Line breaking is EPG15; until it lands the root system
    /// is never broken, so this returns the single unbroken line. That is the right
    /// answer for music that fits on one line and the wrong one for anything longer —
    /// which is exactly what the regression ratchet will report, by file, rather than
    /// something silently plausible.
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
            PlaceColumnsOnOneLine();
            Warn.Message("Drawing systems...");
            systems.Add(_system.GetPaperSystem());
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
