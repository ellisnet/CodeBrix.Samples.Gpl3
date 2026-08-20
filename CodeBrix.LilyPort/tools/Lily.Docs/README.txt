================================================================================
CodeBrix.LilyPort -- tools/Lily.Docs/
================================================================================

Lily.Docs generates the port's nineteen documentation files and renders MANUALS
from them -- print-shaped HTML and PDF -- through the two published
CodeBrix.Texinfo packages.

This is Phase 5 (LilyDocs) of the LilyPort project. The live board is
~/ClaudeHome/PLAN_codebrix_lilyport_phase5_lilydocs_2026-08-18.md.

--------------------------------------------------------------------------------
WHAT THIS IS, AND WHAT IT IS NOT
--------------------------------------------------------------------------------

A REPO TOOL. Decision D52, ruled by Jeremy 2026-08-18: Lily.Docs is
BatchDriver-class tooling in its own solution and SHIPS NOTHING.

    ***  CodeBrix.LilyPort must NOT acquire a dependency on the Texinfo ->  ***
    ***  Html2Pdf -> MarkupParse/StyleSheetParse/three-font-package chain.  ***

That is why Lily.Docs.slnx exists separately from CodeBrix.LilyPort.slnx and why
no packable project references anything under this directory. A future session
that wants a shipped CodeBrix.LilyPort.Docs assembly must RE-OPEN D52 with
Jeremy rather than assume it; the cost is that whole chain landing in the
package's dependency tree.

Nothing here converts Texinfo. Decision D28 gives that job to the published
packages, pinned below. A rendering defect is fixed in this layer or in
~/GitHome/CodeBrix.Texinfo (its own repository, its own authorization) -- NEVER
by editing generated output, the generator, or a vendored source file.

--------------------------------------------------------------------------------
RUNNING IT
--------------------------------------------------------------------------------

    cd tools/Lily.Docs
    dotnet run --project src/Lily.Docs -c Release -- internals --html --pdf \
        -o /tmp/lilydocs-out

    internals                the manual to render (see MANUALS below)
    --html / --pdf           which formats; with neither, both
    -o DIR                   output directory (default ./lilydocs-out)
    --generated DIR          reuse the nineteen files a previous run wrote,
                             instead of generating them again (~40 s saved)
    --warnings               print every warning message, not just the counts
    --baseline               freeze the expected-warnings baselines from this run
    --no-snippets            render with NO engraver -- the CONTROL run. Every
                             snippet becomes source text, which is also exactly
                             what a manual whose engravings all failed looks like.
                             Useful for separating "did the includes resolve?"
                             from "did the music engrave?", and it costs seconds
                             instead of minutes.

Every snippet that fails to engrave has its COMPOSED SOURCE written to
<output>/failed-snippets/, one file per failure, headed by the location and the
message. A failure message names a line in a file that was never on disk -- the
engine is handed the composed text in memory -- so the message alone cannot be
acted on, and composing is this tool's job while engraving is the engine's.

Generation is an engine job of roughly forty seconds warm. During QA, generate
once and then re-render with --generated pointed at that directory. ⚠ Point it at
the `en' directory INSIDE the output, not at its parent: that is where the port's
files are and RenderPaths refuses any other name.

The notation manual's own render is another five minutes or so -- two and a half
thousand engravings, sequential because the engine is process-global. The Texinfo
half of it is under a second; all of the time is music.

--------------------------------------------------------------------------------
TESTS -- THE VSTest DIALECT, CHOSEN DELIBERATELY
--------------------------------------------------------------------------------

    dotnet test Lily.Docs.slnx -c Release

This repository contains BOTH xunit.v3 dialects, and picking one for a new
solution is a decision, not a default:

  * The main CodeBrix.LilyPort solution is the VSTest dialect.
  * tools/Lily.Shell is the Microsoft.Testing.Platform dialect, where the trx
    logger switch is ignored (two MTP0001 warnings) and plain `dotnet test' is
    refused on the .NET 10 SDK.

