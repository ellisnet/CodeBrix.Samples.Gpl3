/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
                 Jan Nieuwenhuizen <janneke@gnu.org>

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
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (epilogue), lily/lily-parser.cc (get_header);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The helper functions <c>parser.yy</c> defines in its epilogue for the rule actions
/// to share, plus <c>get_header</c> from <c>lily-parser.cc</c> and the handful of
/// libguile list operations the bodies lean on (<c>scm_reverse_x</c> and friends),
/// which are implemented locally so that reducing a rule never depends on an
/// interpreter being alive. PARTIAL: a group's session adds the epilogue helpers it
/// needs in its own <c>ParserActionHelpers.RagN.cs</c> file.
/// </summary>
internal static partial class ParserActionHelpers
{
    /// <summary>
    /// Returns the caller's <see cref="IParserHost"/>, refusing to run without one.
    /// <para>
    /// An action that needs the host and silently skipped its work would be exactly
    /// the "declared upstream, half-reproduced, nothing failed loudly" defect the
    /// engine port kept finding — so absence is loud, not tolerated.
    /// </para>
    /// </summary>
    /// <param name="context">The parse in progress.</param>
    /// <returns>The host.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the parse was started
    /// without an <see cref="IParserHost"/> as its user state.</exception>
    internal static IParserHost RequireHost(ParseContext context)
        => context.UserState as IParserHost
           ?? throw new InvalidOperationException(
               "This rule's action reaches Lily_parser state; the parse must be"
               + " started with an IParserHost as its user state.");

    /// <summary>
    /// Reports a parse error the way <c>Lily_parser::parser_error</c> does: the
    /// diagnostic is recorded AND the error level is raised, so a file with a bad
    /// expression still parses to the end but is known to have failed.
    /// </summary>
    /// <param name="context">The parse in progress.</param>
    /// <param name="location">Where the error is.</param>
    /// <param name="message">The message.</param>
    internal static void ParserError(ParseContext context, SourceSpan location, string message)
    {
        context.Error(message, location);
        if (context.UserState is IParserErrorLevel state)
        {
            state.ErrorLevel = 1;
        }
    }

    /// <summary>Answers <c>scm_is_string</c> over the port's two string shapes.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> for a string.</returns>
    internal static bool IsSchemeString(object value)
        => value is string || value is MutableString;

    /// <summary>Returns a Scheme string's text.</summary>
    /// <param name="value">A CLR string or a <see cref="MutableString"/>.</param>
    /// <returns>The text, or null when the value is not a string.</returns>
    internal static string SchemeStringText(object value)
        => value is MutableString mutableString ? mutableString.ToString() : value as string;

    /// <summary>
    /// Destructively reverses a list onto a new tail, which is <c>scm_reverse_x</c> —
    /// the idiom every action that accumulated a list front-to-back finishes with.
    /// </summary>
    /// <param name="list">The list to reverse; its pairs are reused.</param>
    /// <param name="newTail">The tail the reversed list ends in.</param>
    /// <returns>The reversed list.</returns>
    internal static object ReverseInPlace(object list, object newTail)
    {
        object result = newTail;
        object current = list;
        while (current is Pair pair)
        {
            object rest = pair.Cdr;
            pair.Cdr = result;
            result = pair;
            current = rest;
        }

        return result;
    }

    /// <summary>
    /// Reverses a list onto a tail without touching the original, which is SRFI-1's
    /// <c>append-reverse</c> as <c>post_event_cons</c> uses it for tweaks.
    /// </summary>
    /// <param name="list">The list to reverse.</param>
    /// <param name="tail">The tail to end in.</param>
    /// <returns>The reversed copy.</returns>
    internal static object AppendReverse(object list, object tail)
    {
        object result = tail;
        for (object p = list; p is Pair pair; p = pair.Cdr)
        {
            result = new Pair(pair.Car, result);
        }

        return result;
    }

    /// <summary>
    /// Appends two lists, copying the first and sharing the second, which is
    /// <c>ly_append</c> with two arguments.
    /// </summary>
    /// <param name="list">The list to copy.</param>
    /// <param name="tail">The shared tail.</param>
    /// <returns>The appended list.</returns>
    internal static object Append(object list, object tail)
    {
        if (!(list is Pair))
        {
            return tail;
        }

        object head = null;
        Pair last = null;
        for (object p = list; p is Pair pair; p = pair.Cdr)
        {
            Pair copy = new Pair(pair.Car, Nil.Instance);
            if (last == null)
            {
                head = copy;
            }
            else
            {
                last.Cdr = copy;
            }

            last = copy;
        }

        last.Cdr = tail;
        return head;
    }

    /// <summary>
    /// Returns the header module a <c>\header</c> block at this point should start
    /// from: a fresh module, seeded with a copy of <c>$defaultheader</c> when one is
    /// in scope — which is how an inner header retains the values an outer one set.
    /// <para>Upstream: <c>get_header</c> in <c>lily-parser.cc</c>.</para>
    /// </summary>
    /// <param name="host">The parser host.</param>
    /// <returns>The module to open the header scope with.</returns>
    internal static object GetHeader(IParserHost host)
    {
        object existing = host.LookupIdentifier("$defaultheader");
        object module = host.MakeModule();
        if (host.IsModule(existing))
        {
            host.ModuleCopy(module, existing);
        }

        return module;
    }

