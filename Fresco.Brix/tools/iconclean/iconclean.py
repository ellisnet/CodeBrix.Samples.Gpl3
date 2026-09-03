#!/usr/bin/env python3
# Copyright (c) 2026 Jeremy Ellis and contributors
#
# Fresco.Brix is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.

"""Copies the toolbar icons out of the Frescobaldi checkout, cleaning the one
file that needs it.

This tool SHIPS NOTHING. It is not in the solution, no build, test or pack step
runs it, and no such step names the read-only Frescobaldi checkout it reads
(board rule 3). Run it by hand when the icon set changes.

WHAT IT DOES

  For each of the icon names the two window toolbars reference, it copies
  frescobaldi/icons/{Light,Dark}/scalable/<name>.svg into
  src/Fresco.Brix.Core/assets/icons/{light,dark}/<name>.svg BYTE FOR BYTE --
  except tools-score-wizard.svg, which is CLEANED (see below) -- and then
  writes assets/icons/README-frescobaldi-icons.txt from what it actually put
  there, so the file list and the byte counts in that document are measured
  rather than typed.

THE ONE CLEANED FILE  (ruling FR14)

  Upstream's tools-score-wizard.svg is 6,474,769 bytes / 228,519 lines in the
  Light set and 6,474,783 / 228,519 in the Dark set, for a 24-pixel icon. The
  drawing is 7 <path>, 2 <rect> and 28 <g>; the other 228,000 lines are 20,760
  ORPHAN <inkscape:path-effect> elements inside <defs>, which an Inkscape
  session accumulated and never dropped. They are Inkscape's own namespace:
  an SVG renderer is required to ignore them, so removing them cannot change
  the drawing.

  The clean is two steps, in this order:

    1.  inkscape --export-plain-svg --vacuum-defs --export-filename=<out> <in>
        Inkscape's own "plain SVG" writer, with its unused-definition sweep.
        This is what removes the <defs> and every inkscape:/sodipodi: attribute
        and element.

    2.  a python stdlib pass (drop_empty_groups below) that removes <g>
        elements with no children. A group with nothing in it draws nothing,
        so this too cannot change the drawing. Nothing is re-parented and no
        transform is composed -- the pass only deletes.

  Both steps are drawing-preserving BY CONSTRUCTION, and
  tests/Fresco.Brix.Core.Tests/IconThemeTests.cs proves it empirically as well:
  it renders the upstream file and the shipped file through the application's
  OWN renderer at 24, 48 and 96 pixels and compares every pixel.

USAGE

    python3 iconclean.py            # copy and clean into src/
    python3 iconclean.py --check    # report what would change, write nothing
"""

import gzip
import os
import re
import subprocess
import sys
import tempfile

# The read-only reference checkout (board rule 3: porting reads it, builds
# never do).
UPSTREAM = os.path.expanduser("~/GitHome/frescobaldi/frescobaldi/icons")
UPSTREAM_COMMIT = "cec205b9"

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
TARGET = os.path.join(REPO, "src", "Fresco.Brix.Core", "assets", "icons")
FIXTURES = os.path.join(
    REPO, "tests", "Fresco.Brix.Core.Tests", "fixtures", "icons")

INKSCAPE = "/usr/bin/inkscape"

# The icon names the two window toolbars reference, in the order the bars use
# them. Every one of these must exist in BOTH sets; IconThemeTests asserts it.
#
#   Main Toolbar        document-new, document-open, document-save,
#                       document-close, go-previous, go-next, edit-undo,
#                       edit-redo, tools-score-wizard, lilypond-run,
#                       lilypond-stop
#   Music View Toolbar  zoom-in, zoom-out, zoom-magnifier, go-previous,
#                       go-next, edit-clear
#   Manuscript Viewer   document-open, document-close, zoom-in, zoom-out,
#   (its PANEL toolbar; zoom-magnifier, go-previous, go-next, and four that
#    board wave W15)     nothing else referenced: help-contents, reload,
#                        rotate-left, rotate-right
ICONS = [
    "document-new",
    "document-open",
    "document-save",
    "document-close",
    "go-previous",
    "go-next",
    "edit-undo",
    "edit-redo",
    "tools-score-wizard",
    "lilypond-run",
    "lilypond-stop",
    "zoom-in",
    "zoom-out",
    "zoom-magnifier",
    "edit-clear",
    "help-contents",
    "reload",
    "rotate-left",
    "rotate-right",
]

# The one file ruling FR14 says to fix rather than ship as it stands.
CLEANED = "tools-score-wizard"

SETS = [("Light", "light"), ("Dark", "dark")]


