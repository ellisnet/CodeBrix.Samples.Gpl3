#!/usr/bin/env python3
"""compare-diagnostics.py -- grade what CodeBrix.LilyPort SAYS against what the oracle says.

This file is part of CodeBrix.LilyPort.
Copyright (c) 2026 Jeremy Ellis and contributors

CodeBrix.LilyPort is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

------------------------------------------------------------------------------
WHY THIS EXISTS -- THE UNGRADED GAP (plan trap 1a)

Until this script, NOTHING in this project read a diagnostic. compare-output.py
grades SVG geometry. compare-docs grades the 19 generated documentation files.
ratchet.py grades compare-output.py's verdicts. compare-midi.py grades .midi
bytes. A message printed to stderr was scored by none of them -- "the docs run
covers printing" covers printing INTO THOSE 19 FILES and nothing else.

That gap had consequences, which is why this exists rather than being a nicety.
The port's type-check failure message had diverged from upstream's in BOTH
wording and severity and nobody noticed for the life of the project; it was
found by accident while chasing an unrelated defect. Four defect classes
(PARITY 3's D1 and D2) were found only because someone read the sweep's stderr
by hand. A defect whose only symptom is a diagnostic is invisible to every green
light this project otherwise has.

------------------------------------------------------------------------------
THE TWO SIDES ARE ASYMMETRIC, DELIBERATELY

REFERENCE is a DIRECTORY of per-file logs, written by
`MODE=diagnostics ./generate-reference.sh`. The oracle is one process per file,
so per-file logs are its natural artifact -- and the parity corpus's own logs
cannot be used, because those are made with --silent and contain no warning line
at all (plan trap 5).

CANDIDATE is the port's MERGED sweep log -- one file, produced by running
BatchDriver with `2>&1`. The port is ONE process for the whole suite, so a
merged stream is ITS natural artifact. Attribution works because the driver
prints a file's RESULT line AFTER running it: every diagnostic since the
previous result line belongs to the file named on the next one. That is why the
streams must be MERGED; with `> log 2> err` the interleaving is lost and this
script cannot tell which file said what.

    dotnet run --project tools/regression-harness/BatchDriver -c Release -- \\
        tests/regression tools/regression-harness/candidate/svg > /tmp/sweep.log 2>&1
    python3 compare-diagnostics.py reference/diagnostics /tmp/sweep.log

------------------------------------------------------------------------------
WHAT IS COMPARED, AND EVERY NORMALISATION APPLIED

A diagnostic is a line matching, on either side:

    [<file>:<line>:<col>: ]<severity>: <message>

with severity one of warning / error / fatal error / programming error.
EVERYTHING else in both streams is dropped, and each drop is a decision:

  1. PROGRESS CHATTER IS DROPPED -- "Processing", "Parsing...", "Interpreting
     music...", "Drawing systems...", "Success: ...", and the oracle's
     "Changing working directory to: ...". These are exactly the lines --silent
     used to suppress. They describe that a run happened, not what it found.

  2. "continuing, cross fingers" IS DROPPED. Upstream prints it after a
     programming_error. It is that message's punctuation, not a second finding,
     and counting it would double every programming error.

  3. THE SOURCE ECHO IS DROPPED. A located diagnostic is followed by a blank
     line and the offending source text. That is rendered FROM the location
     already being compared, so grading it would grade the same fact twice.

  4. LOCATION IS NOT PART OF THE KEY. The oracle names its input by absolute
     path and the port by bare filename -- a difference about how each was
     INVOKED, not about behaviour. Line and column are recorded and shown, but
     two diagnostics match on (severity, message). Grading position as well
     would conflate two independent questions, and the first one is not yet
     answered.

  5. ORDER IS NOT GRADED; the comparison is of MULTISETS. The two engines
     interleave diagnostics from different phases, and one ordering difference
     would otherwise mask every real difference behind it. COUNTS are compared
     exactly -- a message emitted three times where the oracle emits it twice is
     a difference, and a real one.

  6. AN ABSOLUTE PATH INSIDE A MESSAGE IS REDUCED TO ITS BASENAME, and a load
     path / cwd pair is replaced wholesale. This is normalisation 4 applied one
     level in: the oracle reads its fonts out of an installed tree and the port
     reads them out of its own assembly, and each file runs in a per-process
     scratch cwd (PARITY 3) that the oracle's own temporary directory can never
     equal. Two messages that differ ONLY in where a file lives differ about
     invocation, not behaviour.

     What survives is the part that carries the finding: WHICH font lacked the
     glyph, and WHICH file could not be found. `cannot find file 'x.eps'` still
     grades its file name exactly, which is what makes clip-systems' missing
     base name (D41) a real difference rather than a normalised-away one.

Normalisations 4, 5 and 6 are the places this script is deliberately looser
than compare-output.py. All are recorded as limitations to tighten once the
verdict it produces is clean enough for the noise to be legible.

------------------------------------------------------------------------------
VERDICTS, deliberately close to the vocabulary the other comparators use

  MATCH          identical multisets. The port says exactly what the oracle says.
  TEXT-DIFFERS   same severities, same total count, but at least one message
                 SPELLED differently. Kept separate on purpose: plan D8 will
                 make the port's wording match upstream's, and until it does,
                 wording differences would swamp the behavioural ones this
                 exists to find.
                 ⚠ NOT only a wording bucket, and the very first run proved it.
                 `warning: compressing over-full page by 4.3 staff-spaces` vs
                 the oracle's own figure is the SAME sentence carrying a
                 DIFFERENT NUMBER -- a real layout difference wearing a wording
                 verdict. Read every TEXT-DIFFERS before assuming it is D8; a
                 later version should split "same sentence, different value"
                 into its own verdict.
  EXTRA          the port says something the oracle does not. It is reaching a
                 state upstream never reaches (PARITY 3's D1 and D2 are this).
  MISSING        the oracle says something the port does not. The port is
                 failing to notice something upstream notices.
  BOTH           extra AND missing.
  UNGRADED       the file is on one side only -- nothing to compare against.
"""

