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
# HOW A GLYPH IS IDENTIFIED (rewritten 2026-08-05, EPG13; rewritten again
# 2026-08-12, GLYPH-PARITY -- see the CONTRACT below, which has CHANGED)
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
# THE CONTRACT (D29 as restated 2026-08-12 -- this REPLACES "two glyphs are the
# same glyph exactly when their path data agrees", which was this file's rule from
# 2026-08-05 until then, and which is now WRONG for music glyphs):
#
#     Glyph identity is NAMED-GLYPH identity, byte-verified against each side's
#     own font. Everything else remains byte-exact. Visual and tolerance
#     comparison remain forbidden.
#
# Why it had to change. Both engravers copy a glyph's outline verbatim out of the
# .svg font they were built against, and the two sides read DIFFERENT BUILDS of the
# same font: the port ships its own Emmentaler (FontForge 20230101, from the
# vendored mf/ mirror), the oracle's came from the official 2.27.2 release build
# (FontForge 20200314). Same design -- LILC, LILY, every advance and every cmap
# mapping byte-identical, sampled bounding boxes equal to 0.00 font-units -- but the
# two FontForge versions SERIALIZE the outlines differently. So the same notehead
# reads as two different strings of bytes, and under the old rule NO page carrying
# music could ever report MATCH. That was measured: the oracle's black notehead path
# appears in 1,242 reference pages and zero candidate pages; the port's appears in
# 1,089 candidate pages and zero reference pages -- a perfect per-page substitution.
#
# What it is NOT. This is not fuzzy matching, and no tolerance was added. A glyph
# path is still identified by its EXACT bytes; those bytes are simply resolved,
# through the committed glyph-identity.tsv index, to the NAME of the glyph they are
# a verbatim copy of IN THEIR OWN SIDE'S FONTS. The identity is
#
#     (the SET of glyph names whose outline bytes equal this path's d
#      on this side, PLUS the transform's scale string)
#
# A name SET, because two names in one font can legitimately share bytes -- a whole
# note and a half note shape-note head really are the same drawing. Those classes
# are recorded in the index header and never resolved to a winner. The scale string
# is part of the identity because it is what pins the OPTICAL SIZE: without it, a
# glyph drawn from emmentaler-11 and the same-named glyph from emmentaler-26 would
# collapse to one identity. (Note for the record: the scale did NOT participate in
# the identity before 2026-08-12 -- it was only ever used to RECOGNISE a glyph path.
# Under byte identity it did not need to; under name identity it does.)
#
# FAIL-STRICT. A `d` that resolves to no glyph name on its side keeps raw-byte
# identity -- exactly the behaviour of this file before 2026-08-12. Unresolvable can
# only ever make the comparison STRICTER, never looser.
#
# A GLYPH IS NOT ALWAYS DRAWN UNDER A PURE scale() (found 2026-08-15, PARITY 13
# PREP). `output-svg.scm` draws a multi-glyph run -- one `glyph-string` expression --
# by placing each glyph on the PATH's own transform at the cumulative advance of the
# glyphs before it, so the transform reads `translate(...) scale(...)` rather than a
# bare `scale(...)`. Those paths are glyph outlines copied verbatim out of a font,
# exactly like every other glyph; only their PLACEMENT arrived differently. Until this
# was fixed, they fell to the DRAWN-path branch below and were graded by command-letter
# signature across the two font builds -- which is precisely the substitution the
# contract above exists to defeat, so identical named glyphs graded as different. 49 of
# the 86 sole-kind `path` residue rows drew the same resolved glyph sequence on both
# sides. Resolution is therefore attempted for EVERY path, and the scale string that
# travels with a compound-transform name is the literal marker "compound".
#
# Two properties of that marker matter and are deliberate. It cannot collide with any
# pure-scale identity (no scale string is ever the word "compound"), so a glyph drawn
# at an optical size still cannot be confused with the same glyph drawn in a run; and
# BOTH sides get the same marker, so it grades a difference rather than creating one.
# This does not relax anything -- an unresolved drawn path (a slur, a tie, a beam)
# keeps its command-letter signature EXACTLY as before, and a compound-transform glyph
# is still identified by the bytes it is a verbatim copy of.
#
# Everything that is not a music-glyph path -- staff lines, beams, slurs, rects,
# text, every attribute -- is compared byte-exact, unchanged. Position still comes
# from the accumulated translate() of the enclosing <g> elements, which is where
# LilyPond puts it.
#
# THE NORMALIZATION THAT MAKES THE LOOKUP WORK. The SVG fonts carry literal newlines
# inside long `d` values, and the pages inherit them; ElementTree turns those into
# spaces when it parses a page. The index is built from font files and applies the
# SAME normalization, so the two agree. If it ever stops agreeing, every lookup
# misses, fail-strict takes every path, and this whole mechanism becomes a silent
# no-op that still passes the reference-against-reference self-check -- which is
# why --selftest carries a canary that a real page must resolve a real glyph name.
#
# R10'S BOUNDED FONT-SIZE DELTA, AS A POST-PASS (added 2026-08-16, PARITY 15 --
# ruling R12, which is option C of three that were put up)
#
# A text element's identity here is "text:<font-family>:<font-size>:<content>" built
# from the RAW attribute string, so text:serif:2.2000:X and text:serif:2.1997:X are
# two DIFFERENT elements: the multisets differ, the row returns GLYPHS-DIFFER, and
# those elements never reach placement comparison at all. --tolerance cannot reach
# this -- it applies to PLACEMENT, after the multisets have already matched exactly.
#
# Ruling R10 (2026-08-16) accepts a bounded delta of 0.0005 on FOUR named files and
# four named values, whose cause is diagnosed but unmeasured (Engine PORT-COVERAGE,
# "TEXT FONT SIZE, LAST DECIMAL"). D29 forbids tolerance comparison and says a page
# ever blocked by micro-geometry "gets its own ruling that day"; R10 IS that ruling,
# exercised through the mechanism D29 itself provides.
#
# So the identity function above is left BYTE-EXACT AND UNMODIFIED, and the ruling is
# honoured afterwards: grade normally, and only where the result is GLYPHS-DIFFER on
# one of R10's four files, ask whether the ENTIRE inventory difference is text
# elements identical in family and content whose sizes differ by no more than 0.0005.
# If it is -- and only then -- the reconciled inventory is graded again and the row
# is upgraded, with the upgrade REPORTED per row and counted in the summary.
#
# WHY NOT REWRITE THE SIZE INSIDE THE NAME (options A and B, and the reason not to
# "simplify" this into them later). An unconditional rewrite makes the row MATCH, and
# if a SECOND, genuine divergence later lands on one of those four files the row is
# already green and the new defect rides in unnoticed. Gating the upgrade on the size
# difference being the ONLY difference is what stops the exception becoming a hiding
# place -- and re-grading rather than asserting MATCH is what keeps a PLACEMENT
# difference on those four files visible.
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

