using CodeBrix.Platform.PlayTest;
using SilverAssertions;
using Xunit;

namespace Fresco.Brix.PlayTests.Editor;

public sealed class QuickInsertTests(AppFixture fixture) : FrescoTest(fixture)
{
    [Theory]
    [InlineData(true, "{ c4-. }")]
    [InlineData(false, "{ c4\\staccato }")]
    public async Task Articulation_buttons_edit_selected_music_and_respect_shorthands(bool shorthand, string expected)
    {
        await Editor.FillAsync("{ c4 }");
        await Editor.PressAsync("Control+a");
        await MenuItem("Tools").ClickAsync();
        await MenuItem("Coding").HoverAsync();
        await Page.GetByRole(AriaRole.Menuitemcheckbox, new() { Name = "Quick Insert", Exact = true }).CheckAsync();
        await Button("Articulations").ClickAsync();
        var direction = Page.GetByLabel("Direction:", new() { Exact = true });
        await direction.ClickAsync();
        await Page.GetByRole(AriaRole.Option, new() { Name = "Neutral", Exact = true }).ClickAsync();
        await Page.GetByRole(AriaRole.Checkbox, new() { Name = "Allow shorthands", Exact = true }).SetCheckedAsync(shorthand);
        var staccato = Button("Staccato");
        await staccato.ScrollIntoViewIfNeededAsync();
        await staccato.ClickAsync();
        await Expect(Editor).ToHaveValueAsync(expected);
        (await Page.EvaluateAsync(() => Model.Documents.CurrentDocument.IsModified)).Should().BeTrue();
        await Editor.PressAsync("Control+z");
        await Expect(Editor).ToHaveValueAsync("{ c4 }");
    }
}
