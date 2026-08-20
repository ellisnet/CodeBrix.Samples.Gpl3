================================================================================
CodeBrix.LilyPort -- tools/Lily.Docs/svg-dialect/
================================================================================

THE SVG DIALECT THE PORT'S ENGINE EMITS FOR DOCUMENTATION SNIPPETS, measured and
frozen. This is the specification a downstream renderer implements against in
order to place engraved music in a PDF.

+-----------------------------------------------------------------------------+
| THIS FOLDER IS SELF-CONTAINED. Everything needed to READ and UNDERSTAND the  |
| inventory is in here; nothing outside it has to be opened. The two .cs files |
| are REFERENCE COPIES of the live sources, kept byte-identical by a gate --   |
| see PROVENANCE at the foot of this file.                                     |
+-----------------------------------------------------------------------------+

CONTENTS

    README.txt                    this file: what the vocabulary MEANS, and what
                                  a conforming renderer has to support
    inventory.tsv                 the frozen vocabulary:
                                  kind <TAB> name <TAB> count
    SvgDialectInventory.cs        reference copy of the SCANNER -- the code that
                                  both froze inventory.tsv and rechecks it, so
                                  the two cannot drift apart in interpretation
    SvgDialectInventoryTests.cs   reference copy of the GATE -- what is asserted
                                  about the dialect on every test run, including
                                  the control

Read them in that order. inventory.tsv is the data; SvgDialectInventory.cs says
exactly how a member was counted; SvgDialectInventoryTests.cs says exactly what
a change to the dialect would break.

--------------------------------------------------------------------------------
WHY THIS FILE EXISTS, AND WHY IT LIVES HERE AND NOWHERE ELSE
--------------------------------------------------------------------------------

CodeBrix.PdfDocCreate.Html2Pdf places raster images only -- hand it an SVG and it
answers "not in a supported format and was skipped", so the picture is simply
absent from the PDF. The port's snippets are SVG. The open question (decision D51
of the Phase-5 board) is whether Html2Pdf learns to place SVG directly or
Lily.Docs grows a rasterizer that converts to PNG first. Either way somebody has
to know exactly what these files contain, and "SVG" is not an answer -- the
format is enormous and what the engine actually uses is a small corner of it.

+-----------------------------------------------------------------------------+
| THE ENGRAVED SVGs ARE NOT SHAREABLE. They are derived from the GFDL corpus    |
| mirror, and this is the GPL-3 repository. A renderer's own repository -- all  |
| of the MitLicenseForever packages -- must NOT take a copy of them as test     |
| fixtures. This document is the deliverable instead: it states the dialect in  |
| words and numbers so a renderer author can write SYNTHETIC test SVGs that     |
| exercise the same constructs, with no corpus content leaving this repository. |
| Verification against the real pictures happens HERE, in Lily.Docs' gates.     |
+-----------------------------------------------------------------------------+

--------------------------------------------------------------------------------
HOW IT WAS MEASURED
--------------------------------------------------------------------------------

Over the notation manual's complete engraved output -- 2,546 SVG files, every
picture the manual places -- not a sample. The scanner is SvgDialectInventory.cs
in this folder, and the same code both froze the baseline and checks it, so the
two cannot drift apart in interpretation.

Counts in inventory.tsv are the number of FILES a member appears in, except
FONT-FAMILY, which counts OCCURRENCES. FACT/FILES is the corpus size.

+-----------------------------------------------------------------------------+
| WHAT IS ASSERTED IS THE SET, NOT THE COUNTS. A new element, attribute or font |
| family is a requirement on a renderer that nobody agreed to, so it goes red.  |
| The counts move whenever any snippet moves; freezing them would make the gate |
| fail for reasons that say nothing about the dialect, and a gate that cries    |
| wolf gets regenerated instead of read. A member falling to ZERO is also red   |
| -- a construct that stopped being emitted is a dialect change too.            |
+-----------------------------------------------------------------------------+

--------------------------------------------------------------------------------
WHAT A CONFORMING RENDERER MUST SUPPORT
--------------------------------------------------------------------------------

ELEVEN ELEMENTS, and nothing else:

    svg g text tspan rect a path line polygon circle ellipse

THIRTY-FOUR ATTRIBUTES, and nothing else:

    geometry   x y width height viewBox transform
               x1 y1 x2 y2          (line)
               d                    (path)
               points               (polygon)
               r cx cy rx ry        (circle, ellipse, rounded rect)
    paint      fill stroke stroke-width color
               stroke-linecap stroke-linejoin stroke-dasharray
    text       font-family font-size font-style font-weight font-variant
               text-anchor
    document   version xmlns xmlns:xlink xlink:href

WHAT IS ABSENT, and may be relied on as absent while this gate is green:

    no gradients, filters, clip paths, masks, patterns, symbols or markers
    no <use>, <image> or <foreignObject>
    no CSS: there is no style= attribute and no <style> element anywhere
    no animation
    no matrix() or skew() transforms

