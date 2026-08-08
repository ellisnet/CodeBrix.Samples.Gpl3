#!/usr/bin/env python3
# Copyright (c) 2026 Jeremy Ellis and contributors
#
# CodeBrix.LilyPort is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
"""The regression ratchet: progress against the oracle may only go forward.

WHY A RATCHET AND NOT A PASS LIST

The comparator grades every file on a ladder (MISSING at the bottom, MATCH at the
top) rather than pass/fail, because a port crosses that ladder slowly: right notes
before right positions, right positions before exact agreement. A plain "these
files pass" list throws that away and stays empty for months.

So the committed manifest records, per file, the BEST VERDICT EVER ACHIEVED. A run
must meet or beat every recorded verdict. Sliding backwards on any file -- even
while another file improves, even while the total count of MATCHes rises -- is a
regression and fails the gate.

That asymmetry is the whole design. Totals can hide a swap (one file gained MATCH,
another lost it); per-file floors cannot.

USAGE

    # after a comparison run that wrote its per-file verdicts
    ./compare-output.py REF_DIR CAND_DIR --tsv /tmp/run.tsv
    ./ratchet.py check /tmp/run.tsv           # gate: fails on any backslide
    ./ratchet.py update /tmp/run.tsv          # ratchet forward, then commit

    ./ratchet.py self-test                    # prove the gate logic itself

    # a row that must come DOWN -- a decision, never a fix (see below)
    ./ratchet.py rebaseline /tmp/run.tsv --reason "why" [--only a.svg,b.svg]

LOWERING A ROW

A floor only means something if lowering it is harder than raising it. `update`
therefore cannot lower anything: it takes the better of manifest and run, row by
row. When a row genuinely has to come down -- the port deliberately gave up a
page, or the floor was recorded from evidence that turned out not to be evidence
-- `rebaseline` is the only way, it demands a written reason, and it appends
every change to pass-manifest-decisions.tsv so the manifest can always be read
back against why it says what it says.

That file exists because of 2026-08-07: BatchDriver never cleaned its output
directory, so files that had STOPPED producing pages kept their stale ones,
the comparator graded those, and 97 rows stood on evidence from as far back as
two days earlier. Nothing in the repository recorded that a floor could be
unearned, so nothing questioned it for three sessions.

EXIT CODES

    0  no file regressed (check), or the manifest was advanced (update/rebaseline)
    1  at least one file regressed
    2  usage or I/O error
"""

import argparse
import datetime
import os
import sys

MANIFEST = os.path.join(os.path.dirname(os.path.abspath(__file__)), "pass-manifest.tsv")
DECISIONS = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                         "pass-manifest-decisions.tsv")

# Worst to best. The ratchet compares by INDEX in this list, so adding a rung means
# deciding where it sits on the ladder -- which is the right thing to be forced to
# think about.
LADDER = [
    "MISSING",
    "REFERENCE-BAD",
    "UNPARSEABLE",
    "GLYPHS-DIFFER",
    "PLACEMENT-COUNT",
    "PLACEMENT-ORDER",
    "PLACEMENT-DIFFERS",
    "MATCH",
]

RANK = {verdict: index for index, verdict in enumerate(LADDER)}


def rank_of(verdict):
    """Return the ladder rank of a verdict, rejecting anything unknown."""
    if verdict not in RANK:
        raise KeyError(
            "unknown verdict %r -- add it to LADDER at the right rung, do not guess"
            % verdict)
    return RANK[verdict]


def read_verdicts(path):
    """Read a name -> verdict mapping from a TSV, ignoring comments and blanks."""
    verdicts = {}
    with open(path) as handle:
        for line in handle:
            line = line.rstrip("\n")
            if not line or line.startswith("#"):
                continue
            parts = line.split("\t")
            if len(parts) < 2:
                raise ValueError("malformed row (need name and verdict): %r" % line)
            name, verdict = parts[0], parts[1]
            if name in verdicts:
                raise ValueError("duplicate row for %s" % name)
            verdicts[name] = verdict
    return verdicts


