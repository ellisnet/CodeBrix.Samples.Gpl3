using CodeBrix.Platform.PlayTest;
using SilverAssertions;
using Xunit;

namespace Fresco.Brix.PlayTests.Dialogs;

public sealed class SessionTests(AppFixture fixture) : FrescoTest(fixture)
{
    [Fact]
    public async Task New_session_requires_a_name_and_stores_its_options()
    {
        await MenuAsync("Session", "New...");
        var ok = Dialog.GetByRole(AriaRole.Button, new() { Name = "OK", Exact = true });
        await Expect(ok).ToBeDisabledAsync();
        await Dialog.GetByRole(AriaRole.Textbox).Nth(0).FillAsync("PlayTest Session");
        await Dialog.GetByRole(AriaRole.Textbox).Nth(1).FillAsync(Fixture.TestDirectory);
        await Expect(ok).ToBeEnabledAsync();
        await ok.ClickAsync();
        await WaitAsync(() => Model.SessionStore.Exists("PlayTest Session"), exists => exists);
        (await Page.EvaluateAsync(() => Model.SessionStore.Read("PlayTest Session").BaseDirectory)).Should().Be(Fixture.TestDirectory);
    }

    [Fact]
    public async Task Cancelled_new_session_is_not_saved()
    {
        await MenuAsync("Session", "New...");
        await Dialog.GetByRole(AriaRole.Textbox).First.FillAsync("Cancelled session");
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        (await Page.EvaluateAsync(() => Model.SessionStore.Exists("Cancelled session"))).Should().BeFalse();
    }

    [Fact]
    public async Task Session_manager_opens_and_closes_without_changing_the_document()
    {
        await Editor.FillAsync("c4 d e f");
        await MenuAsync("Session", "Manage...");
        await Expect(Dialog).ToContainTextAsync("Manage Sessions");
        await Expect(Dialog.GetByRole(AriaRole.Button, new() { Name = "New...", Exact = true })).ToBeEnabledAsync();
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "Close", Exact = true }).ClickAsync();
        (await TextAsync()).Should().Be("c4 d e f");
    }
}
