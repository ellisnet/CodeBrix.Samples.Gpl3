// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.QuickInsert;
using Fresco.Brix.Services;
using Microsoft.UI.Xaml;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Windows.UI;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The two toolbars' icon sets: what ships, which set a theme asks for, and
/// ruling FR14's one cleaned file.
/// </summary>
public class IconThemeTests
{
    /// <summary>Every icon name the two window toolbars reference.</summary>
    public static TheoryData<string> IconNames()
    {
        TheoryData<string> data = new TheoryData<string>();
        foreach (string name in IconTheme.Names) { data.Add(name); }

        return data;
    }

    [Theory]
    [MemberData(nameof(IconNames))]
    public void every_toolbar_icon_ships_in_both_sets(string name)
    {
        //Arrange, Act
        bool light = IconTheme.Has(IconSet.Light, name);
        bool dark = IconTheme.Has(IconSet.Dark, name);

        //Assert — upstream's Light and Dark sets carry the same 110 names, and
        //the toolbars must be able to draw under either theme.
        light.Should().BeTrue();
        dark.Should().BeTrue();
    }

    [Theory]
    [MemberData(nameof(IconNames))]
    public void every_toolbar_icon_draws_something_in_both_sets(string name)
    {
        //Arrange
        Color ink = Color.FromArgb(0xff, 0x10, 0x10, 0x10);

        //Act
        byte[] light = SymbolIcons.PixelsOfResource(
            IconTheme.LightPrefix, name, ink, IconTheme.ToolbarIconSize);
        byte[] dark = SymbolIcons.PixelsOfResource(
            IconTheme.DarkPrefix, name, ink, IconTheme.ToolbarIconSize);

        //Assert — an SVG the renderer cannot read comes back null, and one that
        //draws nothing comes back fully transparent; neither is a usable icon.
        Opaque(light).Should().BeGreaterThan(0);
        Opaque(dark).Should().BeGreaterThan(0);
    }

    [Fact]
    public void the_icon_list_is_the_one_the_two_bars_reference()
    {
        //Arrange, Act
        IReadOnlyList<string> names = IconTheme.Names;

        //Assert — the Main Toolbar's eleven, the Music View Toolbar's four that
        //are not already among them (its page buttons reuse go-previous and
        //go-next), and the Manuscript Viewer panel toolbar's four that neither
        //window bar referenced.
        //was previously: the first fifteen only, before board wave W15 gave the
        //Manuscript Viewer a panel toolbar of its own.
        names.Should().BeEquivalentTo(new[]
        {
            "document-new", "document-open", "document-save", "document-close",
            "go-previous", "go-next", "edit-undo", "edit-redo",
            "tools-score-wizard", "lilypond-run", "lilypond-stop",
            "zoom-in", "zoom-out", "zoom-magnifier", "edit-clear",
            "help-contents", "reload", "rotate-left", "rotate-right",
        });
        names.Distinct(StringComparer.Ordinal).Count().Should().Be(names.Count);
    }

    [Theory]
    [InlineData(ElementTheme.Dark, IconSet.Dark)]
    [InlineData(ElementTheme.Light, IconSet.Light)]
    [InlineData(ElementTheme.Default, IconSet.Light)]
    public void the_theme_chooses_the_set(ElementTheme theme, IconSet expected)
    {
        //Arrange, Act
        IconSet set = IconTheme.SetFor(theme);

        //Assert — upstream's rule (icons/__init__.py update_theme): the dark
        //set when the window colour is darker than the text colour, the light
        //set otherwise. ElementTheme.Default cannot occur on an ActualTheme and
        //reads as light, which is what a palette with no answer looks like.
        set.Should().Be(expected);
        IconTheme.PrefixFor(set).Should().Be(
            expected == IconSet.Dark ? IconTheme.DarkPrefix : IconTheme.LightPrefix);
    }

    [Fact]
    public void a_dark_theme_draws_the_icons_light_and_a_light_theme_dark()
    {
        //Arrange, Act
        Color dark = IconTheme.ForegroundFor(ElementTheme.Dark);
        Color light = IconTheme.ForegroundFor(ElementTheme.Light);

        //Assert — the same two colours the Quick Insert panel uses for its own
        //glyphs, so an icon and a glyph beside each other agree.
        dark.R.Should().BeGreaterThan(light.R);
        dark.A.Should().Be(0xff);
        light.A.Should().Be(0xff);
    }