import argparse
import collections
import os
import re
import sys

SEVERITIES = ("fatal error", "programming error", "warning", "error")

# [<path>:<line>:<col>: ]<severity>: <message>
DIAGNOSTIC = re.compile(
    r"^(?:(?P<loc>\S*?):(?P<line>\d+):(?P<col>\d+):\s+)?"
    r"(?P<severity>fatal error|programming error|warning|error):\s+(?P<message>.*)$")

# A file's result line in the port's merged sweep log.
RESULT = re.compile(r"^(?P<name>[A-Za-z0-9_.+-]+)\t(?P<kind>[A-Z-]+)\t")

# Only these close a file's window. SIDE-FILE and MIDI are extra rows ABOUT the
# same file and must not be treated as the next file starting.
TERMINAL_KINDS = frozenset(("SVG", "NOOUT", "ERROR"))

DROP_EXACT = frozenset((
    "continuing, cross fingers",
))

DROP_PREFIX = (
    "Changing working directory to:",
    "Processing ",
    "Parsing...",
    "Interpreting music...",
    "Preprocessing graphical objects...",
    "Finding the ideal number of pages...",
    "Fitting music on ",
    "Drawing systems...",
    "Success: ",
    "Layout output to ",
    "Converting to ",
)

Diagnostic = collections.namedtuple("Diagnostic", "severity message location")


def parse_diagnostics(text):
    """Every diagnostic in one stream, in order, as (severity, message, location)."""
    found = []
    for raw in text.splitlines():
        line = raw.rstrip("\r")
        stripped = line.strip()
        if not stripped or stripped in DROP_EXACT:
            continue
        if any(stripped.startswith(p) for p in DROP_PREFIX):
            continue
        match = DIAGNOSTIC.match(stripped)
        if not match:
            # Not a diagnostic: source echo, driver bookkeeping, a `.ly` file's
            # own display output. Dropped rather than guessed at.
            continue
        location = ""
        if match.group("loc"):
            location = "%s:%s:%s" % (
                os.path.basename(match.group("loc")),
                match.group("line"),
                match.group("col"))
        found.append(Diagnostic(
            match.group("severity"), match.group("message").strip(), location))
    return found


def read_reference(directory):
    """Per-file oracle logs -> {name: [Diagnostic]}."""
    out = {}
    for entry in sorted(os.listdir(directory)):
        if not entry.endswith(".log"):
            continue
        path = os.path.join(directory, entry)
        with open(path, "r", errors="replace") as handle:
            out[entry[:-len(".log")]] = parse_diagnostics(handle.read())
    return out