INDEX_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                          "glyph-identity.tsv")

# R10's four files and its bound. The standing intent lives here, beside the code
# that is its only consumer, rather than in a second auditable artifact: the G1 skip
# list rules rows OUT of the gate, and these four are ruled IN and graded, with a
# stated bound. The full account is in the Engine's PORT-COVERAGE under "TEXT FONT
# SIZE, LAST DECIMAL".
#
#     file                         oracle    port      delta
#     markup-note-sizes            1.1666    1.1668    0.0002
#     page-layout-bottom-padding   1.7460    1.7465    0.0005
#     fret-diagrams-size           1.9800    1.9797    0.0003
#     tablature-full-notation      2.2000    2.1997    0.0003
#
# It is a BOUND, not a frozen pair: the port's value may move anywhere within it and
# stay accepted; beyond it the row goes red by itself. Corpus-wide this would repeal
# D29 by the back door, which R10 forbids in as many words -- hence the file list.
R10_FILES = frozenset([
    "markup-note-sizes.svg",
    "page-layout-bottom-padding.svg",
    "fret-diagrams-size.svg",
    "tablature-full-notation.svg",
])
R10_SIZE_BOUND = 0.0005


def load_glyph_index(path):
    """Read the committed glyph-identity index.

    Returns side -> {sha256-of-normalized-d: frozenset(glyph names)}. A missing
    index is not fatal: resolution simply never fires and every glyph keeps raw-byte
    identity, which is what this file did before 2026-08-12. It IS reported, because
    silently degrading to the old behaviour is precisely the failure the canary in
    --selftest exists to catch.
    """
    if not os.path.exists(path):
        return None

    accumulating = {}
    with open(path) as handle:
        for line in handle:
            if line.startswith("#") or not line.strip():
                continue
            fields = line.rstrip("\n").split("\t")
            if len(fields) != 4:
                raise SystemExit("malformed glyph-identity row: %r" % line)
            side, _font, glyph_name, digest = fields
            accumulating.setdefault(side, {}).setdefault(digest, set()).add(glyph_name)

    return {side: {digest: frozenset(names) for digest, names in by_digest.items()}
            for side, by_digest in accumulating.items()}


def _normalize_outline(data):
    """The `d` string as both this file and the index generator agree to see it."""
    return WHITESPACE.sub(" ", (data or "").strip())


