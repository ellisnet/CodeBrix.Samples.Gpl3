================================================================================
LILYPOND'S OWN EMMENTALER BINARIES -- TEST FIXTURES, NEVER SHIPPED
================================================================================

    Added:      2026-08-17 (PARITY 24, ruling R19)
    Copied from: the official GNU LilyPond 2.27.2 binary distribution,
                 share/lilypond/2.27.2/fonts/otf/
    Hashes:      SHA256SUMS.txt beside this file

--------------------------------------------------------------------------------
WHAT THESE ARE, AND WHAT THEY ARE NOT
--------------------------------------------------------------------------------

These nine files are the Emmentaler music fonts as LILYPOND BUILT THEM. The port
does not use them to engrave anything. It ships its OWN Emmentaler build, made
from the Metafont sources in CodeBrix.LilyPort/mf by tools/font-build -- ruling
R19 (Jeremy, 2026-08-17):

    "if LilyPond is building their own .OTF files from Metafont sources -- then
     that is what we are doing.  Final.  End of discussion.  So, we need our
     stuff to work with *our own fonts* that we are shipping.  Final."

/!\ THEY MUST NEVER SHIP IN THE NUGET PACKAGE. That is not a preference, it is
a settled packaging decision, and it is FENCED rather than remembered:
PackagedFontTests opens the built .nupkg and asserts these files are not inside
it. They live under tests/ and not under assets/ so that no asset glob can
reach them by accident.

--------------------------------------------------------------------------------
WHY THEY ARE HERE
--------------------------------------------------------------------------------

Two FontForge runs over the same Metafont sources do not produce identical
outlines -- 385 of emmentaler-20's 662 outlines differ between the two builds,
and emmentaler-26's clefs.G is 40 curve segments in ours against 47 in theirs,
same bounding box, different curve. A SKYLINE READS OUTLINES, so a handful of
corpus rows land in a slightly different place, and until PARITY 24 there was no
way to tell that apart from an engine defect.

Running the same corpus BOTH ways separates the two, and that is the whole
purpose of these files:

    with LILYPOND'S fonts   any divergence from the reference corpus is the
                            ENGINE, and is a defect to investigate.
    with OUR OWN fonts      any remaining divergence is the FONT BUILD, and is
                            measured, recorded and held to a ceiling
                            (tools/regression-harness/font-delta.py and its
                            committed ledger).

    BatchDriver ... --fonts tests/fixtures/lilypond-fonts

No rebuild is needed: FontAssets consults its SearchPaths before the assembly's
embedded copies, so the substitution is a command-line flag.

--------------------------------------------------------------------------------
/!\ THE .svg FONTS ARE DELIBERATELY NOT HERE -- DO NOT ADD THEM
--------------------------------------------------------------------------------

LilyPond also ships emmentaler-<size>.svg, which is what the SVG backend draws
music glyphs FROM, and putting those here breaks the corpus outright. MEASURED
2026-08-17: a full sweep with both the .otf and .svg substituted graded 121 of
2316 MATCH, against 2304 with the .otf alone.

The reason is decision D29. A music-glyph path is identified by the glyph NAME
whose outline bytes it equals, resolved through a committed index that
generate-glyph-identity.py builds from THE PORT'S OWN fonts. Draw the glyphs
from LilyPond's .svg fonts and no path resolves against that index, so every
page reads GLYPHS-DIFFER and nothing is gradeable.

It also gains nothing. D29 exists precisely so that a difference in DRAWN bytes
does not count -- only the glyph's identity and its position do. Substituting
the .otf changes what the engine MEASURES, which is the thing under test;
substituting the .svg only changes what it DRAWS, which the comparator is
designed to be indifferent to.

    CONSEQUENCE FOR THE GATE: the LilyPond-font run is exact at D29's
    NAMED-GLYPH identity -- same glyphs, same names, same positions, no
    tolerance -- and it cannot be strengthened to byte-identity, because the
    port draws from its own .svg fonts and R19 says it keeps them.

--------------------------------------------------------------------------------
LICENSING
--------------------------------------------------------------------------------

Same terms the port already conveys for the 24 vendored TEXT faces, and recorded
in CodeBrix.LilyPort/THIRD-PARTY-NOTICES.txt: dual-licensed under either
GPL-3.0-or-later with the LilyPond Font Exception, or the SIL Open Font License
1.1, Copyright (C) LilyPond's authors.

"Emmentaler" is a Reserved Font Name under the OFL. The reservation binds
MODIFIED fonts; these are unmodified byte-for-byte copies, so they keep the name.
DO NOT EDIT THEM -- a modified copy could not be distributed under that name
under the OFL option, and there is no reason to modify a fixture whose entire
value is being exactly what upstream ships.
