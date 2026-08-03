#!/bin/bash
#
# compare-output.sh -- measure CodeBrix.LilyPort against the LilyPond reference.
#
# This file is part of CodeBrix.LilyPort.
# Copyright (c) 2026 Jeremy Ellis and contributors
#
# CodeBrix.LilyPort is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REFERENCE_DIR="${1:-${SCRIPT_DIR}/reference/svg}"
CANDIDATE_DIR="${2:-${SCRIPT_DIR}/candidate/svg}"
TOLERANCE="${TOLERANCE:-0.01}"

if [ ! -d "${REFERENCE_DIR}" ]; then
    echo "No reference at ${REFERENCE_DIR}." >&2
    echo "Run ./generate-reference.sh first -- see README.txt section 3." >&2
    exit 2
fi

if [ ! -d "${CANDIDATE_DIR}" ]; then
    echo "No candidate output at ${CANDIDATE_DIR}." >&2
    echo "CodeBrix.LilyPort cannot engrave yet; this becomes useful during milestone 6." >&2
    exit 2
fi

exec python3 "${SCRIPT_DIR}/compare-output.py" \
    "${REFERENCE_DIR}" "${CANDIDATE_DIR}" --tolerance "${TOLERANCE}"
