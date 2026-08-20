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

using Fresco.Brix.Ly.Slexing;

namespace Fresco.Brix.Ly.Lex.DocbookMode; //was previously: ly/lex/docbook.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.
// Parses and tokenizes DocBook input, recognizing LilyPond in DocBook.

/// <summary>Parses DocBook from the toplevel.</summary>
public class ParseDocBook : Lex.Parser
{
    private static readonly TokenRule[] ParserItems =
    {
        Lex.Space.Rule,
    };

    /// <summary>Initializes the parser.</summary>
    public ParseDocBook()
    {
    }

    /// <summary>Initializes the parser with an argument count (thaw path).</summary>
    /// <param name="argcount">The argument count.</param>
    public ParseDocBook(int argcount)
        : base(argcount)
    {
    }

    /// <summary>Gets the mode this parser begins.</summary>
    public override string Mode => "docbook";

    /// <summary>Gets the token rules, in the upstream items order.</summary>
    protected override TokenRule[] Items => ParserItems;
}
