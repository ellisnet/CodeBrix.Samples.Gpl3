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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/tie-configuration.cc, lily/include/tie-configuration.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - upstream's Tie_configuration is a VALUE: Ties_configuration is a
//     std::vector<Tie_configuration>, so every push_back, every copy-construction of a
//     variant and every assignment from the possibilities_ cache makes a fresh copy, and
//     the cached configuration is deliberately mutated in one place before being copied
//     (generate_ties_configuration's manual delta_y). A C# class shares instead of
//     copying, which would let one variant's score leak into another, so every one of
//     those value copies is written out as an explicit Copy() and nothing here is ever
//     aliased into a Ties_configuration.
//   - upstream's Ties_configuration derives PRIVATELY from std::vector and re-exports a
//     named subset; the port composes a List and exposes the same subset, which is the
//     same contract without the inheritance.

/// <summary>
/// One candidate placement for a single tie: where it sits, which way it bends, and what
/// that costs.
/// </summary>
public sealed class TieConfiguration
{
    /// <summary>The staff position, in half staff spaces.</summary>
    public int Position;

    /// <summary>Which way the tie bends.</summary>
    public Direction Dir;

    /// <summary>A vertical nudge on top of <see cref="Position"/>, in output units.</summary>
    public double DeltaY;

    /// <summary>The paper-column ranks the tie spans.</summary>
    public DrulArray<int> ColumnRanks;

    /// <summary>Where the tie attaches horizontally. Computed, not chosen.</summary>
    public Interval AttachmentX;

    private string _scoreCard = string.Empty;
    private double _score;
    private bool _scored;

    /// <summary>Initializes an unscored configuration with upstream's defaults.</summary>
    public TieConfiguration()
    {
        Dir = Direction.Center;
        Position = 0;
        DeltaY = 0.0;
        _score = 0.0;
        _scored = false;
    }

    /// <summary>Gets the demerits accumulated so far.</summary>
    /// <returns>The score.</returns>
    public double Score() => _score;

    /// <summary>Gets the human-readable breakdown of the score.</summary>
    /// <returns>The score card.</returns>
    public string Card() => _scoreCard;

    /// <summary>Gets a value indicating whether this configuration has been scored.</summary>
    /// <returns><see langword="true"/> once scoring has run.</returns>
    public bool IsScored() => _scored;

    /// <summary>Marks this configuration as scored.</summary>
    public void SetScored() => _scored = true;

    /// <summary>Returns an independent copy, which is what upstream's value semantics give.</summary>
    /// <returns>The copy.</returns>
    public TieConfiguration Copy()
    {
        TieConfiguration copy = new TieConfiguration();
        copy.Position = Position;
        copy.Dir = Dir;
        copy.DeltaY = DeltaY;
        copy.ColumnRanks = ColumnRanks;
        copy.AttachmentX = AttachmentX;
        copy._scoreCard = _scoreCard;
        copy._score = _score;
        copy._scored = _scored;
        return copy;
    }

    /// <summary>Adds demerits, recording the reason on the score card.</summary>
    /// <param name="s">The demerits.</param>
    /// <param name="desc">Why they were added.</param>
    public void AddScore(double s, string desc)
    {
        Debug.Assert(!_scored, "scoring a configuration that is already scored");
        _score += s;
        if (s != 0.0)
        {
            _scoreCard += desc + "=" + s.ToString("F2", CultureInfo.InvariantCulture) + " ";
        }
    }

    /// <summary>Shifts the tie so its curve is vertically centred on its position.</summary>
    /// <param name="details">The tie details.</param>
    public void CenterTieVertically(TieDetails details)
    {
        Bezier b = GetUntransformedBezier(details);
        Offset middle = b.CurvePoint(0.5);
        Offset edge = b.CurvePoint(0.0);
        double center = (edge[Axis.Y] + middle[Axis.Y]) / 2.0;

        DeltaY = -(int)Dir * center;
    }

