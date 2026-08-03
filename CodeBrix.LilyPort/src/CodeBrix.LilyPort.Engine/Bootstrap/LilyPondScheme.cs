// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// Access to LilyPond's vendored Scheme layer, and the bootstrap that loads it on top of
/// LilyScheme.
/// <para>
/// The 91 files under <c>Scheme/lily</c> are a byte-identical mirror of LilyPond's
/// <c>scm/</c> directory at v2.27.2. They are loaded in LilyPond's own order, taken from
/// <c>scm/lily.scm</c>, because the files define things for each other and loading them
/// alphabetically produces a cascade of spurious unbound-variable failures.
/// </para>
/// </summary>
public static class LilyPondScheme
{
    private const string LoadOrderResource = "load-order.txt";
    private const string LilyFolderMarker = ".Scheme.lily.";

    private static readonly string[] LoadOrderCache = ReadLoadOrder();

    /// <summary>
    /// Gets the program-option table the most recently created interpreter uses. It is
    /// also where the <c>ly:warning</c> family's output goes, so a test can read back
    /// what LilyPond's Scheme complained about while loading.
    /// </summary>
    public static ProgramOptions Options { get; private set; } = new ProgramOptions();

    /// <summary>
    /// Gets the registries the most recently created interpreter fills in: grob
    /// interfaces, translators and stencil heads. LilyPond's Scheme populates all three
    /// while it loads, so their contents are a direct measure of how much of the layer
    /// actually ran.
    /// </summary>
    public static EngineRegistries Registries { get; private set; } = new EngineRegistries();

    /// <summary>
    /// Gets LilyPond's own load order, from <c>scm/lily.scm</c>, filtered to the files
    /// that actually exist.
    /// <para>
    /// Upstream's list names <c>font-encodings</c>, which was deleted from <c>scm/</c>
    /// without the list being updated; entries with no matching file are skipped rather
    /// than reported as failures, because they are not ours to fix.
    /// </para>
    /// </summary>
    /// <returns>The ordered file names, without the <c>.scm</c> suffix.</returns>
    public static IReadOnlyList<string> LoadOrder() => LoadOrderCache;

    /// <summary>
    /// Gets every vendored <c>scm/</c> file name, load-order files first and the rest in
    /// alphabetical order. The remainder are the documentation generators, the
    /// output backends and other modules LilyPond loads on demand rather than at startup.
    /// </summary>
    /// <returns>The file names, without the <c>.scm</c> suffix.</returns>
    public static IReadOnlyList<string> AllFiles()
    {
        HashSet<string> ordered = new HashSet<string>(LoadOrderCache, StringComparer.Ordinal);
        List<string> all = new List<string>(LoadOrderCache);
        all.AddRange(VendoredNames().Where(name => !ordered.Contains(name)).OrderBy(name => name, StringComparer.Ordinal));
        return all;
    }

    /// <summary>Gets the names of every vendored <c>scm/</c> file, unordered.</summary>
    /// <returns>The file names, without the <c>.scm</c> suffix.</returns>
    public static IEnumerable<string> VendoredNames()
    {
        Assembly assembly = typeof(LilyPondScheme).Assembly;
        foreach (string resource in assembly.GetManifestResourceNames())
        {
            int marker = resource.IndexOf(LilyFolderMarker, StringComparison.Ordinal);
            if (marker < 0 || !resource.EndsWith(".scm", StringComparison.Ordinal))
            {
                continue;
            }

            string name = resource.Substring(marker + LilyFolderMarker.Length);
            yield return name.Substring(0, name.Length - 4);
        }
    }

