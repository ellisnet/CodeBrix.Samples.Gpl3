using CodeBrix.Platform.PlayTest;
using Fresco.Brix.Preferences;
using SilverAssertions;
using Xunit;

namespace Fresco.Brix.PlayTests.Dialogs;

public sealed class PreferencesTests(AppFixture fixture) : FrescoTest(fixture)
{
    private async Task OpenGeneralAsync()
    {
        await MenuAsync("Edit", "Preferences...");
        await PreferencePage("General").ClickAsync();
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Reset", Exact = true }).ClickAsync();
    }
    private Locator PullDowns => Dialog.GetByRole(AriaRole.Checkbox, new() { Name = "Add pull-down menus in main toolbar", Exact = true });
    // The first list is the navigation sidebar; pages such as Shortcuts contain
    // their own lists with some of the same labels.
    private Locator PreferencePage(string name) => Dialog.GetByType<Microsoft.UI.Xaml.Controls.ListView>().First
        .GetByText(name, new() { Exact = true });

    [Theory]
    [InlineData("General", "Add pull-down menus in main toolbar")]
    [InlineData("Music View", "Continuous scrolling")]
    [InlineData("MIDI", "Instrument:")]
    [InlineData("Editor", "Wrap long lines by default")]
    [InlineData("Tools", "Show log when a job is started")]
    [InlineData("Paths", "Folders containing hyphenation dictionaries")]
    [InlineData("Documentation", "Show the contents list")]
    [InlineData("Shortcuts", null)]
    [InlineData("Fonts & Colors", "Font:")]
    [InlineData("Helper Apps", "File Manager:")]
    public async Task Each_preferences_page_is_reachable_and_cancel_keeps_the_editor(string page, string evidence)
    {
        await Editor.FillAsync("preserved document");
        await MenuAsync("Edit", "Preferences...");
        await PreferencePage(page).ClickAsync();
        if (evidence != null) await Expect(Dialog).ToContainTextAsync(evidence);
        else await Expect(Dialog.GetByRole(AriaRole.Textbox)).ToHaveCountAsync(1);
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await Expect(Editor).ToHaveValueAsync("preserved document");
    }

    [Fact]
    public async Task Cancel_discards_a_changed_preference()
    {
        await OpenGeneralAsync();
        await PullDowns.ScrollIntoViewIfNeededAsync();
        await PullDowns.CheckAsync();
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        (await Page.EvaluateAsync(() => Model.Settings.GetBool(GeneralValues.VerboseToolButtonsKey))).Should().BeFalse();
    }

    [Fact]
    public async Task Apply_saves_toolbar_preference_while_leaving_preferences_open()
    {
        await OpenGeneralAsync();
        await PullDowns.ScrollIntoViewIfNeededAsync();
        await PullDowns.CheckAsync();
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ClickAsync();
        await Expect(Dialog).ToBeVisibleAsync();
        (await Page.EvaluateAsync(() => Model.Settings.GetBool(GeneralValues.VerboseToolButtonsKey))).Should().BeTrue();
        await Expect(Dialog.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true })).ToBeDisabledAsync();
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        (await Page.EvaluateAsync(() => AppFixture.Descendants(Fixture.View).OfType<CodeBrix.Platform.UI.CommandBar.ToolDropDownButton>().Count())).Should().BeGreaterThanOrEqualTo(4);
    }

    [Fact]
    public async Task Reset_reverts_unsaved_preferences_in_the_dialog()
    {
        await OpenGeneralAsync();
        await PullDowns.ScrollIntoViewIfNeededAsync();
        await PullDowns.CheckAsync();
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Reset", Exact = true }).ClickAsync();
        await Expect(PullDowns).Not.ToBeCheckedAsync();
        await Expect(Dialog.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true })).ToBeDisabledAsync();
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
    }
}
