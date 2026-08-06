================================================================================
CodeBrix.LilyPort -- TEXT FONT ASSETS
================================================================================

These are the 24 text faces LilyPond 2.27.2 ships and measures text with --
six families x 4 faces (Regular / Bold / Italic / BoldItalic). Unlike the
Emmentaler music fonts one directory up (which this project builds itself
from Metafont sources), these ARE redistributed prebuilt binaries, vendored
byte-for-byte by decision D13 (Jeremy, 2026-08-05).

    Vendored:      2026-08-05
    Copied from:   the pinned oracle installation
                   (official GNU LilyPond 2.27.2 binary distribution,
                   share/lilypond/2.27.2/fonts/otf/)
    Why prebuilt:  these faces are the very files that produced the
                   committed regression reference SVGs. Text extents drive
                   grob positions and line breaks, so parity needs metrics
                   byte-identical to the oracle's -- same files, not
                   same-ish builds.

--------------------------------------------------------------------------------
WHAT THEY ARE
--------------------------------------------------------------------------------

  URW faces (the urw-base35 project, snapshot 20200910) -- LilyPond's
  DEFAULT text faces:

    C059-*.otf           Century Schoolbook clone   default SERIF
    NimbusSans-*.otf     Helvetica clone            default SANS
    NimbusMonoPS-*.otf   Courier clone              default TYPEWRITER

  GUST e-foundry faces (TeX Gyre collection, release 2.501) -- the first
  FALLBACKS in LilyPond's alias chains:

    texgyreschola-*.otf  serif fallback
    texgyreheros-*.otf   sans fallback
    texgyrecursor-*.otf  typewriter fallback

  The alias chains are defined by fonts/00-lilypond-fonts.conf in the
  upstream distribution, e.g. LilyPond Serif -> C059 -> TeX Gyre Schola ->
  DejaVu Serif -> Noto CJK. DejaVu and Noto are NOT bundled by upstream and
  are not vendored here. The port's chain deliberately DIVERGES at that
  point (decision D23, 2026-08-05): glyphs beyond the 24 faces fall through
  to Roboto (the CodeBrix.Platform.Fonts.Roboto NuGet package -- OFL,
  consumed as a dependency PINNED at version 1.0.209.315, nothing vendored;
  a version change is a deliberate re-baseline event), and there is NO
  system-font
  fallback -- scripts the chain does not cover (CJK, Hebrew/Arabic, ...)
  render missing-glyph tofu by design. CodeBrix.Platform never falls back
  to system fonts: cross-platform behavior must not depend on what a host
  happens to have installed. Regression files whose references bake in the
  oracle's system-DejaVu metrics are handled by the D23 two-phase protocol:
  validated against the oracle ONCE using a test-harness-only DejaVu mode
  (on the machine whose DejaVu generated the references), then re-baselined
  as committed PORT-GENERATED Roboto references -- clearly marked as such,
  never mixed with oracle references -- so the suite passes on every
  supported platform with no DejaVu anywhere.