    /// <summary>Returns the tie's curve, placed where the configuration puts it.</summary>
    /// <param name="details">The tie details.</param>
    /// <returns>The curve.</returns>
    public Bezier GetTransformedBezier(TieDetails details)
    {
        Bezier b = GetUntransformedBezier(details);

        b.Scale(1, (int)Dir);
        b.Translate(new Offset(
            AttachmentX[Direction.Negative],
            DeltaY + (details.StaffSpace * 0.5 * Position)));

        return b;
    }

    /// <summary>Returns the tie's curve with its left control point at the origin.</summary>
    /// <param name="details">The tie details.</param>
    /// <returns>The curve.</returns>
    public Bezier GetUntransformedBezier(TieDetails details)
    {
        double l = AttachmentX.Length;
        if (double.IsInfinity(l) || double.IsNaN(l))
        {
            Warn.ProgrammingError("Inf or NaN encountered");
            l = 1.0;
        }

        return BezierBow.SlurShape(l, details.HeightLimit, details.Ratio);
    }

    /// <summary>Returns how many paper columns the tie spans.</summary>
    /// <returns>The span, zero for a semi-tie.</returns>
    public int ColumnSpanLength() => ColumnRanks[Direction.Positive] - ColumnRanks[Direction.Negative];

    /// <summary>Returns how high the tie's curve rises.</summary>
    /// <param name="details">The tie details.</param>
    /// <returns>The height.</returns>
    public double Height(TieDetails details)
    {
        double l = AttachmentX.Length;

        return BezierBow.SlurShape(l, details.HeightLimit, details.Ratio).CurvePoint(0.5)[Axis.Y];
    }

    /// <summary>Returns a signed measure of how far apart two configurations sit.</summary>
    /// <param name="a">The first configuration.</param>
    /// <param name="b">The second configuration.</param>
    /// <returns>The distance.</returns>
    public static double Distance(TieConfiguration a, TieConfiguration b)
    {
        double d = 3 * (a.Position - b.Position);
        if (d < 0)
        {
            return d + (2 + ((int)b.Dir - (int)a.Dir));
        }

        return d + (2 + ((int)a.Dir - (int)b.Dir));
    }
}

/// <summary>
/// A placement for every tie in a chord, scored as a whole.
/// </summary>
public sealed class TiesConfiguration
{
    private readonly List<TieConfiguration> _ties = new List<TieConfiguration>();
    private readonly List<string> _tieScoreCards = new List<string>();
    private double _score;
    private string _scoreCard = string.Empty;
    private bool _scored;

    /// <summary>Initializes an empty, unscored configuration.</summary>
    public TiesConfiguration()
    {
        _score = 0.0;
        _scored = false;
    }

    /// <summary>Returns a deep copy — upstream copy-constructs a vector of values.</summary>
    /// <returns>The copy.</returns>
    public TiesConfiguration Copy()
    {
        TiesConfiguration copy = new TiesConfiguration();
        foreach (TieConfiguration tie in _ties)
        {
            copy._ties.Add(tie.Copy());
        }

        copy._tieScoreCards.AddRange(_tieScoreCards);
        copy._score = _score;
        copy._scoreCard = _scoreCard;
        copy._scored = _scored;
        return copy;
    }

    /// <summary>Gets how many ties this configuration places.</summary>
    public int Count => _ties.Count;

    /// <summary>Gets a value indicating whether no tie is placed.</summary>
    public bool IsEmpty => _ties.Count == 0;

    /// <summary>Gets the first tie's configuration.</summary>
    /// <returns>The configuration.</returns>
    public TieConfiguration Front() => _ties[0];

    /// <summary>Gets the last tie's configuration.</summary>
    /// <returns>The configuration.</returns>
    public TieConfiguration Back() => _ties[_ties.Count - 1];

