================================================================================
CodeBrix.LilyPort -- tools/Lily.Shell/
================================================================================

Lily.Shell is the interactive shell for the port: a CodeBrix.Platform application
whose window is a terminal, hosting the LilyPond engine IN PROCESS. Parse a file,
engrave it, talk to the Scheme layer, render a manual -- without a twenty-second
engine start-up between each one.

It is a REPO TOOL. Nothing here ships: VERIFIED 2026-08-19, the one packable
project in this repository (src/CodeBrix.LilyPort) references nothing under tools/
at all, and no packaging step reaches this directory. Decision D14 settled what
this replaces -- there is no public `lilypond'-style console CLI; this is the
user-facing surface instead.

--------------------------------------------------------------------------------
RUNNING IT
--------------------------------------------------------------------------------

    cd tools/Lily.Shell
    dotnet run --project src/Lily.Shell.LinuxX11 -c Release

Six heads, all from one solution and one shared UI project:

    Lily.Shell.LinuxX11          net10.0            the one used day to day here
    Lily.Shell.LinuxWayland      net10.0
    Lily.Shell.LinuxFrameBuffer  net10.0            no desktop needed
    Lily.Shell.MacOS             net10.0
    Lily.Shell.Win32Skia         net10.0
    Lily.Shell.WinWpfSkia        net10.0-windows

⚠ THE ENGINE LOADS IN THE BACKGROUND AND THE FIRST LOAD IS THE SLOW ONE: about
20 s warm, and roughly 67 s the first time after a build, which is JIT rather than
work. The window title is the progress bar -- one more dot every five seconds --
and the banner says so. Commands that need the engine wait for it and say they are
waiting; commands that do not are usable immediately.

--------------------------------------------------------------------------------
COMMANDS
--------------------------------------------------------------------------------

    help       Lists commands, or shows usage for one command.
    clear      Clears the terminal screen.
    version    Shows the ported LilyPond version and engine state.
    usage      Prints the command-line usage message.
    parse      Parses a .ly file and shows diagnostics.
    engrave    Engraves a .ly file to SVG (and .midi when the score has \midi).
    demo       Engraves the first-light demo (quarter-note c'4) to SVG.
    include    Lists or adds parser include directories.
    scheme     Enters the LilyScheme REPL (the engine's Scheme sandbox).
    docs       Renders one of the port's nine manuals to HTML and PDF.
    exit       Closes Lily.Shell.

`usage' prints the engine's own UsageText.Text -- the SAME string the ly:usage
Scheme binding prints. One string, two callers, deliberately.

`engrave -o' means what lilypond's -o means: a NAME, not merely a directory
(BatchRunner.SplitOutputName, ported from main.cc:729-761). It lists EVERY page it
wrote plus any MIDI, because a multi-page book reported by its first page is a
book three quarters lost.

--------------------------------------------------------------------------------
THE `docs' COMMAND -- PHASE 5's CAPABILITY, IN THE SHELL
--------------------------------------------------------------------------------

    lily> docs                        list the nine manuals, and say whether the
                                      port's nineteen documentation files have
                                      been generated yet this session
    lily> docs contributor            both formats, into /tmp/lily-shell-docs/
    lily> docs notation --html        one format
    lily> docs learning -o ~/manuals  somewhere else
    lily> docs notation --no-snippets the control run: no engraver, seconds