--------------------------------------------------------------------------------
SHA256 MANIFEST (byte-identical to the oracle installation)
--------------------------------------------------------------------------------

  60d0ab6a22e4018b1232806d98f4015e04f9a0a569db7a169c40d55e2a9f6252  C059-BdIta.otf
  75dea10068264324522e7b96612083582eee0158eb728fa9c219279890f33e26  C059-Bold.otf
  77852deabd3a84f7d0213932239de522410309374c2255ccbd510f01dc7c6e80  C059-Italic.otf
  e00cc7b88f0cf25ae43f0e48de39d0043fe042b55719a3ac32669ab3369542ec  C059-Roman.otf
  f036d05d2168c7f71cb11d31e81d11133f3d09711e24ebde19d08a24842384d5  NimbusMonoPS-Bold.otf
  a67ed9e364c933c79fc3ce88e17a0265334697c184b065407e754d43cbbf6d0a  NimbusMonoPS-BoldItalic.otf
  7f1f85498027e07befadd3c7592518909b9e8c7f96ee5615e0b21be0c399c4b4  NimbusMonoPS-Italic.otf
  4f225ca8e13acb16f733ce741693105e527d5f7a5443901b9ecc190fca4e149b  NimbusMonoPS-Regular.otf
  7f33328e6b4d4cd21b45fa625791928c9407dc702db6780e56b09ca9a3ecaa67  NimbusSans-Bold.otf
  3f47fb34fcb7de09f8cbc9f305191340ddebf7a068419f4bb5f49287dea59b87  NimbusSans-BoldItalic.otf
  7b0bef5686aa58c0fd0f0d01beeae56664208490e30ca6a25431281c9a0c6402  NimbusSans-Italic.otf
  7c25be4d78155523080ab85b10277150657ff7dabbcad7037bdd536c9b6d0d08  NimbusSans-Regular.otf
  95e9755bfa05759e8d1f8fba21a919ba46091e1275aeb5bf831dffd9c93b041e  texgyrecursor-bold.otf
  6aa5c41f489b898aa890c543e7dcd5e732c63894babc65569e01ee7e46533d82  texgyrecursor-bolditalic.otf
  545f24a2a9e5dd5ace865fe5e80134f55a37794b36e26abae5424aebff600056  texgyrecursor-italic.otf
  0667deb48aa0e88be8f499c4d308e8b9116f290e7f969b0f5a34ee15c9644272  texgyrecursor-regular.otf
  b170162835f4efc288886dd4231406dc47e19b614cf4416836635599d44a7d60  texgyreheros-bold.otf
  166fc6d068d9c9974281555cb3d730365537a9b676ab269bb5163f5a75496505  texgyreheros-bolditalic.otf
  6473df7fa107b3fb4be38973710afe22b0640c2ac076d5337cf126bed9aa108c  texgyreheros-italic.otf
  6ae1a09d5a940367b7aaaa91ee8bd8a2c333bfe193e7096e23f931357d62081f  texgyreheros-regular.otf
  988c7b2a0ff0eae77d1df3338751b3c47a5e759117734a375b8e7f9de80e698b  texgyreschola-bold.otf
  95f085da44c04817771f2a3a754b6eeb442b9a79e79f51dc3b0612e3cc0d75cc  texgyreschola-bolditalic.otf
  2f45b8c394037951aaec73d947f0b0c3af715950f8f207c9f4290ba37a53a4d2  texgyreschola-italic.otf
  935b82e25f56b1d1276ca82793205e8ce254fbb37ebd38bedf7388cef21fbf44  texgyreschola-regular.otf

  Total: 24 files, 2,560,264 bytes.

--------------------------------------------------------------------------------
LICENSES -- the texts travel with the fonts, in licenses/
--------------------------------------------------------------------------------

  URW faces (12 files):
      AGPL-3.0 WITH a font-embedding exception permitting inclusion of the
      font programs in PostScript or PDF documents regardless of the
      document's license.
        licenses/urw-base35-fonts-20200910.LICENSE   (the exception grant)
        licenses/urw-base35-fonts-20200910.COPYING   (the AGPL-3 text)

  TeX Gyre faces (12 files):
      GUST Font License (LPPL 1.3c or later; rename clause binds DERIVED
      works only, and is a request, not a requirement).
        licenses/tg2_501otf.GUST-FONT-LICENSE.txt

  Both license files are carried verbatim as shipped in the official
  LilyPond 2.27.2 binary distribution's licenses/ directory.

  These are separately-licensed works aggregated alongside the GPL-3
  program (GPL-3 section 5) -- exactly the aggregation upstream LilyPond
  itself makes. MEASURED 2026-08-05: LilyPond's SVG backend embeds NO font
  data (text is emitted as <text font-family=...> elements), so the port's
  parity output never embeds these fonts. The URW PS/PDF embedding
  exception becomes relevant -- and grants exactly what is needed -- if a
  future backend (Milestone 7 Skia) embeds text fonts into exported PDFs.

  *** ASSETS FOREVER -- NEVER EDIT. *** Subsetting, converting, or editing
  any of these files creates a DERIVED font: the GUST rename request wakes
  up, and AGPL source obligations attach to the modified version. If a
  face must ever change, it gets a new name and a new decision.

Full attribution and compliance record:
CodeBrix.LilyPort/THIRD-PARTY-NOTICES.txt section 10.

================================================================================
