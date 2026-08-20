// This file is part of python-ly, https://pypi.python.org/pypi/python-ly
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
using Fresco.Brix.Ly.Slexing;

namespace Fresco.Brix.Ly.Lex; //was previously: ly/lex/__init__.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The slexer parser, slightly extended for the ly modes: multiline matching,
/// an argument count, <see cref="Unparsed"/> as the default token, and a mode name
/// on the parsers that begin one.
/// </summary>
public abstract class Parser : Slexing.Parser
{
    /// <summary>Initializes the parser with its class's default argument count.</summary>
    protected Parser()
    {
        Argcount = 0;
    }

    /// <summary>Initializes the parser with an explicit argument count.</summary>
    /// <param name="argcount">The argument count.</param>
    protected Parser(int argcount)
    {
        Argcount = argcount;
    }

    /// <summary>Gets or sets how many arguments remain to be parsed.</summary>
    public int Argcount { get; set; }

    /// <summary>Gets the regex options: multiline, as upstream compiles with
    /// <c>re.MULTILINE | re.UNICODE</c> (.NET is Unicode-native).</summary>
    public override RegexOptions ReFlags => RegexOptions.Multiline;

    /// <summary>Gets the default token rule: <see cref="Unparsed"/>.</summary>
    public override TokenRule Default => Unparsed.Rule;

    /// <summary>Gets the mode this parser begins, or <see langword="null"/>.</summary>
    public virtual string Mode => null;

    /// <summary>Freezes the argument count.</summary>
    /// <returns>The values a thaw hands back to the constructor.</returns>
    public override object[] Freeze() => new object[] { Argcount };
}

/// <summary>The fallthrough variant of the ly-mode parser.</summary>
public abstract class FallthroughParser : Parser
{
    /// <summary>Initializes the parser.</summary>
    protected FallthroughParser()
    {
    }

    /// <summary>Initializes the parser with an explicit argument count.</summary>
    /// <param name="argcount">The argument count.</param>
    protected FallthroughParser(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets whether the pattern anchors at the position: it does.</summary>
    protected override bool Anchored => true;

    /// <summary>Python's <c>re.match</c> at the position.</summary>
    /// <param name="text">The text.</param>
    /// <param name="pos">The position the match must start at.</param>
    /// <returns>The match, or <see langword="null"/>.</returns>
    public override Match Parse(string text, int pos)
    {
        Match match = Pattern.Searcher.Match(text, pos);
        return match.Success && match.Index == pos ? match : null;
    }

    /// <summary>The follow half of the fallthrough behaviour — see
    /// <see cref="Slexing.FallthroughParser.FollowInternal"/>.</summary>
    /// <param name="token">The token being followed.</param>
    /// <param name="state">The state following it.</param>
    /// <returns>Whether the state fell through.</returns>
    public override bool FollowInternal(Slexing.Token token, Slexing.State state)
    {
        System.Type tokenClass = token.GetType();
        foreach (TokenRule rule in ItemRules)
        {
            if (rule.TokenClass == tokenClass)
            {
                return false;
            }
        }

        Fallthrough(state);
        return true;
    }

    /// <summary>Leaves the current parser and lets parsing continue.</summary>
    /// <param name="state">The state to alter.</param>
    /// <returns><see langword="false"/> — parsing continues.</returns>
    public override bool Fallthrough(Slexing.State state)
    {
        state.Leave();
        return false;
    }
}

/// <summary>
/// The slexer state extended with the two ly-mode operations: ending an argument
/// and answering the current mode.
/// </summary>
public class State : Slexing.State
{
    /// <summary>Initializes the state with an initial parser instance.</summary>
    /// <param name="initialParser">The initial parser.</param>
    public State(Slexing.Parser initialParser)
        : base(initialParser)
    {
    }

    /// <summary>Initializes the state over an existing stack — the thaw path.</summary>
    /// <param name="stack">The parser stack, bottom first.</param>
    protected State(List<Slexing.Parser> stack)
        : base(stack)
    {
    }

    /// <summary>
    /// Decreases the argument count and leaves the parser if it would reach 0 —
    /// walking outward so a finished argument can finish the parsers waiting on it.
    /// </summary>
    public void EndArgument()
    {
        while (Depth() > 1)
        {
            if (CurrentParser() is Parser parser)
            {
                if (parser.Argcount == 1)
                {
                    Leave();
                    continue;
                }

                if (parser.Argcount > 0)
                {
                    parser.Argcount -= 1;
                }
            }

            return;
        }
    }

    /// <summary>
    /// Returns the mode of the first parser (from the current one outward) that has
    /// one, or <see langword="null"/>.
    /// </summary>
    /// <returns>The mode name.</returns>
    public string Mode()
    {
        foreach (Slexing.Parser candidate in Parsers())
        {
            if (candidate is Parser parser && parser.Mode != null)
            {
                return parser.Mode;
            }
        }

        return null;
    }

    /// <summary>Reproduces a ly-mode state from a frozen one.</summary>
    /// <param name="frozen">The frozen state.</param>
    /// <returns>The live state.</returns>
    public static new State Thaw(FrozenState frozen) => new State(frozen.ThawStack());
}

/// <summary>The fridge whose thawed states are ly-mode <see cref="State"/>s.</summary>
public class Fridge : Slexing.Fridge
{
    /// <summary>Initializes the fridge.</summary>
    public Fridge()
        : base(State.Thaw)
    {
    }
}