Lily.Docs takes the VSTest dialect so the command above works exactly as
written, with trx logging available. Do not add
UseMicrosoftTestingPlatformRunner without changing this file in the same edit.

⚠ THE ENGINE PROJECTS ARE LISTED IN Lily.Docs.slnx ON PURPOSE. A project a
solution does not name is built with its OWN default configuration, so
`dotnet test -c Release' silently ran the suite against a DEBUG engine until
they were added (measured 2026-08-18). If a project is ever removed from the
solution file, check what configuration it actually builds in before believing
a green Release run.

--------------------------------------------------------------------------------
THE INCLUDE SEARCH PATHS -- THEY DECIDE WHAT A MANUAL MEANS
--------------------------------------------------------------------------------

There are TWO of them, they reach different consumers, and conflating them costs
engravings rather than errors. Both are built in RenderPaths.

(1) IncludeSearchPaths -- where the TEXINFO renderer resolves @include files and
    the files @lilypondfile names:

    1. the GENERATED directory   the port's own nineteen files, written into a
                                 directory NAMED en/. First, always: the
                                 generated bytes are the specification.
    2. its PARENT                because the manuals include those files as
                                 `@include en/markup-commands.tely', eighteen
                                 times over. Upstream reconciles the same two
                                 facts the same way: -I $(outdir)/en -I $(outdir)
                                 in Documentation/GNUmakefile.
    3. the CORPUS directory      ../../Documentation -- the manual prose.
                                 Supplied only for manuals that need it; the
                                 Internals Reference is rendered WITHOUT it, on
                                 purpose, so that the vendored assets are proved
                                 to carry the render rather than being quietly
                                 stood in for by the mirror.
    4. CORPUS/en/included        for the one @lilypondfile written as a bare name.
    5. the ASSETS directory      assets/ -- the vendored GFDL support files.
    6. the VERSION directory     where the version.itexi stand-in was written.

    ⚠ THIS LIST IS NOT THE WHOLE SEARCH PATH, AND IT IS NOT FIRST. The package
    searches the source file's own directory and that directory's PARENT ahead of
    anything a caller supplies. For a corpus manual that means Documentation/en
    and Documentation come first, so "generated first" is NOT what the ordering
    above buys. What makes it true is that the mirror holds NONE of the port's
    nineteen outputs -- they are build products there exactly as upstream --
    and CorpusMirrorTests asserts precisely that, because it is the assumption
    the claim actually rests on.