def _resolve_outline(data, names_by_digest):
    """The glyph name SET this path's bytes are a verbatim copy of, or None.

    Split out of `_outline_key` because a compound-transform glyph needs the lookup
    without the fail-strict fallback: it has to be able to decline, so an unresolved
    path can fall through to the DRAWN-path signature instead.
    """
    if names_by_digest is None:
        return None

    normalized = _normalize_outline(data)
    digest = hashlib.sha256(normalized.encode("utf-8")).hexdigest()
    return names_by_digest.get(digest)


def _outline_key(data, scale, names_by_digest):
    """Identify a music glyph: by NAME where its bytes resolve, by bytes where not.

    `names_by_digest` is one side of the committed index, or None to force raw-byte
    identity (--raw-glyph-bytes, and any run with no index present).

    The scale string travels with the name because the name alone does not say which
    optical size was drawn. It is carried VERBATIM rather than parsed, so a glyph is
    the same glyph only when the two sides wrote the same transform -- no rounding,
    no tolerance.
    """
    names = _resolve_outline(data, names_by_digest)
    if names is not None:
        return "glyph:%s@%s" % ("+".join(sorted(names)), scale)

    # Fail-strict: unresolved bytes keep the pre-2026-08-12 identity exactly.
    digest = hashlib.sha1(_normalize_outline(data).encode("utf-8")).hexdigest()[:12]
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


def parse_svg(path, names_by_digest=None):
    """Extract the engraving-relevant marks from one SVG page.

    `names_by_digest` is THIS SIDE's half of the committed glyph-identity index (see
    the contract at the top of this file), or None to identify glyphs by raw bytes.

    Returns a dict with the mark inventory, each mark's placement in page
    coordinates, and how many glyph paths resolved to a name -- the last is what the
    canary reads, since a normalization slip would show up as "resolved 0" while
    every other number stayed plausible.
    """
    try:
        tree = ElementTree.parse(path)
    except ElementTree.ParseError as error:
        return {"error": "unparseable: %s" % error}

    glyphs = collections.Counter()
    placements = []
    counts = {"glyph_paths": 0, "resolved": 0}

    def visit(element, offset_x, offset_y):
        transform = element.get("transform") or ""

        # A pure scale() on a <path> is the glyph transform, not a placement, and
        # must not move the accumulated origin.
        glyph_scale = None
        if element.tag == SVG_NS + "path":
            match = GLYPH_TRANSFORM.match(transform)
            if match:
                glyph_scale = "%s,%s" % (match.group(1), match.group(2))
        is_glyph = glyph_scale is not None

        if not is_glyph and transform:
            for match in TRANSLATE.finditer(transform):
                offset_x += float(match.group(1))
                offset_y += float(match.group(2))

        tag = element.tag
        if tag == SVG_NS + "path":
            data = element.get("d") or ""
            if is_glyph:
                counts["glyph_paths"] += 1
                name = _outline_key(data, glyph_scale, names_by_digest)
                if "@" in name:
                    counts["resolved"] += 1
            else:
                # A glyph drawn inside a run carries a compound transform (see the
                # contract above). Resolve it too; a path that declines is a DRAWN
                # path and keeps its signature.
                # The two counters deliberately stay on the pure-scale population:
                # they are the canary's ratio, and every compound path that resolves
                # would raise both halves of it, which is how a canary stops being one.
                compound_names = _resolve_outline(data, names_by_digest)
                if compound_names is not None:
                    name = "glyph:%s@compound" % "+".join(sorted(compound_names))
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
    return {"glyphs": glyphs, "placements": placements, "counts": counts}


def _round(value):
    return "%.4f" % value


def _number(text):
    if not text:
        return 0.0
    match = re.match(r"\s*(-?[0-9.]+(?:[eE][-+]?[0-9]+)?)", text)
    return float(match.group(1)) if match else 0.0


def _text_fields(name):
    """(family, size, content) for a text signature, or None for anything else.

    Split with maxsplit rather than on every colon: a font-family list never carries
    one, but page CONTENT is arbitrary and routinely does ("quavers:" is in the
    corpus).
    """
    if not name.startswith("text:"):
        return None

    parts = name.split(":", 3)
    if len(parts) != 4:
        return None

    try:
        size = float(parts[2])
    except ValueError:
        return None

    return parts[1], size, parts[3]


def _sizes_are_separable(glyphs, bound):
    """Whether every pair of DISTINCT text sizes on a page is further apart than bound.

    The fence R10 owes. If two real sizes sat within the bound of each other the
    reconciliation below could pair the wrong elements, so this is asserted rather
    than assumed -- there is no live risk (markup-note-sizes carries sixteen sizes
    whose closest pair is 0.0667 apart) but the file set is free to change.
    """
    sizes = sorted({fields[1] for fields in
                    (_text_fields(name) for name in glyphs) if fields is not None})
    return all(later - earlier > bound
               for earlier, later in zip(sizes, sizes[1:]))


