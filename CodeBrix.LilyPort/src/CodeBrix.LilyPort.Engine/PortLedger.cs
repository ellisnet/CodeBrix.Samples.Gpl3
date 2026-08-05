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
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine;

/// <summary>What the port intends to do with an upstream <c>lily/*.cc</c> file.</summary>
public enum LedgerDisposition
{
    /// <summary>The file's types and algorithms are carried by named C# file(s).</summary>
    Ported,

    /// <summary>The file is owed by a named engine port group (EPG).</summary>
    Group,

    /// <summary>The file has no analogue in the port, for a recorded reason.</summary>
    NoPort,
}

/// <summary>One upstream <c>lily/*.cc</c> file and what the port does with it.</summary>
public sealed class LedgerRow
{
    /// <summary>Initializes a ledger row.</summary>
    /// <param name="file">The upstream file name, without the <c>lily/</c> prefix.</param>
    /// <param name="disposition">What the port does with it.</param>
    /// <param name="detail">
    /// The carrying C# file(s), the owing group, or the no-port reason, according to
    /// <paramref name="disposition"/>.
    /// </param>
    /// <param name="notes">Free-text notes; empty for the ordinary cases.</param>
    public LedgerRow(string file, LedgerDisposition disposition, string detail, string notes)
    {
        File = file;
        Disposition = disposition;
        Detail = detail;
        Notes = notes;
    }

    /// <summary>Gets the upstream file name, without the <c>lily/</c> prefix.</summary>
    public string File { get; }

    /// <summary>Gets what the port does with the file.</summary>
    public LedgerDisposition Disposition { get; }

    /// <summary>Gets the carrying files, owing group, or no-port reason.</summary>
    public string Detail { get; }

    /// <summary>Gets the free-text notes, or an empty string.</summary>
    public string Notes { get; }

    /// <summary>
    /// Gets the C# files carrying this row, for a <see cref="LedgerDisposition.Ported"/>
    /// row. Empty for every other disposition.
    /// </summary>
    /// <returns>The repo-relative paths.</returns>
    public IReadOnlyList<string> PortedFiles()
        => Disposition == LedgerDisposition.Ported
            ? Detail.Split(';', StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();

    /// <summary>Returns the external representation.</summary>
    /// <returns>The file and its disposition.</returns>
    public override string ToString() => File + " -> " + Disposition + " (" + Detail + ")";
}

/// <summary>
/// The ledger over upstream's <c>lily/*.cc</c>: one row per file, so what remains to port
/// is COMPUTED rather than remembered.
/// <para>
/// This is the EPG0 deliverable. The alternative — a plan document listing what is left —
/// goes stale the moment a session lands a file and forgets to cross it off, and the
/// error is invisible because nothing fails. Here, a file that gains a port changes its
/// row, and <see cref="NotYetPorted"/> shrinks by construction.
/// </para>
/// <para>
/// The row set is VENDORED, not enumerated from <c>~/GitHome/lilypond</c>: standing rule 7
/// forbids any build or test step from reaching into the read-only reference tree. The
/// upstream file set was captured when the ledger was generated, and
/// <see cref="UpstreamFileCount"/> is the figure a re-sync would have to move.
/// </para>
/// </summary>
public static class PortLedger
{
    private const string LedgerResource = "lily-cc-ledger.tsv";

    /// <summary>
    /// The number of <c>lily/*.cc</c> files in the pinned upstream reference
    /// (LilyPond 2.27.2). Every one has exactly one ledger row.
    /// </summary>
    public const int UpstreamFileCount = 448;

    private static readonly IReadOnlyList<LedgerRow> RowCache = ReadLedger();

    /// <summary>Gets every ledger row, in upstream file-name order.</summary>
    public static IReadOnlyList<LedgerRow> Rows => RowCache;

    /// <summary>Gets the files whose types and algorithms are carried.</summary>
    public static IReadOnlyList<string> Ported => Select(LedgerDisposition.Ported);

    /// <summary>
    /// Gets the files still owed by an EPG group — the porting worklist, computed from
    /// the ledger rather than maintained by hand.
    /// </summary>
    public static IReadOnlyList<string> NotYetPorted => Select(LedgerDisposition.Group);

    /// <summary>Gets the files that will never be ported, each with a recorded reason.</summary>
    public static IReadOnlyList<string> NoPort => Select(LedgerDisposition.NoPort);

    /// <summary>Gets how many files each EPG group still owes, largest group first.</summary>
    /// <returns>The group names mapped to their outstanding file counts.</returns>
    public static IReadOnlyList<KeyValuePair<string, int>> RemainingByGroup()
        => RowCache
            .Where(row => row.Disposition == LedgerDisposition.Group)
            .GroupBy(row => row.Detail, StringComparer.Ordinal)
            .Select(group => new KeyValuePair<string, int>(group.Key, group.Count()))
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();

