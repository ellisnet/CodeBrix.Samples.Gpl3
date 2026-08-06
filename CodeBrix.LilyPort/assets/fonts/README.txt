================================================================================
CodeBrix.LilyPort -- MUSIC FONT ASSETS
================================================================================

These are the Emmentaler music fonts that CodeBrix.LilyPort ships and renders
with. They are BUILD OUTPUTS, committed deliberately.

    Built:           2026-08-02
    Built from:      CodeBrix.LilyPort/mf  (byte-identical mirror of
                     lilypond/mf at v2.27.2, commit 2d621459bd)
    Built by:        CodeBrix.LilyPort/tools/font-build/build-fonts.sh
    Version stamp:   2.27.2
    Reproducible:    YES -- rebuilding produces byte-identical files.
                     Verified across two independent full builds.

--------------------------------------------------------------------------------
THESE ARE OUR OWN BUILDS, NOT LILYPOND'S BINARIES
--------------------------------------------------------------------------------

CodeBrix.LilyPort does not redistribute LilyPond's prebuilt MUSIC fonts. It
vendors the Metafont sources and builds them itself, so provenance is
verifiable end to end from Metafont source to shipped binary.

Do not replace these with files copied out of a LilyPond release. If they need
regenerating, run the build -- see tools/font-build/README.txt.

The TEXT fonts under text/ are the opposite case, deliberately: 24 prebuilt
faces (URW C059/Nimbus Sans/Nimbus Mono PS + TeX Gyre Schola/Heros/Cursor)
vendored byte-for-byte from the official LilyPond 2.27.2 binary distribution
by decision D13 (2026-08-05), because text parity needs metrics byte-identical
to the oracle that produced the regression references. Their provenance,
sha256 manifest, and licenses (AGPL-3 + embedding exception; GUST Font
License) live in text/README.txt and text/licenses/.

--------------------------------------------------------------------------------
CONTENTS
--------------------------------------------------------------------------------

  otf/  OpenType -- the fonts the engraver renders with

    emmentaler-11.otf        99,768   design sizes are STAFF SIZES in points.
    emmentaler-13.otf       100,008   LilyPond selects the font whose design
    emmentaler-14.otf        99,236   size is closest to the staff size in
    emmentaler-16.otf        99,940   use. Eight exist rather than one
    emmentaler-18.otf        99,416   scalable face because the glyphs are
    emmentaler-20.otf        98,936   optically corrected per size, not
    emmentaler-23.otf        99,272   merely scaled.
    emmentaler-26.otf        99,928
    emmentaler-brace.otf     95,996   braces: continuously scalable, so a
                           ---------  separate font
                             892,500 bytes

    1512e8a40f0909ea74a1a0e37588fbb6  emmentaler-11.otf
    127db2cd1c510d2d181d481912da6a16  emmentaler-13.otf
    9438f70a19488b95a96cc2d56b3ee047  emmentaler-14.otf
    24b6dbb4111a86212710b2614de883df  emmentaler-16.otf
    a660304309845b05dbbe484f905a6e5a  emmentaler-18.otf
    3c88cbda486a5a80a62f6dae876ff4ea  emmentaler-20.otf
    d0b5fe6e13b2234622dc8621902ef6f6  emmentaler-23.otf
    a3d4e19aa01e27f7a1cd6c1bfc5a5a3b  emmentaler-26.otf
    dfd4ceb7a72e4b4829fe4bdaf8cff586  emmentaler-brace.otf

  svg/  SVG 1.1 fonts -- same glyphs as SVG <glyph> path definitions

    emmentaler-11.svg       310,083   Emitted by the same FontForge pass that
    emmentaler-13.svg       309,403   produces the OTFs, from the same
    emmentaler-14.svg       309,254   outlines. 667 <glyph> elements each.
    emmentaler-16.svg       308,190
    emmentaler-18.svg       307,949   Needed if LilyPond's SVG backend is
    emmentaler-20.svg       307,120   implemented with embedded glyph
    emmentaler-23.svg       306,055   definitions rather than references.
    emmentaler-26.svg       305,322
    emmentaler-brace.svg    227,045
                          ----------
                           2,690,421 bytes

    1a693c291c9db6939f5515984ec9a3f9  emmentaler-11.svg
    be4f6ab9a9a341a296dd30d03c11cba6  emmentaler-13.svg
    163cb373fce76fec2a5a34838429b3e4  emmentaler-14.svg
    238b3bdcece3b17e4cf07a3b3f2acbab  emmentaler-16.svg
    5addc4e8fcc2122e572861ef5ec0b115  emmentaler-18.svg
    be20f484deaa6ab0f51728d8966bbb66  emmentaler-20.svg
    6288a6f2fdb69488b34dbb347b47c9c1  emmentaler-23.svg
    cbf7418b4d841279d6d50013de4abe6d  emmentaler-26.svg
    65d06df21207be9c016f6ae2bffe10ec  emmentaler-brace.svg

  Total: 18 files, 3,582,921 bytes.

  NOTE: the SVG fonts carry NO LILC/LILY metadata -- those are OpenType
  tables and have no SVG equivalent. Anything needing glyph extents, stem
  attachment points or ledger shortening ranges must read them from the OTF.