Decision D52 ruled tools/Lily.Docs a repo tool that ships nothing AND a `docs'
command here, so the manuals are reachable without building a separate tool. This
command does not reimplement anything: DocsCommand and DocsRunner (in
Lily.Shell.Core) drive Lily.Docs in this process through
LilyPortHost.RunEngineWorkAsync. The full description of what is rendered, what
the baselines mean and where the manuals come from is in
tools/Lily.Docs/README.txt.

Four things about it are load-bearing rather than incidental:

  * GENERATION IS CACHED FOR THE SESSION, because it works once per PROCESS. The
    second run of ly/generate-documentation.ly in a process writes NOTHING,
    reports all nineteen files missing, and does not throw -- and a shell is
    exactly where two `docs' commands in one process is the normal case. An
    INCOMPLETE generation is deliberately not cached, and the retry it allows
    fails identically; restarting the shell is the fix. The alternative is a
    manual rendered out of a half-written directory, successfully, with its
    appendices simply absent.
  * ASKING FOR BOTH FORMATS IS ONE RENDER, not two. Rendering HTML and then PDF
    runs the Texinfo source twice and engraves the music once per format -- two
    and a half thousand engravings and five minutes, for the notation manual.
  * THE COUNTS REPORTED ARE ASKED AND FAILED, never "it finished". The Texinfo
    package CATCHES a snippet renderer that throws and prints the snippet's
    source instead, so a render that completed is entirely compatible with every
    engraving in it having failed.
  * THERE IS NO --baseline HERE, and there is a test asserting there is not.
    Lily.Docs can freeze a manual's expected-warnings baseline from a run; the
    shell only renders. A baseline is frozen from a run that was READ, in the
    repository, by the tool that owns the file.

⚠ THE EIGHT CORPUS MANUALS NEED THE REPOSITORY; `internals' does not. The corpus
mirror is found by walking up from the running assembly to CodeBrix.LilyPort.slnx,
while the vendored GFDL assets travel beside the assembly -- so a copy of this app
moved out of its build tree still renders `internals' and answers any other manual
with "could not find CodeBrix.LilyPort.slnx above ...".

⚠ AND THIS IS WHY Lily.Shell CARRIES THE Texinfo -> Html2Pdf -> SkiaSharp CHAIN,
which decision D52 refuses for CodeBrix.LilyPort itself. Lily.Shell ships nothing,
and MEASURED 2026-08-19: its heads already carried 561 MB of the identical
SkiaSharp 4.151.0 native assets through CodeBrix.Platform, so the reference cost
45 MB of managed assemblies plus two font packages rather than a new native
payload, and Roboto/Roboto Mono resolved at the versions already pinned here.

--------------------------------------------------------------------------------
LAYOUT
--------------------------------------------------------------------------------

    src/libs/Lily.Shell.Kernel/        VT input tokenizer, line editor, command
                                       registry, sub-interpreter stack, the
                                       ShellSession itself. No UI, no engine.
    src/libs/Lily.Shell.TerminalView/  an EMPTY PASS-THROUGH project. The terminal
                                       control graduated into the platform family
                                       as CodeBrix.Platform.TerminalView; this
                                       project only flows that package to the app
                                       and the tests.
    src/Lily.Shell.UI/                 the shared XAML (a .shproj): App + MainPage.
    src/Lily.Shell.Core/               LilyPortHost, the commands, the view model,
                                       window chrome. Engine by ProjectReference
                                       to all four LilyPort projects (the facade
                                       packs them PrivateAssets="all", so an
                                       in-repo consumer must name each one).
    src/Lily.Shell.<head>/             one Program.cs per platform head.
    tests/Lily.Shell.Core.Tests/       the `docs' command surface and the
                                       once-per-process generation contract.
    tests/libs/*.Tests/                Kernel and TerminalView.

The Emmentaler faces are copied to <appdir>/fonts/otf from Core, because the
engine's font layer probes there.

--------------------------------------------------------------------------------
TESTS -- THE MTP DIALECT, AND WHY PLAIN `dotnet test' IS REFUSED
--------------------------------------------------------------------------------

    dotnet test --solution Lily.Shell.slnx -c Release      86 tests

    41  Lily.Shell.Kernel.Tests
    28  Lily.Shell.TerminalView.Tests
    17  Lily.Shell.Core.Tests

