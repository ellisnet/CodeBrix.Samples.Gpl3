/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Jan Nieuwenhuizen <janneke@gnu.org>
  Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/lily-guile.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The association-list and type-checking operations the object model is built on.
/// <para>
/// LilyPond stores every property of every music object, grob and context in a Scheme
/// alist, and reaches it through these. Keeping them here rather than scattering
/// <c>assq</c> walks through the engine is what makes the property layer swappable.
/// </para>
/// </summary>
public static class SchemeUtilities
{
    private static readonly Symbol OriginSymbol = Symbol.Intern("origin");
    private static readonly Symbol BackendTypeCheckSymbol = Symbol.Intern("backend-type?");
    private static readonly Symbol TypeNameSymbol = Symbol.Intern("type-name");

    /// <summary>Looks a key up in an association list by identity — <c>assq</c>.</summary>
    /// <param name="key">The key to find.</param>
    /// <param name="alist">The association list.</param>
    /// <returns>The matching pair, or <see langword="null"/> when absent.</returns>
    /// <remarks>
    /// Identity is Guile's <c>eq?</c>, NOT raw reference equality, which is what
    /// <see cref="ReferenceComparer"/> implements: Guile fixnums, booleans and characters
    /// are IMMEDIATES rather than heap objects, so <c>(eq? 1 1)</c> is true there and
    /// <c>(assq 1 '((1 . a)))</c> finds its entry. Corrected here — this
    /// had compared with <c>ReferenceEquals</c>, under which a boxed number is never equal
    /// to another boxed number of the same value, so EVERY numerically-keyed alist lookup
    /// in the engine silently missed. <c>Ottava_spanner_engraver</c> reading
    /// <c>ottavationMarkups</c>, which is keyed by octave count, is what exposed it.
    /// </remarks>
    public static Pair Assq(object key, object alist)
    {
        object cursor = alist;
        while (cursor is Pair pair)
        {
            if (pair.Car is Pair entry && ReferenceComparer.Instance.Equals(entry.Car, key))
            {
                return entry;
            }

            cursor = pair.Cdr;
        }

        return null;
    }

    /// <summary>
    /// Looks a key up in a CHAIN of association lists, returning the first match.
    /// <para>
    /// The property alist chain is how a grob's own settings, its type's defaults and
    /// the layout's defaults are consulted in that order without any of them being
    /// merged. <see cref="Grob.GetPropertyAlistChain"/> builds the chain; this reads it.
    /// </para>
    /// </summary>
    /// <param name="key">The key to find.</param>
    /// <param name="chain">A list of association lists.</param>
    /// <param name="fallback">What to answer when no list in the chain has the key.</param>
    /// <returns>The value, or the fallback.</returns>
    public static object ChainAssocGet(object key, object chain, object fallback)
    {
        object cursor = chain;
        while (cursor is Pair pair)
        {
            Pair entry = Assq(key, pair.Car);
            if (entry != null)
            {
                return entry.Cdr;
            }

            cursor = pair.Cdr;
        }

        return fallback;
    }

    /// <summary>
    /// Looks a key up in ONE association list, answering a fallback when it is absent —
    /// <c>ly_assoc_get</c>.
    /// </summary>
    /// <param name="key">The key to find.</param>
    /// <param name="alist">The association list.</param>
    /// <param name="fallback">What to answer when the key is absent.</param>
    /// <returns>The value, or the fallback.</returns>
    /// <remarks>
    /// Goes through <see cref="LyAssoc"/>, exactly as upstream's <c>ly:assoc-get</c> does.
    /// It formerly went straight to <see cref="Assq"/> and carried a recorded NARROWING for
    /// keys that are neither symbols nor immediates; the tablature group closed it when
    /// <c>Drum_notes_engraver</c> became the engine's first caller to look a key up with
    /// upstream's own branch.
    /// </remarks>
    public static object LyAssocGet(object key, object alist, object fallback)
    {
        Pair entry = LyAssoc(key, alist);
        return entry != null ? entry.Cdr : fallback;
    }