VALUE VOCABULARIES are similarly closed, as measured over the same corpus:

    transform         translate(...) scale(...) rotate(...)   -- these three only
    text-anchor       start                                   -- this one only
    stroke-linecap    round                                   -- this one only
    stroke-linejoin   round                                   -- this one only
    font-style        italic                                  -- this one only
    font-weight       bold                                    -- this one only
    font-variant      small-caps                              -- this one only
    xlink:href        https://lilypond.org                    -- see LINKS below

--------------------------------------------------------------------------------
WHAT ONE ACTUALLY LOOKS LIKE
--------------------------------------------------------------------------------

A vocabulary list says what may appear; it does not say how a file is PUT
TOGETHER. This is one complete snippet -- verbatim, but re-wrapped to this
file's width, and with one elision marked in the path data. It is a two-note
example, and it happens to contain nearly the whole dialect in 23 lines.

    <?xml version="1.0" encoding="UTF-8"?>
    <svg xmlns="http://www.w3.org/2000/svg"
         xmlns:xlink="http://www.w3.org/1999/xlink" version="1.2"
         width="137.2456mm" height="22.4522mm"                      (1)
         viewBox="20.2552 6.6906 78.1003 12.7765">                  (1)
    <g fill="currentColor" color="black">                           (2)
    <g transform="translate(51.2491, 19.0139)">                     (3)
    <text font-family="serif" font-size="2.2000" text-anchor="start"
          fill="currentColor">
    <tspan>LilyPond v2.27.2</tspan>
    </text>
    </g>
    <g transform="translate(51.2491, 19.0139)">
    <a xlink:href="https://lilypond.org">                           (4)
    <rect x="0.0000" y="-0.4532" width="17.0034" height="2.0747"
          fill="none" stroke="none" stroke-width="0.0"/>
    </a>
    </g>
    <g transform="translate(21.1461, 10.7906)">                     (3)
    <path transform="scale(0.0071, -0.0071)"                        (5)
          d="M0 104c30 88 94 146 107 146c9 0 18 -9 18 -18c0 -4 -2 -8 -6 -12
             ... [elided] ... c0 7 5 16 15 16c29 0 87 -79 110 -145z"
          fill="currentColor"/>
    </g>
    <g transform="translate(21.1461, 10.7906)">
    <line stroke-linejoin="round" stroke-linecap="round"            (6)
          stroke-width="0.2000" stroke="currentColor"
          x1="0.0000" y1="-0.0000" x2="0.0000" y2="-4.0000"/>
    </g>
    </g>
    </svg>

(1) TWO COORDINATE SYSTEMS, and they are not the same one. width/height are the
    PHYSICAL size in millimetres -- how big this picture is on paper. viewBox is
    the internal drawing space, in the engine's own units. Everything inside is
    expressed in viewBox units; the renderer maps that box onto the mm box. Read
    the mm and you never need a DPI setting.

(2) THE COLOUR ROOT. One outer <g> carries color="black", and every element
    below paints with fill="currentColor" or stroke="currentColor" rather than
    naming a colour. Without currentColor inheritance nothing below draws
    correctly -- and it will not draw as an obvious error, it will draw as
    black-by-luck until a snippet sets a colour.

(3) PLACEMENT IS A FLAT LIST OF TRANSFORMED SIBLINGS. Each object sits in its
    own <g transform="translate(...)"> under the colour root. Nesting is shallow
    but there is a LOT of it: 108,484 <g> elements across the 2,546 files -- a
    median of 17 per picture, a mean of 43, and the largest single picture
    carries 3,893. A renderer that recurses per group wants that number in mind.

(4) THE TAGLINE LINK, wrapping an INVISIBLE rectangle -- fill="none"
    stroke="none". Honour those or every example acquires a visible box.

(5) TRANSFORMS COMPOSE ACROSS THE PARENT AND THE ELEMENT. The <g> translates to
    where the note head goes; the <path> then scales by 0.0071 with a NEGATIVE
    Y, because glyph outlines arrive in font units with the Y axis pointing the
    other way. A renderer that applies only one of the two, or that drops the
    sign, produces a picture that is recognisably music and quietly wrong.

(6) STAFF LINES, STEMS AND BARLINES ARE <line>, and note heads are <path>. This
    is what "music is geometry, not text" means in practice: no music font is
    named anywhere, and there is nothing here to resolve against one.

⚠ PROVENANCE: this is Lily.Docs' own engraving of a documentation snippet from
the GFDL corpus mirror, produced by the port's engine at wave LD3. It is quoted
here as illustration; the pictures themselves are not committed, because the gate
rescans live output every run and a committed snapshot would need its own fence
to stay honest.

--------------------------------------------------------------------------------
THE FIVE THINGS THAT DECIDE WHETHER THE PICTURE IS RIGHT
--------------------------------------------------------------------------------