def drop_empty_groups(text):
    """Removes <g ...></g> and <g ... /> elements that hold nothing.

    Only deletions; nothing is re-parented and no transform is composed. A
    group with no children draws nothing, so the rendered result is unchanged.
    """
    empty_self_closing = re.compile(r"<g\b[^>]*/>", re.DOTALL)
    empty_pair = re.compile(r"<g\b[^>]*(?<!/)>\s*</g>", re.DOTALL)
    while True:
        after = empty_self_closing.sub("", text)
        after = empty_pair.sub("", after)
        if after == text:
            return text
        text = after


def clean(source):
    """Answers the cleaned bytes of one SVG file."""
    if not os.path.exists(INKSCAPE):
        raise SystemExit(
            "inkscape is not at %s; the clean cannot be reproduced" % INKSCAPE)

    with tempfile.TemporaryDirectory() as work:
        plain = os.path.join(work, "plain.svg")
        command = [
            INKSCAPE,
            "--export-plain-svg",
            "--vacuum-defs",
            "--export-filename=" + plain,
            source,
        ]
        subprocess.run(command, check=True, stdout=subprocess.DEVNULL,
                       stderr=subprocess.DEVNULL)
        with open(plain, encoding="utf-8") as handle:
            text = handle.read()

    return drop_empty_groups(text)


def read(path):
    with open(path, "rb") as handle:
        return handle.read()


def main(argv):
    check = "--check" in argv

    if not os.path.isdir(UPSTREAM):
        raise SystemExit("no Frescobaldi checkout at " + UPSTREAM)

    produced = []
    changed = 0
    for upstream_set, ours in SETS:
        folder = os.path.join(TARGET, ours)
        if not check:
            os.makedirs(folder, exist_ok=True)

        for name in ICONS:
            source = os.path.join(UPSTREAM, upstream_set, "scalable", name + ".svg")
            if not os.path.exists(source):
                raise SystemExit("missing upstream icon: " + source)

            original = read(source)
            if name == CLEANED:
                wanted = clean(source).encode("utf-8")
            else:
                wanted = original

            destination = os.path.join(folder, name + ".svg")
            existing = read(destination) if os.path.exists(destination) else None
            if existing != wanted:
                changed += 1
                if not check:
                    with open(destination, "wb") as handle:
                        handle.write(wanted)

            produced.append((ours, name, len(original), len(wanted)))

    print("%d files, %d %s" % (
        len(produced), changed, "would change" if check else "written"))

    if not check:
        write_readme(produced)
        write_fixtures()

    for ours, name, before, after in produced:
        if before != after:
            print("  cleaned %s/%s.svg: %d -> %d bytes (%.4f%%)"
                  % (ours, name, before, after, 100.0 * after / before))

    return 0


def write_fixtures():
    """Records upstream's uncleaned wizard icon as a test fixture.

    Board rule 3 forbids any TEST from naming the read-only Frescobaldi
    checkout, and ruling FR14's second obligation says the fixture stays as
    recorded. So the ORIGINAL bytes are recorded here, gzip-compressed because
    6.4 megabytes of repeated Inkscape definitions compress to about 140
    kilobytes, and IconThemeTests renders the recorded original and the shipped
    file through the application's own renderer and compares the pixels.
    """
    os.makedirs(FIXTURES, exist_ok=True)
    for upstream_set, ours in SETS:
        source = os.path.join(
            UPSTREAM, upstream_set, "scalable", CLEANED + ".svg")
        destination = os.path.join(
            FIXTURES, "%s.%s.upstream.svg.gz" % (CLEANED, ours))
        with open(source, "rb") as handle:
            data = handle.read()

        with gzip.GzipFile(destination, "wb", compresslevel=9, mtime=0) as out:
            out.write(data)

        print("recorded %s (%d -> %d bytes)"
              % (destination, len(data), os.path.getsize(destination)))

    notice = os.path.join(FIXTURES, "README.txt")
    with open(notice, "w", encoding="utf-8") as handle:
        handle.write(
            "fixtures/icons -- ruling FR14's recorded original\n"
            "=================================================\n"
            "\n"
            "tools-score-wizard.{light,dark}.upstream.svg.gz are Frescobaldi's\n"
            "OWN tools-score-wizard.svg from commit %s, byte for byte,\n"
            "gzip-compressed. They are 6,474,769 and 6,474,783 bytes\n"
            "uncompressed: 228,519 lines each, of which 20,760 are orphan\n"
            "<inkscape:path-effect> elements inside <defs>, for a 24-pixel icon.\n"
            "\n"
            "The application ships a CLEANED copy of each (ruling FR14; see\n"
            "src/Fresco.Brix.Core/assets/icons/README-frescobaldi-icons.txt).\n"
            "IconThemeTests renders the recorded original and the shipped file\n"
            "through the application's own renderer at 24, 48 and 96 pixels and\n"
            "asserts that every pixel agrees -- which is what makes the clean a\n"
            "size fix and not a redraw.\n"
            "\n"
            "Recorded by tools/iconclean/iconclean.py, which ships nothing and\n"
            "is not in the solution. The fixture is here, and not read from the\n"
            "Frescobaldi checkout at test time, because no build or test step\n"
            "may name that checkout's path.\n" % UPSTREAM_COMMIT)
    print("wrote " + notice)


