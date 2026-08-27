// === Python slexer (Stateful Lexer) module ===
//
// Copyright (c) 2008 - 2015 by Wilbert Berendsen
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation, either version 3
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
// See http://www.gnu.org/licenses/ for more information.

using System;
using System.Collections.Generic;

namespace Fresco.Brix.Ly.Slexing; //was previously: ly/slexer.py (State.freeze/thaw's tuple);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A state frozen to a comparable, hashable value — upstream's tuple of
/// (parser class, frozen instance values) pairs, which a <see cref="Fridge"/>
/// stores and looks up by equality.
/// </summary>
public sealed class FrozenState : IEquatable<FrozenState>
{
    private readonly Type[] _parserTypes;
    private readonly object[][] _values;
    private readonly int _hash;

    /// <summary>Freezes a parser stack.</summary>
    /// <param name="stack">The stack, bottom first.</param>
    public FrozenState(IReadOnlyList<Parser> stack)
    {
        _parserTypes = new Type[stack.Count];
        _values = new object[stack.Count][];
        for (int i = 0; i < stack.Count; i++)
        {
            _parserTypes[i] = stack[i].GetType();
            _values[i] = stack[i].Freeze();
        }

        HashCode hash = default;
        for (int i = 0; i < _parserTypes.Length; i++)
        {
            hash.Add(_parserTypes[i]);
            foreach (object value in _values[i])
            {
                hash.Add(value);
            }
        }

        _hash = hash.ToHashCode();
    }

    /// <summary>Reproduces the live parser stack.</summary>
    /// <returns>The stack, bottom first.</returns>
    public List<Parser> ThawStack()
    {
        List<Parser> stack = new List<Parser>(_parserTypes.Length);
        for (int i = 0; i < _parserTypes.Length; i++)
        {
            stack.Add(Parser.Thaw(_parserTypes[i], _values[i]));
        }

        return stack;
    }

    /// <summary>Structural equality over the parser classes and their values.</summary>
    /// <param name="other">The frozen state to compare with.</param>
    /// <returns>Whether the two freeze the same state.</returns>
    public bool Equals(FrozenState other)
    {
        if (other == null || other._parserTypes.Length != _parserTypes.Length)
        {
            return false;
        }

        for (int i = 0; i < _parserTypes.Length; i++)
        {
            if (other._parserTypes[i] != _parserTypes[i]
                || other._values[i].Length != _values[i].Length)
            {
                return false;
            }

            for (int j = 0; j < _values[i].Length; j++)
            {
                if (!Equals(other._values[i][j], _values[i][j]))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>Structural equality.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>Whether it is an equal frozen state.</returns>
    public override bool Equals(object obj) => Equals(obj as FrozenState);

    /// <summary>The structural hash.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => _hash;
}

/// <summary>Stores frozen states under an integer number.</summary>
public class Fridge
{
    private readonly List<FrozenState> _states = new List<FrozenState>();
    private readonly Func<FrozenState, State> _thaw;

    /// <summary>Initializes a fridge thawing plain <see cref="State"/>s.</summary>
    public Fridge()
        : this(State.Thaw)
    {
    }

    /// <summary>Initializes a fridge with a state factory — upstream's
    /// <c>stateClass</c> argument, so a subclassed State thaws as itself.</summary>
    /// <param name="thaw">The factory reproducing a state from a frozen one.</param>
    public Fridge(Func<FrozenState, State> thaw)
    {
        _thaw = thaw;
    }

    /// <summary>Stores a state and returns an identifying integer.</summary>
    /// <param name="state">The state to store.</param>
    /// <returns>The number it is stored under.</returns>
    public int Freeze(State state)
    {
        FrozenState frozen = state.Freeze();
        int index = _states.IndexOf(frozen);
        if (index >= 0)
        {
            return index;
        }

        _states.Add(frozen);
        return _states.Count - 1;
    }

    /// <summary>Returns the state stored under a number, or <see langword="null"/>.</summary>
    /// <param name="number">The number.</param>
    /// <returns>The thawed state, or <see langword="null"/>.</returns>
    public State Thaw(int number)
        => number >= 0 && number < _states.Count ? _thaw(_states[number]) : null;

    /// <summary>Returns the number of stored frozen states.</summary>
    /// <returns>The count.</returns>
    public int Count() => _states.Count;
}
