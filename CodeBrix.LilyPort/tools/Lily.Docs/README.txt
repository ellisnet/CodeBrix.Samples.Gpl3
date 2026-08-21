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
    --html / --pdf           which formats; with neither, both. ⚠ ASKING FOR BOTH
                             IS ONE RENDER, NOT TWO -- see THE PDF STAGE below
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
half of it is under a second; all of the time is music. Asking for BOTH formats
does not double that: one pass over the source feeds both outputs.

--------------------------------------------------------------------------------
THE SAME CAPABILITY FROM THE SHELL -- Lily.Shell's `docs' COMMAND
--------------------------------------------------------------------------------

Decision D52 ruled Lily.Docs a repo tool that ships nothing AND a `docs' command
in Lily.Shell, so the manuals are reachable without building a separate tool. The
command lives in tools/Lily.Shell/src/Lily.Shell.Core/Commands/DocsCommand.cs and
drives THIS project in-process:

    lily> docs                        list the nine manuals, and say whether the
                                      nineteen files have been generated yet
    lily> docs contributor            both formats, into /tmp/lily-shell-docs/
    lily> docs notation --html        one format
    lily> docs learning -o ~/manuals  somewhere else
    lily> docs notation --no-snippets the control run: no engraver, seconds

The manual names are read from ManualCatalog rather than repeated, so a manual
added there is renderable from the shell with no edit here. A hand-kept second
list is exactly how the tool and the shell would come to disagree about what
"nine manuals" means.

⚠ THE SHELL RENDERS; IT DOES NOT FREEZE. Lily.Docs' --baseline switch is
deliberately absent from the command, and Lily.Shell.Core.Tests asserts that
`--baseline' is rejected as an unknown option so the omission cannot be read
later as an oversight. A baseline is frozen from a run that was READ, in the
repository, by the tool that owns the file.

⚠ GENERATION IS ONCE PER SESSION THERE, BECAUSE IT IS ONCE PER PROCESS HERE (see
AND GENERATION HAPPENS ONCE PER PROCESS above). A shell is where the trap is
easiest to hit -- `docs internals' followed by `docs notation' is two calls in one
process -- so DocsRunner generates on the first command and every later one reuses
those bytes. The second generation would not throw; it would report all nineteen
files missing and let the next manual render out of an empty directory.

