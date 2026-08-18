================================================================================
BUILDING THE EMMENTALER MUSIC FONTS FOR CodeBrix.LilyPort
================================================================================

This directory contains everything needed to rebuild the Emmentaler music fonts
from the Metafont sources in CodeBrix.LilyPort/mf, on a clean Debian-based
Linux machine, without a LilyPond source tree and without autoconf.

It is written to be followed cold, a year from now, by someone who has done
none of this before.

    Written:            2026-08-02
    Font sources from:  LilyPond 2.27.2
                        (git tag v2.27.2, commit
                        2d621459bd44cb1758f822a69757242eab843060)
    Verified on:        LMDE 7 (gigi), x86_64

--------------------------------------------------------------------------------
CONTENTS
--------------------------------------------------------------------------------

  1.  Why we build our own fonts
  2.  What you must install first
  3.  How to build
  4.  What the pipeline actually does
  5.  What gets produced
  6.  Verifying against an official LilyPond release
  7.  Results from the 2026-08-02 verification run
  8.  Licensing -- read before modifying anything in mf/
  9.  File inventory of this directory
  10. Troubleshooting
  11. Updating to a newer LilyPond release

================================================================================
1.  WHY WE BUILD OUR OWN FONTS
================================================================================

CodeBrix.LilyPort ships the Emmentaler music fonts. There were two ways to get
them: copy the .otf files out of an official LilyPond release, or build them
from the Metafont sources ourselves.

We build them ourselves. The fonts are then genuinely ours, with provenance
verifiable end to end from Metafont source to shipped binary, and we are not
pinned to somebody else's release artifact. The comparison step in section 6
additionally gives us a correctness check that simply copying a binary never
would.

This is a ONE-TIME build whose OUTPUTS are committed to the repository. It is
NOT part of the normal `dotnet build`. Nobody needs the toolchain below in
order to build or use CodeBrix.LilyPort -- only to regenerate the fonts, which
happens when we move to a new LilyPond version and essentially never otherwise.

================================================================================
2.  WHAT YOU MUST INSTALL FIRST
================================================================================

The font build uses a TeX/Metafont and FontForge toolchain. None of it is .NET,
and none of it is needed to build CodeBrix.LilyPort itself.

On Debian / Ubuntu / LMDE / Mint:

    sudo apt install texlive-binaries texlive-base texlive-metapost \
                     fontforge-nox python3-fontforge

What each package is for:

    texlive-binaries     the `mf` (Metafont), `mpost` (MetaPost) and `gftodvi`
                         binaries
    texlive-base         Metafont's base file, metafont/base/plain.mf.
                         `mf` will not run without it.
    texlive-metapost     metapost/base/mfplain.mp, required by the mf2pt1
                         MetaPost dump step
    fontforge-nox        the headless `fontforge` binary. Use the `fontforge`
                         package instead if you want the GUI too -- either
                         provides /usr/bin/fontforge, which is what the build
                         calls.
    python3-fontforge    the `fontforge` Python module. The generator scripts
                         do `import fontforge` directly, and this is a separate
                         package from the binary -- installing `fontforge`
                         alone is NOT enough.

Also required, but present on essentially every Linux install:

    perl                 mf2pt1.pl is a Perl script
    python3              mf-to-table.py and the generator scripts

IMPORTANT -- python3-fontforge is version-locked to the system python3. On
Debian 13 / LMDE 7 it requires python3 >= 3.13, < 3.14. If your python3 is
outside that range, apt will tell you; do not try to work around it with pip.
The FontForge Python module is a compiled extension against libfontforge and
is not meaningfully pip-installable.

Verify the toolchain before building:

    mf --version
    mpost --version
    fontforge --version
    python3 -c "import fontforge; print(fontforge.version())"

All four must succeed. build-fonts.sh checks these itself and stops with a
clear message rather than failing halfway through 57 fonts.

Exact versions used for the verified 2026-08-02 build:

    OS                       LMDE 7 (gigi), x86_64
    texlive-binaries         2024.20240313.70630+ds-6
        mf                   Metafont 2.71828182 (TeX Live 2025/dev/Debian)
        mpost                MetaPost 2.10 (TeX Live 2025/dev/Debian)
        kpathsea             6.4.0/dev
    texlive-base             2024.20250309-1
    texlive-metapost         2024.20250309-1
    fontforge-nox            1:20230101~dfsg-4+b1  (build date 2025-01-07)
    python3-fontforge        1:20230101~dfsg-4+b1
    python3                  3.13.5
    perl                     5.40.1-6

