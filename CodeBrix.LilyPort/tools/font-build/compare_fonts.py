# compare_fonts.py -- structural comparison of built vs reference Emmentaler.
#
# This file is part of CodeBrix.LilyPort.
# Copyright (c) 2026 Jeremy Ellis and contributors
#
# CodeBrix.LilyPort is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# Driven by compare-fonts.sh. Reads OpenType tables directly -- no FontForge
# needed for the table comparison, only for glyph geometry.

import os
import struct
import sys
import zlib


def read_tables(path):
    """Return {tag: bytes} for every table in an OpenType file."""
    with open(path, "rb") as handle:
        data = handle.read()
    count = struct.unpack(">H", data[4:6])[0]
    tables = {}
    for i in range(count):
        rec = 12 + i * 16
        tag = data[rec:rec + 4].decode("latin1")
        offset, length = struct.unpack(">II", data[rec + 8:rec + 16])
        tables[tag] = data[offset:offset + length]
    return tables


def scheme_table(raw):
    """LILC is zlib-compressed; LILY is stored raw. Mirror what
    lily/open-type-font.cc does: try to inflate, fall back to as-is."""
    try:
        return zlib.decompress(raw)
    except zlib.error:
        return raw


def glyph_metrics(path):
    """{glyphname: (width, bbox)} via FontForge, or None if unavailable."""
    try:
        import fontforge
    except ImportError:
        return None
    font = fontforge.open(path)
    metrics = {}
    for glyph in font.glyphs():
        metrics[glyph.glyphname] = (glyph.width, glyph.boundingBox())
    font.close()
    return metrics


def compare_one(name, built, reference):
    """Compare a single font. Returns list of problem strings."""
    problems = []

    bt = read_tables(built)
    rt = read_tables(reference)

    # --- the custom Scheme metadata tables: these MUST match exactly --------
    for tag in ("LILC", "LILY"):
        if tag not in bt:
            problems.append(f"{tag} table missing from built font")
            continue
        if tag not in rt:
            problems.append(f"{tag} table missing from reference font")
            continue
        b = scheme_table(bt[tag])
        r = scheme_table(rt[tag])
        if b != r:
            # Locate the first divergence so the report is actionable.
            limit = min(len(b), len(r))
            at = next((i for i in range(limit) if b[i] != r[i]), limit)
            problems.append(
                f"{tag} content differs (built {len(b)}B, ref {len(r)}B, "
                f"first difference at byte {at})")

    # --- glyph inventory and metrics ---------------------------------------
    bm = glyph_metrics(built)
    rm = glyph_metrics(reference)
    if bm is None or rm is None:
        problems.append("fontforge unavailable - glyph metrics not compared")
        return problems

    only_built = sorted(set(bm) - set(rm))
    only_ref = sorted(set(rm) - set(bm))
    if only_built:
        problems.append(
            f"{len(only_built)} glyph(s) only in built: {only_built[:5]}")
    if only_ref:
        problems.append(
            f"{len(only_ref)} glyph(s) only in reference: {only_ref[:5]}")

    width_diffs = []
    bbox_diffs = []
    for gname in sorted(set(bm) & set(rm)):
        bw, bbox = bm[gname]
        rw, rbox = rm[gname]
        if bw != rw:
            width_diffs.append((gname, bw, rw))
        # Bounding boxes are floats from the outline; allow a hair of slack.
        if any(abs(a - b) > 0.01 for a, b in zip(bbox, rbox)):
            bbox_diffs.append((gname, bbox, rbox))

    if width_diffs:
        problems.append(
            f"{len(width_diffs)} advance-width difference(s), "
            f"e.g. {width_diffs[:3]}")
    if bbox_diffs:
        problems.append(
            f"{len(bbox_diffs)} bounding-box difference(s), "
            f"e.g. {bbox_diffs[0][0]}")

    return problems


def main():
    built_dir, ref_dir = sys.argv[1], sys.argv[2]

    names = sorted(
        f for f in os.listdir(built_dir)
        if f.startswith("emmentaler-") and f.endswith(".otf"))
    if not names:
        print(f"no emmentaler-*.otf found in {built_dir}", file=sys.stderr)
        return 1

    print(f"built:     {built_dir}")
    print(f"reference: {ref_dir}")
    print()
    print(f"{'FONT':22} {'GLYPHS':>7}  RESULT")
    print("-" * 72)

    failures = 0
    for name in names:
        built = os.path.join(built_dir, name)
        reference = os.path.join(ref_dir, name)
        if not os.path.exists(reference):
            print(f"{name:22} {'-':>7}  NO REFERENCE - skipped")
            continue

        problems = compare_one(name, built, reference)
        metrics = glyph_metrics(built)
        count = len(metrics) if metrics else 0

        if problems:
            failures += 1
            print(f"{name:22} {count:>7}  DIFFERS")
            for problem in problems:
                print(f"{'':22} {'':>7}    - {problem}")
        else:
            print(f"{name:22} {count:>7}  MATCH")

    print("-" * 72)
    if failures:
        print(f"{failures} font(s) differ. See README.txt section 6 for how to "
              f"interpret this.")
    else:
        print("All fonts match the reference on glyph names, metrics, "
              "and LILC/LILY metadata.")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
