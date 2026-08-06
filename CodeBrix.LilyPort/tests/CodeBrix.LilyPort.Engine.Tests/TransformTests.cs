// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The affine transform, and the font assets it shares a session with.
/// </summary>
public class TransformTests
{
    [Fact]
    public void the_identity_transform_leaves_a_point_alone()
    {
        //Arrange
        Transform identity = Transform.Identity;

        //Act
        Offset moved = identity.Apply(new Offset(3, 4));

        //Assert
        moved.X.Should().Be(3);
        moved.Y.Should().Be(4);
    }

    [Fact]
    public void a_default_transform_reads_back_as_the_identity()
    {
        //Arrange
        // default(Transform) has all-zero fields, and a zero matrix collapses every
        // point to the origin. The diagonal has to read back as 1.
        Transform uninitialized = default;

        //Act
        Offset moved = uninitialized.Apply(new Offset(3, 4));

        //Assert
        moved.X.Should().Be(3);
        moved.Y.Should().Be(4);
    }

    [Fact]
    public void a_quarter_turn_is_exact_rather_than_nearly_right()
    {
        //Arrange
        // Built from Offset.Directed rather than from sin and cos, which is why the
        // right angles come out exact. Upstream makes the same choice, with the comment
        // that Pango's own rotate "does not bother maintaining sane behavior at
        // multiples of 45 degrees".
        Transform quarter = new Transform(90.0, Offset.Zero);

        //Act
        Offset moved = quarter.Apply(new Offset(1, 0));

        //Assert
        moved.X.Should().Be(0.0);
        moved.Y.Should().Be(1.0);
    }

    [Fact]
    public void rotation_is_counter_clockwise_because_y_increases_upwards()
    {
        //Arrange
        Transform quarter = new Transform(90.0, Offset.Zero);

        //Act
        Offset moved = quarter.Apply(new Offset(0, 1));

        //Assert
        // LilyPond's y axis increases UPWARDS where Pango's increases downwards, so the
        // sense of the rotation is opposite to Pango's documentation.
        moved.X.Should().Be(-1.0);
        moved.Y.Should().Be(0.0);
    }

    [Fact]
    public void rotating_about_a_centre_leaves_the_centre_where_it_was()
    {
        //Arrange
        Offset centre = new Offset(5, 7);
        Transform rotation = new Transform(37.0, centre);

        //Act
        Offset moved = rotation.Apply(centre);

        //Assert
        Math.Abs(moved.X - centre.X).Should().BeLessThan(1e-9);
        Math.Abs(moved.Y - centre.Y).Should().BeLessThan(1e-9);
    }

    [Fact]
    public void composition_applies_the_argument_first()
    {
        //Arrange
        Transform translate = new Transform(new Offset(10, 0));
        Transform scale = default;
        scale.Scale(2, 2);

        //Act
        // scale.Apply(translate) means: translate, then scale.
        Offset moved = scale.Apply(translate).Apply(new Offset(1, 0));

        //Assert
        moved.X.Should().Be(22.0);
    }

    [Fact]
    public void every_vendored_font_is_reachable_from_the_assembly()
    {
        //Arrange
        string[] music =
        {
            "emmentaler-11", "emmentaler-13", "emmentaler-14", "emmentaler-16",
            "emmentaler-18", "emmentaler-20", "emmentaler-23", "emmentaler-26",
            "emmentaler-brace",
        };

        //Act
        int found = 0;
        int outlines = 0;
        foreach (string name in music)
        {
            if (FontAssets.MusicFont(name) != null)
            {
                found++;
            }

            if (FontAssets.OutlineFont(name) != null)
            {
                outlines++;
            }
        }

        //Assert
        // The whole point of embedding: this cannot depend on where the test binary
        // sits, which is what the old directory probe got wrong.
        found.Should().Be(music.Length);
        outlines.Should().Be(music.Length);

        // D13's 24 text faces, six families of four.
        System.Linq.Enumerable.Count(FontAssets.TextFontNames()).Should().Be(24);
    }
}
