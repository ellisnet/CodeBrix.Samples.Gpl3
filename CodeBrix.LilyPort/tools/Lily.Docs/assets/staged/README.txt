================================================================================
CodeBrix.LilyPort -- tools/Lily.Docs/assets/staged/
The two source-tree files the Contributor's Guide embeds verbatim
================================================================================

    ROADMAP                   4,962 bytes   a tour of LilyPond's source tree
    code-review-checklist.md  2,799 bytes   the reviewer's checklist

Byte-identical copies from the pinned checkout at committish
2d621459bd44cb1758f822a69757242eab843060 (v2.27.2). Hashes in MANIFEST.sha256,
fenced by tests/Lily.Docs.Tests/StagedAssetTests.cs.

Decision D57, ruled by Jeremy 2026-08-19 at wave LD5.

--------------------------------------------------------------------------------
NEVER EDIT ANYTHING IN THIS DIRECTORY
--------------------------------------------------------------------------------

Same rule, same reason, as Documentation/, assets/bib/, parser-mirror/ and
book-mirror/. These are INPUT: the Contributor's Guide prints them verbatim, so
an edit here silently changes what a published manual says LilyPond's source tree
looks like.

--------------------------------------------------------------------------------
WHY "STAGED", AND WHY THEY ARE NOT IN Documentation/
--------------------------------------------------------------------------------

The Contributor's Guide names them with @verbatiminclude:

    en/contributor/source-code.itexi:770        @verbatiminclude ROADMAP
    en/contributor/programming-work.itexi:2764  @verbatiminclude code-review-checklist.md

⚠ NEITHER FILE LIVES IN Documentation/ UPSTREAM. ROADMAP is at the SOURCE TREE
ROOT and the checklist is at .agents/skills/lilypond-code-review/assets/
checklist.md. The doc build COPIES both into $(outdir)/en/ before rendering --
Documentation/GNUmakefile lines 573-584 -- which is the same act it performs on
nothing else except build products. "Staged" is upstream's own arrangement, and
this directory is its analogue.

They are NOT in this repository's Documentation/ mirror, deliberately, and the
reason is licensing rather than tidiness: both are GPL-3-or-later LilyPond SOURCE
files, while THIRD-PARTY-NOTICES.txt declares Documentation/ cleanly separated
GNU FDL material, "never intermixed with GPL source". See section 1 of that file.

--------------------------------------------------------------------------------
HOW THEY REACH A RENDER
--------------------------------------------------------------------------------

This directory is on Lily.Docs' include search path (RenderPaths), so
@verbatiminclude resolves them by bare name -- exactly as upstream's own
-I $(outdir)/en does after the build has copied them there.

⚠ THEY WERE FOUND BY READING A RENDER, NOT BY A CLOSURE MEASUREMENT. The closure
script written at wave LD5 followed @include, @lilypondfile, @image, @sourceimage,
and \include/\epsfile inside music snippets -- and not @verbatiminclude. The two
Include warnings the first Contributor's Guide render earned are what named them.
That is the sixth time in this phase that a file closure was measured one level
too shallow, and the sixth time the render found what the survey missed. The
standing lesson: FOLLOW THE EFFECT, NOT THE COMMAND.

--------------------------------------------------------------------------------
RE-SYNCING TO A NEWER LILYPOND
--------------------------------------------------------------------------------

  1. Diff the new checkout's ROADMAP and
     .agents/skills/lilypond-code-review/assets/checklist.md against the copies
     here.
  2. Copy over anything that moved; regenerate MANIFEST.sha256.
  3. Re-render the Contributor's Guide and re-freeze its baselines. Its page count
     WILL move if either file changed length -- deliberately, per the
     known-reason rule.

⚠ Note the checklist's upstream path contains a DOT directory (.agents/), which
is easy to miss with a plain find or a file browser that hides them.

--------------------------------------------------------------------------------
LICENSING
--------------------------------------------------------------------------------

Both are GPL-3.0-or-later, Copyright (C) 1996--2026 the LilyPond authors, from
the LilyPond source tree. THIRD-PARTY-NOTICES.txt section 1.

⚠ Their CONTENT then appears inside a GNU FDL manual, because the Contributor's
Guide prints them verbatim. That is upstream's own arrangement and not something
this port introduces; the files themselves stay GPL and stay out of the FDL tree.
================================================================================