⚠ THE EIGHT CORPUS MANUALS NEED THE REPOSITORY; internals does not. The corpus
mirror is found by walking up from the running assembly to CodeBrix.LilyPort.slnx
(ToolPaths), so a copy of Lily.Shell moved out of its build tree can still render
`internals' -- the vendored assets travel beside the assembly -- and answers any
other manual with "could not find CodeBrix.LilyPort.slnx above ...". That is the
honest failure: the corpus is 3.4 MB of FDL source that the tool reads, not carries.

⚠ AND IT IS THE REASON Lily.Shell CARRIES THE Texinfo -> Html2Pdf -> SkiaSharp
CHAIN. That chain is refused for CodeBrix.LilyPort because the shipped package
must not carry it; Lily.Shell ships nothing, and MEASURED 2026-08-19 its heads
already carried 561 MB of the identical SkiaSharp 4.151.0 native assets through
CodeBrix.Platform, so the reference cost 45 MB of managed assemblies and two more
font packages rather than the native payload. Roboto and Roboto Mono resolved at
the versions Lily.Shell already pinned, so the app's own font surface did not
move.

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
VENDORED ASSETS (assets/) AND THE VERSION STAND-IN
--------------------------------------------------------------------------------

Three directories, three different reasons, and the difference matters:

    assets/en/      GFDL macro files COPIED from the corpus -- byte-identical to
                    Documentation/en/, and fenced as such. Below.
    assets/bib/     GENERATED OUTPUT that exists nowhere upstream, translated once
                    by the BibTeX oracle. Decision D57 -- see that section above.
    assets/staged/  GPL SOURCE-TREE files the doc build copies in from outside
                    Documentation/. Decision D57 -- see that section above.

Only the first is a copy of something a re-sync can diff against; the other two are
hash-fenced instead, and each carries its own README explaining why.

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
LilyPortInfo.CompatibleWithVersion, so the manuals always state the LilyPond
version of the engine that generated them. (That is the compatible-with version,
not LilyPortInfo.Version, which is LilyPort's own package version -- these are
LilyPond's manuals and their @version{} means the LilyPond release.) A vendored copy would freeze a version string that
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
THE PDF STAGE -- WAVE LD4
--------------------------------------------------------------------------------

Asking for both formats runs the Texinfo source ONCE and makes both outputs from
that one result (ManualRenderer.RenderBoth). This is not a speed tweak:

  * The package's snippet coordinator dedupes only WITHIN a render, so rendering
    HTML and then PDF engraves a manual's music TWICE. For notation that is 2,555
    extra engravings and five extra minutes.
  * Every engraving count in notation-snippets.tsv would double -- and re-freezing
    the baseline from such a run would simply record the doubled numbers as
    correct.
  * Decision D51's ruling is that Lily.Docs hands THE SAME SVG to both outputs.
    Two renders hand each output its own separately engraved copy, which is a
    weaker claim wearing the same clothes.

  The fence is NotationReferenceRenderTests.every_engraving_reached_the_document_
  as_a_picture: the count on one side is the DOCUMENT's and on the other the
  ENGRAVER's, and only the engraver's doubles.

PAGE SIZE -- A4, AND ASSERTED FROM THE FILE

All nine manuals in decision D48's scope declare @afourpaper, and that same
declaration is what TexinfoPageGeometry turns into the 160mm line width written
into every snippet's composed source. Set through the package's own named helper,
HtmlRenderOptions.SetPageSize("A4"), which MEASURES 595x842pt -- true A4
(210x297mm) is 595.276x841.890, so the helper is 0.28pt narrow and 0.11pt tall,
a tenth of a millimetre. The size actually used is recorded in the baseline.

⚠ WAVE LD1 SHIPPED THE INTERNALS REFERENCE AT US LETTER FOR A DAY WITH EVERY GATE
GREEN. Nothing in the suite looked at the paper, and A4-measure music on a Letter
page is wrong in no way a count can see. Both PDF gates now read the /MediaBox
values back out of the written bytes (PdfPageBoxes) rather than asking the options
object what it was told -- an options-side check could never have failed.

PICTURES -- SVG IN, RASTER OUT, AT THE PICTURE'S OWN PHYSICAL SIZE

Html2Pdf places the SVG itself (decision D51, dissolved), so Lily.Docs hands the
same file to both outputs and owns no rasterizer. MEASURED at wave LD4 on the
notation PDF:

  * The placed size is the SVG's OWN size in millimetres. snippet-00001 declares
    139.04mm x 99.5707mm and lands as 1052 x 752 px at 192 ppi = 139.2mm x 99.5mm.
  * 192 ppi is 96 CSS dpi x Options.Html.SvgRasterScale, whose shipped default is
    2.0. That is the whole of the picture-quality decision and it lives INSIDE the
    package; Lily.Docs records it in the baseline rather than setting it.
  * 2,519 of 2,554 embedded rasters sit at exactly 192 ppi. The rest are
    explained: 30 are rounding on small pictures, ONE engraving is wider than the
    A4 text column and is scaled down to fit (252 ppi), and TWO are the manual's
    own @sourceimage photographs placed at their own natural size (134/135 ppi).

FONTS -- AND THE ONE THING THIS WAVE HAD TO ADD

Registration is process-global on the package's font registry. The three text
families (Merriweather serif, Roboto sans, Roboto Mono mono) and Noto Music are
discovered automatically, and Noto Music joins the per-glyph FALLBACK chain by
itself -- which is what renders an inline flat or sharp, supplementary-plane music
symbols included.

⚠ THE TEXT FAMILIES DO NOT JOIN THAT CHAIN, AND THE HTML AND SVG PATHS THEREFORE
DISAGREE. MEASURED: a Greek Psi in a run styled `serif' resolves to Merriweather,
which carries no Greek; in HTML it renders anyway, and in an engraved SVG it
DROPS. The flat in the very same picture came out fine, because Noto Music was in
the chain and nothing else was. PdfTextFallback adds the four text families back,
and its own file records what each one is there to cover.

  MEASURED on the three pictures that carry every one of notation's drops:
  26 distinct code points / 29 occurrences before, 22 / 24 after.

⚠ HEBREW IS IRREDUCIBLE AND IS BASELINED, NOT CHASED. U+05D0-U+05EA is carried by
NONE of the twelve font families reachable through this chain -- checked cmap by
cmap, not inherited. 22 code points, 24 occurrences, all inside ONE engraved
snippet's lyrics (snippet-01161, a Hebrew/Bulgarian lyric example). Decision D47
covers it.

