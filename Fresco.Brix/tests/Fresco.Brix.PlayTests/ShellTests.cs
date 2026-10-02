using System.IO;
using System.Threading.Tasks;
using CodeBrix.Platform.PlayTest;
using SilverAssertions;
using Xunit;

namespace Fresco.Brix.PlayTests;

public sealed class ShellTests(AppFixture fixture) : FrescoTest(fixture)
{
    [Fact]
    public async Task Starts_with_one_empty_document_and_the_real_menu_bar()
    {
        (await Page.EvaluateAsync(() => Model.Documents.Documents.Count)).Should().Be(1);
        (await Page.EvaluateAsync(() => Model.Documents.CurrentDocument.Text)).Should().BeEmpty();
        await Expect(Page.GetByText("File", new() { Exact = true })).ToBeVisibleAsync();
        await Page.ScreenshotAsync(new() { Path = Path.Combine(Fixture.DataDirectory, "shell.png") });
    }
}
