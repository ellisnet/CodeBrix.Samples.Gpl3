// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace CodeBrix.LilyPort.ConvertLy; //was previously: scripts/convert-ly.py;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Brings an old document up to the syntax this engine reads — the in-process
/// equivalent of LilyPond's own <c>convert-ly</c> script.
/// <para>
/// A document written for any release from 1.2.3 onwards is carried forward by applying,
/// in order, every rule whose version falls between the document's own <c>\version</c>
/// and the target, and the <c>\version</c> line is rewritten to say where it arrived.
/// </para>
/// </summary>
public static class DocumentConverter
{
    // convert-ly.py:41. The loose form -- used to FIND a version line to rewrite.
    private const string VersionPattern = "\\\\version *\"([0-9.]+)\"";

    // convert-ly.py's strict form, which is what a version is READ with: a third
    // component may be missing only when the second is even, because there are no
    // conversion rules inside a stable series.
    private const string StrictVersionPattern
        = "\\\\version *\"([0-9]+)\\.([0-9]+)(?:\\.([0-9]+))?\"";

    /// <summary>Gets every conversion rule, in the order they are applied.</summary>
    public static IReadOnlyList<ConversionRule> Rules => ConvertRules.All;

    /// <summary>
    /// Gets the newest version any rule converts to — <c>convert-ly</c>'s
    /// <c>latest_version()</c>, and the default target.
    /// </summary>
    public static ConversionVersion LatestVersion
        => ConvertRules.All[ConvertRules.All.Count - 1].Version;

    /// <summary>
    /// Reads the version a document declares.
    /// </summary>
    /// <param name="text">The document text.</param>
    /// <param name="version">The version declared.</param>
    /// <returns>Whether a usable version was declared.</returns>
    /// <remarks>
    /// Upstream's <c>guess_lilypond_version</c> distinguishes three outcomes: a usable
    /// version, a version line whose shape it refuses (an odd minor with no third
    /// component — it raises <c>InvalidVersion</c>), and no version line at all. Both
    /// refusals answer <see langword="false"/> here, and
    /// <see cref="TryReadDeclaredVersion(string, out ConversionVersion, out bool)"/>
    /// tells them apart for a caller that wants to say which happened.
    /// </remarks>
    public static bool TryReadDeclaredVersion(string text, out ConversionVersion version)
        => TryReadDeclaredVersion(text, out version, out bool _);

    /// <summary>
    /// Reads the version a document declares, saying whether an unusable version line
    /// was the reason for failing.
    /// </summary>
    /// <param name="text">The document text.</param>
    /// <param name="version">The version declared.</param>
    /// <param name="malformed">Whether a version line was found but refused.</param>
    /// <returns>Whether a usable version was declared.</returns>
    public static bool TryReadDeclaredVersion(
        string text, out ConversionVersion version, out bool malformed)
    {
        version = default;
        malformed = false;

        Match strict = PythonRegex.Search(StrictVersionPattern, text);
        if (strict.Success
            && (strict.Groups[3].Success
                || (int.Parse(strict.Groups[2].Value) % 2) == 0))
        {
            return ConversionVersion.TryParse(
                strict.Groups[1].Value + "." + strict.Groups[2].Value
                + (strict.Groups[3].Success ? "." + strict.Groups[3].Value : ".0"),
                out version);
        }

        malformed = PythonRegex.Search(VersionPattern, text).Success;
        return false;
    }

    /// <summary>
    /// Converts a document, rewriting its <c>\version</c> line to the version it
    /// reaches.
    /// </summary>
    /// <param name="text">The document text.</param>
    /// <param name="from">
    /// The version to convert FROM, or <see langword="null"/> to use the document's own
    /// <c>\version</c>.
    /// </param>
    /// <param name="to">
    /// The version to convert TO, or <see langword="null"/> for <see cref="LatestVersion"/>.
    /// </param>
    /// <returns>The result.</returns>
    public static ConversionResult Convert(
        string text,
        ConversionVersion? from = null,
        ConversionVersion? to = null)
    {
        string input = Normalize(text);

        ConversionVersion declared = default;
        bool hasDeclared = TryReadDeclaredVersion(input, out declared);
        ConversionVersion? fromVersion = from ?? (hasDeclared ? declared : (ConversionVersion?)null);
        if (fromVersion == null)
        {
            return ConversionResult.Unknown(input);
        }

        ConversionVersion toVersion = to ?? LatestVersion;

        //The rules carry a little state of their own (2.15.18 LEARNS function names
        //as it goes). Upstream keeps it in a module global, so a convert-ly run over
        //several files carries what it learned from one into the next; a conversion
        //here is one document, so the state starts fresh every time.
        ConvertRules.ResetRuleState();
        List<string> messages = ConvertRules.BeginCollecting();
        ConversionVersion? lastRule = null;
        ConversionVersion? lastChange = null;
        List<ConversionVersion> applied = new List<ConversionVersion>();
        int errors = 0;
        string result = input;

        try
        {
            foreach (ConversionRule rule in ConvertRules.All)
            {
                if (!(fromVersion.Value < rule.Version && rule.Version <= toVersion))
                {
                    continue;
                }

                string converted = rule.Convert(result);
                lastRule = rule.Version;
                applied.Add(rule.Version);
                if (!string.Equals(converted, result, System.StringComparison.Ordinal))
                {
                    lastChange = rule.Version;
                }

                result = converted;
            }
        }
        catch (FatalConversionError)
        {
            // Upstream: "Error while converting / Stopping at last successful rule".
            errors++;
        }
        catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
        {
            // See PythonRegex.MatchTimeout: upstream would still be running. The
            // document keeps everything the rules before this one did, and the caller
            // is told which rule could not finish.
            ConvertRules.StdErr(
                "A conversion rule could not finish on this document and was "
                + "abandoned. Everything up to that rule has been applied.");
            errors++;
        }
        finally
        {
            ConvertRules.EndCollecting();
        }

        // convert-ly.py:311-336. The version WRITTEN is the last rule that actually
        // CHANGED something -- "note that last_change can be set even if the result is
        // the same if two conversion rules cancelled out" -- and a document that came
        // out unchanged keeps the version it had.
        ConversionVersion? stamp = lastRule;
        if (stamp != null)
        {
            if (string.Equals(result, input, System.StringComparison.Ordinal))
            {
                stamp = hasDeclared ? declared : fromVersion;
            }
            else
            {
                stamp = lastChange;
                if (stamp != null && stamp.Value.IsUnstable)
                {
                    // An unstable series is stamped with the stable release that
                    // follows it, when the target has got that far.
                    ConversionVersion nextStable = new ConversionVersion(
                        stamp.Value.Major, stamp.Value.Minor + 1, 0);
                    if (nextStable <= toVersion)
                    {
                        stamp = nextStable;
                    }
                }
            }
        }

        if (stamp != null)
        {
            string versionLine = "\\version \"" + stamp.Value + "\"";
            result = PythonRegex.Search(VersionPattern, result).Success
                ? PythonRegex.Sub(VersionPattern, versionLine.Replace("\\", "\\\\"), result)
                : versionLine + "\n" + result;
        }

        return new ConversionResult(
            result, fromVersion.Value, toVersion, lastRule, lastChange, stamp,
            applied, messages, errors,
            !string.Equals(result, input, System.StringComparison.Ordinal));
    }

