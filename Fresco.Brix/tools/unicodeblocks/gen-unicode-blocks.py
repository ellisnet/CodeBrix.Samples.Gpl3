#!/usr/bin/env python3
# Copyright (c) 2026 Jeremy Ellis and contributors
#
# Fresco.Brix is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# Regenerates src/Fresco.Brix.Core/Editor/UnicodeBlocks.g.cs from Frescobaldi's
# own unicode_blocks.py, which carries the Unicode Character Database's
# Blocks.txt verbatim.
#
# The reference checkout is READ ONLY and is touched only when this tool is
# run by hand; nothing in the build reaches it (standing rule 3).
#
#   python3 tools/unicodeblocks/gen-unicode-blocks.py [path-to-frescobaldi]

import os
import re
import sys

DEFAULT_SOURCE = os.path.expanduser(
    "~/GitHome/frescobaldi/frescobaldi/unicode_blocks.py")
OUTPUT = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "src", "Fresco.Brix.Core", "Editor", "UnicodeBlocks.g.cs")

HEADER = '''// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// GENERATED FILE - do not edit by hand.
// Regenerate with: python3 tools/unicodeblocks/gen-unicode-blocks.py
//
//was previously: frescobaldi/unicode_blocks.py, whose block_data string is the
// Unicode Character Database's Blocks.txt verbatim.

using System.Collections.Generic;

namespace Fresco.Brix.Editor;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

public static partial class UnicodeBlocks
{
    /// <summary>The blocks, in code-point order.</summary>
    private static readonly IReadOnlyList<UnicodeBlock> Data = new[]
    {
'''

FOOTER = '''    };
}
'''


def main():
    source = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_SOURCE
    if os.path.isdir(source):
        source = os.path.join(source, "frescobaldi", "unicode_blocks.py")
    text = open(source, encoding="utf-8").read()
    match = re.search(r'block_data\s*=\s*r?"""(.*?)"""', text, re.S)
    if not match:
        raise SystemExit("block_data not found in " + source)

    rows = []
    for line in match.group(1).splitlines():
        line = line.strip()
        if not line or line.startswith("#"):
            continue
        try:
            range_, name = line.split(";", 1)
            start, end = range_.split("..", 1)
            rows.append((int(start, 16), int(end, 16), name.strip()))
        except ValueError:
            continue

    rows.sort()
    with open(OUTPUT, "w", encoding="utf-8") as out:
        out.write(HEADER)
        for start, end, name in rows:
            out.write('        new UnicodeBlock(0x{:04X}, 0x{:04X}, "{}"),\n'
                      .format(start, end, name.replace('"', '\\"')))
        out.write(FOOTER)
    print("wrote {} blocks to {}".format(len(rows), OUTPUT))


if __name__ == "__main__":
    main()
