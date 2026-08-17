================================================================================
CodeBrix.LilyPort -- REGRESSION HARNESS
================================================================================

Written against LilyPond v2.27.2
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
  7.  Results from the initial baseline run
      (later sections, unnumbered: the stale-output fix, the per-file scratch
       working directory, THE DIAGNOSTICS GATE, lowering a floor)
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
    MODE                svg (default, the pinned parity corpus) or diagnostics
                        (the oracle's warnings, for compare-diagnostics.py --
                        touches neither reference/svg nor the manifest)

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
                            ⚠ AND THAT IS WHY reference/logs/ IS USELESS FOR
                            GRADING DIAGNOSTICS: --silent suppresses the warning
                            lines too, so only 10 of 2,146 logs carry any. The
                            diagnostics gate uses MODE=diagnostics, which drops
                            this flag and writes reference/diagnostics/ instead
                            -- see "THE DIAGNOSTICS GATE" below.

THE FONTS ARE PINNED, AND THEY HAVE TO BE
--------------------------------------------------------------------------------
generate-reference.sh builds reference-fonts.conf from reference-fonts.conf.in
and points FONTCONFIG_FILE at it before rendering anything.  This is not a
convenience.  ly/paper-defaults-init.ly:170-180 makes the SVG backend name the
GENERIC font families:

    property-defaults.fonts.serif =
      #(if (eq? 'svg (ly:get-option 'backend)) "serif" "LilyPond Serif")

so under -dbackend=svg Pango resolves "serif", "sans" and "monospace" through
whatever fontconfig the HOST has, and the corpus silently records the metrics of
whatever that machine happens to have installed.  A corpus generated before the pinning carried Noto Serif / Noto Sans / Noto Sans Mono for every text run --
measured glyph by glyph, the oracle's ink boxes and advances for x/X/g/o matched
Noto exactly and did not match C059, which is what LilyPond's own "LilyPond
Serif" alias prefers.

Two things follow.  A corpus generated that way is not reproducible on another
machine, which is the same class of defect as the point-and-click paths above.
And it is unreachable for the port: D23 fixes CodeBrix.LilyPort's text faces to
the 24 vendored ones and forbids system-font fallback outright, so the two sides
were resolving the same generic name to different fonts and text-bearing pages
could never MATCH.

reference-fonts.conf.in therefore pins the generic families to the same faces the
port uses, in the same order, out of the ORACLE'S OWN bundled font directory --
which is byte-identical to the port's vendored copies (sha256, 24 of 24).  It
pins WHICH font is chosen and changes no font.  Its only <dir> is that directory,
so no system font is reachable even in principle, and a code point none of the
bundled faces covers draws .notdef on both sides, which is what D23 asks for.

    IF YOU REGENERATE THE REFERENCE WITHOUT THIS FILE, every text run in the
    corpus moves and the ratchet floor becomes meaningless.  The manifest header
    records the pinning and the font directory for exactly that reason: check it
    before trusting a corpus you did not generate yourself.

The face lists mirror TextFontChain.Families in
src/CodeBrix.LilyPort.Engine/Fonts/TextFace.cs one for one.  If that table
changes, reference-fonts.conf.in changes with it, and the corpus is regenerated.

MIDI IS NOT AFFECTED.  reference-midi/ carries no font data, so
generate-midi-reference.sh is deliberately left unpinned and the committed .midi
files are unchanged by any of this.

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

*** THE FIRST RUN AGAINST THE PORT. ***

    Input `{ c'4 }`, one file. Verdict: GLYPHS-DIFFER -- up the ladder from
    MISSING, which is the position first light was expected to reach.

    It earned its keep on the first run twice over: the port's very first
    output was UNPARSEABLE (the SVG backend used xlink:href without binding
    the namespace -- a real defect, now fixed and pinned by a test), and the
    graded verdict then exposed the backend mismatch described in section 6.

    The full 2,146-file sweep is not worth running until the port can engrave
    more than a single note; the harness is now the active oracle, which is
    what mattered.

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

