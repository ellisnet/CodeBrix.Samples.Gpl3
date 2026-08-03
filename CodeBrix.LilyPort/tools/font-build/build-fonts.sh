#!/bin/bash
#
# build-fonts.sh -- build the Emmentaler music fonts from the Metafont
#                   sources in CodeBrix.LilyPort/mf.
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
# WHAT THIS REPLACES
#
# Upstream LilyPond builds these fonts through mf/GNUmakefile, which pulls in
# the make/ include tree and a pile of autoconf-substituted variables, and
# therefore only runs inside a configured LilyPond source tree. This script is
# a standalone reimplementation of exactly the font-producing subset of that
# makefile, with no autoconf and no LilyPond build tree required.
#
# See README.txt in this directory for the full explanation of the pipeline,
# the toolchain, and the licensing constraints.
# ----------------------------------------------------------------------------

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LILYPORT_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
MF_DIR="${LILYPORT_ROOT}/mf"
OUT_DIR="${1:-${SCRIPT_DIR}/out}"

# Version string stamped into the generated fonts. Keep this matched to the
# LilyPond release the mf/ sources were taken from, or the fonts will not be
# comparable against that release's own output.
VERSION="${LILYPOND_VERSION:-2.27.2}"

# Design sizes and brace subfonts -- these mirror STAFF_SIZES and BRACES in
# upstream mf/GNUmakefile. If upstream ever adds a size, add it here too.
STAFF_SIZES="11 13 14 16 18 20 23 26"
BRACES="a b c d e f g h i"

PERL="${PERL:-perl}"
PYTHON="${PYTHON:-python3}"
FONTFORGE="${FONTFORGE:-fontforge}"

# invoke-mf2pt1.sh reads $FONTFORGE out of the environment.
export FONTFORGE

# ----------------------------------------------------------------------------
# REPRODUCIBILITY AND AUTHORSHIP
#
# Left alone, FontForge stamps every generated font with the wall-clock build
# time (OpenType `head` created/modified, the `UniqueID` name record, and the
# SVG <metadata> block) and with the BUILDING USER'S REAL NAME, which it reads
# from the passwd GECOS field. That gives two bad outcomes:
#
#   * rebuilds are never byte-identical, so the committed fonts cannot be
#     verified by rebuilding them; and
#
#   * the shipped SVG fonts carry a line reading "By <person>", which on a
#     font actually designed by Han-Wen Nienhuys, Jan Nieuwenhuizen and
#     Juergen Reuter reads as a false authorship claim.
#
# Both are fixed here rather than by editing the build output afterwards, so
# that what we ship is exactly what the pipeline produces.
#
# SOURCE_DATE_EPOCH is the reproducible-builds standard variable. We default
# it to the commit timestamp of the LilyPond tag the mf/ sources came from
# (v2.27.2, 2026-08-02 13:36:08 +0200), so the font's creation date is the
# date of the sources it was built from. Update it when re-syncing mf/:
#
#     git log -1 --format=%ct <new tag>
#
# USER and LOGNAME together drive FontForge's "By ..." annotation. We set both
# to the font's actual designers, so the generated files credit the people who
# designed these glyphs rather than whoever happened to run the build.
# ----------------------------------------------------------------------------

SOURCE_DATE_EPOCH="${SOURCE_DATE_EPOCH:-1785670568}"
export SOURCE_DATE_EPOCH
export USER="Nienhuys, Nieuwenhuizen and Reuter"
export LOGNAME="Nienhuys, Nieuwenhuizen and Reuter"

say() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }
note() { printf '    %s\n' "$*"; }
die() { printf '\nERROR: %s\n' "$*" >&2; exit 1; }

# ----------------------------------------------------------------------------
# 0. Preflight -- fail early and legibly rather than half way through 57 fonts
# ----------------------------------------------------------------------------

say "Preflight"

for tool in mf mpost "${FONTFORGE}" "${PERL}" "${PYTHON}"; do
    command -v "${tool}" >/dev/null 2>&1 \
        || die "'${tool}' not found. See README.txt section 2 for the apt install line."
done