/!\ TOOLCHAIN VERSIONS ARE LOAD-BEARING. MATCH THEM. Different FontForge or
Metafont versions produce different glyph OUTLINES -- not merely different
bytes: a different number of curve segments for the same shape between the same
endpoints, because mf2pt1 has FontForge remove outline overlaps and that step's
output is version-dependent.

This paragraph used to end "They do not change anything the engraver actually
uses for layout". That is REFUTED -- see section 7. It is true of everything the
engraver reads from the LILC table, the advance widths and the glyph inventory,
and FALSE of the one path that reads the outline, which is skyline computation,
and which decides eleven regression-corpus rows.

Under ruling R19 the port builds its own fonts permanently, and those eleven
rows are graded against COMMITTED PORT-GENERATED BASELINES frozen against the
outlines THIS toolchain produces. So a regeneration on a different FontForge
will move those baselines silently: the sweep will still be green against the
oracle everywhere else, and the baseline rows will drift with nothing to catch
it. If you rebuild the fonts on a different toolchain, RE-FREEZE the baselines
in the same session and say so in baseline-manifest.tsv's header.

================================================================================
3.  HOW TO BUILD
================================================================================

From this directory:

    ./build-fonts.sh

Output goes to ./out by default. To put it somewhere else:

    ./build-fonts.sh /path/to/output/dir

To stamp a different version string into the fonts (this should match the
LilyPond release the mf/ sources came from):

    LILYPOND_VERSION=2.27.3 ./build-fonts.sh

Expect the run to take several minutes; stage 2 -- 57 separate Metafont runs --
dominates. Stage 2 is cached: a .pfb newer than its .mf source is not rebuilt,
so re-running after a failure in a later stage is cheap. To force a full
rebuild, delete the output directory.

The script is `set -euo pipefail` throughout and stops at the first failure
with the path to the relevant log.

================================================================================
4.  WHAT THE PIPELINE ACTUALLY DOES
================================================================================

Upstream LilyPond builds these fonts through mf/GNUmakefile, which depends on
LilyPond's make/ include tree and a pile of autoconf-substituted variables, so
it only runs inside a configured LilyPond source tree. build-fonts.sh is a
standalone reimplementation of exactly the font-producing subset of it.

The stages:

  STAGE 1 -- mf2pt1.mem

    Older mpost builds required a dumped .mem file. Recent ones do not, but
    invoke-mf2pt1.sh still expects the file to exist. We touch a dummy and
    then attempt a real dump, ignoring failure -- which is what upstream's
    makefile does too.

  STAGE 2 -- Metafont to Type 1   (.mf -> .pfb + .tfm + .log)

    57 fonts, via mf/invoke-mf2pt1.sh calling mf2pt1.pl, which internally
    drives mpost. The 57 are:

        feta11..feta26                       8   the main music font
        feta-alphabet11..26                  8   numerals and text glyphs
        feta-flags11..26                     8   note flags
        feta-noteheads11..26                 8   noteheads
        parmesan11..26                       8   ancient notation
        parmesan-noteheads11..26             8   ancient noteheads
        feta-braces-a .. feta-braces-i       9   braces

    THE .log FILES ARE NOT INCIDENTAL. Stage 3 parses them. Do not clean them
    up between stages.

    mf2pt1 pollutes the current directory, so invoke-mf2pt1.sh runs each font
    in a temporary subdirectory and moves the results out. That is why the
    build does not simply call mf2pt1.pl directly.

  STAGE 3 -- Metafont logs to Scheme metadata  (.log -> .lisp + .global-lisp)

    mf-to-table.py scrapes the Metafont logs for the autometric annotations
    that mf/feta-autometric.mf emits, and writes them out as Scheme alists.

    This is where the engraver-critical metadata comes from: glyph extents in
    staff-space units, stem attachment points, and ledger shortening ranges.

  STAGE 4 -- merge into Emmentaler OTFs  (one per design size)

    fontforge -lang=py -script mf/gen-emmentaler.fontforge.py

    Each emmentaler-<size>.otf merges SIX subfonts at that design size:
    feta, feta-alphabet, feta-flags, feta-noteheads, parmesan,
    parmesan-noteheads. It then attaches two CUSTOM OpenType tables:

        LILC   per-glyph metadata, zlib-compressed (level 9)
        LILY   global metadata, stored RAW -- NOT compressed

    Note the asymmetry. LilyPond's reader (lily/open-type-font.cc) attempts
    zlib inflate on both and falls back to treating the content as
    uncompressed when inflate returns Z_DATA_ERROR. A reader that assumes
    both tables are compressed will fail on LILY.

    The generator derives the design size by REGEX FROM THE OUTPUT FILENAME,
    so the --out name matters. It also emits a .svg beside each .otf.

  STAGE 5 -- the brace font

    Braces are a separate font because they are continuously scalable rather
    than drawn once per design size. gen-emmentaler-brace.fontforge.py
    hardcodes its subfont list as "abcdefghi", so no manifest file is needed
    at build time -- upstream's emmentaler-brace.subfonts rule exists only for
    make dependency tracking, and we do not need it.