def _r10_reconcile(reference_glyphs, candidate, bound):
    """Candidate marks with R10's tolerated text sizes renamed, or None to decline.

    Declines -- and the caller then keeps GLYPHS-DIFFER, unchanged -- unless the
    WHOLE inventory difference is text elements identical in family and content whose
    sizes differ by no more than `bound`. That gate is the point of the ruling: it is
    what stops the exception becoming a place a second, genuine divergence could hide.

    Returns (glyphs, placements, how many renames) or (None, None, reason).
    """
    candidate_glyphs = candidate["glyphs"]

    if not _sizes_are_separable(reference_glyphs, bound) \
            or not _sizes_are_separable(candidate_glyphs, bound):
        return None, None, "two distinct text sizes on the page are within the bound"

    missing = reference_glyphs - candidate_glyphs
    extra = candidate_glyphs - reference_glyphs

    # Group both halves by the fields that must AGREE, carrying each occurrence so a
    # repeated element cannot be reconciled against a single one.
    def grouped(counter):
        groups = collections.defaultdict(list)
        for name, count in counter.items():
            fields = _text_fields(name)
            if fields is None:
                return None
            family, size, content = fields
            groups[(family, content)].extend([(size, name)] * count)
        return groups

    missing_groups = grouped(missing)
    extra_groups = grouped(extra)
    if missing_groups is None or extra_groups is None:
        return None, None, "the difference is not text elements alone"
    if set(missing_groups) != set(extra_groups):
        return None, None, "the differing text elements disagree on family or content"

    renames = {}
    for key, wanted in missing_groups.items():
        found = extra_groups[key]
        if len(wanted) != len(found):
            return None, None, "the differing text elements disagree in count"
        for (reference_size, reference_name), (candidate_size, candidate_name) in \
                zip(sorted(wanted), sorted(found)):
            if abs(reference_size - candidate_size) > bound:
                return None, None, ("a text size differs by more than %.4f" % bound)
            renames[candidate_name] = reference_name

    if not renames:
        return None, None, "nothing to reconcile"

    glyphs = collections.Counter()
    for name, count in candidate_glyphs.items():
        glyphs[renames.get(name, name)] += count

    placements = [(renames.get(name, name), x, y)
                  for name, x, y in candidate["placements"]]

    return glyphs, placements, len(renames)


def compare_one(reference_path, candidate_path, tolerance,
                reference_names=None, candidate_names=None, name=None):
    """Compare one output file against its reference, returning a graded result.

    Each side is resolved against ITS OWN half of the glyph-identity index -- that
    is what D29's "byte-verified against each side's own font" means, and it is why
    the two halves are never merged into one lookup.

    `name` is the page's file name, and is used for one thing only: deciding whether
    R10's post-pass applies (see the header). Grading itself never depends on it.
    """
    if not os.path.exists(candidate_path):
        return "MISSING", "no output produced", None

    reference = parse_svg(reference_path, reference_names)
    candidate = parse_svg(candidate_path, candidate_names)
    stats = (reference.get("counts"), candidate.get("counts"))

    if "error" in candidate:
        return "UNPARSEABLE", candidate["error"], stats
    if "error" in reference:
        return "REFERENCE-BAD", reference["error"], stats

    verdict, detail = _grade(reference["glyphs"], reference["placements"],
                             candidate["glyphs"], candidate["placements"], tolerance)

    # THE POST-PASS. Everything above is untouched by R10; this runs only after a
    # verdict is already in hand, only on GLYPHS-DIFFER, and only on R10's four files.
    if verdict == "GLYPHS-DIFFER" and name in R10_FILES:
        glyphs, placements, note = _r10_reconcile(
            reference["glyphs"], candidate, R10_SIZE_BOUND)
        if glyphs is not None:
            upgraded, upgraded_detail = _grade(
                reference["glyphs"], reference["placements"],
                glyphs, placements, tolerance)
            if upgraded != "GLYPHS-DIFFER":
                return upgraded, ("R10: %d text size(s) within %.4f reconciled; %s"
                                  % (note, R10_SIZE_BOUND, upgraded_detail)), stats

    return verdict, detail, stats


def _grade(reference_glyphs, reference_placements,
           candidate_glyphs, candidate_placements, tolerance):
    """The ladder itself: inventory first, then placement. Returns (verdict, detail)."""
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
    reference_places = sorted(reference_placements)
    candidate_places = sorted(candidate_placements)
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