This solution is the Microsoft.Testing.Platform dialect. xunit.v3 4.0 brings MTP
2.3.3, which removed the legacy VSTest-mode bridge, so on the .NET 10 SDK a plain
`dotnet test' is refused outright. The fix is the global.json BESIDE THIS FILE:

    { "test": { "runner": "Microsoft.Testing.Platform" } }

and the new CLI syntax -- `--solution X.slnx' or `--project x.csproj'; positional
arguments no longer work, and `--logger trx' is ignored here (two MTP0001
warnings). ⚠ The main CodeBrix.LilyPort solution and tools/Lily.Docs are the OTHER
dialect, VSTest, where `dotnet test <solution>' works as written. Both dialects
live in this repository on purpose; do not "fix" one into the other.

--------------------------------------------------------------------------------
SHARP EDGES
--------------------------------------------------------------------------------

  * THE KERNEL EMITS EXPLICIT CRLF, so the terminal control must have
    ConvertEol = false or every line double-spaces.
  * CodeBrix.Terminal.Engine.Buffer COLLIDES WITH System.Buffer -- alias it.
  * SKIA HEADS DELIVER SHIFTED DIGIT-ROW SYMBOLS UNDER KEYSYMS THE VirtualKey
    PATH NEVER SEES (parentheses vanished until this was found). The control
    reads the internal KeyRoutedEventArgs.UnicodeKey by reflection, falls back to
    a US-QWERTY encoder, and tracks modifiers itself; there is no
    CharacterReceived and no IME on Skia heads.
  * ASYNC MESSAGES GO THROUGH ShellSession.WriteOutOfBand, never straight to the
    output: idle means message plus a prompt repaint, busy means message only.
    Writing directly is what produced the doubled prompt when the engine-ready
    announcement raced an awaiting command.
  * A RUNNING SCHEME EVALUATION CANNOT BE INTERRUPTED. Ctrl+C is honoured between
    engine operations, not inside one -- so `docs' says outright that it has
    stopped WAITING and the render is still running.

  ⚠ X11 AUTOMATION: FINDING THIS WINDOW IS HARDER THAN IT LOOKS, AND GETTING IT
    WRONG TYPES INTO SOMEBODY ELSE'S APPLICATION. Two ways it has actually gone
    wrong:
      (a) `xdotool search --name "Lily.Shell"' -- the dot is regex-any, and it
          matched "Lily Shell - CodeBrix Develop"; the keystrokes went to the IDE.
      (b) `xdotool search --pid P --name .' -- the criteria are ORed, not ANDed,
          so it matches every named window on the display; `tail -1' then returned
          an unrelated terminal.
    ⚠ And the obvious fix does not work either: MEASURED, this app's X11 window
    carries NO _NET_WM_PID, so `search --all --pid' finds nothing and
    getwindowpid refuses. What works is the TITLE, anchored and dot-escaped
    (^Lily\.Shell), PLUS UNIQUENESS -- no such window before launch, exactly one
    after -- with getwindowname re-checked before every keystroke and NOTHING
    typed if the check fails.

--------------------------------------------------------------------------------
THE STANDING EXPECTATION
--------------------------------------------------------------------------------

Lily.Shell IS KEPT CURRENT AS THE ENGINE GROWS (Jeremy, 2026-08-07; the boards
carry it as rule 14). A session that lands user-visible engine capability reflects
it here in the same session, even when the answer is a recorded "nothing owed" --
so this finishes as the full shell, sandbox and REPL for LilyPort with no catch-up
project at the end. `engrave' reaching the real batch pipeline and `docs' reaching
the manuals are both that rule being paid.

MIDI PLAYBACK IS OUT OF SCOPE -- not just out of Lily.Shell's, but out of
LilyPort's entirely (decision D27). The port generates MIDI files and compares
them; playing them is a later project in the Fresco.Brix direction.

--------------------------------------------------------------------------------
LICENSING
--------------------------------------------------------------------------------

GPL-3, like the rest of this repository; every file carries the header. Lily.Shell
incorporates no third-party source of its own -- what it consumes arrives as
packages, and THIRD-PARTY-NOTICES.txt §12.1 records the documentation chain that
the `docs' command brought in.

================================================================================
