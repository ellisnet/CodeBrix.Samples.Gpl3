using SilverAssertions;
using Xunit;

namespace Fresco.Brix.PlayTests.Editor;

public sealed class TransformationTests(AppFixture fixture) : FrescoTest(fixture)
{
    [Theory]
    [InlineData("Double durations", "c4 d8 e16", "c2 d4 e8")]
    [InlineData("Halve durations", "c4 d8 e16", "c8 d16 e32")]
    [InlineData("Dot durations", "c4 d8", "c4. d8.")]
    [InlineData("Undot durations", "c4. d8.", "c4 d8")]
    [InlineData("Remove durations", "c4 d8 e16", "c d e")]
    [InlineData("Make explicit", "c4 d e8 f", "c4 d4 e8 f8")]
    [InlineData("Make implicit", "c4 d4 e8 f8", "c4 d e8 f")]
    public async Task Rhythm_menu_transforms_selected_music_and_supports_undo(string command, string source, string expected)
    {
        source = "{ " + source + " }";
        expected = "{ " + expected + " }";
        await Editor.FillAsync(source);
        await Editor.PressAsync("Control+a");
        await MenuAsync("Tools", "Musical Transformations", "Rhythm", command);
        await Expect(Editor).ToHaveValueAsync(expected);
        await Editor.PressAsync("Control+z");
        await Expect(Editor).ToHaveValueAsync(source);
    }

    [Theory]
    [InlineData("Replace full measure rests with spacer rests", "R1 R2", "s1 s2")]
    [InlineData("Replace spacer rests with full measure rests", "s1 s2", "R1 R2")]
    public async Task Rest_menu_changes_the_notation(string command, string source, string expected)
    {
        source = "{ " + source + " }";
        expected = "{ " + expected + " }";
        await Editor.FillAsync(source);
        await MenuAsync("Tools", "Musical Transformations", "Rest", command);
        await Expect(Editor).ToHaveValueAsync(expected);
    }

    [Theory]
    [InlineData("Remove Comments", "c4 % comment\nd4", "c4 \nd4")]
    [InlineData("Remove Slurs", "c4( d4)", "c4 d4")]
    [InlineData("Remove Beams", "c8[ d8]", "c8 d8")]
    public async Task Quick_remove_operates_through_the_nested_menu(string command, string source, string expected)
    {
        source = "{ " + source + " }";
        expected = "{ " + expected + " }";
        await Editor.FillAsync(source);
        await Editor.PressAsync("Control+a");
        await MenuAsync("Tools", "Musical Transformations", "Quick Remove", command);
        await Expect(Editor).ToHaveValueAsync(expected);
    }

    [Fact]
    public async Task Removing_trailing_whitespace_preserves_music_and_line_breaks()
    {
        await Editor.FillAsync("c4   \nd4\t\n");
        await MenuAsync("Tools", "Code Formatting", "Remove Trailing Whitespace");
        (await TextAsync()).Should().Be("c4\nd4\n");
    }
}
