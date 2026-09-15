# CodeBrix.Samples.Gpl3 Blueprints

This file is a set of how-tos for building your own CodeBrix.Platform
application. Each blueprint says when you want the thing, what shape it takes
in an MVVM application, the code that does it, and the files in this repository
the code comes from, so you can open the real thing and read around it. The
material is mined from the one application in this repository, Fresco.Brix, a
music-notation editor and engraving environment that hosts a long-running
engraving engine in process and uses the platform's editor, settings, PDF,
audio and SVG libraries together at full load.

The shape every blueprint is written in is this. Logic lives in view model
classes derived from `SimpleViewModel`: state is exposed as bound properties,
behavior as `SimpleCommand` commands whose enabled state is refreshed by
`[AffectsCommands]`, and work that touches bound state from another thread
comes back through `InvokeOnMainThread` or through a marshaling delegate the
view supplies. Code-behind is thin: it constructs or resolves the view model,
sets `DataContext`, and wires platform plumbing to the view model through an
interface. Platform capabilities that only a view can supply (a file dialog,
the focused editor, a fullscreen switch) reach the view model through a bridge
interface the page implements, and a head that cannot supply one leaves it null
rather than failing. Services sit behind interfaces,
are registered with `SimpleServiceResolver` at startup, and are resolved in the
view model. Heavy work runs off the UI thread and marshals its results back.
Every code block names its source on its first line: a `From` block is verbatim
from the file it names (with `// ...` where something was trimmed), and an
`Adapted` block was recast into the shape above, with the prose saying what
moved and why.