"${PYTHON}" -c 'import fontforge' >/dev/null 2>&1 \
    || die "python3 cannot 'import fontforge'. Install python3-fontforge (README.txt section 2)."

[ -d "${MF_DIR}" ] || die "Metafont sources not found at ${MF_DIR}"
[ -f "${SCRIPT_DIR}/mf2pt1.pl" ] || die "mf2pt1.pl missing from ${SCRIPT_DIR}"
[ -f "${SCRIPT_DIR}/mf-to-table.py" ] || die "mf-to-table.py missing from ${SCRIPT_DIR}"
[ -f "${MF_DIR}/invoke-mf2pt1.sh" ] || die "invoke-mf2pt1.sh missing from ${MF_DIR}"

note "mf        $(mf --version 2>&1 | head -1)"
note "mpost     $(mpost --version 2>&1 | head -1)"
note "fontforge $(${FONTFORGE} --version 2>&1 | grep -oE 'fontforge [0-9]+' | head -1)"
note "python3   $(${PYTHON} --version 2>&1)"
note "version stamp: ${VERSION}"
note "output dir:    ${OUT_DIR}"

mkdir -p "${OUT_DIR}"
OUT_DIR="$(cd "${OUT_DIR}" && pwd)"

# ----------------------------------------------------------------------------
# 1. mf2pt1.mem
#
# Older mpost builds needed a dumped .mem file. Recent ones do not, but
# invoke-mf2pt1.sh still expects the file to exist. Upstream touches a dummy
# first and then lets a real dump overwrite it if the local mpost still works
# that way; we do the same, and deliberately ignore a dump failure.
# ----------------------------------------------------------------------------

say "1/5  mf2pt1.mem"

(
    cd "${OUT_DIR}"
    touch mf2pt1.mem
    mpost -progname=mpost -ini "${MF_DIR}/mf2pt1.mp" '\dump' >/dev/null 2>&1 || true
)
note "ok"

# ----------------------------------------------------------------------------
# 2. Metafont -> Type 1  (.mf -> .pfb + .tfm + .log)
#
# This is the slow stage: 57 Metafont runs. The .log files are NOT incidental
# output -- stage 3 parses them to recover the glyph metadata that becomes the
# LILC/LILY tables, so they must be kept.
# ----------------------------------------------------------------------------

say "2/5  Metafont -> Type 1  (57 fonts)"

cd "${MF_DIR}"
MF_FONTS=$(ls feta[0-9]*.mf \
              feta-braces-[a-z].mf \
              feta-alphabet*[0-9].mf \
              feta-noteheads*[0-9].mf \
              feta-flags*[0-9].mf \
              parmesan[0-9]*.mf \
              parmesan-noteheads*[0-9].mf 2>/dev/null | sed 's/\.mf$//' | sort -u)

TOTAL=$(echo "${MF_FONTS}" | wc -l)
N=0
for font in ${MF_FONTS}; do
    N=$((N + 1))
    printf '    [%2d/%2d] %-28s' "${N}" "${TOTAL}" "${font}"
    if [ -f "${OUT_DIR}/${font}.pfb" ] && [ -f "${OUT_DIR}/${font}.log" ] \
       && [ "${OUT_DIR}/${font}.pfb" -nt "${MF_DIR}/${font}.mf" ]; then
        printf 'cached\n'
        continue
    fi
    if ! "${MF_DIR}/invoke-mf2pt1.sh" \
            "${PERL} ${SCRIPT_DIR}/mf2pt1.pl" \
            "${MF_DIR}/${font}.mf" \
            "${OUT_DIR}/${font}.pfb" \
            >"${OUT_DIR}/${font}.build-log" 2>&1; then
        printf 'FAILED\n'
        die "Metafont run failed for ${font}. See ${OUT_DIR}/${font}.build-log"
    fi
    printf 'ok\n'
done

# ----------------------------------------------------------------------------
# 3. Metafont logs -> Scheme metadata  (.log -> .lisp + .global-lisp)
#
# mf-to-table.py scrapes the Metafont log for the autometric annotations that
# feta-autometric.mf emits, and writes them as Scheme alists. These become the
# LILC (per-glyph) and LILY (global) OpenType tables in stage 4 -- the
# engraver-critical metadata: glyph extents in staff spaces, stem attachment
# points, ledger shortening ranges.
# ----------------------------------------------------------------------------

