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
using System.Linq;
using System.Reflection;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine;

/// <summary>Which C++ macro declared an entry point.</summary>
public enum EntryPointKind
{
    /// <summary>An <c>LY_DEFINE</c> — a primitive callable directly from Scheme.</summary>
    LyDefine,

    /// <summary>A <c>MAKE_SCHEME_CALLBACK</c> — a lazily-invoked grob property callback.</summary>
    Callback,

    /// <summary>
    /// A smob type predicate, declared by a C++ class's <c>type_p_name_</c> member rather
    /// than by either macro — which is why the first extraction pass missed all 36 of them.
    /// </summary>
    TypePredicate,
}

/// <summary>One Scheme-visible entry point that LilyPond implements in C++.</summary>
public sealed class EntryPoint
{
    /// <summary>Initializes an entry point.</summary>
    /// <param name="kind">Which macro declared it.</param>
    /// <param name="name">The Scheme-visible name.</param>
    /// <param name="requiredArguments">The required argument count.</param>
    /// <param name="optionalArguments">The optional argument count.</param>
    /// <param name="hasRest">Whether it takes a rest argument.</param>
    /// <param name="upstreamFile">The upstream file that declares it.</param>
    public EntryPoint(
        EntryPointKind kind,
        string name,
        int requiredArguments,
        int optionalArguments,
        bool hasRest,
        string upstreamFile)
    {
        Kind = kind;
        Name = name;
        RequiredArguments = requiredArguments;
        OptionalArguments = optionalArguments;
        HasRest = hasRest;
        UpstreamFile = upstreamFile;
    }

    /// <summary>Gets which macro declared this entry point.</summary>
    public EntryPointKind Kind { get; }

    /// <summary>Gets the Scheme-visible name.</summary>
    public string Name { get; }

    /// <summary>Gets the required argument count.</summary>
    public int RequiredArguments { get; }

    /// <summary>Gets the optional argument count.</summary>
    public int OptionalArguments { get; }

    /// <summary>Gets a value indicating whether a rest argument is accepted.</summary>
    public bool HasRest { get; }

    /// <summary>Gets the upstream file that declares this entry point.</summary>
    public string UpstreamFile { get; }

    /// <summary>Gets or sets how many times the stub for this entry point was invoked.</summary>
    public int CallCount { get; set; }

    /// <summary>
    /// Gets or sets the stub primitive installed for this entry point.
    /// <para>
    /// Kept so <see cref="EntryPointClosure"/> can tell an implemented primitive from an
    /// unimplemented one by REFERENCE: everything is registered as a stub first and the
    /// real implementations overwrite the bindings afterwards, so "is the binding still
    /// the object we installed" is the only honest test. A name-list would drift.
    /// </para>
    /// </summary>
    public Primitive Stub { get; set; }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The name and arity.</returns>
    public override string ToString()
        => Name + "/" + RequiredArguments.ToString(CultureInfo.InvariantCulture)
           + (OptionalArguments > 0 ? "+" + OptionalArguments.ToString(CultureInfo.InvariantCulture) : string.Empty)
           + (HasRest ? "+rest" : string.Empty);
}

/// <summary>
/// Registers every LilyPond C++ entry point as a throwing stub, and records which ones
/// get called.
/// <para>
/// This is the mechanism that turns "port 107,099 lines of C++" into an ordered
/// worklist. Loading LilyPond's Scheme layer against these stubs reveals exactly which
/// primitives the Scheme actually reaches for, and in what order, so the engine can be
/// ported demand-first instead of file-by-file.
/// </para>
/// </summary>
public static class EnginePrimitives
{
    private const string EntryPointResource = "entry-points.tsv";

    private static readonly Dictionary<string, EntryPoint> Registry
        = new Dictionary<string, EntryPoint>(StringComparer.Ordinal);

    /// <summary>Gets every known entry point, keyed by Scheme name.</summary>
    public static IReadOnlyDictionary<string, EntryPoint> All => Registry;

    /// <summary>
    /// Gets the entry points whose stubs have been invoked, most-called first. This is
    /// the porting worklist.
    /// </summary>
    /// <returns>The called entry points, ordered by call count descending.</returns>
    public static IReadOnlyList<EntryPoint> Called()
        => Registry.Values
            .Where(entry => entry.CallCount > 0)
            .OrderByDescending(entry => entry.CallCount)
            .ThenBy(entry => entry.Name, StringComparer.Ordinal)
            .ToList();

    /// <summary>Resets every call count, so a fresh measurement can be taken.</summary>
    public static void ResetCallCounts()
    {
        foreach (EntryPoint entry in Registry.Values)
        {
            entry.CallCount = 0;
        }
    }

