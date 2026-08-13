# generate-glyph-identity.py -- build the committed glyph-identity index that lets
#                               compare-output.py grade music glyphs BY NAME.
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
# WHY THIS EXISTS
#
# Both engravers write a music glyph as its own outline, lifted verbatim out of
# the .svg font they were built against:
#
#     <path transform="scale(0.0040, -0.0040)" d="M217 136c56 0 ..." fill="currentColor"/>
#
# The port and the oracle read DIFFERENT BUILDS of the same font. CodeBrix.LilyPort
# ships its own Emmentaler, built 2026-08-02 from the vendored mf/ mirror with
# FontForge 20230101; the oracle's came from the official 2.27.2 release build with
# FontForge 20200314. The designs are the same -- LILC, LILY, every advance and
# every cmap mapping are byte-identical between the two builds, and sampled bounding
# boxes agree to 0.00 font-units -- but the two FontForge versions SERIALIZE those
# outlines differently: different contour start points, control points apart by one
# or two units of a 1000-unit em.
#
# So the same notehead reads as two different strings of bytes, and a comparator
# that identifies a glyph by its exact path data can never report MATCH for any page
# carrying music. That is what this index fixes, per D29 as restated 2026-08-12:
#
#     glyph identity is NAMED-GLYPH identity, byte-verified against each side's own
#     font; everything else remains byte-exact; visual/tolerance comparison remains
#     forbidden.
#
# There is no fuzzy matching here and none in the comparator. A path is still
# identified by its EXACT bytes -- those bytes are simply resolved, through this
# index, to the NAME of the glyph they are a verbatim copy of. A path that resolves
# to nothing keeps raw-byte identity and reads as a difference (fail-strict).
#
# WHAT IS COMMITTED, AND WHAT IS NOT
#
# Only HASHES are committed. No font file, and no glyph outline, from either side is
# redistributed by this index, and the comparator never needs the oracle installed:
# it reads the committed TSV and nothing else. The oracle must be present only when
# the index is GENERATED, which is the same model reference generation already uses.
#
# ----------------------------------------------------------------------------
# THE NORMALIZATION, AND WHY GETTING IT WRONG WOULD BE SILENT
#
# The FontForge SVG fonts carry literal NEWLINES inside long `d` attribute values
# (318 of 662 glyphs in the port's emmentaler-20; 338 in the oracle's), and the
# emitted pages inherit them verbatim. compare-output.py reads pages with
# ElementTree, and XML attribute-value normalization turns every newline, carriage
# return and tab inside an attribute value into a SPACE -- so the `d` string the
# comparator sees is NOT the raw bytes of the file. Today that cancels out, because
# both sides go through the same parser.
#
# This index is built from font files, so it MUST apply the same normalization
# before hashing. If it did not, every lookup would miss, fail-strict would take
# every path, and the whole change would be a no-op that still passed the
# reference-against-reference self-check -- a silent nothing. Both the font side
# here and the page side in compare-output.py therefore normalize the same way:
#
#     any run of whitespace -> a single space, then strip
#
# and the fonts are parsed with ElementTree rather than scraped with a regex, so the
# XML normalization is done by the same code that does it for pages.
#
# `--check` is the fence against this index going stale: it regenerates the
# candidate side from the shipped assets and diffs it against what is committed.
# ----------------------------------------------------------------------------

import argparse
import collections
import datetime
import hashlib
import os
import sys
import xml.etree.ElementTree as ElementTree

SVG_NS = "{http://www.w3.org/2000/svg}"

HERE = os.path.dirname(os.path.abspath(__file__))
LILYPORT_ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))

# The port's shipped assets -- the same files FontAssets embeds and serves.
CANDIDATE_FONT_DIR = os.path.join(LILYPORT_ROOT, "assets", "fonts", "svg")

# The pinned oracle installation. Present at GENERATION time only.
DEFAULT_ORACLE_FONT_DIR = os.path.expanduser(
    "~/ClaudeHome/oracle/lilypond-2.27.2/share/lilypond/2.27.2/fonts/svg")

INDEX_PATH = os.path.join(HERE, "glyph-identity.tsv")

SIDES = ("candidate", "reference")


def normalize_path_data(data):
    """Collapse a `d` attribute the way compare-output.py sees it after parsing.

    ElementTree has already turned newlines, carriage returns and tabs inside the
    attribute value into spaces; this collapses runs and strips, which is exactly
    what _outline_key does on the page side. Keep the two in step.
    """
    return " ".join((data or "").split())


def digest_of(data):
    """The index key: sha256 of the NORMALIZED path data."""
    return hashlib.sha256(normalize_path_data(data).encode("utf-8")).hexdigest()


