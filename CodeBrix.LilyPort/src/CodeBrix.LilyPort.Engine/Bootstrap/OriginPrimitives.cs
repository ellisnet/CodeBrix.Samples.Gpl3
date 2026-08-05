// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The Scheme bindings over source locations, source files and music functions — the
/// behaviour of the <c>LY_DEFINE</c> bodies in upstream <c>lily/input-scheme.cc</c>,
/// <c>lily/sources.cc</c> and <c>lily/music-function-scheme.cc</c>. New-in-family code;
/// the derivation is recorded in <c>THIRD-PARTY-NOTICES.txt</c>.
/// </summary>
public static class OriginPrimitives
{
    /// <summary>Registers the location, source-file and music-function primitives.</summary>
    /// <param name="interpreter">The interpreter to register into.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallInput(interpreter);
        InstallMusicFunction(interpreter);
    }

    private static void InstallInput(Interpreter interpreter)
    {
        // The message text goes through simple-format with the rest arguments, exactly as
        // upstream does -- these are format strings, not literals, and callers rely on it.
        interpreter.DefinePrimitive("ly:input-warning", 2, -1, a =>
        {
            Input origin = AsInput(a[0], "ly:input-warning");
            origin.Warning(FormatMessage(interpreter, a, "ly:input-warning"));
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:input-message", 2, -1, a =>
        {
            Input origin = AsInput(a[0], "ly:input-message");
            origin.Message(FormatMessage(interpreter, a, "ly:input-message"));
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:input-file-line-char-column", 1, 1, a =>
        {
            Input origin = AsInput(a[0], "ly:input-file-line-char-column");
            origin.GetCounts(out int line, out int lineChar, out int column, out int _);
            return Pair.List(
                new MutableString(origin.FileString()),
                (long)line,
                (long)lineChar,
                (long)column);
        });

        interpreter.DefinePrimitive("ly:input-both-locations", 1, 1, a =>
        {
            Input origin = AsInput(a[0], "ly:input-both-locations");
            return Pair.List(
                new MutableString(origin.FileString()),
                (long)origin.LineNumber(),
                (long)origin.ColumnNumber(),
                (long)origin.EndLineNumber(),
                (long)origin.EndColumnNumber());
        });
    }

    private static void InstallMusicFunction(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:music-function-extract", 1, 1,
            a => AsMusicFunction(a[0], "ly:music-function-extract").Function);

        interpreter.DefinePrimitive("ly:music-function-signature", 1, 1,
            a => AsMusicFunction(a[0], "ly:music-function-signature").Signature);

        interpreter.DefinePrimitive("ly:make-music-function", 2, 2, a =>
        {
            object signature = a[0];
            object function = a[1];

            if (!(function is Procedure))
            {
                throw new ArgumentException(
                    "ly:make-music-function: argument 2 must be a procedure");
            }

            // Every signature entry must be a predicate, or a (predicate . default) pair.
            // Upstream checks this at construction because a bad entry would otherwise
            // fail much later, inside a call, with no clue where it came from.
            int position = 0;
            for (object cursor = signature; cursor is Pair pair; cursor = pair.Cdr, position++)
            {
                object entry = pair.Car;
                if (entry is Pair optional)
                {
                    entry = optional.Car;
                }

                if (!(entry is Procedure))
                {
                    throw new ArgumentException(
                        "ly:make-music-function: entry " + position
                        + " of the signature is not a music function predicate");
                }
            }

            return new MusicFunction(signature, function);
        });
    }

    private static string FormatMessage(Interpreter interpreter, object[] arguments, string who)
    {
        string template = StringPrimitives.Text(arguments[1], who);
        if (arguments.Length <= 2)
        {
            return template;
        }

        object[] formatArguments = new object[arguments.Length];
        formatArguments[0] = false;
        formatArguments[1] = arguments[1];
        Array.Copy(arguments, 2, formatArguments, 2, arguments.Length - 2);

        object formatter = interpreter.GuileModule.Lookup(Symbol.Intern("simple-format"))?.GetValue();
        if (!(formatter is Procedure))
        {
            return template;
        }

        object result = interpreter.Evaluator.Apply(formatter, formatArguments);
        return result is MutableString text ? text.ToString() : template;
    }

    private static Input AsInput(object value, string who)
        => value as Input
           ?? throw new ArgumentException(who + ": argument 1 must be a location");

    private static MusicFunction AsMusicFunction(object value, string who)
        => value as MusicFunction
           ?? throw new ArgumentException(who + ": argument 1 must be a music function");
}