    /// <summary>Reads the vendored entry-point table.</summary>
    /// <returns>The parsed entry points.</returns>
    public static IReadOnlyList<EntryPoint> LoadEntryPoints()
    {
        Assembly assembly = typeof(EnginePrimitives).Assembly;
        string resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(EntryPointResource, StringComparison.Ordinal));
        if (resource == null)
        {
            throw new InvalidOperationException(
                "Embedded resource '" + EntryPointResource + "' is missing from the assembly.");
        }

        List<EntryPoint> entries = new List<EntryPoint>();
        using (Stream stream = assembly.GetManifestResourceStream(resource))
        using (StreamReader reader = new StreamReader(stream))
        {
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] parts = line.Split('\t');
                if (parts.Length < 5)
                {
                    continue;
                }

                EntryPointKind kind;
                switch (parts[0])
                {
                    case "callback":
                        kind = EntryPointKind.Callback;
                        break;
                    case "type-predicate":
                        kind = EntryPointKind.TypePredicate;
                        break;
                    default:
                        kind = EntryPointKind.LyDefine;
                        break;
                }

                entries.Add(new EntryPoint(
                    kind,
                    parts[1],
                    int.Parse(parts[2], CultureInfo.InvariantCulture),
                    int.Parse(parts[3], CultureInfo.InvariantCulture),
                    parts[4] != "0",
                    parts.Length > 5 ? parts[5] : "-"));
            }
        }

        return entries;
    }

    /// <summary>
    /// Gets or sets a value indicating whether an unported stub throws when called.
    /// <para>
    /// Default is <see langword="false"/>: a stub records the call and returns an
    /// <see cref="UnportedValue"/> placeholder. That matters because LilyPond's Scheme
    /// CALLS these primitives at load time to build its tables, so a throwing stub
    /// aborts the whole file and hides every later call in it. Returning a placeholder
    /// lets loading continue and produces a far more complete worklist.
    /// </para>
    /// <para>
    /// Set to <see langword="true"/> once a primitive is expected to work, to catch
    /// silent reliance on a placeholder.
    /// </para>
    /// </summary>
    public static bool ThrowOnUnported { get; set; }

    /// <summary>
    /// The environment variable that asks for suite mode:
    /// <c>LILYPORT_THROW_ON_UNPORTED=1</c>.
    /// </summary>
    public const string SuiteModeVariable = "LILYPORT_THROW_ON_UNPORTED";

    /// <summary>
    /// Gets a value indicating whether the environment asks for suite mode.
    /// <para>
    /// Suite mode turns every placeholder into a failure, which is the only way to see
    /// the defect class standing rule 4 names: something upstream declares, the port
    /// half-reproduces, and NOTHING FAILS because an inert
    /// <see cref="UnportedValue"/> flowed politely through the caller. It is off by
    /// default because loading LilyPond's Scheme layer legitimately calls unported
    /// primitives while building its tables — see <see cref="ThrowOnUnported"/>.
    /// </para>
    /// <para>
    /// Opt in per run rather than per assembly:
    /// <c>LILYPORT_THROW_ON_UNPORTED=1 dotnet test CodeBrix.LilyPort.slnx -c Release</c>.
    /// </para>
    /// </summary>
    public static bool SuiteModeRequested
    {
        get
        {
            string value = Environment.GetEnvironmentVariable(SuiteModeVariable);
            return string.Equals(value, "1", StringComparison.Ordinal)
                   || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Turns <see cref="ThrowOnUnported"/> on for the lifetime of the returned scope, and
    /// restores the previous setting when it is disposed.
    /// <para>
    /// Scoped rather than set-and-forget because the flag is process-global, like every
    /// other piece of engine state here: a test that switched it on and returned would
    /// change the meaning of every test that ran afterwards on the same thread pool.
    /// </para>
    /// </summary>
    /// <returns>A scope that restores the previous setting on disposal.</returns>
    public static IDisposable ThrowingScope() => new ThrowOnUnportedScope();

    private sealed class ThrowOnUnportedScope : IDisposable
    {
        private readonly bool _previous;

        internal ThrowOnUnportedScope()
        {
            _previous = ThrowOnUnported;
            ThrowOnUnported = true;
        }

        public void Dispose() => ThrowOnUnported = _previous;
    }

    /// <summary>
    /// Installs every entry point into an interpreter as a stub. See
    /// <see cref="ThrowOnUnported"/> for what a stub does when called.
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    /// <returns>The number of stubs installed.</returns>
    public static int InstallStubs(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        Registry.Clear();
        int installed = 0;

        foreach (EntryPoint entry in LoadEntryPoints())
        {
            Registry[entry.Name] = entry;
            EntryPoint captured = entry;

            // Arity is deliberately declared as fully variadic. The stub's job is to be
            // REACHABLE, not to type-check: rejecting a call for arity would hide the
            // fact that the Scheme wanted this primitive at all, which is the one thing
            // we are trying to measure.
            entry.Stub = interpreter.DefinePrimitive(entry.Name, 0, -1, arguments =>
            {
                captured.CallCount++;
                if (ThrowOnUnported)
                {
                    throw new NotPortedException(captured, arguments.Length);
                }

                // A type predicate over a type that has not been ported yet answers #f,
                // and that answer is CORRECT rather than a placeholder: no instance of
                // the type can exist, so nothing can be one. Returning the inert
                // placeholder instead would be truthy, and every predicate in LilyPond's
                // Scheme would silently say yes.
                if (captured.Kind == EntryPointKind.TypePredicate)
                {
                    return false;
                }

                return new UnportedValue(captured);
            });

            installed++;
        }

        return installed;
    }

    /// <summary>
    /// Installs stubs and then loads LilyPond's Scheme layer, reporting what happened.
    /// </summary>
    /// <param name="interpreter">A bootstrapped interpreter.</param>
    /// <param name="schemeFiles">The <c>.scm</c> sources to load, in order.</param>
    /// <returns>A report of which files loaded and which entry points were reached.</returns>
    public static LoadReport LoadLilyPondScheme(
        Interpreter interpreter,
        IEnumerable<KeyValuePair<string, string>> schemeFiles)
    {
        LoadReport report = new LoadReport();
        foreach (KeyValuePair<string, string> file in schemeFiles)
        {
            try
            {
                SchemeBootstrap.LoadExpanded(interpreter, file.Value, LilyPondScheme.SourceNameFor(file.Key));
                report.Loaded.Add(file.Key);
            }
            catch (Exception ex)
            {
                Exception cause = ex;
                while (cause.InnerException != null)
                {
                    cause = cause.InnerException;
                }

                report.Failed[file.Key] = cause.Message;
            }
        }

        return report;
    }
}

/// <summary>
/// Raised when Scheme calls a LilyPond primitive that has not been ported from C++ yet.
/// The message names the upstream file, so the fix is always one grep away.
/// </summary>
public sealed class NotPortedException : Exception
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="entryPoint">The entry point that was called.</param>
    /// <param name="argumentCount">How many arguments the caller passed.</param>
    public NotPortedException(EntryPoint entryPoint, int argumentCount)
        : base(BuildMessage(entryPoint, argumentCount))
    {
        EntryPoint = entryPoint;
    }

    /// <summary>Gets the entry point that was called.</summary>
    public EntryPoint EntryPoint { get; }

    private static string BuildMessage(EntryPoint entryPoint, int argumentCount)
        => "'" + entryPoint.Name + "' is not ported yet ("
           + (entryPoint.Kind == EntryPointKind.LyDefine ? "LY_DEFINE" : "MAKE_SCHEME_CALLBACK")
           + " in lily/" + entryPoint.UpstreamFile + ", called with "
           + argumentCount.ToString(CultureInfo.InvariantCulture) + " argument(s))";
}

