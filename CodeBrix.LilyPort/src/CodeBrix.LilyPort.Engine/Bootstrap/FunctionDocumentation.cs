/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2004--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap; //was previously: lily/function-documentation.cc;

// Modified by Jeremy Ellis on 2026-08-05 as part of the CodeBrix port.

/// <summary>
/// The registry behind <c>ly:get-all-function-documentation</c>: every documented
/// entry point's argument list and docstring, keyed by name.
/// <para>
/// Upstream populates this table from the <c>LY_DEFINE</c> macro at registration
/// time, so it is exactly as complete as the binding set. The port's bindings carry
/// no docstrings yet — EPG24 owes them, because <c>documentation-generate.scm</c>'s
/// output can only match the oracle's when every entry documents itself the way
/// upstream does. Until then the table is honestly sparse rather than absent: the
/// MECHANISM is ported, the CONTENT arrives with the bindings that declare it.
/// </para>
/// <para>
/// Upstream also stores each docstring as the procedure's <c>documentation</c>
/// procedure-property. The port keeps the table only — nothing in the vendored
/// Scheme layer reads the property where it does read the table — recorded in
/// PORT-COVERAGE under DIVERGENCES. <c>ly_check_name</c>, a compile-time guard
/// against C++/Scheme name drift, has no meaning without C++ name mangling and is
/// recorded the same way.
/// </para>
/// </summary>
public static class FunctionDocumentation
{
    private static readonly SchemeHashTable DocTable = new SchemeHashTable(null);

    /// <summary>
    /// The predicate descriptions upstream's <c>init_func_doc</c> registers — the
    /// human-readable type names the generated Internals Reference prints for
    /// documented predicates. Data, kept verbatim for EPG24.
    /// </summary>
    private static readonly Dictionary<string, string> PredicateDescriptions
        = new Dictionary<string, string>
        {
            ["short"] = "integer in [-2^15, 2^15)",
            ["int"] = "integer in [-2^31, 2^31)",
            ["long"] = "integer in [-2^63, 2^63)",
            ["long long"] = "integer in [-2^63, 2^63)",
            ["unsigned short"] = "integer in [0, 2^16)",
            ["unsigned"] = "integer in [0, 2^32)",
            ["unsigned long"] = "integer in [0, 2^64)",
            ["unsigned long long"] = "integer in [0, 2^64)",
            ["number pair"] = "number pair",
            ["axis"] = "axis",
            ["direction"] = "direction",
            ["offset"] = "pair of reals",
            ["bezier"] = "list of four number pairs",
            ["skyline pair"] = "pair of skylines",
            ["list"] = "list",
            ["port"] = "port",
            ["procedure"] = "procedure",
            ["symbol"] = "symbol",
            ["boolean"] = "boolean",
            ["bytevector"] = "bytevector",
            ["integer"] = "integer",
            ["number"] = "number",
            ["pair"] = "pair",
            ["rational"] = "rational",
            ["real"] = "real number",
            ["string"] = "string",
            ["vector"] = "vector",
        };

    /// <summary>Gets the table <c>ly:get-all-function-documentation</c> answers.</summary>
    public static SchemeHashTable Table => DocTable;

    /// <summary>
    /// Records one entry point's documentation — upstream's
    /// <c>ly_add_function_documentation</c>, minus the procedure-property half.
    /// </summary>
    /// <param name="name">The Scheme name, e.g. <c>ly:make-score</c>.</param>
    /// <param name="varlist">The argument list as printed in the docs.</param>
    /// <param name="doc">The docstring; an empty one records nothing.</param>
    public static void Add(string name, string varlist, string doc)
    {
        if (string.IsNullOrEmpty(doc))
        {
            return;
        }

        DocTable.Set(
            Symbol.Intern(name),
            new Pair(new MutableString(varlist ?? string.Empty), new MutableString(doc)));
    }

    /// <summary>
    /// Looks up the printable description of a predicate type — upstream's
    /// <c>init_func_doc</c> data.
    /// </summary>
    /// <param name="key">The type's key, e.g. <c>int</c> or <c>number pair</c>.</param>
    /// <returns>The description, or <see langword="null"/>.</returns>
    public static string DescribePredicate(string key)
        => key != null && PredicateDescriptions.TryGetValue(key, out string text) ? text : null;
}
