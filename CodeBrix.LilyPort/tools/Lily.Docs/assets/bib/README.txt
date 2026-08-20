================================================================================
CodeBrix.LilyPort -- tools/Lily.Docs/assets/bib/
The five LilyPond bibliographies, translated ONCE, and the style file that did it
================================================================================

Five Texinfo bibliographies and the BibTeX style program they were produced by.
Decision D57, ruled by Jeremy 2026-08-19 at wave LD5.

    colorado.itexi            8,587 bytes   51 entries   essay
    computer-notation.itexi  15,282 bytes   61 entries   essay
    engravingbib.itexi        9,961 bytes   35 entries   essay
    we-wrote.itexi            1,997 bytes    8 entries   web.texi (not in scope)
    others-did.itexi          1,593 bytes    5 entries   web.texi (not in scope)
    ------------------------------------------------------------------------
                             37,420 bytes  160 entries

    lily-bib.bst              8,485 bytes   THE RECIPE -- never executed here

Every file's sha256 is in MANIFEST.sha256 and is fenced by
tests/Lily.Docs.Tests/BibliographyAssetTests.cs.

--------------------------------------------------------------------------------
NEVER EDIT ANYTHING IN THIS DIRECTORY
--------------------------------------------------------------------------------

Same rule, same reason, as Documentation/, parser-mirror/, book-mirror/ and mf/.
The five .itexi files are GENERATED OUTPUT held byte-identical to what the oracle
produced -- an edit here is a silent divergence from a file anyone can reproduce
in ten seconds and diff against.

--------------------------------------------------------------------------------
WHY THESE FILES ARE IN THE REPOSITORY AT ALL
--------------------------------------------------------------------------------

