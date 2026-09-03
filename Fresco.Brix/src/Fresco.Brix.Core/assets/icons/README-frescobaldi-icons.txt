assets/icons -- the two window toolbars' icons
==============================================

WHERE THEY CAME FROM

  These 38 files are copied from the Frescobaldi project at commit
  cec205b9, out of
    frescobaldi/icons/Light/scalable/  ->  light/
    frescobaldi/icons/Dark/scalable/   ->  dark/

  The two sets were created FOR Frescobaldi by GitHub user
  inkandpaper-app in pull request #2067, "Icon theme dark/light for
  MacOS and Windows", merged on 2026-07-27 -- which is the commit
  above, the head this application was ported from. They are based on
  Tabler Icons, MIT-licensed, copyright (c) 2020-2026 Paweł Kuna (see
  LICENSE-TablerIcons.txt beside this file), WITH MODIFICATIONS made
  for Frescobaldi -- so the files as they stand here are conveyed
  under Frescobaldi's own GPL-2.0-or-later, and the Tabler MIT notice
  travels with them because the artwork they are drawn from is under
  it. See THIRD-PARTY-NOTICES.txt section 11.

  Upstream's own file NAMES are kept, unchanged, so a file can be
  traced back to the one it came from. A file name is not a user
  interface element, so ruling FR13 does not reach them.

  The SVGs are EmbeddedResources of Fresco.Brix.Core, read through
  the one renderer the application has (QuickInsert/SymbolIcons), so
  they are inside the assembly at run time rather than beside it. The
  FOLDER is still droppable, at BUILD time: empty it and the two
  EmbeddedResource globs match nothing, the toolbar buttons fall back
  to their short text captions, and the application builds and runs.
  The two .txt files beside the SVGs ARE copied to the output folder,
  so the licence and this notice travel with the program either way.

WARNING -- DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14)

  tools-score-wizard.svg is NOT a byte-for-byte copy in either set.
  Upstream's file is 6,474,769 bytes (Light) / 6,474,783 bytes (Dark)
  and 228,519 lines for a 24-pixel icon: the drawing is 7 <path>, 2
  <rect> and 28 <g>, and the remaining ~228,000 lines are 20,760
  ORPHAN <inkscape:path-effect> elements an Inkscape session left
  inside <defs>. Those elements are in Inkscape's own XML namespace,
  which an SVG renderer is required to ignore, so dropping them cannot
  change the drawing. tools/iconclean/iconclean.py records the exact
  command, and tests/Fresco.Brix.Core.Tests/IconThemeTests.cs renders
  upstream's file and the file here through this application's own
  renderer at 24, 48 and 96 pixels and compares every pixel.

  Written up as a bug report against Frescobaldi in
  STATUS_frescobrix_w14_2026-09-02.txt.

HOW TO REGENERATE

    python3 tools/iconclean/iconclean.py

THE FILES  (byte counts measured when this file was written)

    set    name                        upstream       here
    ----   -------------------------   ----------   --------
    light  document-new.svg                  4562       4562
    light  document-open.svg                  551        551
    light  document-save.svg                  530        530
    light  document-close.svg                1714       1714
    light  go-previous.svg                    433        433
    light  go-next.svg                        436        436
    light  edit-undo.svg                      422        422
    light  edit-redo.svg                      422        422
    light  tools-score-wizard.svg         6474769       2761   <- cleaned (FR14)
    light  lilypond-run.svg                  2698       2698
    light  lilypond-stop.svg                 2490       2490
    light  zoom-in.svg                       2183       2183
    light  zoom-out.svg                      1974       1974
    light  zoom-magnifier.svg                1798       1798
    light  edit-clear.svg                    3472       3472
    light  help-contents.svg                 1741       1741
    light  reload.svg                         453        453
    light  rotate-left.svg                    408        408
    light  rotate-right.svg                   406        406
    dark   document-new.svg                  4596       4596
    dark   document-open.svg                  546        546
    dark   document-save.svg                  525        525
    dark   document-close.svg                1709       1709
    dark   go-previous.svg                   1551       1551
    dark   go-next.svg                       1550       1550
    dark   edit-undo.svg                     1472       1472
    dark   edit-redo.svg                     1479       1479
    dark   tools-score-wizard.svg         6474783       2742   <- cleaned (FR14)
    dark   lilypond-run.svg                  2641       2641
    dark   lilypond-stop.svg                 2524       2524
    dark   zoom-in.svg                       2305       2305
    dark   zoom-out.svg                      2080       2080
    dark   zoom-magnifier.svg                1873       1873
    dark   edit-clear.svg                    3536       3536
    dark   help-contents.svg                 1736       1736
    dark   reload.svg                         448        448
    dark   rotate-left.svg                   1394       1394
    dark   rotate-right.svg                  1393       1393