(2) SnippetIncludePaths -- where the ENGINE resolves the files a snippet's own
    \include and \epsfile name. This is upstream's LILYPOND_BOOK_INCLUDE_DIRS,
    reproduced: the generated directory and its parent, then CORPUS/en,
    CORPUS/pictures, CORPUS/en/included and CORPUS.

    ⚠ IT IS NOT OPTIONAL AND ITS ABSENCE DOES NOT LOOK LIKE ITS ABSENCE. Without
    it the notation manual lost 76 engravings, and not one of them was reported
    as a missing file: an unresolved \include leaves the identifiers it would
    have defined undefined, so what comes back is a SYNTAX ERROR at the line that
    uses one -- `syntax error, unexpected '}'' at the closing brace of
    \layout { \neumeDemoLayout }. Nothing in that message says "include".

    It is installed with ly:parser-append-to-include-path, an upstream primitive
    the port implements, ONCE PER PROCESS: the engine's parser session is cached
    and shared, and RestoreDefaults does not touch the include path, so appending
    per snippet would grow the path by six entries two and a half thousand times.

⚠ Rule 7 of the LilyPort plan stands unbroken here: NO BUILD OR TEST STEP
TOUCHES ~/GitHome/lilypond. The corpus is read from this repository's own
Documentation mirror (decision D49(b)). The only thing that reads the upstream
checkout is a texi2any ORACLE run performed by hand, which is not a build step
and is deliberately pointed at upstream so the oracle stays independent of our
copy -- see Documentation/README.txt.

--------------------------------------------------------------------------------
VENDORED ASSETS (assets/en/) AND THE VERSION STAND-IN
--------------------------------------------------------------------------------

Three GFDL files, byte-identical to ../../Documentation/en/ and fenced as such
by VendoredAssetTests (decision D49(a)):

    macros.itexi           the file every manual includes
    common-macros.itexi    pulled by macros.itexi; defines @iref
    cyrillic.itexi         pulled by common-macros.itexi

⚠ THE PLAN ORIGINALLY SAID TWO FILES. cyrillic.itexi sits one level below where
that measurement stopped; rendering without it costs an Include warning and the
macros it defines. VendoredAssetTests COMPUTES the closure rather than listing
it, so a fourth file added upstream fails at the moment it is vendored in
instead of appearing as one warning among twelve.

version.itexi is NOT vendored and never will be. Upstream generates it at build
time from its VERSION file; Lily.Docs writes its own stand-in at render time from
LilyPortInfo.UpstreamVersion, so the manuals always state the version of the
engine that generated them. A vendored copy would freeze a version string that
disagrees with the engine the moment the port's version moves, and do it
silently.

--------------------------------------------------------------------------------
THE SNIPPET ENGRAVING SEAM (src/Lily.Docs/Snippets/) -- WAVE LD2
--------------------------------------------------------------------------------

A manual's @lilypond snippets are engraved by the port's OWN engine, through the
Texinfo package's ILilypondSnippetRenderer. Four types do it:

    TexinfoPageGeometry     the page geometry lilypond-book hands every snippet
    SnippetOptionSet        the option DERIVATIONS -- the computed half
    LilypondSourceComposer  the port of compose_ly and its three templates
    EngineSnippetRenderer   the renderer: composes, engraves, counts

The authority is book-mirror/ (decision D49(c)) -- four mirrored lilypond-book
sources, read and ported with provenance, never executed.

WHAT IS COMPARED, AND WHY IT IS THE CHEAP CHECK

Engraving parity is already a closed claim: gate G1 covers it. What wave LD2 adds
is the WRAPPER, and a wrapper's entire output is a text file -- so the fence is
TEXTUAL, not visual. composed-reference/ holds what the oracle's own lilypond-book
composed for 28 option cases, and LilypondSourceComposerTests asserts our
composition against it. 27 of the 28 match BYTE FOR BYTE, which additionally
proves the Texinfo package reports snippet line numbers on the same base
lilypond-book does.

    ***  THE COMPOSED SOURCE IS THE PARITY ARTEFACT. Nothing may be added   ***
    ***  to it -- not a paper override, not a handler, not a \version.      ***

THE ENGRAVING DIRECTIVES, AND WHY THEY ARE NOT PART OF THAT SOURCE

lilypond-book shapes its output on the COMMAND LINE, not in the file: its texinfo
formatter appends -dseparate-page-formats and -dtall-page-formats, and cropping
rides on those. The port has no command line (decision D14 replaced it), so the
same instructions arrive as engine configuration, wrapped AROUND the composed
source by EngineSnippetRenderer.EngravingTextFor. Two things are needed and both
are measured:

  * RESTORE THE PAGE HANDLER. The composed source includes
    lilypond-book-preamble.ly, which repoints default-toplevel-book-handler at
    print-book-with-defaults-as-systems. The port does not implement that output
    path (Paper_book::classic_output is unported, by design), so the book is
    processed and NOTHING IS WRITTEN -- books=0, errors=0, no diagnostic, no
    picture. The snippet vanishes in complete silence. The prologue saves the
    runner's handler and the epilogue puts it back.

    ⚠ AND THE EPILOGUE ALONE IS NOT ENOUGH -- measured at wave LD3, thirty-five
    snippets. Restoring the handler at the END of the file is in time for the
    IMPLICIT toplevel book, which is collected when the parse finishes, and far
    too late for an EXPLICIT one: a snippet that writes \book { ... } hands it
    over the moment the block closes, through toplevel-book-handler, which the
    preamble has by then pointed at print-book-with-defaults -- a route that
    never reaches the runner's collector. Same silence, same books=0.

    So the prologue also re-points the two functions those handlers CALL,
    print-book-with-defaults-as-systems and print-book-with-defaults, at the
    collector. Whatever the preamble installs afterwards still arrives. All three
    definitions are toplevel, and RestoreDefaults reverts a run's toplevel
    definitions, so they are per-snippet and leak into nothing.

  * ASK FOR ONE PAGE SIZED TO THE MUSIC. \paper { page-breaking =
    #ly:one-page-breaking }. Measured for one two-note snippet: the default
    breaker gives 156.0mm x 273.1mm -- an A4-tall band of whitespace under the
    music -- and one-page-breaking gives 156.0mm x 20.8mm. A 160-note snippet
    grows to 159.4mm x 126.2mm, i.e. it STACKS into systems instead of running
    off in one line, which is why ly:one-line-auto-height-breaking was NOT the
    choice (it left the height at 273.1mm).

    The WIDTH needs nothing: the preamble sets use-paper-size-for-page to false
    and the composed \paper block computes line-width down by the padding, so
    the page is already the music's width. That is upstream's own mechanism doing
    upstream's own job.

COUNT INVOCATIONS AND FAILURES, NEVER COMPLETION

The coordinator CATCHES a renderer that throws and falls back to showing the
snippet's source with one warning. A manual that rendered "successfully" is
therefore compatible with every snippet having failed. EngineSnippetRenderer
counts InvocationCount, EngravedCount, PageCount, FailureCount and DeclineCount
for exactly that reason, and every gate asserts on those rather than on the
render returning. EngineSnippetRendererTests keeps a deliberate control -- the
same document with NO renderer registered -- so the suite can tell the two apart.

--------------------------------------------------------------------------------
THE SUITE RUNS SERIALLY, AND IT HAS TO
--------------------------------------------------------------------------------

tests/Lily.Docs.Tests/AssemblyBehavior.cs disables xunit parallelization for the
whole assembly. The engine is process-global -- one interpreter, one session, and
ONE PROCESS WORKING DIRECTORY -- and both things this suite does change that
directory: generation because upstream writes its nineteen files by relative
name, engraving because each snippet gets its own scratch.

⚠ Wave LD1 had one engine-driving collection so this never showed. LD2 added a
second, and the two raced immediately: the Internals Reference fixture failed
with "the source for manual 'internals' is not at .../generated/internals.texi",
because a snippet render had moved the working directory out from under the
generation step. Note the SHAPE of that failure -- it names a missing generated
file, so it reads as a generation defect. Nothing in it points at parallelism.

--------------------------------------------------------------------------------
AND GENERATION HAPPENS ONCE PER PROCESS
--------------------------------------------------------------------------------

tests/Lily.Docs.Tests/GeneratedDocumentation.cs generates the nineteen files once
for the WHOLE assembly, and every fixture reads that one directory.

⚠ THAT IS NOT AN OPTIMISATION. IT IS THE ONLY NUMBER THAT WORKS. The first call to
DocumentationGenerator.Generate writes all nineteen files in about forty seconds;
every later call IN THE SAME PROCESS returns in a tenth of a second having written
NOTHING, and reports all nineteen as missing. Upstream never meets this because it
gets a fresh process per run.

⚠ AND THE SECOND CALL DOES NOT THROW. It returns a result whose IsComplete is
false, which a caller that does not look is free to ignore -- and then renders a
manual out of an empty directory. That is exactly what wave LD3 did on its first
try, and the shape of the damage is the thing to remember: the render SUCCEEDED,
and the eighteen appendices were simply not in the manual, behind eighteen Include
warnings among the ones a baseline already tolerates. GeneratedDocumentationTests
pins the behaviour so the constraint cannot quietly outlive its reason, and the
notation fixture throws on an incomplete generation rather than rendering anyway.

A new engine-driving fixture uses GeneratedDocumentation.EnsureGenerated() and the
directory it holds. It does not call the generator.

--------------------------------------------------------------------------------
BASELINES (expected-warnings/)
--------------------------------------------------------------------------------

    <manual>.tsv           warning category -> count, plus a TOTAL line
    <manual>-pdf.tsv       PAGES and PDF_WARNINGS
    <manual>-snippets.tsv  ASKED, ENGRAVED, PICTURES, FAILED, DECLINED and
                           DOCUMENT_IMAGES -- what the engraver was asked to do
                           and what came back

⚠ ASKED AND FAILED ARE THE LOAD-BEARING NUMBERS in the third file. The Texinfo
package CATCHES a renderer that throws and shows the snippet's source instead, so
"the manual rendered" is compatible with every engraving having failed. Freezing
what was ASKED FOR as well as what came back also catches the opposite failure:
a search path that stopped resolving the appendices would quietly stop asking.

⚠ A ZERO NEEDS ITS CONTROL. The notation gate asserts ZERO Include warnings, and
a warning channel that had stopped reporting would produce the same zero.
IncludeWarningControlTests renders snippets.tely in the same suite and asserts
exactly thirty-nine Include warnings, naming the thirty-nine files. That is what
separates "everything resolved" from "nothing is being reported".

A baseline is FROZEN FROM A MEASURED RUN THAT WAS THEN READ. It is asserted
exactly and in BOTH directions: a category that shrinks is as much a signal as
one that grows. It is never regenerated to make a test pass. When a change to
the render is deliberate, re-freeze with --baseline, READ the diff, and say in
the commit what moved and why.

--------------------------------------------------------------------------------
MANUALS
--------------------------------------------------------------------------------

    internals    LilyPond Internals Reference -- one of the port's own nineteen
                 generated files, and the only one that is a complete standalone
                 manual. Zero snippets, one @include, 810 nodes. Wave LD1.

    notation     LilyPond Notation Reference -- corpus prose whose APPENDICES are
                 the port's other eighteen generated files, so rendering it is the
                 act the whole phase is named for. Wave LD3, HTML only; its PDF is
                 wave LD4's, behind decisions D50 and D51.

The seven remaining manuals arrive with wave LD5. They are deliberately absent
from ManualCatalog rather than listed and unsupported, so an unknown manual name
is an error rather than a render that quietly produces nothing.

ManualCatalog.IncludeWarningControl holds a tenth entry that is NOT a manual and
is deliberately NOT in ManualCatalog.All: snippets.tely, whose forty @include
lines name thirty-nine files that exist in no checkout. It is the paired control
for every zero-include-warning gate -- see BASELINES below.

Decision D48 was RULED 2026-08-19: nine manuals are owed in both formats --
internals, notation, learning, usage, extending, essay, changes, music-glossary
and contributor. web.texi is excluded, and snippets.tely is not a deliverable
manual at all but the include-warning CONTROL for the zero-include-warning gates
(exactly 39 Include warnings, HTML only, no QA drop).

The snippet engraving seam LD3 and LD5 need is DONE -- wave LD2, see THE SNIPPET
ENGRAVING SEAM above.

--------------------------------------------------------------------------------
PACKAGE PINS -- THE TWO MOVE TOGETHER
--------------------------------------------------------------------------------

    CodeBrix.Texinfo2Html.MitLicenseForever   1.0.220.120
    CodeBrix.Texinfo2Pdf.MitLicenseForever    1.0.220.120

Both are named in src/Lily.Docs/Lily.Docs.csproj even though the PDF package
brings the HTML one transitively, so the pin is visible in one place. Bumping
one without the other is the mistake the pin discipline exists to prevent. A
package fix happens in ~/GitHome/CodeBrix.Texinfo, ships as a PAIR at one
version, and lands here as a pin bump verified by re-running the gates.

================================================================================