⚠ Options.Html.KeepUncoveredCharacters -- the "draw a visible box instead of
removing it" switch -- is set to TRUE. Decision D56, ruled at wave LD5.

  Wave LD4 left it at the package's shipped false and deferred the ruling to the
  manual expected to make it matter: music-glossary, which carries the only music
  symbols in scope that are PROSE rather than engraved music -- a flat four times
  and a sharp once, inline in a chord-name @multitable set in Merriweather, which
  carries neither character.

  ⚠ IT DROPS NEITHER. MEASURED at wave LD5: music-glossary's PDF earns ZERO
  PDF-stage warnings of any kind, and its flats and sharps are in both outputs.
  Noto Music is on the fallback chain and coverage is decided per glyph against
  the resolved face's own cmap, so the flat splits into a one-character Noto Music
  run without the prose changing font. Across all nine manuals PDF_ITEMS is either
  zero or -- for notation -- entirely SVG text, which this switch does not govern.
  So it changes NOTHING measurable in either position.

  It is therefore ruled on what it does NEXT. A character that silently disappears
  from a manual is invisible to the visual QA this phase is accepted on, while a
  row of boxes is not; the family's standing rule is that a font chain ends at the
  package fonts and a gap it cannot fill must be SEEN. It also makes the two text
  paths agree, since SVG text already draws notdef. The package gives the kept case
  its own warning code (font.uncovered.kept), so the drop baselines stay exact.

WHAT SkiaSharp PUTS IN THE OUTPUT DIRECTORY

SVG rasterization is Skia, so SkiaSharp and HarfBuzzSharp arrive through the
chain with native assets for every RID they ship: MEASURED 550 MB, 40 files
across 17 runtime identifiers, of which this machine uses linux-x64's 15 MB.
That is the price of decision D51 and it is paid by a REPO TOOL that ships
nothing -- CodeBrix.LilyPort's own package is untouched, which is exactly what
decision D52 refused to give up.

--------------------------------------------------------------------------------
THE SEVEN CORPUS MANUALS -- WAVE LD5
--------------------------------------------------------------------------------

learning, usage, extending, essay, changes, music-glossary and contributor, both
formats, from ONE fixture (CorpusManualFixture) because the expensive thing --
generation -- is shared and the renders are not. 438 snippets between them, ZERO
engraving failures. Their per-manual gates are theories over the manual NAMES, so
adding a manual is adding a name.

WHAT HAD TO BE COPIED FIRST, AND HOW IT WAS FOUND

The mirror grew 636 -> 690 files. Fourteen are contributor.texi and its thirteen
chapters; FORTY are pictures. ⚠ NOT ONE of those forty is a literal @image:
@sourceimage EXPANDS to @image{pictures/...}, so the closure is a MACRO expansion
and a survey of Texinfo commands sees none of it. The Phase-5 plan recorded
contributor as having "ZERO real @image uses"; it has one, at
programming-work.itexi:52. That is the same reading error that hid the notation
manual's two pictures at wave LD3 -- its fifth firing, and the reason the closure
here was measured by expanding the macro rather than by grepping the command.

WHAT DOES NOT RESOLVE -- TEN WARNINGS, ONE CAUSE, AND IT IS NOT A DEFECT