    /// <summary>Gets the rows a single EPG group owes.</summary>
    /// <param name="group">The group name, for example <c>EPG4</c>.</param>
    /// <returns>The upstream files that group still owes.</returns>
    public static IReadOnlyList<string> Owed(string group)
    {
        if (group == null)
        {
            throw new ArgumentNullException(nameof(group));
        }

        return RowCache
            .Where(row => row.Disposition == LedgerDisposition.Group
                          && string.Equals(row.Detail, group, StringComparison.Ordinal))
            .Select(row => row.File)
            .ToList();
    }

    private static IReadOnlyList<string> Select(LedgerDisposition disposition)
        => RowCache.Where(row => row.Disposition == disposition).Select(row => row.File).ToList();

    private static IReadOnlyList<LedgerRow> ReadLedger()
    {
        List<LedgerRow> rows = new List<LedgerRow>();

        using (StreamReader reader = new StreamReader(OpenResource(LedgerResource)))
        {
            string line;
            bool seenHeader = false;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] parts = line.Split('\t');
                if (!seenHeader)
                {
                    // The single column-name row, skipped once so a file named "file"
                    // could never be silently swallowed later in the table.
                    seenHeader = true;
                    if (string.Equals(parts[0], "file", StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                if (parts.Length < 3)
                {
                    throw new InvalidOperationException(
                        "Malformed ledger row (need at least 3 columns): " + line);
                }

                LedgerDisposition disposition;
                switch (parts[1])
                {
                    case "ported":
                        disposition = LedgerDisposition.Ported;
                        break;
                    case "group":
                        disposition = LedgerDisposition.Group;
                        break;
                    case "no-port":
                        disposition = LedgerDisposition.NoPort;
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Unknown ledger disposition '" + parts[1] + "' for " + parts[0]);
                }

                rows.Add(new LedgerRow(
                    parts[0],
                    disposition,
                    parts[2],
                    parts.Length > 3 ? parts[3] : string.Empty));
            }
        }

        return rows;
    }

    internal static Stream OpenResource(string suffix)
    {
        Assembly assembly = typeof(PortLedger).Assembly;
        string resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.Ordinal));
        if (resource == null)
        {
            throw new InvalidOperationException(
                "Embedded resource '" + suffix + "' is missing from the assembly.");
        }

        return assembly.GetManifestResourceStream(resource);
    }
}

/// <summary>Which mechanism declares a translator upstream.</summary>
public enum TranslatorKind
{
    /// <summary>An <c>ADD_TRANSLATOR</c> — a concrete C++ engraver or performer.</summary>
    Cpp,

    /// <summary>An <c>ADD_TRANSLATOR_GROUP</c> — a C++ translator group.</summary>
    Group,

    /// <summary>An <c>ly:register-translator</c> call in <c>scm/</c>.</summary>
    Scheme,
}

/// <summary>One translator upstream declares.</summary>
public sealed class TranslatorEntry
{
    /// <summary>Initializes a translator entry.</summary>
    /// <param name="kind">Which mechanism declares it.</param>
    /// <param name="name">The upstream translator name.</param>
    /// <param name="file">The declaring upstream file.</param>
    public TranslatorEntry(TranslatorKind kind, string name, string file)
    {
        Kind = kind;
        Name = name;
        File = file;
    }

    /// <summary>Gets which mechanism declares this translator.</summary>
    public TranslatorKind Kind { get; }

    /// <summary>Gets the upstream translator name, for example <c>Beam_engraver</c>.</summary>
    public string Name { get; }

    /// <summary>Gets the declaring upstream file.</summary>
    public string File { get; }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The name and kind.</returns>
    public override string ToString() => Name + " (" + Kind + ", " + File + ")";
}

/// <summary>
/// The translators upstream declares, vendored so gate G4 — every translator registered
/// and reached — is computed rather than remembered.
/// </summary>
public static class TranslatorManifest
{
    private const string ManifestResource = "translators.tsv";

    private static readonly IReadOnlyList<TranslatorEntry> EntryCache = ReadManifest();

    /// <summary>Gets every declared translator.</summary>
    public static IReadOnlyList<TranslatorEntry> Entries => EntryCache;

    /// <summary>Gets the concrete C++ translators (<c>ADD_TRANSLATOR</c>).</summary>
    public static IReadOnlyList<TranslatorEntry> Cpp => Of(TranslatorKind.Cpp);

    /// <summary>Gets the C++ translator groups (<c>ADD_TRANSLATOR_GROUP</c>).</summary>
    public static IReadOnlyList<TranslatorEntry> Groups => Of(TranslatorKind.Group);