SELFTEST_SCALE = "0.0040, -0.0040"
SELFTEST_OTHER_SCALE = "0.0057, -0.0057"

# Two serializations of one imaginary glyph, and one of a second glyph. These are
# INVENTED path data, not outlines from either side's fonts: the mechanism under
# test is "do equal names compare equal", which needs no real outline, and inventing
# them keeps this file from redistributing font content.
SELFTEST_HEAD_A = "M0 -46c0 91 116 182 217 182z"
SELFTEST_HEAD_B = "M217 136c56 0 109 -27 109 -89z"
SELFTEST_OTHER = "M5 5 L4 4 L3 9 z"
SELFTEST_UNKNOWN = "M1 2 L3 4 L5 6 z"

# A THIRD serialization of the same glyph, chosen so its COMMAND MIX differs from both
# of the others (upper-case C, and no lower-case c). The compound-transform cases below
# need that: two serializations that happen to share a command mix would pass even with
# the resolution removed, because the drawn-path signature would agree by accident.
SELFTEST_HEAD_C = "M0 -46C0 91 116 182 217 182z"

# An unresolvable path drawn under a compound transform, and a second one with the SAME
# command mix -- these fence that an unresolved compound path keeps signature identity.
SELFTEST_UNKNOWN_SAME_MIX = "M9 8 L7 6 L5 4 z"


def _selftest_page(entries):
    """One miniature page: [(path-data, transform-tail)] inside a translate().

    A tail of "s,s" is the ordinary glyph transform `scale(s, s)'; a tail starting with
    "translate" is written verbatim, which is how the compound-transform cases are built.
    """
    body = "".join(
        '<path transform="%s" d="%s" fill="currentColor"/>'
        % (scale if scale.startswith("translate") else "scale(%s)" % scale, data)
        for data, scale in entries)
    return ('<?xml version="1.0" encoding="UTF-8"?>\n'
            '<svg xmlns="http://www.w3.org/2000/svg" version="1.2">'
            '<g transform="translate(10.0, 20.0)">%s</g></svg>' % body)


def _selftest_text_page(entries):
    """One miniature page of <text> runs: [(family, size, content)].

    Written the way the SVG backend writes them -- the content in a <tspan>, the
    family and size as attributes of the <text> -- because those three attributes are
    exactly what the text signature is built from.
    """
    body = "".join(
        '<text font-family="%s" font-size="%s"><tspan>%s</tspan></text>'
        % (family, size, content)
        for family, size, content in entries)
    return ('<?xml version="1.0" encoding="UTF-8"?>\n'
            '<svg xmlns="http://www.w3.org/2000/svg" version="1.2">'
            '<g transform="translate(10.0, 20.0)">%s</g></svg>' % body)


def _selftest_r10(arguments):
    """Fence R10's post-pass: what it upgrades, and the three things it must not.

    Every case is a RELATIONSHIP with a control. The one case that must upgrade is
    paired with three that must not, so a post-pass that upgraded everything would
    fail (x), (xi) and (xii), and one that upgraded nothing would fail (ix).

    The expected values are read off the RULING, not off this comparator's output:
    R10 accepts 0.0005 on four named files, and 1.1666 against 1.1668 is the
    markup-note-sizes pair the ruling tabulates.
    """
    import tempfile

    inside = ("serif", "1.1668", "quavers:")       # 0.0002 from the reference below
    reference_run = ("serif", "1.1666", "quavers:")
    outside = ("serif", "1.1673", "quavers:")      # 0.0007 -- beyond the bound
    other = ("serif", "2.2000", "Coda")

    # Two sizes 0.0004 apart: closer than the bound, so the page cannot be reconciled
    # unambiguously and the rule must decline rather than pair the wrong elements.
    crowded_reference = [("serif", "1.1666", "a"), ("serif", "1.1670", "b")]
    crowded_candidate = [("serif", "1.1668", "a"), ("serif", "1.1672", "b")]

    cases = [
        ("(ix)   R10 file, size within the bound the ONLY difference -> upgraded",
         "markup-note-sizes.svg", [reference_run], [inside], "MATCH"),
        ("(x)    the same page on a file R10 does NOT name -> GLYPHS-DIFFER",
         "markup-tag-recognized-in-lyrics.svg", [reference_run], [inside],
         "GLYPHS-DIFFER"),
        ("(xi)   R10 file, size beyond the bound -> GLYPHS-DIFFER",
         "markup-note-sizes.svg", [reference_run], [outside], "GLYPHS-DIFFER"),
        ("(xii)  R10 file, size within the bound but a SECOND difference too "
         "-> GLYPHS-DIFFER",
         "markup-note-sizes.svg", [reference_run, other], [inside], "GLYPHS-DIFFER"),
        ("(xiii) R10 file whose own sizes sit within the bound -> declined",
         "markup-note-sizes.svg", crowded_reference, crowded_candidate,
         "GLYPHS-DIFFER"),
    ]

    failures = 0
    with tempfile.TemporaryDirectory() as workdir:
        for index_of_case, (title, name, reference_entries, candidate_entries,
                            expected) in enumerate(cases):
            reference_path = os.path.join(workdir, "r10ref%d.svg" % index_of_case)
            candidate_path = os.path.join(workdir, "r10cand%d.svg" % index_of_case)
            with open(reference_path, "w") as handle:
                handle.write(_selftest_text_page(reference_entries))
            with open(candidate_path, "w") as handle:
                handle.write(_selftest_text_page(candidate_entries))

            verdict, detail, _ = compare_one(
                reference_path, candidate_path, arguments.tolerance, None, None, name)
            ok = verdict == expected
            failures += 0 if ok else 1
            print("  %-4s %s" % ("ok" if ok else "FAIL", title))
            if not ok:
                print("       expected %s, got %s (%s)" % (expected, verdict, detail))

    return failures