    /// <summary>Gets or sets one tie's configuration.</summary>
    /// <param name="index">Which tie.</param>
    /// <returns>The configuration.</returns>
    public TieConfiguration this[int index]
    {
        get => _ties[index];
        set => _ties[index] = value;
    }

    /// <summary>Appends a tie's configuration.</summary>
    /// <param name="configuration">The configuration to append.</param>
    public void PushBack(TieConfiguration configuration) => _ties.Add(configuration);

    /// <summary>Gets a value indicating whether this configuration has been scored.</summary>
    /// <returns><see langword="true"/> once scoring has run.</returns>
    public bool IsScored() => _scored;

    /// <summary>Marks this configuration as scored.</summary>
    public void SetScored() => _scored = true;

    /// <summary>Gets the total demerits.</summary>
    /// <returns>The score.</returns>
    public double Score() => _score;

    /// <summary>Clears the score so the configuration can be re-scored as a variant.</summary>
    public void ResetScore()
    {
        _score = 0.0;
        _scored = false;
        _scoreCard = string.Empty;
        _tieScoreCards.Clear();
    }

    /// <summary>Adds demerits against the configuration as a whole.</summary>
    /// <param name="amount">The demerits.</param>
    /// <param name="description">Why they were added.</param>
    public void AddScore(double amount, string description)
    {
        Debug.Assert(!_scored, "scoring a ties configuration that is already scored");
        _score += amount;
        if (amount != 0.0)
        {
            _scoreCard += description + "="
                          + amount.ToString("F2", CultureInfo.InvariantCulture) + " ";
        }
    }

    /// <summary>Adds demerits against one tie.</summary>
    /// <param name="amount">The demerits.</param>
    /// <param name="i">Which tie.</param>
    /// <param name="description">Why they were added.</param>
    public void AddTieScore(double amount, int i, string description)
    {
        Debug.Assert(!_scored, "scoring a ties configuration that is already scored");
        _score += amount;
        if (amount != 0.0)
        {
            while (_tieScoreCards.Count < Count)
            {
                _tieScoreCards.Add(string.Empty);
            }

            _tieScoreCards[i] += description + "="
                                 + amount.ToString("F2", CultureInfo.InvariantCulture) + " ";
        }
    }

    /// <summary>Gets the whole-configuration part of the score breakdown.</summary>
    /// <returns>The score card.</returns>
    public string Card() => _scoreCard;

    /// <summary>Gets one tie's part of the score breakdown.</summary>
    /// <param name="i">Which tie.</param>
    /// <returns>The score card.</returns>
    public string TieCard(int i) => _tieScoreCards[i];

    /// <summary>Gets one tie's full score breakdown, including any aggregates.</summary>
    /// <param name="i">Which tie.</param>
    /// <returns>The score card.</returns>
    public string CompleteTieCard(int i)
    {
        string s = string.Empty;
        s += this[i].Position.ToString(CultureInfo.InvariantCulture)
             + " (" + this[i].DeltaY.ToString("F2", CultureInfo.InvariantCulture) + ") "
             + (this[i].Dir == Direction.Positive ? "u" : "d") + ": "
             + this[i].Card() + SafeTieCard(i);

        // this is a little awkward, but we must decide where to put aggregrates.
        if (i == 0)
        {
            s += Card();
        }

        if (i + 1 == Count)
        {
            s += "TOTAL=" + Score().ToString("F2", CultureInfo.InvariantCulture);
        }

        return s;
    }

    /// <summary>Gets every tie's full score breakdown, concatenated.</summary>
    /// <returns>The score card.</returns>
    public string CompleteScoreCard()
    {
        string s = string.Empty;
        for (int i = 0; i < Count; i++)
        {
            s += CompleteTieCard(i);
        }

        return s;
    }

    // upstream indexes tie_score_cards_ directly; it is only ever grown by add_tie_score,
    // so a tie that never collected demerits has no entry at all and must read as "".
    private string SafeTieCard(int i) => i < _tieScoreCards.Count ? _tieScoreCards[i] : string.Empty;
}
