// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The Scheme bindings over the parser — the behaviour of the <c>LY_DEFINE</c> bodies in
/// upstream <c>lily/lily-parser-scheme.cc</c> and the two of <c>lily/sources.cc</c>.
/// New-in-family code; the derivation is recorded in <c>THIRD-PARTY-NOTICES.txt</c>.
/// <para>
/// Every one of these starts, upstream, with <c>scm_fluid_ref (Lily::f_parser)</c>. The
/// port keeps the same fluid: <c>scm/lily.scm</c> defines <c>%parser</c>, the parser
/// publishes itself into it for the duration of a parse, and <c>(*parser*)</c> reads it
/// from Scheme exactly as upstream.
/// </para>
/// </summary>
public static class ParserPrimitives
{
    private static readonly Symbol ParserFluid = Symbol.Intern("%parser");

    /// <summary>Registers the parser primitives.</summary>
    /// <param name="interpreter">The interpreter to register into.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallLookup(interpreter);
        InstallDiagnostics(interpreter);
        InstallParsing(interpreter);
        InstallSources(interpreter);
    }

    /// <summary>
    /// Gets the parser the <c>%parser</c> fluid currently holds, or
    /// <see langword="null"/> outside a parse.
    /// </summary>
    /// <param name="interpreter">The interpreter to read the fluid from.</param>
    /// <returns>The parser, or <see langword="null"/>.</returns>
    public static ILilyParser Current(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            return null;
        }

        return ParserFluidOf(interpreter)?.Value as ILilyParser;
    }

    /// <summary>
    /// Gets the <c>%parser</c> fluid, which <c>scm/lily.scm</c> defines in <c>(lily)</c>.
    /// </summary>
    /// <param name="interpreter">The interpreter.</param>
    /// <returns>The fluid, or <see langword="null"/> when the Scheme layer is not loaded.</returns>
    public static Fluid ParserFluidOf(Interpreter interpreter)
    {
        SchemeModule lily = interpreter.Modules.Resolve(Pair.List(Symbol.Intern("lily")));
        Variable variable = lily?.Lookup(ParserFluid);
        return variable != null && variable.IsBound ? variable.GetValue() as Fluid : null;
    }

    private static void InstallLookup(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:lily-parser?", 1, 1, a => a[0] is ILilyParser);

        interpreter.DefinePrimitive("ly:parser-define!", 2, 2, a =>
        {
            Parser(interpreter, "ly:parser-define!")
                .SetIdentifier(AsSymbol(a[0], "ly:parser-define!"), a[1]);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:parser-define-once!", 2, 2, a =>
        {
            ILilyParser parser = Parser(interpreter, "ly:parser-define-once!");
            Symbol id = AsSymbol(a[0], "ly:parser-define-once!");
            if (parser.LookupIdentifier(id.Name) is DefaultArgument)
            {
                parser.SetIdentifier(id, a[1]);
            }

            return Unspecified.Instance;
        });

        // ly:parser-lookup takes #:default as a KEYWORD argument, and '() when it is
        // absent — not #f, which several callers store deliberately.
        interpreter.DefinePrimitive("ly:parser-lookup", 1, -1, a =>
        {
            ILilyParser parser = Parser(interpreter, "ly:parser-lookup");
            Symbol id = AsSymbol(a[0], "ly:parser-lookup");

            object value = parser.LookupIdentifier(id.Name);
            if (!(value is DefaultArgument))
            {
                return value;
            }

            for (int i = 1; i + 1 < a.Length; i += 2)
            {
                if (a[i] is Keyword keyword
                    && string.Equals(keyword.Name.Name, "default", StringComparison.Ordinal))
                {
                    return a[i + 1];
                }
            }

            return Nil.Instance;
        });

        interpreter.DefinePrimitive("ly:parser-append-to-include-path", 1, 1, a =>
        {
            Parser(interpreter, "ly:parser-append-to-include-path")
                .IncludePath.Add(StringPrimitives.Text(a[0], "ly:parser-append-to-include-path"));
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:parser-set-note-names", 1, 1, a =>
        {
            Parser(interpreter, "ly:parser-set-note-names").SetNoteNames(a[0]);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:parser-output-name", 0, 1, a =>
            new MutableString(
                Parser(interpreter, a, 0, "ly:parser-output-name").OutputBaseName ?? string.Empty));
    }

    private static void InstallDiagnostics(Interpreter interpreter)
    {
        // Without a current parser this is an ordinary error rather than a no-op —
        // upstream falls through to scm_misc_error, and swallowing it would hide every
        // diagnostic a music function raises outside a parse.
        interpreter.DefinePrimitive("ly:parser-error", 1, 2, a =>
        {
            string message = StringPrimitives.Text(a[0], "ly:parser-error");
            Input origin = a.Length > 1 ? a[1] as Input : null;
            ILilyParser parser = Current(interpreter);

            if (parser != null)
            {
                parser.ParserError(origin, message);
            }
            else if (origin != null)
            {
                origin.NonFatalError(message);
            }
            else
            {
                throw new ArgumentException("ly:parser-error: " + message);
            }

            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:parser-clear-error", 0, 1, a =>
        {
            ILilyParser parser = Parser(interpreter, a, 0, "ly:parser-clear-error");
            parser.ErrorLevel = 0;
            parser.LexerErrorLevel = 0;
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:parser-has-error?", 0, 1, a =>
        {
            ILilyParser parser = Parser(interpreter, a, 0, "ly:parser-has-error?");
            return parser.ErrorLevel != 0 || parser.LexerErrorLevel != 0;
        });
    }

    private static void InstallParsing(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:parser-clone", 0, 2, a =>
        {
            ILilyParser parser = Parser(interpreter, "ly:parser-clone");
            object closures = a.Length > 0 && !(a[0] is DefaultArgument) ? a[0] : Nil.Instance;
            Input origin = a.Length > 1 ? a[1] as Input : null;
            return parser.Clone(closures, origin);
        });

        interpreter.DefinePrimitive("ly:parser-parse-string", 2, 2, a =>
        {
            ILilyParser parser = AsParser(a[0], "ly:parser-parse-string");
            string code = StringPrimitives.Text(a[1], "ly:parser-parse-string");

            if (!parser.IsClean)
            {
                parser.ParserError(
                    null,
                    "ly:parser-parse-string is only valid with a new parser."
                    + "  Use ly:parser-include-string instead.");
            }
            else
            {
                parser.ParseString(code);
            }

            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:parse-string-expression", 2, 4, a =>
        {
            ILilyParser parser = AsParser(a[0], "ly:parse-string-expression");
            string code = StringPrimitives.Text(a[1], "ly:parse-string-expression");
            string fileName = a.Length > 2 && a[2] is MutableString name
                ? name.ToString()
                : "<string>";
            int line = a.Length > 3 && SchemeNumberIsInteger(a[3])
                ? (int)Convert.ToInt64(a[3], System.Globalization.CultureInfo.InvariantCulture)
                : 0;

            if (!parser.IsClean)
            {
                parser.ParserError(
                    null,
                    "ly:parse-string-expression is only valid with a new parser."
                    + "  Use ly:parser-include-string instead.");
                return Unspecified.Instance;
            }

            return parser.ParseStringExpression(code, fileName, line);
        });

        interpreter.DefinePrimitive("ly:parser-include-string", 1, 1, a =>
        {
            Parser(interpreter, "ly:parser-include-string")
                .IncludeString(StringPrimitives.Text(a[0], "ly:parser-include-string"));
            return Unspecified.Instance;
        });

        // ly:output-file-name-for-input-file-name: strip the extension, and the directory
        // too when -dstrip-output-dir is set (its default). The --output override is a
        // command-line concern the batch runner owns; until it exists there is nothing to
        // read it from, so only the input-derived branch is reachable.
        interpreter.DefinePrimitive("ly:output-file-name-for-input-file-name", 1, 1, a =>
        {
            string file = StringPrimitives.Text(a[0], "ly:output-file-name-for-input-file-name");
            Flower.FileName name = new Flower.FileName(file);
            name.Extension = string.Empty;
            name.Root = string.Empty;
            if (StripOutputDirectory(interpreter))
            {
                name.Directory = string.Empty;
            }

            return new MutableString(name.ToString());
        });

        InstallFileParsing(interpreter);
    }

    /// <summary>
    /// <c>ly:parse-file</c> and <c>ly:parse-init</c> — the session lifecycle
    /// <c>lily-parser-scheme.cc</c> exposes to Scheme.
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    /// <remarks>
    /// These were the two entry points the long-tail closure was asked to rule N/A and did not, because
    /// filing the session lifecycle as "not applicable" while
    /// <c>CodeBrix.LilyPort.BatchRunner</c> reimplements it is the stale-named-absence
    /// shape standing trap 8 records.
    /// <para>
    /// ⚠ The port has ONE parser session where upstream builds a fresh
    /// <c>Lily_parser</c> per file and throws it away — the per-file state that difference
    /// costs is restored by <c>SnapshotToplevelScope</c>/<c>RestoreToplevelScope</c>
    /// (standing trap 12), and these bindings run through that same ambient session
    /// rather than creating a second one. BatchRunner keeps its own path and does not go
    /// through here; nothing in the vendored Scheme reaches these either, because
    /// <c>ly:command-line-ly-files</c> answers <c>'()</c> and upstream's main loop is what
    /// would call them.
    /// </para>
    /// <para>
    /// Both throw upstream's <c>ly-file-failed</c> key on a non-zero error level, with the
    /// resolved file name as the payload. That is the ONE observable contract a Scheme
    /// caller can depend on, and it is reproduced exactly.
    /// </para>
    /// </remarks>
    private static void InstallFileParsing(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:parse-file", 1, 1, a =>
            ParseThroughAmbientSession(
                interpreter, a[0], "ly:parse-file", setOutputName: true));

        interpreter.DefinePrimitive("ly:parse-init", 1, 1, a =>
            ParseThroughAmbientSession(
                interpreter, a[0], "ly:parse-init", setOutputName: false));
    }

    /// <summary>
    /// Resolves a file against the include path, parses it through the ambient session,
    /// and raises <c>ly-file-failed</c> when the parse set an error level.
    /// </summary>
    /// <param name="interpreter">The interpreter holding the ambient parser.</param>
    /// <param name="argument">The file name, as passed from Scheme.</param>
    /// <param name="procedureName">The entry point's name, for errors.</param>
    /// <param name="setOutputName">
    /// Whether to derive the output base name from the input, as <c>ly:parse-file</c>
    /// does and <c>ly:parse-init</c> does not.
    /// </param>
    /// <returns>Unspecified, or throws.</returns>
    private static object ParseThroughAmbientSession(
        Interpreter interpreter, object argument, string procedureName, bool setOutputName)
    {
        if (!(argument is MutableString) && !(argument is string))
        {
            throw SchemeErrors.WrongType(procedureName, "string", argument);
        }

        string file = StringPrimitives.Text(argument, procedureName);
        ILilyParser parser = Parser(interpreter, procedureName);

        string resolved = ResolveOnIncludePath(parser, file);
        if (resolved == null)
        {
            // Upstream warns and then still throws ly-file-failed below, rather than
            // raising a wrong-type error: a missing file is a run failure, not a caller
            // mistake.
            Warn.Warning("cannot find file: `" + file + "'");
            throw FileFailed(file);
        }

        if (setOutputName)
        {
            Flower.FileName outputName = new Flower.FileName(resolved);
            outputName.Extension = string.Empty;
            outputName.Root = string.Empty;
            if (StripOutputDirectory(interpreter))
            {
                outputName.Directory = string.Empty;
            }

            parser.OutputBaseName = outputName.ToString();
        }

        // Upstream reads error_level_ off a parser that was created for this file alone,
        // so any non-zero level is THIS file's. The port's session is shared and may
        // already carry a level from an earlier file, so the test has to be that the
        // level ROSE — otherwise one bad file would make every later ly:parse-file throw.
        int errorsBefore = parser.ErrorLevel;
        int lexerErrorsBefore = parser.LexerErrorLevel;

        parser.IncludeString(File.ReadAllText(resolved));

        if (parser.ErrorLevel > errorsBefore || parser.LexerErrorLevel > lexerErrorsBefore)
        {
            throw FileFailed(resolved);
        }

        return Unspecified.Instance;
    }

    /// <summary>Builds upstream's <c>ly-file-failed</c> throw.</summary>
    /// <param name="fileName">The file that failed, which is the throw's whole payload.</param>
    /// <returns>The exception to raise.</returns>
    private static SchemeThrow FileFailed(string fileName)
        => new SchemeThrow(
            Symbol.Intern("ly-file-failed"), Pair.List(new MutableString(fileName)));

    /// <summary>
    /// Finds a file on the parser's include path, trying the name as given first.
    /// </summary>
    /// <param name="parser">The parser whose include path to search.</param>
    /// <param name="file">The file name.</param>
    /// <returns>The resolved path, or <see langword="null"/>.</returns>
    private static string ResolveOnIncludePath(ILilyParser parser, string file)
    {
        if (File.Exists(file))
        {
            return file;
        }

        foreach (string directory in parser.IncludePath)
        {
            string candidate = Path.Combine(directory, file);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void InstallSources(Interpreter interpreter)
    {
        // sources.cc's two bindings. Both need the parser's opened-file list, which is why
        // they arrive with lily-parser-scheme.cc rather than with the Sources type.
        interpreter.DefinePrimitive("ly:source-files", 0, 1, a =>
        {
            ILilyParser parser = a.Length > 0 && a[0] is ILilyParser given
                ? given
                : Current(interpreter);
            if (parser == null)
            {
                return Nil.Instance;
            }

            List<object> names = new List<object>();
            HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (SourceFile file in parser.SourceFiles)
            {
                if (seen.Add(file.Name))
                {
                    names.Add(new MutableString(file.Name));
                }
            }

            return Pair.List(names.ToArray());
        });

        // ly:note-extra-source-file records a file the run depends on that the lexer never
        // opened — a \markup \epsfile, an included image. It joins the same list.
        interpreter.DefinePrimitive("ly:note-extra-source-file", 1, 2, a =>
        {
            ILilyParser parser = a.Length > 1 && a[1] is ILilyParser given
                ? given
                : Current(interpreter);
            if (parser is IExtraSourceFiles extra)
            {
                extra.NoteExtraSourceFile(
                    StringPrimitives.Text(a[0], "ly:note-extra-source-file"));
            }

            return Unspecified.Instance;
        });
    }

    private static bool StripOutputDirectory(Interpreter interpreter)
    {
        object value = LilyPondScheme.Options.Get("strip-output-dir");
        return value != null && !(value is bool flag && !flag);
    }

    private static bool SchemeNumberIsInteger(object value)
        => value is long || value is int || value is System.Numerics.BigInteger;

    private static ILilyParser Parser(Interpreter interpreter, string who)
        => Current(interpreter)
           ?? throw new ArgumentException(who + ": there is no current parser");

    private static ILilyParser Parser(Interpreter interpreter, object[] arguments, int index, string who)
        => arguments.Length > index && arguments[index] is ILilyParser given
            ? given
            : Parser(interpreter, who);

    private static ILilyParser AsParser(object value, string who)
        => value as ILilyParser
           ?? throw new ArgumentException(who + ": argument 1 must be a parser");

    private static Symbol AsSymbol(object value, string who)
        => value as Symbol ?? throw new ArgumentException(who + ": argument 1 must be a symbol");
}

/// <summary>
/// Implemented by a parser that can be told about a file it never opened, which is what
/// <c>ly:note-extra-source-file</c> records.
/// </summary>
public interface IExtraSourceFiles
{
    /// <summary>Adds a file to the run's source set without opening it.</summary>
    /// <param name="fileName">The file's name.</param>
    void NoteExtraSourceFile(string fileName);
}
