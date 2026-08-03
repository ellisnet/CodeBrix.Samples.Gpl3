#!/bin/bash
#
# compare-fonts.sh -- compare locally built Emmentaler fonts against the
#                     fonts shipped in an official LilyPond release.
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
# WHY THIS IS NOT A BYTE COMPARISON
#
# Locally built fonts will NEVER be byte-identical to a release's fonts.
# FontForge stamps build metadata, table padding and ordering vary between
# FontForge versions, and the CFF charstring optimiser is not bit-stable
# across releases. A byte diff therefore tells you nothing.
#
# What matters for CodeBrix.LilyPort is that the ENGRAVING-RELEVANT content
# is identical:
#
#   * the same set of glyph names (the engraver addresses glyphs BY NAME)
#   * the same advance widths and bounding boxes (layout depends on them)
#   * identical LILC and LILY tables -- the Scheme metadata carrying glyph
#     extents in staff spaces, stem attachment points and ledger shortening
#     ranges. If these differ, the engraver will lay music out differently.
#
# See README.txt section 6.
# ----------------------------------------------------------------------------

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILT_DIR="${1:-${SCRIPT_DIR}/out}"
REF_DIR="${2:-}"

PYTHON="${PYTHON:-python3}"

if [ -z "${REF_DIR}" ]; then
    cat <<EOF
usage: $(basename "$0") [BUILT_DIR] REFERENCE_DIR

  BUILT_DIR      directory holding our emmentaler-*.otf
                 (default: ${SCRIPT_DIR}/out)
  REFERENCE_DIR  directory holding an official LilyPond release's
                 share/lilypond/<version>/fonts/otf/

To obtain a reference set, download the official release archive and extract:

  tar xzf lilypond-2.27.2-linux-x86_64.tar.gz \\
      --strip-components=5 --wildcards '*/fonts/otf/emmentaler-*.otf' \\
      -C /some/reference/dir

EOF
    exit 2
fi

[ -d "${BUILT_DIR}" ] || { echo "no such directory: ${BUILT_DIR}" >&2; exit 1; }
[ -d "${REF_DIR}" ]   || { echo "no such directory: ${REF_DIR}" >&2; exit 1; }

exec "${PYTHON}" "${SCRIPT_DIR}/compare_fonts.py" "${BUILT_DIR}" "${REF_DIR}"