    /// <summary>Reads one vendored <c>scm/</c> file.</summary>
    /// <param name="name">The file name, with or without the <c>.scm</c> suffix.</param>
    /// <returns>The Scheme source text, or <see langword="null"/> when there is no such file.</returns>
    public static string ReadSource(string name)
    {
        if (name == null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        string fileName = name.EndsWith(".scm", StringComparison.Ordinal) ? name : name + ".scm";
        return ReadResource(LilyFolderMarker + fileName);
    }

    /// <summary>
    /// Creates an interpreter with LilyScheme bootstrapped, the LilyPond Scheme-side
    /// support layer installed, and every unported C++ entry point stubbed.
    /// </summary>
    /// <returns>An interpreter ready to load LilyPond's <c>scm/</c> layer.</returns>
    public static Interpreter CreateInterpreter()
    {
        Interpreter interpreter = new Interpreter();
        SchemeBootstrap.LoadCore(interpreter);

        // Order matters. The stubs go in first so every entry point is REACHABLE and
        // recorded; the ported primitives then replace the ones that exist, and the
        // difference between the two sets is the porting worklist.
        EnginePrimitives.InstallStubs(interpreter);
        EngineClasses.Install();
        EmbeddedLilyReader.Install();
        Options = GeneralPrimitives.Install(interpreter);
        MusicPrimitives.Install(interpreter);
        Registries = RegistryPrimitives.Install(interpreter);
        EngineSupport.Install(interpreter);
        return interpreter;
    }

    /// <summary>
    /// Loads LilyPond's Scheme layer the way LilyPond itself does: by loading
    /// <c>lily.scm</c> and letting its own <c>(for-each ly:load init-scheme-files)</c>
    /// pull in the rest.
    /// <para>
    /// This ordering is not a stylistic preference. <c>lily.scm</c> builds its
    /// type-predicate tables AFTER the load list, from predicates that <c>c++.scm</c> and
    /// <c>lily-library.scm</c> define -- so loading the list without <c>lily.scm</c>
    /// around it leaves the session machinery, the SRFI imports and <c>G_</c> undefined,
    /// while loading <c>lily.scm</c> alone leaves its tables referring to nothing.
    /// </para>
    /// </summary>
    /// <param name="interpreter">A bootstrapped interpreter.</param>
    /// <returns>A report of which files loaded and which failed.</returns>
    public static LoadReport LoadViaLilyScm(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        LoadReport report = new LoadReport();

        // The hook is NOT ly:load. lily.scm defines its own ly:load in Scheme, which
        // resolves the name with %search-load-path and then hands off to
        // primitive-load-path -- so overriding ly:load has no effect at all, because
        // lily.scm replaces it before the load list runs. These two are the real seam,
        // and the vendored resources are what stands in for Guile's load path.
        // Resolution always succeeds for a name under lily/, and primitive-load-path
        // below decides whether there is anything to load. That split is deliberate:
        // upstream's own load list still names font-encodings, which was deleted from
        // scm/ without the list being updated, and lily.scm turns an unresolved name into
        // a fatal ly:error. Answering honestly there would abort the whole load over a
        // defect that is upstream's, not ours.
        interpreter.DefinePrimitive("%search-load-path", 1, 1, arguments =>
            new MutableString(StringPrimitives.Text(arguments[0], "%search-load-path") + ".scm"));

        // A failing file must NOT abort the run: LilyPond's own loader would stop, but
        // the point of this pass is to see how far every file gets, not just the first.
        interpreter.DefinePrimitive("primitive-load-path", 1, 2, arguments =>
        {
            string name = NameOf(arguments[0]);
            string source = ReadSource(name);
            if (source == null)
            {
                // Upstream's list still names font-encodings, deleted from scm/ without
                // the list being updated. Not ours to fix, and not a failure.
                return Unspecified.Instance;
            }

            if (report.Loaded.Contains(name) || report.Failed.ContainsKey(name))
            {
                return Unspecified.Instance;
            }

            try
            {
                SchemeBootstrap.LoadExpanded(interpreter, source, name);
                report.Loaded.Add(name);
            }
            catch (Exception ex)
            {
                report.Failed[name] = Describe(ex);
            }

            return Unspecified.Instance;
        });

        try
        {
            SchemeBootstrap.LoadExpanded(interpreter, ReadSource("lily"), "lily.scm");
            report.Loaded.Add("lily");
        }
        catch (Exception ex)
        {
            report.Failed["lily"] = Describe(ex);
        }

        return report;
    }

    /// <summary>
    /// Loads the <c>scm/</c> files LilyPond does NOT load at startup, retrying until no
    /// further progress is made.
    /// <para>
    /// These are the documentation generators, the output backends and the other modules
    /// LilyPond pulls in on demand. Unlike the startup list, nothing records the order
    /// they depend on each other in -- so rather than guess, this repeats the pass while
    /// it keeps succeeding. A file that fails only because a library it needs has not
    /// loaded yet succeeds on the next round; a file that fails for its own reasons fails
    /// every round, and the last failure is what gets reported.
    /// </para>
    /// </summary>
    /// <param name="interpreter">An interpreter with the startup layer already loaded.</param>
    /// <param name="names">The files to attempt.</param>
    /// <returns>A report of which files loaded and which failed.</returns>
    public static LoadReport LoadToFixpoint(Interpreter interpreter, IEnumerable<string> names)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        List<string> pending = new List<string>(names ?? Array.Empty<string>());
        LoadReport report = new LoadReport();

        while (pending.Count > 0)
        {
            LoadReport round = Load(interpreter, pending);
            report.Loaded.AddRange(round.Loaded);

            if (round.Loaded.Count == 0)
            {
                // No progress this round, so nothing left will ever succeed.
                foreach (KeyValuePair<string, string> failure in round.Failed)
                {
                    report.Failed[failure.Key] = failure.Value;
                }

                break;
            }

            pending = new List<string>(round.Failed.Keys);
        }

        return report;
    }

