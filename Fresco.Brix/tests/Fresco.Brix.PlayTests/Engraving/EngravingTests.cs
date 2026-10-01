using CodeBrix.Platform.PlayTest;
using Fresco.Brix.Engrave;
using Fresco.Brix.MusicView;
using SilverAssertions;
using System.Text.RegularExpressions;
using Xunit;

namespace Fresco.Brix.PlayTests.Engraving;

public sealed class EngravingTests(AppFixture fixture) : FrescoTest(fixture)
{
    private MusicViewPanel Music => (MusicViewPanel)Model.Panels.PanelByName("musicview");
    private Locator MusicTool(string name) => Page.GetByRole(AriaRole.Toolbar, new() { Name = "Music View Toolbar", Exact = true })
        .GetByRole(AriaRole.Button, new() { NameRegex = new Regex("^" + Regex.Escape(name) + @"(?: \(|,|$)") });

    private async Task<JobEventArgs> EngraveAsync()
    {
        await Fixture.Application.WaitForAsync(() => Model.Engine.State, state => state is EngineState.Ready or EngineState.Failed,
            timeout: 120_000, description: "bundled LilyPort engine initialization");
        (await Page.EvaluateAsync(() => Model.Engine.IsReady)).Should().BeTrue();
        var done = new TaskCompletionSource<JobEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        EventHandler<JobEventArgs> handler = (_, result) => done.TrySetResult(result);
        await Page.EvaluateAsync(() => Model.Engraver.JobFinished += handler);
        try
        {
            await Tool("Engrave").ClickAsync(new() { Position = new() { X = 10, Y = 12 } });
            return await done.Task.WaitAsync(TimeSpan.FromMinutes(2), TestContext.Current.CancellationToken);
        }
        finally { await Page.EvaluateAsync(() => Model.Engraver.JobFinished -= handler); }
    }

    [Fact]
    public async Task Engraved_score_displays_pages_supports_zoom_and_exports_svg()
    {
        await OpenScoreAsync("\\version \"2.24.3\"\n{ c'4 d' e' f' }", "engraving.ly");
        await MenuItem("Tools").ClickAsync();
        await MenuItem("Viewers").HoverAsync();
        await Page.GetByRole(AriaRole.Menuitemcheckbox, new() { Name = "Music View", Exact = true }).CheckAsync();
        var result = await EngraveAsync();
        result.Job.Success.Should().BeTrue(result.Job.Error?.ToString());
        await Fixture.Application.WaitForAsync(() => Music.PageCount, count => count > 0, timeout: 30_000);
        (await Page.EvaluateAsync(() => Music.IsVisible)).Should().BeTrue();
        var zoom = await Page.EvaluateAsync(() => Music.ZoomFactor);
        await MusicTool("Zoom In").ClickAsync();
        var zoomedIn = await Page.EvaluateAsync(() => Music.ZoomFactor);
        zoomedIn.Should().BeGreaterThan(zoom);
        await MusicTool("Zoom Out").ClickAsync();
        // Buttons step through preset factors; the initial fit-to-page factor
        // may lie between those presets.
        (await Page.EvaluateAsync(() => Music.ZoomFactor)).Should().BeLessThan(zoomedIn).And.BeGreaterThan(0);
        await MusicTool("Magnifier").CheckAsync();
        await Expect(MusicTool("Magnifier")).ToBeCheckedAsync();
        await MusicTool("Magnifier").UncheckAsync();
        var path = TestPath("exported.svg");
        Fixture.Application.FilePickers.EnqueueSaveFile(path);
        await MenuAsync("Music", "Export", "Export SVG...");
        await Fixture.Application.WaitForAsync(() => File.Exists(path), exists => exists);
        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Should().Contain("<svg");
        await MusicTool("Clear").ClickAsync();
        await WaitAsync(() => Music.PageCount, count => count == 0);
    }

    [Fact]
    public async Task Invalid_music_reports_a_failed_job_and_keeps_the_editor_usable()
    {
        await OpenScoreAsync("\\version \"2.24.3\"\n\\thisCommandDoesNotExist {", "invalid.ly");
        var result = await EngraveAsync();
        result.Job.Success.Should().BeFalse();
        (await Page.EvaluateAsync(() => Model.Panels.PanelByName("logtool").IsVisible)).Should().BeTrue();
        await Editor.FillAsync("{ c'1 }");
        await Expect(Editor).ToHaveValueAsync("{ c'1 }");
    }

    [Fact]
    public async Task Engine_information_describes_the_bundled_engine()
    {
        await MenuAsync("LilyPort", "Engine Information...");
        await Expect(Dialog).ToContainTextAsync("LilyPort");
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
    }

    [Fact]
    public async Task Cancelling_custom_engraving_does_not_start_a_job()
    {
        await MenuAsync("LilyPort", "Engrave (custom)...");
        await Expect(Dialog).ToContainTextAsync("Engraving mode:");
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        (await Page.EvaluateAsync(() => Model.Engraver.RunningJob())).Should().BeNull();
    }
}