def file_digest(path):
    """sha256 of a source font file, recorded as provenance in the index header."""
    hasher = hashlib.sha256()
    with open(path, "rb") as handle:
        for block in iter(lambda: handle.read(65536), b""):
            hasher.update(block)
    return hasher.hexdigest()


def font_name_of(path):
    """emmentaler-20.svg -> emmentaler-20."""
    return os.path.splitext(os.path.basename(path))[0]


def read_font(path):
    """Return [(glyph-name, normalized-d)] for one SVG font, in document order.

    Glyphs with no outline (the space glyph, and anything FontForge emitted without
    a `d`) are skipped: they have no bytes for a page to copy, so they can never be
    the target of a lookup.
    """
    tree = ElementTree.parse(path)
    glyphs = []
    for element in tree.iter():
        if element.tag not in (SVG_NS + "glyph", "glyph"):
            continue
        name = element.get("glyph-name")
        data = element.get("d")
        if not name or not data or not data.strip():
            continue
        glyphs.append((name, normalize_path_data(data)))
    return glyphs


def scan_side(font_dir):
    """Read every emmentaler-*.svg in one side's font directory.

    Returns (rows, provenance, duplicate_classes) where rows is a sorted list of
    (font, glyph-name, digest), provenance is [(font-file, sha256, glyph-count)],
    and duplicate_classes maps font -> {digest: [names]} for the WITHIN-FONT name
    classes that share outline bytes.
    """
    if not os.path.isdir(font_dir):
        raise SystemExit("font directory not found: %s" % font_dir)

    names = sorted(name for name in os.listdir(font_dir) if name.endswith(".svg"))
    if not names:
        raise SystemExit("no .svg fonts in %s" % font_dir)

    rows = []
    provenance = []
    duplicate_classes = {}

    for name in names:
        path = os.path.join(font_dir, name)
        font = font_name_of(path)
        glyphs = read_font(path)

        by_digest = collections.defaultdict(list)
        for glyph_name, data in glyphs:
            digest = digest_of(data)
            rows.append((font, glyph_name, digest))
            by_digest[digest].append(glyph_name)

        shared = {digest: sorted(members)
                  for digest, members in by_digest.items() if len(members) > 1}
        if shared:
            duplicate_classes[font] = shared

        provenance.append((name, file_digest(path), len(glyphs)))

    rows.sort()
    return rows, provenance, duplicate_classes


def inventory_of(rows):
    """font -> set of glyph names, for the two-sided equality assertion."""
    inventory = collections.defaultdict(set)
    for font, glyph_name, _ in rows:
        inventory[font].add(glyph_name)
    return inventory


def write_index(path, sides, provenance, duplicates, oracle_font_dir, today):
    with open(path, "w") as handle:
        handle.write(
            "# CodeBrix.LilyPort glyph-identity index -- generated by "
            "generate-glyph-identity.py.\n"
            "#\n"
            "# Maps the sha256 of a NORMALIZED glyph outline to the glyph NAME it\n"
            "# belongs to, separately for each side, so compare-output.py can grade a\n"
            "# music glyph by name instead of by the serialization of whichever font\n"
            "# build produced it (D29, restated 2026-08-12).\n"
            "#\n"
            "# NORMALIZATION (must stay identical to compare-output.py's _outline_key):\n"
            "#   any run of whitespace -> a single space, then strip. The fonts are read\n"
            "#   with ElementTree, so XML attribute-value normalization (newline, CR and\n"
            "#   tab inside an attribute value become a space) has already been applied\n"
            "#   by the same code that applies it to pages.\n"
            "#\n"
            "# Hashes only -- no outline and no font file from either side is\n"
            "# redistributed here, and the comparator never reads a font.\n"
            "#\n"
            "# generated : %s\n" % today)
        handle.write("# candidate : %s (the port's shipped assets)\n"
                     % os.path.relpath(CANDIDATE_FONT_DIR, LILYPORT_ROOT))
        handle.write("# reference : %s\n" % oracle_font_dir)
        handle.write("#\n# SOURCE FONTS (sha256 of each file as read)\n")
        for side in SIDES:
            for name, digest, count in provenance[side]:
                handle.write("#   %-10s %-22s %s  %d glyphs\n"
                             % (side, name, digest, count))

        # Duplicate-byte name classes are DATA, not errors, so they are recorded HERE
        # -- committed alongside the hashes they explain -- rather than printed once
        # and lost. Two names sharing outline bytes inside one font is an honest
        # equivalence class: a whole-note and a half-note shape-note head really are
        # the same drawing. The comparator's name-SET semantics carries the class
        # through; picking a winner would plant a wrong answer for the page that
        # draws the other glyph.
        handle.write("#\n# DUPLICATE-BYTE NAME CLASSES (recorded, never resolved)\n")
        for side in SIDES:
            classes = duplicates[side]
            count = sum(len(shared) for shared in classes.values())
            handle.write("#   %s: %d classes\n" % (side, count))
            for font in sorted(classes):
                for digest in sorted(classes[font]):
                    handle.write("#     %-16s %s\n"
                                 % (font, ", ".join(classes[font][digest])))

        handle.write("#\n# side\tfont\tglyph-name\tsha256-of-normalized-d\n")
        for side in SIDES:
            for font, glyph_name, digest in sides[side]:
                handle.write("%s\t%s\t%s\t%s\n" % (side, font, glyph_name, digest))


