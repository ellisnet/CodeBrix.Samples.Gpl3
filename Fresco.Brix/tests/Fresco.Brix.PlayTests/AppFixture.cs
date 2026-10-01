using CodeBrix.Platform.AppSettings;
using CodeBrix.Platform.PlayTest;
using Fresco.Brix.Services;
using Fresco.Brix.Shell;
using Fresco.Brix.ViewModels;
using Fresco.Brix.Views;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Xunit;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodeBrix.Platform.UI.AdvancedTextEdit;

[assembly: Xunit.v3.Parallelization(Mode = Xunit.Sdk.ParallelMode.None)]

namespace Fresco.Brix.PlayTests;

[CollectionDefinition(Name)]
public sealed class AppCollection : ICollectionFixture<AppFixture>
{
    public const string Name = "Fresco application";
}

public sealed class AppFixture : IAsyncLifetime
{
    public PlayTestApplication Application { get; private set; }
    public MainPage View { get; private set; }
    public MainViewModel Model => (MainViewModel)View.DataContext;
    public ViewManager Editors => Descendants(View).OfType<ViewManager>().Single();
    public EditorView ActiveEditor => View.ActiveView();
    public string DataDirectory { get; } = Path.Combine(AppContext.BaseDirectory, "TestResults", "PlayTestData", Guid.NewGuid().ToString("N"));
    public string TestDirectory { get; private set; }
    private readonly Dictionary<string, object> _changedSettings = new();
    private bool _restoringSettings;

    public async ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(DataDirectory);
        AppSettingsService.Initialize(SettingsStore.AppName, Path.Combine(DataDirectory, "settings"));
        using (var settings = new SettingsStore())
        {
            settings.SetBool(RemoteInstance.AllowRemoteKey, false);
            settings.SetString("language", "C");
        }
        App.CommandLinePaths = Array.Empty<string>();
        App.CommandLine = CommandLineArguments.Parse(null);
        Application = await PlayTestApplication.LaunchAsync(() => new App(), new()
        {
            ConfigurationAssembly = typeof(AppFixture).Assembly,
            ArtifactsDirectory = Path.Combine(DataDirectory, "failures"),
        });
        View = await Application.EvaluateAsync(() => (MainPage)((Frame)App.Shell.Content).Content);
        await Application.WaitForAsync(() => ActiveEditor != null, ready => ready);
        AppSettingsService.Store.SettingChanged += (_, change) =>
        {
            if (!_restoringSettings) _changedSettings.TryAdd(change.Key, change.OldValue);
        };
    }

    public async Task ResetAsync(ScreenOrientation? orientation)
    {
        TestDirectory = Path.Combine(DataDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(TestDirectory);
        Application.FilePickers.Clear();
        await Application.EvaluateAsync(() =>
        {
            foreach (var popup in VisualTreeHelper.GetOpenPopupsForXamlRoot(View.XamlRoot).ToArray())
            {
                if (popup.Child is ContentDialog dialog) dialog.Hide();
                else popup.IsOpen = false;
            }
            Editors.CloseOtherViewSpaces();
            foreach (var panel in Model.Panels.Panels) panel.IsVisible = false;
            foreach (var document in Model.Documents.Documents.ToArray()) Model.Documents.CloseDocument(document);
            _restoringSettings = true;
            try
            {
                foreach (var pair in _changedSettings)
                    AppSettingsService.Store.Set(pair.Key, pair.Value is string json ? JsonSerializer.Deserialize<JsonElement>(json) : null);
                _changedSettings.Clear();
            }
            finally { _restoringSettings = false; }
            Model.RecentFiles.Invalidate();
            Model.SessionStore.SetCurrentSession(null);
            foreach (var toolbar in Descendants(View).OfType<MainToolbar>()) toolbar.SettingsChanged();
            Model.Documents.CreateDocument();
            foreach (var search in Descendants(View).OfType<Fresco.Brix.Search.SearchBar>()) search.Hide();
        });
        // Keep one real window, services and engine alive, as in an ordinary editor session.
        // Reset document/panel state rather than accumulating page subscriptions to singletons.
        await Application.Page.SetContentAsync(() => View, orientation);
        await Application.EvaluateAsync(() => ActiveEditor.FocusEditor());
    }

    public async ValueTask DisposeAsync()
    {
        if (Application != null)
        {
            await Application.EvaluateAsync(() =>
            {
                foreach (var document in Model.Documents.Documents.ToArray()) Model.Documents.CloseDocument(document);
                Model.DocumentWatcher.Dispose();
                RemoteInstance.Quit();
            });
            await Application.DisposeAsync();
        }
        AppSettingsService.Shutdown();
    }

    internal static IEnumerable<UIElement> Descendants(UIElement root)
    {
        yield return root;
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            if (VisualTreeHelper.GetChild(root, i) is UIElement child)
                foreach (var descendant in Descendants(child)) yield return descendant;
    }
}

[Collection(AppCollection.Name)]
public abstract class FrescoTest(AppFixture fixture) : PageTest(fixture.Application), IAsyncLifetime
{
    protected AppFixture Fixture { get; } = fixture;
    protected MainViewModel Model => Fixture.Model;
    public async ValueTask InitializeAsync()
    {
        var test = (Xunit.v3.IXunitTest)TestContext.Current.Test;
        test.Traits.TryGetValue(PlayTestOrientationAttribute.CaseTraitName, out var orientations);
        await Fixture.ResetAsync(PlayTestOrientationAttribute.Resolve(test.TestMethod.Method, orientations));
    }
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    protected Locator Button(string name) => Page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });
    protected Locator Dialog => Page.GetByRole(AriaRole.Dialog);
    protected Locator Editor => Page.GetByType<ViewManager>().GetByType<AdvancedTextEdit>().First;
    protected Locator Tabs => Page.GetByType<DocumentTabBar>().GetByRole(AriaRole.Tab);
    protected Locator MenuItem(string name) => Page.GetByRole(AriaRole.Menuitem, new() { Name = name, Exact = true });
    protected Locator Tool(string name) => Page.GetByRole(AriaRole.Toolbar, new() { Name = "Main Toolbar", Exact = true })
        .GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^" + Regex.Escape(name) + @"(?: \(|,|$)") });

    protected async Task MenuAsync(params string[] path)
    {
        await MenuItem(path[0]).ClickAsync();
        for (var i = 1; i < path.Length - 1; i++)
        {
            await MenuItem(path[i]).ScrollIntoViewIfNeededAsync();
            await MenuItem(path[i]).HoverAsync();
        }
        if (path.Length > 1)
        {
            await MenuItem(path[^1]).ScrollIntoViewIfNeededAsync();
            await MenuItem(path[^1]).ClickAsync();
        }
    }

    protected Task WaitAsync<T>(Func<T> read, Func<T, bool> ready) => Fixture.Application.WaitForAsync(read, ready);
    protected Task<string> TextAsync() => Page.EvaluateAsync(() => Model.Documents.CurrentDocument.Text);
    protected string TestPath(string name) => Path.Combine(Fixture.TestDirectory, name);
    protected async Task<string> OpenScoreAsync(string text, string name = "score.ly")
    {
        var path = TestPath(name);
        await File.WriteAllTextAsync(path, text);
        Fixture.Application.FilePickers.EnqueueOpenFile(path);
        await MenuAsync("File", "Open...");
        await WaitAsync(() => Model.Documents.CurrentDocument.Path, current => current == path);
        return path;
    }
}
