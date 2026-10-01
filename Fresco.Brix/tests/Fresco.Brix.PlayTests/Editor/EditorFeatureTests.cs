using CodeBrix.Platform.PlayTest;
using CodeBrix.Platform.UI.AdvancedTextEdit.CodeCompletion;
using Fresco.Brix.Tools;
using SilverAssertions;
using Xunit;

namespace Fresco.Brix.PlayTests.Editor;

public sealed class EditorFeatureTests(AppFixture fixture) : FrescoTest(fixture)
{
    [Fact]
    public async Task Automatic_completion_inserts_a_selected_LilyPond_command()
    {
        await Editor.PressSequentiallyAsync("\\relative");
        await Editor.PressAsync("Escape");
        await Editor.FillAsync("\\rel");
        await MenuAsync("Tools", "Show Completions Popup");
        await Expect(Page.GetByType<CompletionList>()).ToHaveCountAsync(1);
        await Editor.PressAsync("Enter");
        await Expect(Editor).ToHaveValueAsync("\\relative");
        await Expect(Page.GetByType<CompletionList>()).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Escape_closes_completion_without_changing_the_prefix()
    {
        await Editor.FillAsync("\\rel");
        await MenuAsync("Tools", "Show Completions Popup");
        await Expect(Page.GetByType<CompletionList>()).ToHaveCountAsync(1);
        await Editor.PressAsync("Escape");
        await Expect(Page.GetByType<CompletionList>()).ToHaveCountAsync(0);
        await Expect(Editor).ToHaveValueAsync("\\rel");
    }

    [Fact]
    public async Task Fold_all_and_unfold_all_preserve_source_text()
    {
        var source = "\\relative c' {\n  c4 d e f\n}\n";
        await Editor.FillAsync(source);
        await WaitAsync(() => Fixture.ActiveEditor.FoldingManager.AllFoldings.Count(), count => count > 0);
        await MenuAsync("View", "Folding", "Fold All");
        (await Page.EvaluateAsync(() => Fixture.ActiveEditor.FoldingManager.AllFoldings.All(f => f.IsFolded))).Should().BeTrue();
        await MenuAsync("View", "Folding", "Unfold All");
        (await Page.EvaluateAsync(() => Fixture.ActiveEditor.FoldingManager.AllFoldings.Any(f => f.IsFolded))).Should().BeFalse();
        (await TextAsync()).Should().Be(source);
    }

    [Fact]
    public async Task Bookmark_toggle_and_clear_update_document_marks()
    {
        await Editor.FillAsync("c4\nd4\ne4");
        await Editor.PressAsync("Control+Home");
        await MenuItem("View").ClickAsync();
        await Page.GetByRole(AriaRole.Menuitemcheckbox, new() { Name = "Mark Current Line", Exact = true }).CheckAsync();
        (await Page.EvaluateAsync(() => Bookmarks.For(Model.Documents.CurrentDocument).MarkedLines())).Should().Equal(0);
        await MenuAsync("View", "Clear All Marks");
        (await Page.EvaluateAsync(() => Bookmarks.For(Model.Documents.CurrentDocument).MarkedLines())).Should().BeEmpty();
    }

    [Theory]
    [InlineData("Wrap Lines")]
    [InlineData("Line Numbers")]
    public async Task View_toggle_updates_the_actual_editor_and_can_be_restored(string name)
    {
        var before = await Page.EvaluateAsync(() => name == "Wrap Lines" ? Fixture.ActiveEditor.Editor.WordWrap : Fixture.ActiveEditor.Editor.ShowLineNumbers);
        await MenuItem("View").ClickAsync();
        await Page.GetByRole(AriaRole.Menuitemcheckbox, new() { Name = name, Exact = true }).SetCheckedAsync(!before);
        (await Page.EvaluateAsync(() => name == "Wrap Lines" ? Fixture.ActiveEditor.Editor.WordWrap : Fixture.ActiveEditor.Editor.ShowLineNumbers)).Should().Be(!before);
        await MenuItem("View").ClickAsync();
        await Page.GetByRole(AriaRole.Menuitemcheckbox, new() { Name = name, Exact = true }).SetCheckedAsync(before);
    }
}
