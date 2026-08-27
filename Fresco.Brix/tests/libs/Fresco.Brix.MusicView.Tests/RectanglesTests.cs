using Fresco.Brix.MusicView;
using SilverAssertions;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fresco.Brix.MusicView.Tests;

public class RectanglesTests
{
    private sealed record Box(string Name, float Left, float Top, float Right, float Bottom);

    private sealed class Boxes : Rectangles<Box>
    {
        public Boxes(IEnumerable<Box> boxes)
            : base(boxes)
        {
        }

        protected override (float Left, float Top, float Right, float Bottom) GetCoords(Box obj)
            => (obj.Left, obj.Top, obj.Right, obj.Bottom);
    }

    private static Boxes Sample() => new Boxes(new[]
    {
        new Box("a", 0, 0, 10, 10),
        new Box("b", 20, 0, 30, 10),
        new Box("c", 0, 20, 10, 30),
        new Box("big", 0, 0, 30, 30),
    });

    [Fact]
    public void at_finds_every_rectangle_touching_the_point()
    {
        //Arrange
        var boxes = Sample();

        //Act
        var found = boxes.At(5, 5).Select(b => b.Name).OrderBy(n => n).ToList();

        //Assert
        found.Should().Equal("a", "big");
    }

    [Fact]
    public void at_finds_nothing_outside_every_rectangle()
    {
        //Arrange
        var boxes = Sample();

        //Act
        var found = boxes.At(100, 100);

        //Assert
        found.Should().BeEmpty();
    }

    [Fact]
    public void inside_finds_only_wholly_enclosed_rectangles()
    {
        //Arrange
        var boxes = Sample();

        //Act
        var found = boxes.Inside(0, 0, 15, 15).Select(b => b.Name).OrderBy(n => n).ToList();

        //Assert
        found.Should().Equal("a");
    }

    [Fact]
    public void intersecting_finds_rectangles_that_merely_overlap()
    {
        //Arrange
        var boxes = Sample();

        //Act
        var found = boxes.Intersecting(5, 5, 25, 25).Select(b => b.Name).OrderBy(n => n).ToList();

        //Assert
        found.Should().Equal("a", "b", "big", "c");
    }

    [Fact]
    public void width_orders_the_results_smallest_first()
    {
        //Arrange
        var boxes = Sample();

        //Act
        var ordered = boxes.At(5, 5).OrderBy(boxes.Width).Select(b => b.Name).ToList();

        //Assert
        ordered.Should().Equal("a", "big");
    }

    [Fact]
    public void nearest_finds_the_closest_rectangle_the_point_misses()
    {
        //Arrange
        var boxes = new Boxes(new[]
        {
            new Box("left", 0, 0, 10, 10),
            new Box("far", 200, 200, 210, 210),
        });

        //Act
        var found = boxes.Nearest(15, 5);

        //Assert
        found.Name.Should().Be("left");
    }

    [Fact]
    public void adding_one_keeps_the_index_usable()
    {
        //Arrange
        var boxes = Sample();
        boxes.At(5, 5); //builds the indexes

        //Act
        boxes.Add(new Box("late", 4, 4, 6, 6));

        //Assert
        boxes.At(5, 5).Select(b => b.Name).OrderBy(n => n).Should().Equal("a", "big", "late");
    }

    [Fact]
    public void removing_one_takes_it_out_of_the_index()
    {
        //Arrange
        var boxes = Sample();
        var a = boxes.First(b => b.Name == "a");
        boxes.At(5, 5);

        //Act
        boxes.Remove(a);

        //Assert
        boxes.At(5, 5).Select(b => b.Name).Should().Equal("big");
    }
}