def command_selftest(arguments):
    """Fence the named-glyph identity itself, in relationships with controls.

    Rule 18: nothing here is a value recorded from the comparator's own output. Each
    case states a RELATIONSHIP that must hold, and the cases that must MATCH are
    paired with controls that must NOT -- a mechanism that answered MATCH to
    everything would fail cases (ii), (iii) and (iv), and one that answered
    "different" to everything would fail case (i).
    """
    import tempfile

    index = {
        "candidate": {
            hashlib.sha256(SELFTEST_HEAD_A.encode()).hexdigest(): frozenset(["test.head"]),
            hashlib.sha256(SELFTEST_OTHER.encode()).hexdigest(): frozenset(["test.other"]),
        },
        "reference": {
            hashlib.sha256(SELFTEST_HEAD_B.encode()).hexdigest(): frozenset(["test.head"]),
            hashlib.sha256(SELFTEST_HEAD_C.encode()).hexdigest(): frozenset(["test.head"]),
            hashlib.sha256(SELFTEST_OTHER.encode()).hexdigest(): frozenset(["test.other"]),
        },
    }

    compound = "translate(1.5, 2.5) scale(%s)" % SELFTEST_SCALE

    cases = [
        ("(i)   same name, different serializations -> MATCH",
         [(SELFTEST_HEAD_B, SELFTEST_SCALE)], [(SELFTEST_HEAD_A, SELFTEST_SCALE)],
         "MATCH"),
        ("(ii)  unresolvable candidate bytes -> NOT a match (fail-strict)",
         [(SELFTEST_HEAD_B, SELFTEST_SCALE)], [(SELFTEST_UNKNOWN, SELFTEST_SCALE)],
         "GLYPHS-DIFFER"),
        ("(iii) different names, same scale -> differ",
         [(SELFTEST_HEAD_B, SELFTEST_SCALE)], [(SELFTEST_OTHER, SELFTEST_SCALE)],
         "GLYPHS-DIFFER"),
        ("(iv)  same name, different scale strings -> differ",
         [(SELFTEST_HEAD_B, SELFTEST_SCALE)], [(SELFTEST_HEAD_A, SELFTEST_OTHER_SCALE)],
         "GLYPHS-DIFFER"),
        # (v)-(viii) fence the COMPOUND-transform resolution added at PARITY 13. Case (v)
        # is the one that was failing: the same named glyph, serialized differently by
        # the two font builds, drawn inside a glyph-string run. Note HEAD_C's command mix
        # differs from HEAD_A's, so (v) cannot pass by signature agreement.
        ("(v)   same name under a COMPOUND transform -> MATCH",
         [(SELFTEST_HEAD_C, compound)], [(SELFTEST_HEAD_A, compound)],
         "MATCH"),
        ("(vi)  different names under a compound transform -> differ",
         [(SELFTEST_HEAD_C, compound)], [(SELFTEST_OTHER, compound)],
         "GLYPHS-DIFFER"),
        ("(vii) compound vs pure scale, same name -> differ (no marker collision)",
         [(SELFTEST_HEAD_B, SELFTEST_SCALE)], [(SELFTEST_HEAD_A, compound)],
         "GLYPHS-DIFFER"),
        ("(viii) UNRESOLVED compound paths keep signature identity -> MATCH",
         [(SELFTEST_UNKNOWN, compound)], [(SELFTEST_UNKNOWN_SAME_MIX, compound)],
         "MATCH"),
    ]

    failures = 0
    with tempfile.TemporaryDirectory() as workdir:
        for index_of_case, (title, reference_entries, candidate_entries, expected) in \
                enumerate(cases):
            reference_path = os.path.join(workdir, "ref%d.svg" % index_of_case)
            candidate_path = os.path.join(workdir, "cand%d.svg" % index_of_case)
            with open(reference_path, "w") as handle:
                handle.write(_selftest_page(reference_entries))
            with open(candidate_path, "w") as handle:
                handle.write(_selftest_page(candidate_entries))

            verdict, detail, _ = compare_one(
                reference_path, candidate_path, arguments.tolerance,
                index["reference"], index["candidate"])
            ok = verdict == expected
            failures += 0 if ok else 1
            print("  %-4s %s" % ("ok" if ok else "FAIL", title))
            if not ok:
                print("       expected %s, got %s (%s)" % (expected, verdict, detail))

    failures += _selftest_r10(arguments)
    failures += _selftest_canary(arguments)

    if failures:
        print("\n*** comparator --selftest FAILED (%d) ***" % failures, file=sys.stderr)
        return 1
    print("\n*** comparator --selftest holds ***")
    return 0