1. PHYSICAL SIZE IS ON THE ROOT ELEMENT, IN MILLIMETRES.

   <svg width="135.6800mm" height="13.5824mm" viewBox="21.1461 8.0691 77.2094 7.7291">

   All 2,546 files carry mm on the root and a viewBox in a DIFFERENT coordinate
   space. The engraver has already decided how big the picture is on paper, so a
   renderer that honours the mm size needs no DPI setting and no scaling
   heuristic: place it at the size it declares, and map the viewBox onto that
   box. Getting this wrong does not fail -- it produces music at the wrong size,
   which only a human comparing against the HTML will notice.

2. MUSIC IS GEOMETRY, NOT TEXT. No music font is named anywhere in the corpus;
   there is no Emmentaler reference to resolve. Note heads, stems, beams, slurs
   and barlines arrive as <path>, <rect>, <polygon> and <line>. A renderer needs
   NO access to the port's vendored music faces.

3. TEXT ASKS FOR CSS GENERICS, spelled the SVG way:

       serif                     7678 occurrences
       sans                      4522     <-- NOTE: "sans", not "sans-serif"
       monospace                  568
       ---------------------------------------------------------------
       Linux Libertine O,serif     34     the tail: real face names
       TeX Gyre Schola             13
       Linux Libertine O            6
       Liberation Serif             3
       DejaVu Sans Mono             3
       Liberation Mono              2
       Liberation Sans              2
       DejaVu Sans                  1

   The generics are 99.5% of the runs, and mapping them onto a renderer's own
   serif/sans/mono faces disposes of almost the whole problem. Two traps:

     - "sans" is not "sans-serif". A CSS engine that only knows the standard
       generic name will silently miss 4,522 runs.
     - The 61-occurrence tail names REAL system and TeX fonts, because the corpus
       documents them by example. The family rule is that font chains END at
       family packages and never fall through to the machine's own fonts, so
       these must resolve to a package face or produce visible tofu -- never to
       whatever the host happens to have installed.

   ⚠ FIDELITY, NOT JUST COVERAGE. The <text> positions in these files were
   computed by the ENGINE using its own faces at engraving time. A renderer that
   sets the same runs in different faces gets different glyph advances, so text
   drifts from where the engraver put it. Where a renderer can be told which
   faces to use, being told the port's own faces is worth more than a visually
   similar substitute.

4. COLOUR IS INHERITED THROUGH currentColor. Every file wraps its content in
   <g fill="currentColor" color="black">, and inner elements say
   fill="currentColor" rather than naming a colour. A renderer that does not
   implement currentColor inheritance draws nothing visible, or draws it black by
   accident and breaks the day a snippet sets a colour.

5. LINKS ARE DECORATION, NOT NAVIGATION. <a xlink:href> appears in 2,500 files
   and every target is https://lilypond.org -- the version tagline at the foot of
   each example, wrapping an INVISIBLE hit rectangle:

       <a xlink:href="https://lilypond.org">
         <rect x="0" y="-0.4532" width="17.0034" height="2.0747"
               fill="none" stroke="none" stroke-width="0.0"/>
       </a>

   These are NOT point-and-click source links. A renderer may turn them into PDF
   link annotations or ignore them; either is defensible. What it must not do is
   PAINT the rectangle -- fill="none" stroke="none" has to be honoured, or every
   example acquires a box.

--------------------------------------------------------------------------------
WHEN THIS FILE IS WRONG
--------------------------------------------------------------------------------

The gate (SvgDialectInventoryTests.cs, in this folder) fences the notation
manual, which is by far the largest engraving load
(2,546 of roughly 2,990 pictures across all nine manuals). The remaining eight
manuals are rendered at wave LD5 and carry about 442 snippets between them; when
they run, re-scan and expect this vocabulary to hold. If it does not, the new
member is the finding -- update inventory.tsv and this file together, in the same
session, and tell whoever is implementing the renderer.

--------------------------------------------------------------------------------
PROVENANCE OF THE TWO REFERENCE COPIES
--------------------------------------------------------------------------------

The .cs files here are COPIES, so that this folder can be read on its own. They
are not compiled -- both projects glob sources from their own directories, and
this folder is a sibling of neither. Their live originals are:

    SvgDialectInventory.cs        src/Lily.Docs/Snippets/SvgDialectInventory.cs
    SvgDialectInventoryTests.cs   tests/Lily.Docs.Tests/SvgDialectInventoryTests.cs

⚠ A COPY THAT CAN DRIFT WILL DRIFT, so it is fenced rather than trusted:
VendoredAssetTests asserts both copies byte-identical to their originals on every
run, the same treatment the vendored GFDL macro files get. Editing the original
without refreshing the copy fails the suite; the copies are never edited directly.

Measured 2026-08-19 at wave LD3's output, engine version 2.27.2.