def write_manifest(path, verdicts):
    """Write the committed manifest, sorted so diffs stay readable."""
    with open(path, "w") as handle:
        handle.write(
            "# CodeBrix.LilyPort regression ratchet -- the BEST VERDICT EVER ACHIEVED\n"
            "# per reference page. A run must meet or beat every row here; see\n"
            "# ratchet.py for why this is a per-file floor and not a pass count.\n"
            "#\n"
            "# Advanced only by `ratchet.py update`, never by hand. A row that needs to\n"
            "# come DOWN is a decision, not a fix: record why in the commit message.\n"
            "#\n"
            "# name\tbest-verdict\n")
        for name in sorted(verdicts):
            handle.write("%s\t%s\n" % (name, verdicts[name]))


def compare(manifest, run):
    """Return (regressions, improvements, unseen) between a manifest and a run."""
    regressions = []
    improvements = []
    unseen = []

    for name, recorded in sorted(manifest.items()):
        if name not in run:
            # A file the manifest knows and the run never produced a verdict for.
            # Treated as a regression: silently dropping a file from the sweep is
            # exactly how a suite quietly stops covering something.
            unseen.append(name)
            continue
        current = rank_of(run[name])
        floor = rank_of(recorded)
        if current < floor:
            regressions.append((name, recorded, run[name]))
        elif current > floor:
            improvements.append((name, recorded, run[name]))

    return regressions, improvements, unseen


def rows_to_lower(manifest, run, only):
    """Return the (name, from, to) triples a rebaseline would write.

    Only rows that CURRENTLY regress are eligible: rebaseline exists to record a
    floor that cannot be met, never to lower a row that is being met. `only`
    restricts it further to a named set, so one decision's rows can be recorded
    with one reason and another's with another.
    """
    lowered = []
    for name, recorded in sorted(manifest.items()):
        if only is not None and name not in only:
            continue
        if name not in run:
            continue
        if rank_of(run[name]) < rank_of(recorded):
            lowered.append((name, recorded, run[name]))
    return lowered


def append_decisions(path, lowered, reason, today):
    """Append the audit trail. The manifest says WHAT; this says WHY, forever."""
    fresh = not os.path.exists(path)
    with open(path, "a") as handle:
        if fresh:
            handle.write(
                "# CodeBrix.LilyPort ratchet -- every time a floor came DOWN, and why.\n"
                "# Append-only, written by `ratchet.py rebaseline`. The manifest records\n"
                "# what the floor IS; this records what happened to it, so a row that\n"
                "# reads lower than a plan document claims can always be accounted for.\n"
                "#\n"
                "# date\tname\tfrom\tto\treason\n")
        for name, was, now in lowered:
            handle.write("%s\t%s\t%s\t%s\t%s\n"
                         % (today, name, was, now, reason.replace("\t", " ")))


def command_check(arguments):
    manifest = read_verdicts(arguments.manifest) if os.path.exists(arguments.manifest) else {}
    run = read_verdicts(arguments.run)
    regressions, improvements, unseen = compare(manifest, run)

    print("manifest   : %s (%d rows)" % (arguments.manifest, len(manifest)))
    print("run        : %s (%d rows)" % (arguments.run, len(run)))
    print("regressions: %d" % len(regressions))
    print("improved   : %d" % len(improvements))
    print("not in run : %d" % len(unseen))

    for name, was, now in regressions:
        print("  REGRESSED  %-46s %s -> %s" % (name, was, now))
    for name in unseen:
        print("  ABSENT     %-46s (manifest expects it)" % name)
    for name, was, now in improvements[:20]:
        print("  improved   %-46s %s -> %s" % (name, was, now))

    if regressions or unseen:
        print("\n*** RATCHET FAILED: %d regressed, %d absent ***"
              % (len(regressions), len(unseen)))
        return 1

    print("\n*** ratchet holds (%d improvements available to record) ***"
          % len(improvements))
    return 0