    /// <summary>Gets the Scheme-implemented translators.</summary>
    public static IReadOnlyList<TranslatorEntry> Scheme => Of(TranslatorKind.Scheme);

    private static IReadOnlyList<TranslatorEntry> Of(TranslatorKind kind)
        => EntryCache.Where(entry => entry.Kind == kind).ToList();

    private static IReadOnlyList<TranslatorEntry> ReadManifest()
    {
        List<TranslatorEntry> entries = new List<TranslatorEntry>();

        using (StreamReader reader = new StreamReader(PortLedger.OpenResource(ManifestResource)))
        {
            string line;
            bool seenHeader = false;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0 || line[0] == '#')
                {
                    continue;
                }

                string[] parts = line.Split('\t');
                if (!seenHeader)
                {
                    seenHeader = true;
                    if (string.Equals(parts[0], "kind", StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                if (parts.Length < 3)
                {
                    throw new InvalidOperationException(
                        "Malformed translator row (need 3 columns): " + line);
                }

                TranslatorKind kind;
                switch (parts[0])
                {
                    case "cpp":
                        kind = TranslatorKind.Cpp;
                        break;
                    case "group":
                        kind = TranslatorKind.Group;
                        break;
                    case "scheme":
                        kind = TranslatorKind.Scheme;
                        break;
                    default:
                        throw new InvalidOperationException(
                            "Unknown translator kind '" + parts[0] + "' for " + parts[1]);
                }

                entries.Add(new TranslatorEntry(kind, parts[1], parts[2]));
            }
        }

        return entries;
    }
}

/// <summary>
/// How much of upstream's Scheme-visible surface the engine actually implements.
/// <para>
/// An entry point counts as implemented when its binding in the root module is no longer
/// the stub <see cref="EnginePrimitives.InstallStubs"/> installed for it. That is a
/// measurement, not a list: porting a primitive replaces the binding and moves the number
/// by itself, and a primitive that was ported and then lost its registration moves it
/// back. Gate G3 reads this.
/// </para>
/// </summary>
public sealed class EntryPointClosure
{
    private EntryPointClosure(IReadOnlyList<EntryPoint> implemented, IReadOnlyList<EntryPoint> stubbed)
    {
        Implemented = implemented;
        Stubbed = stubbed;
    }

    /// <summary>Gets the entry points whose binding is a real implementation.</summary>
    public IReadOnlyList<EntryPoint> Implemented { get; }

    /// <summary>Gets the entry points still answering from a stub.</summary>
    public IReadOnlyList<EntryPoint> Stubbed { get; }

    /// <summary>Gets how many entry points the manifest declares.</summary>
    public int Total => Implemented.Count + Stubbed.Count;

    /// <summary>Gets the stubbed entry points grouped by their declaring upstream file.</summary>
    /// <returns>The upstream files mapped to their outstanding entry-point counts.</returns>
    public IReadOnlyList<KeyValuePair<string, int>> StubbedByFile()
        => Stubbed
            .GroupBy(entry => entry.UpstreamFile, StringComparer.Ordinal)
            .Select(group => new KeyValuePair<string, int>(group.Key, group.Count()))
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Measures an interpreter that has been through
    /// <see cref="Bootstrap.LilyPondScheme.CreateInterpreter"/>.
    /// </summary>
    /// <param name="interpreter">The bootstrapped interpreter to measure.</param>
    /// <returns>The closure over every declared entry point.</returns>
    public static EntryPointClosure Measure(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        List<EntryPoint> implemented = new List<EntryPoint>();
        List<EntryPoint> stubbed = new List<EntryPoint>();

        foreach (EntryPoint entry in EnginePrimitives.All.Values)
        {
            Variable variable = interpreter.GuileModule.Lookup(Symbol.Intern(entry.Name));

            // An entry point whose binding vanished entirely counts as stubbed rather
            // than implemented: the Scheme cannot call it either way, and calling it
            // "implemented" would flatter the gate.
            if (variable == null || !variable.IsBound
                || ReferenceEquals(variable.GetValue(), entry.Stub))
            {
                stubbed.Add(entry);
            }
            else
            {
                implemented.Add(entry);
            }
        }

        return new EntryPointClosure(implemented, stubbed);
    }

    /// <summary>Returns a one-line summary.</summary>
    /// <returns>The implemented and stubbed counts.</returns>
    public override string ToString()
        => Implemented.Count.ToString(CultureInfo.InvariantCulture) + " implemented, "
           + Stubbed.Count.ToString(CultureInfo.InvariantCulture) + " stubbed, of "
           + Total.ToString(CultureInfo.InvariantCulture);
}
