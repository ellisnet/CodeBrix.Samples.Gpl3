using System.Linq;
using System.Threading.Tasks;
using CodeBrix.Platform.PlayTest;
using CodeBrix.Platform.UI.AdvancedTextEdit;
using CodeBrix.Platform.UI.Toolkit;
using Fresco.Brix.Shell;
using SilverAssertions;
using Xunit;

namespace Fresco.Brix.PlayTests.Layout;

public sealed class DockAndSplitTests(AppFixture fixture) : FrescoTest(fixture)
{
    private async Task TogglePanelAsync(string group, string title)
    {
        await MenuItem("Tools").ClickAsync();
        await MenuItem(group).HoverAsync();
        await Page.GetByRole(AriaRole.Menuitemcheckbox, new() { Name = title == "MIDI" ? "MIDI Player" : title, Exact = true }).ClickAsync();
    }

    [Theory]
    [InlineData("Viewers", "Music View", "musicview")]
    [InlineData("Viewers", "Documentation Browser", "docbrowser")]
    [InlineData("Viewers", "Layout Control Options", "layoutcontrol")]
    [InlineData("Coding", "Quick Insert", "quickinsert")]
    [InlineData("Coding", "Special Characters", "charmap")]
    [InlineData("Coding", "Snippets", "snippettool")]
    [InlineData("Structure", "Documents", "doclist")]
    [InlineData("Structure", "Outline", "outline")]
    [InlineData("MIDI", "MIDI", "miditool")]
    public async Task Tool_panel_opens_a_real_dock_tab_and_closes_again(string group, string title, string id)
    {
        await TogglePanelAsync(group, title);
        await WaitAsync(() => Model.Panels.PanelByName(id).IsVisible, visible => visible);
        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = title, Exact = true })).ToBeVisibleAsync();
        (await Page.EvaluateAsync(() => Model.Panels.PanelByName(id).IsInstantiated)).Should().BeTrue();
        await TogglePanelAsync(group, title);
        (await Page.EvaluateAsync(() => Model.Panels.PanelByName(id).IsVisible)).Should().BeFalse();
        await Expect(Page.GetByRole(AriaRole.Tab, new() { Name = title, Exact = true })).ToHaveCountAsync(0);
    }

    [Theory]
    [InlineData("Viewers", "Music View", -100f)]
    [InlineData("Structure", "Documents", 100f)]
    public async Task Dragging_a_side_divider_resizes_the_pane_and_saves_the_layout(string group, string panel, float distance)
    {
        await TogglePanelAsync(group, panel);
        var divider = Page.GetByType<TriPaneViewDivider>();
        await Expect(divider).ToHaveCountAsync(1);
        var before = await divider.BoundingBoxAsync();
        await divider.DragByAsync(distance, 0);
        var after = await divider.BoundingBoxAsync();
        (after.X - before.X).Should().BeApproximately(distance, 3);
        (await Page.EvaluateAsync(() => DockLayout.Load(Model.Settings).Sizes.IsUsable)).Should().BeTrue();
        (await Page.EvaluateAsync(() => AppFixture.Descendants(Fixture.View).OfType<TriPaneViewDivider>().Any(d => d.IsDragging))).Should().BeFalse();
    }

    [Fact]
    public async Task Hiding_and_restoring_a_side_panel_preserves_its_width()
    {
        await TogglePanelAsync("Viewers", "Music View");
        var divider = Page.GetByType<TriPaneViewDivider>();
        await divider.DragByAsync(-60, 0);
        var dragged = await divider.BoundingBoxAsync();
        await TogglePanelAsync("Viewers", "Music View");
        await Expect(divider).ToHaveCountAsync(0);
        await TogglePanelAsync("Viewers", "Music View");
        ((await divider.BoundingBoxAsync()).X).Should().BeApproximately(dragged.X, 2);
    }

    [Fact]
    public async Task Dragging_the_bottom_divider_changes_height_without_changing_source()
    {
        await Editor.FillAsync("c4 d e f");
        await TogglePanelAsync("Coding", "Snippets");
        var divider = Page.GetByType<TriPaneViewDivider>();
        var before = await divider.BoundingBoxAsync();
        await divider.DragByAsync(0, -80);
        ((await divider.BoundingBoxAsync()).Y).Should().BeApproximately(before.Y - 80, 3);
        (await TextAsync()).Should().Be("c4 d e f");
    }

    [Theory]
    [InlineData("Split Horizontally")]
    [InlineData("Split Vertically")]
    public async Task Split_views_share_a_document_and_edits(string split)
    {
        await Editor.FillAsync("c4");
        await MenuAsync("Window", split);
        await Expect(Page.GetByType<AdvancedTextEdit>()).ToHaveCountAsync(2);
        await Page.GetByType<AdvancedTextEdit>().Last.PressAsync("Control+End");
        await Page.GetByType<AdvancedTextEdit>().Last.PressSequentiallyAsync(" d4");
        await Expect(Page.GetByType<AdvancedTextEdit>().First).ToHaveValueAsync("c4 d4");
        await Expect(Page.GetByType<AdvancedTextEdit>().Last).ToHaveValueAsync("c4 d4");
        (await Page.EvaluateAsync(() => Fixture.Editors.ViewSpaces.Select(v => v.Document).Distinct().Count())).Should().Be(1);
    }

    [Fact]
    public async Task Closing_other_views_preserves_the_active_document()
    {
        await Editor.FillAsync("keep this score");
        await MenuAsync("Window", "Split Vertically");
        await MenuAsync("Window", "Split Horizontally");
        await Expect(Page.GetByType<AdvancedTextEdit>()).ToHaveCountAsync(3);
        await MenuAsync("Window", "Close Other Views");
        await Expect(Page.GetByType<AdvancedTextEdit>()).ToHaveCountAsync(1);
        await Expect(Editor).ToHaveValueAsync("keep this score");
    }

    [Fact]
    public async Task Closing_a_split_keeps_the_other_view_usable()
    {
        await MenuAsync("Window", "Split Vertically");
        await MenuAsync("Window", "Close Current View");
        await Expect(Page.GetByType<AdvancedTextEdit>()).ToHaveCountAsync(1);
        await Editor.PressSequentiallyAsync("still editable");
        await Expect(Editor).ToHaveValueAsync("still editable");
    }

    [Fact]
    [PlayTestOrientation(ScreenOrientation.Portrait)]
    public async Task Nested_dock_panes_leave_an_editable_center_in_portrait()
    {
        await TogglePanelAsync("Viewers", "Music View");
        await TogglePanelAsync("Structure", "Documents");
        await Expect(Page.GetByType<TriPaneView>()).ToHaveCountAsync(2);
        var bounds = await Editor.BoundingBoxAsync();
        bounds.Width.Should().BeGreaterThanOrEqualTo(190);
        await Editor.PressSequentiallyAsync("c4 d e f");
        await Expect(Editor).ToHaveValueAsync("c4 d e f");
    }
}
