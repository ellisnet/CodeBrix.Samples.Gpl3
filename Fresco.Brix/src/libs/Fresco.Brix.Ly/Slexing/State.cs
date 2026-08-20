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

using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Fresco.Brix.Ly.Slexing; //was previously: ly/slexer.py (classes State, Fridge);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Maintains state while parsing text: a stack of <see cref="Parser"/> instances,
/// the last one active. The initial parser can never be left.
/// </summary>
public class State
{
    /// <summary>Initializes the state with an initial parser instance.</summary>
    /// <param name="initialParser">The initial parser.</param>
    public State(Parser initialParser)
    {
        Stack = new List<Parser> { initialParser };
    }

    /// <summary>Initializes the state over an existing stack — the thaw path,
    /// which subclasses re-expose so a Fridge can reproduce THEIR class
    /// (upstream's <c>cls.__new__(cls)</c> in the classmethod).</summary>
    /// <param name="stack">The parser stack, bottom first.</param>
    protected State(List<Parser> stack)
    {
        Stack = stack;
    }

    /// <summary>Gets the parser stack, bottom first. Upstream's <c>state</c> list.</summary>
    protected List<Parser> Stack { get; }

    /// <summary>Returns the currently active parser instance.</summary>
    /// <returns>The parser.</returns>
    public Parser CurrentParser() => Stack[Stack.Count - 1];

    /// <summary>Returns all active parsers, the most current one first.</summary>
    /// <returns>The parsers.</returns>
    public IReadOnlyList<Parser> Parsers()
    {
        List<Parser> reversed = new List<Parser>(Stack);
        reversed.Reverse();
        return reversed;
    }

    /// <summary>
    /// Parses a text string using the state, yielding tokens as found, updating the
    /// state as it goes — upstream's generator, decision for decision, including the
    /// default-token spans for text the active parser's items skipped and the final
    /// default token after the loop.
    /// </summary>
    /// <param name="text">The text to parse.</param>
    /// <param name="pos">Where to start.</param>
    /// <returns>The tokens.</returns>
    public IEnumerable<Token> Tokens(string text, int pos = 0)
    {
        Parser parser;
        while (true)
        {
            parser = CurrentParser();
            Match m = parser.Parse(text, pos);
            if (m != null)
            {
                if (parser.Default != null && pos < m.Index)
                {
                    Token defaultToken = parser.Default.Create(
                        text.Substring(pos, m.Index - pos), pos);
                    defaultToken.UpdateState(this);
                    yield return defaultToken;
                }

                Token token = parser.MakeToken(m);
                token.UpdateState(this);
                yield return token;
                pos = m.Index + m.Length;
            }
            else if (pos == text.Length || parser.Fallthrough(this))
            {
                break;
            }
        }

        if (parser.Default != null && pos < text.Length)
        {
            Token tail = parser.Default.Create(text.Substring(pos), pos);
            tail.UpdateState(this);
            yield return tail;
        }
    }

    /// <summary>Enters a new parser.</summary>
    /// <param name="parser">The parser instance to enter.</param>
    public void Enter(Parser parser) => Stack.Add(parser);

    /// <summary>Leaves the current parser; the first parser is never left.</summary>
    public void Leave()
    {
        if (Stack.Count > 1)
        {
            Stack.RemoveAt(Stack.Count - 1);
        }
    }

    /// <summary>Replaces the current parser — unlike <see cref="Leave"/> this can
    /// also replace the first one.</summary>
    /// <param name="parser">The replacement.</param>
    public void Replace(Parser parser) => Stack[Stack.Count - 1] = parser;

    /// <summary>Returns the number of parsers currently active (1 or more).</summary>
    /// <returns>The depth.</returns>
    public int Depth() => Stack.Count;

    /// <summary>
    /// Acts as if the token has been instantiated with the current state — for
    /// following already-parsed (cached) tokens, with the fallthrough machinery
    /// handled the way <see cref="Token.UpdateState"/> alone cannot.
    /// </summary>
    /// <param name="token">The token to follow.</param>
    public void Follow(Token token)
    {
        while (CurrentParser().FollowInternal(token, this))
        {
        }

        token.UpdateState(this);
    }

    /// <summary>Returns the current state as a hashable, comparable value.</summary>
    /// <returns>The frozen state.</returns>
    public FrozenState Freeze() => new FrozenState(Stack);

    /// <summary>Reproduces a state from a frozen one.</summary>
    /// <param name="frozen">The frozen state.</param>
    /// <returns>The live state.</returns>
    public static State Thaw(FrozenState frozen) => new State(frozen.ThawStack());
}