    /// <summary>
    /// Lists the rules that would run — what upstream's <c>--show-rules</c> prints,
    /// and what an editor shows a user before it touches their document.
    /// </summary>
    /// <param name="from">The version to convert from.</param>
    /// <param name="to">The version to convert to.</param>
    /// <returns>The rules, in order.</returns>
    public static IReadOnlyList<ConversionRule> RulesBetween(
        ConversionVersion from, ConversionVersion to)
    {
        List<ConversionRule> found = new List<ConversionRule>();
        foreach (ConversionRule rule in ConvertRules.All)
        {
            if (from < rule.Version && rule.Version <= to)
            {
                found.Add(rule);
            }
        }

        return found;
    }

    /// <summary>
    /// Puts a document's line endings in the one shape the rules are written against.
    /// </summary>
    /// <param name="text">The document text.</param>
    /// <returns>The normalized text.</returns>
    /// <remarks>
    /// convert-ly.py:286-291 reads the file as BYTES and then runs it through
    /// <c>io.StringIO(input, newline=None)</c> — universal newlines — precisely because
    /// it cannot open in text mode without knowing the encoding first. The rules then
    /// only ever see <c>\n</c>, and several of them would not match otherwise.
    /// </remarks>
    private static string Normalize(string text)
        => (text ?? string.Empty).Replace("\r\n", "\n").Replace("\r", "\n");
}

/// <summary>What a conversion did.</summary>
public sealed class ConversionResult
{
    internal ConversionResult(
        string text,
        ConversionVersion fromVersion,
        ConversionVersion toVersion,
        ConversionVersion? lastRule,
        ConversionVersion? lastChange,
        ConversionVersion? stampedVersion,
        IReadOnlyList<ConversionVersion> appliedRules,
        IReadOnlyList<string> messages,
        int errors,
        bool changed)
    {
        Text = text;
        FromVersion = fromVersion;
        ToVersion = toVersion;
        LastRuleApplied = lastRule;
        LastChange = lastChange;
        StampedVersion = stampedVersion;
        AppliedRules = appliedRules;
        Messages = messages;
        Errors = errors;
        Changed = changed;
    }

    /// <summary>Gets the converted document.</summary>
    public string Text { get; }

    /// <summary>Gets the version the document was converted from.</summary>
    public ConversionVersion FromVersion { get; }

    /// <summary>Gets the version the document was converted to.</summary>
    public ConversionVersion ToVersion { get; }

    /// <summary>Gets the last rule that ran, or <see langword="null"/> if none did.</summary>
    public ConversionVersion? LastRuleApplied { get; }

    /// <summary>Gets the last rule that changed anything.</summary>
    public ConversionVersion? LastChange { get; }

    /// <summary>Gets the version written into the document's <c>\version</c> line.</summary>
    public ConversionVersion? StampedVersion { get; }

    /// <summary>Gets every rule version that ran.</summary>
    public IReadOnlyList<ConversionVersion> AppliedRules { get; }

    /// <summary>
    /// Gets what the rules had to say — the "not smart enough to convert" remarks that
    /// tell a user which parts of their document still need a human.
    /// </summary>
    public IReadOnlyList<string> Messages { get; }

    /// <summary>Gets how many rules gave up.</summary>
    public int Errors { get; }

    /// <summary>Gets whether the document text changed at all.</summary>
    public bool Changed { get; }

    /// <summary>Gets whether the document declared no version to start from.</summary>
    public bool VersionUnknown { get; private init; }

    /// <summary>The result for a document with no usable version line.</summary>
    /// <param name="text">The document text, unchanged.</param>
    /// <returns>The result.</returns>
    internal static ConversionResult Unknown(string text)
        => new ConversionResult(
            text, default, default, null, null, null,
            new List<ConversionVersion>(), new List<string>(), 0, false)
        {
            VersionUnknown = true,
        };
}