================================================================================
5.  WHAT GETS PRODUCED
================================================================================

In the output directory:

    emmentaler-11.otf   emmentaler-16.otf   emmentaler-23.otf
    emmentaler-13.otf   emmentaler-18.otf   emmentaler-26.otf
    emmentaler-14.otf   emmentaler-20.otf   emmentaler-brace.otf

    emmentaler-*.svg        SVG 1.1 versions, emitted by the same FontForge
                            pass from the same outlines
    *.pfb *.tfm *.log       stage 2 intermediates
    *.lisp *.global-lisp    stage 3 intermediates
    *.build-log *.gen-log   our per-font logs, for diagnosing failures

BOTH the nine .otf and the nine .svg files are shipped, to
assets/fonts/otf/ and assets/fonts/svg/ respectively. Everything else is
intermediate.

Note that the SVG fonts carry NO LILC/LILY metadata -- those are OpenType
tables with no SVG equivalent. Anything needing glyph extents, stem attachment
points or ledger shortening ranges must read them from the OTF.

The design sizes are staff sizes in points. LilyPond selects the font whose
design size is closest to the staff size in use, which is why eight of them
exist rather than one scalable face -- the glyphs are optically corrected per
size, not merely scaled.

================================================================================
6.  VERIFYING AGAINST AN OFFICIAL LILYPOND RELEASE
================================================================================

    ./compare-fonts.sh ./out /path/to/reference/otf

To get a reference set, download the official release archive and extract just
the fonts:

    tar xzf lilypond-2.27.2-linux-x86_64.tar.gz \
        --strip-components=5 --wildcards '*/fonts/otf/emmentaler-*.otf' \
        -C /path/to/reference

DO NOT EXPECT A BYTE MATCH, and do not use `cmp` or `diff` for this. FontForge
stamps build metadata, table padding and ordering vary between FontForge
versions, and the CFF charstring optimiser is not bit-stable across releases.
A byte comparison tells you nothing useful.

compare-fonts.sh instead compares what actually matters for engraving:

    * the set of glyph NAMES -- the engraver addresses glyphs by name, never
      by index
    * advance widths -- these drive horizontal spacing
    * bounding boxes
    * the LILC and LILY tables, decompressed and compared exactly

The LILC/LILY comparison is the important one. If those match, the engraver
will make identical layout decisions.

================================================================================
7.  RESULTS FROM THE 2026-08-02 VERIFICATION RUN
================================================================================

