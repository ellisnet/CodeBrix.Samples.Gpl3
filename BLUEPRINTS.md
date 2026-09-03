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
  - [Register window-level shortcuts that survive a focused text editor](#register-window-level-shortcuts-that-survive-a-focused-text-editor)
  - [Build menus and toolbars in code from command objects](#build-menus-and-toolbars-in-code-from-command-objects)
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

**When you want this.** You want tool panels around a center area, resizable by
dragging, that come back where the user left them.

**The MVVM shape.** The shell is a `Grid`-derived container built in code;
panels are plain objects with a widget and a toggle command. The view model
holds the panel manager and owns the settings store; the page captures and
applies the arrangement, because only the page can see it.

**Code.** The divider is a plain `Grid` with its own pointer handling, because
the themed `Thumb` paints nothing on the Skia heads:

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
ShellHost.Content = _shell;

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
// captured layout and the window's own bounds.

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

    //The WINDOW's own bounds, not AppWindow.Size: on the X11 head the
    //latter answers the FRAMED size [...] so feeding it back to Resize —
    //which sets the size the window itself gets — would grow the window by
    //the frame on every launch. Bounds is what Resize is the inverse of.
    Windows.Foundation.Rect bounds = App.Shell?.Bounds ?? default;
    ViewModel.SaveLayout(
        _shell.CaptureLayout(),
        (int)Math.Round(bounds.Width),
        (int)Math.Round(bounds.Height));
}
```

**Where to look.**
`Fresco.Brix/src/Fresco.Brix.Core/Shell/SplitContainer.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Shell/DockShell.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Shell/DockLayout.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Shell/Panel.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Shell/PanelManager.cs`,
`Fresco.Brix/src/Fresco.Brix.UI/Views/MainPage.xaml.cs` (`RestoreWindowLayout`,
`SaveWindowLayout`).

**Sharp edges.**

- The themed `Thumb` paints nothing on the Skia heads, and so do a standalone
  `ScrollBar` and the themed tab controls. The answer used throughout this
  application is a plain `Grid` with its own pointer handling; `TrackBar.cs`
  says the same thing about `Slider`.
- Save the window's `Bounds`, not `AppWindow.Size`. On the LinuxX11 head the
  latter is the framed size, so feeding it back into `Resize()` grows the
  window by the frame width on every launch.
- Store divider positions as relative weights, not pixels, so the layout
  survives a different screen.
- A panel's widget is built once and kept, and an element has one parent, so a
  tab being rebuilt must have its content moved before the old tab is thrown
  away.
- Write the layout on both the explicit Quit path and the window's `Closed`
  event. Writing it twice is harmless, because it writes the same thing.
- Panel registration order is what decides menu order. Register them in one
  block so the order is readable.

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

**When you want this.** Menus and toolbars whose entries follow one command
object each, with correct enabled and checked state, built from data rather
than from XAML.

**The MVVM shape.** The view model owns the commands and the handlers. A
command that only needs Execute and CanExecute is a `SimpleCommand` on a
`SimpleViewModel`, refreshed by `[AffectsCommands]` when a property it depends
on changes. A menu or toolbar entry needs more than that (a caption, a tooltip,
an icon name, a checked state, a shortcut list), so this application's command
type adds those and raises `INotifyPropertyChanged` for them; the builders
subscribe and re-read on change. Either way the builder holds no logic: it
follows the command.

**Code.**

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

```csharp
// From CodeBrix.Samples.Gpl3/Fresco.Brix/src/Fresco.Brix.Core/Shell/MainToolbar.cs
private UIElement ButtonFor(ToolbarEntry entry, string barTitle)
{
    AppAction action = entry.Action;
    ButtonBase button = action.IsCheckable
        ? new ToggleButton { IsChecked = action.IsChecked }
        : new Button();

    button.Content = ContentFor(action);
    Describe(button, action, barTitle);

    if (button is ToggleButton toggle)
    {
        toggle.Click += (_, _) =>
        {
            //A ToggleButton has already flipped itself by the time it is
            //clicked; AppAction.Trigger flips the action. Setting the
            //action's state from the button first keeps the two from
            //cancelling each other out.
            action.IsChecked = toggle.IsChecked != true;
            action.Trigger();
        };
    }
    else { button.Click += (_, _) => action.Trigger(); }

    void Update() { /* re-reads IsEnabled/Content/tooltip/IsChecked */ }
    Update();
    action.PropertyChanged += (_, _) => Update();
    // ... an arrow Button carrying a MenuFlyout when the entry has one
}
```

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
`Fresco.Brix/src/Fresco.Brix.Core/Shell/MainToolbar.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Shell/ToolbarLayout.cs`,
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
- Re-create toolbar buttons when the bar is rebuilt: the change subscription
  goes with the old button.
- A `ToggleButton` has already flipped itself by the time its click handler
  runs. Set the command's state from the button before triggering, or the two
  cancel out.

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
scheme, shipped inside the assembly, recolored to the theme's foreground.

**The MVVM shape.** Icons are `EmbeddedResource` items under two logical-name
prefixes. One static renderer turns a named resource into an `Image`; a theme
helper chooses the prefix and the foreground, and returns an unsubscribe action
so a control can follow theme changes and stop following when it goes away.

**Code.**

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
`Fresco.Brix/src/Fresco.Brix.Core/Services/IconTheme.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/QuickInsert/SymbolIcons.cs`,
`Fresco.Brix/src/Fresco.Brix.Core/Fresco.Brix.Core.csproj` (the two
`EmbeddedResource` groups and their `LogicalName` prefixes).

The same parser reads whole engraved pages and turns their anchors into
clickable regions in
[Parse SVG once into a scene graph and use its anchors as hit-test geometry](#parse-svg-once-into-a-scene-graph-and-use-its-anchors-as-hit-test-geometry).

**Sharp edges.**

- Keep one renderer. Every SVG in the application, from toolbar icons to the
  generated symbol glyphs, goes through one call site by rule; a second one is a
  second place to fix a rendering problem.
- Ask whether the resource exists before rendering. The toolbar falls back to a
  text caption when an icon is absent, so emptying the icon folder leaves a
  working, if wordy, toolbar.
- Draw a button's icon at reduced opacity while its command is disabled. Full
  opacity on a disabled button looks enabled; the toolbar's comment records
  that defect and the fix.

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

  <!-- The two window toolbars' icons [...] EmbeddedResource, exactly as the
       Quick Insert glyphs are, and read through the same one renderer [...]
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