def write_readme(produced):
    """Writes the notice that travels with the icons, from what was written."""
    lines = []
    lines.append("assets/icons -- the two window toolbars' icons")
    lines.append("=" * 46)
    lines.append("")
    lines.append("WHERE THEY CAME FROM")
    lines.append("")
    lines.append("  These %d files are copied from the Frescobaldi project at commit"
                 % len(produced))
    lines.append("  %s, out of" % UPSTREAM_COMMIT)
    lines.append("    frescobaldi/icons/Light/scalable/  ->  light/")
    lines.append("    frescobaldi/icons/Dark/scalable/   ->  dark/")
    lines.append("")
    lines.append("  The two sets were created FOR Frescobaldi by GitHub user")
    lines.append("  inkandpaper-app in pull request #2067, \"Icon theme dark/light for")
    lines.append("  MacOS and Windows\", merged on 2026-07-27 -- which is the commit")
    lines.append("  above, the head this application was ported from. They are based on")
    lines.append("  Tabler Icons, MIT-licensed, copyright (c) 2020-2026 Paweł Kuna (see")
    lines.append("  LICENSE-TablerIcons.txt beside this file), WITH MODIFICATIONS made")
    lines.append("  for Frescobaldi -- so the files as they stand here are conveyed")
    lines.append("  under Frescobaldi's own GPL-2.0-or-later, and the Tabler MIT notice")
    lines.append("  travels with them because the artwork they are drawn from is under")
    lines.append("  it. See THIRD-PARTY-NOTICES.txt section 11.")
    lines.append("")
    lines.append("  Upstream's own file NAMES are kept, unchanged, so a file can be")
    lines.append("  traced back to the one it came from. A file name is not a user")
    lines.append("  interface element, so ruling FR13 does not reach them.")
    lines.append("")
    lines.append("  The SVGs are EmbeddedResources of Fresco.Brix.Core, read through")
    lines.append("  the one renderer the application has (QuickInsert/SymbolIcons), so")
    lines.append("  they are inside the assembly at run time rather than beside it. The")
    lines.append("  FOLDER is still droppable, at BUILD time: empty it and the two")
    lines.append("  EmbeddedResource globs match nothing, the toolbar buttons fall back")
    lines.append("  to their short text captions, and the application builds and runs.")
    lines.append("  The two .txt files beside the SVGs ARE copied to the output folder,")
    lines.append("  so the licence and this notice travel with the program either way.")
    lines.append("")
    lines.append("WARNING -- DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14)")
    lines.append("")
    lines.append("  tools-score-wizard.svg is NOT a byte-for-byte copy in either set.")
    lines.append("  Upstream's file is 6,474,769 bytes (Light) / 6,474,783 bytes (Dark)")
    lines.append("  and 228,519 lines for a 24-pixel icon: the drawing is 7 <path>, 2")
    lines.append("  <rect> and 28 <g>, and the remaining ~228,000 lines are 20,760")
    lines.append("  ORPHAN <inkscape:path-effect> elements an Inkscape session left")
    lines.append("  inside <defs>. Those elements are in Inkscape's own XML namespace,")
    lines.append("  which an SVG renderer is required to ignore, so dropping them cannot")
    lines.append("  change the drawing. tools/iconclean/iconclean.py records the exact")
    lines.append("  command, and tests/Fresco.Brix.Core.Tests/IconThemeTests.cs renders")
    lines.append("  upstream's file and the file here through this application's own")
    lines.append("  renderer at 24, 48 and 96 pixels and compares every pixel.")
    lines.append("")
    lines.append("  Written up as a bug report against Frescobaldi in")
    lines.append("  STATUS_frescobrix_w14_2026-09-02.txt.")
    lines.append("")
    lines.append("HOW TO REGENERATE")
    lines.append("")
    lines.append("    python3 tools/iconclean/iconclean.py")
    lines.append("")
    lines.append("THE FILES  (byte counts measured when this file was written)")
    lines.append("")
    lines.append("    set    name                        upstream       here")
    lines.append("    ----   -------------------------   ----------   --------")
    for ours, name, before, after in produced:
        mark = "   <- cleaned (FR14)" if before != after else ""
        lines.append("    %-5s  %-25s   %10d   %8d%s"
                     % (ours, name + ".svg", before, after, mark))
    lines.append("")

    path = os.path.join(TARGET, "README-frescobaldi-icons.txt")
    with open(path, "w", encoding="utf-8") as handle:
        handle.write("\n".join(lines))
    print("wrote " + path)


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