def _selftest_canary(arguments):
    """A REAL page must resolve a REAL glyph name.

    Everything above runs on a synthetic index, so it would pass unchanged even if
    the normalization applied to real fonts disagreed with the normalization applied
    to real pages -- the exact slip that would turn this whole mechanism into a
    silent no-op. This case is the one that would catch it, so it reads a page the
    oracle actually produced and insists a black notehead resolves by name.
    """
    index = load_glyph_index(arguments.index)
    if index is None:
        print("  skip no glyph-identity index at %s -- the canary cannot run, and "
              "a skipped canary is not a passing one" % arguments.index)
        return 1

    page = os.path.join(os.path.dirname(os.path.abspath(__file__)),
                        "reference", "svg", "bend-spanner-simple.svg")
    if not os.path.exists(page):
        # The reference corpus is a 62 MB build product and is deliberately not
        # committed; regenerating it just to run a canary is not this command's job.
        print("  skip reference corpus absent (%s) -- regenerate it with "
              "generate-reference.sh to run the canary" % os.path.basename(page))
        return 0

    parsed = parse_svg(page, index["reference"])
    counts = parsed["counts"]

    # The identity is a name SET, so the glyph is looked for INSIDE the class rather
    # than as a whole key: on this page noteheads.s2 shares its outline bytes with
    # noteheads.s2sol, and reads as "noteheads.s2+noteheads.s2sol". Asserting the
    # whole key would be asserting a duplicate class that is free to change.
    wanted = [name for name in parsed["glyphs"]
              if name.startswith("glyph:") and "@" in name
              and "noteheads.s2" in name[len("glyph:"):name.rindex("@")].split("+")]

    if not wanted:
        print("  FAIL canary: %s resolved %d of %d glyph paths by name, but NONE to "
              "noteheads.s2 -- suspect the normalization (see the header)"
              % (os.path.basename(page), counts["resolved"], counts["glyph_paths"]))
        return 1

    print("  ok   canary: %s resolved %d of %d glyph paths by name, including %s"
          % (os.path.basename(page), counts["resolved"], counts["glyph_paths"],
             wanted[0]))
    return 0


def resolve_sides(index_path, reference_dir, candidate_dir, raw_glyph_bytes,
                  baseline=False):
    """Pick each side's half of the glyph-identity index.

    Normally the first directory is the oracle's corpus and the second is the port's,
    so they resolve against the "reference" and "candidate" halves respectively.

    THE FIRST EXCEPTION is the standing self-check, which compares the reference
    directory against ITSELF. Those bytes came out of the oracle's fonts on both
    sides, so both sides must resolve against the reference half; grading the second
    copy against the port's half would make every oracle glyph unresolvable, and the
    self-check would report thousands of differences between a directory and itself.
    Comparing a directory with itself is therefore detected, not configured.

    THE SECOND is --baseline (R9, 2026-08-16), where the first directory holds
    PORT-GENERATED output rather than the oracle's. Both sides' bytes then came out
    of the PORT's fonts, so both resolve against the candidate half. This one is
    configured rather than detected, because two different directories of port output
    are indistinguishable from a corpus comparison by inspection -- and getting it
    wrong would make every glyph on the baseline side unresolvable, which fail-strict
    would report as a difference rather than as a mistake.

    Returns (reference_names, candidate_names, note).
    """
    if raw_glyph_bytes:
        return None, None, "glyph identity: RAW PATH BYTES (--raw-glyph-bytes, diagnostic)"

    index = load_glyph_index(index_path)
    if index is None:
        return None, None, ("glyph identity: RAW PATH BYTES -- no index at %s"
                            % index_path)

    reference_names = index.get("reference")
    candidate_names = index.get("candidate")

    if baseline:
        return (candidate_names, candidate_names,
                "glyph identity: named-glyph (--baseline -- BOTH sides are port "
                "output and resolve against the port's fonts)")

    same_corpus = os.path.realpath(reference_dir) == os.path.realpath(candidate_dir)
    if same_corpus:
        return (reference_names, reference_names,
                "glyph identity: named-glyph (self-check -- both sides resolve "
                "against the reference fonts)")

    return (reference_names, candidate_names,
            "glyph identity: named-glyph (reference and candidate each against "
            "their own fonts)")


