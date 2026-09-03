# Fresco.Brix

Fresco.Brix is a desktop music-notation editor and engraving environment. You
write music in the LilyPond language in a language-aware code editor, press
Engrave, and the score comes back as pages you can read, click into, play,
annotate and export. The editor knows the language rather than coloring it by
regular expression: mode-aware highlighting for notes, lyrics, chords, Scheme,
markup and HTML; folding; matching-pair navigation; an outline; bookmarks;
context-aware autocompletion; snippets; split views over one document; tabs;
and named sessions that remember which documents belong together. A Score
Wizard builds a complete score from instrument and container part types. Music
tools transpose, change the pitch language, convert between absolute and
relative, reshape rhythms, strip articulations or dynamics out of a selection,
hyphenate lyrics and reformat the source. File import converts MusicXML, ABC
and MIDI into LilyPond source; export writes vector PDF, PNG, SVG, MusicXML,
WAV audio and color-highlighted HTML. MIDI plays in process through a SoundFont
bank, nine reference manuals and the application's own user guide are read
inside the window, and preferences, a shortcut editor and shipped interface
translations make it configurable.

For a CodeBrix.Platform developer it is the reference for hosting a
long-running engine in process, and for driving the platform's editor,
settings, PDF, audio and SVG libraries together, at full load, in one program
that runs unchanged on all six desktop heads.

## Credit, and what engraves the score

Fresco.Brix is a faithful adaptation of Frescobaldi, the LilyPond music editor
by Wilbert Berendsen and contributors, and is deeply indebted to it. It is not
Frescobaldi, and neither the Frescobaldi project nor its authors endorse it.
Three of that author's projects are ported here (Frescobaldi, python-ly and
qpageview), and every ported file names the upstream file it came from on its
namespace line. The Help > About box says the same thing in the application's
own words.

Scores are engraved in process by the CodeBrix.LilyPort library, a managed port
of the LilyPond engraver. No LilyPond installation is used or required: there
is no external process, no `PATH` lookup and nothing for the user to install.
The language being edited is LilyPond and the bundled manuals are LilyPond's
own, while the application's chrome names the engine that is actually running.

## What this sample shows a CodeBrix.Platform developer

