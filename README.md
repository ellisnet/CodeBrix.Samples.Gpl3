# CodeBrix.Samples.Gpl3

Complete, runnable **reference applications** for the [CodeBrix](https://github.com/ellisnet)
family of .NET libraries — the ones whose licensing lands on the **GNU General Public License
version 3**. Everything in this repository is licensed under GPL-3.0 (see [LICENSE](LICENSE));
the third-party components each application incorporates are itemized, with their own licences
and their upstream locations, in that application's `THIRD-PARTY-NOTICES.txt`.

Today the repository holds one application: **[Fresco.Brix](#frescobrix)**, a full-featured
music-notation text editor and engraving environment. Its notices file is
[Fresco.Brix/THIRD-PARTY-NOTICES.txt](Fresco.Brix/THIRD-PARTY-NOTICES.txt).

**Why this repository is GPL-3.0.** Fresco.Brix engraves scores *in process* with
[CodeBrix.LilyPort](https://www.nuget.org/packages/CodeBrix.LilyPort.GplLicenseForever), a
managed port of the LilyPond engraver that is conveyed as GPL-3.0-only. An application that
links it is conveyed on the same terms. Fresco.Brix's own sources are GPL-3.0-or-later — every
file carries that header — and the aggregate takes the narrower of the terms it combines, so the
application as distributed is **GPL-3.0-only**. That is a deliberate, ruled decision rather than
an accident of dependency: the samples whose libraries permit it live in the sibling
`CodeBrix.Samples` (permissive) and `CodeBrix.Samples.Gpl2` (classic games) repositories.

| Sample | What it is | Headline CodeBrix libraries |
| --- | --- | --- |
| [Fresco.Brix](#frescobrix) | A music-notation text editor and engraving environment — write LilyPond source, engrave it *without a LilyPond installation*, read the score back, play it, and export it | `CodeBrix.LilyPort`, `CodeBrix.Platform.*` (incl. the AdvancedTextEdit and AppSettings add-ins), `CodeBrix.Audio`, `CodeBrix.PdfDocCreate.Html2Pdf`, `CodeBrix.PdfRasterizer`, `CodeBrix.SkiaSvg` |

---

## Fresco.Brix

### What it is

**Fresco.Brix** is a complete desktop application for writing music in the
[LilyPond](https://lilypond.org) language: a specialised text editor, an engraver, a score
viewer, a MIDI player, a score wizard, a library of music-editing tools, nine bundled reference
manuals and a user guide — six desktop heads from one codebase.

It is a **faithful adaptation of [Frescobaldi](https://frescobaldi.org)**, Wilbert Berendsen's
LilyPond music editor, rebuilt as a CodeBrix.Platform application. Frescobaldi is openly and
gratefully credited: the About box says, in the application's own words, that *"Fresco.Brix is
modelled on Frescobaldi, the LilyPond music editor by Wilbert Berendsen and contributors, and is
deeply indebted to it. It is not Frescobaldi, and neither the Frescobaldi project nor its
authors endorse it."* Three of that author's projects are ported here — Frescobaldi itself,
`python-ly` and `qpageview` — and every ported file names the upstream file it came from on its
namespace line.

The one thing that makes it a different kind of program is what happens when you press Ctrl+M.
Frescobaldi runs the `lilypond` binary you installed. **Fresco.Brix has no external process, no
`PATH` lookup, no version chooser and no installer to run: the engraver is a NuGet package.**
`CodeBrix.LilyPort` is a managed port of LilyPond 2.27.2 that runs *inside* the application —
same parser, same Scheme, same layout engine, same SVG and MIDI output — so engraving a document
is a method call on a background thread. The engine is ready about 0.4 seconds after launch, and
the score comes back as SVG pages the application draws itself, point-and-click anchors intact.

Because the engine the user drives is LilyPort, no menu, panel, button, tooltip or status line in
the application says "LilyPond". The language you type is still LilyPond, the manuals are still
LilyPond's manuals under their own titles, and the About box states the lineage plainly — but the
chrome names the engine that is actually running.

### What it does / how to use it

**Write.** The editing surface is the `CodeBrix.Platform.AdvancedTextEdit` add-in driven by a
port of `python-ly`, so it knows the language rather than colouring it by regex: mode-aware
highlighting (notes, lyrics, chords, Scheme, markup, HTML), auto-indent, matching-pair
highlighting and navigation, code folding, an outline panel, bookmarks, search and replace,
"go to definition", "open file at cursor", document tooltips, split views over one document,
tabs, and named sessions that remember which documents (and which include path) belong together.
Autocomplete is context-aware — it knows whether the caret is in music, markup, a `\with` block,
a `\header`, a `\paper` or Scheme — and offers commands, contexts, grobs and grob properties,
engravers, clefs, MIDI instruments, music glyphs, pitch-language names, and everything it has
harvested from the document and its includes (the variables and markup commands it defines, and
the words in its lyrics, markup, strings and comments). A snippet library with per-snippet
shortcuts sits beside **22 native editor commands** — delete line, change case,
comment/uncomment, duplicate, move to the next blank line, smart quotes, insert a colour, insert
a MIDI tempo — on the same keys the original uses.

**Start from a wizard.** The **Score Wizard** (Ctrl+Shift+N) builds a complete score from
**94 instrument and container part types in 9 categories** — strings, plucked strings, woodwinds,
brass, vocal, keyboard, percussion, special, and containers like staff groups and score
structures — with per-part settings, a pitch language, a metronome mark, and full header fields.
Its output was verified character for character against the original's.

**Engrave.** Ctrl+M runs the engine on the current document — preview, publish, or a custom run
with your own options; autocompile can do it for you as you type. The **log panel** streams the
engine's own messages live, and every `file:line:column` in the log is a link back into the
editor. The layout-control debug modes (grob anchors, grob names, voice colouring, spacing
annotation, paper columns) — Frescobaldi's own formatters, shipped here as assets — have their
own panel.

**Read the score.** The **Music View** draws the engine's SVG pages as a paged, scrolling,
zoomable view. Point-and-click works **both ways**: click a note in the score and the caret lands
on the source that produced it; press Ctrl+J and the score scrolls to what the caret is on.
Rubber-band a region for **Copy to Image**, maximize the view over the whole window, and open the
same view over the bundled manuals.

**Hear it.** MIDI plays **in process** through `CodeBrix.Audio`'s SoundFont synthesizer with the
bundled GeneralUser GS bank (30.8 MB, 128 GM programs plus 13 percussion kits) — transport,
position, tempo and output-volume control, no external synth, no MIDI ports to configure.

**Work on the music.** Transpose; change the pitch language; convert between absolute and
relative; double or halve durations, add or remove dots, copy and paste a rhythm; strip
articulations, ornaments, instrument scripts, slurs, beams, ligatures, dynamics or comments out
of a selection; hyphenate lyrics with **24 hyphenation dictionaries**; reformat
the source; edit a fragment in place; and set the score's text and music fonts from a four-tab
**Document Fonts** dialog that lists the fonts the engine can actually see, previews them over
six sample scores, and writes the `\paper` block for you.

**Bring music in, send it out.** **File ▸ Import** converts **MusicXML**, **ABC** and **MIDI**
into LilyPond source — each with the original's own option dialog, each running the converter
in process. Export gives you **PDF** (vector, with the engine's own faces subset into the file),
**PNG**, **SVG**, **Copy to Image**, **MusicXML** (schema-conformant or not written at all),
**audio** (MIDI rendered to WAV), and colour-highlighted **HTML** of the source.

**Update old files.** Tools ▸ *Update with convert-ly* runs the same conversion rules LilyPond's
own `convert-ly` uses — ported into the LilyPort package, and run in process like everything else
— over a document written for an older version, shows you a coloured diff of what it would
change, and applies it as a single undo step.

**Look things up.** F9 opens the **Documentation Browser** over **nine bundled manuals — 3,368
pages, 27.2 MB of vector PDF** (Learning, Notation, Usage, Extending, Internals, Essay, Music
Glossary, Changes, Contributor), each with its own outline; the manuals are generated by the
engine itself rather than downloaded. Shift+F9 on a grob or command name jumps to the page that
documents it. F1 opens the **user guide**: **69 pages** of the application's own documentation,
cross-linked, rendered natively (no embedded browser).

**Make it yours.** Ten preferences pages — general, editor, fonts & colours (with a text-format
editor and colour schemes), shortcuts, MIDI, documentation, paths, tools, Music View and helper
applications — all persisted through the `CodeBrix.Platform.AppSettings` add-in. The interface
speaks **13 languages** (Czech, German, Spanish, French, Galician, Italian, Dutch, Polish,
Brazilian Portuguese, Russian, Swedish, Turkish, Ukrainian) plus English, using Frescobaldi's own
translation catalogs; anything the catalogs do not have falls back to English. Opening a file
from your file manager while the application is running adds a **tab to the running window**
instead of starting a second copy.

### Solutions — what to open where

| Solution | Use on | Contains |
| --- | --- | --- |
| `Fresco.Brix/Fresco.Brix.slnx` | Linux, macOS, Windows | Everything that ships: the shared UI project, the application core, all six heads, the two libraries and the three test projects |

There is one solution and it is the same on every platform — the Windows-only head
(`Fresco.Brix.WinWpfSkia`, `net10.0-windows`) simply does not build off Windows, and the rest
build everywhere. `Fresco.Brix/global.json` selects the **Microsoft.Testing.Platform** test
runner for the whole solution.

The 24 programs under `Fresco.Brix/tools/` are deliberately **not** in the solution. They ship
nothing, they are built and run on demand, and two of them (`tools/manuals`, `tools/symbolicons`)
generate assets that are committed.

### The six heads

One application core (`Fresco.Brix.Core`) and one shared XAML UI project (`Fresco.Brix.UI`,
a `.shproj`) drive all six. Each head is a `Program.cs` plus exactly one runtime package.

| Project | Platform / windowing | Runtime package |
| --- | --- | --- |
| `src/Fresco.Brix.LinuxX11` | Linux desktop, X11 | `CodeBrix.Platform.Runtime.Skia.X11.ApacheLicenseForever` |
| `src/Fresco.Brix.LinuxWayland` | Linux desktop, native Wayland | `…Runtime.Skia.Wayland…` |
| `src/Fresco.Brix.LinuxFrameBuffer` | Linux framebuffer (kiosk/embedded, no display server) | `…Runtime.Skia.FrameBuffer…` |
| `src/Fresco.Brix.MacOS` | macOS | `…Runtime.Skia.MacOS…` |
| `src/Fresco.Brix.Win32Skia` | Windows, native Win32 window | `…Runtime.Skia.Win32…` |
| `src/Fresco.Brix.WinWpfSkia` | Windows, Skia hosted in a WPF window (`net10.0-windows`) | `…Runtime.Skia.Wpf…` |

All six build with **0 warnings and 0 errors**; the X11 head is the one verified interactively,
end to end, at every stage of the build-out.

### How the projects/files/folders are organized

```
Fresco.Brix/
├─ Fresco.Brix.slnx                  The one solution (global.json → MTP test runner)
├─ THIRD-PARTY-NOTICES.txt           The attribution ledger: 10 sections, per-file licences
│
├─ src/
│  ├─ Fresco.Brix.UI/                Shared XAML (.shproj): App.xaml + Views/MainPage.xaml
│  ├─ Fresco.Brix.Core/              The application — 258 C# files, ~81,800 lines
│  │   ├─ Commands/                  Action collections, KeySequence, shortcut tables
│  │   ├─ Completion/                Autocomplete models and sources
│  │   ├─ Documentation/             Manual catalog, PDF manual view, Shift+F9 context help
│  │   ├─ DocumentFonts/             The four-tab Document Fonts dialog and \paper composer
│  │   ├─ Documents/                 Document manager, per-document editor state, meta-info
│  │   ├─ Editor/                    Highlighter, indenter, matcher, folding, cursor logic
│  │   ├─ Engrave/                   The engine host: jobs, ordering, autocompile, log
│  │   ├─ Export/                    PDF/PNG/SVG, MusicXML, audio (WAV), coloured HTML
│  │   ├─ Import/                    MusicXML / ABC / MIDI import over LilyPort.Importers
│  │   ├─ Midi/                      Player service, SoundFonts, MIDI-input seam
│  │   ├─ MusicView/                 Score panel, point-and-click both ways, context menu
│  │   ├─ ObjectEditor/              Grob property editor (experimental-features toggle)
│  │   ├─ Preferences/               The ten preferences pages + the values they store
│  │   ├─ QuickInsert/               Articulations/dynamics/spanners/bar-lines palettes
│  │   ├─ ScoreWizard/               94 part types in 9 categories, and the score builder
│  │   ├─ Search/  Sessions/  Snippets/  Tools/  UserGuide/  ViewModels/  Widgets/
│  │   ├─ Shell/                     Dock shell, panels, dialogs, menus, shortcut registrar
│  │   ├─ Services/                  Settings facade, app info, debug info, helper apps, i18n
│  │   └─ assets/
│  │       ├─ docs/                  Nine manuals: 3,368 pages, 28,487,108 bytes + MANIFEST
│  │       ├─ userguide/             69 markdown pages + 5 screenshots
│  │       ├─ i18n/<lang>/LC_MESSAGES/  13 compiled gettext catalogs
│  │       ├─ hyphdicts/             24 hyphenation dictionaries + a README per dictionary
│  │       ├─ soundfonts/            GeneralUser GS 2.0.3 (+ its licence and changelog)
│  │       ├─ symbols/               135 Quick Insert icons, engraved by the engine itself
│  │       ├─ fonttemplates/         Six Document Fonts preview scores
│  │       └─ layoutcontrol/         The layout-control debug formatters
│  ├─ libs/Fresco.Brix.Ly/           python-ly port — 71 files, ~35,400 lines. Platform-free:
│  │                                 references no CodeBrix package at all, tests host-free
│  ├─ libs/Fresco.Brix.MusicView/    qpageview subset — 22 files, ~5,800 lines: the paged
│  │                                 view, the SVG page, PDF/PNG/SVG export
│  └─ Fresco.Brix.{LinuxX11,LinuxWayland,LinuxFrameBuffer,MacOS,Win32Skia,WinWpfSkia}/
│                                    Six thin heads: one Program.cs, one runtime package
│
├─ tests/
│  ├─ Fresco.Brix.Core.Tests/            3,417 tests
│  ├─ libs/Fresco.Brix.Ly.Tests/           807 tests (+ the vendored MusicXML 4.0 schema)
│  └─ libs/Fresco.Brix.MusicView.Tests/    100 tests
│
└─ tools/                            24 repo programs that ship nothing:
   ├─ manuals, symbolicons           …generate committed assets (PDF manuals, symbol icons)
   ├─ datagen, snippetdata, unicodeblocks, charsettables   …generate committed .g.cs data
   ├─ i18nharvest                    …maps upstream msgids onto ours (renamed-string table)
   └─ lexprobe, docinfoprobe, rhythmprobe, pitchprobe, musicprobe, domprobe, colorizeprobe,
      varprobe, scorewizprobe, hyphenprobe, midiprobe, midiinputprobe, musicxmlprobe,
      importprobe, snippetprobe, userguideprobe, fontprobe   …run the ORIGINAL code and
      record what it answers, so the ports are tested against upstream and never against
      themselves
```

Dependency directions are one-way: `UI → Core → { Ly, MusicView, CodeBrix.LilyPort, packages }`.
`Fresco.Brix.Ly` knows nothing about the UI framework *or* the engine — it is pure ported logic
with host-free tests. `Fresco.Brix.MusicView` knows the UI framework but not the engine. No
reference of any kind leaves the `Fresco.Brix` folder: every CodeBrix library, including the
engraver, arrives from nuget.org.

### How it uses the CodeBrix libraries

`src/Fresco.Brix.Core/Fresco.Brix.Core.csproj`:

| Package | Version | Role |
| --- | --- | --- |
| `CodeBrix.LilyPort.GplLicenseForever` | 1.0.244.98 | **The engraver.** A managed port of LilyPond 2.27.2 — parser, Scheme interpreter, layout engine, SVG/MIDI backends — run in process. Also supplies `convert-ly` (`DocumentConverter`), the MusicXML/ABC/MIDI importers, and the engine's own text faces |
| `CodeBrix.Platform.ApacheLicenseForever` | 1.0.243.226 | The cross-platform XAML/UI framework and the "Simple" MVVM toolkit (`SimpleViewModel`, `SimpleCommand`, `SimpleServiceResolver`, `SimpleDialog`) |
| `CodeBrix.Platform.AdvancedTextEdit.ApacheLicenseForever` | 1.0.243.226 | The code editor add-in: `TextDocument`, text areas, undo stack, rendering — two views over one document, which is what split views are |
| `CodeBrix.Platform.AppSettings.ApacheLicenseForever` | 1.0.243.226 | The settings store behind every preference, session and remembered window state |
| `CodeBrix.Audio.MitLicenseForever` | 1.0.241.985 | SoundFont synthesis for MIDI playback and for rendering a score to WAV |
| `CodeBrix.PdfRasterizer.MitLicenseForever` | 1.0.243.38 | Draws the nine bundled manuals in the Documentation Browser (and their outlines) |
| `CodeBrix.Platform.Fonts.Roboto.OflLicenseForever` | 1.0.240.51 | The interface font |
| `CodeBrix.Platform.Fonts.RobotoMono.OflLicenseForever` | 1.0.238.534 | The editor font |
| `Microsoft.Extensions.Hosting` / `.Logging.Console` | 10.0.11 | DI host and console logging |

`src/libs/Fresco.Brix.MusicView/Fresco.Brix.MusicView.csproj`:

| Package | Version | Role |
| --- | --- | --- |
| `CodeBrix.SkiaSvg.MitLicenseForever` | 1.0.238.140 | Parses the engine's SVG pages into a retained scene graph — whose `<a>` element bounds *are* the point-and-click geometry |
| `CodeBrix.Platform.SkiaSharp.Views.MitLicenseForever` | 4.151.0 | The `SKXamlCanvas` the pages are drawn on |
| `CodeBrix.PdfDocCreate.MitLicenseForever` + `.Html2Pdf` | 1.0.243.38 | PDF export — vector, with the engine's own faces registered and CFF-subset into the file |
| `CodeBrix.Platform.ApacheLicenseForever` | 1.0.243.226 | The UI framework the paged view is built on |

Each head adds exactly one package:
`CodeBrix.Platform.Runtime.Skia.{X11,Wayland,FrameBuffer,MacOS,Win32,Wpf}.ApacheLicenseForever`
(1.0.243.226). `CodeBrix.Sqlite` arrives transitively, underneath AppSettings, and
`CodeBrix.LilyScheme` transitively underneath LilyPort. The test projects use **xUnit v3 4.0**
with `SilverAssertions.ApacheLicenseForever`; `tools/manuals` uses `CodeBrix.PdfRasterizer` and
`tools/symbolicons` uses `CodeBrix.LilyPort` directly.

### Why it's noteworthy for a CodeBrix developer

- **The largest single-application sample in the family, and it is still one codebase and six
  heads.** ~123,000 lines of C# across the core and two libraries, a dock shell with eleven
  dockable panels, twenty dialogs, a menu system, a shortcut system — and the six heads are still nothing but
  `Program.cs` plus one runtime package each. If you want to see how far the "one shared UI,
  many platform heads" arrangement scales before it needs per-head special-casing, this is the
  sample that answers it.
- **A heavyweight native-shaped engine as a NuGet package, running in process.** No external
  binary, no `Process.Start`, no temp-file protocol, no "please install LilyPond first" dialog.
  `Engrave/` is worth reading as a template for hosting *any* long-running engine in a
  CodeBrix.Platform app: one engine per process, loaded in the background with progress in the
  title bar, a per-document scratch directory, a job queue with autocompile debounce, cancellation
  at the engine's own boundaries, and the engine's live output posted onto the captured
  `SynchronizationContext` because it writes from its own thread.
- **The platform add-ins in real, load-bearing use.** `AdvancedTextEdit` is not a demo text box
  here: it carries a language-aware highlighter, folding, matching-pair navigation, autocomplete
  and two views over one document. `AppSettings` is the single settings store behind ten
  preferences pages, sessions, and per-document metadata — reached through one thin facade
  (`Services/SettingsStore`), the single file in the application that names the add-in's type,
  behind which 84 other files read and write settings without knowing what the store is. `PdfRasterizer`
  renders 3,368 pages of documentation, `Html2Pdf` writes the vector score PDF, `CodeBrix.Audio`
  plays and renders MIDI, and `SkiaSvg`'s scene graph doubles as hit-test geometry.
- **Ports verified against the original, not against themselves.** Twenty-four programs under
  `tools/` run the upstream Python (or the engine) and record what it answers; the C# is then
  asserted against those recordings. Examples worth stealing: `varprobe` AST-lifts pure
  definitions out of modules that import a GUI toolkit that is not installed;
  `scorewizprobe/qtshim.py` stands in for the widget library so widget-shaped upstream code runs
  unchanged as an oracle; `midiprobe` runs each file in its own subprocess under a timeout
  because upstream hangs on some inputs. The result is **4,324 tests** — 3,417 core, 807 language,
  100 view — including 51 MusicXML documents proven against the W3C 4.0 schema (and 30 that are
  refused rather than written non-conformant), and a parity suite that reproduces upstream's user
  guide byte for byte.
- **Internationalization done the hard, correct way.** The 13 shipped catalogs are Frescobaldi's
  own compiled `.mo` files, looked up by the *verbatim upstream msgid*, with a harvest tool that
  reconciles 196 deliberately renamed strings — the ones this application had to reword because
  its chrome names a different engine — so a missing translation falls back to English instead of
  silently reintroducing the wrong name. `assets/i18n/README.txt` explains why the catalogs are
  never edited.
- **A licence ledger you can copy.** `THIRD-PARTY-NOTICES.txt` is ten sections and 43 KB of
  per-file provenance: what each hyphenation dictionary actually is (they came from one project
  and not one of them is under that project's licence), which SoundFont files ship and which
  deliberately do not, what was generated from upstream data, and an explicit "still owed"
  section that today reads *nothing*. It is the model for how a CodeBrix sample that aggregates
  GPL and permissive work states its terms.
- **Small platform lessons, already solved.** A window-level shortcut registrar that survives a
  focused text editor; a menu builder that re-hooks its handlers when a flyout closes; a dock
  shell with drawn splitters because the themed `Thumb` paints nothing on the Skia heads; a
  paged view that is *not* inside a `ScrollViewer` but scrolls by translating a viewport-sized
  surface. These are in `Shell/`, `Commands/` and `libs/Fresco.Brix.MusicView/`.

---

## Building

Everything builds with the plain **.NET 10 SDK** — no workloads, no native toolchain, no
LilyPond, nothing to install beyond the SDK itself:

```
dotnet build Fresco.Brix/Fresco.Brix.slnx -c Release
```

Run a head — on Linux with X11:

```
dotnet run --project Fresco.Brix/src/Fresco.Brix.LinuxX11 -c Release
```

…or `Fresco.Brix.LinuxWayland`, `Fresco.Brix.LinuxFrameBuffer`, `Fresco.Brix.MacOS`,
`Fresco.Brix.Win32Skia`, `Fresco.Brix.WinWpfSkia` (the last two on Windows). A head accepts file
paths on the command line; if the application is already running, the files open as tabs in the
running window and the second process exits.

The test projects are **Microsoft.Testing.Platform** executables. Run the binaries:

```
Fresco.Brix/tests/Fresco.Brix.Core.Tests/bin/Release/net10.0/Fresco.Brix.Core.Tests
Fresco.Brix/tests/libs/Fresco.Brix.Ly.Tests/bin/Release/net10.0/Fresco.Brix.Ly.Tests
Fresco.Brix/tests/libs/Fresco.Brix.MusicView.Tests/bin/Release/net10.0/Fresco.Brix.MusicView.Tests
```

⚠ A plain `dotnet test` against an MTP executable can report **"Zero tests ran"** — run the
binaries above (or `dotnet test --project <csproj>` / `--solution`), which is what the figures in
this README were measured with.

The first engrave after a rebuild takes about 30 seconds: the engine's boot cache is keyed to the
engine assemblies and is rebuilt once per set of bits. Every later start is about 0.4 seconds.

## License

Copyright (c) 2026 Jeremy Ellis and contributors.

This repository is free software, licensed under the **GNU General Public License, version 3**
— see [LICENSE](LICENSE) for the full text. Fresco.Brix's own sources are GPL-3.0-or-later and
the application as conveyed is GPL-3.0-only, for the reason given at the top of this file.
Third-party code, data and assets, with their own licences and authors, are itemized in
[Fresco.Brix/THIRD-PARTY-NOTICES.txt](Fresco.Brix/THIRD-PARTY-NOTICES.txt).
