# CodeBrix.Samples.Gpl3

This repository holds complete, runnable reference applications for the CodeBrix
family of .NET libraries. Each one is a real application rather than a snippet:
it starts, it does the whole job, it ships its assets, and it is built to show
how the libraries are meant to be consumed from a CodeBrix.Platform
application. If you are building your own application and want to see how
something is actually done, open the application that does it and read around
the code.

The house style is the same in every application. One shared view model layer
and one shared XAML UI drive every head, and each head is a thin project that
supplies only its platform plumbing: a `Program.cs` and the one runtime package
for its windowing system. The six CodeBrix.Platform heads are LinuxX11,
LinuxWayland, LinuxFrameBuffer, MacOS, Win32Skia and WinWpfSkia. Libraries are
consumed as packages, never as source references, so each application folder is
self-contained and nothing a build needs comes from outside it.

Everything in this repository is licensed under the GNU General Public License,
version 3. Fresco.Brix's own sources are GPL-3.0-or-later; the application as
conveyed is GPL-3.0-only, because the CodeBrix.LilyPort library it links is
conveyed on those terms and an aggregate takes the narrower of the terms it
combines. That is why this repository exists separately: it holds the reference
applications whose libraries land on GPL-3.0, so that the licensing of one
application never sets the terms for the rest. The sibling repositories
CodeBrix.Samples and CodeBrix.Samples.Gpl2 hold the others, permissively
licensed applications in the first and the classic games in the second.

The documentation is organized the same way throughout. Every application
folder has a `README.md`, the detailed guide to that application, and a
`THIRD-PARTY-NOTICES.txt`, its attribution record. [BLUEPRINTS.md](BLUEPRINTS.md)
at the root collects the how-tos mined from all of the applications.

## The applications

| Application | What it is | Headline CodeBrix libraries |
| --- | --- | --- |
| [Fresco.Brix](Fresco.Brix/README.md) | A desktop music-notation editor and engraving environment: write music in the LilyPond language in a language-aware code editor, engrave it in process with no LilyPond installation, then read, play, annotate and export the score | CodeBrix.LilyPort, CodeBrix.Platform with the AdvancedTextEdit and AppSettings add-ins, CodeBrix.Audio, CodeBrix.SkiaSvg, CodeBrix.PdfRasterizer, CodeBrix.PdfDocCreate and its Html2Pdf add-on |

## Blueprints

[BLUEPRINTS.md](BLUEPRINTS.md) is a set of how-tos for building your own
CodeBrix.Platform application, mined from the applications in this repository.
Each blueprint says when you want the thing, what shape it takes in an MVVM
application, the code that does it, and the files the code comes from. The
shape it teaches throughout is the one the platform is designed around: logic
lives in view model classes derived from `SimpleViewModel`, with state exposed
as bound properties and behavior as `SimpleCommand` commands whose enabled
state is refreshed by `[AffectsCommands]`; code-behind constructs or resolves
the view model, sets `DataContext` and wires platform plumbing through an
interface; capabilities only a view or a head can supply reach the view model
through a bridge interface it consumes; services sit behind interfaces,
are registered with `SimpleServiceResolver` at startup, and are resolved in the
view model; and heavy work runs off the UI thread and marshals its results back
through `InvokeOnMainThread`.

## Building and testing

The plain .NET 10 SDK is the only prerequisite for everything here. There are
no workloads to install, no native toolchain, and in particular no LilyPond
installation: the engraver arrives as a library with the build, as does every
other asset the application needs.

Open the application's solution in its own folder, `Fresco.Brix/Fresco.Brix.slnx`.
It is the same solution on Linux, macOS and Windows, and it contains the shared
UI project, the application core, all six heads, the libraries and the test
projects. The WinWpfSkia head builds only on Windows; every other project
builds on any of the three operating systems. The programs under `tools/` are
deliberately outside the solution: they ship nothing, and they are built and run
on demand.

A head accepts file paths on the command line. If the application is already
running, those files open as tabs in the running window and the second process
exits.

`Fresco.Brix/global.json` selects the Microsoft.Testing.Platform runner for the
tree, and the test projects are self-executing Microsoft.Testing.Platform
executables. A plain `dotnet test` against such an executable can report that
zero tests ran, so run the built test binaries directly or use the form of
`dotnet test` that works; the [Fresco.Brix README](Fresco.Brix/README.md) gives
the exact commands.

Anything else an application needs is stated in its own README.

## Third-party notices

Each application folder carries its own `THIRD-PARTY-NOTICES.txt`, which is the
authoritative record of the third-party code, data and assets that application
adapts, bundles or uses at run time, together with their licenses and upstream
locations:
[Fresco.Brix/THIRD-PARTY-NOTICES.txt](Fresco.Brix/THIRD-PARTY-NOTICES.txt) is
that application's attribution ledger. The root
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt) says the same and points at
it. Packages carry their own notices in their own packages, so those are not
reproduced here.

## License

Everything in this repository is licensed under the GNU General Public License,
version 3, and the full text is in [LICENSE](LICENSE). Fresco.Brix's own
sources are GPL-3.0-or-later; the application as conveyed is GPL-3.0-only,
because the CodeBrix.LilyPort library it links is conveyed on those terms and
an aggregate takes the narrower of the terms it combines.

Copyright (c) 2026 Jeremy Ellis and contributors