def read_candidate(path):
    """The port's MERGED sweep log -> {name: [Diagnostic]}.

    The driver prints a file's result AFTER running it, so diagnostics accumulate
    until a terminal result line names their owner.
    """
    out = collections.defaultdict(list)
    pending = []
    with open(path, "r", errors="replace") as handle:
        for raw in handle:
            match = RESULT.match(raw)
            if match:
                name = match.group("name")
                if pending:
                    out[name].extend(pending)
                if match.group("kind") in TERMINAL_KINDS:
                    # Register the file even when it said NOTHING. A file the
                    # sweep ran and that emitted no diagnostic is a file with an
                    # EMPTY diagnostic set, which is a gradeable fact and usually
                    # a MATCH -- not an absence. Leaving it unregistered made 85%
                    # of the corpus read as UNGRADED on this comparator's first
                    # run, hiding the 12 files that genuinely agreed.
                    out.setdefault(name, [])
                    pending = []
                continue
            pending.extend(parse_diagnostics(raw))
    return dict(out)


# Normalisation 6. Both sides are reduced, so neither engine's spelling wins.
#
#   in font `/some/where/C059-Roman.otf'  ->  in font `C059-Roman.otf'
#   (load path: '...', cwd: '...')        ->  (load path: <PATH>, cwd: <PATH>)
#
# The quoted FILE NAME in "cannot find file '...'" is deliberately NOT touched:
# it is the finding, and D41's missing base name has to stay visible.
FONT_PATH = re.compile(r"(in font [`'\"])([^`'\"]*/)([^`'\"]+)")
LOAD_PATH = re.compile(r"\(load path: '[^']*', cwd: '[^']*'\)")


def normalize_message(message):
    """Strips the parts of a message that describe invocation, not behaviour."""
    message = FONT_PATH.sub(r"\1\3", message)
    message = LOAD_PATH.sub("(load path: <PATH>, cwd: <PATH>)", message)
    return message


def key_of(diagnostic):
    """What two diagnostics must agree on to be the same one (normalisations 4, 6)."""
    return (diagnostic.severity, normalize_message(diagnostic.message))


def grade(reference, candidate):
    """One file's verdict and a one-line detail."""
    ref = collections.Counter(key_of(d) for d in reference)
    cand = collections.Counter(key_of(d) for d in candidate)

    if ref == cand:
        return "MATCH", "%d diagnostic(s)" % sum(ref.values())

    missing = ref - cand
    extra = cand - ref

    ref_sev = collections.Counter(s for s, _ in ref.elements())
    cand_sev = collections.Counter(s for s, _ in cand.elements())
    if ref_sev == cand_sev and sum(ref.values()) == sum(cand.values()):
        sample = next(iter(sorted(extra.elements())), None)
        return "TEXT-DIFFERS", "%d reworded; e.g. %s: %s" % (
            sum(extra.values()),
            sample[0] if sample else "?",
            (sample[1][:60] if sample else "?"))

    if extra and not missing:
        sample = sorted(extra.elements())[0]
        return "EXTRA", "+%d; e.g. %s: %s" % (
            sum(extra.values()), sample[0], sample[1][:60])
    if missing and not extra:
        sample = sorted(missing.elements())[0]
        return "MISSING", "-%d; e.g. %s: %s" % (
            sum(missing.values()), sample[0], sample[1][:60])

    return "BOTH", "+%d / -%d" % (sum(extra.values()), sum(missing.values()))