/// <summary>
/// The value an unported stub returns. It is deliberately inert and identifiable: if
/// one of these reaches real output, something depended on a primitive that has not
/// been ported.
/// </summary>
public sealed class UnportedValue
{
    /// <summary>Initializes a placeholder.</summary>
    /// <param name="entryPoint">The entry point that produced it.</param>
    public UnportedValue(EntryPoint entryPoint)
    {
        EntryPoint = entryPoint;
    }

    /// <summary>Gets the entry point that produced this placeholder.</summary>
    public EntryPoint EntryPoint { get; }

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description naming the unported primitive.</returns>
    public override string ToString() => "#<unported " + EntryPoint.Name + ">";
}

/// <summary>The outcome of loading a set of Scheme files.</summary>
public sealed class LoadReport
{
    /// <summary>Gets the files that loaded without error.</summary>
    public List<string> Loaded { get; } = new List<string>();

    /// <summary>Gets the files that failed, mapped to the reason.</summary>
    public Dictionary<string, string> Failed { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets the number of files attempted.</summary>
    public int Total => Loaded.Count + Failed.Count;

    /// <summary>Returns a one-line summary.</summary>
    /// <returns>The loaded and failed counts.</returns>
    public override string ToString()
        => Loaded.Count.ToString(CultureInfo.InvariantCulture) + " loaded, "
           + Failed.Count.ToString(CultureInfo.InvariantCulture) + " failed, of "
           + Total.ToString(CultureInfo.InvariantCulture);
}
