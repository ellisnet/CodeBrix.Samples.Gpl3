// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The score-level container, the output definition it is laid out under, and the
/// spanner-bound rule that first light turned up.
/// </summary>
public class PaperScoreTests
{
    private static Symbol Sym(string name) => Symbol.Intern(name);

    private static object Alist(params (string Key, object Value)[] entries)
    {
        object result = Nil.Instance;
        for (int i = entries.Length - 1; i >= 0; i--)
        {
            result = new Pair(new Pair(Sym(entries[i].Key), entries[i].Value), result);
        }

        return result;
    }

    private static object GrobBasics(params (string Key, object Value)[] extra)
    {
        System.Collections.Generic.List<(string, object)> entries
            = new System.Collections.Generic.List<(string, object)>
            {
                ("meta", Alist(("name", Sym("TestGrob")), ("interfaces", Nil.Instance))),
            };
        entries.AddRange(extra);
        return Alist(entries.ToArray());
    }

    private static SystemGrob NewSystem()
        => new SystemGrob(GrobBasics(("axes", Pair.List(0L, 1L))));

    [Fact]
    public void a_system_never_takes_its_own_bound_as_horizontal_parent()
    {
        //Arrange
        // THE DEFECT FIRST LIGHT FOUND. A paper column's X parent is the system, so a
        // system that took its left bound as X parent would close the cycle
        // column -> system -> column -> ... Nothing detects that: the next walk up a
        // parent chain -- Grob::common_refpoint, the very first loop in it -- simply
        // never terminates, and the whole engrave spins at 100% CPU with no error and
        // no allocation growth. Upstream guards it explicitly and says why; the port
        // had inherited the COMMENT but tested "X parent is null" instead of
        // "this is not a System", which is true the first time and so lets the cycle
        // form.
        SystemGrob system = NewSystem();
        PaperColumn column = new PaperColumn(GrobBasics());
        system.TypesetGrob(column);
        system.AddColumn(column);

        //Act
        system.SetBound(Direction.Negative, column);

        //Assert
        column.XParent.Should().BeSameAs(system);
        system.XParent.Should().BeNull();
        system.GetBound(Direction.Negative).Should().BeSameAs(column);

        // The real proof: a walk up the parent chain terminates.
        column.CommonRefpoint(system, Axis.X).Should().BeSameAs(system);
    }

    [Fact]
    public void an_ordinary_spanner_does_take_its_left_bound_as_horizontal_parent()
    {
        //Arrange
        // The other half of the same rule -- the guard must not be so broad that it
        // stops every spanner from anchoring itself.
        SystemGrob system = NewSystem();
        PaperColumn column = new PaperColumn(GrobBasics());
        system.TypesetGrob(column);
        system.AddColumn(column);

        Spanner staffSymbol = new Spanner(GrobBasics());

        //Act
        staffSymbol.SetBound(Direction.Negative, column);

        //Assert
        staffSymbol.XParent.Should().BeSameAs(column);
    }

    [Fact]
    public void a_spanner_whose_parent_is_already_a_spanner_keeps_it()
    {
        //Arrange
        // Upstream keeps a SPANNER parent because it is split at line breaks too, and
        // the original is what later alignment measures against.
        Spanner outer = new Spanner(GrobBasics());
        Spanner inner = new Spanner(GrobBasics());
        inner.SetParent(outer, Axis.X);

        PaperColumn column = new PaperColumn(GrobBasics());

        //Act
        inner.SetBound(Direction.Negative, column);

        //Assert
        inner.XParent.Should().BeSameAs(outer);
    }

    [Fact]
    public void a_paper_score_adopts_the_first_system_as_its_root()
    {
        //Arrange
        OutputDef layout = PaperDefaults.Create();
        PaperScore score = new PaperScore(layout);
        SystemGrob first = NewSystem();
        SystemGrob second = NewSystem();

        //Act
        score.TypesetSystem(first);
        score.TypesetSystem(second);

        //Assert
        // Only the FIRST becomes the root; later ones are the pieces line breaking
        // makes, and they are told which score they belong to without displacing it.
        score.RootSystem.Should().BeSameAs(first);
        second.PaperScore.Should().BeSameAs(score);
        second.Layout.Should().BeSameAs(layout);
    }

    [Fact]
    public void the_default_paper_derives_lilyponds_own_staff_size_numbers()
    {
        //Arrange / Act
        OutputDef paper = PaperDefaults.Create();

        //Assert
        // A 20pt staff is four staff spaces, so output-scale and staff-space are both
        // staff-height/4. These are translated from scm/paper.scm, not chosen, and the
        // font selection reads staff-height directly.
        double pt = Dimensions.Point;
        paper.GetDimension("staff-height").Should().BeApproximately(20 * pt, 1e-9);
        paper.GetDimension("staff-space").Should().BeApproximately(20 * pt / 4, 1e-9);
        paper.GetDimension("output-scale").Should().BeApproximately(20 * pt / 4, 1e-9);
        paper.GetDimension("line-thickness").Should().BeGreaterThan(0);
    }

    [Fact]
    public void an_unset_variable_is_undefined_rather_than_the_empty_list()
    {
        //Arrange
        // Upstream distinguishes SCM_UNDEFINED from '(), and several callers test which
        // they got. The port answers null for "never set", which is a different value
        // from Nil and has to stay that way.
        OutputDef paper = new OutputDef();

        //Act / Assert
        paper.LookupVariable(Sym("no-such-variable")).Should().BeNull();

        paper.SetVariable("no-such-variable", Nil.Instance);
        paper.LookupVariable(Sym("no-such-variable")).Should().BeSameAs(Nil.Instance);
    }

    [Fact]
    public void variables_fall_through_to_the_parent_definition()
    {
        //Arrange
        // This is what lets a \layout override two settings and inherit the rest from
        // the \paper above it.
        OutputDef paper = new OutputDef();
        paper.SetVariable("staff-space", 1.75);
        paper.SetVariable("line-thickness", 0.1);

        OutputDef layout = new OutputDef { Parent = paper };
        layout.SetVariable("line-thickness", 0.2);

        //Act / Assert
        layout.GetDimension("staff-space").Should().Be(1.75);
        layout.GetDimension("line-thickness").Should().Be(0.2);
        paper.GetDimension("line-thickness").Should().Be(0.1);
    }

    [Fact]
    public void the_emmentaler_design_size_chosen_is_the_closest_by_ratio()
    {
        //Arrange
        // feta-design-size-mapping records that the file called "20" really is 20.0 and
        // the one called "18" is 17.82. Closeness is measured as a RATIO rather than a
        // difference, so the choice does not drift with absolute size.

        //Act
        int twenty = FontInterface.BestRoundedDesignSize(20.0, out double twentyActual);
        int eleven = FontInterface.BestRoundedDesignSize(11.2, out double _);
        int large = FontInterface.BestRoundedDesignSize(26.0, out double _);

        //Assert
        twenty.Should().Be(20);
        twentyActual.Should().Be(20.0);
        eleven.Should().Be(11);
        large.Should().Be(26);
    }
}