def command_update(arguments):
    manifest = read_verdicts(arguments.manifest) if os.path.exists(arguments.manifest) else {}
    run = read_verdicts(arguments.run)
    regressions, improvements, unseen = compare(manifest, run)

    if (regressions or unseen) and not arguments.force:
        print("refusing to update: %d regressed, %d absent. Fix them, or pass --force"
              " if the drop is a deliberate, explained decision."
              % (len(regressions), len(unseen)), file=sys.stderr)
        for name, was, now in regressions:
            print("  REGRESSED  %-46s %s -> %s" % (name, was, now), file=sys.stderr)
        return 1

    advanced = dict(manifest)
    added = 0
    for name, verdict in run.items():
        if name not in advanced:
            advanced[name] = verdict
            added += 1
        elif rank_of(verdict) > rank_of(advanced[name]):
            advanced[name] = verdict

    write_manifest(arguments.manifest, advanced)
    print("manifest advanced: %d rows (%d new, %d improved)"
          % (len(advanced), added, len(improvements)))
    return 0


def command_rebaseline(arguments):
    manifest = read_verdicts(arguments.manifest) if os.path.exists(arguments.manifest) else {}
    run = read_verdicts(arguments.run)

    reason = arguments.reason.strip()
    if not reason:
        print("rebaseline: --reason must say WHY the floor is coming down",
              file=sys.stderr)
        return 2

    only = None
    if arguments.only:
        only = set(arguments.only.split(","))
        unknown = sorted(only - set(manifest))
        if unknown:
            print("rebaseline: not in the manifest: %s" % ", ".join(unknown), file=sys.stderr)
            return 2

    lowered = rows_to_lower(manifest, run, only)
    if not lowered:
        print("rebaseline: nothing to lower — no selected row regresses. Manifest untouched.")
        return 0

    today = datetime.date.today().isoformat()
    for name, _was, now in lowered:
        manifest[name] = now

    write_manifest(arguments.manifest, manifest)
    append_decisions(arguments.decisions, lowered, reason, today)

    print("rebaselined %d row(s); reason recorded in %s"
          % (len(lowered), arguments.decisions))
    for name, was, now in lowered[:20]:
        print("  LOWERED    %-46s %s -> %s" % (name, was, now))
    if len(lowered) > 20:
        print("  ... and %d more (all of them are in the decisions file)"
              % (len(lowered) - 20))
    return 0


