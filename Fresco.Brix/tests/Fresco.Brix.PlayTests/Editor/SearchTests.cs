using System.Threading.Tasks;
using CodeBrix.Platform.PlayTest;
using Fresco.Brix.Search;
using SilverAssertions;
using Xunit;

namespace Fresco.Brix.PlayTests.Editor;

public sealed class SearchTests(AppFixture fixture) : FrescoTest(fixture)
{
    private Locator Search => Page.GetByType<SearchBar>();
    private Locator Query => Search.GetByRole(AriaRole.Textbox).First;
    private Locator Replacement => Search.GetByRole(AriaRole.Textbox).Last;

    [Fact]
    public async Task Find_next_and_previous_move_the_editor_selection()
    {
        await Editor.FillAsync("c4 d4 c4 e4 c4");
        await Editor.PressAsync("Control+Home");
        await MenuAsync("Edit", "Find...");
        await Query.FillAsync("c4");
        await Query.PressAsync("Enter");
        var first = await Page.EvaluateAsync(() => Fixture.ActiveEditor.SelectionStart);
        first.Should().Be(6);
        await Query.PressAsync("Enter");
        var second = await Page.EvaluateAsync(() => Fixture.ActiveEditor.SelectionStart);
        second.Should().Be(12);
        (await Page.EvaluateAsync(() => Fixture.ActiveEditor.SelectedText)).Should().Be("c4");
        await Query.PressAsync("ArrowUp");
        (await Page.EvaluateAsync(() => Fixture.ActiveEditor.SelectionStart)).Should().Be(first);
        await Query.PressAsync("ArrowUp");
        (await Page.EvaluateAsync(() => Fixture.ActiveEditor.SelectionStart)).Should().Be(0);
        await Query.PressAsync("ArrowUp");
        (await Page.EvaluateAsync(() => Fixture.ActiveEditor.SelectionStart)).Should().Be(12);
    }

    [Theory]
    [InlineData("c4", "d8", "d8 e4 d8")]
    [InlineData("missing", "d8", "c4 e4 c4")]
    public async Task Replace_all_changes_matches_and_can_be_undone(string find, string replacement, string expected)
    {
        await Editor.FillAsync("c4 e4 c4");
        await MenuAsync("Edit", "Replace...");
        await Query.FillAsync(find);
        await Replacement.FillAsync(replacement);
        var replaceAll = Search.GetByRole(AriaRole.Button, new() { Name = "All", Exact = true });
        if (find == "missing") await Expect(replaceAll).ToBeDisabledAsync();
        else await replaceAll.ClickAsync();
        (await TextAsync()).Should().Be(expected);
        if (find == "c4")
        {
            await Query.PressAsync("Escape");
            await Editor.PressAsync("Control+z");
            await Expect(Editor).ToHaveValueAsync("c4 e4 c4");
        }
    }

    [Fact]
    public async Task Case_insensitive_replace_matches_both_letter_cases()
    {
        await Editor.FillAsync("Theme theme THEME");
        await MenuAsync("Edit", "Replace...");
        await Search.GetByRole(AriaRole.Checkbox, new() { Name = "Case", Exact = true }).UncheckAsync();
        await Query.FillAsync("theme");
        await Replacement.FillAsync("score");
        await Search.GetByRole(AriaRole.Button, new() { Name = "All", Exact = true }).ClickAsync();
        (await TextAsync()).Should().Be("score score score");
        await Search.GetByRole(AriaRole.Checkbox, new() { Name = "Case", Exact = true }).CheckAsync();
    }

    [Fact]
    public async Task Regex_replace_uses_capture_groups()
    {
        await Editor.FillAsync("c4 d8 e16");
        await MenuAsync("Edit", "Replace...");
        var regex = Search.GetByRole(AriaRole.Checkbox, new() { Name = "Regex", Exact = true });
        await regex.CheckAsync();
        await Query.FillAsync(@"([cde])(\d+)");
        await Replacement.FillAsync("$1'${2}");
        await Search.GetByRole(AriaRole.Button, new() { Name = "All", Exact = true }).ClickAsync();
        (await TextAsync()).Should().Be("c'4 d'8 e'16");
        await regex.UncheckAsync();
    }

    [Fact]
    public async Task Invalid_regex_leaves_the_document_unchanged()
    {
        await Editor.FillAsync("c4 d8");
        await MenuAsync("Edit", "Find...");
        var regex = Search.GetByRole(AriaRole.Checkbox, new() { Name = "Regex", Exact = true });
        await regex.CheckAsync();
        await Query.FillAsync("[");
        (await TextAsync()).Should().Be("c4 d8");
        await regex.UncheckAsync();
        await Query.FillAsync("d8");
        await Query.PressAsync("Enter");
        (await Page.EvaluateAsync(() => Fixture.ActiveEditor.SelectedText)).Should().Be("d8");
    }

    [Fact]
    public async Task Escape_closes_search_and_returns_keyboard_input_to_the_editor()
    {
        await MenuAsync("Edit", "Find...");
        await Query.FillAsync("query");
        await Query.PressAsync("Escape");
        await Expect(Search).ToHaveCountAsync(0);
        await Page.Keyboard.TypeAsync("c4");
        (await TextAsync()).Should().Be("c4");
    }
}
