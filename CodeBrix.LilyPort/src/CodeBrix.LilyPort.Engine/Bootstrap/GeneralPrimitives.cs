// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The general-purpose engine entry points: the dimension constants, the diagnostic
/// procedures, the program-option table and the small helpers from
/// <c>lily/general-scheme.cc</c>.
/// <para>
/// These are the cheapest entries on the porting worklist and among the most reached:
/// LilyPond's Scheme layer calls <c>ly:add-option</c>, <c>ly:get-option</c>,
/// <c>ly:debug</c> and <c>ly:progress</c> while it is still loading.
/// </para>
/// </summary>
public static class GeneralPrimitives
{
    /// <summary>The LilyPond version this port targets.</summary>
    public const string LilyPondVersion = "2.27.2";

    /// <summary>Installs the primitives, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    /// <returns>The program-option table the interpreter will use.</returns>
    public static ProgramOptions Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        ProgramOptions options = new ProgramOptions();
        InstallDimensions(interpreter);
        InstallDiagnostics(interpreter, options);
        InstallOptions(interpreter, options);
        InstallGeneral(interpreter, options);
        return options;
    }

    private static void InstallDimensions(Interpreter interpreter)
    {
        DefineUnit(interpreter, "ly:pt", Dimensions.Point);
        DefineUnit(interpreter, "ly:mm", Dimensions.Millimetre);
        DefineUnit(interpreter, "ly:cm", Dimensions.Centimetre);
        DefineUnit(interpreter, "ly:inch", Dimensions.Inch);
        DefineUnit(interpreter, "ly:bp", Dimensions.BigPoint);

        // Upstream is `return scm_number_p (d);` and nothing else — ANY Scheme number is
        // a dimension. A C# type pattern is not that test (trap 10): it silently rejected
        // exact rationals, bignums and complex. Everything declared `,ly:dimension?` in
        // define-grob-properties.scm went through here, so `\magnifyStaff #3/4` — which
        // scales every grob's baseline-skip, word-space and space-alist by its factor,
        // making 3 x 3/4 = 9/4 — had its scaled values REFUSED, and each property kept
        // its UNSCALED default with only a programming error to show for it. 2,243
        // refusals across the regression suite, on baseline-skip and staff-space.
        // Proved against the ORACLE before it was changed (rule 35): upstream renders
        // `baseline-skip = #9/4` byte-identically to `#2.25` and both differently from
        // the default, while the port rendered 9/4 identically to the DEFAULT.
        interpreter.DefinePrimitive("ly:dimension?", 1, 1, a => SchemeNumber.IsNumber(a[0]));
    }

    private static void DefineUnit(Interpreter interpreter, string name, double factor)
        => interpreter.DefinePrimitive(name, 1, 1, a =>
            SchemeConvert.ToDouble(a[0], name) * factor);

    private static void InstallDiagnostics(Interpreter interpreter, ProgramOptions options)
    {
        // EACH ENTRY POINT NAMES THE flower/warn.cc FUNCTION UPSTREAM GIVES IT.
        //
        //was previously: every one of these reported ONLY through options.Report, whose
        // writer is TextWriter.Null and is assigned by nothing — so the whole ly: message
        // family had been writing into a null sink for the life of the port, and no
        // diagnostic raised by the vendored Scheme layer had ever been seen. It is why
        // beam-quant-standard printed nothing at all: its expected output is an
        // ly:warning from layout-beam.scm's check-beam-quant.
        //
        // Report is KEPT for its bookkeeping — WarningCount and the recorded list — and
        // its own writer stays null, so nothing is printed twice.
        //
        // ⚠ Routing these through Warn is what makes the SEVERITIES real: upstream's
        // ly:programming-error is programming_error(), not a warning, and it shared
        // MessageSeverity.Warning with ly:warning here. Progress, message and debug reach
        // Warn too, and Warn.Level is LevelWarn by default, so they stay unprinted
        // exactly as they were — the log level decides, rather than the sink being dead.
        DefineMessage(interpreter, "ly:message", options, MessageSeverity.Message,
            m => Warn.Message(m));
        DefineMessage(interpreter, "ly:progress", options, MessageSeverity.Progress,
            m => Warn.Progress(m));
        DefineMessage(interpreter, "ly:basic-progress", options, MessageSeverity.Progress,
            m => Warn.BasicProgress(m));
        DefineMessage(interpreter, "ly:debug", options, MessageSeverity.Debug,
            m => Warn.Debug(m));
        DefineMessage(interpreter, "ly:warning", options, MessageSeverity.Warning,
            m => Warn.Warning(m));
        DefineMessage(interpreter, "ly:deprecation-warning", options, MessageSeverity.Warning,
            m => Warn.DeprecationWarning(m));
        DefineMessage(interpreter, "ly:programming-error", options, MessageSeverity.Warning,
            m => Warn.ProgrammingError(m));
        DefineMessage(interpreter, "ly:non-fatal-error", options, MessageSeverity.Error,
            m => Warn.NonFatalError(m));

        // ly:error aborts, exactly as upstream does: it is how LilyPond's Scheme reports
        // a condition it cannot continue past, and swallowing it would let a broken load
        // look successful.
        interpreter.DefinePrimitive("ly:error", 1, -1, a =>
            throw new SchemeThrow(
                Symbol.Intern("lilypond-error"),
                Pair.List(
                    new MutableString("ly:error"),
                    new MutableString(FormatMessage(interpreter, a)),
                    Nil.Instance,
                    false)));

        interpreter.DefinePrimitive("ly:warning-located", 2, -1, a =>
        {
            object[] rest = new object[Math.Max(0, a.Length - 1)];
            Array.Copy(a, 1, rest, 0, rest.Length);
            string text = FormatMessage(interpreter, rest);
            options.Report(MessageSeverity.Warning, text);
            Warn.Warning(text, TextOf(a[0]));
            return Unspecified.Instance;
        });

        //was previously: both of these were no-op stubs returning *unspecified*, so
        // ly:expect-warning registered nothing and ly:check-expected-warnings checked
        // nothing. 122 regression files call the first one, and upstream SUPPRESSES the
        // warning each one names — so every one of those warnings the port emitted was an
        // EXTRA against a reference log that is silent, and the file whose whole subject
        // is the missing-warning report produced nothing at all.
        interpreter.DefinePrimitive("ly:expect-warning", 1, -1, a =>
        {
            Warn.ExpectWarning(FormatMessage(interpreter, a));
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:check-expected-warnings", 0, 0, a =>
        {
            Warn.CheckExpectedWarnings();
            return Unspecified.Instance;
        });
    }

    private static void DefineMessage(
        Interpreter interpreter,
        string name,
        ProgramOptions options,
        MessageSeverity severity,
        Action<string> emit)
        => interpreter.DefinePrimitive(name, 1, -1, a =>
        {
            string text = FormatMessage(interpreter, a);
            options.Report(severity, text);
            emit(text);
            return Unspecified.Instance;
        });

    private static void InstallOptions(Interpreter interpreter, ProgramOptions options)
    {
        // Three required arguments plus a keyword rest, which carries #:type and friends
        // for command-line parsing. #:type and #:internal? are still accepted and
        // ignored -- nothing in the port's load path reads them back, and rejecting them
        // would abort lily.scm outright.
        //
        // #:accumulative? IS read. It has to be: the vendored lily.scm asks
        // `(object-property key 'program-option-accumulative?)' to decide between
        // ly:append-to-option and ly:set-option, so leaving the property unset routes
        // every accumulative option to the wrong binding. Upstream sets exactly this
        // object property from exactly this keyword.
        interpreter.DefinePrimitive("ly:add-option", 3, -1, a =>
        {
            string name = AsSymbolName(a[0]);
            options.Add(name, a[1], TextOf(a[2]));

            if (ReadKeywordFlag(a, "accumulative?"))
            {
                options.MarkAccumulative(name);
                SetObjectProperty(
                    interpreter, Symbol.Intern(name), AccumulativeOptionSymbol, true);
            }

            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:set-option", 1, 2, a =>
        {
            string name = AsSymbolName(a[0]);

            // Upstream WARNS and changes nothing rather than overwriting an accumulative
            // option's gathered list with a single value.
            if (options.IsAccumulative(name))
            {
                options.Report(
                    MessageSeverity.Warning,
                    "option " + name + " is accumulative; use ly:append-to-option instead "
                    + "of ly:set-option");
                return Unspecified.Instance;
            }

            options.Set(name, a.Length > 1 && !(a[1] is DefaultArgument) ? a[1] : true);
            return Unspecified.Instance;
        });

        // program-option-scheme.cc: add a value to an accumulative option. Upstream warns
        // (and still adds) when the option is not accumulative, and warns and does
        // NOTHING when no such option is declared -- two different outcomes for two
        // different mistakes.
        interpreter.DefinePrimitive("ly:append-to-option", 2, 2, a =>
        {
            string name = AsSymbolName(a[0]);
            if (!options.IsAccumulative(name))
            {
                options.Report(
                    MessageSeverity.Warning,
                    "option " + name + " is not accumulative; use ly:set-option instead "
                    + "of ly:add-to-option");
            }

            if (!options.AppendTo(name, a[1]))
            {
                options.Report(
                    MessageSeverity.Warning, "no such program option: " + name);
            }

            return Unspecified.Instance;
        });

        // Reset every option to the values in ALIST. Upstream goes through
        // internal_set_option, NOT through ly:set-option, so this deliberately bypasses
        // both the accumulative guard above and the value type check.
        interpreter.DefinePrimitive("ly:reset-options", 1, 1, a =>
        {
            for (object cursor = a[0]; cursor is Pair pair; cursor = pair.Cdr)
            {
                if (!(pair.Car is Pair entry))
                {
                    throw SchemeErrors.WrongType("ly:reset-options", "pair", pair.Car);
                }

                options.Set(AsSymbolName(entry.Car), entry.Cdr);
            }

            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:get-option", 1, 1, a => options.Get(AsSymbolName(a[0])));

        interpreter.DefinePrimitive("ly:option-usage", 0, 1, a => Unspecified.Instance);

        // main.cc's ly:usage. Prints the port's own command line — see UsageText for why
        // it is not upstream's option table — through the same sink ly:message uses, so
        // it lands wherever the run's diagnostics are going rather than unconditionally
        // on stdout.
        interpreter.DefinePrimitive("ly:usage", 0, 0, a =>
        {
            options.Report(MessageSeverity.Message, UsageText.Text);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:verbose-output?", 0, 0, a =>
            Evaluator.IsTrue(options.Get("verbose")));

        interpreter.DefinePrimitive("ly:command-line-options", 0, 0, a => Nil.Instance);
        interpreter.DefinePrimitive("ly:command-line-code", 0, 0, a => Nil.Instance);
        interpreter.DefinePrimitive("ly:command-line-ly-files", 0, 0, a => Nil.Instance);
        interpreter.DefinePrimitive("ly:all-options", 0, 0, a => options.ToAlist());
    }

    private static readonly Symbol AccumulativeOptionSymbol
        = Symbol.Intern("program-option-accumulative?");

    /// <summary>
    /// Reads a boolean keyword flag out of an <c>ly:add-option</c> style rest argument.
    /// </summary>
    /// <param name="arguments">The full argument array.</param>
    /// <param name="keyword">The keyword name, without its <c>#:</c> prefix.</param>
    /// <returns>Whether the flag was given and is true.</returns>
    /// <remarks>
    /// Upstream binds these with <c>scm_c_bind_keyword_arguments</c>, which pairs each
    /// keyword with the value that FOLLOWS it. Scanning to the last argument would read
    /// <c>#:type string</c> as a flag; the scan therefore stops one short and steps in
    /// pairs the way the binder does.
    /// </remarks>
    private static bool ReadKeywordFlag(object[] arguments, string keyword)
    {
        for (int i = 3; i + 1 < arguments.Length; i++)
        {
            if (arguments[i] is Keyword given
                && string.Equals(given.Name.Name, keyword, StringComparison.Ordinal))
            {
                return Evaluator.IsTrue(arguments[i + 1]);
            }
        }

        return false;
    }

    /// <summary>
    /// Sets a Guile object property, in the shape <c>set-object-property!</c> stores —
    /// the property symbol keys a table whose value is an alist of
    /// <c>(object . value)</c>.
    /// </summary>
    /// <param name="interpreter">The interpreter holding the table.</param>
    /// <param name="target">The object the property is attached to.</param>
    /// <param name="property">The property name.</param>
    /// <param name="value">The value to store.</param>
    private static void SetObjectProperty(
        Interpreter interpreter, object target, Symbol property, object value)
    {
        interpreter.ObjectProperties.TryGetValue(property, out object table);
        interpreter.ObjectProperties[property]
            = new Pair(new Pair(target, value), table ?? Nil.Instance);
    }

    private static void InstallGeneral(Interpreter interpreter, ProgramOptions options)
    {
        // function-documentation.cc's table, now real: every Add lands in what this
        // returns, and the docs-parity run is what grades its content.
        interpreter.DefinePrimitive("ly:get-all-function-documentation", 0, 0, a =>
            FunctionDocumentation.Table);

        // lily/ly-module.cc: dump a module's bindings as ((name . value) ...),
        // skipping unbound variables the way upstream does.
        interpreter.DefinePrimitive("ly:module->alist", 1, 1, a =>
        {
            if (!(a[0] is SchemeModule module))
            {
                throw SchemeErrors.WrongType("ly:module->alist", "module", a[0]);
            }

            object result = Nil.Instance;
            foreach (KeyValuePair<Symbol, Variable> binding in module.Bindings)
            {
                if (binding.Value.IsBound)
                {
                    result = new Pair(new Pair(binding.Key, binding.Value.GetValue()), result);
                }
            }

            return result;
        });

        // lily/module-scheme.cc: look SYM up across the list MODULES, answering the
        // first module's bound value, then DEF when given, else #f.
        //
        // PULLED FORWARD from the long-tail pool by the carry-forward demand:
        // performance naming goes through scm/midi.scm's performance-name-from-headers,
        // whose (or (ly:modules-lookup headers 'midititle) ...) chain treated the
        // polite unported stub's placeholder as a real value — so every performance
        // would have been named after the placeholder rather than its title.
        interpreter.DefinePrimitive("ly:modules-lookup", 2, 3, a =>
        {
            if (!(a[1] is Symbol symbol))
            {
                throw SchemeErrors.WrongType("ly:modules-lookup", "symbol", a[1]);
            }

            object cursor = a[0];
            while (cursor is Pair pair)
            {
                if (pair.Car is SchemeModule module)
                {
                    Variable variable = module.Lookup(symbol);
                    if (variable != null && variable.IsBound)
                    {
                        return variable.GetValue();
                    }
                }

                cursor = pair.Cdr;
            }

            return a.Length > 2 && !(a[2] is DefaultArgument) ? a[2] : (object)false;
        });

        // lily/module-scheme.cc: copy every BOUND binding out of SRC into DEST.
        //
        // Upstream's own comment is the contract: this is a one-time copy of VALUES, so a
        // later change in SRC is not seen by DEST. Unbound variables are skipped rather
        // than copied as unbound — scm_variable_bound_p guards the define.
        interpreter.DefinePrimitive("ly:module-copy", 2, 2, a =>
        {
            if (!(a[0] is SchemeModule destination))
            {
                throw SchemeErrors.WrongType("ly:module-copy", "module", a[0]);
            }

            if (!(a[1] is SchemeModule source))
            {
                throw SchemeErrors.WrongType("ly:module-copy", "module", a[1]);
            }

            // Snapshot before defining: `(ly:module-copy m m)' is legal to write, and
            // defining into the dictionary being enumerated would raise. Upstream folds
            // over SRC's obarray while defining into DEST and has the same hazard; taking
            // a copy is the port answering the question rather than inheriting it.
            List<KeyValuePair<Symbol, Variable>> bindings
                = new List<KeyValuePair<Symbol, Variable>>(source.Bindings);
            foreach (KeyValuePair<Symbol, Variable> binding in bindings)
            {
                if (binding.Value.IsBound)
                {
                    destination.Define(binding.Key, binding.Value.GetValue());
                }
            }

            return Unspecified.Instance;
        });

        // lily/warn-scheme.cc: rewrite a C++ printf format string as a Guile format
        // string, character by character, exactly as upstream does and for the reason
        // upstream gives — there is no order of simple replacements that gets `~`, `%%`
        // and `%s` all right.
        //
        // The mapping: `~` doubles to `~~`; `%%` collapses to a literal `%`; `%s` and
        // `%d` both become `~a` (NOT `~s`, which would add quotes, and not `~d`, which
        // only ice-9 supports); any other `%` becomes a bare `~`.
        //
        // The translation step upstream wraps this in, _(...), is a gettext lookup. D17
        // defers i18n, so the port translates nothing and passes the string through —
        // recorded in PORT-COVERAGE rather than left to be re-discovered here.
        interpreter.DefinePrimitive("ly:translate-cpp-warning-scheme", 1, 1, a =>
        {
            string text = TextOf(a[0]);
            StringBuilder result = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '~')
                {
                    result.Append("~~");
                }
                else if (c == '%')
                {
                    // Upstream looks ahead one character; a C string is NUL-terminated so
                    // the lookahead is always legal there. Here the bound check does the
                    // same job, and a trailing '%' falls through to the bare '~'.
                    char next = i + 1 < text.Length ? text[i + 1] : '\0';
                    if (next == '%')
                    {
                        result.Append('%');
                        i++;
                    }
                    else if (next == 's' || next == 'd')
                    {
                        result.Append("~a");
                        i++;
                    }
                    else
                    {
                        result.Append('~');
                    }
                }
                else
                {
                    result.Append(c);
                }
            }

            return new MutableString(result.ToString());
        });

        interpreter.DefinePrimitive("ly:version", 0, 0, a =>
        {
            string[] parts = LilyPondVersion.Split('.');
            List<object> numbers = new List<object>(parts.Length);
            foreach (string part in parts)
            {
                numbers.Add(long.Parse(part, CultureInfo.InvariantCulture));
            }

            return Pair.ListFrom(numbers);
        });

        // (ly:assoc-get key alist [default [strict-checking]])
        interpreter.DefinePrimitive("ly:assoc-get", 2, 4, a =>
        {
            foreach (object entry in Pair.ToList(a[1]))
            {
                if (entry is Pair pair && CorePrimitives.SchemeEqual(pair.Car, a[0]))
                {
                    return pair.Cdr;
                }
            }

            return a.Length > 2 && !(a[2] is DefaultArgument) ? a[2] : false;
        });

        // A chain is a list of alists, searched front to back.
        interpreter.DefinePrimitive("ly:chain-assoc-get", 2, 4, a =>
        {
            foreach (object alist in Pair.ToList(a[1]))
            {
                foreach (object entry in Pair.ToList(alist))
                {
                    if (entry is Pair pair && CorePrimitives.SchemeEqual(pair.Car, a[0]))
                    {
                        return pair.Cdr;
                    }
                }
            }

            return a.Length > 2 && !(a[2] is DefaultArgument) ? a[2] : false;
        });

        interpreter.DefinePrimitive("ly:hash-table-keys", 1, 1, a =>
        {
            List<object> keys = new List<object>();
            if (a[0] is Dictionary<object, object> table)
            {
                foreach (KeyValuePair<object, object> entry in table)
                {
                    keys.Add(entry.Key);
                }
            }

            return Pair.ListFrom(keys);
        });

        interpreter.DefinePrimitive("ly:number->string", 1, 1, a =>
            new MutableString(FormatNumber(a[0])));

        interpreter.DefinePrimitive("ly:string-substitute", 3, 3, a =>
            new MutableString(TextOf(a[2]).Replace(TextOf(a[0]), TextOf(a[1]))));

        // Percent-encodes for a URL. The SVG backend's textedit:// anchors go through
        // this, so a stub costs point-and-click every file whose path has a space in it
        // — and only those, which is the kind of gap that shows up on someone else's
        // machine and not on the author's.
        interpreter.DefinePrimitive("ly:string-percent-encode", 1, 1, a =>
            new MutableString(Origins.PointAndClick.PercentEncode(TextOf(a[0]))));

        // general-scheme.cc:116-123. A NON-NUMBER answers #f, and that half is the whole
        // point of the predicate: this used to read the argument as `a[0] is long value ?
        // value : 0`, so every value that was not a C# long — a symbol, a list, a string,
        // a grob — became 0, and 0 IS a valid direction. The predicate answered #t to
        // everything.
        //
        // It is also trap 6c's shape: upstream tests scm_is_integer, which is true of the
        // whole exact-integer tower, not of one C# representation.
        interpreter.DefinePrimitive("ly:dir?", 1, 1, a =>
        {
            if (!SchemeNumber.IsNumber(a[0]) || !SchemeNumber.IsInteger(a[0]))
            {
                return false;
            }

            double direction = Convert.ToDouble(a[0], CultureInfo.InvariantCulture);
            return direction >= -1 && direction <= 1;
        });

        // Upstream's ly:find-file takes an optional STRICT flag, and when the file is
        // missing and strict is #t it raises a FATAL error naming the load path and the
        // cwd. The port had dropped the flag entirely and always answered #f, so
        // \markup \image and \verbatim-file silently produced nothing where the oracle
        // stops the run — the whole of D9's MISSING (ii). The load path and cwd are
        // machine-specific VALUES, not wording, so they legitimately differ between the
        // two engines; the sentence is upstream's verbatim.
        //was previously: return File.Exists(name) ? (object)new MutableString(name) : false;
        interpreter.DefinePrimitive("ly:find-file", 1, 2, a =>
        {
            string name = TextOf(a[0]);
            string resolved = FindOnLoadPath(interpreter, name, out string loadPath);
            if (resolved != null)
            {
                return new MutableString(resolved);
            }

            if (a.Length < 2 || !Objects.SchemeUtilities.IsSchemeTrue(a[1]))
            {
                return false;
            }

            Warn.Error("cannot find file '" + name + "' (load path: '" + loadPath
                       + "', cwd: '" + Directory.GetCurrentDirectory() + "')");
            return false;
        });

        interpreter.DefinePrimitive("ly:effective-prefix", 0, 0, a => new MutableString(string.Empty));

        // Case-insensitive orderings, used by the documentation generators to sort their
        // indexes the way a reader expects rather than by code point.
        interpreter.DefinePrimitive("ly:string-ci<?", 2, 2, a => string.Compare(
            TextOf(a[0]), TextOf(a[1]), StringComparison.OrdinalIgnoreCase) < 0);

        interpreter.DefinePrimitive("ly:symbol-ci<?", 2, 2, a => string.Compare(
            AsSymbolName(a[0]), AsSymbolName(a[1]), StringComparison.OrdinalIgnoreCase) < 0);

        // Upstream lowercases the first letter and inserts a hyphen before each later
        // capital: Span_stem_engraver stays as-is, but NoteHead becomes note-head.
        // general-scheme.cc:367 returns ly_symbol2scm (result) — a SYMBOL, not a string.
        // The two callers are both in document-music.scm and both hand the answer
        // straight to ly:make-event-class, which looks it up in a table keyed by symbol:
        // a string prints exactly like the symbol it should have been, matches nothing,
        // and the music node quietly loses the "Event classes" and "Accepted by" blocks
        // of every one of its 272 entries.
        interpreter.DefinePrimitive("ly:camel-case->lisp-identifier", 1, 1, a =>
            Symbol.Intern(CamelCaseToLispIdentifier(AsSymbolName(a[0]))));

        // A Bezier crosses the Scheme boundary as a list of four (x . y) pairs, which is
        // the representation LilyPond's Scheme already uses for control points.
        interpreter.DefinePrimitive("ly:bezier-extract", 3, 3, a =>
        {
            List<Offset> controls = new List<Offset>();
            foreach (object point in Pair.ToList(a[0]))
            {
                if (!(point is Pair pair))
                {
                    throw SchemeErrors.WrongType("ly:bezier-extract", "list of number pairs", a[0]);
                }

                controls.Add(new Offset(
                    SchemeConvert.ToDouble(pair.Car, "ly:bezier-extract"),
                    SchemeConvert.ToDouble(pair.Cdr, "ly:bezier-extract")));
            }

            Bezier extracted = new Bezier(controls).Extract(
                SchemeConvert.ToDouble(a[1], "ly:bezier-extract"),
                SchemeConvert.ToDouble(a[2], "ly:bezier-extract"));

            List<object> result = new List<object>(Bezier.ControlCount);
            for (int i = 0; i < Bezier.ControlCount; i++)
            {
                result.Add(new Pair(extracted[i].X, extracted[i].Y));
            }

            return Pair.ListFrom(result);
        });

        // (ly:directed direction [magnitude]) -- direction is an angle in degrees or an
        // (x . y) pair; magnitude may be a number or a pair scaling each axis separately,
        // which is what ellipse drawing needs.
        interpreter.DefinePrimitive("ly:directed", 1, 2, a =>
        {
            Offset result = a[0] is Pair directionPair
                ? new Offset(
                        SchemeConvert.ToDouble(directionPair.Car, "ly:directed"),
                        SchemeConvert.ToDouble(directionPair.Cdr, "ly:directed"))
                    .Direction()
                : Offset.Directed(SchemeConvert.ToDouble(a[0], "ly:directed"));

            if (a.Length > 1 && !(a[1] is DefaultArgument))
            {
                if (a[1] is Pair magnitudePair)
                {
                    result = new Offset(
                        result.X * SchemeConvert.ToDouble(magnitudePair.Car, "ly:directed"),
                        result.Y * SchemeConvert.ToDouble(magnitudePair.Cdr, "ly:directed"));
                }
                else
                {
                    result *= SchemeConvert.ToDouble(a[1], "ly:directed");
                }
            }

            return new Pair(result.X, result.Y);
        });

        // Upstream is scm_c_make_string (1, scm_integer_to_char (wc)): the argument is an
        // INTEGER CODEPOINT, and every caller passes one — #x200a for the hair spaces in
        // boxed rehearsal marks, #x0132/#x0133 for the IJ ligatures, and the whole chord-
        // modifier set (degree sign, slashed o, triangles).
        //
        // This port read the argument as a SchemeChar and fell back to codepoint ZERO for
        // anything else, so it answered a NUL character to every real call. Nothing failed:
        // a NUL is a perfectly good string as far as the engine is concerned, and it only
        // became visible when the volta/tuplet group made enough marks reach the page for the
        // SVG to stop being well-formed XML — NUL is not a legal XML character. Integers
        // are the contract; a character is tolerated rather than turned back into a NUL.
        interpreter.DefinePrimitive("ly:wide-char->utf-8", 1, 1, a =>
        {
            int codePoint;
            switch (a[0])
            {
                case SchemeChar character:
                    codePoint = character.CodePoint;
                    break;
                case long value:
                    codePoint = (int)value;
                    break;
                case int value:
                    codePoint = value;
                    break;
                case System.Numerics.BigInteger big:
                    codePoint = (int)big;
                    break;
                default:
                    throw SchemeErrors.WrongType("ly:wide-char->utf-8", "integer", a[0]);
            }

            return new MutableString(char.ConvertFromUtf32(codePoint));
        });

        // LilyPond's own formatter — NOT Guile's format. It knows ~a, ~s, ~f with an
        // optional single-digit precision, ~$ (precision 2) and ~~, nothing else, and
        // the SVG backend leans on it for every coordinate it prints.
        interpreter.DefinePrimitive("ly:format", 1, -1, a =>
        {
            string format = TextOf(a[0]);
            System.Text.StringBuilder result = new System.Text.StringBuilder();
            int next = 1;

            int i = 0;
            while (i < format.Length)
            {
                int tilde = format.IndexOf('~', i);
                if (tilde < 0)
                {
                    result.Append(format, i, format.Length - i);
                    break;
                }

                result.Append(format, i, tilde - i);
                tilde++;

                char spec = format[tilde++];
                if (spec == '~')
                {
                    result.Append('~');
                }
                else
                {
                    if (next >= a.Length)
                    {
                        Warn.ProgrammingError("ly:format: not enough arguments for format.");
                        return new MutableString(string.Empty);
                    }

                    object argument = a[next++];
                    int precision = 8;

                    if (spec == '$')
                    {
                        precision = 2;
                    }
                    else if (char.IsDigit(spec))
                    {
                        precision = spec - '0';
                        spec = format[tilde++];
                    }

                    if (spec == 'a' || spec == 'A' || spec == 'f' || spec == '$')
                    {
                        result.Append(FormatSingleArgument(argument, precision, false, options));
                    }
                    else if (spec == 's' || spec == 'S')
                    {
                        result.Append(FormatSingleArgument(argument, precision, true, options));
                    }
                }

                i = tilde;
            }

            if (next < a.Length)
            {
                Warn.ProgrammingError("ly:format: too many arguments");
            }

            return new MutableString(result.ToString());
        });

        // In contrast to Guile's rename-file, this replaces the destination when it
        // already exists.
        interpreter.DefinePrimitive("ly:rename-file", 2, 2, a =>
        {
            string oldName = TextOf(a[0]);
            string newName = TextOf(a[1]);
            try
            {
                File.Move(oldName, newName, true);
            }
            catch (Exception exception) when (exception is IOException
                || exception is UnauthorizedAccessException
                || exception is ArgumentException)
            {
                throw new SchemeThrow(
                    Symbol.Intern("lilypond-error"),
                    Pair.List(
                        new MutableString("ly:rename-file"),
                        new MutableString(
                            "cannot rename `" + oldName + "' to `" + newName + "'"),
                        Nil.Instance,
                        false));
            }

            return Unspecified.Instance;
        });

        // The FILE form redirects everything the port writes as a diagnostic. The FD
        // form is dup2 on file descriptor 2, which has no in-process equivalent here;
        // it answers loudly rather than pretending (PORT-COVERAGE, DIVERGENCES).
        interpreter.DefinePrimitive("ly:stderr-redirect", 1, 2, a =>
        {
            if (a[0] is long || a[0] is int)
            {
                throw new SchemeThrow(
                    Symbol.Intern("lilypond-error"),
                    Pair.List(
                        new MutableString("ly:stderr-redirect"),
                        new MutableString(
                            "not applicable: file-descriptor redirection has no "
                            + "in-process equivalent; pass a file name"),
                        Nil.Instance,
                        false));
            }

            string fileName = TextOf(a[0]);
            string mode = a.Length > 1 && !(a[1] is DefaultArgument) ? TextOf(a[1]) : "w";
            StreamWriter writer = new StreamWriter(
                File.Open(
                    fileName,
                    mode.StartsWith("a", StringComparison.Ordinal)
                        ? FileMode.Append
                        : FileMode.Create))
            {
                AutoFlush = true,
            };

            Warn.Output.Flush();
            Warn.Output = writer;
            Console.SetError(writer);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:base64-encode", 1, 1, a =>
        {
            if (!(a[0] is byte[] bytes))
            {
                throw SchemeErrors.WrongType("ly:base64-encode", "bytevector", a[0]);
            }

            return new MutableString(Convert.ToBase64String(bytes));
        });

        // The Ghostscript trio — process spawning and the gs API — exists to drive
        // the PostScript pipeline, which D15 rules out of the port. N/A per D25,
        // category ps-backend, rows in entry-point-na-candidates.tsv; the bindings
        // stay LOUD so a file that somehow reaches one fails with its reason.
        InstallNotApplicable(interpreter, "ly:spawn", 1,
            "the PostScript/Ghostscript pipeline is not ported (D15)");
        InstallNotApplicable(interpreter, "ly:shutdown-gs", 0,
            "the PostScript/Ghostscript pipeline is not ported (D15)");
        InstallNotApplicable(interpreter, "ly:gs-api", 2,
            "the PostScript/Ghostscript pipeline is not ported (D15)");
    }

    private static void InstallNotApplicable(
        Interpreter interpreter,
        string name,
        int required,
        string reason)
        => interpreter.DefinePrimitive(name, required, -1, a =>
            throw new SchemeThrow(
                Symbol.Intern("lilypond-error"),
                Pair.List(
                    new MutableString(name),
                    new MutableString(
                        "not applicable: " + reason + " -- N/A per D25, category ps-backend"),
                    Nil.Instance,
                    false)));

    /// <summary>
    /// Upstream's <c>format_single_argument</c>: exact integers print as integers,
    /// other numbers at fixed precision, strings as-is (or escaped and quoted for
    /// <c>~s</c>), symbols by name; anything else draws a progress message.
    /// </summary>
    private static string FormatSingleArgument(
        object argument,
        int precision,
        bool escape,
        ProgramOptions options)
    {
        if (argument is long exact)
        {
            return exact.ToString(CultureInfo.InvariantCulture);
        }

        if (argument is System.Numerics.BigInteger big)
        {
            return big.ToString(CultureInfo.InvariantCulture);
        }

        if (SchemeConvert.IsNumber(argument))
        {
            double value = SchemeConvert.ToDouble(argument, "ly:format");
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                options.Report(
                    MessageSeverity.Warning,
                    "Found infinity or nan in output.  Substituting 0.0");
                return "0.0";
            }

            return value.ToString("F" + precision, CultureInfo.InvariantCulture);
        }

        if (argument is MutableString || argument is string)
        {
            string text = StringPrimitives.Text(argument, "ly:format");
            if (escape)
            {
                // Escape backslashes and double quotes, wrap in double quotes. Percents
                // deliberately stay: upstream leaves them for the png backend's %d.
                text = text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$");
                text = "\"" + text + "\"";
            }

            return text;
        }

        if (argument is Symbol symbol)
        {
            return symbol.Name;
        }

        options.Report(
            MessageSeverity.Progress,
            "\nUnsupported SCM value for format: " + Printer.Display(argument));
        return string.Empty;
    }

    private static string CamelCaseToLispIdentifier(string name)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            char current = name[i];
            if (char.IsUpper(current))
            {
                if (i > 0)
                {
                    builder.Append('-');
                }

                builder.Append(char.ToLowerInvariant(current));
            }
            else
            {
                builder.Append(current);
            }
        }

        return builder.ToString();
    }

    private static string FormatNumber(object value)
    {
        if (value is double real)
        {
            return real.ToString("0.####", CultureInfo.InvariantCulture);
        }

        return Printer.Display(value);
    }

    /// <summary>
    /// Builds a diagnostic's text the way upstream builds it, with
    /// <c>simple-format</c>.
    /// </summary>
    /// <param name="interpreter">The interpreter holding the formatter.</param>
    /// <param name="arguments">The template followed by its format arguments.</param>
    /// <returns>The formatted message.</returns>
    /// <remarks>
    /// //was previously: the arguments were APPENDED to the template separated by
    /// spaces, so <c>(ly:warning (G_ "Bar number is ~a; expected ~a") 3 15)</c> printed
    /// the template with its escapes intact and "3 15" stuck on the end. Every
    /// <c>ly:</c> message entry point upstream runs
    /// <c>scm_simple_format (SCM_BOOL_F, str, rest)</c> first, so the escapes are
    /// SUBSTITUTED. This is rule 15 applied to the message body rather than its prefix,
    /// and it is also what lets <c>ly:expect-warning</c> match: an expectation and the
    /// warning it suppresses have to be built by the same rule, and 56 of the 298
    /// expectations in the suite pass format arguments.
    /// </remarks>
    private static string FormatMessage(Interpreter interpreter, object[] arguments)
    {
        if (arguments.Length == 0)
        {
            return string.Empty;
        }

        string template = TextOf(arguments[0]);
        if (arguments.Length == 1)
        {
            return template;
        }

        object[] formatArguments = new object[arguments.Length + 1];
        formatArguments[0] = false;
        Array.Copy(arguments, 0, formatArguments, 1, arguments.Length);

        object formatter = interpreter.GuileModule
            .Lookup(Symbol.Intern("simple-format"))?.GetValue();
        if (!(formatter is Procedure))
        {
            return template;
        }

        object result = interpreter.Evaluator.Apply(formatter, formatArguments);
        return result is MutableString text ? text.ToString() : template;
    }

    private static string AsSymbolName(object value)
        => value is Symbol symbol ? symbol.Name : TextOf(value);

    private static string TextOf(object value)
        => value is MutableString || value is string
            ? StringPrimitives.Text(value, "lilypond")
            : Printer.Display(value);

    /// <summary>
    /// Resolves a file the way upstream's <c>global_path.find</c> does — the working
    /// directory first, then the current parser's include path — and reports the path
    /// that was searched so a failure can name it, as upstream's message does.
    /// </summary>
    /// <param name="interpreter">The interpreter whose parser supplies the path.</param>
    /// <param name="name">The file name to resolve.</param>
    /// <param name="loadPath">Receives the searched path, colon-separated.</param>
    /// <returns>The resolved name, or <see langword="null"/> when nothing matched.</returns>
    private static string FindOnLoadPath(Interpreter interpreter, string name, out string loadPath)
    {
        IList<string> directories = ParserPrimitives.Current(interpreter)?.IncludePath
                                    ?? (IList<string>)Array.Empty<string>();
        loadPath = string.Join(":", directories);

        if (File.Exists(name))
        {
            return name;
        }

        foreach (string directory in directories)
        {
            string candidate = Path.Combine(directory, name);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }
}