*** CORRECTION, from the first run against the port ***

  The `<use>` branch of parse_svg is DEAD against real LilyPond output.
  LilyPond 2.27.2's SVG backend embeds each glyph's OUTLINE as a <path> with a
  scaled transform; it never emits <use xlink:href="#glyphname">, and
  -dsvg-woff does not change that (checked). Music glyphs therefore all land in
  the deliberately coarse <path:N> bucket.

  CodeBrix.LilyPort's own backend emits <use> with the glyph name. So for the
  same music the comparator sees `<path:1> x2, <path:5> x1` on one side and
  `noteheads.s2 x1, clefs.G x1` on the other, and reports GLYPHS-DIFFER for a
  reason that has nothing to do with the engraving.

  Why the validation below did not catch it: BOTH sides of both checks were
  LilyPond output, and the coarse path signature does vary with staff size, so
  the check still discriminated. It only fails to discriminate across BACKENDS.

  What it means: glyph-level comparison needs the port to emit outlines too,
  which needs a CFF charstring interpreter. That was recorded as deferred
  ("only wanted for skyline tracing"); it is now also what stands between the
  port and its only correctness oracle. See CodeBrix.LilyPort.Engine's
  PORT-COVERAGE.txt, eighth pass.

VALIDATION. A comparator that always says MATCH is worse than none, so this one
was checked in both directions:

  * Reference against ITSELF          -> 60 of 60 MATCH (100%)
  * Reference against the same inputs
    rendered at a different global
    staff size                        -> 2 MATCH, 10 GLYPHS-DIFFER, with the
                                         differing glyph counts named per file

So it detects a real layout change and localises it, rather than merely
reporting inequality.

================================================================================
7.  RESULTS FROM THE INITIAL BASELINE RUN
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

--------------------------------------------------------------------------------
COMPARATOR SELF-CHECK
--------------------------------------------------------------------------------

The comparator now grades at GLYPH and POSITION level. Position comes from
accumulating the translate() of the enclosing <g> elements.

*** SUPERSEDED IN PART by named-glyph identity (below). *** This section used to go on
to say that the `d` attribute IS the glyph's identity, because upstream's SVG
backend writes each glyph's own path inline rather than referencing a shared
definition. The first half is still true and the conclusion no longer is: the port
and the oracle copy their outlines out of DIFFERENT BUILDS of the same font, which
serialize identically-shaped glyphs differently. A music glyph is now identified by
NAME, resolved from its exact bytes through the committed glyph-identity index --
see "NAMED-GLYPH IDENTITY, AND THE GLYPH-IDENTITY INDEX" at the end of this file
for the current contract. Everything else is still byte-exact.

Because that machinery only runs once two pages agree on their glyph inventory,
and no port output does yet, it is fenced by comparing the reference directory
AGAINST ITSELF:

    python3 compare-output.py reference/svg reference/svg

Expected: 2316 of 2316 MATCH. Anything less means the comparator cannot see
something it should -- which is exactly the failure the previous version had and
nothing caught: it looked for <use xlink:href="#glyph"> elements that LilyPond
never emits, so it read ZERO glyphs and ZERO placements out of every reference
page and graded the whole suite on a coarse path-shape histogram. It could not
have reported a position difference even in principle.

Run this self-check after any change to parse_svg.

