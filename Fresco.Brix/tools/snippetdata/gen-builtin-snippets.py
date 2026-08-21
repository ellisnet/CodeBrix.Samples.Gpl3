#!/usr/bin/env python3
# Copyright (c) 2026 Jeremy Ellis and contributors
#
# Fresco.Brix is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.
#
# Regenerates src/Fresco.Brix.Core/Snippets/BuiltinSnippets.g.cs from
# Frescobaldi's own snippet/builtin.py.
#
# The snippets whose text begins with a "-*- python;" line are LEFT OUT: ruling
# FR5.3 excludes snippet Python-code execution along with the extensions
# system. Their names are written into the generated file as a comment so the
# omission is visible where it matters, and FD10 tracks them.
#
# The reference checkout is READ ONLY and is touched only when this tool is run
# by hand; nothing in the build reaches it (standing rule 3).
#
#   python3 tools/snippetdata/gen-builtin-snippets.py [path-to-frescobaldi]

import os
import sys

DEFAULT_SOURCE = os.path.expanduser("~/GitHome/frescobaldi/frescobaldi")
OUTPUT = os.path.join(
    os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))),
    "src", "Fresco.Brix.Core", "Snippets", "BuiltinSnippets.g.cs")

HEADER = '''// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// GENERATED FILE - do not edit by hand.
// Regenerate with: python3 tools/snippetdata/gen-builtin-snippets.py
//
//was previously: frescobaldi/snippet/builtin.py
//
// The titles are the VERBATIM upstream msgids, so the i18n harvest tool
// (W-I18N) can map them to Frescobaldi's own catalogs.

using System.Collections.Generic;

namespace Fresco.Brix.Snippets;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

public static partial class BuiltinSnippets
{
'''


# Two snippet TITLES name LilyPond, and a snippet's title is what the Snippets
# menu and the snippet list SHOW - which makes it chrome, and ruling FR13 says
# no chrome names LilyPond. The titles are renamed here; the snippets' TEXT is
# left alone, because that is document content the user engraves (and one of
# them calls the engine's own #(lilypond-version), which must keep its name).
#
# W-I18N consequence: these msgids are no longer Frescobaldi's, so the harvest
# tool cannot map them to upstream's catalogs. They belong in its renamed-string
# table with the rest of FR13's divergences.
RENAMED_TITLES = {
    "LilyPond Version": "LilyPort Version",
    "Tagline with date and LilyPond version":
        "Tagline with date and LilyPort version",
}


def csharp_string(text):
    return '"' + text.replace('\\', '\\\\').replace('"', '\\"') \
                     .replace('\r', '').replace('\n', '\\n') + '"'


def main():
    source = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_SOURCE
    if not source.endswith("snippet"):
        source = os.path.join(source, "snippet")
    src = open(os.path.join(source, "builtin.py"), encoding="utf-8").read()
    src = src.replace("import builtins\n", "")
    src = src.replace("_ = lambda *args: lambda: builtins._(*args)",
                      "_ = lambda *args: (lambda: args[-1])")
    namespace = {}
    exec(compile(src, "builtin.py", "exec"), namespace)
    snippets = namespace["builtin_snippets"]

    kept = []
    dropped = []
    for name in sorted(snippets):
        template = snippets[name]
        first = template.text.lstrip("\n").split("\n", 1)[0]
        if first.startswith("-*- ") and "python" in first:
            dropped.append(name)
            continue
        title = template.title() if template.title else ""
        title = RENAMED_TITLES.get(title, title)
        kept.append((name, title, template.text))

    with open(OUTPUT, "w", encoding="utf-8") as out:
        out.write(HEADER)
        out.write("    /// <summary>\n")
        out.write("    /// The {} snippets upstream ships that are TEMPLATES.\n"
                  .format(len(kept)))
        out.write("    /// </summary>\n")
        out.write("    /// <remarks>\n")
        out.write("    /// Upstream ships {} altogether; the {} left out run\n"
                  .format(len(snippets), len(dropped)))
        out.write("    /// Python code, which FR5.3 excludes:\n")
        for i in range(0, len(dropped), 4):
            out.write("    /// {}\n".format(", ".join(dropped[i:i + 4])))
        out.write("    /// </remarks>\n")
        out.write("    private static readonly "
                  "IReadOnlyList<BuiltinSnippet> Data = new[]\n    {\n")
        for name, title, text in kept:
            out.write("        new BuiltinSnippet(\n")
            out.write("            {},\n".format(csharp_string(name)))
            out.write("            {},\n".format(csharp_string(title)))
            out.write("            {}),\n".format(csharp_string(text)))
        out.write("    };\n}\n")

    print("wrote {} snippets ({} python snippets left out: {}) to {}"
          .format(len(kept), len(dropped), ", ".join(dropped), OUTPUT))


if __name__ == "__main__":
    main()