    /// <summary>
    /// <c>ly_assoc</c>: looks a key up in an alist, comparing with <c>eq?</c> when the key
    /// is a symbol or an immediate and with <c>equal?</c> otherwise.
    /// </summary>
    /// <param name="key">The key to find.</param>
    /// <param name="alist">The association list.</param>
    /// <returns>The matching entry, or <see langword="null"/> when there is none.</returns>
    /// <remarks>
    /// The branch is upstream's own (<c>lily-guile.hh</c>) and is not an optimisation:
    /// <c>assq</c> on a string key would miss every time, and <c>assoc</c> on a symbol key
    /// would go through the general comparator for no reason.
    /// </remarks>
    public static Pair LyAssoc(object key, object alist)
    {
        // SCM_IMP: the values Guile represents without a heap cell. The set is the same
        // one ReferenceComparer already treats as eq?-by-value, plus '().
        bool immediate = key is Symbol || key is long || key is bool
                         || key is SchemeChar || key is Nil || key == null;

        if (immediate)
        {
            return Assq(key, alist);
        }

        object cursor = alist;
        while (cursor is Pair pair)
        {
            if (pair.Car is Pair entry && IsEqual(entry.Car, key))
            {
                return entry;
            }

            cursor = pair.Cdr;
        }

        return null;
    }

    /// <summary>
    /// Copies a list and puts a tail on the end — <c>ly_append</c>.
    /// </summary>
    /// <param name="list">The list whose elements are copied.</param>
    /// <param name="tail">What the copy's last pair points at.</param>
    /// <returns>The joined list.</returns>
    /// <remarks>
    /// The first list is copied and the second is SHARED, which is <c>scm_append</c>'s
    /// own contract — callers that go on to mutate the tail would see it through both.
    /// </remarks>
    public static object LyAppend(object list, object tail)
    {
        List<object> items = Pair.ToList(list);
        object result = tail;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            result = new Pair(items[i], result);
        }