    /// <summary>
    /// Tries the ways a string-ish value can satisfy a predicate: as itself, a key as
    /// a one-key list, a string as a symbol list and then as a single symbol.
    /// <para>Upstream: <c>try_string_variants</c> in <c>parser.yy</c>'s epilogue. The
    /// predicate is a CLR delegate rather than a Scheme procedure — call sites whose
    /// upstream predicate is an SCM value wrap it over <see cref="IParserHost.Call"/>.
    /// </para>
    /// </summary>
    /// <param name="host">The parser host, for the key test.</param>
    /// <param name="predicate">The predicate to satisfy.</param>
    /// <param name="value">The value to interpret.</param>
    /// <returns>The accepted interpretation, or
    /// <see cref="DefaultArgument.Instance"/> for <c>SCM_UNDEFINED</c> when none
    /// fits.</returns>
    internal static object TryStringVariants(IParserHost host, Func<object, bool> predicate, object value)
    {
        // a matching predicate is always ok
        if (predicate(value))
        {
            return value;
        }

        // a key may be interpreted as a list of keys if it helps
        if (host.IsKey(value))
        {
            object asList = new Pair(value, Nil.Instance);
            return predicate(asList) ? asList : DefaultArgument.Instance;
        }

        if (!IsSchemeString(value))
        {
            return DefaultArgument.Instance;
        }

        // Let's attempt the symbol list interpretation first.
        Symbol symbol = Symbol.Intern(SchemeStringText(value));
        object list = new Pair(symbol, Nil.Instance);
        if (predicate(list))
        {
            return list;
        }

        // Try the single symbol interpretation
        if (predicate(symbol))
        {
            return symbol;
        }

        return DefaultArgument.Instance;
    }

    /// <summary>
    /// Answers whether a string is a regular identifier: letters, with single
    /// <c>-</c> or <c>_</c> separators (and <c>.</c> or <c>,</c> when
    /// <paramref name="multiple"/> allows a compound path), never at the edges.
    /// <para>Upstream: <c>is_regular_identifier</c> in <c>parser.yy</c>'s epilogue,
    /// including its byte-wise treatment of anything beyond ASCII as a letter.</para>
    /// </summary>
    /// <param name="value">The value to test; non-strings answer no.</param>
    /// <param name="multiple">Whether <c>.</c> and <c>,</c> separators are allowed.</param>
    /// <returns><see langword="true"/> for a regular identifier.</returns>
    internal static bool IsRegularIdentifier(object value, bool multiple)
    {
        if (!IsSchemeString(value))
        {
            return false;
        }

        string text = SchemeStringText(value);
        bool middle = false;

        foreach (char c in text)
        {
            if ((c >= 'a' && c <= 'z')
                || (c >= 'A' && c <= 'Z')
                || c > 0x7f)
            {
                middle = true;
            }
            else if (middle && (c == '-' || c == '_' || (multiple && (c == '.' || c == ','))))
            {
                middle = false;
            }
            else
            {
                return false;
            }
        }

        return middle;
    }

    /// <summary>
    /// Conses a post event onto a tail — except that a <c>post-event-wrapper</c> is
    /// UNPACKED: its elements are appended instead, each first receiving the
    /// wrapper's tweaks (in order) and its other properties (last to first, so the
    /// current value of a duplicate survives).
    /// <para>Upstream: <c>post_event_cons</c> in <c>parser.yy</c>'s epilogue.</para>
    /// </summary>
    /// <param name="postEvent">The post event, or anything that is not music.</param>
    /// <param name="tail">The list to cons onto.</param>
    /// <returns>The extended list.</returns>
    internal static object PostEventCons(object postEvent, object tail)
    {
        MusicObject music = postEvent as MusicObject;
        if (music == null)
        {
            return tail;
        }

        if (!music.IsMusicType("post-event-wrapper"))
        {
            return new Pair(postEvent, tail);
        }

        object elements = DefaultArgument.Instance;
        object properties = Nil.Instance;
        object tweaks = DefaultArgument.Instance;
        for (object p = music.GetPropertyAlist(true); p is Pair entry; p = entry.Cdr)
        {
            Pair pair = (Pair)entry.Car;
            object symbol = pair.Car;
            if (ReferenceEquals(symbol, Symbol.Intern("origin")))
            {
                continue;
            }
            else if (ReferenceEquals(symbol, Symbol.Intern("elements"))
                     && elements is DefaultArgument)
            {
                elements = pair.Cdr;
            }
            else if (ReferenceEquals(symbol, Symbol.Intern("tweaks"))
                     && tweaks is DefaultArgument)
            {
                tweaks = pair.Cdr;
            }
            else
            {
                properties = new Pair(pair, properties);
            }
        }

        if (!(elements is Pair))
        {
            return tail;
        }

        elements = ReverseInPlace(elements, Nil.Instance);
        for (object p = elements; p is Pair pair; p = pair.Cdr)
        {
            MusicObject element = (MusicObject)pair.Car;

            // tweaks are always collected in-order, newer tweaks
            // nearer to the front of the list
            if (tweaks is Pair)
            {
                element.SetProperty("tweaks", AppendReverse(tweaks, element.GetProperty("tweaks")));
            }

            // other properties are applied last to first so that
            // in case of duplicate properties, the actually
            // current one survives
            for (object q = properties; q is Pair propertyEntry; q = propertyEntry.Cdr)
            {
                Pair property = (Pair)propertyEntry.Car;
                element.SetProperty((Symbol)property.Car, property.Cdr);
            }
        }

        return Append(elements, tail);
    }
}
