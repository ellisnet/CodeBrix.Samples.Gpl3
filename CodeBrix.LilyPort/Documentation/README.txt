================================================================================
CodeBrix.LilyPort -- Documentation/
================================================================================

A BYTE-IDENTICAL PARTIAL MIRROR of LilyPond's documentation SOURCES, at the
pinned release v2.27.2 (commit 2d621459bd44cb1758f822a69757242eab843060).

    690 files, 5.20 MB, mirrored from lilypond/Documentation/
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

  en/            91 files, 2.8 MB
                 The @include closure of the NINE corpus manuals -- notation,
                 learning, usage, extending, essay, changes, music-glossary,
                 contributor and snippets -- plus en/included/, whose 25 .ly
                 files are targets of @lilypondfile in the manual prose.

                 The closure was computed by resolving @include transitively.
                 Files it does NOT contain are the port's own nineteen generated
                 outputs, which are build products here just as they are upstream
                 and are supplied at render time from the generation directory
                 (that is the whole point of Phase 5).

                 The 14 files added at wave LD5 (2026-08-19) are contributor.texi
                 and its thirteen contributor/*.itexi chapters, 302 KB. Decision
                 D48 ruled contributor.texi into scope on 2026-08-19; it is the
                 one in-scope manual that consumes none of the port's generated
                 files, which is a fact about the mission's origin rather than
                 about cost.

  snippets/      533 files, 2.3 MB
                 Targets of @lilypondfile in the manuals -- 192 references from
                 the notation manual alone. PUBLIC DOMAIN, carved out of both the
                 GPL and the FDL by upstream's own exception list.

  ly-examples/   18 files, 172 KB
                 Image targets of the @image macros defined in en/macros.itexi.

  bib/           5 files, 68 KB
                 Bibliography sources. The essay manual includes three .itexi
                 files generated FROM these by bib2texi upstream; those generated
                 files do not exist in the checkout.

                 ⚠ THEY DO NOW EXIST IN THIS REPOSITORY, at
                 ../tools/Lily.Docs/assets/bib/ -- all FIVE of them, translated
                 once by the bibtex on the build machine run as an ORACLE, and
                 held byte-identical to what it produced. Decision D57, ruled
                 2026-08-19. The .bib SOURCES stay here because they are FDL
                 documentation text; the lily-bib.bst style program that consumes
                 them is GPL LilyPond source and is vendored beside its output
                 instead, so this tree stays cleanly FDL.

                 ⚠ ON A RE-SYNC, DIFF BOTH: if neither these .bib files nor
                 upstream's Documentation/lily-bib.bst moved, nothing in
                 assets/bib/ needs regenerating. On thirty years of upstream
                 history that is the usual answer -- seven commits have touched
                 this directory, five have touched the .bst.

  pictures/      43 files, 1.7 MB. Every one of them is named by the
                 @sourceimage MACRO, which expands to @image{pictures/...}, or --
                 in one case -- from inside a music snippet. NONE of them is a
                 literal @image in any manual, which is why the original closure
                 measurement found no pictures at all.

                 Three were added at wave LD3 (2026-08-19), for notation:

                   Gonville_after.png, Gonville_before.png -- @sourceimage
                   targets. The survey looked for @image and found only macro
                   DEFINITIONS. The two Include warnings the first render earned
                   are what found them.

                   context-example.eps -- named from INSIDE a music snippet,
                   \epsfile #X #10 "./context-example.eps" in text.itely, and so
                   invisible to any survey of the Texinfo source's own commands.
                   The failed engraving is what found it. Upstream reaches it the
                   same way, with -I $(src-dir)/pictures on lilypond-book's
                   command line (Documentation/GNUmakefile).

                 Forty more were added at wave LD5 (2026-08-19), by MEASURING the
                 @sourceimage closure of the seven remaining manuals rather than
                 by surveying @image again:

                   34 for essay (en/essay/engraving.itely), 1.3 MB -- the
                   engraving-comparison plates.
                   5 for learning (en/learning/installing.itely), 336 KB -- the
                   Frescobaldi_* screenshots.
                   1 for contributor (en/contributor/programming-work.itexi),
                   58 KB -- architecture-diagram.png. ⚠ The Phase-5 plan recorded
                   contributor as having "ZERO real @image uses"; that survey had
                   looked for @image, and this is a @sourceimage. Same trap, fifth
                   firing.

                 TWO KINDS OF PICTURE ARE DELIBERATELY *NOT* HERE:

                   pictures/pdf/*.pdf (10 files) -- the @iftex variants of nine
                   of essay's plates. The Texinfo package's @image extension probe
                   excludes .pdf BY DESIGN ("a manual that keeps pdf/NAME variants
                   for its TeX branch would then hand Html2Pdf a file it cannot
                   decode"), so copying them would buy nothing. The renderer's
                   Print conditional profile reads both @iftex and @ifnottex, so
                   each of those pictures still reaches the document -- from the
                   other branch -- and the unresolved TeX-branch variant is one
                   baselined warning apiece.

                   pictures/context-example.png -- does not exist upstream. It is
                   a BUILD PRODUCT there, made from the .eps that IS here.
                   learning's @sourceimage{context-example,6cm,} therefore cannot
                   resolve, and is one baselined warning. The same .eps resolves
                   perfectly well for the notation manual, because there it is
                   reached by \epsfile from inside a snippet, where the ENGINE
                   reads it rather than the image resolver.

                 ⚠ THE REST OF pictures/ IS STILL NOT COPIED (242 files, 8.6 MB),
                 and after wave LD5 that is a CLOSED question rather than an open
                 one: what remains belongs to web.texi, which decision D48
                 excluded, and to the translated manuals, which D17 and D27
                 excluded. No in-scope manual names any of it.

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
