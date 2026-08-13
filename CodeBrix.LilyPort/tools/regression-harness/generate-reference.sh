#!/bin/bash
#
# generate-reference.sh -- run the official LilyPond binary over the vendored
#                          regression suite and record what it produces.
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
# LilyPond has no unit tests in lily/. Correctness is established by rendering
# input/regression/*.ly and comparing the output against a previous run. Since
# CodeBrix.LilyPort is a reimplementation, the "previous run" has to come from
# the real LilyPond -- so this script drives the official binary and records a
# reference the port can be measured against.
#
# See README.txt for what is stored and why the full SVG output is NOT committed.
# ----------------------------------------------------------------------------

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
LILYPORT_ROOT="$(cd "${SCRIPT_DIR}/../.." && pwd)"
SUITE_DIR="${LILYPORT_ROOT}/tests/regression"

LILYPOND_BIN="${LILYPOND_BIN:-}"
OUT_DIR="${1:-${SCRIPT_DIR}/reference}"
JOBS="${JOBS:-$(nproc)}"
LIMIT="${LIMIT:-0}"
PER_FILE_TIMEOUT="${PER_FILE_TIMEOUT:-60}"

say() { printf '\n\033[1m==> %s\033[0m\n' "$*"; }
note() { printf '    %s\n' "$*"; }
die() { printf '\nERROR: %s\n' "$*" >&2; exit 1; }

say "Preflight"

if [ -z "${LILYPOND_BIN}" ]; then
    if command -v lilypond >/dev/null 2>&1; then
        LILYPOND_BIN="$(command -v lilypond)"
    else
        die "lilypond not found. Extract the official release and set LILYPOND_BIN, e.g.
    tar xzf lilypond-2.27.2-linux-x86_64.tar.gz
    export LILYPOND_BIN=\$PWD/lilypond-2.27.2/bin/lilypond
  See README.txt section 2."
    fi
fi

[ -x "${LILYPOND_BIN}" ] || die "not executable: ${LILYPOND_BIN}"
[ -d "${SUITE_DIR}" ] || die "regression suite not found at ${SUITE_DIR}"

VERSION="$("${LILYPOND_BIN}" --version 2>&1 | head -1)"
note "oracle:  ${LILYPOND_BIN}"
note "         ${VERSION}"
note "suite:   ${SUITE_DIR} ($(ls "${SUITE_DIR}"/*.ly 2>/dev/null | wc -l) files)"
note "output:  ${OUT_DIR}"
note "jobs:    ${JOBS}"

case "${VERSION}" in
    *2.27.2*) ;;
    *) note "WARNING: the port targets 2.27.2. Comparing against a different"
       note "         version measures version drift, not port fidelity." ;;
esac

mkdir -p "${OUT_DIR}/svg" "${OUT_DIR}/logs"

# ----------------------------------------------------------------------------
# PIN THE FONTS.  Under -dbackend=svg, ly/paper-defaults-init.ly makes LilyPond
# name the GENERIC families ("serif"/"sans"/"monospace") instead of its own
# "LilyPond Serif" etc., so Pango resolves them through the HOST's fontconfig and
# the corpus silently records whatever that machine happens to have installed.
# reference-fonts.conf.in pins them to the same faces CodeBrix.LilyPort uses, out
# of the oracle's OWN bundled font directory, with no system directory in scope.
# See that file's header for the measurement this rests on.
# ----------------------------------------------------------------------------
LILYPOND_PREFIX="$(cd "$(dirname "${LILYPOND_BIN}")/.." && pwd)"
FONT_DIR=""
for d in "${LILYPOND_PREFIX}"/share/lilypond/*/fonts/otf; do
    [ -d "${d}" ] && FONT_DIR="${d}"
done
[ -n "${FONT_DIR}" ] || die "cannot find the oracle's fonts under ${LILYPOND_PREFIX}/share/lilypond/*/fonts/otf"

FONT_CONF="${OUT_DIR}/reference-fonts.conf"
sed "s|@FONTDIR@|${FONT_DIR}|g" "${SCRIPT_DIR}/reference-fonts.conf.in" > "${FONT_CONF}"
export FONTCONFIG_FILE="${FONT_CONF}"
export FONTCONFIG_PATH="${OUT_DIR}"
note "fonts:   pinned to ${FONT_DIR}"
note "         via ${FONT_CONF} (generic families -> the port's own chain)"

# ----------------------------------------------------------------------------
# Render one file. Runs in a subshell per job, so it must not touch shared state
# beyond its own output files.
# ----------------------------------------------------------------------------

# Render one file into OUT_DIR. Written as a standalone worker script rather than
# an exported shell function: exporting functions through xargs is fragile, and a
# real file is also far easier to run by hand when one input misbehaves.

