================================================================================
CodeBrix.LilyPort -- REGRESSION HARNESS
================================================================================

Written 2026-08-02, against LilyPond v2.27.2
(commit 2d621459bd44cb1758f822a69757242eab843060).

This is milestone 5 of the port: the machinery that answers "is CodeBrix.LilyPort
engraving the same music LilyPond does?" It exists BEFORE the engine port starts,
deliberately, because 2,146 test inputs are too many to retrofit comparison onto
once the output path has hardened around whatever the port happens to produce.

--------------------------------------------------------------------------------
CONTENTS
--------------------------------------------------------------------------------

  1.  Why an external oracle is required
  2.  What you must install first
  3.  Generating the reference
  4.  What is committed, and what is not
  5.  Comparing the port against the reference
  6.  How the comparison works, and why it is not a byte diff
  7.  Results from the 2026-08-02 baseline run
  8.  Licensing of the vendored suite
  9.  Troubleshooting

================================================================================
1.  WHY AN EXTERNAL ORACLE IS REQUIRED
================================================================================

There are ZERO unit tests in LilyPond's lily/. Upstream establishes correctness
by rendering input/regression/*.ly and comparing against a previous run of
itself. There is no checked-in expected output to compare against -- the
reference IS the previous build.

A reimplementation therefore has nothing to test against unless it produces a
reference from the real LilyPond first. That is what generate-reference.sh does.

(Note that flower/ DOES ship unit tests, roughly 2,000 lines of them, and those
are ported directly -- see src/CodeBrix.LilyPort.Flower/PORT-COVERAGE.txt. The
"no tests upstream" problem applies to the engine, not the utility layer.)

================================================================================
2.  WHAT YOU MUST INSTALL FIRST
================================================================================

Only the official LilyPond binary, and only to generate the reference. Nothing
here is needed to build or use CodeBrix.LilyPort.

The Linux release is a SELF-CONTAINED tarball -- no installation, nothing written
outside the directory you extract into:

    tar xzf lilypond-2.27.2-linux-x86_64.tar.gz
    export LILYPOND_BIN="$PWD/lilypond-2.27.2/bin/lilypond"

Verify:

    "$LILYPOND_BIN" --version      # expect: GNU LilyPond 2.27.2

MATCH THE VERSION TO THE PORT. Comparing against a different LilyPond measures
version drift, not port fidelity. generate-reference.sh warns when the version is
not 2.27.2 but does not stop, because comparing across versions is occasionally
what you want.

Also required: python3 (for the comparison), and GNU coreutils.

================================================================================
3.  GENERATING THE REFERENCE
================================================================================

    export LILYPOND_BIN=/path/to/lilypond-2.27.2/bin/lilypond
    ./generate-reference.sh [OUTPUT_DIR]

Environment variables:

    LILYPOND_BIN        path to the oracle (required unless lilypond is on PATH)
    JOBS                parallel renders (default: nproc)
    LIMIT               render only the first N inputs (default: 0, meaning all)
    PER_FILE_TIMEOUT    seconds per input (default: 60)

Expect roughly four minutes for the full suite at JOBS=10.

The per-file timeout matters: a few regression inputs are deliberately
pathological, and without it one of them stalls the entire run.

Rendering flags, and why each is there:

    --formats=svg           Ask for SVG only. WITHOUT THIS, LilyPond still
                            attempts PDF, warns that the format is unsupported,
                            and --silent promotes that warning to a FATAL error --
                            which failed 19 of the first 60 inputs before the flag
                            was added.
    -dbackend=svg           Use the SVG backend.
    -dno-point-and-click    Suppress textedit:// links. They embed ABSOLUTE SOURCE
                            PATHS, which would make the reference specific to the
                            machine that generated it.
    --silent                Keep the logs to real diagnostics.

================================================================================
4.  WHAT IS COMMITTED, AND WHAT IS NOT
================================================================================

  COMMITTED:

    tests/regression/*.ly       the 2,146 inputs, vendored verbatim from
                                LilyPond's input/regression/. These are the test
                                suite; the harness is meaningless without them.

    manifest.tsv                one line per reference output: sha256, byte size,
                                file name. Small, diffable, and enough to detect
                                that a reference has drifted.

  NOT COMMITTED:

    the reference SVGs          ~2,000 files and hundreds of megabytes. They are
                                regenerable in about four minutes from a pinned
                                oracle, they would dominate the repository, and a
                                committed copy silently goes stale when the
                                LilyPond version moves.

    logs/                       per-file render logs, useful only while debugging
                                a specific input.

  The consequence is that the manifest is the durable artifact and the SVGs are
  a build product. Regenerate before comparing.

================================================================================
5.  COMPARING THE PORT AGAINST THE REFERENCE
================================================================================

    ./compare-output.sh REFERENCE_SVG_DIR CANDIDATE_SVG_DIR

    TOLERANCE=0.01 ./compare-output.sh ...     # placement tolerance

This is not usable yet in the sense that matters: CodeBrix.LilyPort cannot
engrave anything until milestone 6 produces an engine. The harness is built and
verified now so that milestone 6 has a scoreboard from its first day.

================================================================================
6.  HOW THE COMPARISON WORKS, AND WHY IT IS NOT A BYTE DIFF
================================================================================

Byte-comparing SVG is useless as a porting signal. Two engravers can lay music
out identically and still differ in element ordering, floating-point formatting
or id numbering. And a byte diff yields exactly one bit -- "different" -- which
says nothing about WHERE the port diverged.

compare-output.py instead grades each file on a ladder, coarse to fine:

    MATCH               same glyphs, same places, within tolerance
    PLACEMENT-DIFFERS   right glyphs, wrong positions -- a spacing or layout bug
    PLACEMENT-ORDER     right glyphs, emitted in a different order
    PLACEMENT-COUNT     right glyph inventory, wrong number of placements
    GLYPHS-DIFFER       wrong musical content: missing clef, wrong notehead,
                        extra accidental. Reports which glyphs and how many.
    UNPARSEABLE         output is not well-formed SVG
    MISSING             no output produced at all

The ordering is the point. A port reaches GLYPHS-DIFFER long before it reaches
PLACEMENT-DIFFERS, and PLACEMENT-DIFFERS long before MATCH, so progress can be
measured continuously instead of as a single distant pass/fail. Getting the right
notes on the page is a different milestone from getting them in the right place,
and this reports them separately.

VALIDATION. A comparator that always says MATCH is worse than none, so this one
was checked in both directions on 2026-08-02:

  * Reference against ITSELF          -> 60 of 60 MATCH (100%)
  * Reference against the same inputs
    rendered at a different global
    staff size                        -> 2 MATCH, 10 GLYPHS-DIFFER, with the
                                         differing glyph counts named per file

So it detects a real layout change and localises it, rather than merely
reporting inequality.

================================================================================
7.  RESULTS FROM THE 2026-08-02 BASELINE RUN
================================================================================

Oracle: GNU LilyPond 2.27.2, SVG backend, point-and-click disabled.
Suite:  2,146 vendored .ly inputs.

    rendered ok  2,120
    no output       22    input produces no score -- usually expected
    failed           4
    timed out        0
    output       2,316 SVG pages, 62 MB

  Committed record: reference-manifest.tsv (one sha256 + size + name per output
  page) and reference-status.txt (per-input outcome).

  The four failures are all missing-asset cases, not engraving failures:
  clip-systems depends on an .eps another test generates, and three markup tests
  reference image or text files by path. They are recorded rather than hidden.

  Inputs that produce NO output are not necessarily failures: parts of the suite
  test error handling, MIDI-only output, or lilypond-book fragments.

  GETTING HERE TOOK TWO CORRECTIONS worth remembering:
    * Without --formats=svg, 19 of the first 60 inputs failed fatally. Section 3.
    * Without the .ily include files vendored alongside the .ly inputs, 113 of
      2,146 failed on "cannot find file". The suite is not just its .ly files.

  Self-check: comparing the full reference against itself yields
  2,316 of 2,316 MATCH (100.00%), so the comparator is sound at full scale.

================================================================================
8.  LICENSING OF THE VENDORED SUITE
================================================================================

tests/regression/*.ly is GPL-3.0-or-later, part of LilyPond, and is recorded in
CodeBrix.LilyPort/THIRD-PARTY-NOTICES.txt section 1.

TWO THINGS TO KNOW:

  * ly/articulate.ly is GPL-3-ONLY, not "or later", and is DELIBERATELY ABSENT.
    See THIRD-PARTY-NOTICES.txt section 2 -- that decision is still open.

  * input/regression/musicxml/ is MIT-licensed (Reinhold Kainhofer), NOT GPL. It
    is a separate work that happens to live inside the regression tree. It is NOT
    vendored here. If MusicXML support is ever ported, those 165 files come with
    their own attribution requirement -- see THIRD-PARTY-NOTICES.txt section 6.

================================================================================
9.  TROUBLESHOOTING
================================================================================

  "lilypond not found"
      Set LILYPOND_BIN. Section 2.

  Nearly every input fails with "ignoring unsupported formats (pdf)"
      The --formats=svg flag is missing. Section 3 explains why that turns into a
      fatal error under --silent.

  The script prints "Rendering" and then exits with no output
      A pipeline under `set -o pipefail` where the reader closes early -- `head`
      is the usual culprit -- gives the writer a SIGPIPE, and `set -e` then exits
      SILENTLY. The LIMIT path wraps its `head` in `set +o pipefail` for exactly
      this reason. Suspect it first if you add another pipeline.

  A single input hangs
      PER_FILE_TIMEOUT bounds it; the run continues and the file is reported as
      TIMEOUT. Run that one by hand with the worker script written to
      OUTPUT_DIR/.render-one.sh.

  Comparison reports MISSING for everything
      CodeBrix.LilyPort has not produced output. Expected until milestone 6.

================================================================================
END
================================================================================
