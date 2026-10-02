# Fresco.Brix PlayTests

These xUnit v3 tests launch the real shared Fresco application in the CodeBrix
PlayTest head. They use real XAML controls, pointer and keyboard input, the
AdvancedTextEdit document pipeline, and the bundled LilyPort engraving engine.
Assertions use SilverAssertions and PlayTest's retrying UI assertions.

## Run

From `Fresco.Brix` with .NET 10:

```sh
dotnet restore tests/Fresco.Brix.PlayTests/Fresco.Brix.PlayTests.csproj
dotnet build tests/Fresco.Brix.PlayTests/Fresco.Brix.PlayTests.csproj
dotnet test --project tests/Fresco.Brix.PlayTests/Fresco.Brix.PlayTests.csproj --no-build
dotnet test --project tests/Fresco.Brix.PlayTests/Fresco.Brix.PlayTests.csproj --no-build --nonheadless
```

The preview is a view of the same rendered surface used headlessly. Physical
keyboard/mouse input does not control the tests. The default headed action delay
is 250 ms; `CODEBRIX_PLAYTEST_SLOWMO` can override it. The PlayTest head uses
Control-based keyboard shortcuts on all operating systems.

```sh
dotnet test --project tests/Fresco.Brix.PlayTests/Fresco.Brix.PlayTests.csproj --no-build \
  --nonheadless --theme=dark --orientation=portrait

# Create this directory first; it must be empty.
dotnet test --project tests/Fresco.Brix.PlayTests/Fresco.Brix.PlayTests.csproj --no-build \
  --screenshotfolder="$HOME/Temp/Fresco PlayTest screenshots"

# Run one area while developing.
dotnet test --project tests/Fresco.Brix.PlayTests/Fresco.Brix.PlayTests.csproj --no-build \
  --filter-class '*EditingTests'
```

Theme/orientation CLI values override environment and project defaults. An
individual test's orientation attribute wins for that test. The screenshot
recorder creates PNGs and `screenshot-index.json`, including test identity,
theory arguments, local timestamps, actions and available PDB source locations.
It captures the virtual surface without needing macOS Screen Recording access.
The theme switch controls the application theme; the editor's separate syntax
color scheme remains governed by Fresco's Fonts & Colors preferences.

The complete set of PlayTest command-line switches, environment variables and
project properties is documented in the PlayTest package's `AGENT-README.txt`,
which ships inside the `CodeBrix.Platform.PlayTest.ApacheLicenseForever` package
and lives at
[`src/Platform.UI.Runtime.Skia.PlayTest/AGENT-README.txt`](https://github.com/ellisnet/CodeBrix.Platform/blob/main/src/Platform.UI.Runtime.Skia.PlayTest/AGENT-README.txt)
in CodeBrix.Platform.

## Package references

AdvancedTextEdit supplies a standard UI automation Value provider and focus
peer in its own add-in. PlayTest has no dependency on that add-in, CommandBar,
SilverAssertions, or Fresco. The **test application** references the add-ins it
uses, just as a normal head does.

## Coverage

| Area | Exercised behavior |
| --- | --- |
| Documents | New/open/save/save-as, cancelled pickers, dirty close answers, recent files, tabs and existing-path reuse |
| Editor | Routed typing including Unicode/newlines, whole-value fill, undo/redo, selection, caret navigation, clipboard, context menu, completion, folding, bookmarks and display toggles |
| Search | Next/previous, literal and regex replacement, case sensitivity, invalid expressions, no matches and returning focus |
| Layout | Nested TriPaneView panes, real divider drags, saved sizes, hide/restore, split editors sharing a document and portrait layout |
| Dock panels | Music, documentation, layout control, quick insert, characters, snippets, documents, outline and MIDI |
| Preferences | All pages, apply/cancel/reset and toolbar presentation settings |
| Score wizard | Headers and live preview, accepting/cancelling, remembered entries, clear, part selection and page navigation |
| Tools | Nested rhythm/rest/quick-remove menus, whitespace removal, Quick Insert articulations and undo |
| Sessions | Name validation, creation, cancellation and management dialog |
| Engraving | Bundled engine, successful/failed jobs, rendered pages, zoom, magnifier, SVG export and clear |
| Help | About/Credits/Version pages and built-in user guide |

## Isolation and limits

One serialized application fixture owns the UI dispatcher and engine. Tests use
an isolated settings database and unique files below the output directory's
`TestResults/PlayTestData`. Remote-instance handling is disabled before launch.
The fixture resets documents, split editors, panels, changed settings, picker
queues, clipboard and orientation between tests. Failure screenshots remain
under that run's `failures` directory. The suite does not use your normal Fresco
settings, documents, or system clipboard.

Native macOS system menus, actual process termination, external browser/helper
launches, OS file-picker dialogs and physical audio/MIDI hardware are separate
desktop integration concerns. This head tests the in-window shared menus.
“Insert from File” is currently unwired/disabled in Fresco; a test verifies that
disabled state rather than claiming an insertion workflow exists.

General control-contract regressions also live in CodeBrix.Platform's
`samples/CodeBrixPlatform/PlayTestDemo`. Its interactive “Try desktop controls”
screen demonstrates the same editor, toolbar, nested-menu and overflow APIs.

## Regressions covered by this suite

The tests guard Fresco's case-insensitive literal replacement, previous-match
selection and duplicate search-key handling. Quick Insert uses its intended
Neutral default when no direction setting exists. Its icon buttons/direction
chooser and the score wizard's header fields expose accessible names or labels,
so tests can locate them by what users see. The score wizard's header preview
uses the embedded music icons and follows the resolved theme, rather than
depending on musical characters in the desktop UI font.

Framework regressions cover AdvancedTextEdit focus, undoable value replacement
and protected sections, empty completion lists allowing Enter, and element-based
tab headers exposing their text. Those generic tests live in PlayTestDemo.