WORKER="${OUT_DIR}/.render-one.sh"
cat > "${WORKER}" <<'WORKER_EOF'
#!/bin/bash
set -uo pipefail
ly="$1"; out_dir="$2"; lilypond="$3"; timeout_s="$4"
name="$(basename "${ly}" .ly)"
work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT

# A per-file timeout matters: a handful of regression inputs are deliberately
# pathological, and one hang would stall the whole run.
if timeout "${timeout_s}" "${lilypond}" \
        --formats=svg -dbackend=svg -dno-point-and-click --silent \
        -o "${work}/${name}" "${ly}" > "${out_dir}/logs/${name}.log" 2>&1; then
    produced=0
    for svg in "${work}/${name}"*.svg; do
        [ -e "${svg}" ] || continue
        cp "${svg}" "${out_dir}/svg/$(basename "${svg}")"
        produced=1
    done
    [ "${produced}" = "1" ] && echo "OK ${name}" || echo "NOOUT ${name}"
else
    status=$?
    [ "${status}" = "124" ] && echo "TIMEOUT ${name}" || echo "FAIL ${name}"
fi
WORKER_EOF
chmod +x "${WORKER}"

say "Rendering"

FILES="$(ls "${SUITE_DIR}"/*.ly)"
if [ "${LIMIT}" != "0" ]; then
    # `set -o pipefail` is on, and `head` closing the pipe early gives the writer a
    # SIGPIPE -- which under `set -e` exits the script SILENTLY, right after the
    # "Rendering" banner. Disable pipefail just for this substitution.
    FILES="$(set +o pipefail; echo "${FILES}" | head -n "${LIMIT}")"
    note "LIMIT=${LIMIT} -- rendering a subset only"
fi

TOTAL="$(echo "${FILES}" | wc -l)"
note "${TOTAL} file(s), ${PER_FILE_TIMEOUT}s timeout each"

echo "${FILES}" \
    | xargs -P "${JOBS}" -I@ "${WORKER}" @ "${OUT_DIR}" "${LILYPOND_BIN}" "${PER_FILE_TIMEOUT}" \
    > "${OUT_DIR}/render-status.txt" || true

OK="$(grep -c '^OK ' "${OUT_DIR}/render-status.txt" || true)"
FAILED="$(grep -c '^FAIL ' "${OUT_DIR}/render-status.txt" || true)"
NOOUT="$(grep -c '^NOOUT ' "${OUT_DIR}/render-status.txt" || true)"
TIMEDOUT="$(grep -c '^TIMEOUT ' "${OUT_DIR}/render-status.txt" || true)"

say "Building the manifest"

# The manifest is what gets committed: one line per output file, with a hash.
# The SVGs themselves are NOT committed -- see README.txt section 4.
{
    echo "# CodeBrix.LilyPort regression reference manifest"
    echo "# Generated by tools/regression-harness/generate-reference.sh"
    echo "# Oracle: ${VERSION}"
    echo "# Backend: svg, point-and-click disabled"
    echo "# Fonts: generic families PINNED to the oracle's own bundled faces"
    echo "#        (serif -> C059, TeX Gyre Schola; sans -> Nimbus Sans, TeX Gyre Heros;"
    echo "#         monospace -> Nimbus Mono PS, TeX Gyre Cursor), no system directory in"
    echo "#        scope -- see reference-fonts.conf.in.  A corpus generated WITHOUT that"
    echo "#        pinning records the host's default families instead and will not match."
    echo "# Font dir: ${FONT_DIR}"
    echo "# Columns: sha256 <TAB> bytes <TAB> output-file"
} > "${OUT_DIR}/manifest.tsv"

(
    cd "${OUT_DIR}/svg"
    for f in *.svg; do
        [ -e "${f}" ] || continue
        printf '%s\t%s\t%s\n' "$(sha256sum "${f}" | cut -d' ' -f1)" "$(stat -c%s "${f}")" "${f}"
    done | sort -k3,3
) >> "${OUT_DIR}/manifest.tsv"

say "Done"
note "rendered ok : ${OK}"
note "no output   : ${NOOUT}   (input produces no score -- often expected)"
note "failed      : ${FAILED}"
note "timed out   : ${TIMEDOUT}"
note "manifest    : ${OUT_DIR}/manifest.tsv ($(grep -vc '^#' "${OUT_DIR}/manifest.tsv") entries)"
note "svg output  : ${OUT_DIR}/svg ($(du -sh "${OUT_DIR}/svg" | cut -f1))"
note ""
note "Next: ./compare-output.sh to measure CodeBrix.LilyPort against this reference."