def normalisation_selftest():
    """Normalisation 6, each case paired with a CONTROL that must NOT collapse.

    A symmetric normalisation is invisible to the round-trip below -- both sides
    get it -- so it needs its own check, and each case needs a control, or a
    normaliser that flattened everything to one string would pass.
    """
    ora = ("/home/jeremy/ClaudeHome/oracle/lilypond-2.27.2/share/lilypond/"
           "2.27.2/fonts/otf/C059-Roman.otf")
    cases = [
        # (name, a, b, must_be_equal)
        ("font path -> basename",
         "no glyph for character 'x' (U+0078 LATIN SMALL LETTER X) in font `%s'" % ora,
         "no glyph for character 'x' (U+0078 LATIN SMALL LETTER X) in font `C059-Roman.otf'",
         True),
        ("CONTROL: a different FONT still differs",
         "no glyph for character 'x' (U+0078 LATIN SMALL LETTER X) in font `%s'" % ora,
         "no glyph for character 'x' (U+0078 LATIN SMALL LETTER X) in font `NimbusSans-Regular.otf'",
         False),
        ("CONTROL: a different CHARACTER still differs",
         "no glyph for character 'x' (U+0078 LATIN SMALL LETTER X) in font `%s'" % ora,
         "no glyph for character 'y' (U+0079 LATIN SMALL LETTER Y) in font `%s'" % ora,
         False),
        ("load path and cwd -> placeholder",
         "cannot find file 'a.eps' (load path: '/one:/two', cwd: '/tmp/tmp.AAA')",
         "cannot find file 'a.eps' (load path: '', cwd: '/tmp/scratch-1')",
         True),
        ("CONTROL: the missing FILE NAME still differs (this is D41)",
         "cannot find file 'clip-systems-clip-input-from-2.0.1-to-4.0.1-clip.eps' "
         "(load path: '/one', cwd: '/tmp/a')",
         "cannot find file '-clip-input-from-2.0.1-to-4.0.1-clip.eps' "
         "(load path: '/one', cwd: '/tmp/a')",
         False),
        ("CONTROL: an ordinary message is untouched",
         "unbound variable: foo",
         "unbound variable: foo",
         True),
    ]

    bad = []
    for name, a, b, want_equal in cases:
        same = normalize_message(a) == normalize_message(b)
        if same != want_equal:
            bad.append((name, normalize_message(a), normalize_message(b)))

    if bad:
        print("*** NORMALISATION SELF-TEST FAILED: %d case(s) ***" % len(bad))
        for name, a, b in bad:
            print("  %-46s" % name)
            print("      %s" % a)
            print("      %s" % b)
    else:
        print("normalisation self-test: %d case(s) pass (%d of them controls)"
              % (len(cases), sum(1 for c in cases if c[0].startswith("CONTROL"))))
    return bad


def selftest(directory):
    """Round-trip the reference through the CANDIDATE path and demand 100% MATCH.

    compare-output.py's standing check is `reference vs reference -> all MATCH`.
    That shape is unavailable here because the two sides take different
    artifacts, and the asymmetry is exactly where this script is most likely to
    be wrong: the candidate side has to parse a merged stream AND attribute each
    line to a file, neither of which the reference side does.

    So the reference is re-emitted in the port's own merged-log format -- every
    file's diagnostics, then its result line -- and fed back in as the candidate.
    Anything less than 100% MATCH means the parser and the attributor disagree
    about the same bytes, and every verdict this script produces is worthless
    until that is fixed.
    """
    failures = normalisation_selftest()

    reference = read_reference(directory)
    if not reference:
        print("no .log files in %s" % directory, file=sys.stderr)
        return 2

    synthetic = os.path.join(
        os.environ.get("TMPDIR", "/tmp"), "compare-diagnostics-selftest.log")
    with open(synthetic, "w") as handle:
        for name in sorted(reference):
            for d in reference[name]:
                prefix = ("%s: " % d.location) if d.location else ""
                handle.write("%s%s: %s\n" % (prefix, d.severity, d.message))
                # The follow-up upstream prints after a programming error, so the
                # self-test also proves that line is dropped rather than counted.
                if d.severity == "programming error":
                    handle.write("continuing, cross fingers\n")
            handle.write("%s\tSVG\t1 system(s), 0 parse error(s)\n" % name)

    candidate = read_candidate(synthetic)
    bad = []
    for name in sorted(set(reference) | set(candidate)):
        if name not in reference or name not in candidate:
            # A file with NO diagnostics at all legitimately has no candidate
            # entry, because nothing accumulated for it. That is a match, not a
            # gap -- but only when the reference side is empty too.
            if not reference.get(name) and not candidate.get(name):
                continue
            bad.append((name, "UNGRADED", "present on one side only"))
            continue
        verdict, detail = grade(reference[name], candidate[name])
        if verdict != "MATCH":
            bad.append((name, verdict, detail))

    graded = sum(1 for n in reference if reference[n])
    print("self-test: %d file(s), %d carrying diagnostics" % (len(reference), graded))
    if bad:
        print("*** SELF-TEST FAILED: %d file(s) did not round-trip ***" % len(bad))
        for name, verdict, detail in bad[:20]:
            print("  %-46s %-14s %s" % (name, verdict, detail))
        return 3
    if failures:
        return 3
    print("*** self-test passed: the merged-log path reproduces the reference exactly ***")
    return 0