    [Fact]
    public void an_icon_that_is_not_in_the_sets_answers_nothing()
    {
        //Arrange, Act, Assert
        IconTheme.Has(IconSet.Light, "no-such-icon").Should().BeFalse();
        SymbolIcons.PixelsOfResource(
            IconTheme.LightPrefix, "no-such-icon",
            Color.FromArgb(0xff, 0, 0, 0), 24).Should().BeNull();
    }

    [Theory]
    [InlineData("light", 24)]
    [InlineData("light", 48)]
    [InlineData("light", 96)]
    [InlineData("dark", 24)]
    [InlineData("dark", 48)]
    [InlineData("dark", 96)]
    public void the_cleaned_wizard_icon_draws_what_upstream_draws(
        string set, int size)
    {
        //Arrange — ⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14). The
        //shipped tools-score-wizard.svg is NOT a byte-for-byte copy: upstream's
        //is 6,474,769 bytes (Light) / 6,474,783 (Dark) and 228,519 lines for a
        //24-pixel icon, because an Inkscape session left 20,760 orphan
        //<inkscape:path-effect> elements in its <defs>.
        //tools/iconclean/iconclean.py removes them with
        //`inkscape --export-plain-svg --vacuum-defs' and a pass that deletes
        //empty groups, giving 2,761 and 2,742 bytes.
        //
        //The fixture is upstream's own file, recorded gzip-compressed
        //(fixtures/icons/README.txt) — no test may name the reference checkout.
        Color ink = Color.FromArgb(0xff, 0x30, 0x60, 0x90);
        string prefix = set == "dark" ? IconTheme.DarkPrefix : IconTheme.LightPrefix;

        //Act — BOTH files go through the application's own renderer, the one
        //the toolbar buttons draw with.
        byte[] original;
        using (Stream file = File.OpenRead(Path.Combine(
            "fixtures", "icons",
            "tools-score-wizard." + set + ".upstream.svg.gz")))
        using (GZipStream expanded = new GZipStream(file, CompressionMode.Decompress))
        using (MemoryStream svg = new MemoryStream())
        {
            expanded.CopyTo(svg);
            svg.Position = 0;
            original = SymbolIcons.PixelsOfStream(svg, ink, size);
        }

        byte[] shipped = SymbolIcons.PixelsOfResource(
            prefix, IconTheme.DivergentIconName, ink, size);

        //Assert — every pixel, at every size.
        original.Should().NotBeNull();
        shipped.Should().NotBeNull();
        shipped.Length.Should().Be(original.Length);
        Difference(original, shipped).Should().Be(0);
    }

    [Fact]
    public void the_cleaned_wizard_icon_is_the_only_file_that_diverges()
    {
        //Arrange, Act, Assert — the ruling names exactly one file, and the
        //README beside the assets lists exactly one as cleaned.
        IconTheme.DivergentIconName.Should().Be("tools-score-wizard");
        IconTheme.Names.Should().Contain(IconTheme.DivergentIconName);
    }

    /// <summary>Answers how many pixels are not fully transparent.</summary>
    /// <param name="pixels">The BGRA pixels, or null.</param>
    /// <returns>The count, or -1 when there are no pixels at all.</returns>
    private static int Opaque(byte[] pixels)
    {
        if (pixels == null) { return -1; }

        int count = 0;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0) { count++; }
        }

        return count;
    }

    /// <summary>Answers the largest channel difference between two renders.</summary>
    /// <param name="left">One render.</param>
    /// <param name="right">The other.</param>
    /// <returns>The difference, 0 when they agree exactly.</returns>
    private static int Difference(byte[] left, byte[] right)
    {
        int worst = 0;
        for (int i = 0; i < left.Length; i++)
        {
            int apart = Math.Abs(left[i] - right[i]);
            if (apart > worst) { worst = apart; }
        }

        return worst;
    }
}
