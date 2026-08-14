// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The entry points ruled NOT APPLICABLE to the port under decision D25, installed as
/// LOUD bindings.
/// <para>
/// D25's mechanism, and the reason this file exists rather than a list of missing names:
/// an accepted N/A binding still EXISTS and THROWS <c>"not applicable: &lt;reason&gt;"</c>.
/// It never silently answers <c>#f</c>, and it never answers the inert
/// <see cref="UnportedValue"/> — either of those would let a caller carry on with a wrong
/// answer, which is the whole defect class standing rule 4 is about. If Phase 4 or the
/// ratchet ever reaches one of these, it fails loudly with its reason and the row flips
/// back to owed.
/// </para>
/// <para>
/// Jeremy rules on CATEGORIES, not on items. The categories used here are
/// <c>ps-backend</c> (D15), <c>font-plumbing</c> (D13/D23), <c>guile-internals</c>, and
/// <c>instrumentation</c> — the last ratified during the long-tail closure. Every row is also
/// recorded in <c>entry-point-na-candidates.tsv</c>.
/// </para>
/// </summary>
public static class NotApplicableEntryPoints
{
    /// <summary>Installs the N/A bindings, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        InstallPostScriptBackend(interpreter);
        InstallFontPlumbing(interpreter);
        InstallGuileInternals(interpreter);
        InstallInstrumentation(interpreter);
    }

    /// <summary>
    /// Category <c>ps-backend</c> (D15): SVG is the port's only backend, so the PostScript
    /// and Cairo output paths — and the PNG conversion that exists to serve them — have
    /// nothing to drive.
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallPostScriptBackend(Interpreter interpreter)
    {
        NotApplicable(interpreter, "ly:cairo-output-stencil", 4, 4, "ps-backend",
            "the Cairo backend writes PDF, PostScript and PNG; D15 makes SVG the port's "
            + "only backend");

        NotApplicable(interpreter, "ly:cairo-output-stencils", 5, 5, "ps-backend",
            "the Cairo backend writes PDF, PostScript and PNG; D15 makes SVG the port's "
            + "only backend");

        NotApplicable(interpreter, "ly:png-dimensions", 1, 1, "ps-backend",
            "reads back the size of a PNG the Ghostscript pipeline produced; that "
            + "pipeline is not ported (D15)");

        NotApplicable(interpreter, "ly:png->eps-dump", 6, 6, "ps-backend",
            "wraps a PNG in an EPS for inclusion in a PostScript stream (D15)");

        NotApplicable(interpreter, "ly:ttf->pfa", 1, 2, "ps-backend",
            "converts a TrueType face to a PostScript Type 1 font for embedding (D15)");

        NotApplicable(interpreter, "ly:ttf-ps-name", 1, 2, "ps-backend",
            "reads the PostScript name a font would be embedded under (D15)");
    }

    /// <summary>
    /// Category <c>font-plumbing</c> (D13/D23): the port's font layer is the 24 vendored
    /// faces plus the pinned Roboto fallback, with NO system-font fallback ever, so there
    /// is no host font configuration to query or extend.
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallFontPlumbing(Interpreter interpreter)
    {
        NotApplicable(interpreter, "ly:font-config-add-directory", 1, 1, "font-plumbing",
            "adds a directory to fontconfig's search; D23 forbids system-font fallback, "
            + "so there is no host search to widen");

        NotApplicable(interpreter, "ly:font-config-add-font", 1, 1, "font-plumbing",
            "registers a font file with fontconfig; the port's faces are vendored and "
            + "registered through its own font layer (D13/D23)");

        NotApplicable(interpreter, "ly:font-config-display-fonts", 0, 1, "font-plumbing",
            "dumps fontconfig's view of the host's fonts; the port never consults it "
            + "(D13/D23)");

        NotApplicable(interpreter, "ly:font-config-get-font-file", 1, 1, "font-plumbing",
            "resolves a family name to a file through fontconfig; D23 forbids the host "
            + "lookup outright");

        NotApplicable(interpreter, "ly:pango-font-physical-fonts", 1, 1, "font-plumbing",
            "lists the physical faces a Pango font resolved to; the port has no Pango "
            + "layer, and ly:pango-font? answers #f for everything (D13/D23)");
    }

    /// <summary>
    /// Category <c>guile-internals</c>: GC and smob plumbing that exists to inspect
    /// Guile's own allocator, which the port does not have.
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallGuileInternals(Interpreter interpreter)
    {
        NotApplicable(interpreter, "ly:smob-protects", 0, 0, "guile-internals",
            "lists the smobs held alive by GC protection; the port's objects are ordinary "
            + "CLR references and nothing protects them explicitly");

        // ⚠ ly:parsed-undead-list! was FILED N/A HERE and taken straight back out again,
        // in the same session, by D25's reversal-by-demand rule — the demanding file is
        // ly/declarations-init.ly line 171, whose `#(session-save)' calls lily.scm's
        // (dump-zombies 0), which calls this on EVERY init-layer load. It is installed as
        // a real implementation in InstallGuileLiveness below.
        InstallGuileLiveness(interpreter);
    }

    /// <summary>
    /// <c>undead.cc</c>'s <c>ly:parsed-undead-list!</c> — the list of parsed objects found
    /// alive that should have been dead, which the port can answer honestly.
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    /// <remarks>
    /// Upstream's <c>parsed_dead::readout ()</c> walks a vector that is only ever
    /// populated during a mark pass with <c>debug-gc-assert-parsed-dead</c> armed, and
    /// answers <c>SCM_EOL</c> when it is empty — which is every ordinary run.
    /// <para>
    /// The port tracks no such objects at all, so the empty list is not a placeholder for
    /// the real answer, it IS the real answer: nothing was found alive that should have
    /// been dead, because nothing is being watched. <c>dump-zombies</c> then iterates over
    /// nothing and reports nothing, exactly as upstream does in a run with no zombies.
    /// </para>
    /// <para>
    /// This is what the stub was accidentally getting away with: an
    /// <see cref="UnportedValue"/> is not a pair, so <c>for-each</c> walked zero
    /// elements and nobody noticed (trap 7). Answering <c>'()</c> makes that correct by
    /// construction rather than by luck — and unlike the N/A binding it briefly was, it
    /// does not abort the init layer.
    /// </para>
    /// </remarks>
    private static void InstallGuileLiveness(Interpreter interpreter)
        => interpreter.DefinePrimitive("ly:parsed-undead-list!", 0, 0, a => Nil.Instance);

    /// <summary>
    /// Category <c>instrumentation</c> (ratified during the long-tail closure): machinery that
    /// profiles LilyPond's OWN execution rather than doing anything to the music.
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    /// <remarks>
    /// These are N/A rather than owed because implementing them means instrumenting hot
    /// paths — the grob property lookup on every read, and a trace event around every
    /// phase — to feed counters nothing in the port reads. The parity yardstick is the
    /// engraved bytes, and these produce none.
    /// </remarks>
    private static void InstallInstrumentation(Interpreter interpreter)
    {
        NotApplicable(interpreter, "ly:property-lookup-stats", 1, 1, "instrumentation",
            "reports grob property-lookup counts for -dprofile-property-accesses; the "
            + "port does not instrument the property-lookup path");

        NotApplicable(interpreter, "ly:time-tracer-set-file", 1, 1, "instrumentation",
            "names the file the Chrome-trace timing log is written to");

        NotApplicable(interpreter, "ly:time-tracer-restart", 1, 1, "instrumentation",
            "restarts the Chrome-trace timing log");

        NotApplicable(interpreter, "ly:time-tracer-stop", 0, 0, "instrumentation",
            "stops the Chrome-trace timing log");

        NotApplicable(
            interpreter, "ly:time-tracer-include-and-remove-file", 1, 1, "instrumentation",
            "splices another trace file into this run's Chrome-trace log and deletes it");
    }

    /// <summary>
    /// Registers one N/A binding: it exists, it is callable, and calling it raises with
    /// its reason and its category.
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    /// <param name="name">The Scheme-visible name.</param>
    /// <param name="minimum">The smallest accepted argument count.</param>
    /// <param name="maximum">The largest accepted argument count.</param>
    /// <param name="category">The ratified D25 category.</param>
    /// <param name="reason">Why the port has nothing for this entry point to do.</param>
    private static void NotApplicable(
        Interpreter interpreter,
        string name,
        int minimum,
        int maximum,
        string category,
        string reason)
        => interpreter.DefinePrimitive(name, minimum, maximum, a =>
            throw new SchemeThrow(
                Symbol.Intern("lilypond-error"),
                Pair.List(
                    new MutableString(name),
                    new MutableString(
                        "not applicable: " + reason + " -- N/A per D25, category "
                        + category),
                    Nil.Instance,
                    false)));
}
