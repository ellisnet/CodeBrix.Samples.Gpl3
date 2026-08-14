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

# MODE=svg          the pinned parity corpus: --silent, SVGs copied out, manifest
#                   written. This is the D30 corpus and its contract is fixed.
# MODE=diagnostics  the oracle's DIAGNOSTICS for the same inputs: --silent
#                   DROPPED, nothing copied out, no manifest. Writes only
#                   ${OUT_DIR}/diagnostics/<name>.log.
#
# Two modes in ONE script rather than two scripts, and the reason is D30: the
# font pinning below has to be identical on both runs, and a second script would
# be a second copy of it to keep in step. Font resolution failures ARE
# diagnostics ("cannot find font"), so a diagnostics run made without the pinning
# would record the host's font situation as though it were the oracle's.
#
# Diagnostics mode CANNOT disturb the parity corpus: it writes to a different
# directory, copies no SVG, and rewrites no manifest -- and the manifest hashes
# `.svg` only, so nothing it produces is part of what parity is measured against.
MODE="${MODE:-svg}"
case "${MODE}" in
    svg|diagnostics) ;;
    *) printf '\nERROR: MODE must be svg or diagnostics, got %s\n' "${MODE}" >&2; exit 1 ;;
esac

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
note "mode:    ${MODE}"

case "${VERSION}" in
    *2.27.2*) ;;
    *) note "WARNING: the port targets 2.27.2. Comparing against a different"
       note "         version measures version drift, not port fidelity." ;;
esac

if [ "${MODE}" = "diagnostics" ]; then
    mkdir -p "${OUT_DIR}/diagnostics"
else
    mkdir -p "${OUT_DIR}/svg" "${OUT_DIR}/logs"
fi

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
ly="$1"; out_dir="$2"; lilypond="$3"; timeout_s="$4"; mode="$5"
name="$(basename "${ly}" .ly)"
work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT

# The two modes differ in exactly two things: whether --silent is passed, and
# where the captured output lands. Everything else -- the temp working
# directory, the timeout, the flags that decide what is DRAWN -- is deliberately
# identical, so a diagnostics run describes the same rendering the parity corpus
# was made from.
#
# The working directory is a fresh mktemp per file in BOTH modes. That is not
# tidiness: a .ly file may WRITE, and giving each its own directory is what stops
# one input's output being visible to the next (the same isolation BatchDriver
# arranges on the port side).
if [ "${mode}" = "diagnostics" ]; then
    silent_flag=""
    capture="${out_dir}/diagnostics/${name}.log"
else
    silent_flag="--silent"
    capture="${out_dir}/logs/${name}.log"
fi

# A per-file timeout matters: a handful of regression inputs are deliberately
# pathological, and one hang would stall the whole run.
if timeout "${timeout_s}" "${lilypond}" \
        --formats=svg -dbackend=svg -dno-point-and-click ${silent_flag} \
        -o "${work}/${name}" "${ly}" > "${capture}" 2>&1; then
    # Diagnostics mode copies NOTHING out. The parity corpus is not its business
    # and must not be touched by it.
    if [ "${mode}" = "diagnostics" ]; then
        echo "OK ${name}"
        exit 0
    fi
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

STATUS_FILE="${OUT_DIR}/render-status.txt"
[ "${MODE}" = "diagnostics" ] && STATUS_FILE="${OUT_DIR}/diagnostics-status.txt"

echo "${FILES}" \
    | xargs -P "${JOBS}" -I@ "${WORKER}" @ "${OUT_DIR}" "${LILYPOND_BIN}" "${PER_FILE_TIMEOUT}" "${MODE}" \
    > "${STATUS_FILE}" || true

OK="$(grep -c '^OK ' "${STATUS_FILE}" || true)"
FAILED="$(grep -c '^FAIL ' "${STATUS_FILE}" || true)"
NOOUT="$(grep -c '^NOOUT ' "${STATUS_FILE}" || true)"
TIMEDOUT="$(grep -c '^TIMEOUT ' "${STATUS_FILE}" || true)"

# Diagnostics mode stops here. No manifest, because there is nothing to pin: the
# logs are regenerable in minutes and are compared per-file by
# compare-diagnostics.py, not hashed as a corpus.
if [ "${MODE}" = "diagnostics" ]; then
    # `|| true` is load-bearing under `set -euo pipefail`: grep exits 1 when it
    # matches nothing, which is the NORMAL case for a clean corpus, and that
    # would abort the script silently right before its summary -- the same trap
    # this file already documents for `head` in the LIMIT branch.
    WITH_DIAG="$( { grep -rlE '(^|:[0-9]+:[0-9]+: )(warning|error|programming error|fatal error):' "${OUT_DIR}/diagnostics" 2>/dev/null || true; } | wc -l)"
    say "Done (diagnostics)"
    note "rendered ok    : ${OK}"
    note "failed         : ${FAILED}"
    note "timed out      : ${TIMEDOUT}"
    note "logs           : ${OUT_DIR}/diagnostics ($(ls "${OUT_DIR}/diagnostics" | wc -l) files, $(du -sh "${OUT_DIR}/diagnostics" | cut -f1))"
    note "with diagnostics: ${WITH_DIAG} file(s) carry at least one warning/error line"
    note ""
    note "Next: python3 compare-diagnostics.py ${OUT_DIR}/diagnostics <merged-sweep.log>"
    exit 0
fi

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
