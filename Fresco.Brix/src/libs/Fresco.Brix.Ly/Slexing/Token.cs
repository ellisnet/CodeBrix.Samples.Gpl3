// === Python slexer (Stateful Lexer) module ===
//
// Copyright (c) 2008 - 2015 by Wilbert Berendsen
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation; either version 3
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

namespace Fresco.Brix.Ly.Slexing; //was previously: ly/slexer.py (class Token);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A parsed piece of text; the SUBCLASS determines the type.
/// <para>
/// Upstream's Token IS a Python string subclass with <c>pos</c> and <c>end</c>
/// attributes. Here the text is carried as <see cref="Text"/>, and code that
/// compared a token to a string compares <see cref="Text"/>; code that asked
/// <c>isinstance</c> uses a C# type test, because the class hierarchy is
/// preserved one for one.
/// </para>
/// </summary>
public abstract class Token
{
    /// <summary>Initializes the token over its matched text.</summary>
    /// <param name="text">The matched text.</param>
    /// <param name="pos">The position in the parsed string.</param>
    protected Token(string text, int pos)
    {
        Text = text ?? string.Empty;
        Pos = pos;
    }

    /// <summary>Gets the token's text.</summary>
    public string Text { get; }

    /// <summary>Gets the position of the first character in the parsed string.</summary>
    public int Pos { get; }

    /// <summary>Gets the position just past the last character.</summary>
    public int End => Pos + Text.Length;

    /// <summary>Gets the text's length, as Python's <c>len(token)</c> answers it.</summary>
    public int Length => Text.Length;

    /// <summary>
    /// Lets the token update the state, e.g. enter a different parser.
    /// <para>
    /// Called by the <see cref="State"/> upon instantiation of the token, BEFORE the
    /// token is yielded — the order upstream establishes and the mode parsers rely
    /// on. The default implementation lets the current parser decide
    /// (<see cref="Parser.UpdateState"/>), exactly as upstream's does.
    /// </para>
    /// </summary>
    /// <param name="state">The state to update.</param>
    public virtual void UpdateState(State state) => state.CurrentParser().UpdateState(state, this);

    /// <summary>Answers the token's text, so formatting mirrors the Python string.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Text;
}
