tools/iconclean -- the toolbar icons
====================================

This SHIPS NOTHING. It is not in the solution, no build, test or pack step runs
it, and no such step names the read-only Frescobaldi checkout it reads (board
rule 3). Run it by hand when the icon set changes.


THE FILES

  iconclean.py    the tool. Copies the icons the two window toolbars reference
                  out of frescobaldi/icons/{Light,Dark}/scalable/ into
                    src/Fresco.Brix.Core/assets/icons/{light,dark}/
                  byte for byte -- except the one file ruling FR14 says to
                  clean -- and then writes, from what it actually put there:
                    src/Fresco.Brix.Core/assets/icons/README-frescobaldi-icons.txt
                    tests/Fresco.Brix.Core.Tests/fixtures/icons/
                  --check reports without writing anything.


THE USUAL RUN

    python3 iconclean.py --check        # what would change
    python3 iconclean.py                # copy, clean, and re-record


WHAT IT NEEDS

  /usr/bin/inkscape, for the one cleaned file. Nothing else outside the
  standard library. If Inkscape is not there the tool stops rather than
  shipping a differently-cleaned file, because the point of doing the clean
  with a recorded command is that anyone can reproduce it.


THE ONE CLEANED FILE  (ruling FR14; see THIRD-PARTY-NOTICES.txt section 11.3)

  tools-score-wizard.svg is 6,474,769 bytes (Light) and 6,474,783 (Dark) for a
  24-pixel icon. 20,760 orphan <inkscape:path-effect> elements sit in its
  <defs>; the drawing is 7 <path>, 2 <rect> and 28 <g>. The clean is

    1. inkscape --export-plain-svg --vacuum-defs
    2. a stdlib pass that DELETES groups with no children

  and nothing else -- no re-parenting, no transform composition, no path
  rewriting. Both steps only remove things an SVG renderer must already ignore
  or that draw nothing, so the result cannot differ.

  It is proven as well as argued. The tool records upstream's original,
  gzip-compressed, in tests/Fresco.Brix.Core.Tests/fixtures/icons/, and
  IconThemeTests renders the recorded original and the shipped file through the
  APPLICATION'S OWN renderer at 24, 48 and 96 pixels and asserts that every
  pixel agrees. The fixture is recorded rather than read from the checkout
  because a test may not name the checkout's path.


IF THE CLEAN EVER STOPS BEING PIXEL-IDENTICAL

  Do not ship it. Ruling FR16's fallback is upstream's own
  icons/TangoExt/scalable/tools-score-wizard.svg (GPL-2.0, based on
  Action_wizard from KDE's Crystal and document_new from Tango) for that one
  button -- coloured art, so it would be drawn untinted -- and the STATUS file
  of the wave that finds it says so.
