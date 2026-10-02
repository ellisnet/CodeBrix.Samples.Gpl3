using System.Threading.Tasks;
using CodeBrix.Platform.PlayTest;
using SilverAssertions;
using Xunit;

namespace Fresco.Brix.PlayTests.Editor;

public sealed class EditingTests(AppFixture fixture) : FrescoTest(fixture)
{
    [Theory]
    [InlineData("c4 d8 e f g2")]
    [InlineData("% Étude: ♯ ♭ — Ελληνικά")]
    [InlineData("hello world")]
    public async Task Typing_uses_the_editor_input_pipeline(string text)
    {
        await Editor.PressSequentiallyAsync(text);
        await Expect(Editor).ToHaveValueAsync(text);
        (await Page.EvaluateAsync(() => Model.Documents.CurrentDocument.IsModified)).Should().BeTrue();
    }

    [Fact]
    public async Task Whole_value_replacement_preserves_undo_and_redo()
    {
        await Editor.FillAsync("c4");
        await Editor.FillAsync("d4");
        await Editor.PressAsync("Control+z");
        await Expect(Editor).ToHaveValueAsync("c4");
        await Editor.PressAsync("Control+y");
        await Expect(Editor).ToHaveValueAsync("d4");
    }

    [Fact]
    public async Task Select_copy_cut_and_paste_use_the_isolated_clipboard()
    {
        await Editor.FillAsync("c4 d e f");
        await Editor.PressAsync("Control+a");
        await MenuAsync("Edit", "Copy");
        (await Page.ClipboardTextAsync()).Should().Be("c4 d e f");
        await MenuAsync("Edit", "Cut");
        await Expect(Editor).ToHaveValueAsync("");
        await MenuAsync("Edit", "Paste");
        await Expect(Editor).ToHaveValueAsync("c4 d e f");
    }

    [Fact]
    public async Task Arrow_selection_replaces_only_the_selected_characters()
    {
        await Editor.FillAsync("abcdef");
        await Editor.PressAsync("Control+Home");
        await Editor.PressAsync("Shift+ArrowRight");
        await Editor.PressAsync("Shift+ArrowRight");
        await Editor.PressSequentiallyAsync("XY");
        await Expect(Editor).ToHaveValueAsync("XYcdef");
    }

    [Theory]
    [InlineData("Backspace", "ab")]
    [InlineData("Control+Home", "abc")]
    public async Task Keyboard_editing_keeps_document_and_caret_in_sync(string key, string expected)
    {
        await Editor.FillAsync("abc");
        await Editor.PressAsync(key);
        await Expect(Editor).ToHaveValueAsync(expected);
        (await Page.EvaluateAsync(() => Fixture.ActiveEditor.Editor.CaretOffset)).Should().Be(key == "Backspace" ? 2 : 0);
    }

    [Fact]
    public async Task Enter_inserts_a_new_line_through_the_editing_command()
    {
        await Editor.PressSequentiallyAsync("c4\nd4");
        await Expect(Editor).ToHaveValueAsync("c4\nd4");
        (await Page.EvaluateAsync(() => Fixture.ActiveEditor.Line)).Should().Be(2);
    }

    [Fact]
    public async Task Go_to_line_dialog_moves_to_the_first_nonblank_character()
    {
        await Editor.FillAsync("c4\n  d4\ne4");
        await MenuAsync("View", "Go to Line...");
        await Dialog.GetByRole(AriaRole.Textbox).FillAsync("2");
        await Dialog.GetByRole(AriaRole.Button, new() { Name = "OK", Exact = true }).ClickAsync();
        (await Page.EvaluateAsync(() => (Fixture.ActiveEditor.Line, Fixture.ActiveEditor.Column))).Should().Be((2, 3));
    }

    [Fact]
    public async Task Editor_context_menu_is_opened_by_a_real_right_click()
    {
        await Editor.FillAsync("c4 d e f");
        await Editor.ClickAsync(new() { Button = MouseButton.Right, Position = new() { X = 100, Y = 25 } });
        await Expect(MenuItem("Select All")).ToBeVisibleAsync();
        await MenuItem("Select All").ClickAsync();
        (await Page.EvaluateAsync(() => Fixture.ActiveEditor.SelectedText)).Should().Be("c4 d e f");
    }
}