def read_index(path):
    """Return side -> sorted [(font, glyph-name, digest)] from a committed index."""
    if not os.path.exists(path):
        raise SystemExit("index not found: %s -- generate it first" % path)
    sides = {side: [] for side in SIDES}
    with open(path) as handle:
        for line in handle:
            if line.startswith("#") or not line.strip():
                continue
            fields = line.rstrip("\n").split("\t")
            if len(fields) != 4:
                raise SystemExit("malformed index row: %r" % line)
            side, font, glyph_name, digest = fields
            if side not in sides:
                raise SystemExit("unknown side %r in index" % side)
            sides[side].append((font, glyph_name, digest))
    for side in SIDES:
        sides[side].sort()
    return sides


def report_duplicates(duplicate_classes, side):
    """Summarise the within-font duplicate-byte name classes on one side.

    The FULL list is written into the index header by write_index -- committed next
    to the hashes it explains -- so this only has to say how many there are and
    where. Duplicate classes are DATA, not errors: a whole-note and a half-note
    shape-note head really are the same drawing, and the comparator's name-SET
    semantics carries the class through.
    """
    total = sum(len(shared) for shared in duplicate_classes.values())
    if not total:
        print("  %-10s no within-font duplicate-byte name classes" % side)
        return
    per_font = ", ".join("%s %d" % (font, len(duplicate_classes[font]))
                         for font in sorted(duplicate_classes))
    print("  %-10s %d classes (%s)" % (side, total, per_font))


def report_class_agreement(candidate_rows, reference_rows):
    """Measure how often the two builds AGREE on a glyph's byte-class.

    A glyph name resolves to the SET of names sharing its outline bytes in its own
    font. If the two builds disagree about that set for some name -- one build
    happens to serialize two glyphs identically where the other does not -- then a
    page drawing it resolves to different sets on the two sides and reads as a
    difference. That is fail-strict's direction (stricter, never looser), but it
    caps how much of the corpus can ever resolve equal, so it is measured and
    recorded rather than assumed away.
    """
    def class_map(rows):
        by_font = collections.defaultdict(lambda: collections.defaultdict(set))
        for font, glyph_name, digest in rows:
            by_font[font][digest].add(glyph_name)
        resolved = {}
        for font, by_digest in by_font.items():
            for names in by_digest.values():
                shared = frozenset(names)
                for name in names:
                    resolved[(font, name)] = shared
        return resolved

    candidate = class_map(candidate_rows)
    reference = class_map(reference_rows)
    disagreeing = sorted(key for key in candidate
                         if candidate[key] != reference.get(key))
    total = len(candidate)
    print("class agreement: %d of %d glyph names resolve to the SAME name-set on "
          "both sides (%.2f%%)"
          % (total - len(disagreeing), total,
             100.0 * (total - len(disagreeing)) / total if total else 0.0))
    for font, name in disagreeing[:10]:
        print("  differs: %-16s %-28s candidate %s / reference %s"
              % (font, name,
                 sorted(candidate[(font, name)]), sorted(reference.get((font, name), ()))))
    if len(disagreeing) > 10:
        print("  ... and %d more" % (len(disagreeing) - 10))
    return disagreeing


def command_generate(arguments):
    today = datetime.date.today().isoformat()
    sides = {}
    provenance = {}
    duplicates = {}

    for side, font_dir in (("candidate", CANDIDATE_FONT_DIR),
                           ("reference", arguments.oracle_font_dir)):
        rows, side_provenance, side_duplicates = scan_side(font_dir)
        sides[side] = rows
        provenance[side] = side_provenance
        duplicates[side] = side_duplicates
        print("%-10s %s -- %d fonts, %d named outlines"
              % (side, font_dir, len(side_provenance), len(rows)))

    candidate_inventory = inventory_of(sides["candidate"])
    reference_inventory = inventory_of(sides["reference"])
    if not check_inventories(candidate_inventory, reference_inventory):
        return 1

    print("\nduplicate-byte name classes (full list is written into the index header):")
    for side in SIDES:
        report_duplicates(duplicates[side], side)

    print()
    report_class_agreement(sides["candidate"], sides["reference"])

    write_index(arguments.index, sides, provenance, duplicates,
                arguments.oracle_font_dir, today)
    print("\nwrote %s" % arguments.index)
    return 0


