================================================================================
Fresco.Brix -- tools/manuals/
================================================================================

Renders the nine manuals with CodeBrix.LilyPort's own tools/Lily.Docs and
installs the PDFs as this application's documentation assets.

    cd tools/manuals
    dotnet build Manuals.csproj -c Release
    ./bin/Release/net10.0/Manuals

That writes src/Fresco.Brix.Core/assets/docs/ -- nine PDFs, COPYING.FDL and
MANIFEST.txt -- and takes about ten minutes, five of which are the Notation
Reference's 2,555 engravings.

    --render-dir DIR   where Lily.Docs renders (default: a temp directory)
    -o ASSETS_DIR      where the PDFs are installed
    --skip-render      install from an existing render directory, which is what
                       to use after a render you already have

--------------------------------------------------------------------------------
WHAT THIS IS, AND WHAT IT IS NOT
--------------------------------------------------------------------------------

A REPO TOOL. It ships nothing, it is deliberately NOT in Fresco.Brix.slnx, and
a normal build must not depend on it. The precedents are tools/symbolicons in
this repository and tools/Lily.Docs in CodeBrix.LilyPort.

The application NEVER renders a manual. It opens files that were made here.
That is board decision D52 seen from this side: Lily.Docs is a repo tool that
ships nothing, CodeBrix.LilyPort must not acquire a dependency on the
Texinfo -> Html2Pdf -> font-package chain, and Fresco.Brix bundles the finished
PDFs as assets. Nothing in this project references Lily.Docs either -- see
below.

--------------------------------------------------------------------------------
WHY LILY.DOCS IS A CHILD PROCESS AND NOT A PROJECT REFERENCE
--------------------------------------------------------------------------------

Lily.Docs' ToolPaths finds the FDL corpus mirror by walking UP from the running
assembly to CodeBrix.LilyPort.slnx. An assembly built under Fresco.Brix/tools
has no such ancestor -- the two repositories are SIBLINGS inside
CodeBrix.Samples.Gpl3 -- so eight of the nine manuals would fail to find their
own source text and the ninth (internals, which needs no corpus) would quietly
succeed. Driving the documented command line instead costs nine process starts
and keeps the whole Texinfo chain out of this project.

--------------------------------------------------------------------------------
GENERATION IS ONCE PER PROCESS
--------------------------------------------------------------------------------

Board trap 15, and Lily.Docs' own README says it first. The nineteen generated
documentation files are an engine job of roughly forty seconds, and asking for
them a SECOND time in one process does not throw: it reports all nineteen
missing and renders the manual out of an empty directory. So the first manual
is rendered in a process that generates, and every later render is handed
`--generated <render-dir>/generated/en'.

Each render is its own `dotnet run', so each pays the engine's warm start
(about four and a half seconds). Only the first builds; the rest pass
--no-build.

--------------------------------------------------------------------------------
THE MANUALS -- board decision D48, and the reading order
--------------------------------------------------------------------------------

Nine, and the ORDER in this tool is the order the documentation panel lists
them in, which is READING order rather than Lily.Docs' command-line order: a
person new to the language opens the Learning Manual and a person looking
something up opens the Notation Reference. ManualCatalog in Fresco.Brix.Core
declares the same order and ManualCatalogTests checks the two agree.

    learning         253 pages     5,553,063 bytes
    notation        1280 pages    33,818,277 bytes
    usage             96 pages       656,329 bytes
    extending         76 pages       685,882 bytes
    internals       1266 pages     7,306,505 bytes
    essay             63 pages     3,245,616 bytes
    music-glossary   135 pages     1,751,251 bytes
    changes           10 pages       195,908 bytes
    contributor      189 pages       958,883 bytes
    ----------------------------------------------
    TOTAL           3368 pages    54,171,714 bytes  (51.7 MB)

MEASURED 2026-08-21 at wave W10, against LilyPort at CodeBrix.LilyScheme
1.0.232.256 and CodeBrix.Texinfo 1.0.232.110. The page counts are read out of
the SHIPPED files by CodeBrix.PdfRasterizer -- the same reader the application
uses -- and they reproduce Lily.Docs' own frozen baselines exactly.

⚠ ONE ENGRAVING FAILURE IS EXPECTED AND IS NOT A FAULT. notation's
`{ \skip 1 \skip1 \skip 1 }' (en/notation/rhythms.itely:912) is introduced by
the manual's own prose as producing "no output of any kind", and Lily.Docs
leaves it as a FAILURE because "ran clean and produced nothing" is exactly what
a real engraving loss looks like. 2,555 asked, 2,554 engraved.

--------------------------------------------------------------------------------
LICENSING -- THE MANUALS ARE GFDL, THE APPLICATION IS GPL-3.0-only
--------------------------------------------------------------------------------

The manuals are LilyPond documentation under the GNU Free Documentation
License 1.3-or-later, with NO Invariant Sections, NO Front-Cover Texts and NO
Back-Cover Texts. The FDL is not GPL-compatible in either direction, so:

  * they live in ONE clearly separated folder, src/Fresco.Brix.Core/assets/docs,
    which holds no source and from which no text is ever copied into source
    files or XML doc comments;

  * COPYING.FDL is installed BESIDE them by this tool, because the licence
    requires a copy of itself to accompany the work;

  * the arrangement is aggregation, the same reading the hyphenation
    dictionaries ship under (GPLv3 section 5): separate documents, each keeping
    its own notice, in a folder that can be emptied without the application
    failing.

THIRD-PARTY-NOTICES.txt section 9 records all of this. CodeBrix.LilyPort's own
THIRD-PARTY-NOTICES.txt section 4 is where the upstream licence text is quoted
verbatim, including the noted discrepancy between LICENSE.DOCUMENTATION's
"version 1.3, or (at your option) any later version" and the per-file notice
macros.itexi emits into the generated manuals, which says "Version 1.1 or any
later version".

--------------------------------------------------------------------------------
WHEN TO RUN IT
--------------------------------------------------------------------------------

Rarely. The manuals change when the ENGINE changes -- a new LilyPond release
implemented in LilyPort, or a Texinfo package fix that changes the rendering.
They do not change when Fresco.Brix changes. After a run, check MANIFEST.txt
into the repository with the PDFs and update ManualCatalog if any page count
moved; ManualCatalogTests fails if you forget.
