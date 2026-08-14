#!/usr/bin/env python3
# Copyright (c) 2026 Jeremy Ellis and contributors
#
# CodeBrix.LilyPort is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.

"""Extracts every entry point's docstring and argument list from upstream's C++.

`LY_DEFINE` carries three things the Internals Reference prints: the Scheme name,
the C++ argument list, and a docstring. The macro hands all three to
`ly_add_function_documentation` (lily/function-documentation.cc:58), which keys a
hash table by name and stores `(varlist . docstring)`; `ly:get-all-function-
documentation` returns that table, and scm/document-functions.scm turns it into
the "Scheme functions" node.

The port registers its entry points from C#, where the docstring has nowhere to
live -- so the table was empty and the node documented nothing. This recovers the
data as a committed table, the same mechanism translator-descriptions.tsv uses.

The argument list is stored the way the C PREPROCESSOR would stringify it: #ARGLIST
collapses every run of whitespace to one space, and LilyPond writes long argument
lists across two lines, so the newlines must not survive. document-functions.scm's
format-c-header then strips the SCM tokens and the parentheses out of it.

Usage:
    extract-entry-point-docs.py LILYPOND_SRC OUT_TSV

LILYPOND_SRC is the pinned read-only reference checkout (~/GitHome/lilypond).
"""

import os
import re
import subprocess
import sys

from cxxscan import collapse_whitespace, split_macro_arguments

# LY_DEFINE (FNAME, PRIMNAME, REQ, OPT, VAR, ARGLIST, DOCSTRING) and
# LY_DEFINE_WITH_SETTER (FNAME, PRIMNAME, SETTERNAME, REQ, OPT, VAR, ARGLIST,
# DOCSTRING) -- the setter form carries one extra name up front, which moves the
# two arguments this script wants along by one.
#
# The two callback macros register documentation the same way and are easy to
# miss, because they do not look like entry-point declarations: they name a C++
# member function, and the Scheme name is the THIRD argument. Their varlist is
# registered as the empty string (lily-guile-macros.hh:230), not as an argument
# list, so the manual prints the signature from Guile's own arity instead.
MACROS = {
    # name: (argument count, name index, arglist index, doc index)
    "LY_DEFINE": (7, 1, 5, 6),
    "LY_DEFINE_WITH_SETTER": (8, 1, 6, 7),
    "MAKE_DOCUMENTED_SCHEME_CALLBACK": (5, 2, None, 4),
    "MAKE_SCHEME_CALLBACK_WITH_OPTARGS": (6, 2, None, 5),
}

MACRO_RE = re.compile(
    r"^(LY_DEFINE_WITH_SETTER|LY_DEFINE|MAKE_DOCUMENTED_SCHEME_CALLBACK"
    r"|MAKE_SCHEME_CALLBACK_WITH_OPTARGS)\s*\(", re.MULTILINE)

# const char *const Context_def::type_p_name_ = "ly:context-def?";
#
# A smob's type predicate is not declared by any of the macros above: Smob_base's
# init() defines it and GENERATES its docstring from the class name
# (lily/include/smobs.tcc:136-146). Thirty-six entry points are documented that
# way and no other, so the text is generated here too, from the same two pieces.
SMOB_PREDICATE_RE = re.compile(
    r"^const char \*const (\w+)::type_p_name_\s*=\s*\"([^\"]+)\"", re.MULTILINE)

# ⚠ DECLARING type_p_name_ IS NOT THE SAME AS BEING A SMOB. The predicate is
# defined by Smob_base<T>::init(), which only runs for a class that actually
# derives from one of the smob bases, so a class that declares the name without
# deriving never gets a predicate at all. lily/include/box.hh:31-34 is exactly
# that -- `class Box' with no base and a leftover type_p_name_ -- and the oracle
# answers #f to (defined? 'ly:box?). Taking the declaration at face value put a
# procedure in the manual that upstream does not have.
# Both spellings: Duration and Tuplet_description are STRUCTs, and taking only
# `class' silently drops two live predicates.
SMOB_BASE_RE = re.compile(
    r"\b(?:class|struct)\s+(\w+)\s*:\s*([^{;]*)", re.MULTILINE)

# The oracle build has GhostScript's API off, so the two entry points behind
# #if GS_API (lily/general-scheme.cc:556) do not exist in it -- (defined?
# 'ly:gs-api) answers #f. It is the only build flag guarding an LY_DEFINE in the
# pinned tree, so it is skipped by name rather than by evaluating conditionals.
INACTIVE_GUARDS = ("GS_API",)


def guarded_spans(text):
    """Returns the [start, end) spans of #if blocks the oracle build leaves out."""
    spans = []
    depth_stack = []
    for match in re.finditer(r"^\s*#\s*(if|ifdef|ifndef|else|elif|endif)\b(.*)$",
                             text, re.MULTILINE):
        directive = match.group(1)
        if directive in ("if", "ifdef", "ifndef"):
            inactive = any(flag in match.group(2) and not match.group(2).strip().startswith("!")
                           for flag in INACTIVE_GUARDS)
            depth_stack.append(match.start() if inactive else None)
        elif directive in ("else", "elif"):
            if depth_stack and depth_stack[-1] is not None:
                spans.append((depth_stack[-1], match.start()))
                depth_stack[-1] = None
        elif directive == "endif":
            if depth_stack:
                start = depth_stack.pop()
                if start is not None:
                    spans.append((start, match.end()))

    return spans


