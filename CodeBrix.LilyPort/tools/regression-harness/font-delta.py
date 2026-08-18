#!/usr/bin/env python3
# Copyright (c) 2026 Jeremy Ellis and contributors
#
# CodeBrix.LilyPort is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
"""What the port's OWN Emmentaler build costs, measured page by page.

THE MEASUREMENT
    Two renders of the same corpus by the same engine from the same input, with
    ONLY the font files different: the port's own build against the one LilyPond
    ships.  Everything that differs between them is the font build and nothing
    else, which is what makes this a measurement rather than an estimate.

WHY IT IS RECORDED RATHER THAN JUST CHECKED
    Ruling R19: the port builds its own Emmentaler fonts from the Metafont
    sources, exactly as LilyPond builds its own from the same sources, and two
    FontForge runs do not produce identical outlines.  A skyline reads outlines,
    so a handful of rows land in a slightly different place, and that is accepted
    -- up to a ceiling.  But an ACCEPTED difference that CHANGES is a different
    fact from an accepted difference that holds, and only a committed record can
    tell them apart.  Every number here is exact and reproducible: the font build
    is byte-reproducible and the engine is deterministic, so there is no noise to
    tolerate and ANY change is a signal.

    A page that is byte-identical between the two builds is not listed.  The
    ledger holds only what the font build actually moves.

UNITS
    Displacements come out of the SVG in the paper's own staff-spaces, and a
    staff-space is 1.7573 mm at the default 20 pt staff but 0.4393 mm at staff
    size 5.  So the ceiling is applied in MILLIMETRES, converted per file from
    that file's own viewBox and page height -- otherwise a small-staff file's
    delta reads four times larger than it is.  The ranking really does change:
    repeat-sign-global-size5 is the largest in staff-spaces and only the second
    largest in mm.

IT ALSO REPLACES THE R9/R15 BASELINE
    `baseline/svg' held eight committed pages and claimed one thing about them:
    NO DRIFT -- the port's output had not changed since the freeze.  Six of those
    eight are now covered by the GATE, and covered BETTER, because the gate checks
    them against the ORACLE rather than against the port's own earlier answer.  The
    other two are not, because the gate does not certify them either.

    So the rule here is general and self-maintaining: ANY PAGE THE GATE DOES NOT
    CERTIFY CARRIES A CONTENT HASH.  That covers the two orphans, ten more pages
    that never had drift protection at all, and it drops a page automatically once
    the engine work makes the gate vouch for it.

USAGE
    font-delta.py OURS_DIR THEIRS_DIR --gate GATE_VERDICTS.tsv --write LEDGER.tsv
    font-delta.py OURS_DIR THEIRS_DIR --gate GATE_VERDICTS.tsv --check LEDGER.tsv
    font-delta.py --selftest

    GATE_VERDICTS.tsv is what `compare-output.py reference/svg GATE_DIR --tsv'
    writes.  --check exits 1 on any change against the ledger, in either
    direction, or any row over the ceiling.
"""
import hashlib
import os
import re
import sys

TRANSLATE = re.compile(r'<g transform="translate\(([-\d.]+), ([-\d.]+)\)">')
VIEWBOX = re.compile(r'viewBox="[-\d.]+ [-\d.]+ ([\d.]+) ([\d.]+)"')
PAGE_MM = re.compile(r'height="([\d.]+)mm"')

DEFAULT_CEILING_MM = 0.05


def mm_per_unit(text):
    """Returns how many millimetres one SVG user unit is on this page."""
    box = VIEWBOX.search(text)
    page = PAGE_MM.search(text)
    if not box or not page:
        return None
    height_units = float(box.group(2))
    if height_units <= 0:
        return None
    return float(page.group(1)) / height_units


def marks(text):
    return [(float(m.group(1)), float(m.group(2))) for m in TRANSLATE.finditer(text)]


