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
        InstallGeneral(interpreter);
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

    private static void InstallGeneral(Interpreter interpreter)
    {
        // Upstream returns the hash table of LY_DEFINE docstrings, built by the
        // LY_DEFINE machinery at registration time. The port's primitives carry no
        // docstrings, so the honest translation is an EMPTY hash table — the shape
        // document-functions hash-maps over, with nothing to document (recorded in
        // PORT-COVERAGE under DIVERGENCES).
        interpreter.DefinePrimitive("ly:get-all-function-documentation", 0, 0, a =>
            new SchemeHashTable(null));

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

        interpreter.DefinePrimitive("ly:wide-char->utf-8", 1, 1, a =>
            new MutableString(char.ConvertFromUtf32(
                a[0] is SchemeChar character ? character.CodePoint : 0)));
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