say "3/5  Metafont logs -> Scheme metadata"

cd "${OUT_DIR}"
LOGS=$(ls ./*.log 2>/dev/null | grep -v '\.build-log$' || true)
[ -n "${LOGS}" ] || die "no .log files in ${OUT_DIR} -- stage 2 produced nothing"

# shellcheck disable=SC2086
"${PYTHON}" "${SCRIPT_DIR}/mf-to-table.py" ${LOGS}
note "$(ls ./*.lisp 2>/dev/null | wc -l) .lisp, $(ls ./*.global-lisp 2>/dev/null | wc -l) .global-lisp"

# ----------------------------------------------------------------------------
# 4. Merge into Emmentaler OTFs  (one per design size)
#
# Each emmentaler-<size>.otf merges SIX subfonts at that design size:
#   feta, feta-alphabet, feta-flags, feta-noteheads,
#   parmesan, parmesan-noteheads
# and then attaches the LILC/LILY tables. gen-emmentaler.fontforge.py derives
# the design size by regex from the OUTPUT filename, so the --out name matters.
# It also emits a .svg alongside the .otf.
# ----------------------------------------------------------------------------

say "4/5  Merge -> emmentaler-<size>.otf"

for size in ${STAFF_SIZES}; do
    printf '    emmentaler-%-3s ' "${size}"
    if ! "${FONTFORGE}" -lang=py \
            -script "${MF_DIR}/gen-emmentaler.fontforge.py" \
            --version="${VERSION}" \
            --in "${OUT_DIR}/" \
            --out "${OUT_DIR}/emmentaler-${size}.otf" \
            >"${OUT_DIR}/emmentaler-${size}.gen-log" 2>&1; then
        printf 'FAILED\n'
        die "fontforge failed for size ${size}. See ${OUT_DIR}/emmentaler-${size}.gen-log"
    fi
    printf 'ok  (%s bytes)\n' "$(stat -c%s "${OUT_DIR}/emmentaler-${size}.otf")"
done

# ----------------------------------------------------------------------------
# 5. The brace font
#
# Braces are a separate font because they are continuously scalable rather
# than drawn per design size. gen-emmentaler-brace.fontforge.py hardcodes the
# subfont list "abcdefghi", so no subfonts manifest is needed at build time --
# upstream's .subfonts rule exists only for make dependency tracking.
# ----------------------------------------------------------------------------

say "5/5  emmentaler-brace.otf"

printf '    emmentaler-brace '
if ! "${FONTFORGE}" -lang=py \
        -script "${MF_DIR}/gen-emmentaler-brace.fontforge.py" \
        --version "${VERSION}" \
        --in "${OUT_DIR}" \
        --out="${OUT_DIR}/emmentaler-brace.otf" \
        >"${OUT_DIR}/emmentaler-brace.gen-log" 2>&1; then
    printf 'FAILED\n'
    die "fontforge failed for the brace font. See ${OUT_DIR}/emmentaler-brace.gen-log"
fi
printf 'ok  (%s bytes)\n' "$(stat -c%s "${OUT_DIR}/emmentaler-brace.otf")"

# ----------------------------------------------------------------------------

# ----------------------------------------------------------------------------
# Housekeeping.
#
# The generator scripts import emmentaler_codes / emmentaler_features /
# emmentaler_kerning from mf/, which makes Python drop a __pycache__ directory
# in there. CodeBrix.LilyPort/mf is meant to stay a byte-identical mirror of
# upstream lilypond/mf, so clean it up rather than leaving the mirror dirty.
# ----------------------------------------------------------------------------

rm -rf "${MF_DIR}/__pycache__"

say "Done"
note "Built $(ls "${OUT_DIR}"/emmentaler-*.otf 2>/dev/null | wc -l) OTF files in ${OUT_DIR}"
note "Next: ./compare-fonts.sh to check them against an official LilyPond release."
