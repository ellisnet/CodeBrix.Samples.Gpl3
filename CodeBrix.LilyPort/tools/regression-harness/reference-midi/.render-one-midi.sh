#!/bin/bash
set -uo pipefail
ly="$1"; out_dir="$2"; lilypond="$3"; timeout_s="$4"
name="$(basename "${ly}" .ly)"
work="$(mktemp -d)"
trap 'rm -rf "${work}"' EXIT

# --formats=midi asks for MIDI and nothing else: no pages are rendered, which is
# what makes this run fast enough to be worth having as its own step.
if timeout "${timeout_s}" "${lilypond}" \
        --formats=midi -dno-point-and-click --silent \
        -o "${work}/${name}" "${ly}" > "${out_dir}/logs/${name}.log" 2>&1; then
    produced=0
    for midi in "${work}/${name}"*.midi; do
        [ -e "${midi}" ] || continue
        cp "${midi}" "${out_dir}/midi/$(basename "${midi}")"
        produced=1
    done
    [ "${produced}" = "1" ] && echo "OK ${name}" || echo "NOOUT ${name}"
else
    status=$?
    [ "${status}" = "124" ] && echo "TIMEOUT ${name}" || echo "FAIL ${name}"
fi