- How to keep a library with expensive, process-global state alive as one
  singleton service and load it off the UI thread:
  [Host one long-running engine per process and load it in the background](../BLUEPRINTS.md#host-one-long-running-engine-per-process-and-load-it-in-the-background).
- How to let the user work while that load is still running, by folding the
  state into a bound window title:
  [Show a long background load in the window title instead of a splash screen](../BLUEPRINTS.md#show-a-long-background-load-in-the-window-title-instead-of-a-splash-screen).
- How to make a service that cannot be re-entered safe against a user who
  presses the button twice:
  [Run one job at a time against a process-global engine](../BLUEPRINTS.md#run-one-job-at-a-time-against-a-process-global-engine).
- How to deliver a library's live, mid-run output to listeners that touch
  UI-owned state:
  [Marshal live engine output onto the thread that started the job](../BLUEPRINTS.md#marshal-live-engine-output-onto-the-thread-that-started-the-job).
- How to give Core services one way onto the UI thread without teaching them
  anything about the UI:
  [Marshal work onto the UI thread with one delegate handed to services](../BLUEPRINTS.md#marshal-work-onto-the-ui-thread-with-one-delegate-handed-to-services).
- How a view model reaches file dialogs, the focused editor and a yes/no
  question without Core referencing a window:
  [Own window state in a view model and reach the view through one interface](../BLUEPRINTS.md#own-window-state-in-a-view-model-and-reach-the-view-through-one-interface).
- How to behave honestly about cancellation when a library can only honor it at
  certain boundaries:
  [Cancel work at the boundaries a library can honor](../BLUEPRINTS.md#cancel-work-at-the-boundaries-a-library-can-honor).
- How to run something expensive "as the user types" without running it on
  every keystroke:
  [Debounce automatic background work behind a timer with eligibility gates](../BLUEPRINTS.md#debounce-automatic-background-work-behind-a-timer-with-eligibility-gates).
- How to turn a tool's `file:line:column` messages into links that still point
  at the right place after the user has typed:
  [Turn tool diagnostics into clickable source locations that survive edits](../BLUEPRINTS.md#turn-tool-diagnostics-into-clickable-source-locations-that-survive-edits).
- How to run a tool over an unsaved document without writing anything beside
  the user's file:
  [Give each document a private scratch directory cleaned up at process exit](../BLUEPRINTS.md#give-each-document-a-private-scratch-directory-cleaned-up-at-process-exit).
- How to color an editor by grammar rather than by regular expression, once per
  document:
  [Attach a language-aware highlighter to the text editor add-in](../BLUEPRINTS.md#attach-a-language-aware-highlighter-to-the-text-editor-add-in).
- How to let a library with its own document abstraction operate on the live
  editor document, one undo step per batch:
  [Bridge a platform-free document model onto the editor text document](../BLUEPRINTS.md#bridge-a-platform-free-document-model-onto-the-editor-text-document).
- How folding, matching-pair navigation and auto-indent stay in agreement with
  the highlighter:
  [Fold match pairs and auto-indent from the same tokenization](../BLUEPRINTS.md#fold-match-pairs-and-auto-indent-from-the-same-tokenization).
- How two editor views over one document share their tokenization while keeping
  their own carets and folds:
  [Show two editor views over one document](../BLUEPRINTS.md#show-two-editor-views-over-one-document).
- How completion learns where the caret is in the grammar and what the document
  itself defines:
  [Offer context-aware autocompletion in the editor](../BLUEPRINTS.md#offer-context-aware-autocompletion-in-the-editor).
- How to draw a zoomable, scrollable document whose full content is far larger
  than any allocatable surface:
  [Draw a paged document view that scrolls by translating a viewport-sized surface](../BLUEPRINTS.md#draw-a-paged-document-view-that-scrolls-by-translating-a-viewport-sized-surface).
- How to get both fast vector redraw and clickable regions out of one SVG
  parse:
  [Parse SVG once into a scene graph and use its anchors as hit-test geometry](../BLUEPRINTS.md#parse-svg-once-into-a-scene-graph-and-use-its-anchors-as-hit-test-geometry).
- How two-way navigation between a rendered artifact and its source survives
  edits to that source:
  [Move the caret from a click in a rendered document and back again](../BLUEPRINTS.md#move-the-caret-from-a-click-in-a-rendered-document-and-back-again).
- How to show PDF pages inside an application that has no WebView anywhere:
  [Show PDF pages inside the application with PdfRasterizer](../BLUEPRINTS.md#show-pdf-pages-inside-the-application-with-pdfrasterizer).
- How to write a PDF that stays vector, with the exact faces you drew with
  subset into the file:
  [Write a vector PDF with PdfDocCreate and the Html2Pdf add-on](../BLUEPRINTS.md#write-a-vector-pdf-with-pdfdoccreate-and-the-html2pdf-add-on).
- How to play a MIDI file in process and render the same material to WAV
  offline:
  [Play a MIDI file and render one to WAV with the audio library](../BLUEPRINTS.md#play-a-midi-file-and-render-one-to-wav-with-the-audio-library).
- How an import or upgrade that used to shell out to a command-line tool
  becomes a library call and one undo step:
  [Convert a file through a library in process and apply the result as one undo step](../BLUEPRINTS.md#convert-a-file-through-a-library-in-process-and-apply-the-result-as-one-undo-step).
- How to build tool panels around a center area, resizable by dragging, that
  come back where the user left them:
  [Build a dock shell with drawn splitters and remember its arrangement](../BLUEPRINTS.md#build-a-dock-shell-with-drawn-splitters-and-remember-its-arrangement).
- How menus and toolbars follow command objects, with enabled and checked state
  that stays correct:
  [Build menus and toolbars in code from command objects](../BLUEPRINTS.md#build-menus-and-toolbars-in-code-from-command-objects).
- How accelerators fire before their menu has ever been opened, and are not
  swallowed by a focused editor:
  [Register window-level shortcuts that survive a focused text editor](../BLUEPRINTS.md#register-window-level-shortcuts-that-survive-a-focused-text-editor).
- How to show a modal dialog on the Skia heads that is neither clipped by the
  window nor collapsed to nothing:
  [Show and size a modal dialog on the Skia heads](../BLUEPRINTS.md#show-and-size-a-modal-dialog-on-the-skia-heads).
- How vector icons ship inside the assembly, follow the theme and recolor to
  its foreground, through one renderer:
  [Render embedded SVG icons through one renderer and pick the set by theme](../BLUEPRINTS.md#render-embedded-svg-icons-through-one-renderer-and-pick-the-set-by-theme).
- How to keep every reader and writer of settings ignorant of which store they
  are talking to:
  [Put the AppSettings add-in behind one facade](../BLUEPRINTS.md#put-the-appsettings-add-in-behind-one-facade).
- How a multi-page preferences dialog and named workspaces persist through that
  one store:
  [Persist preference pages and named sessions through that one store](../BLUEPRINTS.md#persist-preference-pages-and-named-sessions-through-that-one-store).
- How one shared XAML page runs on six desktop heads whose programs differ by a
  single call:
  [Run one shared XAML UI on six platform heads from one head program](../BLUEPRINTS.md#run-one-shared-xaml-ui-on-six-platform-heads-from-one-head-program).
- How the application container is built, design mode turned off, and the
  interface font named:
  [Bootstrap an application with SimpleServiceResolver and a default font](../BLUEPRINTS.md#bootstrap-an-application-with-simpleserviceresolver-and-a-default-font).
- How to get useful console diagnostics in Debug builds without the framework's
  own lines drowning yours:
  [Configure logging before the host is built](../BLUEPRINTS.md#configure-logging-before-the-host-is-built).
- How XAML constructs the view model and the code-behind hands it a `XamlRoot`
  for its dialogs:
  [Set a page DataContext in XAML and give the view model a XamlRoot](../BLUEPRINTS.md#set-a-page-datacontext-in-xaml-and-give-the-view-model-a-xamlroot).
- How opening a file from the file manager adds a tab to the window that is
  already up instead of starting a second copy:
  [Hand the files of a second launch to the running window and exit](../BLUEPRINTS.md#hand-the-files-of-a-second-launch-to-the-running-window-and-exit).
- How translations produced elsewhere are shipped, looked up and made to fall
  back to English rather than to something wrong:
  [Ship compiled gettext catalogs and look strings up by upstream msgid](../BLUEPRINTS.md#ship-compiled-gettext-catalogs-and-look-strings-up-by-upstream-msgid).
- How to keep one package list for six heads instead of six:
  [Put every package in a Core library and one runtime package in each head](../BLUEPRINTS.md#put-every-package-in-a-core-library-and-one-runtime-package-in-each-head).
- How a library whose project name and namespace differ keeps its XAML types
  where the markup says they are:
  [Give a library that references CodeBrix Platform its own RootNamespace](../BLUEPRINTS.md#give-a-library-that-references-codebrix-platform-its-own-rootnamespace).
- How a chunk of pure logic stays testable in a host-free process with no
  native assets at all:
  [Keep a ported library completely free of the UI framework](../BLUEPRINTS.md#keep-a-ported-library-completely-free-of-the-ui-framework).
- How bundled third-party data ships so that its notices travel beside it and
  the folder can be emptied without breaking the application:
  [Ship data assets beside the program so their licenses travel with them](../BLUEPRINTS.md#ship-data-assets-beside-the-program-so-their-licenses-travel-with-them).
- How the test projects are set up, and how a port is asserted against recorded
  answers from the thing it was ported from:
  [Set up test projects on the Microsoft Testing Platform and check a port against recorded answers](../BLUEPRINTS.md#set-up-test-projects-on-the-microsoft-testing-platform-and-check-a-port-against-recorded-answers).

## Building, running and testing

There is one solution, `Fresco.Brix/Fresco.Brix.slnx`, and it is the same on
Linux, macOS and Windows. It contains the shared UI project, the application
core, all six heads, the libraries under `src/libs` (in a `Libraries` solution
folder) and the test projects (in a `Tests` folder).

| Head project | Platform and windowing |
| --- | --- |
| `src/Fresco.Brix.LinuxX11` | Linux desktop, X11 |
| `src/Fresco.Brix.LinuxWayland` | Linux desktop, native Wayland |
| `src/Fresco.Brix.LinuxFrameBuffer` | Linux framebuffer, no display server |
| `src/Fresco.Brix.MacOS` | macOS |
| `src/Fresco.Brix.Win32Skia` | Windows, native Win32 window |
| `src/Fresco.Brix.WinWpfSkia` | Windows, Skia hosted in a WPF window |

All six are Skia heads; there are no native (WinUI 3, WPF, .NET MAUI) heads.
`Fresco.Brix.WinWpfSkia` targets `net10.0-windows` and only builds on Windows,
though `EnableWindowsTargeting` lets it restore elsewhere; every other project
targets `net10.0` and builds on any of the three operating systems.

Prerequisites are the plain .NET 10 SDK and nothing else. There are no
workloads, no native toolchain, no system libraries to install and nothing to
download at run time: the engraver, the SoundFont bank, the manuals, the
hyphenation dictionaries, the translation catalogs and the fonts all arrive
with the build. No account, token or network access is used anywhere in the
application, and the user supplies no data to make it start.

```text
dotnet build Fresco.Brix.slnx -c Release
dotnet run --project src/Fresco.Brix.LinuxX11 -c Release
```

Substitute `LinuxWayland`, `LinuxFrameBuffer`, `MacOS`, `Win32Skia` or
`WinWpfSkia` for another head. A head accepts file paths on the command line
along with `--line`, `--column` and `--encoding`; if a copy is already running,
those files open as tabs in that window and the second process exits. The first
engrave after a rebuild is slow while the engine's Scheme boot cache is
recorded for the new bits, and later starts are fast; the window is fully
usable while the engine loads, with the loading state shown in the title bar.

| Test project | Covers |
| --- | --- |
| `tests/Fresco.Brix.Core.Tests` | The application core: documents, editor tools, completion, engraving, export, import, the settings store, sessions, preferences, shortcuts, dock layout, MIDI, documentation, manuscripts, the score wizard, the user guide, i18n, the icon theme, the single-instance protocol, and the parity suites against recorded upstream answers |
| `tests/libs/Fresco.Brix.Ly.Tests` | The `Fresco.Brix.Ly` language library: lexer and tokenizer, document model, doc info, pitch, rhythm, colorize, the music DOM and MusicXML, plus schema-conformance tests against a vendored MusicXML schema |
| `tests/libs/Fresco.Brix.MusicView.Tests` | The paged view library: page layout, SVG pages, raster pages, overlays, the rectangle index and the exporters |

`Fresco.Brix/global.json` selects the Microsoft.Testing.Platform runner for the
whole tree, and every test project is an xUnit v3 self-executing binary
(`OutputType` is `Exe`, `UseMicrosoftTestingPlatformRunner` is `true`). A plain
`dotnet test` against such an executable can report that zero tests ran; run
the built binaries directly, or pass `--project <csproj>` or `--solution`:

```text
dotnet test --project tests/Fresco.Brix.Core.Tests/Fresco.Brix.Core.Tests.csproj -c Release
```

Two of the test projects render, so they add a native Skia package of their own
(a host-free test process brings its own) and set
`CodeBrixRuntimeIdentifier=skia` so that the published Platform reference
assemblies are swapped for the real implementations. `Fresco.Brix.Ly.Tests`
needs neither, because the library it tests references no CodeBrix package at
all. Fixtures live in a `fixtures/` folder per project, are copied to the
output with `PreserveNewest`, and are loaded from `AppContext.BaseDirectory`;
`src/Fresco.Brix.Core/InternalsVisibleTo.cs` grants the core test project access
to internals.

## How the projects and folders are organized

```text
Fresco.Brix/
  Fresco.Brix.slnx                 The one solution
  global.json                      Selects the Microsoft.Testing.Platform runner
  THIRD-PARTY-NOTICES.txt          The numbered attribution ledger the csproj comments cite
  src/
    Fresco.Brix.UI/                Shared XAML (.shproj + .projitems): App.xaml, Views/MainPage.xaml
    Fresco.Brix.Core/              The application; carries every package the heads need
      Commands/                    The command type, collections, key sequences, per-area command sets
      Completion/                  Context analysis, completion model, document harvest
      Documentation/               Manual catalog, PDF manual pages, outlines, context help
      DocumentFonts/               The Document Fonts dialog's model and its \paper composer
      Documents/                   Document manager, per-document state, scratch dirs, result files
      Editor/                      Highlighter, indenter, matcher, folding, token iteration, formats
      Engrave/                     The engine host: engine, jobs, queue, autocompile, errors, log
      Export/                      PDF, PNG and SVG, MusicXML, WAV audio, colored HTML
      Helpers/                     The generic-host provider SimpleServiceResolver builds from
      Import/                      MusicXML, ABC and MIDI import over the engine's own importers
      Manuscripts/                 The Manuscript Viewer's open-PDF list and its link reader
      Midi/                        Player service over the audio library, SoundFonts, MIDI input seam
      MusicView/                   Score panel, point-and-click both ways, typeface and PDF font seams
      ObjectEditor/                Grob property editor behind an experimental-features preference
      Preferences/                 The preferences dialog, its pages and the values they store
      QuickInsert/                 Articulation, dynamic and spanner palettes, and the SVG icon renderer
      ScoreWizard/                 Instrument and container part types, and the score builder
      Search/ Sessions/ Snippets/ Tools/ UserGuide/ Widgets/
      Services/                    Settings facade, app info, i18n, helpers, single instance, icons
      Shell/                       Dock shell, panels, dialogs, menus, toolbars, shortcut registrar
      ViewModels/                  MainViewModel and the IWindowBridge interface
      assets/                      Manuals, user guide, catalogs, dictionaries, SoundFont, icons, symbols
    libs/Fresco.Brix.Ly/           The python-ly port; references no CodeBrix package at all
    libs/Fresco.Brix.MusicView/    Paged score view, SVG pages, overlays and exporters
    Fresco.Brix.LinuxX11/          Head: one Program.cs and one runtime package
    Fresco.Brix.LinuxWayland/      Head
    Fresco.Brix.LinuxFrameBuffer/  Head
    Fresco.Brix.MacOS/             Head
    Fresco.Brix.Win32Skia/         Head
    Fresco.Brix.WinWpfSkia/        Head (net10.0-windows)
  tests/
    Fresco.Brix.Core.Tests/        Core tests, plus fixtures/ of recorded upstream answers
    libs/Fresco.Brix.Ly.Tests/     Language-library tests, plus fixtures/ and a vendored schema/
    libs/Fresco.Brix.MusicView.Tests/  View-library tests, plus fixtures/ of real engine SVG output
  tools/                           Programs that ship nothing: asset generators and upstream probes
```

Dependencies run one way. Each head file-links the shared `.projitems` (so
`App.xaml` and `MainPage.xaml` are compiled into the head itself),
project-references `Fresco.Brix.Core`, and adds exactly one runtime package of
its own; nothing else lives in a head. Core project-references both libraries
under `src/libs` and names every other package. `Fresco.Brix.MusicView`
references the platform, the Skia views, the SVG parser and the PDF writers,
but never the engine: the engine reaches the view only through the SVG files it
wrote and through a typeface interface the host fills in. `Fresco.Brix.Ly`
references nothing at all, which is what lets its tests run host-free. No
reference of any kind leaves the `Fresco.Brix` folder.

The programs under `tools/` are deliberately not in the solution and ship
nothing. Some generate or copy in assets that are then committed, some generate
the committed `.g.cs` data files (each of which names its generator in a header
comment), and the rest run the original Python, or the engine itself, to record
the oracles the parity tests assert against.

## CodeBrix libraries and add-ins used

| Library or add-in | What it does in this application | Where |
| --- | --- | --- |
| CodeBrix.Platform | The XAML framework, the six heads' UI, and the Simple toolkit (`SimpleServiceResolver`, `SimpleViewModel`) | `src/Fresco.Brix.UI/App.xaml.cs`, `src/Fresco.Brix.Core/ViewModels/MainViewModel.cs`, `src/Fresco.Brix.Core/Helpers/HostHelper.cs`, all of `Shell/` |
| The CodeBrix.Platform runtime for each head | One runtime package per head project, and nothing else in a head | the csproj in each of `src/Fresco.Brix.LinuxX11`, `LinuxWayland`, `LinuxFrameBuffer`, `MacOS`, `Win32Skia`, `WinWpfSkia` |
| CodeBrix.LilyPort | The engraver, run in process; also the upgrade converter, the MusicXML, ABC and MIDI importers, and the engine's own font assets | `Engrave/LilyPortEngine.cs`, `Engrave/LilyPondJob.cs`, `Engrave/TextJobs.cs`, `Import/ImportJob.cs`, `Shell/ConvertLyDialog.cs`, `MusicView/LilyPortTypefaceResolver.cs`, `MusicView/LilyPortScorePdfFonts.cs`, `DocumentFonts/` |
| CodeBrix.LilyScheme | The Scheme interpreter the engine runs on, used directly where the engine is loaded | `Engrave/LilyPortEngine.cs` |
| The CodeBrix.Platform.AdvancedTextEdit add-in | The editing surface: text document, text areas, highlighting, folding, rendering, code completion, editing services | all of `Editor/` and `Completion/`, `Documents/AteLyDocument.cs`, `Documents/EditorDocument.cs`, `Shell/EditorView.cs`, `Shell/LogPanel.cs`, `Shell/OutlinePanel.cs`, `Shell/ShortcutRegistrar.cs`, `Search/SearchBar.cs`, `Snippets/SnippetInserter.cs`, `Tools/` |
| The CodeBrix.Platform.AppSettings add-in | The one settings store behind every preference, session, shortcut, snippet and remembered layout | `Services/SettingsStore.cs`, the only file that names one of the add-in's types |
| CodeBrix.Audio | SoundFont and SFZ synthesis for MIDI playback, and for rendering a score to WAV | `Midi/MidiPlayerService.cs`, `Midi/MidiSong.cs`, `Midi/SoundFonts.cs`, `Export/AudioExport.cs` |
| CodeBrix.PdfRasterizer | Draws the bundled manuals' pages in the Documentation Browser | `Documentation/PdfManual.cs` |
| CodeBrix.PdfDocuments | Reads PDF outlines, link annotations and page information | `Documentation/ManualOutline.cs`, `Manuscripts/PdfLinks.cs`, `src/libs/Fresco.Brix.MusicView/Export/ScorePdf.cs` |
| CodeBrix.Imaging | The pixel buffers a rasterized page arrives in | `Documentation/PdfManual.cs` |
| CodeBrix.SkiaSvg | Parses an engraved SVG page into a retained scene graph whose anchor bounds are the point-and-click geometry; also renders the embedded icon and symbol SVGs | `src/libs/Fresco.Brix.MusicView/Pages/SvgPage.cs`, `QuickInsert/SymbolIcons.cs` |
| CodeBrix.Platform.SkiaSharp.Views | The `SKXamlCanvas` the paged view draws on | `src/libs/Fresco.Brix.MusicView/View/MusicViewControl.cs` |
| CodeBrix.PdfDocCreate and its Html2Pdf add-on | Writes the vector score PDF, with the engine's own faces registered and CFF-subset into the file | `src/libs/Fresco.Brix.MusicView/Export/ScorePdf.cs` |
| CodeBrix.Platform.Fonts.Roboto | The interface font, and the fallback faces consulted for characters it has no glyph for | `src/Fresco.Brix.UI/App.xaml`, `App.xaml.cs` |
| CodeBrix.Platform.Fonts.RobotoMono | The editor's monospace font | `src/Fresco.Brix.UI/App.xaml`, `Views/MainPage.xaml.cs` |

Read the csproj files for what is named where, and for the exact packages. Core
names the platform, the editor and settings add-ins, the audio library, the PDF
rasterizer, the engraver and the two font packages; the paged-view library names
the platform, the Skia views, the SVG parser and the PDF writers. The rest
arrive with those: the csproj comments record that the PDF rasterizer brings the
PDF document reader and the imaging library with it, that the settings add-in
brings a database library that nothing under `src/` opens itself, and that the
engraver's facade declares the Scheme interpreter as its one real dependency,
which is why `Engrave/LilyPortEngine.cs` can use the interpreter as a
first-class type without the package being named in a csproj.

| Third-party library | What it does in this application | Where |
| --- | --- | --- |
| Microsoft.Extensions.Hosting | The generic host `SimpleServiceResolver` builds its container from | `src/Fresco.Brix.Core/Helpers/HostHelper.cs` |
| Microsoft.Extensions.Logging.Console | Console logging in Debug builds only | `src/Fresco.Brix.UI/App.xaml.cs` |
| xUnit v3, SilverAssertions, the test SDK | The test projects | the csproj files under `tests/` |
| A native Skia package | Native Skia for the two host-free test processes that render | `tests/Fresco.Brix.Core.Tests`, `tests/libs/Fresco.Brix.MusicView.Tests` |

## Worth studying in this application

### The engraver as an in-process service

Engraving is a method call, not a process launch. The engine is registered as a
singleton with `SimpleServiceResolver` in `src/Fresco.Brix.UI/App.xaml.cs`
because its state is process-global, not because a singleton is convenient; the
view model resolves it and starts the load without awaiting it, so the window
is up and usable while the interpreter boots. Every call into the engine goes
through one gate that first waits for readiness and then takes a semaphore, on
a thread with far more stack than a default CLR thread has, so two calls can
never overlap. Read `src/Fresco.Brix.Core/Engrave/LilyPortEngine.cs` first, then
the engine block in `src/Fresco.Brix.Core/ViewModels/MainViewModel.cs`, then
`Engrave/JobManager.cs`, `Engrave/JobQueue.cs` and `Engrave/Engraver.cs` for the
one-slot job model. The sharp edges are recorded in the comments: the include
path is not reset between runs, cancellation is honored only at the boundaries
the engine can honor it at (a token goes into the library's own options object,
never to the `Task.Run()` that hosts it), and command state must not be
computed from the "job started" event alone. In the MVVM shape the view model
owns the Engrave command and the `IsEngraving` state, raises it with
`[AffectsCommands]`, and the page contributes nothing but the marshaling
delegate. See
[Host one long-running engine per process and load it in the background](../BLUEPRINTS.md#host-one-long-running-engine-per-process-and-load-it-in-the-background),
[Run one job at a time against a process-global engine](../BLUEPRINTS.md#run-one-job-at-a-time-against-a-process-global-engine),
[Cancel work at the boundaries a library can honor](../BLUEPRINTS.md#cancel-work-at-the-boundaries-a-library-can-honor),
[Marshal live engine output onto the thread that started the job](../BLUEPRINTS.md#marshal-live-engine-output-onto-the-thread-that-started-the-job)
and
[Show a long background load in the window title instead of a splash screen](../BLUEPRINTS.md#show-a-long-background-load-in-the-window-title-instead-of-a-splash-screen).

### Engrave output that lands back in the source

A run produces three things the application has to place: messages, output
files and errors. `Engrave/EngraveJob.cs` captures the synchronization context
of the thread that started the job and posts (never sends) every message back
on it, because the log writes into a text document and the error collector puts
anchors into one. `Engrave/EngraveErrors.cs` parses each `file:line:column:`
prefix, resolves a relative name against the job's own directory rather than the
process working directory, and binds the location to a text anchor so it still
points at the right place after the user has typed; `Shell/LogPanel.cs` renders
the matches as link spans. Output never lands beside the user's file:
`Documents/ScratchDir.cs` and `Services/PathUtil.cs` give each document a
private temporary directory under one process-wide root that a process-exit
handler removes, best-effort, and map a scratch path back to the document it
came from so an error about a scratch copy opens the right tab. Autocompile,
in `Engrave/AutoCompiler.cs`, is the same machinery on a timer: a one-shot
timer restarted on every change, a tick that is a series of cheap refusals, and
an eligibility test that hashes the document's tokens rather than its
characters, so reformatting or editing a comment starts nothing. In the MVVM
shape the view model owns the log document and the autocompile toggle as bound
state, hands the services its `InvokeOnMainThread` marshaler, and the panel
only draws. See
[Turn tool diagnostics into clickable source locations that survive edits](../BLUEPRINTS.md#turn-tool-diagnostics-into-clickable-source-locations-that-survive-edits),
[Give each document a private scratch directory cleaned up at process exit](../BLUEPRINTS.md#give-each-document-a-private-scratch-directory-cleaned-up-at-process-exit)
and
[Debounce automatic background work behind a timer with eligibility gates](../BLUEPRINTS.md#debounce-automatic-background-work-behind-a-timer-with-eligibility-gates).

### One tokenization behind the whole editor

The editing surface is the AdvancedTextEdit add-in, and everything
language-aware in it is driven from one tokenization per document.
`Documents/DocumentEditorState.cs` is the per-document object that holds the
text document, the highlighter, the document bridge and the folding strategy;
read it first. `Editor/LyHighlighter.cs` implements the add-in's highlighter
and line-tracker interfaces over the ported lexer, and registers itself with a
weak line tracker so the per-line token cache follows edits without holding the
document alive. `Documents/AteLyDocument.cs` is the bridge that lets every
ported tool operate on the live document instead of a copy, applying each batch
of edits from the highest offset down inside one begin/end update pair, so a
transform is one Ctrl+Z. `Editor/LyFoldingStrategy.cs`, `Editor/TokenMatcher.cs`
and `Editor/Indenting.cs` read the same tokens, which is why folding does not
open on a brace inside a string. `Shell/EditorView.cs` shows what is per-view
(caret, colorizer instance, background renderer, folding manager) and what is
shared, which is what makes two views over one document agree; `Completion/`
adds an analyzer that maps the lexer's current parser to candidate models and a
harvest that reads identifiers off that same tokenization. In the MVVM shape a
document view model owns the per-document state object and exposes the editor
commands, while the view supplies only the control and its focus; the pitfalls
worth knowing are in the comments (a view not yet in the visual tree answers
false to `Focus()`, and the completion list must be filtered on construction and
again on `Loaded`). See
[Attach a language-aware highlighter to the text editor add-in](../BLUEPRINTS.md#attach-a-language-aware-highlighter-to-the-text-editor-add-in),
[Bridge a platform-free document model onto the editor text document](../BLUEPRINTS.md#bridge-a-platform-free-document-model-onto-the-editor-text-document),
[Fold match pairs and auto-indent from the same tokenization](../BLUEPRINTS.md#fold-match-pairs-and-auto-indent-from-the-same-tokenization),
[Show two editor views over one document](../BLUEPRINTS.md#show-two-editor-views-over-one-document)
and
[Offer context-aware autocompletion in the editor](../BLUEPRINTS.md#offer-context-aware-autocompletion-in-the-editor).

### The paged view: scores, manuals and manuscripts

One control draws all three. `src/libs/Fresco.Brix.MusicView/View/MusicViewControl.cs`
is a `Grid` holding a canvas the size of the viewport, with its own scroll
offset: the pages are drawn translated by that offset, because a tall score at
a high zoom would need a surface no one can allocate. Overlays (the rubber
band, the magnifier) reach the control through the small `IOverlayHost`
interface, so their arithmetic is testable without a window.
`Pages/SvgPage.cs` parses an engraved page once into a retained picture that
redraws at any zoom, and walks the same retained scene graph for anchor nodes,
turning each node's transformed bounds into fractions of the page; those
fractions are the clickable regions. Typefaces come from the host through
`Pages/IScoreTypefaceResolver.cs`, and the host's chain replaces the default
provider rather than standing in front of it, so a family nobody can answer
draws a box rather than quietly picking up a system font. The same view shows
PDFs: `Documentation/PdfManual.cs` rasterizes a page at a bucketed width and
hands the pixels over as an image source, and `Manuscripts/PdfLinks.cs` reads
link annotations, remembering that a page's size is turned by `/Rotate` while
an annotation's coordinates are not. Two-way point-and-click lives in
`MusicView/TextEditLink.cs` and `MusicView/PointAndClick.cs`: source locations
are bound to text anchors with `SurviveDeletion` set, because a rendered score
is normally older than the source it came from. In the MVVM shape the view
model owns the current document, the zoom and fit mode as bound properties and
the navigation history as commands; the panel forwards a click to a delegate
the page fills in with one line. See
[Draw a paged document view that scrolls by translating a viewport-sized surface](../BLUEPRINTS.md#draw-a-paged-document-view-that-scrolls-by-translating-a-viewport-sized-surface),
[Parse SVG once into a scene graph and use its anchors as hit-test geometry](../BLUEPRINTS.md#parse-svg-once-into-a-scene-graph-and-use-its-anchors-as-hit-test-geometry),
[Move the caret from a click in a rendered document and back again](../BLUEPRINTS.md#move-the-caret-from-a-click-in-a-rendered-document-and-back-again)
and
[Show PDF pages inside the application with PdfRasterizer](../BLUEPRINTS.md#show-pdf-pages-inside-the-application-with-pdfrasterizer).

### Import, export and hearing the result

Everything that used to be a command-line tool is a library call here.
`Import/ImportJob.cs` runs a MusicXML, ABC or MIDI conversion off the UI thread
and posts its messages through the same job channel the engraver uses;
`Import/ImportSettings.cs` turns the dialog's checkbox state into the
converter's own options object. `Shell/ConvertLyDialog.cs` shows a diff before
applying an in-place upgrade, and the upgrade is applied as a single replace
over the whole document, so a user who dislikes the result presses Ctrl+Z once.
On the way out, `src/libs/Fresco.Brix.MusicView/Export/ScorePdf.cs` writes a
vector PDF with the engine's own faces registered and CFF-subset into the file,
`Export/MusicXmlExport.cs` refuses to write output that would not conform
rather than writing it anyway, and `Export/AudioExport.cs` renders a score's
MIDI to WAV through its own bank cache, with a tail appended so the release is
not truncated. `Midi/MidiPlayerService.cs` is the playback side behind
`IMidiPlayer`: it opens no audio device until something is loaded, captures its
synchronization context at construction because the audio engine calls back on
a real-time thread, and uses the library's own end-of-playback event rather than
comparing position against duration. A sharp edge worth copying: a library takes
a string where a command-line tool took a path, so the encoding declared inside
the file has to be honored by your own reader. In the MVVM shape the view model
owns the import, export and transport commands and asks the page for a path
through the window bridge. See
[Convert a file through a library in process and apply the result as one undo step](../BLUEPRINTS.md#convert-a-file-through-a-library-in-process-and-apply-the-result-as-one-undo-step),
[Write a vector PDF with PdfDocCreate and the Html2Pdf add-on](../BLUEPRINTS.md#write-a-vector-pdf-with-pdfdoccreate-and-the-html2pdf-add-on)
and
[Play a MIDI file and render one to WAV with the audio library](../BLUEPRINTS.md#play-a-midi-file-and-render-one-to-wav-with-the-audio-library).

### The window shell: docking, menus, toolbars, shortcuts and icons

The shell is built in code from command objects rather than declared in XAML.
`Shell/DockShell.cs` and `Shell/SplitContainer.cs` place tool panels around a
center area with dividers that are plain `Grid` elements doing their own
pointer capture, because the themed `Thumb`, the standalone `ScrollBar` and the
themed tab controls paint nothing on the Skia heads; that is the house answer
throughout this application, and `Shell/TrackBar.cs` says the same about
`Slider`. `Shell/DockLayout.cs` stores divider positions as relative weights so
a layout survives a different screen, and the window's own `Bounds` are what is
saved, not the framed size an X11 window reports. `Shell/MenuBuilder.cs` and
`Shell/MainToolbar.cs` follow one command object per entry and re-read its state
on change, hooking again on `Loaded` because a flyout's items unload every time
the menu closes. `Shell/ShortcutRegistrar.cs` puts accelerators on the window's
root, not on menu items that are not in the visual tree until the menu is first
opened, and pushes a stacked input handler onto each editor so commands get
first refusal on a modified keystroke. `Shell/DialogSizing.cs` clamps a dialog
against the actual `XamlRoot` size before it is shown. Icons are embedded SVGs
under two logical-name prefixes, rendered by the one renderer in
`QuickInsert/SymbolIcons.cs` and chosen by `Services/IconTheme.cs`. In the MVVM
shape each of those entries is a `SimpleCommand` on a view model with
`[AffectsCommands]` doing what hand-written enable and check updates do here,
and the builders take the command list rather than the page. See
[Build a dock shell with drawn splitters and remember its arrangement](../BLUEPRINTS.md#build-a-dock-shell-with-drawn-splitters-and-remember-its-arrangement),
[Build menus and toolbars in code from command objects](../BLUEPRINTS.md#build-menus-and-toolbars-in-code-from-command-objects),
[Register window-level shortcuts that survive a focused text editor](../BLUEPRINTS.md#register-window-level-shortcuts-that-survive-a-focused-text-editor),
[Show and size a modal dialog on the Skia heads](../BLUEPRINTS.md#show-and-size-a-modal-dialog-on-the-skia-heads)
and
[Render embedded SVG icons through one renderer and pick the set by theme](../BLUEPRINTS.md#render-embedded-svg-icons-through-one-renderer-and-pick-the-set-by-theme).

### One settings store, behind one facade

`Services/SettingsStore.cs` is the only file under `src/` that names a type from
the settings add-in. It is registered as a singleton so the add-in's start-up
backup and pruning pass happens once per process, and it has a second
constructor that opens a store in a directory of its own, which is the seam
every test uses so that no test touches the real store. The accessors are
deliberately two-layered: scalars in a stable text encoding, and whole
collections as typed JSON under one key each, because the add-in has no
prefix-scan API by design. The consequences are worth reading before you copy
the pattern: never read one key both as a scalar and as a family (the typed
getter answers the type's default rather than throwing), and disposing the
facade must not close a store it did not open. `Preferences/` shows the values
side of it, with a two-method `IPreferenceValues` seam per page and a page that
loads on first build and saves only if it was built and touched;
`Sessions/SessionStore.cs` keys each named session by a generated group name so
that renaming a session never moves its data. In the MVVM shape each
preferences page is a view model over its values object and the store is
resolved, never reached through the page. See
[Put the AppSettings add-in behind one facade](../BLUEPRINTS.md#put-the-appsettings-add-in-behind-one-facade)
and
[Persist preference pages and named sessions through that one store](../BLUEPRINTS.md#persist-preference-pages-and-named-sessions-through-that-one-store).

### Six heads, one page, one bridge

Every head's `Program.cs` is the same file apart from the `Use...()` call that
names its windowing back end, and the WinWpfSkia head's one extra statement
setting a software render surface on its host. `src/Fresco.Brix.UI/App.xaml.cs`
does the rest: it names the interface font by direct `.ttf` path (merging the
font package's own resource dictionary does not work on the Skia heads, and the
comment in `App.xaml` says so), builds the container with
`SimpleServiceResolver.CreateInstance()`, and calls
`SimpleViewModel.SetIsDesignMode(false)` so view models do not take their
design-time path at run time. Logging is configured by a static method on `App`
called from `Main` before the host exists, with filters that keep the
framework's informational lines out of yours; the settings facade additionally
turns off the settings add-in's own console output in a static constructor,
because that write bypasses whatever the application configured.
`Views/MainPage.xaml` constructs `MainViewModel` as its `DataContext` and the
code-behind reacts once, guarded, to hand the view model a `XamlRoot` getter for
its dialogs and to subscribe to the properties the title and status line need.
The seam between them is `IWindowBridge`, declared in
`src/Fresco.Brix.Core/ViewModels/MainViewModel.cs` as a bag of delegate
properties the page assigns to its own private methods: file pickers, the active
editor, fullscreen, quit, and the three question shapes (confirm, alert, and
save/discard/cancel, kept separate because a report shown through a
discard/cancel confirm reads as an offer to throw work away). Every delegate is
allowed to be null, so the framebuffer head, which has no file dialogs, degrades
to doing nothing rather than throwing. The page also owns one `OnUiThread`
method and hands it to every service that raises events from a worker thread;
those services run the delegate inline when there is none, which is what keeps
them testable. In the MVVM shape that page work is exactly what belongs there,
and everything else moves to commands on the view model. See
[Run one shared XAML UI on six platform heads from one head program](../BLUEPRINTS.md#run-one-shared-xaml-ui-on-six-platform-heads-from-one-head-program),
[Bootstrap an application with SimpleServiceResolver and a default font](../BLUEPRINTS.md#bootstrap-an-application-with-simpleserviceresolver-and-a-default-font),
[Configure logging before the host is built](../BLUEPRINTS.md#configure-logging-before-the-host-is-built),
[Set a page DataContext in XAML and give the view model a XamlRoot](../BLUEPRINTS.md#set-a-page-datacontext-in-xaml-and-give-the-view-model-a-xamlroot),
[Own window state in a view model and reach the view through one interface](../BLUEPRINTS.md#own-window-state-in-a-view-model-and-reach-the-view-through-one-interface)
and
[Marshal work onto the UI thread with one delegate handed to services](../BLUEPRINTS.md#marshal-work-onto-the-ui-thread-with-one-delegate-handed-to-services).

### One window per desktop

Double-clicking a file in the file manager should add a tab, not start a second
copy that fights the first for the settings store.
`Services/RemoteInstance.cs` is called from every head's `Main` before the host
is built and before the store is opened for the process; if a running instance
answers, the files are handed over and this process ends. The endpoint is a
named pipe on Windows and a unix-domain socket everywhere else behind one
interface, named from the application, the user and the display, with numbered
fallbacks; every candidate is contacted before it is claimed, so a crashed
process leaves nothing that blocks a later launch. The running window
implements the small `IRemoteCommandTarget` interface (open a path, make it
current, move the caret, activate) and calls `Setup` once it can act on what it
is told, passing its UI-thread marshaler. In the MVVM shape those four members
are one-line forwards from the page to view-model commands, which is what they
already are here. See
[Hand the files of a second launch to the running window and exit](../BLUEPRINTS.md#hand-the-files-of-a-second-launch-to-the-running-window-and-exit).

### The interface language

Interface translations are compiled catalogs produced elsewhere, shipped beside
the program in per-language folders and looked up by the upstream message id.
`Services/LanguageSetup.cs` finds the catalog for a language (falling back from
a regional name to its base), `Services/MoFile.cs` reads it,
`Services/PluralExpression.cs` evaluates the plural form the catalog's own
header declares, and `Services/Translations.cs` is the one facade every
user-visible string passes through, answering the original English when there
is no translation. The language is installed at the top of the view model's
construction, before a single command, panel or dialog has built a caption,
which is why a language change takes effect at the next launch. Two rules are
worth taking: a string the application had to reword is in no catalog and shows
in English, so keep a table of those rather than renaming them back; and put
every lookup through one guard, so a translation that reintroduces a name the
application must not use is refused. The catalogs themselves are never edited,
because they are third-party work with their translators' names in the header,
and the whole folder can be emptied, leaving an English-only application that
still runs. See
[Ship compiled gettext catalogs and look strings up by upstream msgid](../BLUEPRINTS.md#ship-compiled-gettext-catalogs-and-look-strings-up-by-upstream-msgid).

### Project layout and the assets that ship beside the program

Every package is named in `src/Fresco.Brix.Core/Fresco.Brix.Core.csproj` or in
the paged-view library's csproj; a head is a `Page` glob, an import of the
shared `.projitems`, one project reference and one runtime package, which is
what keeps six heads from becoming six package lists. Core sets its own
`RootNamespace` so that the project name and the namespace can differ, and the
shared project sets a separate one of its own, which is what puts the generated
XAML types where `x:Class` says they are. `src/libs/Fresco.Brix.Ly` references
nothing at all, and the payoff is visible in its test project, which needs
neither native Skia nor the runtime-identifier lever the other two need. The
asset item groups in the Core csproj each carry a comment explaining the
decision: dictionaries and catalogs are content rather than embedded resources
precisely so their notices travel beside them, `%(RecursiveDir)` in the `Link`
is what preserves the per-language folder shape in the output, and icons are
embedded under two logical-name prefixes with a plain item beside them for the
license text. Every one of those asset folders can be emptied without the
application failing. See
[Put every package in a Core library and one runtime package in each head](../BLUEPRINTS.md#put-every-package-in-a-core-library-and-one-runtime-package-in-each-head),
[Give a library that references CodeBrix Platform its own RootNamespace](../BLUEPRINTS.md#give-a-library-that-references-codebrix-platform-its-own-rootnamespace),
[Keep a ported library completely free of the UI framework](../BLUEPRINTS.md#keep-a-ported-library-completely-free-of-the-ui-framework)
and
[Ship data assets beside the program so their licenses travel with them](../BLUEPRINTS.md#ship-data-assets-beside-the-program-so-their-licenses-travel-with-them).

### Testing a port against the thing it was ported from

The parity suites are what make this a port rather than a rewrite that happens
to look similar. Fixtures under `tests/Fresco.Brix.Core.Tests/fixtures/` hold
what the original answered for each probe input, and the tests assert the
port's answers against them; nothing in those files is recorded from the port's
own output. The recordings are made by the programs under `tools/`, which are
deliberately outside the solution, and their techniques transfer: one lifts the
pure functions out of an upstream module by walking its syntax tree, because
that module imports a GUI toolkit that is not installed; another installs a
shim standing in for the widget library so widget-shaped upstream code runs
unchanged; another runs each input in its own subprocess under a timeout and
records "not answered" with a reason, because a hang is not an answer and a
shorter fixture would be a dishonest one. On the plumbing side, give every test
its own scratch directory so no test can reach the real settings store, set the
runtime-identifier lever in any test project that calls platform code, and
remember that a plain `dotnet test` against a Microsoft.Testing.Platform
executable can report that zero tests ran. See
[Set up test projects on the Microsoft Testing Platform and check a port against recorded answers](../BLUEPRINTS.md#set-up-test-projects-on-the-microsoft-testing-platform-and-check-a-port-against-recorded-answers).

## Third-party content

[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) in this folder is the
numbered attribution ledger, and the csproj comments cite it by section. It
covers the ported code (Frescobaldi, python-ly and qpageview), the Frescobaldi
files shipped as assets (the layout-control formatters, the Document Fonts
sample scores, the user-guide pages, the symbol sources and the interface
translation catalogs with their translators named), the hyphenator and a
per-dictionary audit of the hyphenation dictionaries, data generated from
Frescobaldi's own tables, the engraver, the in-box GeneralUser GS SoundFont, the
bundled manuals under the GNU Free Documentation License, and the toolbar icons,
which are Tabler Icons under the MIT license as modified for Frescobaldi and
conveyed under its terms. License texts that have to travel with their files sit
beside them in the asset tree: `src/Fresco.Brix.Core/assets/docs/COPYING.FDL`,
`assets/icons/LICENSE-TablerIcons.txt`,
`assets/icons/README-frescobaldi-icons.txt`,
`assets/soundfonts/GeneralUser-GS_LICENSE.txt`, `assets/i18n/README.txt` and a
`README_hyph_*.txt` beside each hyphenation dictionary.

## License

Fresco.Brix's own sources are licensed under the GNU General Public License,
version 3 or later; the application as conveyed is GPL-3.0-only, because the
CodeBrix.LilyPort library it links is, and an aggregate takes the narrower of
the terms it combines. See [../LICENSE](../LICENSE).

Copyright (c) 2026 Jeremy Ellis and contributors
