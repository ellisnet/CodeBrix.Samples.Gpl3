# CodeBrix.Samples.Gpl3

Sample and companion projects of the CodeBrix family that are licensed **GPL-3.0-or-later**,
kept in their own repository so that the GPL obligations they carry stay separated from the
rest of the family.

Most CodeBrix libraries are permissively licensed. A project lands here when it incorporates
GPL-licensed material and must therefore be distributed under the GPL itself — keeping those
projects apart makes each repository's licensing answerable on its own terms.

## Projects

### CodeBrix.LilyPort

A managed port of [GNU LilyPond](https://lilypond.org), the music engraving program. It renders
`.ly` input to SVG and MIDI through ported C++ engine code together with LilyPond's own vendored
Scheme layer, which runs on `CodeBrix.LilyScheme` (a separate, LGPL-licensed repository).

The port is verified against a pinned LilyPond 2.27.2 binary used as an oracle. A regression
harness renders the upstream test corpus and compares the result page by page, a committed
ratchet manifest keeps any file from getting worse, and a second comparison runs LilyPond's own
documentation generator and checks its nineteen output files byte for byte. See
`CodeBrix.LilyPort/tools/regression-harness/README.txt` for the harness, and
`CodeBrix.LilyPort/src/CodeBrix.LilyPort.Engine/PORT-COVERAGE.txt` for every deliberate
divergence from upstream with its reason.

The NuGet package is `CodeBrix.LilyPort.GplLicenseForever`, licensed GPL-3.0-only as LilyPond is.
Attribution and compliance records are in `CodeBrix.LilyPort/THIRD-PARTY-NOTICES.txt`.

### Fresco.Brix

A CodeBrix.Platform application built on the port, with heads for Linux (X11, Wayland and the
frame buffer), macOS, and Windows (Win32/Skia and WPF/Skia).

## License

GPL-3.0-or-later; the licence text is in `LICENSE` at the repository root. `LICENSE.OFL` covers
bundled material under the SIL Open Font License. Per-project attribution and third-party
notices live with each project.
