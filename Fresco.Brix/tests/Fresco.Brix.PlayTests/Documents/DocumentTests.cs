using CodeBrix.Platform.PlayTest;
using SilverAssertions;
using Xunit;

namespace Fresco.Brix.PlayTests.Documents;

public sealed class DocumentTests(AppFixture fixture) : FrescoTest(fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task New_document_from_menu_or_toolbar_adds_and_selects_a_tab(bool toolbar)
    {
        if (toolbar) await Tool("New Document").ClickAsync();
        else await MenuAsync("File", "New Document");
        await Expect(Tabs).ToHaveCountAsync(2);
        (await TextAsync()).Should().BeEmpty();
        (await Page.EvaluateAsync(() => Model.Documents.CurrentDocument == Model.Documents.Documents[1])).Should().BeTrue();
    }

    [Fact]
    public async Task Cancelled_open_preserves_current_document()
    {
        await Editor.FillAsync("c4 d e f");
        Fixture.Application.FilePickers.EnqueueOpenFile(null);
        await MenuAsync("File", "Open...");
        (await TextAsync()).Should().Be("c4 d e f");
        await Expect(Tabs).ToHaveCountAsync(1);
        Fixture.Application.FilePickers.OpenFileRequestCount.Should().Be(1);
    }

    [Theory]
    [InlineData("score.ly")]
    [InlineData("include.ily")]
    [InlineData("Unicode étude.LY")]
    public async Task Open_loads_source_and_exposes_the_file_on_its_tab(string name)
    {
        var text = "\\version \"2.24.3\"\n{ c'4 d' e' f' }\n% café ♯";
        var path = await OpenScoreAsync(text, name);
        (await Editor.InputValueAsync()).Should().Be(text);
        (await Page.EvaluateAsync(() => Model.Documents.CurrentDocument.IsModified)).Should().BeFalse();
        (await Page.EvaluateAsync(() => Model.RecentFiles.Paths())).Should().Contain(path);
        await Expect(Tabs.Filter(new() { HasText = name })).ToHaveCountAsync(1);
    }

    [Fact]
    public async Task Save_writes_edited_source_and_clears_modified_state()
    {
        await Editor.FillAsync("{ c'4 d' e' f' }");
        var path = TestPath("saved score.ly");
        Fixture.Application.FilePickers.EnqueueSaveFile(path);
        await MenuAsync("File", "Save Document");
        await WaitAsync(() => Model.Documents.CurrentDocument.IsModified, modified => !modified);
        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Should().Contain("{ c'4 d' e' f' }");
        (await Page.EvaluateAsync(() => Model.Documents.CurrentDocument.Path)).Should().Be(path);
    }

    [Fact]
    public async Task Cancelled_save_keeps_the_unsaved_document_modified()
    {
        await Editor.FillAsync("c4");
        Fixture.Application.FilePickers.EnqueueSaveFile(null);
        await Editor.PressAsync("Control+s");
        (await Page.EvaluateAsync(() => Model.Documents.CurrentDocument.IsModified)).Should().BeTrue();
        (await Page.EvaluateAsync(() => Model.Documents.CurrentDocument.Path)).Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task Save_as_changes_the_path_without_overwriting_the_original()
    {
        var original = await OpenScoreAsync("c4");
        await Editor.FillAsync("d4");
        var copy = TestPath("alternate.ly");
        Fixture.Application.FilePickers.EnqueueSaveFile(copy);
        await MenuAsync("File", "Save", "Save As...");
        await WaitAsync(() => Model.Documents.CurrentDocument.Path, path => path == copy);
        (await File.ReadAllTextAsync(original, TestContext.Current.CancellationToken)).Should().Be("c4");
        (await File.ReadAllTextAsync(copy, TestContext.Current.CancellationToken)).Should().Contain("d4");
    }

    [Theory]
    [InlineData("Cancel", true)]
    [InlineData("Discard", false)]
    public async Task Closing_a_modified_document_honors_the_answer(string answer, bool keep)
    {
        await Editor.FillAsync("% unsaved music");
        var document = await Page.EvaluateAsync(() => Model.Documents.CurrentDocument);
        await MenuAsync("File", "Close Document");
        await Expect(Dialog).ToBeVisibleAsync();
        await Dialog.GetByRole(AriaRole.Button, new() { Name = answer, Exact = true }).ClickAsync();
        await Expect(Dialog).ToHaveCountAsync(0);
        (await Page.EvaluateAsync(() => Model.Documents.Documents.Contains(document))).Should().Be(keep);
    }

    [Fact]
    public async Task Closing_with_save_writes_the_file_before_removing_the_document()
    {
        await Editor.FillAsync("{ g'1 }");
        var document = await Page.EvaluateAsync(() => Model.Documents.CurrentDocument);
        var path = TestPath("save-before-close.ly");
        Fixture.Application.FilePickers.EnqueueSaveFile(path);
        await MenuAsync("File", "Close Document");
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true }).ClickAsync();
        await WaitAsync(() => Model.Documents.Documents.Contains(document), contains => !contains);
        (await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Should().Contain("g'1");
    }

    [Fact]
    public async Task Tab_click_switches_between_independent_documents()
    {
        await Editor.FillAsync("first");
        await MenuAsync("File", "New Document");
        await Editor.FillAsync("second");
        await Tabs.First.ClickAsync();
        await Expect(Editor).ToHaveValueAsync("first");
        await Tabs.Last.ClickAsync();
        await Expect(Editor).ToHaveValueAsync("second");
    }

    [Fact]
    public async Task Reopening_a_path_selects_its_existing_tab()
    {
        var path = await OpenScoreAsync("c4");
        var before = await Page.EvaluateAsync(() => Model.Documents.Documents.Count);
        Fixture.Application.FilePickers.EnqueueOpenFile(path);
        await MenuAsync("File", "Open...");
        (await Page.EvaluateAsync(() => Model.Documents.Documents.Count)).Should().Be(before);
        (await Page.EvaluateAsync(() => Model.Documents.CurrentDocument.Path)).Should().Be(path);
    }

    [Fact]
    public async Task Unimplemented_insert_file_command_is_disabled()
    {
        await MenuItem("Edit").ClickAsync();
        await Expect(MenuItem("Insert from File...")).ToBeDisabledAsync();
    }
}