    /// <summary>Loads LilyPond's Scheme layer into an interpreter, in LilyPond's order.</summary>
    /// <param name="interpreter">A bootstrapped interpreter.</param>
    /// <param name="names">The file names to load, or <see langword="null"/> for the load order.</param>
    /// <returns>A report of which files loaded and which failed.</returns>
    public static LoadReport Load(Interpreter interpreter, IEnumerable<string> names)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        IEnumerable<string> targets = names ?? LoadOrder();
        List<KeyValuePair<string, string>> sources = new List<KeyValuePair<string, string>>();
        foreach (string name in targets)
        {
            string source = ReadSource(name);
            if (source != null)
            {
                sources.Add(new KeyValuePair<string, string>(name, source));
            }
        }

        return EnginePrimitives.LoadLilyPondScheme(interpreter, sources);
    }

    /// <summary>Reads a file from the LilyPort-authored <c>Scheme/lilyport</c> folder.</summary>
    /// <param name="fileName">The file name, for example <c>support.scm</c>.</param>
    /// <returns>The Scheme source text, or <see langword="null"/> when there is no such file.</returns>
    public static string ReadSupportResource(string fileName)
    {
        if (fileName == null)
        {
            throw new ArgumentNullException(nameof(fileName));
        }

        return ReadResource(".Scheme.lilyport." + fileName);
    }

    private static string NameOf(object value)
    {
        // ly:load is called with a Scheme string; MutableString.ToString() is the
        // external representation, complete with quotes, so go through the text accessor.
        string text = value == null
            ? string.Empty
            : StringPrimitives.Text(value, "ly:load");
        int slash = text.LastIndexOf('/');
        if (slash >= 0)
        {
            text = text.Substring(slash + 1);
        }

        return text.EndsWith(".scm", StringComparison.Ordinal)
            ? text.Substring(0, text.Length - 4)
            : text;
    }

    private static string Describe(Exception exception)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        while (exception != null)
        {
            if (builder.Length > 0)
            {
                builder.Append(" <- ");
            }

            builder.Append(exception.Message);
            exception = exception.InnerException;
        }

        return builder.ToString();
    }

    private static Exception Deepest(Exception exception)
    {
        while (exception.InnerException != null)
        {
            exception = exception.InnerException;
        }

        return exception;
    }

    private static string[] ReadLoadOrder()
    {
        string text = ReadResource("." + LoadOrderResource);
        if (text == null)
        {
            throw new InvalidOperationException("Embedded resource 'load-order.txt' is missing from the assembly.");
        }

        HashSet<string> present = new HashSet<string>(VendoredNames(), StringComparer.Ordinal);
        List<string> order = new List<string>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || !present.Contains(line))
            {
                continue;
            }

            order.Add(line);
        }

        return order.ToArray();
    }

    private static string ReadResource(string suffix)
    {
        Assembly assembly = typeof(LilyPondScheme).Assembly;
        string match = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.Ordinal));
        if (match == null)
        {
            return null;
        }

        using (Stream stream = assembly.GetManifestResourceStream(match))
        using (StreamReader reader = new StreamReader(stream))
        {
            return reader.ReadToEnd();
        }
    }
}
