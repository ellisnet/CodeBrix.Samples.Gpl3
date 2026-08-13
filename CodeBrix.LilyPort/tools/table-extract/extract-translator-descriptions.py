#!/usr/bin/env python3
# Copyright (c) 2026 Jeremy Ellis and contributors
#
# CodeBrix.LilyPort is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.

"""Extracts every translator's Internals-Reference metadata from upstream's C++.

Upstream builds the alist `ly:translator-description` answers inside
`Translator::static_translator_description` (lily/translator.cc:126) out of two
sources that only exist at C++ compile time:

  * the four text blocks of the `ADD_TRANSLATOR` macro -- doc, create, read,
    write -- which become `description`, `grobs-created`, `properties-read` and
    `properties-written`; and
  * the translator's listener declarations, which become `events-accepted`.
    `ADD_LISTENER (tie)` names a C++ identifier, and `event_class_symbol`
    (translator.cc:112) turns it into a symbol by replacing underscores with
    hyphens and appending "-event", so `tie` documents as `tie-event`.

None of that survives into the port, whose translators are C# classes: every one
of the 126 registers an EMPTY description (TranslatorCreator.Add), so the whole
Translation node of the Internals Reference is missing its text. This script
recovers the data as a committed table, the same way grob-interfaces.tsv already
recovers the ADD_INTERFACE data.

The output is a TSV, one row per translator:

    name <TAB> source <TAB> grobs-created <TAB> properties-read
         <TAB> properties-written <TAB> events-accepted <TAB> description

Symbol lists are space separated and written in the order upstream declares them;
newlines inside a description are escaped as \\n, matching grob-interfaces.tsv.
Order is not load-bearing for the generated documentation -- documentation-lib's
list-xref-symbols sorts, and document-translation sorts the property lists -- but
it is preserved anyway so the table reads as what upstream wrote.

Usage:
    extract-translator-descriptions.py LILYPOND_SRC OUT_TSV

LILYPOND_SRC is the pinned read-only reference checkout (~/GitHome/lilypond).
Nothing in the build reads it; this script is run by hand when the reference
moves, and its output is committed.
"""

import os
import re
import subprocess
import sys

from cxxscan import split_macro_arguments

# void Name::boot () { ... }, which is where the listener macros sit.
BOOT_RE = re.compile(r"^(\w+)::boot\s*\(\)\s*\{(.*?)^\}", re.MULTILINE | re.DOTALL)

LISTENER_RES = [
    re.compile(r"ADD_LISTENER\s*\(\s*(\w+)\s*\)"),
    re.compile(r"ADD_LISTENER_FOR\s*\(\s*\w+\s*,\s*(\w+)\s*\)"),
    re.compile(r"ADD_DELEGATE_LISTENER\s*\(\s*(\w+)\s*\)"),
    re.compile(r"ADD_DELEGATE_LISTENER_FOR\s*\(\s*\w+\s*,\s*\w+\s*,\s*(\w+)\s*\)"),
]


def event_class_symbol(identifier):
    """translator.cc:112 -- underscores to hyphens, then "-event"."""
    return identifier.replace("_", "-") + "-event"


def symbol_list(block):
    """The macro's text blocks are whitespace-separated symbol lists."""
    return block.split()


def listeners_by_translator(text):
    """Maps each translator named by a boot() in this file to its event classes.

    The association has to go through boot() rather than through the file,
    because several files define more than one translator and the listeners of
    one must not be attributed to another.
    """
    found = {}
    for match in BOOT_RE.finditer(text):
        name = match.group(1)
        body = match.group(2)
        events = []
        for pattern in LISTENER_RES:
            for listener in pattern.finditer(body):
                event = event_class_symbol(listener.group(1))
                if event not in events:
                    events.append(event)

        found[name] = events

    return found


def upstream_commit(source_root):
    """The reference checkout's HEAD, for the provenance header."""
    try:
        return subprocess.run(
            ["git", "-C", source_root, "rev-parse", "HEAD"],
            capture_output=True, text=True, check=True).stdout.strip()
    except (subprocess.CalledProcessError, OSError):
        return "unknown"


def main(argv):
    if len(argv) != 3:
        sys.stderr.write(
            "usage: extract-translator-descriptions.py LILYPOND_SRC OUT_TSV\n")
        return 2

    source_root = argv[1]
    out_path = argv[2]
    lily = os.path.join(source_root, "lily")
    if not os.path.isdir(lily):
        sys.stderr.write("no lily/ directory under " + source_root + "\n")
        return 2

    rows = []
    failures = []
    for name in sorted(os.listdir(lily)):
        if not name.endswith(".cc"):
            continue

        path = os.path.join(lily, name)
        with open(path, "r", encoding="utf-8") as handle:
            text = handle.read()

        if "ADD_TRANSLATOR" not in text:
            continue

        listeners = listeners_by_translator(text)
        for match in re.finditer(r"^ADD_TRANSLATOR\s*\(", text, re.MULTILINE):
            arguments, _ = split_macro_arguments(text, match.end() - 1)
            if len(arguments) != 5:
                sys.stderr.write(
                    "SKIPPED a translator in " + name + ": expected a name and four"
                    " text blocks, found " + str(len(arguments)) + " arguments\n")
                failures.append(name)
                continue

            translator = arguments[0].strip()
            doc, create, read, write = arguments[1:]
            rows.append((
                translator,
                name,
                " ".join(symbol_list(create)),
                " ".join(symbol_list(read)),
                " ".join(symbol_list(write)),
                " ".join(listeners.get(translator, [])),
                doc.strip().replace("\\", "\\\\").replace("\n", "\\n").replace(
                    "\t", " "),
            ))

    rows.sort(key=lambda row: row[0])

    with open(out_path, "w", encoding="utf-8") as handle:
        handle.write(
            "# Translator documentation metadata, extracted from the ADD_TRANSLATOR\n"
            "# macros and boot() listener declarations of LilyPond v2.27.2\n"
            "# (commit " + upstream_commit(source_root) + "), lily/*.cc.\n"
            "#\n"
            "# Upstream assembles this alist in Translator::static_translator_description\n"
            "# (lily/translator.cc:126) from data that exists only at C++ compile time.\n"
            "# The port's translators are C# classes, so the data is carried here and\n"
            "# registered at CreateInterpreter -- see TranslatorDescriptionTable.cs.\n"
            "#\n"
            "# Columns: name, source, grobs-created, properties-read,\n"
            "#          properties-written, events-accepted, description.\n"
            "# Symbol lists are space separated; \\n in a description is a newline.\n"
            "#\n"
            "# Regenerate with tools/table-extract/extract-translator-descriptions.py.\n")
        for row in rows:
            handle.write("\t".join(row) + "\n")

    sys.stderr.write(str(len(rows)) + " translators written to " + out_path + "\n")

    # A partial table is worse than none: the missing translators would document
    # as blank and nothing downstream would say why. Fail rather than truncate.
    if failures:
        sys.stderr.write(
            "FAILED: " + str(len(failures)) + " ADD_TRANSLATOR block(s) not parsed: "
            + ", ".join(sorted(set(failures))) + "\n")
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
