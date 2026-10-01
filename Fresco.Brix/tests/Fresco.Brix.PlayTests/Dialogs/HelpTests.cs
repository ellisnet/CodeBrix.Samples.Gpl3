using CodeBrix.Platform.PlayTest;
using SilverAssertions;
using Xunit;

namespace Fresco.Brix.PlayTests.Dialogs;

public sealed class HelpTests(AppFixture fixture) : FrescoTest(fixture)
{
    [Theory]
    [InlineData("About", "Fresco.Brix")]
    [InlineData("Credits", "Frescobaldi")]
    [InlineData("Version", "LilyPort")]
    public async Task About_pages_display_application_information(string page, string text)
    {
        await MenuAsync("Help", "About Fresco.Brix...");
        await Dialog.GetByRole(AriaRole.Button, new() { Name = page, Exact = true }).ClickAsync();
        await Expect(Dialog).ToContainTextAsync(text);
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "OK", Exact = true }).ClickAsync();
        await Expect(Dialog).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Built_in_user_guide_opens_without_a_web_browser()
    {
        await Editor.FillAsync("keep my score");
        await MenuAsync("Help", "User Guide");
        await Expect(Dialog).ToContainTextAsync("Fresco.Brix");
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
        (await TextAsync()).Should().Be("keep my score");
    }
}