Built with the toolchain versions in section 2 and compared against the
official lilypond-2.27.2-linux-x86_64 release. Recorded here so that a future
run has something to measure itself against.

  IDENTICAL:

    * LILC tables -- all 9 fonts, byte-identical after decompression
    * LILY tables -- all 9 fonts, byte-identical
    * glyph inventory -- 668 glyphs in each emmentaler-<size>, 577 in
      emmentaler-brace; no glyph present in one and missing from the other
    * advance widths -- every glyph, every font, exact match

  DIFFERENT:

    * bounding boxes, on roughly 105-124 of 668 glyphs per font
      (about 18%), and 55 of 577 in the brace font

      Magnitude: median difference 1 font unit, maximum 3 font units, on a
      1000-unit em. That is 0.1% to 0.3% of an em. At a 20pt staff size that
      is under a thousandth of an inch.

      Largest observed: clefs.hufnagel.do_change and accidentals.mensuralM1,
      both 3 units.

  WHERE THE BOUNDING-BOX DIFFERENCES DO NOT MATTER

    This was checked against the LilyPond source rather than assumed.

    Open_type_font::get_indexed_char_dimensions (lily/open-type-font.cc:372)
    reads glyph dimensions from the LILC table --

        SCM bbox = scm_cdr (scm_assq (ly_symbol2scm ("bbox"), alist));

    -- NOT from the font outline. Since our LILC tables are byte-identical to
    the reference, the engraver receives identical glyph dimensions and will
    lay music out identically. That half stands.

  /!\ AND WHERE THEY DO -- CORRECTED 2026-08-17 (PARITY 24), BY MEASUREMENT

    This section used to end: "FreeType outline bounding boxes are consulted in
    exactly one place, lily/stencil-integral.cc:555, via get_glyph_outline_bbox,
    which feeds skyline computation. A 1-3 unit difference there is far below
    anything that could change a layout decision." That is REFUTED, twice over.

    First, the comparison above is of BOUNDING BOXES only. The outlines
    themselves differ STRUCTURALLY: emmentaler-26's `clefs.G' is 40 curve
    segments in this build and 47 in the official release, with the same bbox
    and a different curve between the same endpoints.

    Second, a skyline is not a bbox. The difference is invisible wherever a
    glyph's EXTREME point decides an answer -- which is most of the corpus, and
    is why this went eleven sessions unnoticed -- and visible wherever the
    profile's SHAPE does. Two treble clefs on adjacent staves at
    layout-set-staff-size 30 face each other's slopes, and the rod between them
    comes out 0.0457 units short.

    MEASURED by swapping the release's nine emmentaler .otf files in, rebuilding,
    and grading a full sweep: MATCH 2293 -> 2304. ELEVEN corpus rows are decided
    by this build alone -- repeat-sign-global-size5/size10,
    caesura-style-comma-over-bar-line, merge-rests-engraver,
    ssaattbb-template-with-all-staves, accidental-ancient,
    markup-with-true-dimensions, show-skylines, skyline-debug,
    skyline-embedded-ps and skyline-slur-segments -- and a twelfth improves.
    Only the .otf matters: the .svg font is the SVG backend's drawing source and
    D29's named-glyph identity already tolerates it.

  CONCLUSION: the locally built fonts are equivalent to the official ones for
  every purpose that reads the LILC table, the advance widths or the glyph
  inventory -- which is the engraving. They are NOT equivalent for the one code
  path that reads the OUTLINE, lily/stencil-integral.cc:555's skyline builder,
  and that path decides eleven corpus rows. The differences are Metafont and
  FontForge version artifacts rather than defects in this build.

  /!\ AND THEY ARE NOT A REASON TO STOP BUILDING. RULING R19 (Jeremy,
  2026-08-17): the port builds its own Emmentaler fonts from the Metafont sources,
  exactly as LilyPond builds its own from the same sources, and the engine must be
  correct against THE FONTS THE PORT SHIPS. Shipping the official release's
  binaries instead is REFUSED -- do not re-propose it, and do not read the
  measurement above as an argument for it. Those eleven rows belong to G1's second
  clause (a divergence in an asset the port builds), which grades them against
  committed port-generated baselines. Full account in PORT-COVERAGE's "PARITY 24"
  section.

  REPRODUCIBILITY

    The build is byte-reproducible: two independent full runs produced 18
    identical files. Verified 2026-08-02.

    This required two settings, both applied in build-fonts.sh:

      SOURCE_DATE_EPOCH   FontForge otherwise stamps wall-clock build time
                          into the OpenType `head` table, the `UniqueID` name
                          record, and the SVG <metadata> block. Defaulted to
                          the commit timestamp of the LilyPond tag the sources
                          came from (1785670568 = v2.27.2), so the font's
                          creation date is the date of its sources.

      USER / LOGNAME      FontForge writes "By <name>" into SVG metadata,
                          read from the building user's passwd GECOS field.
                          Left alone the shipped SVGs would credit whoever
                          ran the build -- a false authorship claim on a font
                          designed by others. Set to "Nienhuys, Nieuwenhuizen
                          and Reuter", the actual designers.

    Both are exported near the top of build-fonts.sh and can be overridden
    from the environment. Change SOURCE_DATE_EPOCH when re-syncing mf/ to a
    new LilyPond tag -- see section 11.

================================================================================
8.  LICENSING -- READ BEFORE MODIFYING ANYTHING IN mf/
================================================================================