        return result;
    }

    /// <summary>Determines whether a list contains a value, compared by identity.</summary>
    /// <param name="value">The value to look for.</param>
    /// <param name="list">The list to search.</param>
    /// <returns><see langword="true"/> when the value is present.</returns>
    public static bool Memq(object value, object list)
    {
        object cursor = list;
        while (cursor is Pair pair)
        {
            if (ReferenceEquals(pair.Car, value))
            {
                return true;
            }

            cursor = pair.Cdr;
        }

        return false;
    }

    /// <summary>
    /// Determines whether a value can be called, which is <c>ly_is_procedure</c>.
    /// <para>An applicable smob answers <c>procedure?</c> in Guile too, so both a
    /// <see cref="Procedure"/> and an <see cref="IApplicable"/> count.</para>
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> when the value can be applied.</returns>
    public static bool IsProcedure(object value) => value is Procedure || value is IApplicable;

    /// <summary>
    /// Answers Guile's <c>procedure-with-setter?</c> — an object property built by
    /// <c>make-object-property</c> is one, and that is how upstream decides whether a
    /// property category has a deprecation table at all.
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> when the value carries a setter.</returns>
    public static bool IsProcedureWithSetter(object value)
        => value is Procedure procedure && procedure.Setter != null;

    /// <summary>
    /// Calls a Scheme procedure through the ambient interpreter.
    /// <para>
    /// Answers the empty list when there is no interpreter or the value is not a
    /// procedure, rather than throwing: the engine reads callbacks out of property
    /// alists, where "no callback" is an ordinary state and not an error.
    /// </para>
    /// <para>
    /// The callable test is <see cref="IsProcedure"/>'s, exactly — until 2026-08-12 it
    /// accepted only <see cref="Procedure"/> and silently answered the empty list for a
    /// Scheme-defined closure (an <see cref="IApplicable"/>), which is how every
    /// toplevel <c>\markup \score</c> book rendered ZERO systems: the walk handed
    /// <c>interpret-markup-list</c> — a vendored Scheme lambda — to this method and got
    /// <c>'()</c> back with no error. The line-breaking close-out recorded this exact asymmetry
    /// as a loose end.
    /// </para>
    /// </summary>
    /// <param name="callback">The procedure to call.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The result, or the empty list when nothing could be called.</returns>
    public static object CallCallback(object callback, params object[] arguments)
    {
        Interpreter interpreter = LilyPondScheme.Current;
        if (interpreter == null || !IsProcedure(callback))
        {
            return Nil.Instance;
        }

        return interpreter.Evaluator.Apply(callback, arguments ?? Array.Empty<object>());
    }

    /// <summary>
    /// Sets a key in an association list, returning the possibly-extended list.
    /// <para>
    /// Upstream's <c>scm_assq_set_x</c> mutates the existing pair when the key is
    /// present and conses a new entry on the front when it is not — so the returned
    /// list must always be stored back, exactly as the C++ does.
    /// </para>
    /// </summary>
    /// <param name="alist">The association list.</param>
    /// <param name="key">The key to set.</param>
    /// <param name="value">The value to store.</param>
    /// <returns>The updated list.</returns>
    public static object AssqSet(object alist, object key, object value)
    {
        Pair entry = Assq(key, alist);
        if (entry != null)
        {
            entry.Cdr = value;
            return alist;
        }

        return new Pair(new Pair(key, value), alist ?? Nil.Instance);
    }

    /// <summary>
    /// Sets a key in an association list using <c>equal?</c> comparison —
    /// <c>scm_assoc_set_x</c>.
    /// <para>
    /// The <c>equal?</c> comparison is not incidental. This is the SETTER half of
    /// <see cref="LyAssoc"/>, and pairing an <c>equal?</c> lookup with an <c>eq?</c>
    /// setter would insert a duplicate entry for any key that is not an immediate — the
    /// same narrowing the tablature group found and closed in <c>LyAssocGet</c>. Added
    /// for <c>Break_align_engraver</c>, which is upstream's first caller in
    /// this port.
    /// </para>
    /// </summary>
    /// <param name="alist">The association list.</param>
    /// <param name="key">The key to set.</param>
    /// <param name="value">The value to store.</param>
    /// <returns>The updated list.</returns>
    public static object AssocSet(object alist, object key, object value)
    {
        Pair entry = LyAssoc(key, alist);
        if (entry != null)
        {
            entry.Cdr = value;
            return alist;
        }

        return new Pair(new Pair(key, value), alist ?? Nil.Instance);
    }

    /// <summary>Removes a key from an association list, returning the new list.</summary>
    /// <param name="alist">The association list.</param>
    /// <param name="key">The key to remove.</param>
    /// <returns>The list without that key.</returns>
    public static object AssqRemove(object alist, object key)
    {
        List<object> kept = new List<object>();
        object cursor = alist;
        while (cursor is Pair pair)
        {
            if (!(pair.Car is Pair entry && ReferenceEquals(entry.Car, key)))
            {
                kept.Add(pair.Car);
            }

            cursor = pair.Cdr;
        }

        object result = cursor ?? Nil.Instance;
        for (int i = kept.Count - 1; i >= 0; i--)
        {
            result = new Pair(kept[i], result);
        }

        return result;
    }

    /// <summary>
    /// Copies a Scheme structure deeply: pairs and vectors are rebuilt, everything
    /// else is shared.
    /// </summary>
    /// <param name="source">The value to copy.</param>
    /// <returns>The copy.</returns>
    public static object DeepCopy(object source)
    {
        if (source is Pair)
        {
            List<object> items = new List<object>();
            object cursor = source;
            while (cursor is Pair pair)
            {
                items.Add(DeepCopy(pair.Car));
                cursor = pair.Cdr;
            }

            object result = DeepCopy(cursor);
            for (int i = items.Count - 1; i >= 0; i--)
            {
                result = new Pair(items[i], result);
            }

            return result;
        }

        if (source is object[] vector)
        {
            object[] copy = new object[vector.Length];
            for (int i = 0; i < vector.Length; i++)
            {
                copy[i] = DeepCopy(vector[i]);
            }

            return copy;
        }

        return source;
    }

    /// <summary>
    /// Converts a Scheme value to a bool the way the engine does — and NOT the way
    /// Scheme does.
    /// <para>
    /// Upstream's <c>from_scm&lt;bool&gt;</c> with its default fallback is
    /// <c>scm_is_eq (s, SCM_BOOL_T)</c>: only <c>#t</c> is true, so an UNSET property
    /// (which reads back as the empty list) is false. Scheme itself would call the
    /// empty list true. Every <c>from_scm&lt;bool&gt;</c> in the C++ means this, and
    /// using Scheme truthiness instead silently inverts every "does this grob have
    /// this flag" test.
    /// </para>
    /// </summary>
    /// <param name="value">The Scheme value.</param>
    /// <returns><see langword="true"/> only for <c>#t</c>.</returns>
    public static bool ToBool(object value) => value is bool flag && flag;

    /// <summary>
    /// Converts a Scheme value to a bool using SCHEME truthiness: everything except
    /// <c>#f</c> is true. Use only where upstream calls <c>scm_is_true</c>.
    /// </summary>
    /// <param name="value">The Scheme value.</param>
    /// <returns><see langword="true"/> for anything but <c>#f</c>.</returns>
    public static bool IsSchemeTrue(object value) => !(value is bool flag) || flag;

    /// <summary>
    /// Determines whether two Scheme values are <c>equal?</c> — <c>ly_is_equal</c>, which
    /// upstream spells <c>scm_equal_p</c>.
    /// </summary>
    /// <param name="a">The first value.</param>
    /// <param name="b">The second value.</param>
    /// <returns><see langword="true"/> when the two are structurally equal.</returns>
    /// <remarks>
    /// <para>
    /// DELEGATES to the interpreter's own <c>equal?</c> rather than reimplementing it.
    /// It used to be a second, independent copy that walked pairs and vectors and then
    /// fell through to <c>object.Equals</c>, and that copy was missing TWO cases the
    /// interpreter's has.
    /// </para>
    /// <para>
    /// STRINGS: Guile compares them by CONTENT, and <c>MutableString</c> does not
    /// override <c>Equals</c>, so two distinct strings holding the same characters
    /// compared as unequal. <c>Clef_engraver</c> decides whether the clef changed by
    /// comparing GLYPH NAMES, which are strings.
    /// </para>
    /// <para>
    /// HOST OBJECTS: <c>scm_equal_p</c> ends by dispatching to a smob's own equality
    /// handler, which is exactly what the equality-roster fix added as <c>ISchemeEqual</c>
    /// — and this copy never reached it, so the fix applied to Scheme-level
    /// <c>equal?</c> and not to the engine's. All eight types that declare a handler
    /// (Duration, Moment, Listener, Input, Pitch, Tuplet_description, Prob, Spring)
    /// compared by reference here. <c>Tie_engraver</c> compares PITCHES this way.
    /// </para>
    /// <para>
    /// Found while porting <c>Drum_notes_engraver</c>, whose
    /// <c>ly_assoc</c> lookup is the engine's first caller to need the <c>equal?</c>
    /// branch at all.
    /// </para>
    /// </remarks>
    public static bool IsEqual(object a, object b) => CorePrimitives.SchemeEqual(a, b);

    /// <summary>
    /// Type-checks a property assignment against the predicate recorded for the
    /// property symbol.
    /// <para>
    /// The predicates were installed by <c>define-grob-properties.scm</c> and its
    /// siblings as Guile object properties on each symbol, during the startup load.
    /// When no predicate is recorded the assignment is refused, which is what makes a
    /// typo in a property name visible rather than silent.
    /// </para>
    /// </summary>
    /// <param name="symbol">The property being set.</param>
    /// <param name="value">The value being assigned.</param>
    /// <param name="typeSymbol">
    /// Which family of properties this is: <c>music-type?</c>, <c>backend-type?</c> or
    /// <c>translation-type?</c>.
    /// </param>
    /// <returns><see langword="true"/> when the assignment is allowed.</returns>
    /// <remarks>
    /// This overload DISCARDS any deprecation substitution, which is safe only for the
    /// categories that have no deprecation table — <c>backend-type?</c> and
    /// <c>music-type?</c>, the only two upstream leaves unwired (<c>scm/lily.scm</c>
    /// links <c>deprecated-setter-object-property</c> for <c>translation-type?</c>
    /// alone). A <c>translation-type?</c> caller must use the overload that hands the
    /// checked symbol and value back.
    /// </remarks>
    public static bool TypeCheckAssignment(Symbol symbol, object value, Symbol typeSymbol)
        => TypeCheckAssignment(symbol, value, typeSymbol, out Symbol _, out object _);

    /// <summary>
    /// Type-checks a property assignment and hands back the symbol and value that
    /// should actually be written — which are NOT always the ones passed in, because a
    /// deprecated property redirects to its replacement and converts its value on the
    /// way.
    /// </summary>
    /// <param name="symbol">The property being set.</param>
    /// <param name="value">The value being assigned.</param>
    /// <param name="typeSymbol">
    /// Which family of properties this is: <c>music-type?</c>, <c>backend-type?</c> or
    /// <c>translation-type?</c>.
    /// </param>
    /// <param name="checkedSymbol">The property to write, or <see langword="null"/>.</param>
    /// <param name="checkedValue">The value to write.</param>
    /// <returns><see langword="true"/> when the assignment is allowed.</returns>
    public static bool TypeCheckAssignment(
        Symbol symbol,
        object value,
        Symbol typeSymbol,
        out Symbol checkedSymbol,
        out object checkedValue)
    {
        InternalTypeCheck(symbol, value, false, typeSymbol, out checkedSymbol, out checkedValue);
        return checkedSymbol != null;
    }

    /// <summary>
    /// The unset half of the same check — upstream's <c>type_check_unset</c>.
    /// <para>
    /// It exists for ONE reason: a deprecated property has to be unset under its
    /// REPLACEMENT'S name, because that is where the value actually lives. Skipping the
    /// check leaves the replacement set, which is the opposite of what was asked and
    /// says nothing about it.
    /// </para>
    /// </summary>
    /// <param name="symbol">The property being unset.</param>
    /// <param name="typeSymbol">Which family of properties this is.</param>
    /// <returns>The property to remove, or <see langword="null"/> when refused.</returns>
    public static Symbol TypeCheckUnset(Symbol symbol, Symbol typeSymbol)
    {
        InternalTypeCheck(symbol, null, true, typeSymbol, out Symbol checkedSymbol, out object _);
        return checkedSymbol;
    }

    private static void InternalTypeCheck(
        Symbol symbol,
        object value,
        bool unset,
        Symbol typeSymbol,
        out Symbol checkedSymbol,
        out object checkedValue)
    {
        checkedSymbol = null;
        checkedValue = Unspecified.Instance;

        if (symbol == null)
        {
            return;
        }

        Interpreter interpreter = LilyPondScheme.Current;
        if (interpreter == null)
        {
            // Without a live interpreter there is nothing to check against. Allowing
            // the assignment keeps the object model usable from plain unit tests.
            checkedSymbol = symbol;
            checkedValue = value;
            return;
        }

        // ⚠ THE PREDICATE LOOKUP COMES FIRST, AND THE ORDER IS THE WHOLE POINT.
        // The port used to run value_type_check's short-circuits ('()/#f/*unspecified*,
        // and backend-type?'s procedures) BEFORE asking whether the property exists, so
        // an unknown name carrying any of those values was accepted in silence and the
        // deprecation path below was unreachable for exactly the cases that need it most
        // — `\unset' passes no value at all. Upstream asks for the predicate first and
        // only a property that HAS one gets the short-circuits.
        object predicate = ObjectProperty(interpreter, symbol, typeSymbol);
        if (predicate is Procedure)
        {
            if (unset || ValueTypeCheck(interpreter, symbol, value, typeSymbol, predicate))
            {
                checkedSymbol = symbol;
                checkedValue = value;
            }

            return;
        }

        // THE DEPRECATED-PROPERTY PATH. The port once stopped at
        // the warning below, and the comment where this code goes said so: "until the
        // deprecation path is ported the property is simply unknown". The cost was not a
        // missing warning — `\unset Timing.<deprecated>' was DISCARDED, so a file that
        // set skipTypesetting and then unset it through the deprecated alias never turned
        // typesetting back on and produced NO PAGES AT ALL.
        //
        // scm/lily.scm wires deprecated-setter-object-property for `translation-type?'
        // ALONE and says why, so the category is threaded through rather than assumed;
        // the two unwired categories fall past this to the warning exactly as upstream.
        object objectProperty = DeprecatedProperty.SetterObjectProperty(typeSymbol);
        if (IsProcedureWithSetter(objectProperty))
        {
            // desc is (old-type? old->new 'newSymbol warning)
            object description = DeprecatedProperty.SetterDescription(symbol, objectProperty);
            if (IsSchemeTrue(description))
            {
                if (unset)
                {
                    // Nothing to convert: the caller is removing the value, and the value
                    // lives under the REPLACEMENT'S name.
                    checkedSymbol = Nth(description, 2) as Symbol;
                    checkedValue = value;
                    return;
                }

                // Check the given value against the DEPRECATED property's type, convert
                // it, then re-check the converted value against the NEW property's — a
                // rename is not always only a rename.
                object oldTypePredicate = Nth(description, 0);
                if (ValueTypeCheck(interpreter, symbol, value, typeSymbol, oldTypePredicate))
                {
                    object newValue = interpreter.Evaluator.Apply(
                        Nth(description, 1), new[] { value });
                    InternalTypeCheck(
                        Nth(description, 2) as Symbol,
                        newValue,
                        false,
                        typeSymbol,
                        out checkedSymbol,
                        out checkedValue);
                }

                return;
            }
        }

        Warn.Warning("the property '" + symbol.Name
            + "' does not exist (perhaps a typing error)");
    }

    private static bool ValueTypeCheck(
        Interpreter interpreter,
        Symbol symbol,
        object value,
        Symbol typeSymbol,
        object predicate)
    {
        // Upstream's value_type_check opens with "'(), #f and *unspecified* always
        // succeed" — unsetting through a set IS the idiom (Timing does it to
        // whichBar and measureStartNow at every timestep). The port refused all
        // three for four sessions, which is where the long-standing
        // pop-first/instrumentName/stencil "Type check failed" noise came from,
        // and — worse — a refused context write left the STALE value behind.
        // Found by the bars/meter group, fixed centrally.
        if (value is Nil || value is bool b && !b || value is Unspecified)
        {
            return true;
        }

        // The SAME function's next short-circuit, and the port had been missing it too:
        // for `backend-type?` alone, upstream answers true for ANY procedure, because a
        // grob property holding a callback is not the value the property will end up
        // with — it is the function that computes it — and it can only be checked once it
        // has run. An unpure-pure container is checked by recursing into BOTH halves, for
        // the same reason. Found with scripts/dynamics: `\override Hairpin.stencil =
        // #flared-hairpin` is ordinary LilyPond and was being refused, which left the
        // override off the grob entirely.
        if (ReferenceEquals(typeSymbol, BackendTypeCheckSymbol))
        {
            if (IsProcedure(value))
            {
                return true;
            }

            if (value is UnpurePureContainer upc)
            {
                return ValueTypeCheck(interpreter, symbol, upc.Unpure, typeSymbol, predicate)
                       && ValueTypeCheck(interpreter, symbol, upc.Pure, typeSymbol, predicate);
            }
        }

        if (!(predicate is Procedure))
        {
            return false;
        }

        object ok = interpreter.Evaluator.Apply(predicate, new[] { value });
        if (Evaluator.IsTrue(ok))
        {
            return true;
        }

        //was previously: Warn.ProgrammingError("Type check for `" + symbol.Name
        //     + "' failed; value found: " + Describe(value));
        // Ruling R1: a diagnostic with an upstream counterpart reproduces upstream's
        // WORDING and SEVERITY verbatim. Upstream's value_type_check ends with a
        // `warning`, not a programming_error, and names the expected TYPE — which the
        // port's text never did, because it printed a C# type name instead of asking
        // Scheme. type-name is vendored (c++.scm); print_scm_val was simply unported.
        // type-name answers a STRING (c++.scm returns the alist's cdr, or the predicate's
        // own name with the trailing `?' trimmed), so Display renders it unquoted.
        Warn.Warning(
            "the property '" + symbol.Name + "' must be of type '"
            + Printer.Display(TypeName(predicate))
            + "', ignoring invalid value '" + PrintScmVal(value) + "'");
        return false;
    }

    /// <summary>
    /// Answers the human-readable name of a type predicate, through the vendored
    /// <c>type-name</c> (<c>c++.scm</c>) that upstream calls as <c>Lily::type_name</c>.
    /// </summary>
    /// <param name="predicate">The type predicate.</param>
    /// <returns>The type's name, or the empty string when Scheme cannot answer.</returns>
    private static object TypeName(object predicate)
    {
        object procedure = LilyPondScheme.LookupProcedure(TypeNameSymbol);
        return procedure == null ? string.Empty : CallCallback(procedure, predicate);
    }

    /// <summary>
    /// Renders a Scheme value for a diagnostic, as upstream's <c>print_scm_val</c> does:
    /// the written form, elided in the middle once it passes 200 characters so that one
    /// enormous value cannot bury the rest of the log.
    /// </summary>
    /// <param name="value">The value to render.</param>
    /// <returns>The rendered, possibly elided, text.</returns>
    public static string PrintScmVal(object value)
    {
        string written = Printer.Write(value);
        return written.Length > 200
            ? written.Substring(0, 100) + "\n :\n :\n" + written.Substring(written.Length - 100)
            : written;
    }

    private static object Nth(object list, int index)
    {
        object cursor = list;
        for (int i = 0; i < index && cursor is Pair pair; i++)
        {
            cursor = pair.Cdr;
        }

        return cursor is Pair result ? result.Car : Nil.Instance;
    }

    /// <summary>Reads a Guile object property from the interpreter's table.</summary>
    /// <param name="interpreter">The interpreter holding the table.</param>
    /// <param name="subject">The object the property hangs off, usually a symbol.</param>
    /// <param name="key">The property name.</param>
    /// <returns>The value, or <see langword="false"/> when unset.</returns>
    public static object ObjectProperty(Interpreter interpreter, object subject, Symbol key)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        if (key == null || !interpreter.ObjectProperties.TryGetValue(key, out object table))
        {
            return false;
        }

        object cursor = table;
        while (cursor is Pair pair)
        {
            if (pair.Car is Pair entry && ReferenceEquals(entry.Car, subject))
            {
                return entry.Cdr;
            }

            cursor = pair.Cdr;
        }

        return false;
    }

    /// <summary>Writes a Guile object property into the interpreter's table.</summary>
    /// <param name="interpreter">The interpreter holding the table.</param>
    /// <param name="subject">The object the property hangs off, usually a symbol.</param>
    /// <param name="key">The property name.</param>
    /// <param name="value">The value to store.</param>
    public static void SetObjectProperty(
        Interpreter interpreter,
        object subject,
        Symbol key,
        object value)
    {
        if (interpreter == null || key == null)
        {
            return;
        }

        interpreter.ObjectProperties.TryGetValue(key, out object table);
        interpreter.ObjectProperties[key] =
            new Pair(new Pair(subject, value), table ?? Nil.Instance);
    }

    /// <summary>
    /// Compares two property alists the way <c>Prob::equal_p</c> does: entry by entry,
    /// in order, skipping <c>origin</c> entries on both sides.
    /// </summary>
    /// <param name="a">The first alist.</param>
    /// <param name="b">The second alist.</param>
    /// <returns><see langword="true"/> when the two carry the same properties.</returns>
    public static bool PropertyAlistsEqual(object a, object b)
    {
        object aprop = a;
        object bprop = b;

        while (true)
        {
            // Skip over origin fields
            while (aprop is Pair ap && ap.Car is Pair ae && ReferenceEquals(OriginSymbol, ae.Car))
            {
                aprop = ap.Cdr;
            }

            while (bprop is Pair bp && bp.Car is Pair be && ReferenceEquals(OriginSymbol, be.Car))
            {
                bprop = bp.Cdr;
            }

            /* is one list shorter? */
            bool aIsPair = aprop is Pair;
            bool bIsPair = bprop is Pair;
            if (!aIsPair)
            {
                return !bIsPair;
            }

            if (!bIsPair)
            {
                return false;
            }

            Pair aPair = (Pair)aprop;
            Pair bPair = (Pair)bprop;
            if (!(aPair.Car is Pair aEntry) || !(bPair.Car is Pair bEntry))
            {
                return false;
            }

            if (!ReferenceEquals(aEntry.Car, bEntry.Car) || !IsEqual(aEntry.Cdr, bEntry.Cdr))
            {
                return false;
            }

            aprop = aPair.Cdr;
            bprop = bPair.Cdr;
        }
    }

    /// <summary>
    /// Reads a value as a symbol's name, falling back when it is not a symbol —
    /// upstream's <c>robust_symbol2string</c>.
    /// </summary>
    /// <param name="value">The value to read.</param>
    /// <param name="fallback">What to answer when the value is not a symbol.</param>
    /// <returns>The symbol's name, or the fallback.</returns>
    public static string RobustSymbolToString(object value, string fallback)
        => value is Symbol symbol ? symbol.Name : fallback;

    private static string Describe(object value)
        => value == null
            ? "#<null>"
            : value.ToString() + " [" + value.GetType().Name + "]";
}
