# compare-output.py -- measure CodeBrix.LilyPort against the LilyPond reference.
#
# This file is part of CodeBrix.LilyPort.
# Copyright (c) 2026 Jeremy Ellis and contributors
#
# CodeBrix.LilyPort is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# ----------------------------------------------------------------------------
# WHAT THIS COMPARES, AND WHY NOT BYTES
#
# Comparing SVG byte-for-byte is useless as a porting signal. Two engravers can
# lay music out identically and still differ in element order, floating-point
# formatting, or id numbering. Worse, a byte comparison gives one bit of
# information -- "different" -- which tells you nothing about WHERE the port
# diverged.
#
# So this compares graduated, engraving-meaningful features, from coarse to fine:
#
#   1. Did we produce output at all?
#   2. Page count.
#   3. Glyph inventory: WHICH music glyphs appear, and how many of each. This
#      catches "wrong notehead", "missing clef", "extra accidental" -- errors of
#      musical content rather than of position.
#   4. Glyph placement: the coordinates of each glyph, compared with a tolerance.
#      This is where spacing and layout errors show up.
#
# A port passes stage 3 long before stage 4, and that ordering is deliberate: it
# lets progress be measured continuously instead of as a single distant pass/fail.
# ----------------------------------------------------------------------------

import argparse
import collections
import os
import re
import sys
import xml.etree.ElementTree as ElementTree

SVG_NS = "{http://www.w3.org/2000/svg}"
XLINK_NS = "{http://www.w3.org/1999/xlink}"


def parse_svg(path):
    """Extract the engraving-relevant features from one SVG page.

    Returns a dict with the glyph inventory and their placements. LilyPond's SVG
    backend emits music glyphs as <use> elements referencing glyph ids, and text
    as <tspan>, so those are what we look at.
    """
    try:
        tree = ElementTree.parse(path)
    except ElementTree.ParseError as error:
        return {"error": "unparseable: %s" % error}

    root = tree.getroot()
    glyphs = collections.Counter()
    placements = []

    for element in root.iter():
        tag = element.tag
        if tag == SVG_NS + "use":
            href = element.get(XLINK_NS + "href") or element.get("href") or ""
            name = href.lstrip("#")
            glyphs[name] += 1
            placements.append((name, _number(element.get("x")), _number(element.get("y"))))
        elif tag == SVG_NS + "path":
            # Stems, beams, slurs and staff lines are paths. Their shape is in the
            # 'd' attribute; we record only a coarse signature, because exact
            # control points are the last thing a port gets right.
            data = element.get("d") or ""
            glyphs["<path:%d>" % data.count("M")] += 1
        elif tag == SVG_NS + "tspan" and (element.text or "").strip():
            glyphs["<text>"] += 1

    return {"glyphs": glyphs, "placements": placements}


def _number(text):
    if not text:
        return 0.0
    match = re.match(r"-?[0-9.]+", text)
    return float(match.group(0)) if match else 0.0


def compare_one(reference_path, candidate_path, tolerance):
    """Compare one output file against its reference, returning a graded result."""
    if not os.path.exists(candidate_path):
        return "MISSING", "no output produced"

    reference = parse_svg(reference_path)
    candidate = parse_svg(candidate_path)

    if "error" in candidate:
        return "UNPARSEABLE", candidate["error"]
    if "error" in reference:
        return "REFERENCE-BAD", reference["error"]

    reference_glyphs = reference["glyphs"]
    candidate_glyphs = candidate["glyphs"]

    if reference_glyphs != candidate_glyphs:
        missing = reference_glyphs - candidate_glyphs
        extra = candidate_glyphs - reference_glyphs
        detail = []
        if missing:
            detail.append("missing " + ", ".join(
                "%s x%d" % (name, count) for name, count in missing.most_common(4)))
        if extra:
            detail.append("extra " + ", ".join(
                "%s x%d" % (name, count) for name, count in extra.most_common(4)))
        return "GLYPHS-DIFFER", "; ".join(detail)

    # Same glyphs; now check where they sit.
    reference_places = sorted(reference["placements"])
    candidate_places = sorted(candidate["placements"])
    if len(reference_places) != len(candidate_places):
        return "PLACEMENT-COUNT", "%d vs %d placements" % (
            len(reference_places), len(candidate_places))

    worst = 0.0
    worst_name = None
    for (rname, rx, ry), (cname, cx, cy) in zip(reference_places, candidate_places):
        if rname != cname:
            return "PLACEMENT-ORDER", "%s vs %s" % (rname, cname)
        delta = max(abs(rx - cx), abs(ry - cy))
        if delta > worst:
            worst = delta
            worst_name = rname

    if worst > tolerance:
        return "PLACEMENT-DIFFERS", "worst %.4f at %s (tolerance %.4f)" % (
            worst, worst_name, tolerance)

    return "MATCH", "worst placement delta %.4f" % worst


def main():
    parser = argparse.ArgumentParser(
        description="Compare CodeBrix.LilyPort output against the LilyPond reference.")
    parser.add_argument("reference_dir", help="reference svg/ directory")
    parser.add_argument("candidate_dir", help="CodeBrix.LilyPort svg/ output directory")
    parser.add_argument("--tolerance", type=float, default=0.01,
                        help="maximum placement difference to still count as a match")
    parser.add_argument("--show", type=int, default=15,
                        help="how many examples to print per category")
    arguments = parser.parse_args()

    references = sorted(
        name for name in os.listdir(arguments.reference_dir) if name.endswith(".svg"))
    if not references:
        print("no reference SVGs in %s" % arguments.reference_dir, file=sys.stderr)
        return 2

    results = collections.defaultdict(list)
    for name in references:
        verdict, detail = compare_one(
            os.path.join(arguments.reference_dir, name),
            os.path.join(arguments.candidate_dir, name),
            arguments.tolerance)
        results[verdict].append((name, detail))

    total = len(references)
    print("reference : %s (%d files)" % (arguments.reference_dir, total))
    print("candidate : %s" % arguments.candidate_dir)
    print("tolerance : %.4f" % arguments.tolerance)
    print()

    # Ordered coarse-to-fine, so the report reads as a progress ladder.
    order = ["MATCH", "PLACEMENT-DIFFERS", "PLACEMENT-ORDER", "PLACEMENT-COUNT",
             "GLYPHS-DIFFER", "UNPARSEABLE", "MISSING", "REFERENCE-BAD"]
    print("%-18s %7s  %s" % ("VERDICT", "COUNT", "SHARE"))
    print("-" * 46)
    for verdict in order:
        entries = results.get(verdict)
        if not entries:
            continue
        print("%-18s %7d  %5.1f%%" % (verdict, len(entries), 100.0 * len(entries) / total))

    matched = len(results.get("MATCH", []))
    print("-" * 46)
    print("%-18s %7d  %5.1f%%" % ("TOTAL", total, 100.0))
    print()
    print("*** %d of %d match (%.2f%%) ***" % (matched, total, 100.0 * matched / total))

    for verdict in order:
        entries = results.get(verdict)
        if not entries or verdict == "MATCH":
            continue
        print("\n%s -- first %d of %d:" % (verdict, min(arguments.show, len(entries)), len(entries)))
        for name, detail in entries[:arguments.show]:
            print("  %-46s %s" % (name, detail[:90]))

    return 0 if matched == total else 1


if __name__ == "__main__":
    sys.exit(main())