Every remaining Include warning across the nine manuals is an upstream BUILD
PRODUCT: a file the doc build makes or stages, which therefore exists in no source
checkout. Decision D57 (ruled 2026-08-19) closed the recoverable ones and left the
two that are not worth recovering.

  essay, 9     pictures/pdf/NAME -- the @iftex twin of a plate the @ifnottex branch
               also names. The Print conditional profile deliberately reads BOTH
               branches, and the package's @image probe deliberately EXCLUDES .pdf
               ("a manual that keeps pdf/NAME variants for its TeX branch would
               then hand Html2Pdf a file it cannot decode"). Each of the nine
               plates IS in the document, once, from the other branch, so copying
               the ten .pdf files in would change nothing.

  learning, 1  pictures/context-example -- upstream ships only the .eps and BUILDS
               the .png. ⚠ The same .eps resolves perfectly well for the notation
               manual, which is worth knowing before someone "fixes" this: there it
               is reached by \epsfile from INSIDE a music snippet, so the ENGINE
               reads it rather than the image resolver, and the engine reads EPS.

--------------------------------------------------------------------------------
DECISION D57 -- THE BUILD PRODUCTS THAT *WERE* RECOVERED (assets/bib, assets/staged)
--------------------------------------------------------------------------------

Ruled by Jeremy 2026-08-19. Two sets of files that upstream generates or stages at
build time are now vendored, so this repository can render every manual whole with
no TeX installation and no upstream checkout. Each directory has its own README
with the full provenance and the exact command; both are hash-fenced by
tests/Lily.Docs.Tests/VendoredBuildProductTests.cs.

  assets/bib/      FIVE bibliographies, 37,420 bytes, 160 entries, plus
                   lily-bib.bst -- the style program that produced them, kept as
                   reference and NEVER executed. essay includes three of them;
                   we-wrote and others-did belong to web.texi, which decision D48
                   excluded, and are here against a future day.
                   ⇒ essay's Include warnings went 12 -> 9, its pages 47 -> 63.

  assets/staged/   ROADMAP and code-review-checklist.md, which the Contributor's
                   Guide prints with @verbatiminclude. ⚠ NEITHER LIVES IN
                   Documentation/ UPSTREAM -- one is at the source tree root, one
                   under .agents/ -- and the doc build copies both into $(outdir)/en/
                   before rendering. "Staged" is upstream's own arrangement.
                   ⇒ contributor's Include warnings went 2 -> 0, its pages 186 -> 189.

⚠ "PORT bib2texi" WAS NEVER THE JOB, AND THAT IS THE WHOLE REASON THIS WENT THE WAY
IT DID. scripts/build/bib2texi.py is thirty lines that write a fake .aux file and
shell out to the bibtex BINARY; the translation is BibTeX interpreting an 8.5 KB
.bst program -- name parsing with von/jr particles, first-name abbreviation, @tie{}
insertion, purify$/change.case$ sentence casing, a 78-column wrap. Writing our own
meant writing a BibTeX style-language interpreter, with no parity gate to hold it
honest. MEASURED against thirty years of upstream history before deciding: the
entire bib corpus in LilyPond is five files, 52,615 bytes, 160 entries and one
.bst; Documentation/bib/ has been touched by SEVEN commits out of 35,717; nothing
in LilyPond's user-facing surface has ever accepted a .bib file. Static reference
data, one caller in the world.

⚠ COMPARE version.itexi, WHICH THIS TOOL DELIBERATELY GENERATES RATHER THAN
VENDORING, because that one must track the ENGINE's version and a frozen copy would
silently disagree with the engine that produced the manual. These must track the
CORPUS, and the corpus is pinned. Opposite requirement, opposite answer -- and that
contrast is the rule for deciding the next case like it.

⚠ BOTH DIRECTORIES ARE ON THE INCLUDE SEARCH PATH IN THEIR OWN RIGHT, and LAST, after
the corpus. The files in them are named by BARE NAME (`@include colorado.itexi',
`@verbatiminclude ROADMAP'), so neither is reachable through the assets ROOT, which
is on the path for `en/macros.itexi'. Last is deliberate: if a corpus mirror ever
carried a real colorado.itexi, the corpus copy should win -- we are standing in for
a build product, not overriding a source.

WHAT THE MANUAL-SPECIFIC GATES SAY

  contributor is rendered TWICE. Its catalogue entry declares engravesSnippets:
  false, and a manual rendered with no engraver is exactly what a manual whose
  every engraving failed looks like -- so the fixture renders it again WITH an
  engraver registered and asserts the engraver was never called. ⚠ The claim
  needed checking rather than quoting: its twenty files hold nineteen occurrences
  of the letters @lilypond, every one an ESCAPED mention in the chapter that
  documents how to write snippets.

  music-glossary is where decision D50 finally pays off, and the ONLY place in
  nine manuals the question can be asked. Every other music symbol in scope is
  markup inside a @lilypond snippet -- drawn by the engine into an SVG, never
  handed to a text renderer. These five are prose, and they survive into BOTH
  formats.

  essay places 31 plates and deliberately does NOT place three. engraving.itely
  names henle-flat-bw, baer-flat-bw and lily-flat-bw inside an @ifinfo branch;
  Info output is out of scope and the Print profile turns it off. They are in the
  mirror because the mirror is the manual's SOURCE closure, and their ABSENCE from
  the document is what says the conditional profile is the one this phase renders.

  The licence gate is a BICONDITIONAL, not "every manual shows the notice". Six of
  the seven include fdl.itexi and carry it; changes.tely includes no fdl.itexi and
  carries none. A gate weakened to accommodate that one manual would have stopped
  being a licence gate.

--------------------------------------------------------------------------------
BASELINES (expected-warnings/)
--------------------------------------------------------------------------------

    <manual>.tsv           warning category -> count, plus a TOTAL line
    <manual>-pdf.tsv       PAGES, PDF_WARNINGS and PDF_ITEMS; the page size the
                           render actually used (PAGE_WIDTH_PT, PAGE_HEIGHT_PT);
                           the package default this phase deliberately did not
                           change (SVG_RASTER_SCALE) and the one it deliberately
                           did (KEEP_UNCOVERED, decision D56); and one DROP row
                           per DISTINCT DROPPED CODE POINT
    <manual>-snippets.tsv  ASKED, ENGRAVED, PICTURES, FAILED, DECLINED and
                           DOCUMENT_IMAGES -- what the engraver was asked to do
                           and what came back

⚠ THE DROP ROWS ARE PER CODE POINT ON PURPOSE, and they are four columns where
every other row is two -- DROP, the warning's stable code, the code point, and an
exact occurrence count. A total would keep passing while one character stopped
dropping and a different one started. This is only possible because the packages
grew TexinfoPdfWarnings.PdfItems: the prose form of these warnings names the
FIRST code point seen and carries no count at all, so a baseline built on it
could only ever have been a string match.

⚠ THE PAGE SIZE IS FROZEN BESIDE THE PAGE COUNT for the same reason. At wave LD4
the Internals Reference went 1,349 pages -> 1,266 for TWO known reasons at once:
the page became A4, and the packages' line-metrics fix arrived in the same pin
bump. Two reasons is one too many to separate after the fact. With the size in
the file, the next time a page count moves the size row says whether the paper
changed or the layout did.

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
                                                   0 snippets  1266 pages

    notation     LilyPond Notation Reference -- corpus prose whose APPENDICES are
                 the port's other eighteen generated files, so rendering it is the
                 act the whole phase is named for. HTML at wave LD3, PDF at wave
                 LD4.
                                                2555 snippets  1280 pages

    learning     LilyPond Learning Manual        253 snippets   253 pages
    usage        LilyPond Application Usage       13 snippets    96 pages
    extending    Extending LilyPond               34 snippets    76 pages
    essay        Essay on automated music engr.   32 snippets    63 pages
    changes      LilyPond Changes                 13 snippets    10 pages
    music-glossary  LilyPond Music Glossary       93 snippets   135 pages
    contributor  LilyPond Contributor's Guide      0 snippets   189 pages

⚠ EVERY NUMBER ABOVE IS READ OFF THE FROZEN BASELINES IN expected-warnings/, not
off a run and not off a plan. Two of them moved after the wave that first wrote
them and this table did not: essay went 47 -> 63 pages and contributor 186 -> 189
when decision D57 recovered their absent build products, and notation went 1,272
-> 1,280 at the CodeBrix.LilyScheme 1.0.232.256 pin bump, which took its failed
engravings from 12 to 1. If you change a baseline, change this table in the same
edit -- a stale figure here reads as a measurement.

    NINE MANUALS, BOTH FORMATS:  3,368 PDF pages carrying 2,995 engraved pictures,
    from 2,993 snippets asked -- 2,992 engraved, ONE failed, none declined. The
    one failure is deliberate: notation's `{ \skip 1 \skip1 \skip 1 }', which
    the manual's own prose introduces as producing "no output of any kind", is
    left as a FAILURE because "ran clean and produced nothing" is exactly what a
    real engraving loss looks like.

                 All seven at wave LD5, both formats, ZERO engraving failures
                 between them. ⚠ They consume NONE of the port's nineteen
                 generated files -- pure corpus prose -- which is a fact about the
                 mission's ORIGIN and not about their standing: decision D48 ruled
                 all nine owed in both formats, on the measured ground that
                 consuming none of the nineteen says nothing about a manual's cost
                 or feasibility. CorpusManualRenderTests asserts it rather than
                 leaving it as a sentence here.

A name ManualCatalog does not hold is an ERROR rather than a render that quietly
produces nothing.

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

    CodeBrix.Texinfo2Html.MitLicenseForever   1.0.232.110
    CodeBrix.Texinfo2Pdf.MitLicenseForever    1.0.232.110

Both are named in src/Lily.Docs/Lily.Docs.csproj even though the PDF package
brings the HTML one transitively, so the pin is visible in one place. Bumping
one without the other is the mistake the pin discipline exists to prevent. A
package fix happens in ~/GitHome/CodeBrix.Texinfo, ships as a PAIR at one
version, and lands here as a pin bump verified by re-running the gates.

================================================================================