def main():
    parser = argparse.ArgumentParser(
        description="Grade the port's diagnostics against the oracle's.")
    parser.add_argument(
        "reference", help="directory of oracle per-file logs (reference/diagnostics)")
    parser.add_argument(
        "candidate", nargs="?",
        help="the port's MERGED sweep log (BatchDriver run with 2>&1)")
    parser.add_argument(
        "--selftest", action="store_true",
        help="round-trip the reference through the candidate path; expect 100%% MATCH")
    parser.add_argument("--tsv", help="write per-file verdicts here")
    parser.add_argument(
        "--show", type=int, default=20,
        help="how many differing files to list (default 20; 0 for none)")
    parser.add_argument(
        "--verdict", help="list only files with this verdict, then exit")
    arguments = parser.parse_args()

    if not os.path.isdir(arguments.reference):
        print("no such reference directory: %s" % arguments.reference, file=sys.stderr)
        return 2
    if arguments.selftest:
        return selftest(arguments.reference)
    if not arguments.candidate:
        parser.error("candidate is required unless --selftest is given")
    if not os.path.isfile(arguments.candidate):
        print("no such candidate log: %s" % arguments.candidate, file=sys.stderr)
        return 2

    reference = read_reference(arguments.reference)
    candidate = read_candidate(arguments.candidate)
    if not reference:
        print("no .log files in %s" % arguments.reference, file=sys.stderr)
        return 2

    rows = []
    for name in sorted(set(reference) | set(candidate)):
        if name not in reference or name not in candidate:
            side = "reference" if name in reference else "candidate"
            rows.append((name, "UNGRADED", "present on the %s side only" % side))
            continue
        verdict, detail = grade(reference[name], candidate[name])
        rows.append((name, verdict, detail))

    counts = collections.Counter(v for _, v, _ in rows)

    if arguments.verdict:
        for name, verdict, detail in rows:
            if verdict == arguments.verdict:
                print("%-52s %s" % (name, detail))
        return 0

    print("reference : %s (%d file logs)" % (arguments.reference, len(reference)))
    print("candidate : %s (%d files attributed)" % (arguments.candidate, len(candidate)))
    print("%-16s %6s %6s" % ("VERDICT", "COUNT", "SHARE"))
    order = ("MATCH", "TEXT-DIFFERS", "EXTRA", "MISSING", "BOTH", "UNGRADED")
    for verdict in order:
        if not counts[verdict]:
            continue
        print("%-16s %6d %5.1f%%" % (
            verdict, counts[verdict], 100.0 * counts[verdict] / len(rows)))
    print("%-16s %6d %5.1f%%" % ("TOTAL", len(rows), 100.0))

    # The totals that say how much is actually being said on each side. A file
    # count alone hides a file that emits one message where the oracle emits
    # forty.
    ref_total = sum(len(v) for v in reference.values())
    cand_total = sum(len(v) for v in candidate.values())
    print("\ndiagnostic lines: %d reference / %d candidate" % (ref_total, cand_total))

    print("\n*** %d of %d files match (%.2f%%) ***"
          % (counts["MATCH"], len(rows), 100.0 * counts["MATCH"] / len(rows)))

    if arguments.show:
        shown = 0
        for name, verdict, detail in rows:
            if verdict == "MATCH":
                continue
            if shown == 0:
                print("\nDIFFERENCES -- first %d:" % arguments.show)
            if shown >= arguments.show:
                break
            print("  %-46s %-14s %s" % (name, verdict, detail))
            shown += 1

    if arguments.tsv:
        with open(arguments.tsv, "w") as handle:
            handle.write("# CodeBrix.LilyPort diagnostic comparison\n")
            handle.write("# reference: %s\n" % arguments.reference)
            handle.write("# candidate: %s\n" % arguments.candidate)
            handle.write("# Columns: file <TAB> verdict <TAB> detail\n")
            for name, verdict, detail in rows:
                handle.write("%s\t%s\t%s\n" % (name, verdict, detail))

    return 0 if counts["MATCH"] == len(rows) else 1


if __name__ == "__main__":
    sys.exit(main())