def main():
    parser = argparse.ArgumentParser(
        description="Compare CodeBrix.LilyPort output against the LilyPond reference.")
    parser.add_argument("reference_dir", nargs="?", help="reference svg/ directory")
    parser.add_argument("candidate_dir", nargs="?",
                        help="CodeBrix.LilyPort svg/ output directory")
    parser.add_argument("--selftest", action="store_true",
                        help="fence the glyph-identity mechanism on miniature "
                             "documents plus a real-page canary, and exit")
    parser.add_argument("--tolerance", type=float, default=0.01,
                        help="maximum placement difference to still count as a match")
    parser.add_argument("--show", type=int, default=15,
                        help="how many examples to print per category")
    parser.add_argument("--tsv", metavar="PATH",
                        help="also write one machine-readable row per file: "
                             "name <TAB> verdict <TAB> detail. This is what ratchet.py "
                             "consumes; the human report above is not parseable.")
    parser.add_argument("--index", default=INDEX_PATH,
                        help="the committed glyph-identity index (default: %(default)s)")
    parser.add_argument("--baseline", action="store_true",
                        help="the reference directory holds PORT-GENERATED output "
                             "(R9's D43 baseline), so BOTH sides resolve glyph names "
                             "against the port's own fonts. A baseline claims NO "
                             "DRIFT and nothing else -- it is a regression "
                             "instrument, never a correctness result (rule 33).")
    parser.add_argument("--raw-glyph-bytes", action="store_true",
                        help="DIAGNOSTIC: identify glyphs by raw path bytes, the "
                             "pre-2026-08-12 rule. This is the A/B switch for the "
                             "named-glyph change; it is not a comparison mode anyone "
                             "should grade against.")
    arguments = parser.parse_args()

    if arguments.selftest:
        return command_selftest(arguments)

    if not arguments.reference_dir or not arguments.candidate_dir:
        parser.error("reference_dir and candidate_dir are required unless --selftest")

    references = sorted(
        name for name in os.listdir(arguments.reference_dir) if name.endswith(".svg"))
    if not references:
        print("no reference SVGs in %s" % arguments.reference_dir, file=sys.stderr)
        return 2

    reference_names, candidate_names, index_note = resolve_sides(
        arguments.index, arguments.reference_dir, arguments.candidate_dir,
        arguments.raw_glyph_bytes, arguments.baseline)

    results = collections.defaultdict(list)
    # A whole-corpus resolution rate, so a normalization slip cannot hide: it would
    # read 0.0% here while every verdict count stayed superficially plausible.
    seen = {"reference": [0, 0], "candidate": [0, 0]}
    upgraded = []
    for name in references:
        verdict, detail, stats = compare_one(
            os.path.join(arguments.reference_dir, name),
            os.path.join(arguments.candidate_dir, name),
            arguments.tolerance, reference_names, candidate_names, name)
        results[verdict].append((name, detail))
        if detail.startswith("R10:"):
            upgraded.append((name, verdict))
        if stats:
            for side, counts in zip(("reference", "candidate"), stats):
                if counts:
                    seen[side][0] += counts["glyph_paths"]
                    seen[side][1] += counts["resolved"]

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
    print("identity  : %s" % index_note)
    for side in ("reference", "candidate"):
        paths, resolved = seen[side]
        if paths:
            print("            %-10s %d of %d glyph paths resolved to a name (%.1f%%)"
                  % (side, resolved, paths, 100.0 * resolved / paths))
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

    # The exception says so out loud. A silent upgrade would be the hiding place R12
    # went out of its way to refuse.
    print()
    print("R10 post-pass: %d of %d eligible row(s) upgraded at a %.4f font-size bound"
          % (len(upgraded), len(R10_FILES), R10_SIZE_BOUND))
    for name, verdict in upgraded:
        print("               %-46s -> %s" % (name, verdict))

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
