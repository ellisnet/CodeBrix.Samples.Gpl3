================================================================================
CodeBrix.LilyPort -- book-mirror/
================================================================================

A BYTE-IDENTICAL MIRROR of LilyPond's lilypond-book sources, at the pinned
release v2.27.2 (commit 2d621459bd44cb1758f822a69757242eab843060).

    lilypond-book.py   <- lilypond/scripts/lilypond-book.py     803 lines
    book_snippets.py   <- lilypond/python/book_snippets.py    1,104 lines
    book_base.py       <- lilypond/python/book_base.py          326 lines
    book_texinfo.py    <- lilypond/python/book_texinfo.py       483 lines

    sha256  7f41404f3410af99026babcf1f980d8e02bae0bdd9809556b2dcc9f51ac925a2  lilypond-book.py
    sha256  a27ecc22b9444775de61efae5eb74dcc00fafc1b73a3fe611684bab5b8f52f6e  book_snippets.py
    sha256  a8bf4aa7199895eb27ca1b238d0f979e7d5939301b3d01467db351842a1f4efd  book_base.py
    sha256  0c5531b3fab6a59b40aee3c30084f3616e2302f8b0ae4f1f3f9e2b6ef76c7ca8  book_texinfo.py

--------------------------------------------------------------------------------
FOUR FILES, NOT TWO -- CORRECTED 2026-08-19 AT WAVE LD2
--------------------------------------------------------------------------------

Decision D49(c) named TWO files. The authority is FOUR, and the two that were
missing are not optional extras: `compose_ly' -- the function the whole seam is a
port of -- reads its defaults from `self.formatter.default_snippet_options'
(book_snippets.py:474, :476, :507, :531, :590), and that dictionary is defined in
book_base.py:67-73 and widened by book_texinfo.py's own page geometry. Half the
values that end up in every composed snippet's `%% Options:' line come from files
D49(c) did not name.

This is the SAME SHAPE as the correction wave LD1 made to D49(a), where the
vendored macro closure turned out to be three files rather than two because
common-macros.itexi pulled cyrillic.itexi one level below where the measurement
stopped. Both times the original count was taken by reading one level of
dependency and stopping. The lesson is recorded in both places on purpose.

What was ported OUT of the two new files, and where it went:

  * book_base.py:67-73  `default_snippet_opts' -- the A4 paper defaults
    (597.508\pt x 845.047\pt) and the "[image of music]" alt text
                                    -> Snippets/TexinfoPageGeometry.cs
  * book_texinfo.py     `texinfo_line_widths' -- the page-size -> line-width
    fallback table, and `get_texinfo_width_indent' -- the texi2pdf geometry probe
    whose no-probe fallback path we take, because decision D28 leaves us no TeX
                                    -> Snippets/TexinfoPageGeometry.cs
  * book_base.py:183-186  `split_snippet_options' -- reproduced by the Texinfo
    package's own option splitter, which is what hands us Options.All

BookMirrorTests fences all four files against the sha256s above, so an edit to a
mirror -- the one thing this directory forbids -- fails a test instead of
quietly becoming the authority.

Copyright (C) 1998--2026 Han-Wen Nienhuys and Jan Nieuwenhuizen.
GPL-3.0-or-later. Recorded in ../THIRD-PARTY-NOTICES.txt section 1.

--------------------------------------------------------------------------------
NEVER EDIT ANYTHING IN THIS DIRECTORY
--------------------------------------------------------------------------------

This is the same rule, for the same reason, as parser-mirror/ and mf/. A mirror
that has been edited is no longer a mirror, and the re-sync workflow below
depends on it being one.

--------------------------------------------------------------------------------
WHAT THESE FILES ARE FOR
--------------------------------------------------------------------------------

Decision D49(c), ruled 2026-08-18, in Phase 5 (LilyDocs). These two files are the
FAITHFULNESS AUTHORITY for the snippet-engraving seam -- the
ILilypondSnippetRenderer implementation in tools/Lily.Docs that composes the
source text for each @lilypond snippet a manual embeds, the way lilypond-book
composes it.

They are mirrored rather than read from a lilypond checkout because the need is
ONGOING, not one-time:

  * The initial port happens in wave LD2, but every later wave (LD3-LD5) that
    finds a snippet rendering wrongly answers the question by re-consulting this
    authority. That is what makes it an authority rather than a one-off read.

  * A re-sync to a newer LilyPond needs a diff of what we ported AGAINST what we
    ported FROM. Without a pinned copy of the source, that diff is impossible.

This mirrors the reasoning behind parser-mirror/ (decision O7): the deciding
requirement there was that no external checkout or toolchain is ever needed
again, not even on re-sync. Same requirement, same answer.

--------------------------------------------------------------------------------
THE CLEAN-ROOM BOUNDARY -- READ THIS BEFORE ADDING ANYTHING HERE
--------------------------------------------------------------------------------

Phase 5 treats texi2any and lilypond-book as GPL ORACLES: run them, read their
OUTPUT, never their source. lilypond-book is the ONE NAMED EXCEPTION, because
the port must reproduce its snippet-composition semantics exactly -- the same
standing lily/*.cc has for the engine and parser.yy has for the parser. Ported
logic carries a `//was previously: python/book_snippets.py' provenance note.

    ***  texi2any's source is NOT covered by that exception and must NEVER  ***
    ***  be mirrored here or read. It is a pure oracle. The Texinfo         ***
    ***  rendering is done by the published CodeBrix.Texinfo packages       ***
    ***  (decision D28), never by ported texi2any code.                     ***

Nothing else belongs in this directory. Adding a third file means re-opening the
clean-room boundary with Jeremy first.

--------------------------------------------------------------------------------
RE-SYNCING TO A NEWER LILYPOND
--------------------------------------------------------------------------------

  1. Copy the new scripts/lilypond-book.py and python/book_snippets.py over the
     two files here.
  2. `diff` against the previous mirror to see exactly what changed.
  3. Re-check the option vocabulary. The seam ports only the options the corpus
     actually uses (18 measured at the pin); an option that appears in the diff
     and is used by the corpus needs porting, one that is not does not.
  4. Update the sha256 lines above and the line counts.
  5. Let the LD2 composed-source fences and the manual render gates catch the
     rest.

--------------------------------------------------------------------------------
THESE FILES ARE NOT EXECUTED
--------------------------------------------------------------------------------

Nothing in this repository runs these .py files, and no build or test step reads
them. They are reference text. The port has no Python runtime dependency.

The lilypond-book BINARY is a separate matter: wave LD2 runs the ORACLE's
lilypond-book (from ~/ClaudeHome/oracle/lilypond-2.27.2/bin/) to produce composed
`lily-*.ly' sources and diffs ours against them. That is an oracle run against
its OUTPUT, which the clean-room rule permits.

================================================================================