def compare(ours_text, theirs_text):
    """Returns (verdict, moved, total, dx, dy, mm) for one page.

    THE LEDGER IS ABOUT WHERE THINGS ARE, NOT WHAT THEY LOOK LIKE.  When the
    substitution includes the SVG fonts, every page carrying a music glyph
    differs in its path DATA -- the same shape drawn with different Beziers --
    while nothing has moved at all.  Recording ~2,000 such pages would bury the
    handful that matter, so a page whose marks are all in the same place is not a
    ledger row; the count of them is reported in the header instead.
    """
    a, b = marks(ours_text), marks(theirs_text)
    if len(a) != len(b):
        # The -ddebug-skylines pages DRAW their skylines, so a different outline
        # changes how many segments there are.  That is not a displacement and
        # must not be averaged into one: it gets its own verdict and its own
        # stability check.
        return ("STRUCTURE", len(a), len(b), 0.0, 0.0, 0.0)

    if a == b:
        return ("IDENTICAL" if ours_text == theirs_text else "GLYPH-BYTES",
                0, len(a), 0.0, 0.0, 0.0)

    scale = mm_per_unit(ours_text)
    dx = dy = 0.0
    moved = 0
    for (x1, y1), (x2, y2) in zip(a, b):
        ex, ey = abs(x1 - x2), abs(y1 - y2)
        if ex > 0.0 or ey > 0.0:
            moved += 1
        dx, dy = max(dx, ex), max(dy, ey)
    worst_mm = max(dx, dy) * scale if scale else float("nan")
    return ("MOVED", moved, len(a), dx, dy, worst_mm)


def measure(ours_dir, theirs_dir, gate):
    """Returns (ledger rows, pages seen, pages whose glyph BYTES alone differ).

    A page earns a ledger row two ways, and they are different facts: the two font
    builds render it differently, or the GATE does not certify it.  Neither set
    contains the other -- collision-head-solfa-fa renders identically under both
    font builds and is still uncertified, and most of the 139 moved pages are
    certified -- so the ledger is their union.
    """
    rows = []
    seen = 0
    glyph_only = 0
    for name in sorted(os.listdir(ours_dir)):
        if not name.endswith(".svg"):
            continue
        seen += 1
        verdict_gate = gate.get(name, "UNKNOWN")
        certified = verdict_gate == "MATCH"
        sha = "-" if certified else digest(os.path.join(ours_dir, name))

        other = os.path.join(theirs_dir, name)
        if not os.path.exists(other):
            rows.append((name, "ABSENT", 0, 0, 0.0, 0.0, float("nan"),
                         verdict_gate, sha))
            continue
        with open(os.path.join(ours_dir, name)) as handle:
            ours = handle.read()
        with open(other) as handle:
            theirs = handle.read()
        verdict, moved, total, dx, dy, worst = compare(ours, theirs)
        if verdict == "GLYPH-BYTES":
            glyph_only += 1
            verdict = "IDENTICAL"
        if verdict == "IDENTICAL":
            if certified:
                continue
            rows.append((name, "SAME-FONTS", 0, 0, 0.0, 0.0, 0.0, verdict_gate, sha))
            continue
        rows.append((name, verdict, moved, total, dx, dy, worst, verdict_gate, sha))
    return rows, seen, glyph_only


def digest(path):
    """The page's content hash -- the drift half of the ledger."""
    with open(path, "rb") as handle:
        return hashlib.sha256(handle.read()).hexdigest()


def read_gate(path):
    """Reads compare-output.py's verdict TSV: page name -> verdict."""
    verdicts = {}
    with open(path) as handle:
        for line in handle:
            if line.startswith("#") or not line.strip():
                continue
            fields = line.rstrip("\n").split("\t")
            if len(fields) >= 2:
                verdicts[fields[0]] = fields[1]
    return verdicts


def format_row(row):
    """The row as the ledger stores it.

    /!\\ COMPARISONS GO THROUGH HERE, NOT THROUGH THE RAW FLOATS. The ledger
    records four and six decimals, so the recorded value IS the rounded one; a
    check that compared freshly-measured doubles against values re-parsed from
    the file reported all 139 rows CHANGED on a round trip against the ledger it
    had just written.
    """
    name, verdict, moved, total, dx, dy, worst, gate, sha = row
    return "%s\t%s\t%d\t%d\t%.4f\t%.4f\t%.6f\t%s\t%s" % (
        name, verdict, moved, total, dx, dy, worst, gate, sha)