def check_inventories(candidate_inventory, reference_inventory):
    """Assert both sides carry the same fonts and the same glyph names in each.

    The two builds are the same DESIGN; if they ever stop agreeing on which glyphs
    exist, named-glyph identity is comparing two different fonts and the premise of
    this whole index is gone.
    """
    ok = True
    candidate_fonts = set(candidate_inventory)
    reference_fonts = set(reference_inventory)
    if candidate_fonts != reference_fonts:
        ok = False
        print("FONT SETS DIFFER: candidate-only %s; reference-only %s"
              % (sorted(candidate_fonts - reference_fonts),
                 sorted(reference_fonts - candidate_fonts)), file=sys.stderr)

    for font in sorted(candidate_fonts & reference_fonts):
        only_candidate = candidate_inventory[font] - reference_inventory[font]
        only_reference = reference_inventory[font] - candidate_inventory[font]
        if only_candidate or only_reference:
            ok = False
            print("NAME SETS DIFFER in %s: candidate-only %s; reference-only %s"
                  % (font, sorted(only_candidate)[:8], sorted(only_reference)[:8]),
                  file=sys.stderr)

    if ok:
        print("inventory: both sides carry the same %d fonts and identical glyph-name "
              "sets in each" % len(candidate_fonts))
    return ok


def command_check(arguments):
    """Regenerate the candidate side from the shipped assets and diff the committed
    index against it, then re-assert two-sided inventory equality.

    This is the fence for the index going stale: our fonts are ours to regenerate,
    and nothing else would notice if they moved. The reference side cannot drift --
    it can only change if the pinned oracle changes, which is a project-wide event.
    """
    committed = read_index(arguments.index)
    fresh, _, duplicates = scan_side(CANDIDATE_FONT_DIR)

    ok = True
    if committed["candidate"] != fresh:
        ok = False
        committed_set = set(committed["candidate"])
        fresh_set = set(fresh)
        print("CANDIDATE SIDE IS STALE: the committed index does not match "
              "%s" % os.path.relpath(CANDIDATE_FONT_DIR, LILYPORT_ROOT), file=sys.stderr)
        print("  committed rows %d, regenerated rows %d"
              % (len(committed["candidate"]), len(fresh)), file=sys.stderr)
        for font, glyph_name, digest in sorted(committed_set - fresh_set)[:8]:
            print("  committed only: %s %s %s" % (font, glyph_name, digest[:16]),
                  file=sys.stderr)
        for font, glyph_name, digest in sorted(fresh_set - committed_set)[:8]:
            print("  assets only   : %s %s %s" % (font, glyph_name, digest[:16]),
                  file=sys.stderr)
        print("  regenerate with: python3 generate-glyph-identity.py", file=sys.stderr)
    else:
        print("candidate side matches the shipped assets (%d named outlines)"
              % len(fresh))

    if not check_inventories(inventory_of(committed["candidate"]),
                             inventory_of(committed["reference"])):
        ok = False

    print("\nduplicate-byte name classes (full list is in the index header):")
    report_duplicates(duplicates, "candidate")

    print()
    report_class_agreement(committed["candidate"], committed["reference"])

    if not ok:
        print("\n*** glyph-identity --check FAILED ***", file=sys.stderr)
        return 1
    print("\n*** glyph-identity --check holds ***")
    return 0


def main():
    parser = argparse.ArgumentParser(
        description="Build (or verify) the committed glyph-identity index used by "
                    "compare-output.py to grade music glyphs by name.")
    parser.add_argument("--check", action="store_true",
                        help="do not write: regenerate the candidate side from the "
                             "shipped assets, diff it against the committed index, "
                             "and re-assert two-sided inventory equality")
    parser.add_argument("--index", default=INDEX_PATH,
                        help="path to the committed index (default: %(default)s)")
    parser.add_argument("--oracle-font-dir", default=DEFAULT_ORACLE_FONT_DIR,
                        help="the pinned oracle's fonts/svg directory, read only when "
                             "GENERATING (default: %(default)s)")
    arguments = parser.parse_args()

    if arguments.check:
        return command_check(arguments)
    return command_generate(arguments)


if __name__ == "__main__":
    sys.exit(main())
