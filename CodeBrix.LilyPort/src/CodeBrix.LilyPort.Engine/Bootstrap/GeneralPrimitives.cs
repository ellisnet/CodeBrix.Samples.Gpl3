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
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
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

        interpreter.DefinePrimitive("ly:dimension?", 1, 1, a =>
            a[0] is double || a[0] is long || a[0] is int);
    }

    private static void DefineUnit(Interpreter interpreter, string name, double factor)
        => interpreter.DefinePrimitive(name, 1, 1, a =>
            SchemeConvert.ToDouble(a[0], name) * factor);

    private static void InstallDiagnostics(Interpreter interpreter, ProgramOptions options)
    {
        DefineMessage(interpreter, "ly:message", options, MessageSeverity.Message);
        DefineMessage(interpreter, "ly:progress", options, MessageSeverity.Progress);
        DefineMessage(interpreter, "ly:basic-progress", options, MessageSeverity.Progress);
        DefineMessage(interpreter, "ly:debug", options, MessageSeverity.Debug);
        DefineMessage(interpreter, "ly:warning", options, MessageSeverity.Warning);
        DefineMessage(interpreter, "ly:deprecation-warning", options, MessageSeverity.Warning);
        DefineMessage(interpreter, "ly:programming-error", options, MessageSeverity.Warning);
        DefineMessage(interpreter, "ly:non-fatal-error", options, MessageSeverity.Error);

        // ly:error aborts, exactly as upstream does: it is how LilyPond's Scheme reports
        // a condition it cannot continue past, and swallowing it would let a broken load
        // look successful.
        interpreter.DefinePrimitive("ly:error", 1, -1, a =>
            throw new SchemeThrow(
                Symbol.Intern("lilypond-error"),
                Pair.List(
                    new MutableString("ly:error"),
                    new MutableString(FormatMessage(a)),
                    Nil.Instance,
                    false)));

        interpreter.DefinePrimitive("ly:warning-located", 2, -1, a =>
        {
            object[] rest = new object[Math.Max(0, a.Length - 1)];
            Array.Copy(a, 1, rest, 0, rest.Length);
            options.Report(MessageSeverity.Warning, FormatMessage(rest));
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:expect-warning", 1, -1, a => Unspecified.Instance);
        interpreter.DefinePrimitive("ly:check-expected-warnings", 0, 0, a => Unspecified.Instance);
    }

    private static void DefineMessage(
        Interpreter interpreter,
        string name,
        ProgramOptions options,
        MessageSeverity severity)
        => interpreter.DefinePrimitive(name, 1, -1, a =>
        {
            options.Report(severity, FormatMessage(a));
            return Unspecified.Instance;
        });

    private static void InstallOptions(Interpreter interpreter, ProgramOptions options)
    {
        // Three required arguments plus a keyword rest, which carries #:type and friends
        // for command-line parsing. The rest is accepted and ignored -- nothing in the
        // load path reads it back, and rejecting it would abort lily.scm outright.
        interpreter.DefinePrimitive("ly:add-option", 3, -1, a =>
        {
            options.Add(AsSymbolName(a[0]), a[1], TextOf(a[2]));
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:set-option", 1, 2, a =>
        {
            options.Set(AsSymbolName(a[0]), a.Length > 1 && !(a[1] is DefaultArgument) ? a[1] : true);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:get-option", 1, 1, a => options.Get(AsSymbolName(a[0])));

        interpreter.DefinePrimitive("ly:option-usage", 0, 1, a => Unspecified.Instance);

        interpreter.DefinePrimitive("ly:verbose-output?", 0, 0, a =>
            Evaluator.IsTrue(options.Get("verbose")));

        interpreter.DefinePrimitive("ly:command-line-options", 0, 0, a => Nil.Instance);
        interpreter.DefinePrimitive("ly:command-line-code", 0, 0, a => Nil.Instance);
        interpreter.DefinePrimitive("ly:command-line-ly-files", 0, 0, a => Nil.Instance);
        interpreter.DefinePrimitive("ly:all-options", 0, 0, a => options.ToAlist());
    }

    private static void InstallGeneral(Interpreter interpreter, ProgramOptions options)
    {
        // function-documentation.cc's table, now real: the port's bindings still carry
        // no docstrings (EPG24 owes the content), so the table is honestly sparse —
        // but it is THE table, and every Add lands in what this returns.
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

        interpreter.DefinePrimitive("ly:dir?", 1, 1, a =>
        {
            long direction = a[0] is long value ? value : 0;
            return direction == -1 || direction == 0 || direction == 1;
        });

        interpreter.DefinePrimitive("ly:find-file", 1, 2, a =>
        {
            string name = TextOf(a[0]);
            return File.Exists(name) ? (object)new MutableString(name) : false;
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
        interpreter.DefinePrimitive("ly:camel-case->lisp-identifier", 1, 1, a =>
            new MutableString(CamelCaseToLispIdentifier(AsSymbolName(a[0]))));

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
        // became visible when EPG17 (2026-08-07) made enough marks reach the page for the
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

    private static string FormatMessage(object[] arguments)
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

        System.Text.StringBuilder builder = new System.Text.StringBuilder(template);
        for (int i = 1; i < arguments.Length; i++)
        {
            builder.Append(' ').Append(Printer.Display(arguments[i]));
        }

        return builder.ToString();
    }

    private static string AsSymbolName(object value)
        => value is Symbol symbol ? symbol.Name : TextOf(value);

    private static string TextOf(object value)
        => value is MutableString || value is string
            ? StringPrimitives.Text(value, "lilypond")
            : Printer.Display(value);
}
