// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.TerminalView.Rendering;
using CodeBrix.Terminal.Engine;
using SilverAssertions;
using SkiaSharp;
using Xunit;

namespace Lily.Shell.TerminalView.Tests;

public class AttributeDecoderTests
{
    private static readonly SKColor Fore = new(0xff, 0xff, 0xff);
    private static readonly SKColor Back = new(0x00, 0x00, 0x00);

    private static int Pack(FLAGS flags, int fg, int bg) => ((int)flags << 18) | (fg << 9) | bg;

    [Fact]
    public void the_default_attribute_uses_the_default_colors()
    {
        //Act
        var style = AttributeDecoder.Decode(CharData.DefaultAttr, Fore, Back);

        //Assert
        style.Foreground.Should().Be(Fore);
        style.Background.Should().Be(Back);
        style.Bold.Should().Be(false);
        style.Italic.Should().Be(false);
        style.Underline.Should().Be(false);
    }

    [Fact]
    public void palette_indices_resolve_to_ansi_colors()
    {
        //Arrange - fg 1 is the dark red of the default palette
        var attribute = Pack(0, 1, 256);

        //Act
        var style = AttributeDecoder.Decode(attribute, Fore, Back);

        //Assert
        var expected = Color.DefaultAnsiColors[1];
        style.Foreground.Should().Be(new SKColor(expected.Red, expected.Green, expected.Blue));
        style.Background.Should().Be(Back);
    }

    [Fact]
    public void bold_promotes_dark_palette_colors_to_bright()
    {
        //Arrange
        var attribute = Pack(FLAGS.BOLD, 1, 256);

        //Act
        var style = AttributeDecoder.Decode(attribute, Fore, Back);

        //Assert - bright red (index 9), and the bold face flag
        var expected = Color.DefaultAnsiColors[9];
        style.Foreground.Should().Be(new SKColor(expected.Red, expected.Green, expected.Blue));
        style.Bold.Should().Be(true);
    }

    [Fact]
    public void inverse_swaps_foreground_and_background()
    {
        //Arrange
        var attribute = Pack(FLAGS.INVERSE, 256, 256);

        //Act
        var style = AttributeDecoder.Decode(attribute, Fore, Back);

        //Assert
        style.Foreground.Should().Be(Back);
        style.Background.Should().Be(Fore);
    }

    [Fact]
    public void dim_darkens_the_foreground()
    {
        //Arrange
        var attribute = Pack(FLAGS.DIM, 256, 256);

        //Act
        var style = AttributeDecoder.Decode(attribute, Fore, Back);

        //Assert
        style.Foreground.Should().Be(new SKColor(153, 153, 153));
    }

    [Fact]
    public void invisible_paints_text_in_the_background_color()
    {
        //Arrange
        var attribute = Pack(FLAGS.INVISIBLE, 256, 256);

        //Act
        var style = AttributeDecoder.Decode(attribute, Fore, Back);

        //Assert
        style.Foreground.Should().Be(Back);
    }

    [Fact]
    public void decoration_flags_map_through()
    {
        //Arrange
        var attribute = Pack(FLAGS.UNDERLINE | FLAGS.ITALIC | FLAGS.CrossedOut, 256, 256);

        //Act
        var style = AttributeDecoder.Decode(attribute, Fore, Back);

        //Assert
        style.Underline.Should().Be(true);
        style.Italic.Should().Be(true);
        style.CrossedOut.Should().Be(true);
    }

    [Fact]
    public void a_colored_background_is_reported_visible()
    {
        //Arrange
        var attribute = Pack(0, 256, 4);

        //Act
        var style = AttributeDecoder.Decode(attribute, Fore, Back);

        //Assert
        style.HasVisibleBackground(Back).Should().Be(true);
        AttributeDecoder.Decode(CharData.DefaultAttr, Fore, Back)
            .HasVisibleBackground(Back).Should().Be(false);
    }
}