The essay manual's "Long literature list" ends with three @include lines naming
colorado.itexi, computer-notation.itexi and engravingbib.itexi. Those files are
BUILD PRODUCTS upstream: the doc build generates them from Documentation/bib/*.bib
and they exist in no source checkout. Without them the essay renders three empty
subheadings behind three Include warnings.

⚠ AND THE THING THAT PRODUCES THEM IS NOT A PYTHON SCRIPT. scripts/build/bib2texi.py
is thirty lines that write a fake .aux file and shell out to the BibTeX BINARY:

    run(["bibtex", "-terse", tempfile], env=dict(environ, TEXMFOUTPUT=tempdir))
    copy(Path(tempdir) / "bib2texi-tmp.bbl", args.output)

The translation is done by BibTeX interpreting lily-bib.bst -- 8.5 KB of BibTeX's
postfix stack language, 28 FUNCTIONs -- including name parsing (von/jr particles,
first-name abbreviation, @tie{} insertion), purify$/change.case$ sentence casing,
add.period$, and a 78-column wrap with two-space continuation indent. Writing our
own would mean writing a .bst interpreter, with no parity gate to hold it honest.

So the choice was never "port a script". It was:

    (a) run bibtex at render time  -- an external native binary in the production
        path, which decision D28's posture rules out, and which would make
        Lily.Docs unable to render the essay on a machine without TeX Live;
    (b) implement a BibTeX .bst interpreter in C#;
    (c) translate ONCE with the oracle and vendor the result.  <-- RULED

⚠ AND (c) IS RIGHT ON THE MEASUREMENTS, not merely cheapest. This is static
reference data, not a living format:

  * The ENTIRE bib corpus in LilyPond is five files, 52,615 bytes, 160 entries,
    and one .bst. There is no more of it anywhere in the repository.
  * Documentation/bib/ has been touched by SEVEN commits out of the repository's
    35,717, across thirty years (first commit 1996-10-09). lily-bib.bst has five.
    That is roughly one content edit every two years.
  * Nothing in LilyPond's user-facing surface has ever accepted a .bib file -- not
    \include, not lilypond-book, not any ly: primitive. The build reaches these
    through a pattern rule, $(outdir)/%.itexi: bib/%.bib, available to LilyPond's
    own documentation authors and to nobody else.

Building a general translator for a format that yields 37 KB of output, changes
once every two years, and has exactly one caller would be a machine for
reproducing a constant.

⚠ COMPARE version.itexi, WHICH LILY.DOCS DELIBERATELY GENERATES RATHER THAN
VENDORING. That one must track the ENGINE's version, so a frozen copy would
silently disagree with the engine that produced the manual. These must track the
CORPUS, and the corpus is pinned. Opposite requirement, opposite answer -- and
that contrast is the whole rule for deciding the next case like it.

--------------------------------------------------------------------------------
WHY ALL FIVE, WHEN ONLY THREE ARE IN SCOPE
--------------------------------------------------------------------------------

we-wrote.itexi and others-did.itexi are included by web/community.itexi, and
decision D48 excluded web.texi from Phase 5 on measured grounds. They are here
anyway, by Jeremy's ruling: translating them cost one bibtex invocation each, they
total 3.6 KB, and the alternative was needing a TeX installation on some future
day to recover them. ⚠ NOTHING RENDERS THEM TODAY, so they are fenced for
structure and hash only -- no manual gate covers their content.

⚠ we-wrote.itexi calls @staticFile{}, a macro macros.itexi defines only under
@ifset web. Rendering it outside web.texi would earn Macro warnings. That is a
fact about the file, not a defect in it.

--------------------------------------------------------------------------------
HOW THEY WERE MADE, AND HOW TO REMAKE THEM
--------------------------------------------------------------------------------

Produced 2026-08-19 with the bibtex installed on this machine (/usr/bin/bibtex),
run as an ORACLE -- the same standing texi2any and lilypond-book have under
decision D28: run it, keep its OUTPUT, never take its source into production.
All five runs completed with exit 0 and ZERO BibTeX warnings.

Sources, at pinned committish 2d621459bd44cb1758f822a69757242eab843060 (v2.27.2):

    lilypond/Documentation/bib/<name>.bib   -- ALSO MIRRORED, at
                                               ../../../../Documentation/bib/
    lilypond/Documentation/lily-bib.bst     -- vendored HERE

To remake one (this reproduces the exact bytes in this directory):

    cd "$(mktemp -d)"
    cat > tmp.aux <<'EOF'
    \relax
    \citation{*}
    \bibstyle{<path to>/lily-bib}
    \bibdata{<path to>/Documentation/bib/<name>}
    EOF
    BSTINPUTS=<path to>/Documentation/essay TEXMFOUTPUT=. bibtex -terse tmp.aux
    cp tmp.bbl <name>.itexi

⚠ The \bibstyle line names the style WITHOUT its .bst extension -- BibTeX appends
it -- and BSTINPUTS must point at Documentation/essay, which is what upstream's
own GNUmakefile rule sets. Getting either wrong makes bibtex fail loudly rather
than quietly, which is the one merciful thing about this toolchain.

--------------------------------------------------------------------------------
RE-SYNCING TO A NEWER LILYPOND
--------------------------------------------------------------------------------

  1. Diff the new checkout's Documentation/lily-bib.bst against the copy here, and
     its Documentation/bib/*.bib against the corpus mirror's. ⚠ IF NEITHER MOVED,
     NOTHING HERE NEEDS REGENERATING -- and on thirty years of history that is the
     usual answer.
  2. If either moved, remake the affected files with the command above.
  3. Regenerate MANIFEST.sha256 and diff it. That diff IS the list of
     bibliography changes to account for.
  4. Re-render the essay and re-freeze its baselines. Its page count WILL move if
     entries were added or removed -- deliberately, per the known-reason rule.

--------------------------------------------------------------------------------
LICENSING
--------------------------------------------------------------------------------

  *.itexi        Derived from Documentation/bib/*.bib (GNU FDL 1.3-or-later,
                 Copyright (C) 1996--2026 the LilyPond authors) by way of
                 lily-bib.bst. Treated as FDL. THIRD-PARTY-NOTICES.txt section 4.

  lily-bib.bst   GPL-3.0-or-later, Copyright (C) 2011--2026 Phil Holmes -- it is
                 LilyPond SOURCE, not manual text. THIRD-PARTY-NOTICES.txt
                 section 1.

⚠ THAT SPLIT IS WHY THIS DIRECTORY EXISTS RATHER THAN THE MIRROR. The obvious home
for a file called lily-bib.bst is Documentation/, which is where it lives upstream
-- but THIRD-PARTY-NOTICES declares this repository's Documentation/ tree cleanly
separated FDL material, "never intermixed with GPL source". A GPL style program
does not belong in it. Keeping the recipe beside its output also means a reader
who opens this directory can see the whole story without leaving it.
================================================================================