--------------------------------------------------------------------------------
WHAT MAKES THESE FONTS UNUSUAL
--------------------------------------------------------------------------------

Emmentaler is not consumed as ordinary text. Two things matter for anyone
working on the rendering path:

  1. GLYPHS ARE ADDRESSED BY NAME, never by character code -- "noteheads.s2",
     "clefs.G", "rests.0". Note that the `post` table in these fonts is
     FORMAT 3.0, which carries no glyph names at all; the 668 names live in
     the CFF charset. Any name-to-glyph-index lookup must read the CFF
     charset, not `post`.

  2. TWO CUSTOM OPENTYPE TABLES carry engraver-critical metadata:

       LILC   per-glyph: extents in staff-space units, stem attachment
              points, ledger shortening ranges.   zlib-COMPRESSED.
       LILY   global font metadata.               stored RAW, NOT compressed.

     The asymmetry is real and is easy to get wrong. LilyPond's own reader
     attempts inflate on both and falls back to as-is on failure.

     The engraver takes glyph dimensions from LILC, NOT from the font
     outlines. Layout correctness depends on these tables, not on the
     outline geometry.

--------------------------------------------------------------------------------
REPRODUCIBILITY AND ATTRIBUTION
--------------------------------------------------------------------------------

These files are BYTE-REPRODUCIBLE: running build-fonts.sh again produces files
identical to the ones committed here. Verified across two independent full
builds, all 18 files. So the checksums above are meaningful -- you can rebuild
and confirm that what is committed is what the pipeline produces.

That does not happen by default, and two settings in build-fonts.sh make it
work. Both fix the problem at the source rather than by editing build output.

  SOURCE_DATE_EPOCH=1785670568
      Left alone, FontForge stamps wall-clock build time into the OpenType
      `head` table (created/modified), the `UniqueID` name record, and the SVG
      <metadata> block, so no two builds ever match. This is the
      reproducible-builds standard variable. The value is the commit timestamp
      of the LilyPond tag the mf/ sources came from -- v2.27.2, 2026-08-02
      13:36:08 +0200 -- so the font's creation date is the date of the sources
      it was built from. Re-derive when re-syncing mf/:

          git log -1 --format=%ct <new tag>

  USER / LOGNAME = "Nienhuys, Nieuwenhuizen and Reuter"
      FontForge writes a "By <name>" line into the SVG metadata, taken from
      the building user's passwd GECOS field. Left alone, the shipped SVG
      fonts would credit whoever ran the build -- which on a font designed by
      other people reads as a false authorship claim. Setting both variables
      credits the actual designers instead.

      The OTFs never carried a builder name; only the SVGs did.

--------------------------------------------------------------------------------
VERIFICATION AGAINST THE OFFICIAL 2.27.2 RELEASE
--------------------------------------------------------------------------------

These exact files were compared against the fonts shipped in
lilypond-2.27.2-linux-x86_64 on 2026-08-02:

    LILC tables         byte-identical, all 9 fonts
    LILY tables         byte-identical, all 9 fonts
    glyph inventory     identical -- 668 per design size, 577 brace
    advance widths      exact match, every glyph
    bounding boxes      differ on ~18% of glyphs, by 1-3 units of a
                        1000-unit em

The bounding-box differences are Metafont/FontForge version artifacts and do
not affect engraving, because the engraver reads dimensions from LILC. The
full reasoning, with source citations, is in tools/font-build/README.txt
section 7.

Re-verify at any time:

    cd ../../tools/font-build
    ./compare-fonts.sh ../../assets/fonts/otf /path/to/reference/otf

--------------------------------------------------------------------------------
LICENSE
--------------------------------------------------------------------------------

DUAL-LICENSED, at your option:

    (a) GPL-3.0-or-later WITH the LilyPond Font Exception
    (b) SIL Open Font License, Version 1.1

Both options are conveyed onward exactly as LilyPond offers them. The OFL text
is at LICENSE.OFL in the repository root; the GPL-3 text is at LICENSE.

Copyright (c) 1996--2026, The LilyPond authors (lilypond.org)
with Reserved Font Name "Emmentaler" and "Feta".

Per-file, from the Metafont sources:
    Copyright (C) 1997--2026 Jan Nieuwenhuizen <janneke@gnu.org>
                           & Han-Wen Nienhuys <hanwen@xs4all.nl>
                           & Juergen Reuter <reuter@ipd.uka.de>

RESERVED FONT NAMES. "Emmentaler" and "Feta" are Reserved Font Names under the
OFL. That restriction binds MODIFIED fonts only. These fonts are built from
unmodified sources through the documented pipeline, which is not a
modification, so they legitimately carry those names.

*** IF ANY FILE UNDER mf/ IS EVER EDITED, THAT CHANGES. *** A modified font
may not be distributed under a Reserved Font Name if the OFL option is taken.
Either rename the font, or take the GPL+Font-Exception option, which carries
no name reservation -- and record the choice in THIRD-PARTY-NOTICES.txt
section 3.

Full attribution and compliance record: CodeBrix.LilyPort/THIRD-PARTY-NOTICES.txt

================================================================================