The full record is in CodeBrix.LilyPort/THIRD-PARTY-NOTICES.txt. The operative
points for anyone running this build:

  THE FONT SOURCES ARE DUAL-LICENSED. Everything under CodeBrix.LilyPort/mf is
  offered under EITHER GPL-3.0-or-later with the LilyPond Font Exception, OR
  the SIL Open Font License 1.1. We convey both options onward, exactly as
  upstream does. We do not have to choose, and we should not.

  "Emmentaler" and "Feta" ARE RESERVED FONT NAMES under the OFL. This only
  binds MODIFIED fonts. Building unmodified sources with the documented
  pipeline is not a modification, so our output may keep those names.

  *** IF YOU EVER EDIT A .mf FILE, THAT CHANGES. *** A modified font may not
  be distributed under a Reserved Font Name if the OFL option is taken. You
  would then have to either rename the font, or take the GPL+Font-Exception
  option, which carries no name reservation. Either way, record the decision
  in THIRD-PARTY-NOTICES.txt section 3 and add a modification notice to the
  edited file, as GPL-3 section 5(a) requires.

  mf2pt1 IS NOT LILYPOND CODE AND IS NOT GPL. mf2pt1.pl in this directory, and
  mf/mf2pt1.mp, are by Scott Pakin, licensed under the LaTeX Project Public
  License 1.3c or later. LilyPond ships them without listing them in its own
  license exception list, so this is easy to miss. LPPL requires that modified
  versions be renamed. Do not edit these files in place; if you must change
  behaviour, wrap them.

  The generator scripts (gen-emmentaler.fontforge.py,
  gen-emmentaler-brace.fontforge.py, mf-to-table.py, invoke-mf2pt1.sh) are
  LilyPond's own, GPL-3.0-or-later, Copyright (C) Han-Wen Nienhuys.

================================================================================
9.  FILE INVENTORY OF THIS DIRECTORY
================================================================================

  OURS -- written for CodeBrix.LilyPort:

    README.txt              this file
    build-fonts.sh          the standalone build driver, replacing the
                            font-producing subset of mf/GNUmakefile
    compare-fonts.sh        wrapper: verify our output against a release
    compare_fonts.py        the actual structural comparison

  VENDORED -- copied verbatim from LilyPond 2.27.2, because they live outside
  mf/ in the upstream tree and would otherwise be missing:

    mf2pt1.pl               from lilypond/scripts/build/mf2pt1.pl
                            Scott Pakin, LPPL-1.3c-or-later, 1,213 lines
    mf-to-table.py          from lilypond/scripts/build/mf-to-table.py
                            LilyPond, GPL-3.0-or-later

  Everything else the build needs is in CodeBrix.LilyPort/mf, which is a
  byte-identical mirror of lilypond/mf at v2.27.2 (115 files) -- including
  invoke-mf2pt1.sh, mf2pt1.mp, and the two generator scripts.

================================================================================
10. TROUBLESHOOTING
================================================================================

  "'mf' not found" / "'fontforge' not found"
      Section 2. build-fonts.sh checks all five tools up front.

  "python3 cannot 'import fontforge'"
      You installed the fontforge binary but not the Python module. They are
      separate packages: install python3-fontforge.

  Metafont run fails for one font
      See out/<font>.build-log. The most common cause is a missing Metafont
      base -- confirm texlive-base is installed and that
      /usr/share/texlive/texmf-dist/metafont/base/plain.mf exists.

  "no .log files in ... -- stage 2 produced nothing"
      Stage 2 failed silently or the output directory was cleaned between
      stages. Delete the output directory and re-run from scratch.

  FontForge stage fails
      See out/emmentaler-<size>.gen-log. If it complains about a missing .pfb
      or .lisp, stage 2 or 3 did not complete for one of the six subfonts at
      that design size.

  FontForge warnings about .notdef advance width in Emmentaler-Brace
      Harmless and present in the official release fonts too. FontForge emits
      these on both our build and LilyPond's own.

  Comparison reports bounding-box differences
      Expected. Read section 7 before treating it as a failure. Differences in
      glyph NAMES, ADVANCE WIDTHS, or LILC/LILY content are the ones that
      would be real problems.

================================================================================
11. UPDATING TO A NEWER LILYPOND RELEASE
================================================================================

  1. Update the LilyPond reference checkout and check out the new tag.

  2. Re-mirror the font sources:

         rm -rf CodeBrix.LilyPort/mf
         cp -a <lilypond>/mf CodeBrix.LilyPort/mf

     Then confirm it is byte-identical:

         diff -r <lilypond>/mf CodeBrix.LilyPort/mf

  3. Re-vendor the two out-of-tree build scripts from
     <lilypond>/scripts/build/ -- mf2pt1.pl and mf-to-table.py. Check
     mf2pt1.pl's version and license header while you are there; it is
     third-party and can change independently of LilyPond.

  4. Check whether upstream's STAFF_SIZES or BRACES lists changed in
     mf/GNUmakefile. If so, update the matching variables at the top of
     build-fonts.sh.

  5. Build with the new version stamp:

         LILYPOND_VERSION=<new version> ./build-fonts.sh

  6. Compare against the matching official release -- section 6. Compare
     against the SAME version you took the sources from, or the LILC/LILY
     comparison is meaningless.

  7. Update THIRD-PARTY-NOTICES.txt with the new pinned revision, and update
     section 7 of this file with the new verification results.

================================================================================
END
================================================================================