Packages are named here by library or add-in ("CodeBrix.Platform", "the
CodeBrix.Platform.AdvancedTextEdit add-in", "CodeBrix.LilyPort"), never by
package ID or version. CodeBrix package IDs carry a license suffix that says
which license the package is offered under; the project's csproj is the source
of truth for the exact package and version, and the code blocks below show
`Include="..."` where an ID would be.

## Contents

- Application structure and startup
  - [Run one shared XAML UI on six platform heads from one head program](#run-one-shared-xaml-ui-on-six-platform-heads-from-one-head-program)
  - [Bootstrap an application with SimpleServiceResolver and a default font](#bootstrap-an-application-with-simpleserviceresolver-and-a-default-font)
  - [Configure logging before the host is built](#configure-logging-before-the-host-is-built)
  - [Hand the files of a second launch to the running window and exit](#hand-the-files-of-a-second-launch-to-the-running-window-and-exit)
  - [Ship compiled gettext catalogs and look strings up by upstream msgid](#ship-compiled-gettext-catalogs-and-look-strings-up-by-upstream-msgid)
- View models, commands and threading
  - [Show a long background load in the window title instead of a splash screen](#show-a-long-background-load-in-the-window-title-instead-of-a-splash-screen)
  - [Marshal work onto the UI thread with one delegate handed to services](#marshal-work-onto-the-ui-thread-with-one-delegate-handed-to-services)
  - [Host one long-running engine per process and load it in the background](#host-one-long-running-engine-per-process-and-load-it-in-the-background)
  - [Marshal live engine output onto the thread that started the job](#marshal-live-engine-output-onto-the-thread-that-started-the-job)
  - [Run one job at a time against a process-global engine](#run-one-job-at-a-time-against-a-process-global-engine)
  - [Debounce automatic background work behind a timer with eligibility gates](#debounce-automatic-background-work-behind-a-timer-with-eligibility-gates)
  - [Cancel work at the boundaries a library can honor](#cancel-work-at-the-boundaries-a-library-can-honor)
- Bridging platform services into the view model
  - [Own window state in a view model and reach the view through one interface](#own-window-state-in-a-view-model-and-reach-the-view-through-one-interface)
- Views, XAML and custom controls
  - [Set a page DataContext in XAML and give the view model a XamlRoot](#set-a-page-datacontext-in-xaml-and-give-the-view-model-a-xamlroot)
  - [Build a dock shell with drawn splitters and remember its arrangement](#build-a-dock-shell-with-drawn-splitters-and-remember-its-arrangement)
  - [Nest two TriPaneView controls to put four regions around an editor](#nest-two-tripaneview-controls-to-put-four-regions-around-an-editor)
  - [Show and hide a TriPaneView pane by minimizing and restoring it](#show-and-hide-a-tripaneview-pane-by-minimizing-and-restoring-it)
  - [Register window-level shortcuts that survive a focused text editor](#register-window-level-shortcuts-that-survive-a-focused-text-editor)
  - [Build menus and toolbars in code from command objects](#build-menus-and-toolbars-in-code-from-command-objects)
  - [Add the CommandBar add-in and build a tray of two toolbars from command objects](#add-the-commandbar-add-in-and-build-a-tray-of-two-toolbars-from-command-objects)
  - [Give a panel its own toolbar with the CommandBar add-in](#give-a-panel-its-own-toolbar-with-the-commandbar-add-in)
  - [Show and size a modal dialog on the Skia heads](#show-and-size-a-modal-dialog-on-the-skia-heads)
  - [Render embedded SVG icons through one renderer and pick the set by theme](#render-embedded-svg-icons-through-one-renderer-and-pick-the-set-by-theme)
- Graphics and rendering
  - [Draw a paged document view that scrolls by translating a viewport-sized surface](#draw-a-paged-document-view-that-scrolls-by-translating-a-viewport-sized-surface)
  - [Parse SVG once into a scene graph and use its anchors as hit-test geometry](#parse-svg-once-into-a-scene-graph-and-use-its-anchors-as-hit-test-geometry)
  - [Move the caret from a click in a rendered document and back again](#move-the-caret-from-a-click-in-a-rendered-document-and-back-again)
- Media, camera and vision
  - [Play a MIDI file and render one to WAV with the audio library](#play-a-midi-file-and-render-one-to-wav-with-the-audio-library)
- Documents, data and web APIs
  - [Give each document a private scratch directory cleaned up at process exit](#give-each-document-a-private-scratch-directory-cleaned-up-at-process-exit)
  - [Turn tool diagnostics into clickable source locations that survive edits](#turn-tool-diagnostics-into-clickable-source-locations-that-survive-edits)
  - [Show PDF pages inside the application with PdfRasterizer](#show-pdf-pages-inside-the-application-with-pdfrasterizer)
  - [Write a vector PDF with PdfDocCreate and the Html2Pdf add-on](#write-a-vector-pdf-with-pdfdoccreate-and-the-html2pdf-add-on)
  - [Convert a file through a library in process and apply the result as one undo step](#convert-a-file-through-a-library-in-process-and-apply-the-result-as-one-undo-step)
- Settings and persistence
  - [Put the AppSettings add-in behind one facade](#put-the-appsettings-add-in-behind-one-facade)
  - [Persist preference pages and named sessions through that one store](#persist-preference-pages-and-named-sessions-through-that-one-store)
- Text editing
  - [Bridge a platform-free document model onto the editor text document](#bridge-a-platform-free-document-model-onto-the-editor-text-document)
  - [Attach a language-aware highlighter to the text editor add-in](#attach-a-language-aware-highlighter-to-the-text-editor-add-in)
  - [Fold match pairs and auto-indent from the same tokenization](#fold-match-pairs-and-auto-indent-from-the-same-tokenization)
  - [Show two editor views over one document](#show-two-editor-views-over-one-document)
  - [Offer context-aware autocompletion in the editor](#offer-context-aware-autocompletion-in-the-editor)
- Testing
  - [Set up test projects on the Microsoft Testing Platform and check a port against recorded answers](#set-up-test-projects-on-the-microsoft-testing-platform-and-check-a-port-against-recorded-answers)
  - [Assert a toolbar's contents and a shell's pane arithmetic in host-free tests](#assert-a-toolbars-contents-and-a-shells-pane-arithmetic-in-host-free-tests)
- Project layout, packaging and native assets
  - [Put every package in a Core library and one runtime package in each head](#put-every-package-in-a-core-library-and-one-runtime-package-in-each-head)
  - [Give a library that references CodeBrix Platform its own RootNamespace](#give-a-library-that-references-codebrix-platform-its-own-rootnamespace)
  - [Keep a ported library completely free of the UI framework](#keep-a-ported-library-completely-free-of-the-ui-framework)
  - [Ship data assets beside the program so their licenses travel with them](#ship-data-assets-beside-the-program-so-their-licenses-travel-with-them)

## Application structure and startup

### Run one shared XAML UI on six platform heads from one head program

**When you want this.** You are starting a CodeBrix.Platform application and
want every head to be a file you write once and never touch again.

**The MVVM shape.** The head owns nothing but the host. It initializes logging,
parses the command line into static properties on `App`, builds the host with
one `Use...()` call for its windowing back end, and runs. All state lives in
the view model that `MainPage.xaml` instantiates as its `DataContext`.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.LinuxX11/Program.cs
internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        App.InitializeLogging();

        //FD5: a second launch hands its files to the window that is already up
        //and stops here — BEFORE this process reads the settings store for
        //itself, so two processes never share one.
        if (RemoteInstance.TryHandOff(args)) { return; }

        App.CommandLine = CommandLineArguments.Parse(args);
        App.CommandLinePaths = App.CommandLine.Files;

        var host = CodeBrixPlatformHostBuilder.Create()
            .App(() => new App())
            .UseLinuxX11()
            .UseDirectSkiaCanvasMode() //Experimental - should be safe to leave enabled
            .Build();

        host.Run();
    }
}
```

The other five heads are the same file with `UseLinuxWayland()`,
`UseLinuxFrameBuffer()`, `UseMacOS()`, `UseWindowsWin32()` or
`UseWindowsWpf()` in place of `UseLinuxX11()`. The WinWpfSkia head is the only
one that touches its host object after `Build()`:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.WinWpfSkia/Program.cs
        if (host is WpfHost wpfHost)
        {
            wpfHost.RenderSurfaceType = RenderSurfaceType.Software;
        }
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.LinuxX11/Program.cs`, and the same file in
`Fresco.Brix.LinuxWayland`, `Fresco.Brix.LinuxFrameBuffer`,
`Fresco.Brix.MacOS`, `Fresco.Brix.Win32Skia` and `Fresco.Brix.WinWpfSkia`.

**Sharp edges.**

- The single-instance check runs before the host is built and before the
  settings store is opened, so two processes never hold the store at once.
- `App.InitializeLogging()` runs before anything else, because the host itself
  logs while it starts.
- The command line is parsed into statics on `App` rather than passed down: the
  page reads it once when the view model starts.

### Bootstrap an application with SimpleServiceResolver and a default font

**When you want this.** Every CodeBrix.Platform application needs this file. It
registers the services the view models resolve, turns design mode off, and
names the font the whole interface draws in.

**The MVVM shape.** `App.xaml.cs` builds the container and nothing else; view
models call `GetService<T>()` for what they need. A service registered as a
singleton here is process-wide by intent, not by convenience, and the comment
that registers it says which.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/App.xaml.cs
public App()
{
    //Set Roboto as the default font for all text in the application
    global::CodeBrix.Platform.UI.FeatureConfiguration.Font.DefaultTextFontFamily =
        "ms-appx:///CodeBrix.Platform.Fonts.Roboto/Fonts/Roboto.ttf";

    //Fonts consulted for characters the default font has no glyph for
    global::CodeBrix.Platform.UI.FeatureConfiguration.Font.FallbackFontFamilies =
    [
        "ms-appx:///CodeBrix.Platform.Fonts.Roboto/Fonts/NotoSansArmenian.ttf",
        "ms-appx:///CodeBrix.Platform.Fonts.Roboto/Fonts/NotoSansGeorgian.ttf",
    ];

    SimpleServiceResolver.CreateInstance(HostHelper.GetHost(), services =>
    {
        //Register the app's services here
        services.AddSingleton<Services.SettingsStore>();
        services.AddSingleton<Services.RecentFiles>();

        //One engine per process. It is a singleton because the engine's
        //state is process-global, not because one is convenient.
        services.AddSingleton<Engrave.LilyPortEngine>();
    });
    SimpleViewModel.SetIsDesignMode(false);

    InitializeComponent();
}
```

The resolver builds its container from a host-builder provider, which is one
small class in the Core library:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Helpers/HostHelper.cs
public static class HostHelper
{
    private sealed class HostBuilderProvider : IHostBuilderProvider
    {
        public IHostBuilder CreateDefaultBuilder() => Host.CreateDefaultBuilder();
        public IHostBuilder CreateDefaultBuilder(string[] args) => Host.CreateDefaultBuilder(args);
    }

    private static readonly HostBuilderProvider Provider = new();

    /// <summary>Gets the shared host-builder provider.</summary>
    public static IHostBuilderProvider GetHost() => Provider;
}
```

The view model's end of the same contract is the design-mode guard and the
`GetService<T>()` calls:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/ViewModels/MainViewModel.cs
public class MainViewModel : SimpleViewModel
{
    /// <summary>Creates the window's state.</summary>
    public MainViewModel()
    {
        if (IsDesignMode(true)) { return; } //Leave as the first line of constructor
        // ...
        _settings = GetService<SettingsStore>();
        // ...
    }
}
```

The fonts themselves are declared as resources in `App.xaml`:

```xml
<!-- From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/App.xaml -->
      <!-- Roboto font - reference the .ttf file directly (the Fonts.xaml
           merge does not work on Skia targets) -->
      <m:FontFamily x:Key="RobotoFont">ms-appx:///CodeBrix.Platform.Fonts.Roboto/Fonts/Roboto.ttf</m:FontFamily>
      <!-- The editor monospace font (FD4) -->
      <m:FontFamily x:Key="RobotoMonoFont">ms-appx:///CodeBrix.Platform.Fonts.RobotoMono/Fonts/RobotoMono.ttf</m:FontFamily>
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.UI/App.xaml.cs`,
`Fresco.Brix/src/Fresco.Brix.UI/App.xaml`,
`Fresco.Brix/src/Fresco.Brix.Core/Helpers/HostHelper.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/ViewModels/MainViewModel.cs`.

**Sharp edges.**

- `SetIsDesignMode(false)` must be called or view models take their design-time
  path at run time.
- `IsDesignMode(true)` stays the first line of the view model's constructor, so
  a designer that constructs the type never resolves a service or opens a
  store.
- Declare font resources by direct `.ttf` path. Merging a font package's own
  `Fonts.xaml` does not work on the Skia heads, and the comment in `App.xaml`
  says so.
- The fallback font list is what a character outside the default face's
  coverage is drawn from. There is no system-font fallback behind it, which is
  deliberate: a missing glyph draws a box you can see rather than quietly
  picking up whatever the machine has.

### Configure logging before the host is built

**When you want this.** You want console diagnostics in Debug builds without
the framework's own informational lines drowning yours, and nothing at all in
Release.

**The MVVM shape.** A static method on `App`, called from every head's `Main`
before the host exists. Nothing else in the application configures logging.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/App.xaml.cs
// Called from each head's Program.Main BEFORE building the host.
public static void InitializeLogging()
{
#if DEBUG
    var factory = LoggerFactory.Create(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(LogLevel.Information);
        builder.AddFilter("CodeBrix.Platform", LogLevel.Warning);
        builder.AddFilter("Windows", LogLevel.Warning);
        builder.AddFilter("Microsoft", LogLevel.Warning);
    });

    global::CodeBrix.Platform.Extensions.LogExtensionPoint.AmbientLoggerFactory = factory;
    global::CodeBrix.Platform.UI.Adapter.Microsoft.Extensions.Logging.LoggingAdapter.Initialize();
#endif
}
```

An add-in can write to the console on its own, ahead of your filters. The
settings facade turns that off in a static constructor that runs before any
store is opened:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Services/SettingsStore.cs
static SettingsStore()
{
    //The add-in's own logging service writes to the console BY DEFAULT, and
    //that write bypasses the logging the application configures. [...]
    //Forwarding to the ambient logger is left ON: the application's
    //own filters then decide, which is the point of having them. This runs
    //before ANY store is opened, including the single-instance check that
    //reads one setting before the application has built its container.
    AppSettingLoggingService.ConsoleOutput = false;
}
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.UI/App.xaml.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Services/SettingsStore.cs`.

**Sharp edges.**

- An add-in with its own console output writes before your filters exist. Turn
  it off in a static constructor on whatever type opens the add-in, so the
  switch is thrown before the first use rather than at some point during
  startup.

### Hand the files of a second launch to the running window and exit

**When you want this.** Opening a file from the file manager should add a tab
to the window that is already open rather than starting a second copy.

**The MVVM shape.** A static service in the Core library with two halves. The
head calls `TryHandOff` before the host is built, and if it returns true the
process ends. The page implements a small command-target interface and calls
`Setup` once the window can act on what it is told, passing its UI-thread
marshaler. Every command the listener receives is forwarded to the view model
in one line.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Services/RemoteInstance.cs
public static bool TryHandOff(IReadOnlyList<string> arguments)
{
    CommandLineArguments parsed = CommandLineArguments.Parse(arguments);
    if (parsed.New) { return false; }

    if (!ReadEnabledSetting()) { return false; }

    RemoteConnection connection = Connect();
    if (connection == null) { return false; }

    try
    {
        connection.CommandLine(parsed);
        connection.Close();
        return true;
    }
    catch (IOException)
    {
        //The running instance went away between connecting and writing;
        //carry on and start normally.
        connection.Dispose();
        return false;
    }
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Services/RemoteProtocol.cs
public interface IRemoteCommandTarget
{
    void OpenPath(string path, string encoding);
    void SetCurrent(string path);
    void SetCursor(int line, int column);
    void ActivateWindow();
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs
public void OpenPath(string path, string encoding)
    => _ = ViewModel?.OpenPathAsync(path, encoding);

public void SetCurrent(string path)
{
    //[...] the same "it is only current if it is open" rule is said here by
    //looking it up.
    EditorDocument document = ViewModel?.Documents.FindDocument(path);
    if (document != null) { ViewModel.Documents.CurrentDocument = document; }
}

public void SetCursor(int line, int column)
    => _viewManager?.ActiveView?.GoTo(line, column);

public void ActivateWindow()
{
    App.Shell?.Activate();
    _viewManager?.ActiveView?.FocusEditor();
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs
//FD5: start listening for a later launch, now that this window can act
//on what it is told.
RemoteInstance.Setup(viewModel.Settings, this, OnUiThread);
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Services/RemoteInstance.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Services/RemoteProtocol.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Services/CommandLineArguments.cs`,
`Fresco.Brix/src/Fresco.Brix.LinuxX11/Program.cs`,
`Fresco.Brix/tests/Fresco.Brix.Core.Tests/RemoteInstanceTests.cs`.

**Sharp edges.**

- The transport is a named pipe on Windows and a unix-domain socket everywhere
  else, behind one interface.
- The endpoint name is built from the application name, the user and the
  display variable, with numbered fallbacks, so two desktops or two users do
  not collide.
- Contact every candidate name before claiming it. A name nobody answers on is
  stale, is removed, and is taken over. Without this a crashed process leaves a
  socket that blocks every later launch.
- Close the listener on quit so the socket goes with the process that made it.
- The reader runs on a worker thread; every command reaches the window through
  the supplied UI-thread delegate.
- The one setting the check reads is read by opening the store and closing it
  again, before the application has taken the store for itself. A store that
  cannot be opened must not stop the application from starting.

### Ship compiled gettext catalogs and look strings up by upstream msgid

**When you want this.** You are shipping translations produced elsewhere and
want a missing translation to fall back to English rather than to something
wrong.

**The MVVM shape.** A static `I18n` facade every user-visible string passes
through, a catalog loader that reads a compiled `.mo` file from beside the
program, a plural-form evaluator built from the catalog's own header, and one
guard where every lookup lands. The view model installs the language as the
first thing it does, before any command, panel or dialog has built a caption.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Services/LanguageSetup.cs
public static ITranslationCatalog CatalogFor(string language)
{
    if (IsEnglish(language)) { return null; }
    lock (Loaded)
    {
        if (Loaded.TryGetValue(language, out var known)) { return known; }
    }
    string file = FileFor(language);
    if (file == null) { throw new UnknownLanguageException(language); }
    MoCatalog catalog = new MoCatalog(language, MoFile.FromFile(file));
    lock (Loaded) { Loaded[language] = catalog; }
    return catalog;
}

public static string FileFor(string language)
{
    string root = CatalogDirectoryOverride ?? CatalogDirectory;
    foreach (var candidate in new[] { language, BaseOf(language) })
    {
        if (string.IsNullOrEmpty(candidate)) { continue; }
        string path = Path.Combine(root, candidate, "LC_MESSAGES", Domain + ".mo");
        if (File.Exists(path)) { return path; }
    }
    return null;
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Services/Translations.cs
public static string Get(string message) => Catalog?.Lookup(null, message) ?? message;
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Services/MoFile.cs
public override string Gettext(string message)
    => _catalog.TryGetValue(message ?? string.Empty, out var translation)
        ? translation
        : Miss(message);
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/ViewModels/MainViewModel.cs
//THE INTERFACE LANGUAGE GOES IN FIRST, before a single command, panel
//or dialog has built a caption. [...] nothing user-visible exists yet.
LanguageSetup.Setup(_settings);
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Services/Translations.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Services/MoFile.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Services/LanguageSetup.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Services/PluralExpression.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/assets/i18n/README.txt`,
`Fresco.Brix/tools/i18nharvest/README.txt`.

**Sharp edges.**

- Install the language before anything user-visible is constructed. Captions
  are read once, when a command or panel is built.
- A string your application had to reword is in no catalog and will show in
  English. Record which strings those are rather than renaming them back: this
  application keeps a renamed-string table its harvest tool reconciles against
  the code on every run, and a string in neither the code nor the table is
  reported.
- Send every lookup through one guard, so a translation that reintroduces a
  name the application must not use is refused and shown in English instead.
  The catalogs themselves are never edited: they are third-party work with
  their translators' names in the header.
- A language change takes effect at the next launch, because a window is built
  once and holds its own strings.
- The whole catalog folder can be emptied. The language picker then offers only
  English and the application runs.

## View models, commands and threading

### Show a long background load in the window title instead of a splash screen

**When you want this.** Something the application needs takes long enough to
notice, but the user can start working before it is ready.

**The MVVM shape.** The view model exposes a computed `EngineStatusText` and a
`WindowTitle` that folds it in. The service raises `StateChanged` from its own
thread; the page marshals a `Refresh(nameof(...))` back onto the UI thread and
pushes the title onto the window, which has no bindable title property.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/ViewModels/MainViewModel.cs
public string WindowTitle
{
    get
    {
        EditorDocument document = Documents?.CurrentDocument;
        if (document == null) { return AppInfo.AppName; }

        string star = document.IsModified ? "*" : string.Empty;
        string engine = EngineStatusText;
        string suffix = string.IsNullOrEmpty(engine) ? string.Empty : $" [{engine}]";
        return $"{document.DocumentName()}{star} - {AppInfo.AppName}{suffix}";
    }
}

/// <summary>
/// Gets what the title bar says about the engine while it loads.
/// </summary>
/// <remarks>The load takes seconds and the window is fully usable
/// throughout, so this is a note in the title rather than a splash screen
/// standing in front of the application.</remarks>
public string EngineStatusText
    => Engine?.State switch
    {
        EngineState.Loading => I18n.Get("loading the LilyPort engine..."),
        EngineState.Failed => I18n.Get("the LilyPort engine failed to load"),
        _ => string.Empty,
    };

/// <summary>Announces that a bound property of the window changed.</summary>
public void Refresh(string propertyName) => NotifyPropertyChanged(propertyName);
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs
//The engine announces its state from its own load thread.
viewModel.Engine.StateChanged += (_, _) => OnUiThread(() =>
{
    viewModel.Refresh(nameof(MainViewModel.EngineStatusText));
    viewModel.Refresh(nameof(MainViewModel.WindowTitle));
});
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/ViewModels/MainViewModel.cs` (the bindable
properties region),
`Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs` (`WireEngraving`).

**Sharp edges.**

- A computed property is only as current as the notifications behind it.
  `WindowTitle` is computed from the document name, its modified flag and the
  engine state, and every one of those has to raise a change notification for
  it; the view model wires several document events to a single title handler
  for exactly that reason.
- The window title is not a bindable property of the page, so it is pushed onto
  the window object rather than bound.

### Marshal work onto the UI thread with one delegate handed to services

**When you want this.** Services in your Core library raise events from worker
threads and must not know anything about the UI thread.

**The MVVM shape.** The page owns one `OnUiThread(Action)` method built on
`DispatcherQueue` and hands it to every service that needs it as an
`Action<Action>`. The service stores the delegate and posts through it; when
none was supplied it runs the work inline, which is what makes the service
testable in a host-free process.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs
private void OnUiThread(Action work)
{
    if (DispatcherQueue == null || DispatcherQueue.HasThreadAccess)
    {
        work();
        return;
    }

    DispatcherQueue.TryEnqueue(() => work());
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs
viewModel.DocumentWatcher.ToUiThread = OnUiThread;
viewModel.ExternalChanges.ToUiThread = OnUiThread;
// ...
viewModel.StartAutoCompiler(OnUiThread);
// ...
RemoteInstance.Setup(viewModel.Settings, this, OnUiThread);
```

The service side takes the delegate as a constructor argument or a settable
property and falls back to running inline:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Services/RemoteProtocol.cs
private void Post(Action work)
{
    if (_toUiThread == null) { work(); return; }
    _toUiThread(work);
}
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs` (`OnUiThread`,
`WireExternalChanges`, `WireEngraving`),
`Fresco.Brix/src/Fresco.Brix.Core/Services/RemoteProtocol.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Engrave/AutoCompiler.cs`.

**Sharp edges.**

- Keep the fast path that runs inline when you are already on the UI thread. It
  is what keeps event ordering intact; posting unconditionally reorders a
  notification behind work that was already queued.
- One delegate, handed down, is what keeps the Core library free of any
  reference to a dispatcher. A `SimpleViewModel` reaches the same place with
  `InvokeOnMainThread` when the code that needs marshaling is on the view model
  itself.

### Host one long-running engine per process and load it in the background

**When you want this.** A library you depend on has expensive, process-global
state (an interpreter, a model, a runtime) that must be loaded once, off the UI
thread, before anything can use it.

**The MVVM shape.** The engine is a plain service registered as a singleton
with `SimpleServiceResolver`. The view model resolves it and starts the load
without awaiting it, so the window is up and usable immediately. Every call
into the engine goes through one gate that first waits for readiness and then
takes a semaphore, so two calls can never overlap. The engine raises
`StateChanged` from its own thread and the page marshals it.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/ViewModels/MainViewModel.cs
//The engine is one per process and starts loading NOW, in the
//background: it takes seconds, and the first thing a user does after
//opening a file is press Engrave.
//(The window subscribes to StateChanged itself: the engine raises it on
//its own thread and only the view knows how to get back onto the UI's.)
Engine = GetService<LilyPortEngine>();
_ = Engine.BeginLoadingAsync();
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Engrave/LilyPortEngine.cs
public Task BeginLoadingAsync() => EnsureLoadTask();

public Task EnsureLoadedAsync(CancellationToken cancellationToken = default)
    => EnsureLoadTask().WaitAsync(cancellationToken);

private Task EnsureLoadTask()
{
    lock (_gate)
    {
        return _loadTask ??= Task.Run(Load);
    }
}

private void Load()
{
    SetState(EngineState.Loading);
    try
    {
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = LilyPondScheme.CreateInterpreter();
            LilyPondScheme.LoadViaLilyScm(interpreter);
        });
        // ...
        SetState(EngineState.Ready);
    }
    catch (Exception error)
    {
        Error = error;
        SetState(EngineState.Failed);
        throw;
    }
}

private async Task<T> RunOnEngineAsync<T>(Func<T> work, CancellationToken cancellationToken)
{
    await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
    await _engineLock.WaitAsync(cancellationToken).ConfigureAwait(false);
    try
    {
        return await Task.Run(
            () => Interpreter.RunWithLargeStack(work), CancellationToken.None)
            .ConfigureAwait(false);
    }
    finally { _engineLock.Release(); }
}
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Engrave/LilyPortEngine.cs`,
`Fresco.Brix/src/Fresco.Brix.UI/App.xaml.cs` (the singleton registration),
`Fresco.Brix/src/Fresco.Brix.Core/ViewModels/MainViewModel.cs`.

**Sharp edges.**

- The engine's interpreter needs far more stack than a default CLR thread has,
  which is why every call runs through `RunWithLargeStack` on a background
  thread rather than being awaited directly.
- The engine's include path is not reset between runs, so a directory added
  once stays added for the life of the process.
- Nothing about the singleton is a convenience. Say so where you register it:
  the state is process-global, and a second instance would be wrong rather than
  merely wasteful.
- The load task is created once under a lock and then shared. Every later
  caller awaits the same task instead of starting a second load.

### Marshal live engine output onto the thread that started the job

**When you want this.** A library writes progress or diagnostics from its own
thread while it runs, and your listeners touch UI-owned state (a text document,
an editor anchor) that may only be touched from one thread.

**The MVVM shape.** The job object captures `SynchronizationContext.Current`
when it is started, which is the UI thread because a command started it, and
posts every message back through it. Listeners never think about threads.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Engrave/EngraveJob.cs
//⚠ THE ENGINE WRITES FROM ITS OWN THREAD, MID-RUN. Everything that
//listens to a job's output touches the editor — the log writes into
//a text document, and the error collector puts anchors into one —
//and a text document may only be touched from the thread that owns
//it. So the thread that STARTS a job is remembered here, and every
//message is delivered back on it.
_context = SynchronizationContext.Current;
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Engrave/EngraveJob.cs
public void Message(string text, MessageType type = MessageType.Neutral)
{
    // ...
    SynchronizationContext context = _context;
    if (context == null || context == SynchronizationContext.Current)
    {
        Output?.Invoke(this, message);
        return;
    }

    //Posted, not sent: the engine must not be made to wait for a redraw,
    //and posting keeps the messages in order.
    context.Post(_ => Output?.Invoke(this, message), null);
}
```

The same idiom appears wherever a service raises events from a worker: the MIDI
player captures its context at construction because the audio engine's
callbacks arrive on a real-time thread, and the PDF manual does the same for
render completions.

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Engrave/EngraveJob.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Midi/MidiPlayerService.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Documentation/PdfManual.cs`.

The other way round the same application reaches the UI thread is
[Marshal work onto the UI thread with one delegate handed to services](#marshal-work-onto-the-ui-thread-with-one-delegate-handed-to-services).
Capture the context when the object is constructed on the UI thread; take a
delegate when it is not.

**Sharp edges.**

- `Post`, never `Send`. Sending would block the engine's thread on a redraw;
  posting also preserves message order, which a log depends on.
- Capture the context where the object is created, not where the work runs, and
  handle the case where there is none: a test process has no synchronization
  context, and the code must then run inline.

### Run one job at a time against a process-global engine

**When you want this.** You have a service that cannot be re-entered and a user
who can press the button twice.

**The MVVM shape.** A per-document job manager refuses to start a second job
while one is running; a caller that means to replace a running job aborts it
first. A queue type exists above that, but the slot is deliberately one deep.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Engrave/JobManager.cs
/// <remarks>Does nothing when a job is already running — the caller aborts
/// that one first if it means to replace it.</remarks>
public void StartJob(EngraveJob job)
{
    if (job == null || IsRunning) { return; }

    _job = job;
    job.Done += OnJobDone;

    //Announce BEFORE the work begins, so a log connected by the
    //announcement still sees the job's very first message.
    JobEventArgs arguments = new JobEventArgs(Document, job);
    JobStarted?.Invoke(this, arguments);
    AnyJobStarted?.Invoke(null, arguments);

    _ = job.StartAsync();
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Engrave/Engraver.cs
public void RunJob(EngraveJob job, EditorDocument document)
{
    if (job == null || document == null) { return; }

    EngraveJob running = JobManager.JobFor(document);
    if (running is { IsRunning: true }) { running.Abort(); }
    // ...
    job.Started += (_, _) => UpdateActions();
    JobManager.For(document).StartJob(job);
}
```

The queue's own class comment explains why more slots would buy nothing:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Engrave/JobQueue.cs
/// The engrave queue has exactly ONE slot, and that is not a simplification:
/// the engine is process-global and serializes every call through one gate, so
/// a second slot would only queue behind the first. The slot machinery is kept
/// because it is what makes "run these in sequence and let each see the
/// previous one's output" a property of the queue rather than of every caller.
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Engrave/JobManager.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Engrave/JobQueue.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Engrave/Engraver.cs`.

**Sharp edges.**

- Announce the job before starting it, or a listener that subscribes on the
  announcement misses the first message.
- Do not compute command state from the "job started" event alone. The
  `Engraver` comment records a defect where `IsRunning` was still false at that
  instant, so every enabled and disabled answer was one run behind.

### Debounce automatic background work behind a timer with eligibility gates

**When you want this.** You want to do something expensive "as the user types"
without doing it on every keystroke.

**The MVVM shape.** A plain service given the delegate that gets it onto the UI
thread, holding a one-shot timer restarted on every change. The tick is a
series of cheap refusals before any work starts. The view model starts the
service and owns the setting that enables it.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Engrave/AutoCompiler.cs
/// <summary>How long after the last change a run is considered.</summary>
public const int DelayMilliseconds = 750;
// ...
_timer = new Timer(_ => _toUiThread(Tick), null, Timeout.Infinite, Timeout.Infinite);
// ...
public void StartTimer() => _timer.Change(DelayMilliseconds, Timeout.Infinite);
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Engrave/AutoCompiler.cs
public void Tick()
{
    if (!_enabled) { return; }

    EditorDocument document = _engraver.Document();
    if (document == null) { return; }

    EngraveJob running = JobManager.JobFor(document);
    if (running is { IsRunning: true })
    {
        //A job the user asked for is running. Come back when it is done
        //rather than queueing behind it.
        void Resume(object sender, bool success)
        {
            running.Done -= Resume;
            _toUiThread(StartTimer);
        }
        running.Done += Resume;
        return;
    }

    AutoCompileState state = AutoCompileState.For(document);
    if (!state.MayCompile()) { return; }

    PreviewJob job = new PreviewJob(_engraver.Engine, document);
    JobAttributes.For(job).Hidden = true;
    _engraver.RunJob(job, document);
}
```

The eligibility test compares a hash of the document's tokens, not its text:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Engrave/AutoCompiler.cs
int hash = info.DocInfo().TokenHash();
if (hash != _hash)
{
    _hash = hash;
    //An empty document hashes to the empty hash; engraving that
    //produces nothing and would simply run forever on every keystroke.
    if (hash != EmptyHash) { return true; }
}
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Engrave/AutoCompiler.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/ViewModels/MainViewModel.cs`
(`StartAutoCompiler`).

**Sharp edges.**

- Hash tokens rather than characters. Reformatting or editing a comment then
  does not trigger a run.
- Mark work the user did not ask for as hidden, so the log does not pop open
  and the toolbar button does not flip to Stop.
- The document's modified flag is not yet true while the contents-changed event
  is running. The change is on the undo stack by then, which is what the code
  tests instead.

### Cancel work at the boundaries a library can honor

**When you want this.** A library gives you a `CancellationToken` but cannot
interrupt itself mid-call, and you need the UI to behave honestly about it.

**The MVVM shape.** The job owns a `CancellationTokenSource`, passes the token
into the library's own options object, and treats `OperationCanceledException`
as a non-failure. The command that aborts says so in the log rather than
pretending the work stopped instantly.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Engrave/EngraveJob.cs
/// <remarks>Cancellation is honored where the engine can honor it — before
/// a parse, between books, and before output is written. One book's
/// engraving is a single uninterruptible call, so a very large score
/// finishes that book first.</remarks>
public void Abort()
{
    if (!IsRunning) { return; }

    IsAborted = true;
    WriteAbortMessage();
    _cancellation?.Cancel();
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Engrave/EngraveJob.cs
catch (OperationCanceledException)
{
    //An aborted run is not a failure to report as one; upstream's
    //abort path writes its own message and ends the job quietly.
    success = false;
}
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Engrave/EngraveJob.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Engrave/LilyPortEngine.cs` (the token goes
into the run options, not into the `Task.Run` wrapper).

**Sharp edges.**

- Give the token to the library's options object rather than to the `Task.Run`
  that hosts it. Cancelling the task would abandon the work while the library
  kept running, which for a process-global engine is worse than waiting.
- Say in the documentation comment where cancellation actually takes effect. A
  Stop button that does nothing for a while is fine; a Stop button nobody can
  explain is not.

## Bridging platform services into the view model

### Own window state in a view model and reach the view through one interface

**When you want this.** The view model needs a file dialog, the focused editor,
a fullscreen switch and a way to ask the user a question, and you do not want
any of those in your Core library.

**The MVVM shape.** The view model declares a bridge interface as a bag of
delegate properties, holds one instance of it as `Window`, and calls through it
with null checks. The page implements the interface and assigns each delegate
to one of its own private methods; a head that cannot supply a capability
leaves that delegate null and the view model degrades to doing nothing rather
than throwing.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/ViewModels/MainViewModel.cs
/// <summary>
/// What the window can do that only the view knows how to do: put a file
/// dialog in front of the user, reach the editor they are working in, go
/// fullscreen, and close.
/// </summary>
/// <remarks>Each head fills in what it has; the FrameBuffer head has no file
/// dialogs, and every delegate is allowed to be null.</remarks>
public interface IWindowBridge
{
    Func<Task<string>> PickOpenPathAsync { get; set; }
    Func<string, Task<string>> PickSavePathAsync { get; set; }
    Func<string, string, string, Task<string>> PickExportPathAsync { get; set; }
    Func<IReadOnlyList<string>, bool, Task<IReadOnlyList<string>>> PickImportPathsAsync { get; set; }
    Func<Import.ImportFormat, Task<Import.ImportSettings>> ConfigureImportAsync { get; set; }
    Func<EditorView> ActiveView { get; set; }
    Action<bool> SetFullScreen { get; set; }
    Action Quit { get; set; }
    Func<string, string, Task<bool>> ConfirmAsync { get; set; }
    Func<string, string, Task> AlertAsync { get; set; }
    Func<string, string, Task<CloseAnswer>> AskSaveDiscardAsync { get; set; }
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/ViewModels/MainViewModel.cs
/// <summary>Gets or sets the bridge to what only the view can do.</summary>
public IWindowBridge Window { get; set; }
```

The page implements the interface and fills every delegate in one block, then
hands itself to the view model:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs
public sealed partial class MainPage : Page, IWindowBridge, IRemoteCommandTarget
// ...
    PickOpenPathAsync = PickOpenAsync;
    PickSavePathAsync = PickSaveAsync;
    PickExportPathAsync = PickExportAsync;
    PickImportPathsAsync = PickImportAsync;
    ConfigureImportAsync
        = format => ImportDialog.ShowAsync(XamlRoot, format, viewModel.Settings);
    ActiveView = () => _viewManager?.ActiveView;
    // ...
    ConfirmAsync = AskAsync;
    AlertAsync = (title, message)
        => InputDialogs.AlertAsync(XamlRoot, title, message);
    AskSaveDiscardAsync = AskSaveDiscardCancelAsync;
    viewModel.Window = this;
```

Each of those private methods is a picker and nothing more. This is the whole
of the save-a-file capability the view model consumes:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs
/// <remarks>
/// A picker of its own rather than PickSaveAsync's, whose one file type is
/// a LilyPort source file: a MusicXML export offered as `.ly` would be
/// offered under the wrong name and filtered by the wrong suffix.
/// </remarks>
private async Task<string> PickExportAsync(
    string suggestedName, string label, string extension)
{
    var picker = new FileSavePicker
    {
        SuggestedStartLocation = PickerLocationId.DocumentsLibrary,
        SuggestedFileName = suggestedName == null
            ? null
            : Path.GetFileName(suggestedName),
    };
    picker.FileTypeChoices.Add(label, new[] { extension });

    var file = await picker.PickSaveFileAsync();
    return file?.Path;
}
```

The view model consumes the bridge defensively, so a head with no picker and no
editor does nothing rather than failing:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/ViewModels/MainViewModel.cs
private void WithEditor(Action<EditorView> work)
{
    EditorView view = Window?.ActiveView?.Invoke();
    if (view != null) { work(view); }
}
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/ViewModels/MainViewModel.cs` (the interface
and the `Window` property),
`Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs` (the `IWindowBridge`
region, the picker methods and `BuildShell`).

**Sharp edges.**

- Three separate save pickers exist on purpose. The document picker always
  offers the application's own file type; an export offered under that filter
  would be named and filtered wrongly, and a generic import takes several files
  at once.
- Confirm and alert are separate delegates. A report shown through a two-button
  Discard and Cancel confirm reads as an offer to throw work away, and the
  comment in the interface records that this was a real defect.
- `System` must be imported in the page even when nothing obvious needs it: the
  `IAsyncOperation` awaiter extension that lets you `await` a picker lives
  there, and the file says so in a comment.
- Keep the delegates as properties on an interface rather than as abstract
  methods. A head that has no answer for one leaves it null, and the null check
  at the call site is the degradation.

## Views, XAML and custom controls

### Set a page DataContext in XAML and give the view model a XamlRoot

**When you want this.** You want the view model constructed by XAML, and its
dialog helpers to have somewhere to attach.

**The MVVM shape.** `<Page.DataContext><vm:MainViewModel /></Page.DataContext>`
in the markup. The code-behind reacts to `DataContextChanged` once, hands the
view model a `XamlRoot` getter, subscribes to the properties the window title
and status line need, and builds the shell. The rest of the page's controls
bind.

**Code.**

```xml
<!-- From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml -->
<Page
    x:Class="Fresco.Brix.Views.MainPage"
    xmlns="clr-namespace:Microsoft.UI.Xaml.Controls;assembly=CodeBrix.Platform.UI"
    xmlns:d="clr-namespace:Microsoft.UI.Xaml.Data;assembly=CodeBrix.Platform.UI"
    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
    xmlns:vm="clr-namespace:Fresco.Brix.ViewModels;assembly=Fresco.Brix.Core"
    xmlns:local="using:Fresco.Brix.Views"
    FontFamily="{StaticResource RobotoFont}"
    Background="{ThemeResource ApplicationPageBackgroundThemeBrush}">

    <Page.DataContext>
        <vm:MainViewModel />
    </Page.DataContext>
    <!-- ... -->
        <!-- The window's message line. The caret position lives on each
             editor pane's own status bar, as it does upstream, so this stays
             out of the way until there is something to say. -->
        <TextBlock x:Name="StatusLine" Grid.Row="4" Padding="8,4,8,6"
                   Visibility="Collapsed"
                   Text="{d:Binding StatusText}" />
</Page>
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs
public MainPage()
{
    DataContextChanged += (_, _) =>
    {
        //Give the view model's SimpleDialog helpers a XamlRoot to attach dialogs to
        (DataContext as IXamlRootGetter)?.SetXamlRootGetter(() => XamlRoot);

        if (DataContext is MainViewModel viewModel && _shell == null)
        {
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.StatusText))
                {
                    UpdateStatus();
                }
                else if (e.PropertyName == nameof(MainViewModel.WindowTitle)
                    && App.Shell != null)
                {
                    App.Shell.Title = viewModel.WindowTitle;
                }
            };
            BuildShell(viewModel);
        }
    };

    this.InitializeComponent(); //Leave this line last
}

private MainViewModel ViewModel => DataContext as MainViewModel;
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml`,
`Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs`.

**Sharp edges.**

- Subscribe to `DataContextChanged` before `InitializeComponent()`, and leave
  `InitializeComponent()` as the last line of the constructor. The comment in
  the file says so; the XAML-declared `DataContext` is set during that call.
- Guard the one-time work. `DataContextChanged` can fire more than once and the
  shell must be built exactly once; here the guard is that the shell field is
  still null.
- Hand over a `Func<XamlRoot>`, not a captured `XamlRoot`. A page has no
  `XamlRoot` until it is in the visual tree.
- The window title is not bindable, so the page pushes it onto the window when
  the view model announces a change.

### Build a dock shell with drawn splitters and remember its arrangement

**When you want this.** Tool panels around a center area, built out of two
nested `TriPaneView` controls from the CodeBrix.Platform toolkit, resizable by
dragging, coming back where the user left them.

The two controls themselves, and the opening and shutting of their panes, are in
[Nest two TriPaneView controls to put four regions around an editor](#nest-two-tripaneview-controls-to-put-four-regions-around-an-editor)
and
[Show and hide a TriPaneView pane by minimizing and restoring it](#show-and-hide-a-tripaneview-pane-by-minimizing-and-restoring-it).

**The MVVM shape.** The platform's toolkit ships a three-pane control: a side
pane, and a stack of an upper and a lower pane beside it, with a draggable
divider on each axis. A window that wants four regions around an editor — a
left strip, a right strip, a bottom strip and the editor itself — needs one
more region than that, so the shell NESTS two of them and owns the pair; it is
not a control itself. Panels stay plain objects with a widget and a toggle
command. The view model holds the panel manager and owns the settings store;
the page captures and applies the arrangement, because only the page can see
it.

**Code.** Nest the two controls mirrored, so that each strip is owned by
whichever control can give it the shape it should have. Here the OUTER control
places its side pane on the right and keeps the right strip there, full window
height, with the bottom strip in its lower pane; the INNER control is the outer
control's upper pane, places its side pane on the left, and keeps the left
strip there with the editor in its upper pane. One pane of the inner control is
left over, and is simply never opened:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/DockShell.cs
private readonly TriPaneView _inner = new TriPaneView
{
    SidePanePlacement = TriPaneViewSidePanePlacement.Left,
    SidePanePercent = ShellLayout.DefaultInnerSidePercent,
    StackPercent = ShellLayout.DefaultInnerStackPercent,
    UpperPanePercent = 100d,

    //Not a region of the window. Held at zero for the life of the window,
    //which with RestoreGripMode.Never means the inner stack divider is
    //never drawn and no grip is ever offered for it.
    LowerPanePercent = 0d,
    SidePaneMinLength = 200d,
    StackMinLength = 200d,
    SidePaneVerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
    UpperPaneVerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
    LowerPaneVerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
    IsDragToMinimizeEnabled = false,
    RestoreGripMode = TriPaneViewRestoreGripMode.Never,
};

private readonly TriPaneView _outer = new TriPaneView
{
    SidePanePlacement = TriPaneViewSidePanePlacement.Right,
    // ... the same four percents, from the same ShellLayout constants
    SidePaneMinLength = 200d,
    StackMinLength = EditorMinLength,
    UpperPaneMinLength = 200d,
    LowerPaneMinLength = 80d,
    // ... the same three scroll settings, and the same two drag settings
};
```

The three vertical scroll settings are not decoration. Every pane of the
control sits in a scroll viewer, and a scroll viewer measures its content with
unbounded height unless its vertical scroll bar is `Disabled`, so a pane that
should FILL comes out a few pixels tall without them — including the outer
upper pane, which is what keeps the INNER control from being measured unbounded.

A strip is shown and hidden by opening and minimizing its pane, never by
writing a zero share, because minimizing is what takes the snapshot the strip
reopens at:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/DockShell.cs
public DockShell()
{
    _outer.UpperPane = _inner;

    //Every strip starts shut, because no panel is visible yet. Minimizing
    //rather than writing a zero percent is what gives each one a snapshot
    //of the weight it should come back at; a pane that was simply set to
    //zero has nothing to go back to and reopens at the control's own class
    //default instead of the share this shell chose.
    _outer.MinimizeSidePane();
    _outer.MinimizeLowerPane();
    _inner.MinimizeSidePane();

    _outer.DividerDragCompleted += (_, _) => OnDividerDragCompleted();
    _inner.DividerDragCompleted += (_, _) => OnDividerDragCompleted();
}
```

Order matters when several panes change at once, and so does the floor under
the stack once a second control is living inside it:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/DockShell.cs
/// <remarks>Everything that is to be open is opened BEFORE anything is
/// shut, on both controls: a control refuses a request that would leave it
/// with nothing open at all, and doing it in the other order would walk
/// into that refusal on the way through. [...]</remarks>
private void ApplyPanes(ShellPanes wanted)
{
    if (wanted == null) { return; }

    //A pane control narrower than the sum of its own floors gives its side
    //pane the floor and its stack whatever is left, which can be nothing at
    //all: measured on X11, dragging the outer side divider down to the
    //editor block's 200 left the left strip at its own 200 and the EDITOR
    //at zero width, with the inner side divider gone with it. So the outer
    //stack's floor is the room the inner control actually needs — its side
    //pane's floor, its divider, and the editor's — whenever the left strip
    //is open, and just the editor's when it is not.
    _outer.StackMinLength = wanted.LeftStripIsOpen
        ? _inner.SidePaneMinLength + _inner.DividerThickness + EditorMinLength
        : EditorMinLength;

    //The inner control is only touched while it is on screen. [...]
    if (wanted.EditorBlockIsOpen)
    {
        _outer.RestoreUpperPane();
        if (wanted.EditorIsOpen) { _inner.RestoreUpperPane(); }

        if (wanted.LeftStripIsOpen) { _inner.RestoreSidePane(); }

        if (!wanted.EditorIsOpen) { _inner.MinimizeUpperPane(); }

        if (!wanted.LeftStripIsOpen) { _inner.MinimizeSidePane(); }
    }

    if (wanted.RightStripIsOpen) { _outer.RestoreSidePane(); }
    // ... the bottom strip, then the three minimize calls, in the same shape
}
```

Inside the editor region the application still needs a splitter of its own,
because a view space splits in two and either half splits again, as many times
as the user asks, which a fixed three-pane control cannot do. Its divider is a
plain `Grid` with its own pointer handling, because the themed `Thumb` paints
nothing on the Skia heads:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/SplitContainer.cs
private UIElement CreateDivider(bool horizontal, int slot, int leftPane)
{
    //A bare Thumb paints nothing under the theme templates on the Skia
    //heads (the same sharp edge the standalone ScrollBar has), so the
    //divider is a plain Grid with its own background and pointer handling.
    Grid host = new Grid
    {
        Background = DividerBrush,
        HorizontalAlignment = HorizontalAlignment.Stretch,
        VerticalAlignment = VerticalAlignment.Stretch,
    };

    bool dragging = false;
    double lastPosition = 0;

    host.PointerPressed += (sender, e) =>
    {
        dragging = true;
        lastPosition = Position(e, horizontal);
        ((Grid)sender).CapturePointer(e.Pointer);
        e.Handled = true;
    };
    host.PointerMoved += (sender, e) =>
    {
        if (!dragging) { return; }
        double position = Position(e, horizontal);
        Resize(horizontal, leftPane, position - lastPosition);
        lastPosition = position;
        e.Handled = true;
    };
    host.PointerReleased += (sender, e) =>
    {
        dragging = false;
        ((Grid)sender).ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    };
    host.PointerCaptureLost += (_, _) => dragging = false;

    if (horizontal) { SetColumn(host, slot); }
    else { SetRow(host, slot); }
    return host;
}
```

The page builds the shell, registers the panels in one block, and restores the
arrangement at the one moment when the panels exist and nothing has been able
to move them yet:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs
_shell = new DockShell { Center = _viewManager };
ShellHost.Content = _shell.Root;

//A divider the user has finished dragging is written out there and
//then, as well as on the way out: upstream saves only on close, but
//upstream's dividers are not the only thing this key holds.
_shell.LayoutChanged += (_, _) => SaveWindowLayout();

viewModel.Panels = new PanelManager(_shell, viewModel.Settings);
viewModel.ActionManager.Add(viewModel.Panels.Actions);
// ... panels constructed, then registered in one block, because
// registration order is what decides their order in the Tools menus
viewModel.Panels.AddPanel(_musicViewPanel, "viewers");
viewModel.Panels.AddPanel(_manuscriptPanel, "viewers");
// ...
//Upstream's readSettings — the window comes back the size it was, with the
//tools that were open still open, in their areas, in their tab order, at the
//divider positions the user left. Done HERE: the panels exist and nothing
//has been able to move them yet.
RestoreWindowLayout(viewModel);
```

Reading and writing the store is view model work; capturing and applying the
arrangement is view work. In the sample both halves sit on the page, so the
blocks below are recast to put the store access where it belongs, keeping every
statement as the sample has it:

```csharp
// Adapted from CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs
// The sample's RestoreWindowLayout and SaveWindowLayout read and write the
// settings store from the page. Here the view model owns the store, as it owns
// every other service, and the page passes it what only the page can see: the
// captured layout and the window's own size.

// On the view model:
public DockLayout LoadLayout() => DockLayout.Load(_settings);

public (int Width, int Height) LoadWindowSize()
    => DockLayout.LoadWindowSize(_settings);

public void SaveLayout(DockLayout layout, int width, int height)
{
    layout?.Save(_settings);
    DockLayout.SaveWindowSize(_settings, width, height);
}

// On the page:
private void RestoreWindowLayout(MainViewModel viewModel)
{
    if (viewModel == null) { return; }

    (int width, int height) = viewModel.LoadWindowSize();
    if (width > 0 && height > 0)
    {
        App.Shell?.AppWindow?.Resize(
            new Windows.Graphics.SizeInt32 { Width = width, Height = height });
    }

    _shell?.ApplyLayout(viewModel.LoadLayout());
}

private void SaveWindowLayout()
{
    if (ViewModel == null || _shell == null) { return; }

    //AppWindow.Size, not the window's Bounds. Resize and Size are a matched
    //pair — whatever quantity one sets, the other reports — and on the X11
    //head that pair is the FRAMED size, the window plus whatever the window
    //manager draws around it, while Bounds keeps answering the CLIENT area
    //the page is laid out into. Storing Bounds and restoring through Resize
    //therefore loses the frame on every round trip, and the window shrinks
    //by the frame on each launch. Storing what Resize consumes makes the
    //round trip exact: launch N + 1 opens the same window as launch N.
    Microsoft.UI.Windowing.AppWindow window = App.Shell?.AppWindow;
    Windows.Graphics.SizeInt32 size = window != null ? window.Size : default;
    ViewModel.SaveLayout(_shell.CaptureLayout(), size.Width, size.Height);
}
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Shell/DockShell.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Shell/ShellLayout.cs` (the five panes the
window uses, the default shares, and the arithmetic that decides which panes a
set of open panels asks for),
`Fresco.Brix/src/Fresco.Brix.Core/Shell/SplitContainer.cs` (the editor area's
own recursive splitter),
`Fresco.Brix/src/Fresco.Brix.Core/Shell/DockLayout.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Shell/Panel.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Shell/PanelManager.cs`,
`Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs` (`RestoreWindowLayout`,
`SaveWindowLayout`).

**Sharp edges.**

- Disable the VERTICAL scroll bar on every pane whose content should fill it.
  Each pane sits in a scroll viewer, which measures with unbounded height
  unless the bar is `Disabled`, and an editor, a log or a grid of controls then
  comes out a few pixels tall. On a nested pair the outer pane HOLDING the
  inner control needs it too, or the inner control is the thing measured
  unbounded.
- Close a pane by minimizing it, never by writing a zero share. Minimizing
  records what the pane should come back at; a pane set to zero has nothing to
  come back to and reopens at the control's own class default.
- Open everything that is to be open before you shut anything, on both
  controls. A control refuses a request that would leave it with nothing open,
  and the other order walks into that refusal halfway through.
- Never call a restore-everything method on a control one of whose panes is
  deliberately unused: it reopens that pane at the class default, and a region
  the window does not have appears on screen. Restore pane by pane instead.
- A control given less room than the sum of its own floors gives the side pane
  its floor and the stack whatever is left, which can be zero. When a second
  control lives in the stack, the stack's floor has to be the room that control
  actually needs — its own side floor, plus its divider, plus its content's
  floor — or the inner content vanishes at the end of a drag.
- Save what the window's resize call CONSUMES, not the client bounds. On the
  LinuxX11 head `AppWindow.Resize` and `AppWindow.Size` are a matched pair in
  the FRAMED size while `Bounds` answers the client area, so storing `Bounds`
  and restoring through `Resize` loses the frame on every launch and the window
  shrinks a little each time.
- Store divider positions as the share each side has of its own axis, not as
  pixels, so the layout survives a different screen — and a share read back
  from a pane that is currently shut reads as zero, so record an axis only
  while both of its panes are open.
- The themed `Thumb` paints nothing on the Skia heads, and so do a standalone
  `ScrollBar` and the themed tab controls. Where you draw your own divider the
  answer used throughout this application is a plain `Grid` with its own
  pointer handling; `TrackBar.cs` says the same thing about `Slider`.
- A drawn divider and the control's own divider are both about six pixels
  wide. A press four pixels off the center does nothing at all and looks
  exactly like a divider that is dead, so aim at the center when you drive one
  from a test.
- A panel's widget is built once and kept, and an element has one parent, so a
  tab being rebuilt must have its content moved before the old tab is thrown
  away.
- Write the layout on both the explicit Quit path and the window's `Closed`
  event, and on the control's drag-completed event as well if a divider the
  user moved should survive a crash. Writing it twice is harmless, because it
  writes the same thing.
- Panel registration order is what decides menu order. Register them in one
  block so the order is readable.

### Nest two TriPaneView controls to put four regions around an editor

**When you want this.** A window with four regions around a center area, a left
strip, a right strip, a bottom strip and the editor itself, built out of the
platform toolkit's `TriPaneView`, which offers three.

**The MVVM shape.** `TriPaneView` is part of the toolkit that ships inside the
core CodeBrix.Platform package, so a project that already references the
platform adds nothing at all to its csproj to use it; it is the one control in
this recipe that costs no new reference. The control is sealed, so a window that
needs a fourth region OWNS a pair of controls rather than deriving from one: a
plain class holds the two, exposes the outer one as the element the page drops
into its content host, and keeps the bookkeeping about which pane is which.
Panels stay plain objects with a widget and a toggle command, the view model
holds the panel manager, and the page is the only thing that ever touches a
control.

**Code.** Nest the two mirrored, so that each strip is owned by whichever
control can give it the shape it should have. The OUTER control places its side
pane on the right and keeps the right strip there, full window height, with the
bottom strip in its lower pane and everything else in its upper pane:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/DockShell.cs
private readonly TriPaneView _outer = new TriPaneView
{
    SidePanePlacement = TriPaneViewSidePanePlacement.Right,
    SidePanePercent = ShellLayout.DefaultOuterSidePercent,
    StackPercent = ShellLayout.DefaultOuterStackPercent,
    UpperPanePercent = ShellLayout.DefaultOuterUpperPercent,
    LowerPanePercent = ShellLayout.DefaultOuterLowerPercent,
    SidePaneMinLength = 200d,
    StackMinLength = EditorMinLength,
    UpperPaneMinLength = 200d,
    LowerPaneMinLength = 80d,
    SidePaneVerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
    UpperPaneVerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
    LowerPaneVerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
    IsDragToMinimizeEnabled = false,
    RestoreGripMode = TriPaneViewRestoreGripMode.Never,
};
```

Everything the control needs is written in the object initializer, so it is
already in force on the first frame drawn and nothing waits for the window to
load. The three vertical scroll settings are not decoration: every pane sits in
a scroll viewer, which measures its content with unbounded height unless its
vertical scroll bar is `Disabled`, and in a nested pair the OUTER upper pane's
setting is the one that keeps the inner control itself from being measured
unbounded. `RestoreGripMode` is `Never` and drag-to-minimize is off because
this window offers its own toggle command for every strip.

The INNER control is the outer control's upper pane, and the editor is the
inner control's upper pane, which is what puts the left strip and the editor
inside the region the right strip and the bottom strip divide:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/DockShell.cs
_outer.UpperPane = _inner;
// ...
/// <summary>Gets the element the window puts in its content host.</summary>
/// <remarks>A pane control is sealed, so this shell OWNS one rather than
/// being one.</remarks>
public UIElement Root => _outer;

/// <summary>Gets or sets what sits in the middle — the editor area.</summary>
public UIElement Center
{
    get => _center;
    set
    {
        _center = value;
        _inner.UpperPane = _center;
    }
}
```

Each strip goes into its pane once and stays there for the life of the window,
so showing and hiding an area is the pane opening and shutting rather than a
widget being re-parented, and every tab keeps its scroll position and its
selection:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/DockShell.cs
//Each strip goes into its pane once and stays there. From here on it is
//the PANE that opens and shuts.
switch (area)
{
    case DockArea.Left:
        _inner.SidePane = view;
        break;
    case DockArea.Right:
        _outer.SidePane = view;
        break;
    default:
        _outer.LowerPane = view;
        break;
}
```

Four regions out of two three-pane controls leaves one pane over. Choose which
one deliberately, and hold it shut rather than filling it:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/DockShell.cs
//Not a region of the window. Held at zero for the life of the window,
//which with RestoreGripMode.Never means the inner stack divider is
//never drawn and no grip is ever offered for it.
LowerPanePercent = 0d,
```

A stack pane is the right one to give up, because a stack pane held at zero
takes its divider off the screen with it: the window then shows exactly three
dividers, and every one of them is a real, themed divider the control draws.
The page does nothing but drop the outer control into a host and let it
stretch:

```xml
<!-- From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml -->
<ContentControl x:Name="ShellHost" Grid.Row="3"
                HorizontalContentAlignment="Stretch"
                VerticalContentAlignment="Stretch" />
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Shell/DockShell.cs` (both initializers, the
nesting, and the one place a strip is put into a pane),
`Fresco.Brix/src/Fresco.Brix.Core/Shell/ShellLayout.cs` (the five panes the
window uses, the default shares, and the arithmetic that decides which panes a
set of open panels asks for),
`Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml` and
`Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs` (the host, and the one
line that fills it),
`Fresco.Brix/src/Fresco.Brix.Core/Shell/SplitContainer.cs` (the editor area's
own recursive splitter, which is what a fixed three-pane control cannot be).

Opening and shutting a pane, and the floor the outer control needs once a
second control is living in its stack, are in
[Show and hide a TriPaneView pane by minimizing and restoring it](#show-and-hide-a-tripaneview-pane-by-minimizing-and-restoring-it).
Persisting the arrangement, the drawn splitter inside the editor area, and the
window size that survives a relaunch are in
[Build a dock shell with drawn splitters and remember its arrangement](#build-a-dock-shell-with-drawn-splitters-and-remember-its-arrangement).

**Sharp edges.**

- Nothing is added to a csproj for this control. It is part of the toolkit in
  the core platform package, which is already referenced, so a recipe that
  reaches for a new package reference here is reaching for the wrong thing.
- The control is sealed. Own a pair of them from a plain class and expose the
  outer one as a `UIElement`; do not try to derive a shell from one.
- Mirror the placement rather than repeating it. Whichever side pane you put
  where decides which strip can run the full height of the window and which one
  stops at a divider, and only one of the two controls can own a given corner.
- Disable the VERTICAL scroll bar on every pane whose content should fill it,
  including the outer pane that HOLDS the inner control, or the inner control is
  the thing measured unbounded.
- Give the leftover pane away on the stack axis, not the side axis, and hold it
  at zero with the restore grip turned off. A stack pane held shut takes its
  divider with it; a side pane held shut still leaves the window looking like it
  has a region the user cannot reach.
- Put each strip into its pane once. Re-parenting a strip to show or hide it
  costs every scroll position and every selection inside it, and an element has
  one parent, so the old container has to be cleared first or the content ends
  up claimed twice.

### Show and hide a TriPaneView pane by minimizing and restoring it

**When you want this.** A strip that appears when the user opens a tool and
disappears when the last tool in it is closed, coming back at the width they
left it at rather than at the control's own default.

**The MVVM shape.** The panels say what they want, as booleans, and one pure
function turns those booleans into a description of which panes are to be open.
That function lives in the Core library and names no XAML type at all, so the
whole arrangement is decidable in a process with no window; the class that owns
the controls is a thin driver over its answers. Panels raise a visibility event,
the driver recomputes the whole arrangement rather than working out what
changed, and asks for it.

**Code.** A pane is shut by minimizing it and shown by restoring it. Never by
writing a zero share:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/DockShell.cs
//Every strip starts shut, because no panel is visible yet. Minimizing
//rather than writing a zero percent is what gives each one a snapshot
//of the weight it should come back at; a pane that was simply set to
//zero has nothing to go back to and reopens at the control's own class
//default instead of the share this shell chose.
_outer.MinimizeSidePane();
_outer.MinimizeLowerPane();
_inner.MinimizeSidePane();
```

The snapshot is also the way a remembered share is handed to a pane that is
currently shut: open it, write the share, shut it again, all while the window is
still being built, so the strip comes back at the user's own width the first
time they open it:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/DockShell.cs
/// <summary>Gives both controls the shares the shell is holding.</summary>
/// <remarks>A pane that is shut is opened, given its share and shut again,
/// which is how the share reaches the control's own snapshot: the strip
/// then comes back at the width the user left it at rather than at this
/// shell's default the first time they open it. None of that is visible —
/// this runs while the window is still being built.</remarks>
private void ApplySizes()
{
    ApplySidePercents(_outer, _sizes.OuterSidePercent, _sizes.OuterStackPercent);
    ApplyStackPercents(_outer, _sizes.OuterUpperPercent, _sizes.OuterLowerPercent);
    ApplySidePercents(_inner, _sizes.InnerSidePercent, _sizes.InnerStackPercent);
}

private static void ApplySidePercents(TriPaneView panes, double side, double stack)
{
    bool wasMinimized = panes.IsSidePaneMinimized;
    if (wasMinimized) { panes.RestoreSidePane(); }

    panes.SidePanePercent = side;
    panes.StackPercent = stack;
    if (wasMinimized) { panes.MinimizeSidePane(); }
}
```

A control refuses a request that would leave it with nothing open at all, so the
description of a wanted arrangement carries the reading that says it never comes
to that, and the driver opens everything that is to be open before it shuts
anything:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/ShellLayout.cs
/// <summary>Gets whether the outer control would keep at least one pane open.</summary>
/// <remarks>A pane control refuses a request that would leave it with
/// nothing open at all, so nothing the shell asks for may ever come to
/// that: this is the reading that says it never does.</remarks>
public bool OuterKeepsAPaneOpen
    => RightStripIsOpen || EditorBlockIsOpen || BottomStripIsOpen;

/// <summary>Gets whether the inner control would keep at least one pane open.</summary>
/// <remarks>Read only while <see cref="EditorBlockIsOpen"/> is true; the
/// inner control is left alone otherwise.</remarks>
public bool InnerKeepsAPaneOpen => LeftStripIsOpen || EditorIsOpen;
```

Undoing a "give this panel the whole window" is the same code path, and it goes
pane by pane rather than through the control's own restore-everything:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/DockShell.cs
/// <summary>Puts the layout back the way it was before a maximize.</summary>
/// <remarks>Pane by pane, and never through the control's own "restore
/// everything": the inner control's lower pane is not one of the window's
/// regions, and a pane held at zero beside a pane that is not reads as
/// minimized, so restoring everything would open a fourth region this
/// window does not have. Nothing has to be re-shown or re-raised, because
/// nothing was hidden.</remarks>
public void RestoreFromMaximized()
{
    if (_maximized == null) { return; }

    Panel wasMaximized = _maximized;
    _maximized = null;
    ApplyPanes(WantedPanes());
    // ...
}
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Shell/DockShell.cs` (`ApplyPanes`, which is
the driver, plus `ApplySizes` and `RecordSizes`),
`Fresco.Brix/src/Fresco.Brix.Core/Shell/ShellLayout.cs` (`PanesFor`,
`PanesForMaximized`, and the two readings above),
`Fresco.Brix/src/Fresco.Brix.Core/Shell/Panel.cs` and
`Fresco.Brix/src/Fresco.Brix.Core/Shell/PanelManager.cs` (what raises the event
the driver answers).

The nesting these panes belong to is in
[Nest two TriPaneView controls to put four regions around an editor](#nest-two-tripaneview-controls-to-put-four-regions-around-an-editor);
storing the shares and reading them back is in
[Build a dock shell with drawn splitters and remember its arrangement](#build-a-dock-shell-with-drawn-splitters-and-remember-its-arrangement),
whose sharp edges also carry the floor a nested pair needs under its stack.
What a test can assert about all of this without a window is in
[Assert a toolbar's contents and a shell's pane arithmetic in host-free tests](#assert-a-toolbars-contents-and-a-shells-pane-arithmetic-in-host-free-tests).

**Sharp edges.**

- Minimize to close, restore to open. A share of zero written by hand is not a
  closed pane with a memory, it is a pane with nothing to come back to, and it
  reopens at the control's own class default instead of yours.
- Open everything that is to be open before you shut anything, on both controls.
  A control refuses a request that would leave it with nothing open, and the
  other order walks into that refusal halfway through and leaves the layout
  somewhere neither you nor the user asked for.
- Ask for the whole arrangement every time rather than working out what changed.
  Minimizing a pane that is already shut does nothing and, in particular, does
  not overwrite the snapshot the first minimize took; restoring one that is
  already open does nothing either.
- Never call a restore-everything method on a control one of whose panes is
  deliberately unused. A pane held at zero beside a pane that is not reads as
  minimized, so restore-everything gives it the control's default share and a
  region the window does not have appears on screen. Restore pane by pane.
- Reach a shut pane's remembered share by opening it, writing the share and
  shutting it again, before the window is on screen. Writing the share while the
  pane is shut goes nowhere the strip will read it from.
- Read a share back only while both panes of that axis are open. A shut pane's
  own share reads as zero, and storing THAT brings the strip back with no width
  at all on the next launch.

### Register window-level shortcuts that survive a focused text editor

**When you want this.** Menu accelerators that work before the menu has ever
been opened, and that a focused editor cannot swallow.

**The MVVM shape.** The view model owns the commands. A registrar built over
the page is given the view model's action manager; it puts a
`KeyboardAccelerator` on the window's root element for every command, and
additionally pushes a stacked input handler onto each editor's text area so
commands get first refusal on a keystroke.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/ShortcutRegistrar.cs
/// Upstream adds its QActions to the main window, so their shortcuts fire
/// wherever the focus is... Here the equivalent is to register the shortcuts
/// on the window's root element: a menu flyout's items are not in the visual
/// tree until the menu is first opened, so accelerators attached to THEM
/// never fire.
/// ...
/// An accelerator on the window only fires for a keystroke nothing below it
/// took first, and the editor takes plenty [...] So every editor also gets a
/// stacked input handler (Attach) that offers a keystroke to the commands
/// BEFORE the editor sees it.
public void Attach(TextArea textArea)
{
    if (textArea == null) { return; }
    textArea.PushStackedInputHandler(new ShortcutInputHandler(this, textArea));
}

public bool Handle(VirtualKey key, VirtualKeyModifiers modifiers)
{
    //A plain keystroke, or one with only Shift, belongs to the editor: a
    //command bound to one of those would make typing impossible.
    if ((modifiers & (VirtualKeyModifiers.Control | VirtualKeyModifiers.Menu
        | VirtualKeyModifiers.Windows)) == 0)
    {
        return false;
    }
    foreach (var pair in _registered)
    {
        AppAction action = pair.Key;
        if (!action.IsEnabled) { continue; }
        foreach (var shortcut in action.Shortcuts)
        {
            if (shortcut.Key != key || shortcut.Modifiers != modifiers) { continue; }
            action.Trigger();
            return true;
        }
    }
    return false;
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs
//The commands' shortcuts belong to the WINDOW, not to the menu items:
//a flyout item is not in the visual tree until its menu is opened.
_shortcuts = new ShortcutRegistrar(this);
_shortcuts.RegisterAll(viewModel.ActionManager);
// ... and, as each editor view is created:
    _shortcuts?.Attach(created.View.Editor.TextArea);
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Shell/ShortcutRegistrar.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Commands/KeySequence.cs`,
`Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs`.

**Sharp edges.**

- Read modifiers from the keyboard source, not from the key event's arguments.
  The registrar's comment records that Alt is reported as Shift in the editor's
  key arguments on the Skia heads, so Alt+Return arrived as Shift+Return.
- A disabled command must not swallow the keystroke, or the editor below never
  sees a key it has its own use for.
- A shortcut string that fails to parse silently loses the binding. Handle the
  case where the key itself is `+` (which splits into an empty tail), and carry
  a table of alternative key spellings so a shortcut such as Alt+Backspace does
  not parse to nothing.
- Never bind a plain keystroke, or one with only Shift, to a window-level
  command while a text editor has focus.

### Build menus and toolbars in code from command objects

**When you want this.** Menus, and the buttons on a CommandBar add-in toolbar,
whose entries follow one command object each, with correct enabled and checked
state, built from data rather than from XAML.

What a project adds to get those bars, and what a dock panel's own bar is made
of, are in
[Add the CommandBar add-in and build a tray of two toolbars from command objects](#add-the-commandbar-add-in-and-build-a-tray-of-two-toolbars-from-command-objects)
and
[Give a panel its own toolbar with the CommandBar add-in](#give-a-panel-its-own-toolbar-with-the-commandbar-add-in).

**The MVVM shape.** The view model owns the commands and the handlers. A
command that only needs Execute and CanExecute is a `SimpleCommand` on a
`SimpleViewModel`, refreshed by `[AffectsCommands]` when a property it depends
on changes. A menu or toolbar entry needs more than that (a caption, a tooltip,
an icon name, a checked state, a shortcut list), so this application's command
type adds those and raises `INotifyPropertyChanged` for them; the builders
subscribe and re-read on change. Either way the builder holds no logic: it
follows the command.

Menus are built by hand from the command list. Toolbars are not: the CommandBar
add-in ships the bar, the tray that lays two bars out side by side, the button
types, the separators, the overflow chevron, the keyboard walk along a bar and
the automation peers, so the application's job shrinks to handing each button a
command object and saying what goes on which bar in what order. Keeping that
order as DATA — a list of entries in a static class — is what lets the order be
asserted in a host-free test, with no window anywhere.

**Code.** A menu entry follows its command and re-reads on change, hooking
again on `Loaded`:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/MenuBuilder.cs
/// The handler is unhooked when the entry leaves the tree, so a rebuilt
/// menu does not leave the old entries listening — and hooked again, with
/// a refresh, when it comes back.
///
/// ⚠ The refresh is the whole point [...] A flyout's items UNLOAD every time
/// the menu closes, not only when the menu is thrown away: unhooking without
/// hooking up again froze every entry at whatever state it had the first time
/// it was shown.
private static void Follow(AppAction action, MenuFlyoutItemBase item, Action update)
{
    //Shortcuts are NOT attached here: a flyout's items are not in the
    //visual tree until the menu is first opened, so an accelerator on one
    //would never fire. ShortcutRegistrar puts them on the window instead.
    PropertyChangedEventHandler handler = (_, _) => update();
    bool following = false;

    void Start()
    {
        if (following) { return; }
        action.PropertyChanged += handler;
        following = true;
        //Catch up on everything that changed while nobody was listening.
        update();
    }

    void Stop()
    {
        if (!following) { return; }
        action.PropertyChanged -= handler;
        following = false;
    }

    Start();
    item.Loaded += (_, _) => Start();
    item.Unloaded += (_, _) => Stop();
}
```

A toolbar button is bound to the command object and given nothing else. It is
never handed an explicit enabled state, and its click is never wired by hand:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/MainToolbar.cs
private ToolButton ButtonFor(ToolbarEntry entry)
{
    AppAction action = entry.Action;
    ToolButton button;
    if (entry.Menu != ToolbarMenu.None)
    {
        //Qt's MenuButtonPopup, as one button: the main part IS the action
        //and the arrow part opens the menu. [...]
        MenuFlyout flyout = new MenuFlyout();
        flyout.Opening += (_, _) => FillMenu(flyout, entry.Menu);
        button = new ToolDropDownButton
        {
            PopupMode = PopupMode.MenuButton,
            Flyout = flyout,
        };
    }
    else if (action.IsCheckable)
    {
        button = new ToolToggleButton { IsChecked = action.IsChecked };
    }
    else
    {
        button = new ToolButton();
    }

    //IsEnabled is NOT set here. The button follows the command's
    //CanExecute, which AppAction answers from IsEnabled and re-raises on
    //every change; an explicit IsEnabled would outrank the command for good.
    button.Command = action;

    //A tool tip the action writes for itself is not the button's label, so
    //the application sets it: the add-in leaves a tool tip it did not
    //compose alone. [...] Every other button says
    //nothing at all here and the add-in composes "Text (Shortcut)", which
    //is the same string ToolbarLayout.ToolTipFor writes.
    bool applicationOwnsToolTip = false;

    void Update()
    {
        button.Text = MenuBuilder.Display(action.Text);
        button.Shortcut = action.Shortcuts.Count > 0
            ? action.Shortcuts[0].ToString()
            : null;

        //No icon of that name in the shipped sets hands back nothing at
        //all, which is what makes the add-in show the button's text rather
        //than draw a blank square. [...]
        button.Icon = IconTheme.Source(action.IconName);

        if (applicationOwnsToolTip || !string.IsNullOrEmpty(action.ToolTip))
        {
            applicationOwnsToolTip = true;
            ToolTipService.SetToolTip(button, ToolbarLayout.ToolTipFor(action));
        }

        //A click flips the toggle's own state and, separately, runs the
        //command, which flips the action's — one flip each, ending in step.
        //The action is never written from here, which is what keeps the two
        //from cancelling each other out.
        if (button is ToolToggleButton box) { box.IsChecked = action.IsChecked; }
    }

    Update();

    // ... one PropertyChanged subscription, because a toolbar button is never
    // removed from the tree while its bar lives
    action.PropertyChanged += (_, _) => Update();
    return button;
}
```

The presentation settings are inherited attached properties, so setting them
once on the tray settles both bars and every button on them — and a tray hosted
in a `ContentControl` has to be told how wide it is allowed to be, or it never
wraps and never chevrons:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/MainToolbar.cs
//The four presentation settings are INHERITED attached properties, so
//setting them on the tray settles both bars and every button on them. [...]
ToolBarProperties.SetIconSize(this, IconTheme.ToolbarIconSize);
ToolBarProperties.SetLabelMode(this, LabelMode.IconOnly);
ToolBarProperties.SetShowToolTips(this, true);

// ... and, from FollowHostWidth, which walks up to the nearest ContentControl
// on Loaded and follows its SizeChanged:

/// <summary>Caps the tray at the width its host was arranged to.</summary>
/// <remarks>
/// A <see cref="ContentControl"/> measures its content with an UNBOUNDED
/// width, so a tray hosted in one is offered infinite room: it never wraps
/// its second bar to a second row, and no bar ever moves its trailing items
/// behind the overflow chevron. The ARRANGE pass is right — the host reports
/// the window's own width — so the host's arranged width is handed back to
/// the tray as a maximum and the next measure is bounded by it. [...]
/// </remarks>
private void ApplyHostWidth(double width)
{
    if (width > 0 && !double.IsInfinity(width)) { MaxWidth = width; }
}
```

A panel that carries its own bar needs none of that width work — a bar measured
inside a real dock strip is measured against the strip's real width and grows
its chevron on its own — so the pieces the panels share are one small static
builder, and each panel says only what is on its bar and in what order.

One button changes meaning as state changes, and its handler asks the keyboard
for the modifier rather than trusting the event:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Engrave/Engraver.cs
public static EngraveRunnerAction RunnerActionFor(bool isRunning, bool shiftHeld)
    => isRunning ? EngraveRunnerAction.Abort
        : shiftHeld ? EngraveRunnerAction.Custom
        : EngraveRunnerAction.Preview;

public void UpdateActions()
{
    EngraveJob job = JobManager.JobFor(Document());
    bool running = job is { IsRunning: true };
    bool visible = running && !JobAttributes.For(job).Hidden;
    Actions.EngraveRunner.IconName = visible ? "lilypond-stop" : "lilypond-run";
    Actions.EngraveRunner.ToolTip = visible
        ? I18n.Get("Abort engraving job")
        : I18n.Get("Engrave (preview; press Shift for custom)");
}
```

Note that `RunnerActionFor` is a static, view-free function of two booleans:
which of the three things the one button means is decided by testable logic in
the Core library, not inside a click handler.

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Shell/MenuBuilder.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Shell/MainToolbar.cs` (the window's tray of
two bars),
`Fresco.Brix/src/Fresco.Brix.Core/Shell/ToolbarLayout.cs` (what is on each
window bar and in what order, as data, plus the wording of a tool tip that is
not a label),
`Fresco.Brix/src/Fresco.Brix.Core/Shell/PanelToolbar.cs` (the static builder
the two panel bars share),
`Fresco.Brix/src/Fresco.Brix.Core/Shell/ManuscriptViewerPanel.cs` and
`Fresco.Brix/src/Fresco.Brix.Core/Shell/DocumentationPanel.cs` (one bar each,
icons on one and captions on the other),
`Fresco.Brix/src/Fresco.Brix.Core/Commands/AppAction.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Commands/ActionCollection.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Commands/ActionCollectionManager.cs`.

**Sharp edges.**

- Rebuild handlers on `Loaded`, not only at construction: a flyout's items
  unload every time the menu closes, and an entry that stopped listening never
  updates again.
- A menu that has been opened once ignores `Clear()`; remove items from the end
  instead. There is no "about to show" event either, so refills hang on
  `PointerEntered` and `Loaded`.
- Mnemonic markers in menu text are stripped at display only, never out of the
  translation key. The platform raises no access-key event, so the mnemonic
  itself does nothing.
- An async command handler started with a discarded task fails in silence. Await
  it inside a try/catch and route the failure somewhere visible; this
  application routes it to a static failure handler the window points at its
  internal-error dialog.
- Never set `IsEnabled` on a button that carries a command. The button already
  follows the command's `CanExecute`, and an explicit `IsEnabled` outranks it
  from then on, so the button stops greying out when the command does.
- Let the bar compose the ordinary tool tip out of the button's label and its
  shortcut, and set a tip yourself only where the command's own tip is NOT its
  label. Latch a flag the first time you set one: once the application owns a
  button's tip it has to keep saying the whole thing, because the bar will not
  compose over it.
- A checkable button and its command each flip once — the button flips itself
  on the click, the command flips the action — so writing the action's state
  from the click handler as well makes the two cancel out. Re-read the action
  onto the button when the action changes, and never the other way.
- A tray or a bar hosted in a `ContentControl` is measured with an unbounded
  width, so it never wraps to a second row and never moves anything behind the
  overflow chevron. Hand it the host's ARRANGED width back as a `MaxWidth` and
  the next measure is bounded. A bar placed in a real, sized container does not
  need this.
- An icon name that no shipped set carries should resolve to nothing rather
  than to a blank square: the bar then falls back to the button's text, and
  emptying the icon folder leaves a working, if wordy, toolbar.
- Re-create toolbar buttons when the bar is rebuilt: the change subscription
  goes with the old button.
- Keep what is on each bar, and in what order, as data in its own class. That
  is what makes the order assertable in a host-free test, and it survives the
  bar being rebuilt for a preference change.

### Add the CommandBar add-in and build a tray of two toolbars from command objects

**When you want this.** Real toolbars in a CodeBrix.Platform window: one or more
bars laid out side by side, with an overflow chevron, keyboard navigation and
automation peers, without writing any of that yourself.

**The MVVM shape.** The CommandBar add-in is one package reference in the Core
library's csproj, and that is the whole of what a project adds: the heads name
nothing, and the add-in brings the SVG add-in with it, which is how a button's
icon gets drawn. What arrives is the bar, the tray that lays bars out side by
side, the button types (a plain button, a toggle, a drop-down whose main part is
the command and whose arrow opens a menu), the separator, the filling spacer,
the overflow chevron, the keyboard walk along a bar, the automation peers and a
composed tool tip. What is left for the application is a view model that owns
the commands and a small static class that says what is on each bar and in what
order, as DATA, so the order can be asserted with no window anywhere.

**Code.** The reference sits under a comment that records what it replaced and
what it drags in with it:

```xml
<!-- From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Fresco.Brix.Core.csproj -->
<!-- The two window toolbars and the two panel toolbars: the CommandBar
     add-in. ToolBarTray/ToolBar/ToolButton/ToolDropDownButton/
     ToolToggleButton/ToolBarSeparator replace the hand-built StackPanel row
     in Shell/MainToolbar.cs, which was written when CodeBrix.Platform had no
     toolbar control. The add-in owns the chrome, the chevron overflow, the
     keyboard walk and the automation peers.
     It carries a HARD dependency on the Svg add-in
     ...
     icons are drawn: the embedded light/dark icon SVGs below are handed to
     SvgIconSource through cb-res:// URIs. The Svg add-in in turn brings
     ...
     Fresco.Brix.MusicView already pins. Apache-2.0 and MIT throughout. -->
```

The class that IS the window's toolbar area derives from the tray, sets the
presentation once, and builds its bars when it enters a tree:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/MainToolbar.cs
public sealed class MainToolbar : ToolBarTray
// ...
ToolBarProperties.SetIconSize(this, IconTheme.ToolbarIconSize);
ToolBarProperties.SetLabelMode(this, LabelMode.IconOnly);
ToolBarProperties.SetShowToolTips(this, true);

//The bars are built once the tray is in a tree, because a button's icon
//resolves against the theme the tree says it is in. A theme change no
//longer rebuilds anything: an icon element re-renders itself when the
//theme or the display scale changes, which is also what keeps it
//pixel-exact at a fractional scale.
Loaded += (_, _) =>
{
    FollowHostWidth();
    if (_built) { return; }

    Rebuild();
};

Unloaded += (_, _) => StopFollowingHostWidth();
```

Each bar is filled from the layout class, and a bar with nothing on it is never
added at all, so an application that ships a bar's commands conditionally does
not leave an empty run of chrome behind:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/MainToolbar.cs
private void Rebuild()
{
    _built = true;

    while (Children.Count > 0)
    {
        Children.RemoveAt(Children.Count - 1);
    }
    // ...
    ToolBar mainBar = new ToolBar { Title = ToolbarLayout.MainTitle() };
    bool verbose = VerboseToolButtons(_settings);
    foreach (ToolbarEntry entry in ToolbarLayout.Main(
        _main, _browser, _scoreWizard, _engrave, verbose))
    {
        Add(mainBar, entry);
    }

    if (mainBar.Items.Count > 0) { Children.Add(mainBar); }

    IReadOnlyList<ToolbarEntry> musicEntries = ToolbarLayout.Music(_music);
    if (musicEntries.Count > 0)
    {
        ToolBar musicBar = new ToolBar { Title = ToolbarLayout.MusicTitle() };
        foreach (ToolbarEntry entry in musicEntries)
        {
            Add(musicBar, entry);
        }

        Children.Add(musicBar);
    }
    // ...
}
```

One entry becomes one item, and the three kinds an entry can be are the three
things a bar takes:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/MainToolbar.cs
private void Add(ToolBar bar, ToolbarEntry entry)
{
    switch (entry.Kind)
    {
        case ToolbarEntryKind.Separator:
            bar.Items.Add(new ToolBarSeparator());
            return;

        case ToolbarEntryKind.Widget:
            UIElement control = ControlFor(entry, bar.Title);
            if (control != null) { bar.Items.Add(control); }

            return;

        default:
            if (entry.Action == null) { return; }

            bar.Items.Add(ButtonFor(entry));
            return;
    }
}
```

The order itself is a list of entries in a static class, with a title per bar,
and nothing in it names a XAML type:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/ToolbarLayout.cs
/// <summary>The Main Toolbar's title.</summary>
// ...
public static string MainTitle() => I18n.Get("Main Toolbar");

/// <summary>The Music View Toolbar's title.</summary>
public static string MusicTitle() => I18n.Get("Music View Toolbar");
// ...
public static IReadOnlyList<ToolbarEntry> Music(MusicViewActions music)
{
    List<ToolbarEntry> entries = new List<ToolbarEntry>();
    if (music == null) { return entries; }

    entries.Add(ToolbarEntry.Control(
        ToolbarWidget.DocumentChooser, music.MusicDocumentSelect));
    // ...
    entries.Add(ToolbarEntry.Separator());
    entries.Add(ToolbarEntry.For(music.MusicZoomIn));
    entries.Add(ToolbarEntry.Control(ToolbarWidget.ZoomChooser));
    entries.Add(ToolbarEntry.For(music.MusicZoomOut));
    entries.Add(ToolbarEntry.For(music.MusicMagnifier));
    // ...
    return entries;
}
```

The page holds a stretched host for the tray and fills it in code, after the
menus, because two of the pull-downs on the bars are the File menu's own
sub-menus:

```xml
<!-- From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml -->
<ContentControl x:Name="ToolbarHost" Grid.Row="1"
                HorizontalContentAlignment="Stretch" />
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs
_toolbar = new MainToolbar(
    viewModel.Actions,
    viewModel.Browser.Actions,
    // ... the rest of the command collections the two bars follow
    viewModel.Settings)
{
    MusicView = _musicViewPanel,
};
ToolbarHost.Content = _toolbar;
```

How one entry turns into a button that follows its command (the enabled state,
the checked state, the icon, and who writes the tool tip) is the same story the
menus tell, and it is in
[Build menus and toolbars in code from command objects](#build-menus-and-toolbars-in-code-from-command-objects)
rather than repeated here. The icons the buttons carry come from
[Render embedded SVG icons through one renderer and pick the set by theme](#render-embedded-svg-icons-through-one-renderer-and-pick-the-set-by-theme).

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Fresco.Brix.Core.csproj` (the one reference,
and the comment recording what the add-in brings with it),
`Fresco.Brix/src/Fresco.Brix.Core/Shell/MainToolbar.cs` (the tray, the rebuild,
and the width work a hosted tray needs),
`Fresco.Brix/src/Fresco.Brix.Core/Shell/ToolbarLayout.cs` (what is on each bar
and in what order, as data, plus the wording of a tool tip that is not a label),
`Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml` and
`Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs` (the host, and the one
line that fills it).

A panel's own bar is built from the same pieces in
[Give a panel its own toolbar with the CommandBar add-in](#give-a-panel-its-own-toolbar-with-the-commandbar-add-in),
and what all of it can be asserted with no window is in
[Assert a toolbar's contents and a shell's pane arithmetic in host-free tests](#assert-a-toolbars-contents-and-a-shells-pane-arithmetic-in-host-free-tests).

**Sharp edges.**

- One package reference, in the library every head already references. The heads
  name nothing, and the SVG add-in that draws the icons arrives with the toolbar
  add-in rather than being named again.
- Set the presentation on the TRAY, not on each bar and not on each button. The
  settings are inherited attached properties, so one call each settles every bar
  and every button under it, and a single button can still override its own.
- Build the bars once the tray is in a tree. A button's icon resolves against
  the theme the tree says it is in, and a tray that builds in its constructor
  has no tree to ask.
- A tray hosted in a `ContentControl` is measured with an unbounded width, so it
  never wraps a second bar to a second row and never moves anything behind the
  chevron. Hand the host's ARRANGED width back as a maximum and the next measure
  is bounded; a bar placed in a real, sized container needs none of this.
- Keep what is on each bar, and in what order, as data in its own class. That is
  what makes the order assertable with no window, and it survives the bars being
  rebuilt when a preference changes.
- Do not add an empty bar. A bar whose entries all came from a feature that is
  switched off is a run of chrome with nothing in it.
- Re-create the buttons when the bars are rebuilt. The subscription that keeps a
  button following its command goes with the old button.

### Give a panel its own toolbar with the CommandBar add-in

**When you want this.** A dock panel with a real toolbar of its own, in a strip
narrow enough that some of the buttons will not fit.

**The MVVM shape.** A window's tray and a panel's bar share almost everything,
so the shared part is one small static builder in the Core library: it creates a
bar with the presentation set on it, and it turns a command object into a
button. Each panel then says only what is on its bar and in what order. The
builder is internal, takes command objects rather than a page, and names no
panel, which is what lets two panels with very different bars use the same three
methods.

**Code.** The shared builder is the whole of what the panels have in common:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/PanelToolbar.cs
/// <remarks>
/// The presentation settings are INHERITED attached properties, so setting
/// them on the bar settles every button on it and any one button can still
/// override its own. <see cref="OverflowMode"/> is left at its default,
/// <see cref="OverflowMode.Chevron"/>.
/// </remarks>
internal static ToolBar Create(string title, LabelMode labels)
{
    ToolBar bar = new ToolBar { Title = title ?? string.Empty };
    ToolBarProperties.SetIconSize(bar, IconTheme.ToolbarIconSize);
    ToolBarProperties.SetLabelMode(bar, labels);
    ToolBarProperties.SetShowToolTips(bar, true);
    return bar;
}
```

The bar's title is not decoration: it is the bar's accessible name and the tail
of each button's accessible name, which is what a panel would otherwise have to
set on every control by hand.

One panel asks for an icon bar and pushes its Help button to the far end with a
filling spacer, which is what a bar that stretches across a row can do and a bar
sharing a tray cannot:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/ManuscriptViewerPanel.cs
internal ToolBar BuildToolBar()
{
    _toolbar = PanelToolbar.Create(Title, LabelMode.IconOnly);

    _toolbar.Items.Add(PanelToolbar.ButtonFor(Actions.ViewerOpen));
    _toolbar.Items.Add(PanelToolbar.ButtonFor(Actions.ViewerClose));
    _toolbar.Items.Add(new ToolBarSeparator());
    _toolbar.Items.Add(BuildChooser());
    // ... zoom, paging and rotation, each group behind a separator of its own
    _toolbar.Items.Add(PanelToolbar.ButtonFor(Actions.ViewerReload));

    //Upstream keeps Help in a SECOND toolbar, right-aligned, "not intended
    //to be configured" (viewers/toolbar.py createLayout/populate). A filling
    //spacer is what pushes it there, and it has something to fill because
    //this bar stretches across a Grid row rather than sharing a tray with
    //another bar (the add-in's pitfall 7).
    _toolbar.Items.Add(new ToolBarSpacer { Fill = true });
    _toolbar.Items.Add(PanelToolbar.ButtonFor(Actions.ViewerHelp));

    UpdateViewState();
    return _toolbar;
}
```

The other asks for a text bar, because most of its buttons name no artwork that
the shipped icon sets carry and several of them have no command behind them at
all. A caption that is not what the command is called is the one case where the
application, not the add-in, writes the whole tool tip:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/DocumentationPanel.cs
ToolBar bar = PanelToolbar.Create(Title, LabelMode.TextOnly);
bar.Items.Add(PanelToolbar.ButtonFor(Actions?.HelpBack, "<<"));
bar.Items.Add(PanelToolbar.ButtonFor(Actions?.HelpForward, ">>"));

//Upstream's own separator, between the two history buttons and the rest
//(docbrowser/browser.py).
bar.Items.Add(new ToolBarSeparator());
bar.Items.Add(PanelToolbar.ButtonFor(Actions?.HelpHome));
bar.Items.Add(BuildContentsToggle());
// ... the zoom and fit buttons, then paging, then the external-viewer button
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/PanelToolbar.cs
/// <remarks>
/// A caption such as "&lt;&lt;" is not what the command is called, so the
/// add-in must not compose a tool tip out of it: the application sets the
/// whole tip, in <see cref="ToolbarLayout.ToolTipFor"/>'s wording, and the
/// add-in leaves a tip it did not compose alone.
/// </remarks>
internal static ToolButton ButtonFor(AppAction action, string caption)
{
    if (action == null) { return Button(caption, caption, null); }

    ToolButton button = New(action);

    void Update()
    {
        button.Text = caption;
        ToolTipService.SetToolTip(button, ToolbarLayout.ToolTipFor(action));
        ShowChecked(button, action);
    }

    Update();
    action.PropertyChanged += (_, _) => Update();
    return button;
}
```

A bar in a dock strip needs no width rule of its own, and measuring is what
settled that:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/PanelToolbar.cs
/// A panel bar needs NO width rule of its own. <see cref="MainToolbar"/> has to
/// cap its tray at its host's arranged width, because a
/// <see cref="ContentControl"/> measures its content with an unbounded width
/// and a tray offered infinite room never wraps and never chevrons (the package
// ...
/// dock strip does not have that problem — it is measured against the strip's
// ...
/// item back when the strip is widened, with no cap anywhere. The same cap was
/// written here first and then removed once it was measured to do nothing.
```

The chevron is what makes a single bar workable in a narrow strip at all: what
does not fit moves behind it, in order, and comes back when the room does, which
is why neither panel needs a second row or a hidden horizontal scroll bar.

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Shell/PanelToolbar.cs` (the shared builder:
`Create`, the two `ButtonFor` overloads, and the command-free `Button`),
`Fresco.Brix/src/Fresco.Brix.Core/Shell/ManuscriptViewerPanel.cs`
(`BuildToolBar`, the filling spacer, and the chooser that caps its own width
because it shares a bar in a dock strip),
`Fresco.Brix/src/Fresco.Brix.Core/Shell/DocumentationPanel.cs` (`BuildToolBar`,
and the chooser that is given a line of its own instead),
`Fresco.Brix/src/Fresco.Brix.Core/Shell/ToolbarLayout.cs` (`ToolTipFor`, the
wording a panel bar sets by hand).

The window's own tray is in
[Add the CommandBar add-in and build a tray of two toolbars from command objects](#add-the-commandbar-add-in-and-build-a-tray-of-two-toolbars-from-command-objects),
and the rules a button follows in either place are in
[Build menus and toolbars in code from command objects](#build-menus-and-toolbars-in-code-from-command-objects).

**Sharp edges.**

- Do not give a panel bar a width cap. A bar measured inside a real dock strip
  is measured against the strip's real width and grows its chevron on its own;
  the cap a hosted tray needs does nothing here, which is worth measuring before
  copying it across.
- A filling spacer fills what its bar has spare. In a bar that stretches across
  a row that is the whole trailing end, which is how a Help button reaches the
  far side; in a bar sharing a tray with another bar there is nothing spare to
  fill and the spacer does nothing visible.
- Give the bar its title. It is the bar's accessible name and it becomes the
  tail of every button's accessible name, which is a whole class of
  per-control automation properties you then do not write.
- Pick the label mode from what the commands can actually show. A bar whose
  commands mostly name no shipped icon is three decorated buttons among a row of
  bare ones; text is the honest answer there, and icons the honest answer on a
  bar whose commands all name artwork.
- Let the add-in compose the tool tip where the caption IS the command's name,
  and write the whole tip yourself where it is not. A caption invented for the
  bar describes nothing, so a tip composed out of it describes nothing either.
- A control on a bar that can hold a long string, such as a chooser over file
  names, needs a maximum of its own in a narrow strip, or one long entry pushes
  every control after it behind the chevron.

### Show and size a modal dialog on the Skia heads

**When you want this.** A `ContentDialog` that is not clipped on a small window
and does not collapse to nothing.

**The MVVM shape.** The view model asks for a decision through a bridge
delegate; the page builds and shows the dialog. One small helper clamps the
dialog's size against the actual `XamlRoot` before it is shown.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/DialogSizing.cs
/// Board trap 43: a <c>ContentDialog</c>'s width is the
/// <c>ContentDialogMaxWidth</c> RESOURCE, and something inside it must carry a
/// <c>MinWidth</c> or the content collapses. What that trap does NOT say [...]
/// is that the dialog is also clipped by the window: a design that asks for
/// 1,180 pixels inside a 1,024-pixel window loses its right-hand column,
/// silently and in every language, because the number is a constant rather
/// than a measurement.
public static void Clamp(ContentDialog dialog, double designWidth, double designHeight)
{
    if (dialog?.XamlRoot == null) { return; }
    Size available = dialog.XamlRoot.Size;
    double width = available.Width > Margin
        ? Math.Min(designWidth, available.Width - Margin)
        : designWidth;
    double height = available.Height > Margin
        ? Math.Min(designHeight, available.Height - Margin)
        : designHeight;
    dialog.Resources["ContentDialogMaxWidth"] = width;
    dialog.Resources["ContentDialogMaxHeight"] = height;
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Widgets/WidgetDialog.cs
public async Task<bool> ShowAsync(XamlRoot xamlRoot)
{
    Dialog = new ContentDialog
    {
        Title = Title,
        Content = _panel,
        PrimaryButtonText = AcceptText ?? StandardButtons.Ok,
        CloseButtonText = RejectText ?? StandardButtons.Cancel,
        DefaultButton = ContentDialogButton.Primary,
        IsPrimaryButtonEnabled = IsAcceptEnabled,
        XamlRoot = xamlRoot,
    };
    try { return await Dialog.ShowAsync() == ContentDialogResult.Primary; }
    finally { Dialog = null; }
}
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Shell/DialogSizing.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Widgets/WidgetDialog.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Shell/InputDialogs.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Shell/InternalErrorDialog.cs`.

**Sharp edges.**

- Set the width through the `ContentDialogMaxWidth` resource, not the
  `MaxWidth` property, and give something inside a `MinWidth` or the content
  collapses.
- Clamp against the real `XamlRoot` size. A constant design width is clipped
  silently on a smaller window, in every language.
- A `ContentDialog` has three button slots. OK and Cancel spend two, so a Help
  or Reset button goes inside the content instead.
- The themed tab control paints nothing on the Skia heads, so a multi-page
  dialog is a row of buttons over one visible page at a time.
- Install a global unhandled-exception handler that shows a dialog, but pass it
  a `Func<XamlRoot>` rather than a captured root. A page has no `XamlRoot`
  until it is in the visual tree, and capturing the value there captures null,
  so every failure is shown to nobody.

### Render embedded SVG icons through one renderer and pick the set by theme

**When you want this.** Vector icons that follow the desktop's light or dark
scheme, shipped inside the assembly, handed to the platform as an
`SvgIconSource` where it can draw them, as every CommandBar add-in toolbar
button here does, and recolored by your own renderer where it cannot.

The bars those buttons sit on are built in
[Add the CommandBar add-in and build a tray of two toolbars from command objects](#add-the-commandbar-add-in-and-build-a-tray-of-two-toolbars-from-command-objects)
and
[Give a panel its own toolbar with the CommandBar add-in](#give-a-panel-its-own-toolbar-with-the-commandbar-add-in).

**The MVVM shape.** Icons are `EmbeddedResource` items under two logical-name
prefixes, one per theme, and there are two ways to get one onto the screen.
Where the platform can draw the file itself — a toolbar button is the case here
— hand it BOTH files of a pair as resource URIs and let it pick, so no code of
yours watches the theme at all. Where you need the pixels (a bitmap, a
measurement, a test that compares two renderings), one static renderer of your
own turns a named resource into an `Image`, and a theme helper chooses the
prefix and the foreground and returns an unsubscribe action.

**Code.** The pair, built once per name and cached, with either set standing in
for the other when only one of them ships a name:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Services/IconTheme.cs
public static SvgIconSource Source(string name)
{
    if (string.IsNullOrEmpty(name)) { return null; }

    lock (_sources)
    {
        if (_sources.TryGetValue(name, out SvgIconSource cached)) { return cached; }

        bool light = Has(IconSet.Light, name);
        bool dark = Has(IconSet.Dark, name);
        if (!light && !dark)
        {
            _sources[name] = null;
            return null;
        }

        //Either set stands in for the other when only one of them ships the
        //name, so the button never draws nothing at one theme and something
        //at the other. [...]
        Uri lightUri = ResourceUri(light ? LightPrefix : DarkPrefix, name);
        Uri darkUri = ResourceUri(dark ? DarkPrefix : LightPrefix, name);

        SvgIconSource source = new SvgIconSource
        {
            Source = lightUri,
            Dark = darkUri,
            TintMode = IconTintMode.None,
            Size = ToolbarIconSize,
        };

        _sources[name] = source;
        return source;
    }
}

/// <summary>Builds the resource URI one icon file is read through.</summary>
public static Uri ResourceUri(string prefix, string name)
    => IconResourceScheme.Create(typeof(IconTheme).Assembly, prefix + name + ".svg");
```

`TintMode` is `None` deliberately: each of the two sets already carries the
strokes that are right for the background it is meant for, so tinting would
overpaint an intent the artwork already expresses. A set that encoded its
foreground as one color for both themes would want the opposite.

The same two prefixes, the same embedded files, through the application's own
renderer, for everything the platform is not drawing:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Services/IconTheme.cs
public static IconSet SetFor(ElementTheme theme)
    => theme == ElementTheme.Dark ? IconSet.Dark : IconSet.Light;

public static string PrefixFor(IconSet set)
    => set == IconSet.Dark ? DarkPrefix : LightPrefix;

public static Color ForegroundFor(ElementTheme theme)
    => theme == ElementTheme.Dark
        ? Color.FromArgb(0xff, 0xe8, 0xe8, 0xe8)
        : Color.FromArgb(0xff, 0x10, 0x10, 0x10);

public static Image Image(ElementTheme theme, string name, int size = ToolbarIconSize)
    => SymbolIcons.Icon(PrefixFor(SetFor(theme)), name, ForegroundFor(theme), size);

public static Action Follow(FrameworkElement element, Action<ElementTheme> changed)
{
    if (element == null || changed == null) { return () => { }; }
    void OnThemeChanged(FrameworkElement sender, object args) => changed(sender.ActualTheme);
    element.ActualThemeChanged += OnThemeChanged;
    return () => element.ActualThemeChanged -= OnThemeChanged;
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/QuickInsert/SymbolIcons.cs
/// <summary>
/// THE application's one SVG renderer: an embedded SVG in, a recoloured
/// bitmap out.
/// </summary>
/// <remarks>
/// [...] Board rule 6b is the reason
/// there is one of these and not two: a second renderer would be a second Skia
/// call site, and the family is trying to leave Skia, not spread it.
/// </remarks>
public static bool Has(string resourcePrefix, string name)
    => name != null && resourcePrefix != null
        && typeof(SymbolIcons).Assembly.GetManifestResourceInfo(
            resourcePrefix + name + ".svg") != null;
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/QuickInsert/SymbolIcons.cs
//`currentColor' in the engine's output resolves to whatever paint the
//canvas carries, so the recolour is a colour filter over the picture.
using SKPaint paint = new SKPaint
{
    ColorFilter = SKColorFilter.CreateBlendMode(
        new SKColor(color.R, color.G, color.B, color.A),
        SKBlendMode.SrcIn),
};
canvas.DrawPicture(picture, paint);
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Services/IconTheme.cs` (`Source` and
`ResourceUri` for the platform route, `Image` and `Bitmap` for the renderer),
`Fresco.Brix/src/Fresco.Brix.Core/QuickInsert/SymbolIcons.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Shell/MainToolbar.cs` and
`Fresco.Brix/src/Fresco.Brix.Core/Shell/PanelToolbar.cs` (the one line each
that puts an icon on a button),
`Fresco.Brix/src/Fresco.Brix.Core/Fresco.Brix.Core.csproj` (the two
`EmbeddedResource` groups, their `LogicalName` prefixes, and the comment
recording that the same files are read both ways).

The same parser reads whole engraved pages and turns their anchors into
clickable regions in
[Parse SVG once into a scene graph and use its anchors as hit-test geometry](#parse-svg-once-into-a-scene-graph-and-use-its-anchors-as-hit-test-geometry).

**Sharp edges.**

- Keep ONE renderer of your own. Every SVG the application rasterizes itself,
  from a measured icon to the generated symbol glyphs, goes through one call
  site by rule; a second one is a second place to fix a rendering problem. The
  platform's own icon route is not a second one of yours — it is the platform
  drawing, which is the whole reason to prefer it where it fits.
- Name the resource in FULL when you build a resource URI. A terse suffix that
  happens to match a file in both prefixes matches neither unambiguously and
  resolves to nothing, with no error to see.
- Answer "no such icon" with nothing at all rather than with a blank square.
  The bar then falls back to the button's text, so emptying the icon folder
  leaves a working, if wordy, toolbar — and cache the miss, so a name nobody
  ships is answered from the dictionary too.
- Let the control draw its own disabled state. A hand-built button had to draw
  its icon at reduced opacity itself, because full opacity on a disabled button
  looks enabled; a button that carries a command and an icon source gets that
  from the control instead, which is one fewer thing to get wrong.
- Decide tinting from what the artwork already says. Two sets drawn for two
  backgrounds want no tint; one set that encodes its foreground as a single
  color wants a color filter over the picture, which is what the renderer
  above does with `SrcIn`.

## Graphics and rendering

### Draw a paged document view that scrolls by translating a viewport-sized surface

**When you want this.** A zoomable, scrollable document view where the full
content would be far larger than any surface you can allocate.

**The MVVM shape.** A custom control derived from `Grid` holding an
`SKXamlCanvas` the size of the viewport and its own scroll offset. Overlays
reach the control through a small interface, so their arithmetic can be tested
without a window; the panel that hosts the control exposes delegates the page
fills in.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/libs/Fresco.Brix.MusicView/View/MusicViewControl.cs
/// The scroll area is built by hand rather than taken from a
/// <c>ScrollViewer</c>: the drawing surface must stay the size of the VIEWPORT
/// (a twenty-page score at 400% is 90,000 pixels tall, and a surface that size
/// is not allocatable), so the view keeps its own offset and draws the pages
/// translated by it — which is exactly what a QAbstractScrollArea does.
public sealed class MusicViewControl : Grid, IOverlayHost
{
    private readonly SKXamlCanvas _canvas = new SKXamlCanvas();
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/libs/Fresco.Brix.MusicView/View/MusicViewControl.cs
private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
{
    SKCanvas canvas = e.Surface.Canvas;
    canvas.Clear(new SKColor(0x50, 0x50, 0x50));   // the staging buffer persists
    if (Layout.Count == 0) { return; }

    SKPointI offset = ViewOffset;
    var visible = new SKRect(offset.X, offset.Y, offset.X + e.Info.Width, offset.Y + e.Info.Height);
    foreach (ScorePage page in Layout.PagesAt(visible).OrderBy(Layout.IndexOf))
    {
        var geometry = new SKRect(page.X, page.Y, page.X + page.Width, page.Y + page.Height);
        SKRect onScreen = geometry;
        onScreen.Offset(-offset.X, -offset.Y);          // <- the translate that IS the scroll
        // ...
        canvas.Save();
        canvas.Translate(onScreen.Left, onScreen.Top);
        canvas.ClipRect(new SKRect(0, 0, page.Width, page.Height));
        page.Paint(canvas, new SKRect(0, 0, page.Width, page.Height));
        canvas.Restore();
    }
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/libs/Fresco.Brix.MusicView/View/IOverlayHost.cs
public interface IOverlayHost
{
    SKPointI ViewOffset { get; }
    double ZoomFactor { get; }
    PageLayout Layout { get; }
    SKColor PaperColor { get; }
    void Invalidate();
}
```

**Where to look.**
`Fresco.Brix/src/libs/Fresco.Brix.MusicView/View/MusicViewControl.cs`,
`Fresco.Brix/src/libs/Fresco.Brix.MusicView/View/IOverlayHost.cs`,
`Fresco.Brix/src/libs/Fresco.Brix.MusicView/View/RubberBand.cs`,
`Fresco.Brix/src/libs/Fresco.Brix.MusicView/View/Magnifier.cs`,
`Fresco.Brix/src/libs/Fresco.Brix.MusicView/Layout/PageLayout.cs`,
`Fresco.Brix/src/libs/Fresco.Brix.MusicView/Layout/LayoutEngine.cs`,
`Fresco.Brix/tests/libs/Fresco.Brix.MusicView.Tests/PageLayoutTests.cs`.

**Sharp edges.**

- Clear the canvas on every frame. The staging buffer persists between frames,
  so a missing clear shows the previous frame under this one.
- A click is only usable once the pointer has been released, and the surface
  marks the press handled, so take the released event with `handledEventsToo`.
- Modifiers on a pointer event are not reliable on the Skia heads; read the
  keyboard source instead.
- The control has no natural size. A host that hands out the desired height (a
  dock tab's content does) gives it a few pixels, so a small stretching helper
  stands between them.
- Scroll bars are code-built from their template parts, because a standalone
  themed `ScrollBar` paints nothing on the Skia heads.
- Put the layout arithmetic (which pages intersect the viewport, where each one
  sits) in a plain class with no control in it. That is what makes it testable
  in a host-free test project.

### Parse SVG once into a scene graph and use its anchors as hit-test geometry

**When you want this.** You render SVG that carries semantic links and want
both fast redraw at any zoom and clickable regions, without a second parse.

**The MVVM shape.** A page object parses the file once into a retained picture
and walks the retained scene graph for anchor nodes, converting each node's
transformed bounds into fractions of the page. The host supplies typefaces
through an interface, so the drawing library never learns where fonts come
from.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/libs/Fresco.Brix.MusicView/Pages/SvgPage.cs
/// The file is parsed ONCE into a Skia picture, which then redraws at any zoom
/// in a millisecond or two because it is still vector.
public override void Paint(SKCanvas canvas, SKRect rect)
{
    EnsureLoaded();
    if (PaperColor.HasValue) { canvas.DrawRect(Rect, new SKPaint { Color = PaperColor.Value }); }
    SKPicture picture = _svg?.Picture;
    if (picture == null) { return; }
    SKRect cull = picture.CullRect;
    canvas.Save();
    canvas.Concat(Transform());
    canvas.Translate(-cull.Left, -cull.Top);
    canvas.DrawPicture(picture);
    canvas.Restore();
}

protected override LinkList GetLinks()
{
    EnsureLoaded();
    var links = new List<Link>();
    if (_svg != null && _svg.TryEnsureRetainedSceneGraph(out SvgSceneDocument scene)
        && scene?.Root != null)
    {
        SKRect cull = _svg.Picture?.CullRect ?? SKRect.Empty;
        if (cull.Width > 0 && cull.Height > 0) { CollectAnchors(scene.Root, cull, links); }
    }
    return new LinkList(links);
}

private static void CollectAnchors(SvgSceneNode node, SKRect cull, List<Link> links)
{
    if (node.Kind == SvgSceneNodeKind.Anchor
        && node.Element is CodeBrix.SvgParse.SvgAnchor anchor
        && !string.IsNullOrEmpty(anchor.Href))
    {
        Shim.SKRect b = node.TransformedBounds;
        if (b.Width > 0 && b.Height > 0)
        {
            links.Add(new Link(
                (b.Left - cull.Left) / cull.Width, (b.Top - cull.Top) / cull.Height,
                (b.Right - cull.Left) / cull.Width, (b.Bottom - cull.Top) / cull.Height,
                anchor.Href));
        }
    }
    if (node.Children == null) { return; }
    foreach (SvgSceneNode child in node.Children) { CollectAnchors(child, cull, links); }
}
```

The typeface seam is a one-member interface the host implements:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/libs/Fresco.Brix.MusicView/Pages/IScoreTypefaceResolver.cs
public interface IScoreTypefaceResolver
{
    SKTypeface Resolve(string familyName, SKFontStyleWeight weight,
        SKFontStyleWidth width, SKFontStyleSlant slant);
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/libs/Fresco.Brix.MusicView/Pages/SvgPage.cs
if (_typefaces != null)
{
    //The host's chain REPLACES the default provider rather than
    //standing in front of it: a family the host cannot answer must
    //draw tofu, not quietly find something on the machine.
    svg.Settings.TypefaceProviders.Clear();
    svg.Settings.TypefaceProviders.Add(new ResolverProvider(_typefaces));
}
```

**Where to look.**
`Fresco.Brix/src/libs/Fresco.Brix.MusicView/Pages/SvgPage.cs`,
`Fresco.Brix/src/libs/Fresco.Brix.MusicView/Pages/IScoreTypefaceResolver.cs`,
`Fresco.Brix/src/libs/Fresco.Brix.MusicView/Pages/ScorePage.cs`,
`Fresco.Brix/src/libs/Fresco.Brix.MusicView/Layout/Rectangles.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/MusicView/LilyPortTypefaceResolver.cs`.

**Sharp edges.**

- Replace the default typeface provider rather than chaining behind it, or a
  family your resolver cannot answer quietly picks up a system font. The rule
  here is that a missing glyph draws a box, which is visible, rather than a
  wrong font, which is not.
- The resolver must be idempotent. A defect recorded in
  `LilyPortTypefaceResolver` had a generic family resolve to a real family name
  that then matched nothing on the second pass and fell through to a
  last-resort face; it went unnoticed until the PDF export named the embedded
  faces out loud.
- A page's natural size taken from the SVG's rendered pixel viewport is rounded
  to whole pixels. The file also carries the exact size in millimeters on its
  root element, and that is the number a physical export needs.
- A page copy shares the parsed picture rather than reparsing, so the copy must
  never dispose what it borrowed.
- Re-recording a page to SVG through a canvas loses the anchors, because the
  anchors are not drawing. Copy the original file when you still have it.

### Move the caret from a click in a rendered document and back again

**When you want this.** Two-way navigation between a rendered artifact and the
source that produced it, that still works after the user has edited the source.

**The MVVM shape.** A parser for the link URL scheme; a per-document map from
line and column to text anchors, so the mapping survives edits; and delegates
on the panel that the page fills in, one going to the editor and one coming
back. A click is routed through the view model's navigation history rather than
moving the caret directly.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/MusicView/TextEditLink.cs
/// The engine writes <c>textedit://&lt;file&gt;:&lt;line&gt;:&lt;char&gt;:&lt;column&gt;</c>
/// — four fields, of which the THIRD is the one that matters: it is the 0-based
/// index of the character within the line, and the fourth is a display column
/// that counts a tab as several.
private static readonly Regex Pattern = new Regex(
    @"^textedit://(.*?):(\d+):(\d+)(?::\d+)$",
    RegexOptions.Compiled | RegexOptions.CultureInvariant);

public static bool TryParse(string url, out TextEditPlace place)
{
    place = default;
    if (string.IsNullOrEmpty(url)) { return false; }
    Match match = Pattern.Match(url);
    if (!match.Success) { return false; }
    if (!int.TryParse(match.Groups[2].Value, out int line)
        || !int.TryParse(match.Groups[3].Value, out int column)) { return false; }
    place = new TextEditPlace(Uri.UnescapeDataString(match.Groups[1].Value), line, column);
    return true;
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/MusicView/PointAndClick.cs
public BoundLinks(EditorDocument document,
    IReadOnlyDictionary<(int Line, int Column), List<object>> links)
{
    // ...
    foreach (var entry in links.OrderBy(kv => kv.Key.Line).ThenBy(kv => kv.Key.Column))
    {
        (int line, int column) = entry.Key;
        if (line < 1 || line > store.LineCount) { continue; }
        DocumentLine documentLine = store.GetLineByNumber(line);
        int offset = Math.Min(documentLine.Offset + column, documentLine.EndOffset);
        ITextAnchor anchor = store.CreateAnchor(offset);
        anchor.SurviveDeletion = true;
        // ...
    }
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs
_musicViewPanel = new MusicViewPanel(
    viewModel.Documents,
    viewModel.MusicViewActions,
    new LilyPortTypefaceResolver(),
    viewModel.Settings)
{
    //A click in the score is a JUMP, so it is remembered [...] which is
    //what puts an entry in the Back/Forward history.
    ShowCursor = (document, offset) => viewModel.Browser.GoTo(document, offset),
    CurrentEditorView = () => _viewManager?.ActiveView,
    OpenExternalUrl = OpenExternalFile,
    PickExportPathAsync = PickExportAsync,
    Report = message => viewModel.StatusText = message,
};
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/MusicView/TextEditLink.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/MusicView/PointAndClick.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/MusicView/CursorPositions.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Shell/MusicViewPanel.cs`,
`Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs` (`ShowMusicCursor`).

**Sharp edges.**

- Bind each source location to a text anchor with `SurviveDeletion = true`, or
  the mapping is wrong the moment the user types. A rendered artifact being
  older than its source is the ordinary case, not the exception.
- The reverse direction needs a guard flag so that scrolling the view to the
  caret does not immediately move the caret back.
- Route a click through the application's navigation history rather than moving
  the caret directly, so Back and Forward see the jump.
- Which link action a click means (jump, edit in place, nothing) depends on a
  modifier read from the keyboard source, not from the pointer event.

## Media, camera and vision

### Play a MIDI file and render one to WAV with the audio library

**When you want this.** In-process synthesis with a SoundFont or SFZ bank, with
transport controls, and an offline render of the same material to a file.

**The MVVM shape.** An interface the panel and the view model use; a service
that implements it over CodeBrix.Audio, opens no audio device until something
is loaded, captures its `SynchronizationContext` at construction, and posts
every event back through it.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Midi/IMidiPlayer.cs
public interface IMidiPlayer : IDisposable
{
    event EventHandler PositionChanged;
    event EventHandler StateChanged;
    event EventHandler PlaybackEnded;
    MidiPlayerState State { get; }
    bool HasSong { get; }
    string FileName { get; }
    MidiSong Song { get; }
    long TotalTime { get; }
    long CurrentTime { get; }
    double TempoFactor { get; set; }
    float Volume { get; set; }
    bool Load(string fileName, MidiSong song = null);
    void Play();
    void Pause();
    void Stop();
    void Seek(long milliseconds);
    void Clear();
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Midi/MidiPlayerService.cs
MidiSequence sequence = new MidiSequence(fileName);
MidiMusicPlayer player = EnsurePlayer();

if (string.Equals(
    Path.GetExtension(instrument), ".sfz", StringComparison.OrdinalIgnoreCase))
{
    player.Load(_sfzInstruments.Get(instrument), sequence);
}
else
{
    player.Load(_soundFonts.Get(instrument), sequence);
}

InstrumentPath = instrument;
FileName = fileName;
Song = song ?? SafeSong(fileName);
_totalTime = (long)player.Duration.TotalMilliseconds;
player.Speed = (float)_tempoFactor;
player.Volume = _volume;
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Export/AudioExport.cs
var sequence = new MidiSequence(midiPath);

if (string.Equals(
    Path.GetExtension(instrument), ".sfz", StringComparison.OrdinalIgnoreCase))
{
    using var cache = new SfzInstrumentCache();
    SoundFontRenderer.RenderToWavFile(
        cache.Get(instrument), sequence, wavPath, SampleRate, Tail);
}
else
{
    using var cache = new SoundFontCache();
    SoundFontRenderer.RenderToWavFile(
        cache.Get(instrument), sequence, wavPath, SampleRate, Tail);
}
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Midi/MidiPlayerService.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Midi/IMidiPlayer.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Midi/IMidiInputDevice.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Midi/SoundFonts.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Export/AudioExport.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Shell/MidiPanel.cs`.

**Sharp edges.**

- Build the service lazily. Nothing should touch the sound card until something
  is loaded, so a window whose MIDI panel is never opened never opens an audio
  device.
- The audio engine's callbacks arrive on its own real-time thread. Post every
  event the service raises back to the thread that built it.
- The sequence's duration is its event list, but the sequencer's clock keeps
  running while the final voices ring out. Clamp a reported position to the
  duration or it overshoots by the length of the release tail.
- Use the library's own end-of-playback event rather than comparing position
  against duration on a timer. The two are not interchangeable: the comparison
  fires at the last event, the event fires when the last voices stop, and
  ending at the last event cuts the release tail off anything that finishes on
  a held chord.
- Give an offline render its own bank cache, not the player's. An export may
  run while the panel is sounding, and sharing state across threads is worse
  than reading the bank twice.
- Append a tail to an offline render, or the release and reverb are truncated.
- Delete a half-written output file when a render fails. The alternative is a
  file the user double-clicks and learns nothing from.
- The bundled bank is a default, not a dependency: empty the folder and the
  application still runs on whatever file the user picks. Do not display a
  bank's own embedded name; it may not say what you think it says.

## Documents, data and web APIs

### Give each document a private scratch directory cleaned up at process exit

**When you want this.** You run something over a document that may be unsaved,
or whose output must not land beside the user's file.

**The MVVM shape.** A per-document extension object created lazily; the
directory itself is created on first need, inside one process-wide temporary
root that a process-exit handler removes.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Documents/ScratchDir.cs
/// <summary>Creates the temporary directory if it does not exist yet.</summary>
public void Create() => _directory ??= PathUtil.TempDir();
// ...
string baseName = document.Path == null
    ? null
    : System.IO.Path.GetFileName(document.Path);
if (string.IsNullOrEmpty(baseName))
{
    //A nameless document still needs a name with the right extension:
    //the engine decides how to read a file by its contents, but the
    //rest of the pipeline finds output by base name.
    string mode = DocumentInfo.For(document).Mode();
    baseName = "document"
        + (Modes.Extensions.TryGetValue(mode ?? string.Empty, out var extension)
            ? extension
            : ".ly");
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Services/PathUtil.cs
public static string TempDir()
{
    lock (TempGate)
    {
        if (_tempRoot == null)
        {
            _tempRoot = Path.Combine(
                Path.GetTempPath(), AppInfo.Name + "-" + Guid.NewGuid()/* ... */);
            Directory.CreateDirectory(_tempRoot);

            //Upstream registers an atexit hook; the CLR equivalent is the
            //process-exit event, and the same "never mind if it fails" rule
            //applies — a leftover temporary directory is not worth a crash.
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { Directory.Delete(_tempRoot, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            };
        }
    }
    string directory = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N").Substring(0, 12));
    Directory.CreateDirectory(directory);
    return directory;
}
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Documents/ScratchDir.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Services/PathUtil.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Documents/ResultFiles.cs`.

**Sharp edges.**

- Cleanup is best-effort and never throws. A leftover temporary directory is
  cheaper than a crash on exit.
- Keep the mapping from a scratch path back to the document it was made from.
  That is what lets an error message about a scratch copy land in the right
  editor tab.
- An unsaved document still needs a file name with the right suffix, because
  everything downstream finds output by base name.

### Turn tool diagnostics into clickable source locations that survive edits

**When you want this.** A compiler, linter or converter prints messages of the
form `file:line:column:` and you want them to be links back into the editor
that still point at the right place after the user has typed.

**The MVVM shape.** A per-document service parses each message, resolves the
file name against the job's own directory, and binds each location to a text
anchor in the open document. The log panel renders the matches as link spans;
the view model resolves a clicked reference (opening the file if it is not open
yet) and the page moves the caret it owns.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Engrave/EngraveErrors.cs
/// <remarks>
/// The trailing <c>(?=:)</c> is load-bearing: it requires the colon that
/// SEPARATES the location from the message, so a bare <c>file:12</c> in
/// prose is not mistaken for one. The column is optional because a
/// message about a whole line has none.
/// </remarks>
public static readonly Regex MessagePattern = new Regex(
    @"^((.*?):([1-9]\d*)(?::([1-9]\d*))?)(?=:)",
    RegexOptions.Multiline | RegexOptions.Compiled);
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Engrave/EngraveErrors.cs
public void Bind(EditorDocument document)
{
    if (document == null) { return; }
    _document = document;
    _anchor = document.Document.CreateAnchor(
        document.OffsetAtPosition(Line, Column));
    document.Closed += (_, _) => Unbind();
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/LogPanel.cs
private void WriteWithLinks(string message)
{
    int position = 0;
    foreach (Match match in EngraveErrors.MessagePattern.Matches(message))
    {
        // ...
        string url = match.Groups[1].Value;
        int start = _view.Document.TextLength;
        Append(shown, MessageType.StdErr, asLink: true);
        _errors.Add((start, _view.Document.TextLength, url));
        position = match.Index + match.Length;
    }
}
```

Acting on a click splits cleanly in two: opening a document, binding the
reference and making it current are view model work, and moving the caret is
view work. The sample does all of it on the page, so this block is recast, with
every statement kept as the sample has it:

```csharp
// Adapted from CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs
// The sample's ShowErrorReference opens the file, binds the reference and sets
// the current document from the page. Here the view model does that and
// answers with an offset, and the page only moves the caret.

// On the view model:
public int ResolveErrorReference(ErrorReference reference)
{
    if (reference == null) { return -1; }

    EditorDocument document = reference.Document;
    if (document == null)
    {
        //The file is not open yet — open it, which binds the reference.
        if (!File.Exists(reference.FileName)) { return -1; }

        _ = OpenPathAsync(reference.FileName);
        document = Documents.FindDocument(reference.FileName);
        if (document == null) { return -1; }

        reference.Bind(document);
    }

    Documents.CurrentDocument = document;

    //The anchor's offset is the truth once the document has been edited;
    //the reported line and column are only right for the text as engraved.
    return reference.Offset ?? document.OffsetAtPosition(
        reference.Line, reference.Column);
}

// On the page:
private void ShowErrorReference(ErrorReference reference)
{
    int offset = ViewModel?.ResolveErrorReference(reference) ?? -1;
    if (offset < 0) { return; }

    EditorView view = _viewManager?.ActiveView;
    if (view?.Document != reference.Document) { return; }

    var location = view.Editor.Document.GetLocation(offset);
    view.GoTo(location.Line, location.Column);
    view.FocusEditor();
}
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Engrave/EngraveErrors.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Shell/LogPanel.cs`,
`Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs` (`ShowErrorReference`).

**Sharp edges.**

- Resolve a relative file name against the job's directory, not the process's
  working directory. A tool commonly names its main input by base name, and the
  application's own working directory would send every message to a file that
  does not exist.
- A log opened while a job is running must replay the job's history before
  subscribing, or it starts blank.
- The editor marks its pointer events handled, so register the log's click
  handler with `handledEventsToo` and read the caret on the released event; on
  press the caret has not moved yet.
- Prefer the anchor's current offset over the reported line and column. The
  reported numbers are only right for the text as it was when the tool ran.

### Show PDF pages inside the application with PdfRasterizer

**When you want this.** You bundle documentation, or let the user open their
own PDFs, and there is no WebView anywhere in the application.

**The MVVM shape.** An application-side class rasterizes a page at a width and
hands the pixels to the paged view through the view library's page-image-source
interface, so the view library never learns what a PDF is. Outlines and link
annotations are read with a separate document library.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Documentation/PdfManual.cs
private async Task<SKImage> RasterizeAsync(string path, int number, int dpi)
{
    using Image raw = await _rasterizer
        .RasterizeToImage(path, pageNumber: number, dpi: dpi)
        .ConfigureAwait(false);
    if (raw == null) { return null; }

    // Straight from the rasteriser's pixels into Skia's, rather than out
    // through a PNG and back: the encode and decode would cost more than
    // the rendering did.
    using Image<Bgra32> bgra = raw.CloneAs<Bgra32>();
    byte[] pixels = new byte[(long)bgra.Width * bgra.Height * 4];
    bgra.CopyPixelDataTo(pixels);

    // Opaque: the rasteriser paints its own white background behind the
    // page, so there is no alpha to blend and saying so saves the blend.
    SKImageInfo info = new SKImageInfo(
        bgra.Width, bgra.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
    return SKImage.FromPixelCopy(info, pixels);
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Documentation/ManualOutline.cs
PdfDocument document = PdfReader.Open(path, PdfDocumentOpenMode.InformationOnly);
if (document.PageCount < 1) { return null; }

Dictionary<PdfPage, int> numbers = new Dictionary<PdfPage, int>();
List<PageSize> sizes = new List<PageSize>(document.PageCount);
for (int i = 0; i < document.PageCount; i++)
{
    PdfPage page = document.Pages[i];
    numbers[page] = i + 1;
    sizes.Add(new PageSize(page.Width.Point, page.Height.Point));
}

List<ManualOutlineEntry> entries = new List<ManualOutlineEntry>();
Walk(document.Outlines, 0, numbers, entries);
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Manuscripts/PdfLinks.cs
if (!string.Equals(annotation.Elements.GetName("/Subtype"), "/Link",
    StringComparison.Ordinal)) { continue; }
string url = UrlOf(annotation);          // reads /A << /S /URI /URI (...) >>
PdfRectangle rect = annotation.Elements.GetRectangle("/Rect");
var (left, top, right, bottom) = AreaOf((rect.X1, rect.Y1, rect.X2, rect.Y2), box, rotate);
links.Add(new Link(left, top, right, bottom, url));
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/ManuscriptViewerPanel.cs
private void OnLinkClicked(object sender, MusicLinkEventArgs e)
{
    if (e.Properties is { IsRightButtonPressed: true }) { return; }

    if (!TextEditLink.TryParse(e.Link.Url, out TextEditPlace place))
    {
        if (e.Link.IsExternal) { OpenExternalUrl?.Invoke(e.Link.Url); }
        return;
    }
    // ... otherwise move the caret, or edit in place with Shift
}
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Documentation/PdfManual.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Documentation/ManualOutline.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Documentation/ManualCatalog.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Documentation/ContextHelp.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Manuscripts/PdfLinks.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Shell/DocumentationPanel.cs`,
`Fresco.Brix/src/libs/Fresco.Brix.MusicView/Pages/RasterPage.cs`.

**Sharp edges.**

- A rasterized page is not a vector page. An SVG page parses once and redraws
  at any zoom, so it needs no cache and no render threads; a PDF page must be
  rasterized at a size by a native library, which is why a whole class exists
  for it.
- Bucket the render width. A zoom is a stream of slightly different widths, and
  rendering each one keeps a large document permanently busy; show the last
  rendering, scaled, in between.
- Never evict the newest arrival from the page cache, or a page larger than the
  whole budget is discarded the moment it arrives and asked for again forever.
- A page's width and height are the box as turned by its rotation entry, which
  is the displayed size, but an annotation's own coordinates are not turned.
  Skip the rotation step and links land on the wrong quarter of a rotated page.
- Neither library extracts text, so there is no find-on-page; the outline is
  what stands in for it. Internal destination links are not carried either,
  because the link type holds a URL and nothing else. Record both in comments
  rather than dropping them silently.
- Reading a large outline is slow enough to do once per document, off the UI
  thread, and keep.
- The application's own user guide takes the other road entirely: it parses
  markdown and draws it into `TextBlock`, `StackPanel` and `Hyperlink`
  controls, with internal links dispatched to a navigate delegate and external
  ones to the desktop. See
  `Fresco.Brix/src/Fresco.Brix.Core/UserGuide/GuideRenderer.cs`.

### Write a vector PDF with PdfDocCreate and the Html2Pdf add-on

**When you want this.** You have SVG content and need a PDF that stays vector,
with the exact fonts you drew with subset into the file.

**The MVVM shape.** A static writer in the rendering library, taking the pages,
a document-information object and a font bundle the host builds. Nothing in the
writer draws through Skia, and nothing in it knows where the fonts came from.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/libs/Fresco.Brix.MusicView/Export/ScorePdf.cs
private static HtmlPdfRenderer Renderer(
    double width, double height, ScorePdfInfo info, double rasterResolution)
{
    var renderer = new HtmlPdfRenderer();
    HtmlRenderOptions options = renderer.Options;
    options.PageWidthPoints = width;
    options.PageHeightPoints = height;
    // ...
    options.SvgPlacement = SvgPlacementMode.Vector;
    options.SvgRasterScale = Math.Clamp(rasterResolution / SvgPage.SvgDpi, 0.25, 8.0);

    //The house rule: a character no face covers draws tofu rather than
    //vanishing, so a gap is seen (feedback: never fall back to system fonts).
    options.KeepUncoveredCharacters = true;

    //The engine's faces have CFF outlines. Without this they would go into
    //the file whole [...] under a subset-style name; with it only the glyphs
    //the score uses are kept.
    options.CffSubsetMode = PdfCffSubsetMode.Sparse;
    return renderer;
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/libs/Fresco.Brix.MusicView/Export/ScorePdf.cs
if (fonts != null) { Html2PdfFonts.AddFontFiles(fonts.FontFiles, false); }
// ...
if (rotated)
{
    for (int i = 0; i < sources.Count && i < document.PageCount; i++)
    {
        if (sources[i].QuarterTurns != 0)
        {
            document.Pages[i].Rotate = sources[i].QuarterTurns * 90;
        }
    }
}
```

The host builds the font bundle by writing the engine's own faces out where the
PDF writer can register them:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/MusicView/LilyPortScorePdfFonts.cs
public static ScorePdfFonts Get()
{
    lock (Gate) { return _fonts ??= new ScorePdfFonts(Extract(DefaultDirectory()), MapFamily); }
}

public static string MapFamily(string familyName)
    => ChainFamilies[LilyPortTypefaceResolver.Normalize(familyName)];
```

**Where to look.**
`Fresco.Brix/src/libs/Fresco.Brix.MusicView/Export/ScorePdf.cs`,
`Fresco.Brix/src/libs/Fresco.Brix.MusicView/Export/PageExporter.cs`,
`Fresco.Brix/src/libs/Fresco.Brix.MusicView/Export/ImageExporter.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/MusicView/LilyPortScorePdfFonts.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Export/ScoreExport.cs`.

**Sharp edges.**

- Turn CFF subsetting on explicitly, or every face goes into the file whole.
- Keep uncovered characters, so a missing glyph is visible. The alternative is
  a character that silently vanishes from the output.
- The PDF writer is where a font problem becomes visible, because a PDF names
  the faces it embedded. Two similar faces will not be told apart on screen.
- Export the engine's own SVG file by copying it when you still have it. A
  re-recording through a canvas loses the anchors and the element structure.
- Page rotation is applied to the written document afterwards, page by page.

### Convert a file through a library in process and apply the result as one undo step

**When you want this.** Import and upgrade commands that used to shell out to a
command-line tool.

**The MVVM shape.** A job type that runs the conversion off the UI thread and
posts its messages through the same job channel every other background task
uses; an options object built from the dialog's checkbox state; and, for an
in-place rewrite, a single `Replace` over the whole document so the user can
undo it once.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Import/ImportJob.cs
private ImportResult Convert()
{
    switch (_format)
    {
        case ImportFormat.MusicXml when ImportFormats.IsCompressedMusicXml(_inputPath):
            return MusicXmlImporter.ImportCompressed(
                File.ReadAllBytes(_inputPath), _options as MusicXmlImportOptions);

        case ImportFormat.MusicXml:
            return MusicXmlImporter.Import(
                ReadXmlText(_inputPath), _options as MusicXmlImportOptions);

        case ImportFormat.Midi:
            return MidiImporter.Import(
                File.ReadAllBytes(_inputPath), _options as MidiImportOptions);

        default:
            return AbcImporter.Import(
                ReadPlainText(_inputPath), _options as AbcImportOptions);
    }
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Import/ImportSettings.cs
public override object ToOptions(string sourceName)
    => new MusicXmlImportOptions
    {
        SourceName = sourceName ?? string.Empty,
        PitchMode = AbsoluteMode ? MusicXmlPitchMode.Absolute : MusicXmlPitchMode.Relative,
        NoArticulationDirections = !ImportArticulationDirections,
        NoRestPositions = !ImportRestPositions,
        NoPageLayout = !ImportPageLayout,
        NoBeaming = !ImportBeaming,
        Midi = !CommentOutMidi,
        Language = Language,
    };
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/ConvertLyDialog.cs
bool hasDeclared = DocumentConverter.TryReadDeclaredVersion(
    text, out ConversionVersion declared, out bool malformed);
// ...
result = DocumentConverter.Convert(text, from, to);   // ConversionResult
// ...
IReadOnlyList<DiffRow> rows = TextDiff.Compare(text, result.Text);
```

```csharp
// Adapted from CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs
// The sample runs this from the page; in the MVVM shape the view model owns
// the command and asks the page for the dialog through IWindowBridge.
/// Upstream selects the whole document and replaces it [...] the same thing
/// here is one Replace over the whole range, which is also ONE undo step —
/// so a user who does not like the result presses Ctrl+Z once.
string converted = outcome.Text;
if (outcome.CopyMessages && outcome.Messages.Count > 0)
{
    converted += "\n\n%{\n" + string.Join("\n", outcome.Messages).Trim('\n') + "\n%}\n";
}
if (string.Equals(converted, text, StringComparison.Ordinal)) { return; }
view.Editor.Document.Replace(0, view.Editor.Document.TextLength, converted);
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Import/ImportJob.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Import/ImportSettings.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Import/ImportFormats.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Shell/ImportDialog.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Shell/ConvertLyDialog.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Tools/TextDiff.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Export/MusicXmlExport.cs`.

**Sharp edges.**

- A library takes a string where a command-line tool took a path, so your code
  has to honor the encoding declaration inside the file: a byte-order mark
  first, then the declared encoding, then UTF-8. `ReadAllText` is not enough.
- "The conversion succeeded" and "there were no warnings" are different
  questions. Test whether a document came out, not whether the error count was
  zero: a partly understood input still produces output worth opening.
- Show a diff before applying an in-place conversion, and apply it as one edit.
- Refuse to write output that would not conform, rather than writing it anyway.
  The MusicXML exporter here returns a refusal with a message when there is
  nothing convertible, and its comment is explicit that the refusal is a
  precondition check while the schema validation belongs in the tests.

## Settings and persistence

### Put the AppSettings add-in behind one facade

**When you want this.** Many places in your application read and write settings
and you do not want any of them to know which store they are talking to.

**The MVVM shape.** One sealed facade class registered as a singleton with
`SimpleServiceResolver`. It is the only file that names the add-in's types. View
models and services take it as a constructor argument or resolve it with
`GetService<SettingsStore>()`. A second constructor takes a directory, so tests
get a store of their own and no test can reach the real one.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Services/SettingsStore.cs
using CodeBrix.Platform.AppSettings;
// ...
public sealed class SettingsStore : IDisposable
{
    public const string AppName = "Fresco.Brix";
    private readonly AppSettingsStore _store;
    private readonly bool _ownsStore;

    /// <summary>
    /// Opens (creating if needed) the store in the add-in's default per-user
    /// location. Every caller in the running application shares ONE store [...]
    /// so the add-in's start-up backup and pruning pass happens once per
    /// process rather than once per opener.
    /// </summary>
    public SettingsStore()
    {
        lock (InitializeLock)
        {
            if (!AppSettingsService.IsInitialized)
            {
                AppSettingsService.Initialize(AppName);
            }
        }

        _store = AppSettingsService.Store;
        _ownsStore = false;
    }

    /// <summary>
    /// Opens (creating if needed) a store of its own in a directory of its own
    /// — the seam tests use, so that no test ever touches the real store.
    /// </summary>
    public SettingsStore(string directoryPath)
    {
        _store = new AppSettingsStore(AppName, directoryPath);
        _ownsStore = true;
    }
    // ...
    public void Dispose()
    {
        if (_ownsStore) { _store.Dispose(); }
    }
}
```

The accessors are deliberately two-layered: scalars in a stable text encoding,
and whole collections as typed JSON under one key each.

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Services/SettingsStore.cs
public string GetString(string key, string defaultValue = null)
{
    if (string.IsNullOrEmpty(key)) { return defaultValue; }
    return _store.Get(key, defaultValue);
}

public void SetString(string key, string value)
{
    if (string.IsNullOrEmpty(key)) { return; }
    _store.Set(key, value);
}

public bool GetBool(string key, bool defaultValue = false)
{
    string text = GetString(key);
    return text == null ? defaultValue : text == "1" || text == "true";
}

public void SetBool(string key, bool value) => SetString(key, value ? "1" : "0");

public int GetInt(string key, int defaultValue = 0)
{
    string text = GetString(key);
    return text != null
        && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
        ? value
        : defaultValue;
}

/// <remarks>The add-in answers the type's default rather than throwing when
/// what is stored is not of the type asked for, so a family key is never
/// shared with a scalar one.</remarks>
public T Get<T>(string key) { /* ... */ return _store.Get<T>(key); }
public void Set<T>(string key, T value) { /* ... */ _store.Set(key, value); }

public void Remove(string key) => SetString(key, null);
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Services/SettingsStore.cs`,
`Fresco.Brix/src/Fresco.Brix.UI/App.xaml.cs` (the singleton registration),
`Fresco.Brix/tests/Fresco.Brix.Core.Tests/SettingsStoreTests.cs`.

**Sharp edges.**

- The add-in owns the file lifecycle: a timestamped automatic backup with
  retention pruning on every start, quarantine of a corrupt database with
  restore from the newest good backup, and silent first-run creation. Do not
  re-implement any of it.
- The add-in has no prefix-scan API by design, so a "family" of keys that used
  to be a subtree becomes one key holding a list or a dictionary. Several
  things in this application are stored that way: the action collections, the
  snippet library, the session store, per-document meta-information, the color
  schemes and the recent-file list.
- Because the typed getter silently answers the default when the stored JSON is
  not of the type asked for, never read one key both as a scalar and as a
  family.
- Disposing the facade must not close a store it did not open, or the
  single-instance check that reads one setting before startup would take the
  application's store with it.
- Make the store's registration a singleton. One store per process is what
  makes the add-in's start-up pass run once.

### Persist preference pages and named sessions through that one store

**When you want this.** A preferences dialog with several pages, and named
workspaces the user can switch between.

**The MVVM shape.** Every page's values are an object implementing a
two-method interface; the page builds its controls lazily, loads on first
build, and saves only if it was built and touched. A session is one record in
one JSON-valued key, keyed by a generated group name so renaming never moves
data.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Preferences/PreferenceValues.cs
public interface IPreferenceValues
{
    void Load(SettingsStore settings);
    void Save(SettingsStore settings);
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Preferences/PreferencesPage.cs
public UIElement Panel()
{
    if (_content != null) { return _content; }
    _content = Build();
    LoadSettings();
    HasChanges = false;
    return _content;
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Preferences/PreferencesDialog.cs
public void SaveSettings()
{
    foreach (var page in _pages)
    {
        if (!page.IsBuilt || !page.HasChanges) { continue; }
        page.SaveSettings();
        page.HasChanges = false;
    }
    if (_dialog != null) { _dialog.IsSecondaryButtonEnabled = false; }
    SettingsChanged?.Invoke(this, EventArgs.Empty);
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Sessions/SessionStore.cs
public sealed class SessionData
{
    public IReadOnlyList<string> Paths { get; set; } = Array.Empty<string>();
    public int ActiveIndex { get; set; } = -1;
    public bool AutoSave { get; set; } = true;
    public string BaseDirectory { get; set; }
    public IReadOnlyList<string> IncludePath { get; set; } = Array.Empty<string>();
    public IReadOnlyList<string> Manuscripts { get; set; } = Array.Empty<string>();
    public int ActiveManuscript { get; set; } = -1;
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Sessions/SessionStore.cs
public void Write(string name, SessionData data)
{
    if (_settings == null) { return; }
    Dictionary<string, StoredSession> stored = ReadStored();
    string group = GroupOf(stored, name) ?? CreateGroup(stored, name);
    if (group == null) { return; }
    StoredSession session = stored[group];
    session.Urls = new List<string>(data.Paths ?? Array.Empty<string>());
    session.ActiveIndex = data.ActiveIndex >= 0 ? data.ActiveIndex : -1;
    // ...
    WriteStored(stored);
    SessionsChanged?.Invoke(this, EventArgs.Empty);
}
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Preferences/` (the dialog, the page base,
`PreferenceValues.cs` and the pages),
`Fresco.Brix/src/Fresco.Brix.Core/Sessions/SessionStore.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Sessions/SessionManager.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Commands/ActionCollection.cs` (shortcut
schemes are stored the same way).

**Sharp edges.**

- Controls round-trip through the values object, and the values object
  round-trips through the store. Never bind a control straight to a store key.
- Writing a numeric setting on every keystroke rewrites the box under the
  caret. Write the tidy form on `LostFocus` instead.
- Storing an empty list is meaningful: it means the user removed the default,
  which is not the same as never having customized the value.
- Restoring a session has an ordering rule. Closing every document also closes
  everything attached to them, so anything a session restores alongside its
  documents must be restored after the close, not before.
- Save a page only if it was built and touched. A page the user never opened
  has nothing to write, and writing it anyway would overwrite a value another
  part of the application changed.

## Text editing

### Bridge a platform-free document model onto the editor text document

**When you want this.** You have a library with its own document abstraction (a
parser, a set of transforms) and want it to operate on the live editor document
rather than on a copy.

**The MVVM shape.** One adapter class in the application implements the
library's document base over the add-in's `TextDocument`. The adapter lives in
the application, not in the library, so the library keeps its independence.
Every transform in the library then works on the open document unchanged, and
each batch of edits becomes one undo step.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Documents/AteLyDocument.cs
/// <summary>
/// THE bridge (the plan's §5.2): implements the ported ly.document API over the
/// editor's live <see cref="TextDocument"/>, so every ported ly tool — pitch,
/// rhythm, convert-ly, reformat — operates on the open document unchanged [...]
/// Blocks are the editor's own lines; tokens come from the one shared
/// tokenization (<see cref="LyHighlighter"/>); applied changes go through the
/// editor document, one undo group per batch.
/// </summary>
public class AteLyDocument : DocumentBase
{
    private readonly ConditionalWeakTable<DocumentLine, LineBlock> _blocks
        = new ConditionalWeakTable<DocumentLine, LineBlock>();

    public AteLyDocument(TextDocument document, LyHighlighter highlighter)
    {
        TextDocument = document ?? throw new ArgumentNullException(nameof(document));
        Highlighter = highlighter ?? throw new ArgumentNullException(nameof(highlighter));
    }

    public override int Count => TextDocument.LineCount;
    public override string PlainText() => TextDocument.Text;
    public override DocumentBlock GetBlock(int position)
        => position >= 0 && position <= TextDocument.TextLength
            ? Wrap(TextDocument.GetLineByOffset(position))
            : null;
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Documents/AteLyDocument.cs
protected override void ApplyChanges()
{
    // One undo group per batch [...] Changes arrive sorted with starts
    // DESCENDING (the base's contract), so earlier offsets stay valid while
    // later ones are replaced.
    TextDocument.BeginUpdate();
    try
    {
        foreach ((int start, int? end, string text) in ChangesList)
        {
            int changeEnd = end ?? TextDocument.TextLength;
            TextDocument.Replace(start, changeEnd - start, text);
        }
    }
    finally
    {
        TextDocument.EndUpdate();
    }
}
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Documents/AteLyDocument.cs`,
`Fresco.Brix/src/libs/Fresco.Brix.Ly/DocumentBase.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Documents/DocumentEditorState.cs`.

What the library itself looks like, and why it has no UI dependency to bridge
around, is in
[Keep a ported library completely free of the UI framework](#keep-a-ported-library-completely-free-of-the-ui-framework).

**Sharp edges.**

- Apply a batch of edits from the highest offset down, or every edit after the
  first is applied at the wrong place.
- `BeginUpdate()` and `EndUpdate()` are what make a multi-edit transform one
  Ctrl+Z. A whole-document rewrite is simpler still: one `Replace(0,
  TextLength, text)` call is already one undo step.
- Hold per-line state in a `ConditionalWeakTable` keyed by the editor's own
  line objects, so the adapter adds no lifetime of its own.

### Attach a language-aware highlighter to the text editor add-in

**When you want this.** Your editor must color by grammar rather than by
regular expression, and folding, matching, completion and an outline must all
agree with what the highlighter saw.

**The MVVM shape.** One highlighter per document, not per view, implementing
the add-in's highlighter and line-tracker interfaces. Each view adds a
colorizer over that one highlighter, so split views share the tokenization. The
per-document state object that owns the highlighter is what the view model and
every tool go through.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Editor/LyHighlighter.cs
public sealed class LyHighlighter : ILineTracker, IHighlighter
{
    private readonly TextDocument _document;
    private readonly WeakLineTracker _weakLineTracker;
    // ...
    public LyHighlighter(TextDocument document, string mode = null, ITokenStyler styler = null)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
        _styler = styler ?? new DefaultTokenStyler();
        _mode = mode;
        _weakLineTracker = WeakLineTracker.Register(document, this);
        InvalidateStates();
    }
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Documents/DocumentEditorState.cs
Highlighter = new LyHighlighter(document.Document);
LyDocument = new AteLyDocument(document.Document, Highlighter);
Styler = new SchemeTokenStyler(
    new TextFormatData(TextFormatData.CurrentScheme(settings), settings));
Highlighter.Styler = Styler;
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/EditorView.cs
//Parity highlighting: the shared ly.lex tokenization drawn through the
//editor's colorizer pipeline.
Editor.TextArea.TextView.LineTransformers.Add(
    new HighlightingColorizer(state.Highlighter));
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Editor/LyHighlighter.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Editor/DefaultTokenStyler.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Editor/SchemeTokenStyler.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Editor/TextFormats.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Documents/DocumentEditorState.cs`.

**Sharp edges.**

- Register the highlighter as a weak line tracker. That is what keeps the
  per-line token cache in step with edits without holding the document alive.
- Guard against re-entrancy explicitly. A highlighting pass that triggers
  another one should throw rather than corrupt its state.
- Color schemes come from the application's own format data, and the comment
  there records that there is no system-font fallback anywhere: the Fonts and
  Colors page offers only the faces the application ships.

### Fold match pairs and auto-indent from the same tokenization

**When you want this.** Folding that does not open on a brace inside a string,
and matching-pair navigation that agrees with the highlighter.

**The MVVM shape.** A folding strategy built over the shared highlighter,
installed per view through the add-in's folding manager; a static matcher over
the document bridge; an indenter driven from the same tokens.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Editor/LyFoldingStrategy.cs
public IEnumerable<NewFolding> CreateFoldings(TextDocument document)
{
    List<NewFolding> foldings = new List<NewFolding>();
    Stack<int> starts = new Stack<int>();

    for (int lineNumber = 1; lineNumber <= document.LineCount; lineNumber++)
    {
        DocumentLine line = document.GetLineByNumber(lineNumber);
        foreach (var token in TokenIter.Tokens(_highlighter, lineNumber))
        {
            int offset = line.Offset + token.Pos;
            if (token is IIndent || token is BlockCommentStart)
            {
                starts.Push(offset + token.Text.Length);
            }
            else if (token is IDedent || token is BlockCommentEnd)
            {
                if (starts.Count == 0) { continue; }
                int start = starts.Pop();
                int end = offset;
                //A region that never leaves its line has nothing to hide.
                if (document.GetLineByOffset(start).LineNumber
                    < document.GetLineByOffset(end).LineNumber)
                {
                    foldings.Add(new NewFolding { StartOffset = start, EndOffset = end });
                }
            }
        }
    }
    return foldings.OrderBy(f => f.StartOffset).ToList();
}

public void UpdateFoldings(FoldingManager manager, TextDocument document)
{
    if (manager == null || document == null) { return; }
    //-1: nothing is known to be broken, so every existing fold may keep
    //its open/closed state.
    manager.UpdateFoldings(CreateFoldings(document), -1);
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/EditorView.cs
_foldingManager = FoldingManager.Install(Editor.TextArea);
// ...
Editor.Document.TextChanged += (_, _) => RefreshFoldings();
// ...
Editor.TextArea.Caret.PositionChanged += (_, _) =>
{
    UpdateMatchHighlight();
    UpdateCurrentLineHighlight();
    // ...
};
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Editor/LyFoldingStrategy.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Editor/TokenMatcher.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Editor/Indenting.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Editor/ViewHighlighter.cs`.

**Sharp edges.**

- Passing -1 as the "first broken offset" is what preserves every fold's open
  and closed state across a refresh.
- Name your highlight groups and draw the lowest priority first, so a specific
  highlight (a match) covers a broad one (the current line) rather than
  fighting it.
- Fold regions that never leave their line are worth skipping: they cost a
  marker and hide nothing.

### Show two editor views over one document

**When you want this.** Split views that agree with each other, including their
highlighting, folding and every language tool.

**The MVVM shape.** One per-document state object holds everything shared: the
text document, the highlighter, the document bridge and the folding strategy.
Each view is a new editor control pointed at that same text document, with its
own caret, selection, colorizer instance, background renderer and folding
manager.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Documents/DocumentEditorState.cs
/// This is what makes split views work: two editors showing the same document
/// share this object, so they share the token cache and every ported ly tool
/// sees the same document whichever view is focused.
public sealed class DocumentEditorState : Plugin<EditorDocument, DocumentEditorState>
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/EditorView.cs
Editor = new AdvancedTextEdit
{
    //The SAME text store the other views use: this is the whole point.
    Document = document.Document,
    ShowLineNumbers = true,
    // ...
};
// ...
Editor.TextArea.TextView.LineTransformers.Add(
    new HighlightingColorizer(state.Highlighter));   // shared tokenization
Highlighter = new ViewHighlighter(Editor.TextArea.TextView);   // per-view paint
_foldingManager = FoldingManager.Install(Editor.TextArea);      // per-view folds
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs
//The editor area. Every view of a document shares that document's
//tokenization, which is what makes split views agree with each other.
_viewManager = new ViewManager(
    document => DocumentEditorState.For(document, viewModel.Settings),
    viewModel.ViewActions,
    EditorFontFamily());
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Documents/DocumentEditorState.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Shell/EditorView.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Shell/ViewManager.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Shell/SplitContainer.cs`.

**Sharp edges.**

- A per-owner extension object is built once, by whichever caller asks first.
  A defect recorded in `DocumentEditorState` came from exactly that: a caller
  that passed no settings store won the race, and the document had no
  meta-information for its whole life. The fix is a process-wide default so the
  answer does not depend on who asks first.
- A view that is not yet in the live visual tree answers false to `Focus()` and
  stays unfocused, so route a newly built view's focus request through a small
  helper that replays it on `Loaded`.

### Offer context-aware autocompletion in the editor

**When you want this.** Completion that knows where the caret is in the grammar
and that includes identifiers defined by the document itself.

**The MVVM shape.** A plain completer holding the view; an analyzer that maps
the lexer's current parser type to an ordered list of candidate models; a
harvest that reads identifiers off the shared tokenization; and a short-lived
cache so typing does not stutter.

**Code.**

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Completion/Completer.cs
public void ShowCompletionPopup(bool forced)
{
    if (_view == null) { return; }
    TextArea textArea = _view.Editor.TextArea;
    TextDocument store = _view.Editor.Document;
    int caret = textArea.Caret.Offset;
    DocumentLine line = store.GetLineByOffset(caret);

    CompletionResult result = _analyzer.Completions(_view.Document, caret);
    if (!result.HasCompletions) { Close(); return; }

    int start = Math.Clamp(line.Offset + result.Column, line.Offset, caret);
    string prefix = store.GetText(start, caret - start);
    if (!forced && (!AutoComplete || prefix.Length < AutoCompleteLength)) { return; }
    // ...
    _window = new CompletionWindow(textArea)
    {
        StartOffset = start,
        EndOffset = caret,
        CloseAutomatically = true,
        CloseWhenCaretAtBeginning = true,
    };
    _window.CompletionList.IsFiltering = true;
    foreach (var entry in result.Model.Entries)
    {
        _window.CompletionList.CompletionData.Add(new LyCompletionItem(entry));
    }
    _window.Closed += (_, _) => _window = null;
    _window.Show();
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Completion/CompletionAnalyzer.cs
Parser parser = _state.CurrentParser();
if (parser == null || !Tests.TryGetValue(parser.GetType(), out var tests))
{
    return CompletionResult.None;
}
foreach (var test in tests)
{
    CompletionModel model = test(this);
    if (model is { Count: > 0 }) { return new CompletionResult(Column, model); }
}
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Completion/Completer.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Completion/CompletionAnalyzer.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Completion/CompletionModel.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Completion/CompletionHarvest.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Completion/LyCompletionItem.cs`.

**Sharp edges.**

- The completion window does not arrive already narrowed to what the user has
  typed, and templating its list repopulates it from the unfiltered data. Apply
  the filter once on construction and again on `Loaded`, and pass an empty
  query first, because the selection call short-circuits on a query it has
  already been given.
- Cache the harvest by method rather than by caret position, so a caret that
  moved a little inside the cache window gets the same answer. That is a
  deliberate trade against rebuilding the list on every keystroke.
- Drive the candidate list from the lexer's current parser type. A completion
  list that ignores where the caret is in the grammar offers the wrong things
  everywhere.

## Testing

### Set up test projects on the Microsoft Testing Platform and check a port against recorded answers

**When you want this.** A test setup for a CodeBrix.Platform application, and a
way to test a port against the thing it was ported from rather than against
itself.

**The MVVM shape.** Not applicable. Test projects are self-executing binaries;
fixtures are files copied to the output; the recordings are made by programs
that are deliberately not in the solution and ship nothing.

**Code.**

```xml
<!-- From CodeBrix.Samples.Gpl3/Fresco.Brix/tests/Fresco.Brix.Core.Tests/Fresco.Brix.Core.Tests.csproj -->
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <!-- xUnit.net v3 test projects are self-executing binaries and
         must build as Exe; run via Microsoft.Testing.Platform,
         matching the CodeBrix family test convention. -->
    <OutputType>Exe</OutputType>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
    <!-- The published Platform nuget ships REF assemblies; this lever swaps in
         the real implementations so anything that does call Platform code
         works instead of throwing "Ref assembly". -->
    <CodeBrixRuntimeIdentifier>skia</CodeBrixRuntimeIdentifier>
  </PropertyGroup>

  <!-- Parity fixtures recorded from Frescobaldi's own code -->
  <ItemGroup>
    <None Include="fixtures\**" CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <ItemGroup>
    <!-- Skia's native library: the Music View's typeface chain loads real font
         faces, which is Skia work in a host-free process. -->
    <PackageReference Include="SkiaSharp.NativeAssets.Linux" Version="..." />
    <!-- plus the xUnit, test SDK and assertion packages -->
  </ItemGroup>
```

```json
// From CodeBrix.Samples.Gpl3/Fresco.Brix/global.json
{
    "test": {
        "runner": "Microsoft.Testing.Platform"
    }
}
```

A parity test reads the answers the original produced and asserts the port
against them, one case per recorded probe:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/tests/Fresco.Brix.Core.Tests/DocumentVariablesParityTests.cs
/// <summary>
/// <see cref="DocumentVariables"/> against Frescobaldi's own scanner:
/// <c>fixtures/variables.json</c> holds what upstream's <c>variables()</c>
/// answered for each probe document (regenerate with
/// <c>tools/varprobe/gen-variables-fixtures.py</c>, which lifts the pure
/// functions straight out of the read-only checkout and runs them). Nothing
/// here is recorded from the port's own output.
/// </summary>
public class DocumentVariablesParityTests
{
    private static string FixturePath()
        => Path.Combine(AppContext.BaseDirectory, "fixtures", "variables.json");

    public static IEnumerable<object[]> ProbeNames()
    {
        using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(FixturePath()));
        return fixture.RootElement.EnumerateObject()
            .Select(p => new object[] { p.Name })
            .OrderBy(n => (string)n[0], StringComparer.Ordinal)
            .ToList();
    }

    [Theory]
    [MemberData(nameof(ProbeNames))]
    public void the_variables_match_frescobaldi(string name)
    {
        //Arrange
        // ...
    }
}
```

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/InternalsVisibleTo.cs
[assembly: InternalsVisibleTo("Fresco.Brix.Core.Tests")]
```

**Where to look.**
the three csprojs under `Fresco.Brix/tests/`,
`Fresco.Brix/global.json`,
`Fresco.Brix/src/Fresco.Brix.Core/InternalsVisibleTo.cs`,
`Fresco.Brix/tests/Fresco.Brix.Core.Tests/SettingsStoreTests.cs` (a fixture
that opens a store in its own scratch directory),
`Fresco.Brix/tools/varprobe/gen-variables-fixtures.py`,
`Fresco.Brix/tools/scorewizprobe/qtshim.py`,
`Fresco.Brix/tools/midiprobe/gen-midi-fixtures.py`.

**Sharp edges.**

- A plain `dotnet test` against a Microsoft.Testing.Platform executable can
  report that zero tests ran. Run the built binary directly, or pass
  `--project` or `--solution`.
- Set `CodeBrixRuntimeIdentifier=skia` in any test project that calls platform
  code, or the reference assemblies throw.
- A test project that renders needs its own native Skia package. The heads get
  theirs from their runtime package; a host-free test process has to bring its
  own.
- A test project over a library that references no platform package needs
  neither of those, which is the payoff of keeping that library platform-free.
- Give every test its own scratch directory so no test can reach the real
  settings store. The settings facade's directory constructor exists for
  exactly that.
- Copy fixtures with `PreserveNewest` and load them from
  `AppContext.BaseDirectory`, not from a path relative to the source tree.
- The oracle programs are the interesting part and the techniques transfer. One
  lifts the pure functions out of an upstream module by walking its syntax tree,
  because that module imports a GUI toolkit that is not installed; another
  installs a shim standing in for the widget library so widget-shaped upstream
  code runs unchanged; another runs each input in its own subprocess under a
  timeout and records "not answered" with a reason, because a hang is not an
  answer and a shorter fixture would be a dishonest one.
- Generated data files each name the tool that regenerates them in a header
  comment, so nobody edits the output by mistake.

### Assert a toolbar's contents and a shell's pane arithmetic in host-free tests

**When you want this.** Coverage of the parts of a window that are ordinarily
hardest to test: what is on a toolbar and in what order, what the buttons are
bound to, and which panes a dock shell opens for a given set of visible panels.

**The MVVM shape.** Two seams make all of it reachable. The first is DATA: what
is on each bar and in what order is a list built by a static class that names no
XAML type, so the order is a value a test can compare. The second is a PURE
class: which panes are meant to be open is decided by static functions over
booleans, in the Core library, with the class that owns the controls reduced to
a driver over their answers. Where a test does need real elements, the platform
builds them in a process with no window, so a bar can be constructed, measured
and inspected without a head.

**Code.** The order is asserted against the entries, not against the screen. A
small projection turns the list into something readable, and the expectation is
written out entry for entry:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/tests/Fresco.Brix.Core.Tests/MainToolbarTests.cs
private static IEnumerable<string> Shape(IReadOnlyList<ToolbarEntry> entries)
    => entries.Select(entry => entry.Kind switch
    {
        ToolbarEntryKind.Separator => "|",
        ToolbarEntryKind.Widget => "<" + entry.Widget + ">",
        _ => entry.Action.Name,
    });

[Fact]
public void the_main_toolbar_is_upstreams_own_order()
{
    //Arrange, Act
    IReadOnlyList<ToolbarEntry> entries = MainBar(verbose: false);

    //Assert — mainwindow.createToolBars, entry for entry and separator for
    //separator.
    Shape(entries).Should().BeEquivalentTo(new[]
    {
        "file_new", "file_open", "file_save", "file_close",
        "|", "go_back", "go_forward",
        "|", "edit_undo", "edit_redo",
        "|", "scorewiz", "engrave_runner",
    });
}
```

The elements themselves are built too, in the same process. The one thing to
work around is that a tray builds its bars when it enters a tree and nothing
here enters a tree, so the test asks for the rebuild the same way a preference
change does:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/tests/Fresco.Brix.Core.Tests/MainToolbarTests.cs
[Collection(XamlTestCollection.Name)]
public class MainToolbarBuilderTests : IDisposable
// ...
    //The window builds the bars when the tray enters the tree; nothing
    //enters a tree here, so the same rebuild is asked for directly.
    toolbar.SettingsChanged();
    return toolbar;
```

With the bars built, the data and the elements are checked against each other,
which is what keeps the two from drifting apart:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/tests/Fresco.Brix.Core.Tests/MainToolbarTests.cs
[Fact]
public void the_main_bar_holds_one_item_per_layout_entry()
{
    //Arrange
    MainToolbar toolbar = Build();
    IReadOnlyList<ToolbarEntry> entries = ToolbarLayout.Main(
        _main, _browser, _scoreWizard, _engrave, verboseToolButtons: false);

    //Act
    IReadOnlyList<UIElement> items = Items(Bars(toolbar)[0]);

    //Assert — the order model and the bar say the same thing, entry for
    //entry: a separator entry is a separator, an action entry is a button.
    items.Count.Should().Be(entries.Count);
    for (int index = 0; index < entries.Count; index++)
    {
        if (entries[index].Kind == ToolbarEntryKind.Separator)
        {
            items[index].Should().BeOfType<ToolBarSeparator>();
            continue;
        }

        ToolButton button = items[index].Should().BeAssignableTo<ToolButton>().Subject;
        button.Command.Should().BeSameAs(entries[index].Action);
    }
}
```

Even the overflow chevron is reachable, because a measure pass is all it takes
to make a bar too narrow for its items:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/tests/Fresco.Brix.Core.Tests/MainToolbarTests.cs
[Fact]
public void a_narrow_bar_pushes_its_trailing_items_behind_the_chevron()
{
    //Arrange — the behaviour that replaces the hand-built row's hidden
    //horizontal scrollbar.
    TestHost.EnsureReady();
    MainToolbar toolbar = Build();
    ToolBar mainBar = Bars(toolbar)[0];

    //Act
    mainBar.Measure(new Size(1200d, 100d));
    bool fitsWide = mainBar.HasOverflowItems;
    mainBar.Measure(new Size(90d, 100d));

    //Assert
    fitsWide.Should().BeFalse();
    mainBar.HasOverflowItems.Should().BeTrue();
    mainBar.OverflowItems.Should().NotBeEmpty();
}
```

The shell's arithmetic is asserted against the pure class, with no control
anywhere. Two arrangements that should look the same are compared with each
other rather than with a literal, and the invariant the controls impose is
stated as a property over every case:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/tests/Fresco.Brix.Core.Tests/ShellLayoutTests.cs
[Fact]
public void maximizing_the_editor_is_the_same_picture_as_closing_every_tool()
{
    //Act
    ShellPanes maximized = ShellLayout.PanesForMaximized(ShellRegion.Editor);
    ShellPanes nothingOpen = ShellLayout.PanesFor(
        leftHasPanel: false, rightHasPanel: false, bottomHasPanel: false);

    //Assert
    //Which is what makes the editor's case reachable without a command of
    //its own: close every tool panel and the editor has the window.
    maximized.EditorIsOpen.Should().Be(nothingOpen.EditorIsOpen);
    maximized.EditorBlockIsOpen.Should().Be(nothingOpen.EditorBlockIsOpen);
    maximized.LeftStripIsOpen.Should().Be(nothingOpen.LeftStripIsOpen);
    maximized.RightStripIsOpen.Should().Be(nothingOpen.RightStripIsOpen);
    maximized.BottomStripIsOpen.Should().Be(nothingOpen.BottomStripIsOpen);
}

[Theory]
[InlineData(ShellRegion.Left)]
[InlineData(ShellRegion.Right)]
[InlineData(ShellRegion.Bottom)]
[InlineData(ShellRegion.Editor)]
public void a_maximize_never_asks_a_control_to_shut_its_last_pane(ShellRegion region)
{
    //Act
    ShellPanes panes = ShellLayout.PanesForMaximized(region);

    //Assert
    panes.OuterKeepsAPaneOpen.Should().BeTrue();
    if (panes.EditorBlockIsOpen) { panes.InnerKeepsAPaneOpen.Should().BeTrue(); }
}
```

The assumptions the shell makes about the control ITSELF get their own suite, so
that a change in the control fails here rather than on screen. Nothing in it
draws: only the state the control keeps outside its template is asserted:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/tests/Fresco.Brix.Core.Tests/TriPaneViewShellAssumptionsTests.cs
[Fact]
public void restoring_a_pane_returns_the_share_it_was_minimized_at()
{
    //Arrange
    TriPaneView outer = Outer();
    outer.MinimizeLowerPane();

    //Act
    outer.RestoreLowerPane();

    //Assert
    //Not the control's own default of fifty: the shell's twenty, which is
    //why every strip that starts shut is MINIMIZED rather than set to zero.
    outer.IsLowerPaneMinimized.Should().BeFalse();
    outer.LowerPanePercent.Should().Be(20d);
}

[Fact]
public void minimizing_the_last_open_pane_is_refused()
{
    //Arrange
    TriPaneView outer = Outer();
    outer.MinimizeLowerPane();
    outer.MinimizeUpperPane();

    //Act
    outer.MinimizeSidePane();

    //Assert
    //Which is why the shell opens every pane that is to be open before it
    //shuts any that is to be shut.
    outer.IsSidePaneMinimized.Should().BeFalse();
    outer.SidePanePercent.Should().Be(25d);
}
```

The same suite is where a wrong way of doing something is pinned down as a fact
rather than as a comment, which is the cheapest possible guard on a sharp edge:

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/tests/Fresco.Brix.Core.Tests/TriPaneViewShellAssumptionsTests.cs
[Fact]
public void restore_all_opens_a_lower_pane_that_was_meant_to_stay_shut()
{
    //Arrange
    //The inner control's lower pane is not one of the window's regions.
    TriPaneView inner = Inner();
    inner.MinimizeUpperPane();

    //Act
    //The obvious way to undo a maximize, and the wrong one here.
    inner.RestoreAll();

    //Assert
    //A pane held at zero beside a pane that is not reads as minimized, so
    //restoring everything gives it the control's own default of fifty and
    //the window grows a region it does not have. The shell restores pane by
    //pane for exactly this reason.
    inner.LowerPanePercent.Should().Be(50d);
    inner.IsLowerPaneMinimized.Should().BeFalse();
}
```

**Where to look.**
`Fresco.Brix/tests/Fresco.Brix.Core.Tests/MainToolbarTests.cs` (the layout data,
the builder over real elements, the tool tips and the chevron),
`Fresco.Brix/tests/Fresco.Brix.Core.Tests/ToolTipComposerTests.cs` (who writes a
button's tip, and what happens when the application writes one first),
`Fresco.Brix/tests/Fresco.Brix.Core.Tests/ShellLayoutTests.cs` (the pure pane
arithmetic and the default shares),
`Fresco.Brix/tests/Fresco.Brix.Core.Tests/TriPaneViewShellAssumptionsTests.cs`
(every assumption the shell makes about the control, asserted against the
control),
`Fresco.Brix/tests/Fresco.Brix.Core.Tests/DockLayoutTests.cs` (what survives
being written out and read back).

The shapes these tests are written against are in
[Add the CommandBar add-in and build a tray of two toolbars from command objects](#add-the-commandbar-add-in-and-build-a-tray-of-two-toolbars-from-command-objects),
[Nest two TriPaneView controls to put four regions around an editor](#nest-two-tripaneview-controls-to-put-four-regions-around-an-editor)
and
[Show and hide a TriPaneView pane by minimizing and restoring it](#show-and-hide-a-tripaneview-pane-by-minimizing-and-restoring-it);
the project setup these suites run under is in
[Set up test projects on the Microsoft Testing Platform and check a port against recorded answers](#set-up-test-projects-on-the-microsoft-testing-platform-and-check-a-port-against-recorded-answers).

**Sharp edges.**

- Assert the DATA and the elements against each other, not the elements against
  a literal. A list of expected names checks the order; comparing the built bar
  against that same list is what catches a builder that quietly drops an entry.
- A tray builds its bars when it enters a tree. A test that never puts one in a
  tree has to ask for the build through whatever public call the application
  itself uses when a preference changes.
- Keep the tests that build real elements in a collection of their own, so that
  work stays off every other test thread.
- Measure to reach layout behavior. The chevron, the wrap and the floors are all
  measure-time decisions, so a measure pass at a chosen width is enough to
  assert them, with nothing drawn and no window opened.
- Do not reach for anything that needs the control's template. A default style
  never resolves in a process with no application, so divider visibility,
  cursors and pixel sizes belong to a suite that runs on a real head; the
  shares, the minimized flags and the refusals do not.
- Write down the WRONG way as a passing test. A test asserting that
  restore-everything opens a pane you meant to keep shut is what stops the next
  reader from tidying the pane-by-pane code into one call.

## Project layout, packaging and native assets

### Put every package in a Core library and one runtime package in each head

**When you want this.** You have more than one head and do not want to keep six
package lists in step.

**The MVVM shape.** Not applicable; this is a build-file rule. Every head is a
`Page` glob, an `Import` of the shared `.projitems`, one `ProjectReference` to
the Core library, and one `PackageReference`.

**Code.**

```xml
<!-- From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.LinuxX11/Fresco.Brix.LinuxX11.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <OutputType>Exe</OutputType>
  </PropertyGroup>

  <!-- Tell MSBuild to treat .xaml files as CodeBrix.Platform XAML pages -->
  <ItemGroup>
    <Page Include="**\*.xaml" Exclude="bin\**\*.xaml;obj\**\*.xaml" />
    <None Remove="**\*.xaml" />
  </ItemGroup>

  <!-- Shared UI files (App.xaml + Views) -->
  <Import Project="..\Fresco.Brix.UI\Fresco.Brix.UI.projitems" Label="Shared" />
  <ItemGroup>
    <ProjectReference Include="..\Fresco.Brix.Core\Fresco.Brix.Core.csproj" />
  </ItemGroup>

  <!-- EXACTLY ONE platform head package; all other packages come from Fresco.Brix.Core -->
  <ItemGroup>
    <PackageReference Include="..." />
  </ItemGroup>
</Project>
```

The Windows-only head is the same file with two extra properties:

```xml
<!-- From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.WinWpfSkia/Fresco.Brix.WinWpfSkia.csproj -->
  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <OutputType>Exe</OutputType>
    <EnableWindowsTargeting>true</EnableWindowsTargeting>
  </PropertyGroup>
```

The shared UI project is a `.shproj` with a `.projitems` that carries `App.xaml`
and the page, and sets its own root namespace:

```xml
<!-- From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/Fresco.Brix.UI.projitems -->
  <PropertyGroup Label="Configuration">
    <Import_RootNamespace>Fresco.Brix.UI</Import_RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <Page Include="$(MSBuildThisFileDirectory)App.xaml">
      <SubType>Designer</SubType>
      <Generator>MSBuild:Compile</Generator>
    </Page>
    <Page Include="$(MSBuildThisFileDirectory)Views\MainPage.xaml">
      <SubType>Designer</SubType>
      <Generator>MSBuild:Compile</Generator>
    </Page>
  </ItemGroup>
  <ItemGroup>
    <Compile Include="$(MSBuildThisFileDirectory)App.xaml.cs">
      <DependentUpon>App.xaml</DependentUpon>
    </Compile>
    <Compile Include="$(MSBuildThisFileDirectory)Views\MainPage.xaml.cs">
      <DependentUpon>MainPage.xaml</DependentUpon>
    </Compile>
  </ItemGroup>
```

**Where to look.**
all six csprojs under `Fresco.Brix/src/Fresco.Brix.*/`,
`Fresco.Brix/src/Fresco.Brix.UI/Fresco.Brix.UI.projitems`,
`Fresco.Brix/src/Fresco.Brix.Core/Fresco.Brix.Core.csproj` (the one place every
other package is named).

**Sharp edges.**

- The `Page` glob plus the `None Remove` pair is what makes a `.xaml` file
  compile as a platform XAML page; without it the file is treated as content.
- `EnableWindowsTargeting` is needed so the `net10.0-windows` head restores on a
  non-Windows machine even though it will not build there.
- Dependencies run one way: each head file-links the shared `.projitems`,
  project-references the Core library, and adds exactly one runtime package.
  The Core library carries everything else.

### Give a library that references CodeBrix Platform its own RootNamespace

**When you want this.** Your Core project or a library is named
`Company.App.Core` but your code lives in the `Company.App` namespace, or a
library's generated XAML types would otherwise collide.

**The MVVM shape.** Not applicable; one property per csproj.

**Code.**

```xml
<!-- From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Fresco.Brix.Core.csproj -->
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>

    <!-- Match the namespace used by the app code -->
    <RootNamespace>Fresco.Brix</RootNamespace>
  </PropertyGroup>
```

```xml
<!-- From CodeBrix.Samples.Gpl3/Fresco.Brix/src/libs/Fresco.Brix.MusicView/Fresco.Brix.MusicView.csproj -->
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <RootNamespace>Fresco.Brix.MusicView</RootNamespace>
  </PropertyGroup>
```

The XAML in the shared UI project then names the view model's assembly
explicitly, which only works because the root namespace and the assembly name
are both stated:

```xml
<!-- From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml -->
    xmlns:vm="clr-namespace:Fresco.Brix.ViewModels;assembly=Fresco.Brix.Core"
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Fresco.Brix.Core.csproj`,
`Fresco.Brix/src/libs/Fresco.Brix.MusicView/Fresco.Brix.MusicView.csproj`,
`Fresco.Brix/src/Fresco.Brix.UI/Fresco.Brix.UI.projitems`.

**Sharp edges.**

- The shared project's `.projitems` sets its own `Import_RootNamespace`,
  separate from the head's, so the XAML types land where the page's `x:Class`
  says they do.

### Keep a ported library completely free of the UI framework

**When you want this.** You have a chunk of pure logic (a parser, a language
model, a converter) and want to test it in a host-free process, quickly,
without native assets.

**The MVVM shape.** The library has no view types and no dependency at all. The
application adapts it to the editor at the boundary, and the adapter lives in
the application rather than in the library.

**Code.**

```xml
<!-- From CodeBrix.Samples.Gpl3/Fresco.Brix/src/libs/Fresco.Brix.Ly/Fresco.Brix.Ly.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
```

The view library states the same rule about the engine rather than about the UI:

```xml
<!-- From CodeBrix.Samples.Gpl3/Fresco.Brix/src/libs/Fresco.Brix.MusicView/Fresco.Brix.MusicView.csproj -->
    <!-- The view is a CodeBrix.Platform control; it never references LilyPort
         (architecture §5.1) — the engine reaches it only through the SVG files
         it wrote and the typeface seam the host fills in. -->
```

**Where to look.**
`Fresco.Brix/src/libs/Fresco.Brix.Ly/`,
`Fresco.Brix/src/libs/Fresco.Brix.MusicView/Fresco.Brix.MusicView.csproj`,
`Fresco.Brix/tests/libs/Fresco.Brix.Ly.Tests/Fresco.Brix.Ly.Tests.csproj`.

The adapter that puts such a library to work on the live editor document is in
[Bridge a platform-free document model onto the editor text document](#bridge-a-platform-free-document-model-onto-the-editor-text-document).

**Sharp edges.**

- The payoff is visible in the test csprojs. The two test projects that touch
  platform or Skia code need `CodeBrixRuntimeIdentifier=skia` and a native Skia
  package; the platform-free one needs neither.
- Draw the line at the reference, not at the folder. A library that references
  the drawing surface but never the engine keeps that separation testable,
  because the missing reference is a compile error rather than a convention.

### Ship data assets beside the program so their licenses travel with them

**When you want this.** You bundle third-party data (fonts, dictionaries, sound
banks, documents, translation catalogs) and have to satisfy an attribution
requirement, or you want a folder the user can empty without breaking the
application.

**The MVVM shape.** Not applicable; a set of `None` and `EmbeddedResource` item
groups, each with a comment that says why that choice was made.

**Code.**

```xml
<!-- From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Fresco.Brix.Core.csproj -->
  <!-- The hyphenation dictionaries [...] They are third-party data files, each
       with its own license and its own README, aggregated with the application
       rather than built into it: the folder can be emptied and the application
       still runs [...] Content, not embedded, because the notices have to
       travel WITH the files and because the search that finds them is a
       directory search either way. -->
  <ItemGroup>
    <None Include="assets\hyphdicts\*"
          Link="assets\hyphdicts\%(Filename)%(Extension)"
          CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <!-- The interface translations [...] The RecursiveDir metadata is what keeps
       the per-language folders: a catalog found at
       assets\i18n\de\LC_MESSAGES\frescobaldi.mo has to land at the same place
       beside the program, because that is where Services/LanguageSetup looks
       for it. -->
  <ItemGroup>
    <None Include="assets\i18n\**\*.mo"
          Link="assets\i18n\%(RecursiveDir)%(Filename)%(Extension)"
          CopyToOutputDirectory="PreserveNewest" />
    <None Include="assets\i18n\README.txt"
          Link="assets\i18n\README.txt"
          CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>

  <!-- The toolbars' icons [...] EmbeddedResource, exactly as the Quick
       Insert glyphs are, but read two ways. A toolbar button's icon is drawn
       BY THE PLATFORM [...] Everything else that wants one of these files as a
       bitmap still goes through the application's own renderer [...]
       Two prefixes, because the set is chosen at runtime by the platform's
       theme (Services/IconTheme). -->
  <ItemGroup>
    <EmbeddedResource Include="assets\icons\light\*.svg"
                      LogicalName="Fresco.Brix.Icons.Light.%(Filename).svg" />
    <EmbeddedResource Include="assets\icons\dark\*.svg"
                      LogicalName="Fresco.Brix.Icons.Dark.%(Filename).svg" />
    <None Include="assets\icons\*.txt"
          Link="assets\icons\%(Filename)%(Extension)"
          CopyToOutputDirectory="PreserveNewest" />
  </ItemGroup>
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Fresco.Brix.Core.csproj` (every item group
carries a comment explaining the decision),
`Fresco.Brix/THIRD-PARTY-NOTICES.txt` (the numbered ledger those comments cite
by section).

**Sharp edges.**

- Use a plain `*` for an asset folder glob. A narrower pattern that looks right
  can silently miss the one file a run actually names; the csproj comment
  records a glob that did exactly that, and every debug mode drew nothing.
- `%(RecursiveDir)` in the `Link` is what preserves a nested folder shape in
  the output; without it every file lands in one directory.
- Choosing `EmbeddedResource` for artwork still needs a `None` item for the
  license text beside it, or the notice never reaches the output folder.
- Keep the license texts that must travel with their files next to them in the
  source tree and in the output: a license file per icon set, the documentation
  license beside the bundled manuals, a README beside the sound bank, and one
  per hyphenation dictionary.
- Aim for every asset folder to be emptiable. If the application still runs
  with the folder gone, the data is aggregated with the program rather than
  built into it, which is the argument the notices file makes.

## Not yet covered by a sample

No application in this repository shows any of the following, so look
elsewhere for them:

- Anything on the network: there is no REST or HTTP client, no authentication
  and no token, and nothing is downloaded at run time. The only socket in the
  application is the local one the single-instance handover uses.
- A WebView. This application deliberately has none, and renders its
  documentation, its diffs and its own user guide with PDF rasterization and
  ordinary controls instead.
- A camera, video playback, or image capture.
- Printing.
- Drag and drop between the desktop and the application.
- A native (WinUI 3, WPF or .NET MAUI) head. All six heads here are Skia heads.
- A game engine loop.
- An application-owned database. The only persistent store is the settings
  store the AppSettings add-in owns.