def command_self_test(_arguments):
    """Prove the gate logic. A ratchet that cannot fail is not a gate."""
    failures = []

    def check(label, condition):
        if not condition:
            failures.append(label)

    manifest = {"a.svg": "MATCH", "b.svg": "GLYPHS-DIFFER", "c.svg": "MISSING"}

    # Holding steady is not a regression.
    regressions, improvements, unseen = compare(manifest, dict(manifest))
    check("steady state reports no regression", not regressions)
    check("steady state reports no improvement", not improvements)

    # Going up the ladder is an improvement, never a regression.
    up = dict(manifest, **{"b.svg": "PLACEMENT-DIFFERS"})
    regressions, improvements, unseen = compare(manifest, up)
    check("improvement is not a regression", not regressions)
    check("improvement is reported", len(improvements) == 1)

    # Going down the ladder is a regression.
    down = dict(manifest, **{"a.svg": "PLACEMENT-DIFFERS"})
    regressions, improvements, unseen = compare(manifest, down)
    check("backslide is caught", len(regressions) == 1)

    # A net-positive swap still fails: one file up, one file down.
    swap = dict(manifest, **{"a.svg": "GLYPHS-DIFFER", "b.svg": "MATCH", "c.svg": "MATCH"})
    regressions, improvements, unseen = compare(manifest, swap)
    check("a net-positive swap still fails", len(regressions) == 1)

    # A file vanishing from the run is not silently tolerated.
    missing = {"a.svg": "MATCH", "b.svg": "GLYPHS-DIFFER"}
    regressions, improvements, unseen = compare(manifest, missing)
    check("a dropped file is caught", unseen == ["c.svg"])

    # An unknown verdict is rejected loudly rather than ranked as zero.
    try:
        compare({"a.svg": "MATCH"}, {"a.svg": "SORT-OF-FINE"})
        check("unknown verdict raises", False)
    except KeyError:
        pass

    # The ladder is strictly ordered and complete.
    check("ladder has no duplicates", len(set(LADDER)) == len(LADDER))
    check("MATCH is the top rung", LADDER[-1] == "MATCH")
    check("MISSING is the bottom rung", LADDER[0] == "MISSING")

    # Lowering a floor is a separate, deliberate act, so its selection logic is
    # proven here too -- an unexercised decision path is not one to trust with 97
    # rows.
    down = dict(manifest, **{"a.svg": "GLYPHS-DIFFER", "b.svg": "MATCH"})
    lowered = rows_to_lower(manifest, down, None)
    check("rebaseline lowers only the regressed row",
          lowered == [("a.svg", "MATCH", "GLYPHS-DIFFER")])

    check("rebaseline leaves an improved row alone",
          all(name != "b.svg" for name, _, _ in lowered))

    check("rebaseline honours --only",
          rows_to_lower(manifest, down, {"b.svg"}) == [])

    check("rebaseline lowers nothing when the run meets the floor",
          rows_to_lower(manifest, dict(manifest), None) == [])

    # A file absent from the run is a `check` failure, never a silent rebaseline:
    # the row must not come down on the strength of evidence nobody produced.
    check("rebaseline skips a file the run has no verdict for",
          rows_to_lower(manifest, {"a.svg": "MATCH", "b.svg": "GLYPHS-DIFFER"}, None) == [])

    for failure in failures:
        print("FAIL: %s" % failure)
    if failures:
        print("\n*** %d self-test failure(s) ***" % len(failures))
        return 1

    print("ratchet self-test: all checks pass")
    return 0


def main():
    parser = argparse.ArgumentParser(description=__doc__.split("\n")[0])
    parser.add_argument("--manifest", default=MANIFEST,
                        help="the committed per-file floor (default: %(default)s)")
    parser.add_argument("--decisions", default=DECISIONS,
                        help="append-only log of floors that came DOWN (default: %(default)s)")
    subparsers = parser.add_subparsers(dest="command", required=True)

    check_parser = subparsers.add_parser("check", help="fail if any file regressed")
    check_parser.add_argument("run", help="per-file verdicts from compare-output.py --tsv")
    check_parser.set_defaults(handler=command_check)

    update_parser = subparsers.add_parser("update", help="ratchet the manifest forward")
    update_parser.add_argument("run", help="per-file verdicts from compare-output.py --tsv")
    update_parser.add_argument("--force", action="store_true",
                               help="advance even though something regressed (explain it)")
    update_parser.set_defaults(handler=command_update)

    rebaseline_parser = subparsers.add_parser(
        "rebaseline", help="lower floors that cannot be met, with a recorded reason")
    rebaseline_parser.add_argument("run", help="per-file verdicts from compare-output.py --tsv")
    rebaseline_parser.add_argument("--reason", required=True,
                                   help="why these floors are coming down; goes in the "
                                        "decisions file beside every row it lowers")
    rebaseline_parser.add_argument("--only",
                                   help="comma-separated page names to lower, so rows that "
                                        "come down for DIFFERENT reasons are recorded "
                                        "separately (default: every regressed row)")
    rebaseline_parser.set_defaults(handler=command_rebaseline)

    self_test_parser = subparsers.add_parser("self-test", help="prove the gate logic")
    self_test_parser.set_defaults(handler=command_self_test)

    arguments = parser.parse_args()
    try:
        return arguments.handler(arguments)
    except (OSError, ValueError, KeyError) as error:
        print("ratchet: %s" % error, file=sys.stderr)
        return 2


if __name__ == "__main__":
    sys.exit(main())
