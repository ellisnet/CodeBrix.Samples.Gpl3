using CodeBrix.Platform.PlayTest;
using SilverAssertions;
using Xunit;

namespace Fresco.Brix.PlayTests.Dialogs;

public sealed class ScoreWizardTests(AppFixture fixture) : FrescoTest(fixture)
{
    [Fact]
    public async Task Adding_a_violin_part_builds_a_staff_in_the_new_score()
    {
        await OpenAsync();
        await WizardButton("Parts").ClickAsync();
        var available = Dialog.GetByRole(AriaRole.Tree).First;
        var strings = available.GetByRole(AriaRole.Treeitem, new() { Name = "Strings", Exact = true });
        await strings.ClickAsync();
        await strings.PressAsync("ArrowRight");
        await available.GetByRole(AriaRole.Treeitem, new() { Name = "Violin", Exact = true }).ClickAsync(new() { ClickCount = 2 });
        (await Page.EvaluateAsync(() => Model.ScoreWizard.Model.Root.Children.Count)).Should().Be(1);
        await WizardButton("OK").ClickAsync();
        (await TextAsync()).Should().Contain("\\new Staff").And.Contain("violin");
    }

    private Locator WizardButton(string name) => Dialog.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    private async Task OpenAsync()
    {
        await Tool("Score Wizard...").ClickAsync();
        await WizardButton("Titles and Headers").ClickAsync();
        await WizardButton("Clear").ClickAsync();
        await WizardButton("Parts").ClickAsync();
        await WizardButton("Clear").ClickAsync();
        await WizardButton("Titles and Headers").ClickAsync();
    }

    [Fact]
    public async Task Accepting_headers_creates_a_new_unmodified_score()
    {
        await Editor.FillAsync("original");
        await OpenAsync();
        await Dialog.GetByLabel("Title:", new() { Exact = true }).FillAsync("Étude in C");
        await Dialog.GetByLabel("Composer:", new() { Exact = true }).FillAsync("PlayTest Composer");
        await Expect(Dialog.GetByType<Microsoft.UI.Xaml.Controls.TextBlock>().Filter(new() { HasText = "Étude in C" }).First).ToBeVisibleAsync();
        await WizardButton("OK").ClickAsync();
        await Expect(Tabs).ToHaveCountAsync(2);
        (await TextAsync()).Should().Contain("title = \"Étude in C\"").And.Contain("composer = \"PlayTest Composer\"").And.Contain("\\version");
        (await Page.EvaluateAsync(() => Model.Documents.CurrentDocument.IsModified)).Should().BeFalse();
        await Tabs.First.ClickAsync();
        await Expect(Editor).ToHaveValueAsync("original");
    }

    [Fact]
    public async Task Cancel_preserves_the_document_and_remembers_wizard_entries()
    {
        await Editor.FillAsync("c4");
        await OpenAsync();
        await Dialog.GetByLabel("Title:", new() { Exact = true }).FillAsync("Remember this title");
        await WizardButton("Cancel").ClickAsync();
        await Expect(Tabs).ToHaveCountAsync(1);
        await Expect(Editor).ToHaveValueAsync("c4");
        await Tool("Score Wizard...").ClickAsync();
        await Expect(Dialog.GetByLabel("Title:", new() { Exact = true })).ToHaveValueAsync("Remember this title");
        await WizardButton("Cancel").ClickAsync();
    }

    [Fact]
    public async Task Clear_headers_removes_the_entered_value_from_the_score()
    {
        await OpenAsync();
        await Dialog.GetByLabel("Title:", new() { Exact = true }).FillAsync("Erase this title");
        await WizardButton("Clear").ClickAsync();
        await Expect(Dialog.GetByLabel("Title:", new() { Exact = true })).ToHaveValueAsync("");
        await WizardButton("OK").ClickAsync();
        (await TextAsync()).Should().NotContain("Erase this title");
    }

    [Theory]
    [InlineData("Titles and Headers")]
    [InlineData("Parts")]
    [InlineData("Score settings")]
    public async Task Wizard_pages_are_reachable_with_the_dialog_buttons(string page)
    {
        await OpenAsync();
        await WizardButton(page).ClickAsync();
        await Expect(WizardButton("Clear")).ToBeEnabledAsync();
        await Expect(WizardButton("Preview")).ToBeEnabledAsync();
        await WizardButton("Cancel").ClickAsync();
        await Expect(Editor).ToHaveValueAsync("");
    }
}