===============================================================================
THE CANDIDATE DIRECTORY WAS NOT CLEANED, AND THE RATCHET COULD NOT SEE IT
(found by the repeats/voltas group's first slice; fixed the same day)
===============================================================================

THE DEFECT, kept in full because the shape of it is worth recognising again.

BatchDriver WROTE INTO candidate/svg and never removed what was already there.
So after a sweep the directory held this run's output PLUS every page any
earlier run produced and this one did not. When it was found, candidate/svg had
1,568 files for a sweep that produced 1,470, with some dating from earlier sessions.

WHY THAT MATTERED. compare-output.py grades whatever is in the directory. A file
that STOPS producing a page therefore kept its stale page from a previous run
and graded exactly as it did before -- so the ratchet reported no regression for
precisely the failure mode it exists to catch.

MEASURED, not theorised. The repeats/voltas group's first slice registered the grace iterator
constructor, and `measure-counter-grace` went SVG -> NOOUT (it now reaches an
unported ly:spanner-broken-into). Its manifest row was GLYPHS-DIFFER, so that is
a real regression. `ratchet.py check` reported 0 regressions, because it graded
the stale SVG from the sweep before it.

Nor was it one row. Against the first CLEAN measurement the committed floor was
overstated by 97, accumulated across at least three sessions.

WHAT THE FIX IS. Two things in BatchDriver, because removing the cause and
detecting its return are different jobs:

  * The output directory is EMPTIED of .svg files at startup, every run. Only
    top-level .svg files go; anything else is left alone and named, since
    unexpected contents usually mean the wrong directory was passed.
  * A SELF-CHECK at the end asserts the directory holds exactly the set of pages
    the sweep reported writing, and EXITS 3 if it does not. That is the cheap
    invariant that would have caught this the moment it appeared -- the same
    idea as comparing the reference directory against itself. It is a real gate:
    it was proven to fail by dropping a foreign page into the directory mid-run.

  `--keep-existing` opts out of both, for the rare partial run (--files/--limit)
  that is deliberately adding to a full sweep's output. It says so on stdout,
  and a run made that way is not evidence.

So the sweep is now just:

    dotnet run --project tools/regression-harness/BatchDriver -c Release -- \
        tests/regression tools/regression-harness/candidate/svg > /tmp/sweep.log

No `rm -rf` step to remember. A step you have to remember is a step that will
eventually be skipped by whoever is in a hurry, and that is what happened here.

SCOPE OF THE DOUBT this casts backwards. The IMPROVEMENTS recorded by earlier
sessions are not affected -- an improvement is MISSING -> something, i.e. a file
that had no candidate page and now has one, which stale files cannot fake. What
is NOT trustworthy in the record is any earlier "0 regressions" claim. Every
floor that could not be met against a clean directory has since been lowered,
with its reason, in pass-manifest-decisions.tsv.

-------------------------------------------------------------------------------
THE PER-FILE SCRATCH WORKING DIRECTORY
-------------------------------------------------------------------------------

A .ly file may WRITE, and what it writes is named RELATIVE to the process
working directory. Upstream never has to think about this: it runs one process
per file, so the writes land wherever the user was standing and are that run's
alone. A batch runner has to arrange the same isolation deliberately, exactly as
it already has to arrange LilyPondInit.RestoreDefaults for the interpreter.

WHAT WENT WRONG. The sweep ran from wherever it was launched, which the
documented command makes the repo root. event-listener-output.ly writes
`-violin-1.notes' there -- and opens it for APPEND, so each sweep added to the
previous one's file instead of replacing it: 6,295 bytes of one run read as
18,885 bytes of three. Two consequences, and the second is the one that matters.
The repository gets littered with files that are not output anybody asked for.
And one input file's writes become readable by every file engraved after it,
which is the cross-file leak class that has already produced nine separate
defects in this port. A .gitignore entry would have hidden the first and left
the second exactly where it was.

WHAT THE FIX IS. Each input file runs from its own EMPTY directory:

  * The scratch root is <temp>/codebrix-lilyport-batch-scratch-<pid>, outside
    the repository, so an unexpected file name cannot reach the working tree.
  * PER PROCESS, not one shared root. A shared root has to be emptied at startup
    to stop side files accumulating across sweeps, and that wipe deletes the
    scratch directories of any sweep already running -- a probe run started
    alongside a sweep killed it on its first file. Naming the root after the
    process removes the shared resource rather than scheduling access to it.
  * The per-file directory is emptied immediately before the file runs, so a
    file never sees an earlier run's leftovers, and nothing can append across
    sweeps.
  * SUITE_DIR and OUT_DIR are resolved to full paths BEFORE the first chdir,
    because the documented command passes both as relative paths.
  * A relative LILYPORT_EXPANSION_CACHE_DIR is pinned to a full path at startup.
    That override is read fresh on every access, so left relative it would mean
    a different, cold cache in each of 2,146 scratch directories.

WHAT IT REPORTS. Isolating writes must not become a way to LOSE them, so
anything a file wrote is named on a SIDE-FILE line with its size, and empty
scratch directories are removed -- a directory left behind is evidence, not
leftovers. Over the full suite exactly two files write:

    event-listener-output   SIDE-FILE   -violin-1.notes   6295 bytes
    markup-eps              SIDE-FILE   box.eps            205 bytes

The second had been landing in the repo root all along and nobody had noticed;
it took the reporting to see it. That is the argument for the SIDE-FILE line in
one example.

COST: none measurable. The sweep that introduced it ran 595.2s against the
immediately preceding run's 600.4s, produced the same 2,113 SVG / 32 NOOUT /
1 ERROR / 2,311 pages, and its comparator verdicts were identical to the
previous run's ROW FOR ROW.

-------------------------------------------------------------------------------
THE DIAGNOSTICS GATE: compare-diagnostics.py
-------------------------------------------------------------------------------

THE GAP IT CLOSES. Until this existed, nothing here read a DIAGNOSTIC.
compare-output.py grades SVG geometry; the docs run grades the 19 generated
documentation files; ratchet.py grades compare-output.py's verdicts;
compare-midi.py grades .midi bytes. Text printed to stderr was scored by none of
them. That is how the port's type-check message came to differ from upstream's
in both wording AND severity for the life of the project, found only by accident
while chasing something else -- and why four defect classes had to be found by
reading the sweep's stderr by hand.

BUILDING THE REFERENCE. The parity corpus's own logs are useless for this: they
are made with --silent and contain no warning line at all. So the SAME script
grows a second mode:

    LILYPOND_BIN=... MODE=diagnostics ./generate-reference.sh

which drops --silent, copies NO svg, writes NO manifest, and puts one log per
input in reference/diagnostics/. It cannot disturb the parity corpus: different
directory, nothing copied, and the manifest hashes `.svg` only. ~97 seconds for
2,146 files. One script rather than two because D30's font pinning has to be
IDENTICAL on both runs, and a second script would be a second copy to keep in
step -- font resolution failures are themselves diagnostics.

RUNNING IT. The two sides take different artifacts, deliberately: the oracle is
one process per file so per-file logs are its natural output, while the port is
one process for the whole suite so a MERGED stream is its natural output.

    dotnet run --project tools/regression-harness/BatchDriver -c Release -- \
        tests/regression tools/regression-harness/candidate/svg > /tmp/sweep.log 2>&1
    python3 compare-diagnostics.py reference/diagnostics /tmp/sweep.log

THE STREAMS MUST BE MERGED. Attribution works because BatchDriver prints a
file's result line AFTER running it, so everything since the previous result
line belongs to the file named next. Run it as `> log 2> err` and that
interleaving is gone and nothing can be attributed.

  /!\ AND A FILE MAY PRINT TWO RESULT LINES. A score that writes MIDI as well as
  a page prints a MIDI line and then an SVG line, and this script used to clear
  its pending list only on the TERMINAL one -- so such a file's diagnostics were
  attributed to it TWICE. Latent for the life of the script: the five corpus files
  it affects emitted no diagnostics at all until PARITY 15's missing-glyph warning
  landed, at which point it read as the port saying everything twice, which is
  exactly what a real defect would look like (trap 32a). Fixed 2026-08-16, and
  --selftest now gives every other file a MIDI line so the case is under test.

    python3 compare-diagnostics.py reference/diagnostics --selftest

is the standing check, and it earns its keep here more than elsewhere: the
asymmetry above means the candidate path has to parse a merged stream AND
attribute it, neither of which the reference path does. The self-test re-emits
the reference in the port's own merged format and demands 100% MATCH back.

FIRST READING (2026-08-14, against the post-4a sweep):

    MATCH         1825   85.0%
    EXTRA          290   13.5%    the port says what the oracle does not
    MISSING         23    1.1%    the oracle says what the port does not
    BOTH             6    0.3%
    TEXT-DIFFERS     2    0.1%
    diagnostic lines: 414 reference / 4741 candidate

The port says ELEVEN TIMES as much as the oracle. EXTRA is dominated by the
already-known D1/D2. MISSING was entirely unmeasured before this gate and is the
more interesting half -- see the plan's D9.

TWO KNOWN LOOSENESSES, both deliberate and both recorded in the script's header:
location is not part of the match key (the oracle names its input by absolute
path, the port by bare filename -- an invocation difference), and order is not
graded (multiset comparison; counts ARE compared exactly). Tighten both once the
verdict is clean enough for the noise to be legible.

⚠ TEXT-DIFFERS IS NOT PURELY A WORDING BUCKET. The first run caught
`compressing over-full page by 4.3 staff-spaces` against the oracle's own
figure -- the same sentence carrying a DIFFERENT NUMBER, which is a layout
difference wearing a wording verdict. Read them before assuming.

-------------------------------------------------------------------------------
LOWERING A FLOOR: pass-manifest-decisions.tsv
-------------------------------------------------------------------------------

`ratchet.py update` cannot lower a row -- it takes the better of manifest and
run. When a floor genuinely has to come down, `ratchet.py rebaseline` is the only
way, it requires a written --reason, and it appends every change to
pass-manifest-decisions.tsv (committed, append-only):

    ./ratchet.py rebaseline /tmp/run.tsv --reason "why" [--only a.svg,b.svg]

--only exists so rows that come down for DIFFERENT reasons are recorded
separately rather than under one averaged excuse.

The manifest records what the floor IS; the decisions file records what happened
to it. Before this existed, nothing in the repository could tell an earned floor
from an unearned one, which is why 97 unearned rows stood for three sessions.

The first 97 entries are that re-baseline, 1,541 -> 1,444.


================================================================================
THE MIDI SCOREBOARD
================================================================================

The layout harness above grades PAGES. This one grades MIDI, and it is a separate
oracle, a separate subsuite and a separate comparator because none of those three
is shared.

  tests/regression/midi          73 input files (upstream's input/regression/midi)
  reference-midi/midi            90 .midi rendered by the pinned 2.27.2 binary
  reference-midi/manifest.tsv    sha256 + size per file, committed
  generate-midi-reference.sh     regenerates the above
  compare-midi.py                grades a candidate directory against it

  UNLIKE THE PAGES, THE .midi FILES ARE COMMITTED. The layout reference is 62 MB and
  is gitignored with a manifest standing in for it; the MIDI reference is 364 KB, so
  committing it is cheaper than the machinery for not committing it.

RUNNING IT

    dotnet run --project tools/regression-harness/BatchDriver -c Release -- \
        tests/regression/midi <out-dir>
    python3 compare-midi.py reference-midi/midi <out-dir>

  BatchDriver writes .midi beside .svg. Its startup clean and end-of-run self-check
  cover .svg only -- that is deliberate, because a score's page and its MIDI are
  independent outputs and one may exist without the other.

THE SELF-CHECK, AND WHY IT IS NOT OPTIONAL

    python3 compare-midi.py reference-midi/midi reference-midi/midi

  must report 90 of 90 MATCH. That is what would catch this script reading zero
  events out of every file -- exactly the failure compare-output.py had, unnoticed,
  for four sessions before it was found.

WHAT IS NORMALISED, AND WHAT DELIBERATELY IS NOT

  A byte comparison is tempting and wrong: two files can carry the identical
  performance and differ in bytes. compare-midi.py parses both sides and compares
  event streams, applying exactly four normalisations, each one a place where the
  format admits more than one spelling of the same music:

    1. delta times become absolute ticks
    2. running status is expanded
    3. the "LilyPond <version>" stamp is replaced by a marker (its presence and
       position are still compared -- only the payload is elided)
    4. end-of-track is dropped

  EVERYTHING ELSE IS COMPARED EXACTLY, INCLUDING THE ORDER OF EVENTS SHARING A TICK.
  That order is not incidental: lily/midi-chunk.cc's Midi_track::add exists solely to
  force instrument changes ahead of the notes they apply to, so a comparator that
  sorted within a tick would grade away the thing that code is for.

VERDICTS -- the same vocabulary compare-output.py uses

    MATCH           every track's event stream is identical
    EVENTS-DIFFER   both parse, the streams differ
    MISSING         the port produced no file
    UNPARSEABLE     the port produced something that is not an SMF

  UNPARSEABLE is worth watching for the same reason it is on the layout side: it
  means the port is emitting BYTES that are wrong, not a layout that is wrong. The MIDI
  group met it on eleven files from a Rational conversion that threw mid-write.

THERE IS NO MIDI RATCHET YET. The layout floor is a per-file manifest with a gate;
the MIDI side is currently a scoreboard read by hand. It should grow one once the
figures stop moving in large steps.

===============================================================================
NAMED-GLYPH IDENTITY, AND THE GLYPH-IDENTITY INDEX
(this CHANGED the comparator's contract)
===============================================================================

THE CONTRACT NOW READS (D29, as restated):

    Glyph identity is NAMED-GLYPH identity, byte-verified against each side's own
    font. Everything else remains byte-exact. Visual and tolerance comparison
    remain forbidden.

  This REPLACES the rule this harness originally ran on --
  "two glyphs are the same glyph exactly when their path data agrees". If you are
  reading an older note that states the byte rule, the note is stale.

WHY IT HAD TO CHANGE. Both engravers copy a glyph's outline verbatim out of the
.svg font they were built against, and the two sides read DIFFERENT BUILDS of the
same font: the port ships its own Emmentaler (FontForge 20230101, built from the
vendored mf/ mirror -- the deliberate own-build decision, which STANDS),
while the oracle's came from the official 2.27.2 release build (FontForge 20200314).
The design is the same -- LILC, LILY, every advance and every cmap mapping are
byte-identical between the builds, and sampled bounding boxes agree to 0.00
font-units -- but the two FontForge versions serialize the outlines differently.

  Measured on emmentaler-20's black
  notehead specifically: that ONE outline string appears in 1,242 reference pages
  and ZERO candidate pages, while the port's serialization of the same glyph
  appears in 1,089 candidate pages and ZERO reference pages -- a perfect per-page
  substitution. (Counting the same glyph BY NAME across every optical size, as the
  comparator now does, reads 1,346 reference and 1,322 candidate pages. The two
  figures measure different things and are both right; the byte figures are
  per-serialization, the name figures per-glyph.)

  Under the byte rule, no page carrying music could EVER read MATCH.

WHAT THE IDENTITY IS. For a <path> whose transform is the pure glyph scale
(scale(s, -s) -- dump-path's signature, and nothing else emits it):

    (the SET of glyph names whose outline bytes equal this path's d, looked up in
     THAT SIDE's own fonts, PLUS the transform's scale string)

  A name SET, because two names in one font can legitimately share outline bytes:
  a whole note and a half note shape-note head really are the same drawing. There
  are 466 such classes per side. They are recorded in the index header and NEVER
  resolved to a winner -- forcing a single name would plant a wrong answer for the
  page that draws the other glyph.

  The SCALE STRING is part of the identity because the name alone does not say
  which OPTICAL SIZE was drawn; without it, a glyph from emmentaler-11 and the
  same-named glyph from emmentaler-26 would collapse into one identity. It is
  carried verbatim, not parsed -- no rounding, no tolerance. (Recorded because it
  surprised the session that implemented this: the scale did NOT participate in
  the identity under the byte rule. It did not need to.)

  THIS IS NOT FUZZY MATCHING. A path is still identified by its EXACT bytes; those
  bytes are simply resolved to the NAME of the glyph they are a verbatim copy of.

FAIL-STRICT. A `d` that resolves to no glyph name on its side keeps raw-byte
identity -- the original byte-rule behaviour exactly. Unresolvable can only ever make
the comparison STRICTER, never looser.

EACH SIDE RESOLVES AGAINST ITS OWN FONTS, which is why the two halves of the index
are never merged into one lookup. The ONE exception is the self-check, which
compares the reference directory against ITSELF: those bytes came out of the
oracle's fonts on both sides, so both sides resolve against the reference half.
That case is DETECTED (the two directories are the same path), not configured.

THE INDEX

    glyph-identity.tsv            committed; side / font / glyph-name / sha256
    generate-glyph-identity.py    regenerates it; --check verifies it

  Only HASHES are committed. No font file and no glyph outline from either side is
  redistributed, and the COMPARATOR never reads a font or needs the oracle
  installed -- the oracle is required only when the index is GENERATED, the same
  model reference generation already uses. The header carries provenance: the
  generation date, both font directories, the sha256 of all 18 source font files,
  and the full list of duplicate-byte name classes.

  Current content: 9 fonts and 5,872 named outlines PER SIDE, with identical
  glyph-name sets in every font, and all 5,872 names resolving to the SAME
  name-set on both sides (100.00% class agreement -- the two builds never disagree
  about which glyphs share bytes). 2,131 of the 5,872 names (36.3%) serialize
  IDENTICALLY in both builds; the rest are what made the byte rule unworkable.

*** THE NORMALIZATION IS LOAD-BEARING -- get it wrong and this all silently does
    nothing. ***

  The FontForge SVG fonts carry literal NEWLINES inside long `d` attribute values
  (318 of 662 glyphs in the port's emmentaler-20, 338 in the oracle's), and the
  emitted pages inherit them. compare-output.py reads pages with ElementTree, and
  XML attribute-value normalization turns newline, CR and tab inside an attribute
  value into a SPACE -- so the `d` string the comparator sees is NOT the file's raw
  bytes. Today that cancels, because both pages go through the same parser. The
  index is built from FONT FILES, so it must apply the same normalization: any run
  of whitespace to a single space, then strip. The generator reads the fonts with
  ElementTree for exactly this reason.

  If it ever stops agreeing, every lookup misses, fail-strict takes every path, and
  the mechanism becomes a no-op THAT STILL PASSES the reference-against-reference
  self-check. That is what the canary below is for.

THE STANDING VERIFICATION PROTOCOL -- all four, after any comparator or font change

    python3 compare-output.py --selftest
        Four miniature documents on a synthetic index, each a relationship with a
        control: (i) same name, different serializations -> MATCH; (ii) unresolvable
        candidate bytes -> not a match; (iii) different names, same scale -> differ;
        (iv) same name, different scale strings -> differ. Plus the CANARY: a real
        oracle page must resolve a real glyph name. The miniature cases run on
        invented path data and would pass even if the real normalization were
        broken; the canary is the one that would catch it.

    python3 compare-output.py reference/svg reference/svg
        must still report 2316 of 2316 MATCH.

    python3 generate-glyph-identity.py --check
        regenerates the candidate side from the shipped assets and diffs it against
        the committed index, then re-asserts two-sided inventory equality. This is
        the fence for the index going STALE: our fonts are ours to regenerate and
        nothing else would notice if they moved. (The reference side cannot drift --
        it changes only if the pinned oracle changes, which is a project-wide event.)

    python3 compare-midi.py reference-midi/midi reference-midi/midi
        must still report 90 of 90 MATCH.

  compare-output.py also prints a per-side RESOLUTION RATE on every run
  ("N of M glyph paths resolved to a name"). A normalization slip reads as 0.0%
  there while every verdict count stays superficially plausible, so it is worth a
  glance every time.

--raw-glyph-bytes is the A/B switch: it forces the original byte rule. It is
DIAGNOSTIC ONLY -- nothing is ever graded against it. It exists because trap 17
says an attribution is honest only if made by disabling the change and re-running.

WHAT LANDING THIS ACTUALLY BOUGHT, AND WHAT IT DID NOT (measured)

  100% of glyph paths on BOTH sides now resolve by name (73,292 reference, 72,987
  candidate). Verdicts moved 0 regressed / 43 improved, every one
  GLYPHS-DIFFER -> PLACEMENT-DIFFERS.

  IT PRODUCED NO NEW MATCH ROWS, which the plan for this work expected it to. The
  notehead substitution was not the last wall in front of G1; it was the first of
  three. Behind it, measured over the 2,215 pages still differing:

    * 2,081 pages differ by an INVISIBLE <rect> -- fill="none" stroke="none", a
      zero-ink bounding box the oracle emits and the port does not. On 782 of them
      it is the ENTIRE inventory difference. This is the single biggest thing
      between the port and its first bulk MATCH population.
    * ~1,000 more carry a text-run difference (the systematic 4th-decimal
      text-font-size skew already recorded in CONDENSED_PLAN_2 §3.6).
    * the remainder are real music-glyph, drawn-path and line differences.

  Those are engine/backend work and were deliberately NOT touched here: this change
  is comparator-side only, and the candidate bytes before and after it are identical.

  *** FOLLOW-UP THE SAME DAY: the rect is FIXED. ***

  The SVG backend had no `url-link` case, so upstream's <a> and its invisible
  hot-zone rect were dropped on every page carrying a link. Restoring it moved 782
  pages GLYPHS-DIFFER -> PLACEMENT-DIFFERS with zero regressions -- exactly the
  count predicted above, to the file. It produced no new MATCH rows either,
  because the rect's EXTENTS differ (oracle h=2.2001, port h=2.0746 on the
  tagline): the text skew was standing behind it.

  So the list above now reads: 1,433 pages still differ at inventory level, led by
  TEXT RUNS -- 501 differ by a text-run ALONE -- with rect involvement down to 450
  from some other rect population, then real music-glyph, drawn-path and line
  differences. A cheap high-signal probe for the text gap, needing no sweep: on any
  page with a tagline, compare the hot-zone rect's width and height against the
  oracle's.


================================================================================
DOCUMENTATION PARITY -- THE DOCS RUN (G8)
================================================================================

  ly/generate-documentation.ly is the port's other oracle comparison, and it is
  the ONLY one that grades how values PRINT. It is separate from the sweep, and
  it is cheap.

RUNNING IT

    dotnet run --project tools/regression-harness/DocsDriver -c Release -- /tmp/port-docs
    mkdir -p /tmp/oracle-docs && cd /tmp/oracle-docs \
        && ~/ClaudeHome/oracle/lilypond-2.27.2/bin/lilypond \
           ~/ClaudeHome/oracle/lilypond-2.27.2/share/lilypond/2.27.2/ly/generate-documentation.ly
    for f in /tmp/oracle-docs/*; do cmp -s "$f" "/tmp/port-docs/$(basename $f)" \
        || echo "DIFFER $(basename $f)"; done

  EXPECT NO OUTPUT AT ALL. All nineteen files match byte for byte, internals.texi
  at 2,619,154 bytes. The oracle takes 1.5 s and the port about 40 s, so the
  references are regenerated rather than committed.

  ⚠ THE OUTPUT DIRECTORY IS THE PROCESS WORKING DIRECTORY, NOT AN ARGUMENT. The
  script writes through open-output-file with RELATIVE names, which is why both
  sides are invoked by changing into the target directory.

WHY IT BELONGS BESIDE THE SWEEP

  The layout comparator grades glyph INVENTORY and POSITION and nothing else
  (see THE COMPARATOR above). It is structurally blind to every one of:

    * how a procedure prints -- its name, its formals, its source location;
    * how a smob prints -- #<Pitch e' >, #<Mom 0>, #<Duration 1 >,
      #<unpure-pure-container ... >;
    * what a module reports as its public interface;
    * whether a docstring reached the thing that documents it.

  A defect in any of those is invisible to the ratchet no matter how green it is.
  The session that closed G8 found FIVE separate classes this way, three of which
  nothing else in the harness could have seen -- and two of those five were
  general engine/interpreter defects that the manual merely happened to be the
  first thing to show.

  So: run the docs comparison whenever the Scheme layer, an entry point, a print
  representation or anything in the module system moves. It costs forty seconds.

A SHARP EDGE WORTH KNOWING BEFORE READING A DIFF

  Upstream's own procedure printer has a re-entry latch it never clears
  (libguile/programs.c:108-143): once pretty-print's truncating port aborts from
  inside it, EVERY procedure in that process prints as #<program ADDR CODE>
  instead of #<procedure ...>. The oracle's manual therefore carries 206 of the
  degraded form against 29 ordinary ones, and the port reproduces the latch
  deliberately (standing rule 2). Two things follow for anyone debugging a diff
  here:

    * the hexadecimal address is stripped by scm->string but its WIDTH is not --
      it is stripped AFTER pretty-print has chosen line breaks, so an address of
      the wrong length moves breaks on lines containing no address; and
    * the soft port's BUFFERING decides whether an abort lands inside the printer
      at all, so a change to port buffering changes which procedures print which
      way. CodeBrix.LilyScheme's SoftPortBufferingTests fences the model.

================================================================================
THREE RULINGS THE HARNESS NOW CARRIES (2026-08-16, PARITY 15)
================================================================================

G1's definition has always read "every root regression file + other/ MATCHes the
pinned corpus at NAMED-GLYPH identity, MINUS A COMMITTED, REASONED SKIP LIST;
files whose divergence is in an ASSET the port builds or ships rather than in the
engine match committed PORT-GENERATED baselines". Both clauses existed only as
sentences until now. Three rulings gave them artifacts.

/!\ THE SECOND CLAUSE WAS RE-WORDED ON 2026-08-17 and the old wording was untrue
about every row it covered. It read "fallback-dependent files (D23) match committed
PORT-GENERATED Roboto baselines (Roboto pinned at 1.0.209.315)". Neither half held:
Roboto was never in the fallback chain at all (D23 amended, PARITY 20), and none of
the rows on the baseline are fallback-dependent -- D43's six and R15's two are
font-BUILD-dependent, which is a different thing. Left alone, the sentence describing
G1 when it goes green would have been false about eight of its rows.

  g1-skip-list.tsv          R11 -- the rows ruled OUT of G1
  baseline/svg + baseline-manifest.tsv
                            R9  -- the rows graded against the PORT instead
  compare-output.py's R10 post-pass
                            R12 -- four rows graded WITH a stated bound

THEY ARE THREE DIFFERENT THINGS AND MUST NOT BE MERGED.

  * A SKIP-LIST row is not required to match at all. Eight of them, all one
    mechanism (D31's tofu, extended by name in R8(A)). The file is read by
    NOTHING automatically and has no reporting script: it exists so that
    "2,316 pages match except these eight, ruled out on this date for this
    reason" is answerable from the REPOSITORY, not from a plan document that
    does not ship.

  * A BASELINE row IS required to match -- the PORT's own frozen output rather
    than the oracle's:

        python3 compare-output.py --baseline baseline/svg candidate/svg

    Expect 6 of 6 MATCH. --baseline makes BOTH sides resolve glyph names against
    the PORT's half of glyph-identity.tsv, and it is load-bearing: without it the
    baseline side resolves 5 of 73 paths and every row reads GLYPHS-DIFFER. A
    baseline claims NO DRIFT and nothing else (rule 33 forbids reading a value
    recorded from the port's own output as a correctness result); the
    correctness claim for that mechanism is GlyphOutlineSkylineTests in the
    Engine suite. Landing a baseline without its fence is the failure mode.
    Provenance and the re-freezing rule are in baseline-manifest.tsv's header.

  * The R10 POST-PASS grades normally and then, on four named files only, asks
    whether the ENTIRE inventory difference is text elements identical in family
    and content whose font-size differs by no more than 0.0005 -- and if so
    re-grades the reconciled inventory and REPORTS the upgrade, per row and as a
    count. It does not touch D29's identity function, which stays byte-exact, and
    it does not assert MATCH: a placement difference on those four files still
    reads PLACEMENT-DIFFERS. Fenced by --selftest cases (ix) through (xiii), whose
    controls are a file R10 does not name, a size beyond the bound, a second
    difference alongside the size, and a page whose own sizes sit within the
    bound.

WHY NOT ONE MECHANISM FOR ALL THREE. Because they make different claims, and a
row that stops matching has to be distinguishable from a row that was never
required to. Merging them would lose exactly that.