def render(rows):
    out = [
        "# CodeBrix.LilyPort -- font-build delta ledger (ruling R19).",
        "#",
        "# Every page here renders DIFFERENTLY under the port's own Emmentaler build",
        "# than under the one LilyPond ships.  Same engine, same input, same",
        "# everything else -- so each number is the cost of building our own fonts,",
        "# and nothing else.  Pages absent from this file are byte-identical.",
        "#",
        "# The numbers are EXACT, not sampled: the font build is byte-reproducible and",
        "# the engine is deterministic.  So any change to any of them is a signal and",
        "# has to be explained, even a change that makes a number SMALLER.",
        "#",
        "# verdict  MOVED     marks are in different places; dx/dy are the largest",
        "#                    single-mark displacement in the paper's staff-spaces,",
        "#                    and mm is that converted through this page's own scale.",
        "#          STRUCTURE the page has a different NUMBER of marks.  Only the",
        "#                    -ddebug-skylines pages do this, because they draw the",
        "#                    skylines themselves; `moved'/`total' hold the two counts.",
        "#          ABSENT    the page is missing from the other render entirely.",
        "#          SAME-FONTS both font builds render it identically; it is here",
        "#                    only because the GATE does not certify it.",
        "#",
        "# gate     what the LilyPond-font run graded against the oracle corpus.  A",
        "#          page the gate says MATCH is vouched for by an ORACLE and needs no",
        "#          drift record, so its sha256 column is `-\'.  Every other page",
        "#          carries the hash of OUR OWN rendering, which is what the retired",
        "#          baseline/svg used to hold as eight whole files.",
        "#",
        "# file\tverdict\tmoved\ttotal\tmax_dx_ss\tmax_dy_ss\tmax_mm\tgate\tsha256",
    ]
    for row in rows:
        out.append(format_row(row))
    return "\n".join(out) + "\n"


def load(path):
    """Returns the committed rows, keyed by file name, as (line, parsed-mm)."""
    rows = {}
    with open(path) as handle:
        for line in handle:
            if line.startswith("#") or not line.strip():
                continue
            text = line.rstrip("\n")
            rows[text.split("\t")[0]] = text
    return rows


def check(rows, ledger_path, ceiling):
    recorded = load(ledger_path)
    seen = {r[0]: format_row(r) for r in rows}
    problems = []

    for name in sorted(set(recorded) | set(seen)):
        was, now = recorded.get(name), seen.get(name)
        if was is None:
            problems.append("NEW      %s\n           now %s\n           -- this page "
                            "did not differ before" % (name, now))
        elif now is None:
            problems.append("GONE     %s\n           was %s\n           -- it now "
                            "renders identically" % (name, was))
        elif was != now:
            problems.append("CHANGED  %s\n           was %s\n           now %s"
                            % (name, was, now))

    over = [r for r in rows if r[1] == "MOVED" and r[6] > ceiling]
    for r in sorted(over, key=lambda r: -r[6]):
        problems.append("OVER     %s  %.6f mm exceeds the %.3f mm ceiling"
                        % (r[0], r[6], ceiling))

    return problems


SELFTEST_PAGE = ('<svg width="210.00mm" height="297.00mm" '
                 'viewBox="0.0000 -0.0000 478.0063 676.0375">'
                 '<g transform="translate(10.0000, %s)">'
                 '<line stroke-width="0.1"/></g></svg>')