def is_guarded(spans, index):
    """Whether an index falls inside a block the oracle build leaves out."""
    return any(start <= index < end for start, end in spans)


def smob_class_names(source_root):
    """The classes that actually DERIVE from a smob base, across headers and sources."""
    names = set()
    for folder in (os.path.join(source_root, "lily"),
                   os.path.join(source_root, "lily", "include")):
        if not os.path.isdir(folder):
            continue

        for entry in sorted(os.listdir(folder)):
            if not entry.endswith((".hh", ".cc", ".tcc")):
                continue

            with open(os.path.join(folder, entry), "r", encoding="utf-8") as handle:
                text = handle.read()

            for match in SMOB_BASE_RE.finditer(text):
                # Case-insensitively: the bases are spelled Smob<T>, Smob_base<T> and
                # Simple_smob<T>, and the last one has a LOWERCASE s.
                if "smob" in match.group(2).lower():
                    names.add(match.group(1))

    return names


def upstream_commit(source_root):
    """The reference checkout's HEAD, for the provenance header."""
    try:
        return subprocess.run(
            ["git", "-C", source_root, "rev-parse", "HEAD"],
            capture_output=True, text=True, check=True).stdout.strip()
    except (subprocess.CalledProcessError, OSError):
        return "unknown"


def escape(text):
    """TSV-safe, the way grob-interfaces.tsv escapes a description."""
    return text.replace("\\", "\\\\").replace("\n", "\\n").replace("\t", " ")


def main(argv):
    if len(argv) != 3:
        sys.stderr.write("usage: extract-entry-point-docs.py LILYPOND_SRC OUT_TSV\n")
        return 2

    source_root = argv[1]
    out_path = argv[2]
    lily = os.path.join(source_root, "lily")
    if not os.path.isdir(lily):
        sys.stderr.write("no lily/ directory under " + source_root + "\n")
        return 2

    rows = []
    failures = []
    undocumented = 0
    smob_classes = smob_class_names(source_root)
    skipped_not_a_smob = []
    skipped_guarded = []
    for name in sorted(os.listdir(lily)):
        if not name.endswith(".cc"):
            continue

        path = os.path.join(lily, name)
        with open(path, "r", encoding="utf-8") as handle:
            text = handle.read()

        spans = guarded_spans(text)

        # smobs.tcc:141-145 -- the predicate's docstring, composed from the class name.
        for match in SMOB_PREDICATE_RE.finditer(text):
            if match.group(1) not in smob_classes:
                skipped_not_a_smob.append(match.group(2))
                continue

            rows.append((
                match.group(2),
                name,
                "(SCM x)",
                escape("Is @var{x} a smob of class @code{" + match.group(1) + "}?"),
            ))

        for match in MACRO_RE.finditer(text):
            if is_guarded(spans, match.start()):
                skipped_guarded.append(name)
                continue

            expected, name_index, arglist_index, doc_index = MACROS[match.group(1)]
            arguments, _ = split_macro_arguments(text, match.end() - 1)
            if len(arguments) != expected:
                sys.stderr.write(
                    "SKIPPED a " + match.group(1) + " in " + name + ": expected "
                    + str(expected) + " arguments, found " + str(len(arguments)) + "\n")
                failures.append(name)
                continue

            scheme_name = arguments[name_index].strip()
            arglist = ("" if arglist_index is None
                       else collapse_whitespace(arguments[arglist_index]))
            doc = arguments[doc_index]

            # function-documentation.cc:61-63 returns early on an empty docstring,
            # so the entry point is simply absent from the table rather than
            # present with nothing in it.
            if doc.strip() == "":
                undocumented += 1
                continue

            rows.append((scheme_name, name, arglist, escape(doc)))

    rows.sort(key=lambda row: row[0])

    with open(out_path, "w", encoding="utf-8") as handle:
        handle.write(
            "# Entry-point documentation, extracted from the LY_DEFINE and\n"
            "# LY_DEFINE_WITH_SETTER macros of LilyPond v2.27.2\n"
            "# (commit " + upstream_commit(source_root) + "), lily/*.cc.\n"
            "#\n"
            "# Upstream registers these from the macro into a hash table\n"
            "# (lily/function-documentation.cc:58) that ly:get-all-function-documentation\n"
            "# hands to scm/document-functions.scm. The port's entry points are C#\n"
            "# lambdas with nowhere to carry a docstring, so the data is carried here and\n"
            "# registered at CreateInterpreter -- see FunctionDocumentation.cs.\n"
            "#\n"
            "# Columns: scheme-name, source, arglist, docstring.\n"
            "# The arglist is stringified as the C preprocessor would leave #ARGLIST:\n"
            "# every run of whitespace collapsed to one space. \\n is a newline.\n"
            "#\n"
            "# Regenerate with tools/table-extract/extract-entry-point-docs.py.\n")
        for row in rows:
            handle.write("\t".join(row) + "\n")

    sys.stderr.write(
        str(len(rows)) + " documented entry points written to " + out_path
        + " (" + str(undocumented) + " carry no docstring and are omitted, as"
        " upstream omits them; " + str(len(skipped_not_a_smob))
        + " type_p_name_ declaration(s) skipped for classes that derive from no smob"
        " base: " + ", ".join(sorted(skipped_not_a_smob))
        + "; " + str(len(skipped_guarded))
        + " entry point(s) skipped as build-guarded)\n")

    if failures:
        sys.stderr.write(
            "FAILED: " + str(len(failures)) + " macro(s) not parsed: "
            + ", ".join(sorted(set(failures))) + "\n")
        return 1

    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
