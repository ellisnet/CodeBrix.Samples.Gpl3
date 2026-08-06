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
#   2. Glyph inventory: WHICH marks appear, and how many of each. This catches
#      "wrong notehead", "missing clef", "extra accidental" -- errors of musical
#      content rather than of position.
#   3. Glyph placement: where each mark sits, compared with a tolerance. This is
#      where spacing and layout errors show up.
#
# A port passes stage 2 long before stage 3, and that ordering is deliberate: it
# lets progress be measured continuously instead of as a single distant pass/fail.
#
# HOW A GLYPH IS IDENTIFIED (rewritten 2026-08-05, EPG13)
#
# This file used to look for <use xlink:href="#name"> elements and count <path>
# elements by how many "M" commands they held. That was a guess about LilyPond's
# SVG backend, and it was wrong: LilyPond emits NO <use> elements at all. It
# writes each music glyph as its own outline --
#
#     <path transform="scale(0.0040, -0.0040)" d="M217 136c56 0 ..." fill="currentColor"/>
#
# -- lifted verbatim out of the shipped .svg font. So the old comparator saw zero
# glyphs and zero placements in EVERY reference file, graded every page on a
# coarse path-shape histogram, and could not report a position difference even in
# principle. That is what pinned the whole suite at a GLYPHS-DIFFER floor.
#
# A glyph is now identified BY ITS OUTLINE. The `d` attribute is the shape, so two
# glyphs are the same glyph exactly when their path data agrees -- no font, no
# name table and no id numbering involved. Position comes from the accumulated
# translate() of the enclosing <g> elements, which is where LilyPond puts it.
# ----------------------------------------------------------------------------

import argparse
import collections
import hashlib
import os
import re
import sys
import xml.etree.ElementTree as ElementTree

SVG_NS = "{http://www.w3.org/2000/svg}"
XLINK_NS = "{http://www.w3.org/1999/xlink}"

# A glyph outline is drawn by a transform that ONLY scales, with the Y factor
# negated -- that is dump-path's "scale(s, -s)" and nothing else emits it.
GLYPH_TRANSFORM = re.compile(r"^\s*scale\(\s*([-0-9.eE]+)\s*,\s*([-0-9.eE]+)\s*\)\s*$")

TRANSLATE = re.compile(r"translate\(\s*([-0-9.eE]+)\s*,\s*([-0-9.eE]+)\s*\)")
SCALE = re.compile(r"scale\(\s*([-0-9.eE]+)\s*(?:,\s*([-0-9.eE]+)\s*)?\)")

WHITESPACE = re.compile(r"\s+")


def _outline_key(data):
    """Identify a glyph by its outline, compactly and whitespace-insensitively."""
    normalized = WHITESPACE.sub(" ", (data or "").strip())
    digest = hashlib.sha1(normalized.encode("utf-8")).hexdigest()[:12]
    return "glyph:" + digest


def _path_signature(data):
    """A coarse shape signature for a DRAWN path -- a slur, a tie, a beam.

    These are computed by the engraver rather than copied from a font, so their
    control points are the last thing a port gets right and comparing them exactly
    would drown out everything else. The command mix is enough to tell a slur from
    a stem.
    """
    letters = [character for character in (data or "") if character.isalpha()]
    return "path:" + "".join(sorted(collections.Counter(letters).elements()))


def parse_svg(path):
    """Extract the engraving-relevant marks from one SVG page.

    Returns a dict with the mark inventory and each mark's placement in page
    coordinates.
    """
    try:
        tree = ElementTree.parse(path)
    except ElementTree.ParseError as error:
        return {"error": "unparseable: %s" % error}

    glyphs = collections.Counter()
    placements = []

    def visit(element, offset_x, offset_y):
        transform = element.get("transform") or ""

        # A pure scale() on a <path> is the glyph transform, not a placement, and
        # must not move the accumulated origin.
        is_glyph = element.tag == SVG_NS + "path" and GLYPH_TRANSFORM.match(transform)

        if not is_glyph and transform:
            for match in TRANSLATE.finditer(transform):
                offset_x += float(match.group(1))
                offset_y += float(match.group(2))

        tag = element.tag
        if tag == SVG_NS + "path":
            data = element.get("d") or ""
            if is_glyph:
                name = _outline_key(data)
            else:
                name = _path_signature(data)
            glyphs[name] += 1
            placements.append((name, offset_x, offset_y))
        elif tag == SVG_NS + "use":
            # Not emitted by LilyPond, but a port might; keep it gradeable.
            href = element.get(XLINK_NS + "href") or element.get("href") or ""
            name = "use:" + href.lstrip("#")
            glyphs[name] += 1
            placements.append((name, offset_x + _number(element.get("x")),
                               offset_y + _number(element.get("y"))))
        elif tag == SVG_NS + "line":
            name = "line:%s" % _round(_number(element.get("stroke-width")))
            glyphs[name] += 1
            placements.append((name,
                               offset_x + _number(element.get("x1")),
                               offset_y + _number(element.get("y1"))))
        elif tag in (SVG_NS + "rect", SVG_NS + "polygon", SVG_NS + "circle",
                     SVG_NS + "ellipse"):
            name = tag[len(SVG_NS):]
            glyphs[name] += 1
            placements.append((name,
                               offset_x + _number(element.get("x")),
                               offset_y + _number(element.get("y"))))
        elif tag == SVG_NS + "text":
            # The content lives in child <tspan>s; the family and size are what
            # decide how it will actually look.
            content = "".join(
                (child.text or "") for child in element.iter() if child.tag == SVG_NS + "tspan")
            name = "text:%s:%s:%s" % (
                element.get("font-family") or "",
                element.get("font-size") or "",
                WHITESPACE.sub(" ", content.strip()))
            glyphs[name] += 1
            placements.append((name, offset_x, offset_y))

        for child in element:
            visit(child, offset_x, offset_y)

    visit(tree.getroot(), 0.0, 0.0)
    return {"glyphs": glyphs, "placements": placements}


def _round(value):
    return "%.4f" % value


def _number(text):
    if not text:
        return 0.0
    match = re.match(r"\s*(-?[0-9.]+(?:[eE][-+]?[0-9]+)?)", text)
    return float(match.group(1)) if match else 0.0


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
    parser.add_argument("--tsv", metavar="PATH",
                        help="also write one machine-readable row per file: "
                             "name <TAB> verdict <TAB> detail. This is what ratchet.py "
                             "consumes; the human report above is not parseable.")
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

    if arguments.tsv:
        with open(arguments.tsv, "w") as handle:
            handle.write("# CodeBrix.LilyPort comparison run, one row per reference page.\n")
            handle.write("# name\tverdict\tdetail\n")
            for verdict in sorted(results):
                for name, detail in results[verdict]:
                    handle.write("%s\t%s\t%s\n" % (name, verdict, detail.replace("\t", " ")))

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