def selftest():
    """Six cases, four of them controls."""
    failures = []

    def expect(label, got, want):
        if got != want:
            failures.append("%s: got %r, wanted %r" % (label, got, want))

    identical = SELFTEST_PAGE % "20.0000"
    expect("a page identical to itself does not appear",
           compare(identical, identical)[0], "IDENTICAL")

    # CONTROL: a page that differs must NOT report identical.
    expect("a moved mark is MOVED",
           compare(SELFTEST_PAGE % "20.0000", SELFTEST_PAGE % "20.0100")[0], "MOVED")

    verdict, moved, total, dx, dy, worst = compare(
        SELFTEST_PAGE % "20.0000", SELFTEST_PAGE % "20.0100")
    expect("the displacement is the difference", round(dy, 4), 0.0100)
    expect("only the moved mark is counted", (moved, total), (1, 1))

    # THE UNIT CONVERSION, hand-computed: this page is 297 mm over 676.0375
    # units, so one unit is 0.4393 mm and 0.01 units is 0.004393 mm.  A version
    # that forgot to convert would answer 0.01 here.
    expect("mm comes from the page's own scale", round(worst, 6), 0.004393)

    # CONTROL: the same displacement on a page at a DIFFERENT scale must convert
    # differently, or the conversion is not happening.
    wide = SELFTEST_PAGE.replace("676.0375", "169.0094")
    expect("a different page scale gives a different mm",
           round(compare(wide % "20.0000", wide % "20.0100")[5], 6), 0.017573)

    # CONTROL: a different NUMBER of marks is not a displacement.
    # /!\ The added mark is spelled exactly as the backend spells one. An earlier
    # version of this case wrote a SELF-CLOSING <g/>, which the mark pattern does
    # not match, so the fixture had the same mark count as the page it was being
    # contrasted with and the case passed for the wrong reason -- caught by this
    # self-test on its first run, which is the whole argument for having one.
    two = SELFTEST_PAGE.replace(
        "</svg>", '<g transform="translate(1.0000, 2.0000)"><line/></g></svg>')
    expect("a different mark count is STRUCTURE",
           compare(SELFTEST_PAGE % "20.0000", two % "20.0000")[0], "STRUCTURE")

    # CONTROL: same places, different glyph bytes, is NOT a displacement.
    drawn = SELFTEST_PAGE.replace('stroke-width="0.1"', 'stroke-width="0.2"')
    expect("a redrawn glyph in the same place is GLYPH-BYTES",
           compare(SELFTEST_PAGE % "20.0000", drawn % "20.0000")[0], "GLYPH-BYTES")

    for line in failures:
        print("  FAIL " + line)
    print("font-delta self-test: %d case(s) checked, %d failed"
          % (8, len(failures)))
    return 1 if failures else 0


def main():
    if "--selftest" in sys.argv:
        return selftest()

    if len(sys.argv) < 5:
        print(__doc__)
        return 2

    ours_dir, theirs_dir = sys.argv[1], sys.argv[2]
    ceiling = DEFAULT_CEILING_MM
    if "--ceiling" in sys.argv:
        ceiling = float(sys.argv[sys.argv.index("--ceiling") + 1])

    gate = {}
    if "--gate" in sys.argv:
        gate = read_gate(sys.argv[sys.argv.index("--gate") + 1])
    elif "--write" in sys.argv or "--check" in sys.argv:
        print("*** refusing to run without --gate: the ledger records a drift hash for "
              "every page the gate does not certify, and without the gate's verdicts it "
              "cannot tell which those are ***")
        return 2

    rows, seen, glyph_only = measure(ours_dir, theirs_dir, gate)
    moved = [r for r in rows if r[1] == "MOVED"]
    print("# ours   : %s" % ours_dir)
    print("# theirs : %s" % theirs_dir)
    print("# %d page(s) compared; %d place every mark identically, of which %d draw "
          "the same shapes with different curves" % (seen, seen - len(rows), glyph_only))
    print("# %d ledger row(s): %d moved, %d structural, %d same-fonts, %d absent; "
          "ceiling %.3f mm"
          % (len(rows), len(moved),
             len([r for r in rows if r[1] == "STRUCTURE"]),
             len([r for r in rows if r[1] == "SAME-FONTS"]),
             len([r for r in rows if r[1] == "ABSENT"]), ceiling))
    print("# %d row(s) carry a drift hash, because the gate does not certify them"
          % len([r for r in rows if r[8] != "-"]))
    if moved:
        worst = max(moved, key=lambda r: r[6])
        values = sorted(r[6] for r in moved)
        print("# worst  : %s at %.6f mm" % (worst[0], worst[6]))
        print("# median %.6f mm, p90 %.6f mm"
              % (values[len(values) // 2], values[min(int(0.9 * (len(values) - 1)),
                                                      len(values) - 1)]))

    if "--write" in sys.argv:
        path = sys.argv[sys.argv.index("--write") + 1]
        with open(path, "w") as handle:
            handle.write(render(rows))
        print("*** wrote %d row(s) to %s ***" % (len(rows), path))
        return 0

    if "--check" in sys.argv:
        path = sys.argv[sys.argv.index("--check") + 1]
        problems = check(rows, path, ceiling)
        if not problems:
            print("*** font-delta holds: %d row(s), all matching the ledger, "
                  "none over %.3f mm ***" % (len(rows), ceiling))
            return 0
        for line in problems:
            print(line)
        print("\n*** font-delta FAILED: %d problem(s) ***" % len(problems))
        return 1

    print(render(rows), end="")
    return 0


if __name__ == "__main__":
    sys.exit(main())
