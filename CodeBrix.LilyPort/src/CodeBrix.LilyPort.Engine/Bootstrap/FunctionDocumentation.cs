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

using System;
using System.Collections.Generic;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap; //was previously: lily/function-documentation.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The registry behind <c>ly:get-all-function-documentation</c>: every documented
/// entry point's argument list and docstring, keyed by name.
/// <para>
/// Upstream populates this table from the <c>LY_DEFINE</c> macro at registration
/// time, so it is exactly as complete as the binding set. The port's bindings take
/// their docstrings from the vendored entry-point table, because
/// <c>documentation-generate.scm</c>'s output can only match the oracle's when every
/// entry documents itself the way upstream does; the docs-parity run grades the
/// result byte for byte.
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

    private static readonly Symbol DocumentationSymbol = Symbol.Intern("documentation");

    /// <summary>
    /// The predicate descriptions upstream's <c>init_func_doc</c> registers — the
    /// human-readable type names the generated Internals Reference prints for
    /// documented predicates. Data, kept verbatim for the documentation run.
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
    /// Loads the vendored extraction of upstream's <c>LY_DEFINE</c> docstrings.
    /// <para>
    /// Upstream's macro hands the name, the stringified argument list and the docstring
    /// to <c>ly_add_function_documentation</c> as the binding is registered, so the
    /// table is exactly as complete as the binding set. A C# lambda has nowhere to carry
    /// a docstring, so the port reads the same three fields from a committed table —
    /// the mechanism <c>GrobInterfaceTable</c> and <c>TranslatorDescriptionTable</c>
    /// already use for data that only exists at C++ compile time.
    /// </para>
    /// <para>
    /// EVERY documented name is recorded, not only the ones this interpreter happens to
    /// bind, because the table is upstream's and the generated manual is compared
    /// against upstream's. Whether the port actually binds each of them is a different
    /// question and a real one — <c>EntryPointDocumentationTests</c> asserts it, so a
    /// documented-but-missing entry point fails a test instead of quietly documenting a
    /// procedure that is not there.
    /// </para>
    /// </summary>
    /// <param name="interpreter">The interpreter whose bound procedures carry the
    /// <c>documentation</c> property half.</param>
    public static void LoadTable(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        foreach (EntryPointDocumentation entry in EntryPointDocumentationTable.All)
        {
            Add(entry.Name, entry.ArgumentList, entry.Documentation);
            SetDocumentationProperty(interpreter, entry);
        }
    }

    /// <summary>
    /// Sets the <c>documentation</c> procedure-property upstream's
    /// <c>ly_add_function_documentation</c> sets alongside the table entry.
    /// <para>
    /// This was recorded as a deliberate divergence — "nothing in the vendored Scheme
    /// layer reads the property where it does read the table" — and that was wrong.
    /// <c>scm/document-functions.scm</c> makes a SECOND pass over every public
    /// procedure in <c>(lily)</c> and documents each one whose
    /// <c>procedure-documentation</c> answers, deliberately skipping names the table
    /// already covers. The 63 entry points LilyPond re-exports under a Scheme name —
    /// <c>(define-public assoc-get ly:assoc-get)</c> and its like — are documented ONLY
    /// through that pass, because the table is keyed by <c>ly:assoc-get</c> while the
    /// module binds <c>assoc-get</c>.
    /// </para>
    /// <para>
    /// The composed string is upstream's, character for character
    /// (<c>lily/function-documentation.cc:66-69</c>): a leading <c>" - LilyPond
    /// procedure: "</c>, the name, a space, the argument list as the C preprocessor
    /// stringified it, a newline, then the docstring — which itself begins with a
    /// newline, and that is what puts the blank line in the manual.
    /// </para>
    /// </summary>
    /// <param name="interpreter">The interpreter holding the binding.</param>
    /// <param name="entry">The entry point's documentation.</param>
    private static void SetDocumentationProperty(
        Interpreter interpreter, EntryPointDocumentation entry)
    {
        Variable variable = interpreter.GuileModule.Lookup(Symbol.Intern(entry.Name));
        if (variable == null || !variable.IsBound || !(variable.GetValue() is Procedure procedure))
        {
            // Not bound here. The port binds every documented entry point, and
            // EntryPointDocumentationTests asserts exactly that — so this is the
            // fence's business, not a place to warn from.
            return;
        }

        string composed = " - LilyPond procedure: " + entry.Name + " "
            + entry.ArgumentList + "\n" + entry.Documentation;
        procedure.Properties = new Pair(
            new Pair(DocumentationSymbol, new MutableString(composed)),
            procedure.Properties);
    }

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
