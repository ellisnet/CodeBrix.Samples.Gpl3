================================================================================
CodeBrix.LilyPort -- Documentation/
================================================================================

A BYTE-IDENTICAL PARTIAL MIRROR of LilyPond's documentation SOURCES, at the
pinned release v2.27.2 (commit 2d621459bd44cb1758f822a69757242eab843060).

    633 files, 3.21 MB, mirrored from lilypond/Documentation/
    Every file's sha256 is recorded in MANIFEST.sha256 (this directory).

GNU Free Documentation License -- see ../THIRD-PARTY-NOTICES.txt section 4 and
../COPYING.FDL, EXCEPT snippets/ which is PUBLIC DOMAIN (section 5).
Copyright (C) 1996--2026 the LilyPond authors.

--------------------------------------------------------------------------------
NEVER EDIT ANYTHING IN THIS DIRECTORY
--------------------------------------------------------------------------------

Same rule, same reason, as parser-mirror/, book-mirror/ and mf/. These files are
the INPUT the rendered manuals are built from; an edited input silently changes
what a manual MEANS, and every warning baseline in tools/Lily.Docs is frozen
against these exact bytes.

--------------------------------------------------------------------------------
WHY THESE FILES ARE IN THE REPOSITORY AT ALL
--------------------------------------------------------------------------------

Decision D49(b), ruled by Jeremy 2026-08-18, in Phase 5 (LilyDocs).

Phase 5 renders LilyPond's manuals from the port's OWN generated documentation.
The generated files are not standalone: eighteen of the nineteen are INCLUDES of
the notation manual, whose surrounding prose lives here. So rendering the
notation manual requires this source text as well as the port's output.

The alternative -- having the render gates read ~/GitHome/lilypond/Documentation
directly, the way the sibling CodeBrix.Texinfo repo's corpus gates do -- was
DECLINED. Standing rule 7 of the LilyPort plan is that NO BUILD OR TEST STEP
TOUCHES ~/GitHome/lilypond, and Phase 5 takes no exception to it. Copying the
referenced sources in once, here, keeps that rule intact.

Two consequences worth knowing:

  * The render gates ALWAYS RUN. They do not skip when a checkout is absent,
    because the inputs are in the repository. A gate that skipped would now be a
    defect, not a courtesy.

  * The FDL tree is kept CLEARLY SEPARATE from GPL source, per the standing
    instruction in THIRD-PARTY-NOTICES.txt section 4. FDL-licensed text must
    never be copied into source files or XML doc comments anywhere under src/.

--------------------------------------------------------------------------------
WHAT WAS COPIED, AND WHY EACH PART
--------------------------------------------------------------------------------

  en/            77 files, 2.5 MB
                 The @include closure of the eight corpus manuals -- notation,
                 learning, usage, extending, essay, changes, music-glossary and
                 snippets -- plus en/included/, whose 25 .ly files are targets of
                 @lilypondfile in the manual prose.

                 The closure was computed by resolving @include transitively; it
                 is 54 files. Files it does NOT contain are the port's own
                 nineteen generated outputs, which are build products here just
                 as they are upstream and are supplied at render time from the
                 generation directory (that is the whole point of Phase 5).

  snippets/      533 files, 2.3 MB
                 Targets of @lilypondfile in the manuals -- 192 references from
                 the notation manual alone. PUBLIC DOMAIN, carved out of both the
                 GPL and the FDL by upstream's own exception list.

  ly-examples/   18 files, 172 KB
                 Image targets of the @image macros defined in en/macros.itexi.

  bib/           5 files, 68 KB
                 Bibliography sources. The essay manual includes three .itexi
                 files generated FROM these by bib2texi upstream; those generated
                 files do not exist in the checkout, so wave LD5 either generates
                 them from these sources or baselines three include warnings.

  NOT copied:    pictures/ (11 MB) -- referenced only by web.texi, which is out
                 of scope (a package non-goal). If a future wave needs an image
                 from it, copy it in the same way and update MANIFEST.sha256.

--------------------------------------------------------------------------------
THE ORACLE DOES NOT READ THIS DIRECTORY
--------------------------------------------------------------------------------

texi2any is run as an ORACLE against ~/GitHome/lilypond/Documentation DIRECTLY --
NOT against this mirror. Jeremy ruled this deliberately on 2026-08-18, and the
reason is worth preserving, because pointing the oracle here looks tidier and is
wrong:

    An oracle that read OUR copy would inherit any copy defect identically on
    both sides. The two would then agree, and the comparison would prove
    nothing. Reading upstream keeps the oracle independent, so a faithless copy
    shows up AS a difference rather than hiding inside an agreement.

Oracle runs are not build or test steps, so this does not breach rule 7.

--------------------------------------------------------------------------------
RE-SYNCING TO A NEWER LILYPOND
--------------------------------------------------------------------------------

  1. Re-run the closure computation against the new checkout (the procedure is
     described in the Phase-5 plan, section 4, decision D49).
  2. Copy the new file set over this directory.
  3. Regenerate MANIFEST.sha256 and diff it against the old one -- that diff IS
     the list of documentation changes to account for.
  4. Re-render every manual. The expected-warnings baselines under
     tools/Lily.Docs/expected-warnings/ will move; each movement is reviewed and
     re-frozen deliberately, never blanket-accepted.

================================================================================
