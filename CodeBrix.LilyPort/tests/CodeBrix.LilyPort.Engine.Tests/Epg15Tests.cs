// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// EPG15's rules — the pure-property reader, the break-permission lattice and the shape a
/// skyline pair has to have in a grob property — asserted against HAND-COMPUTED values.
/// </summary>
/// <remarks>
/// Same rule EPG10, EPG11, EPG12, EPG14 and EPG20 set: never assert what the port happens
/// to produce. Every expectation below comes from upstream's own expression, read in the
/// pinned 2.27.2 source, so the test is able to disagree with the code.
/// </remarks>
[Collection(EngineGlobalStateCollection.Name)]
public class Epg15Tests
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

    private static Item MakeItem(params (string Key, object Value)[] extra)
    {
        List<(string, object)> entries = new List<(string, object)>
        {
            ("meta", Alist(("name", Sym("TestGrob")), ("interfaces", Nil.Instance))),
        };
        entries.AddRange(extra);
        return new Item(Alist(entries.ToArray()));
    }

    // ----- Grob::internal_get_pure_property (grob-property.cc) -----

    [Fact]
    public void a_plain_property_value_reads_the_same_way_pure_as_unpure()
    {
        //Arrange
        // Upstream falls off the end of internal_get_pure_property and returns the value
        // itself when it is neither a procedure nor an unpure-pure container.
        Epg8TestHarness.Loaded();
        Item item = MakeItem(("test-property", 7.5));

        //Act
        object pure = item.GetPureProperty(Sym("test-property"), 0, 4);

        //Assert
        pure.Should().Be(7.5);
    }

    [Fact]
    public void a_bare_procedure_answers_false_to_a_pure_read()
    {
        //Arrange
        // THE SURPRISING ONE, and it is upstream's: call_pure_function ends
        // `if (!ly_is_procedure (value)) return value; return SCM_BOOL_F;'. A bare
        // procedure carries no pure half, so upstream refuses to guess and answers #f
        // rather than calling it. An implementation that "sensibly" called the procedure
        // would pass any test that only asserted "something came back", so the fence is
        // both that the answer is #f AND that the procedure never ran -- it records a
        // Scheme variable when it does.
        Epg8TestHarness.Loaded();
        Epg8TestHarness.Eval("(define epg15-bare-ran #f)");
        object procedure = Epg8TestHarness.Eval(
            "(lambda (grob) (set! epg15-bare-ran #t) 3.0)");
        Item item = MakeItem(("test-property", procedure));

        //Act
        object pure = item.GetPureProperty(Sym("test-property"), 0, 4);

        //Assert
        pure.Should().Be(false);
        Epg8TestHarness.Eval("epg15-bare-ran").Should().Be(false);
    }

    [Fact]
    public void a_container_with_a_pure_half_calls_it_with_the_two_columns()
    {
        //Arrange
        // scm_apply_3 (value, car (args), to_scm (start), to_scm (end), cdr (args)):
        // the grob first, then the two column ranks. 11 and 29 are arbitrary and are
        // asserted exactly, because a pure callback given the wrong two numbers is the
        // failure this whole layer exists to avoid.
        Epg8TestHarness.Loaded();
        Epg8TestHarness.Eval("(define epg15-pure-args '())");
        object unpure = Epg8TestHarness.Eval("(lambda (grob) 1.0)");
        object pure = Epg8TestHarness.Eval(
            "(lambda (grob start end) (set! epg15-pure-args (list grob start end)) 2.0)");
        Item item = MakeItem(("test-property", new UnpurePureContainer(unpure, pure)));

        //Act
        object answer = item.GetPureProperty(Sym("test-property"), 11, 29);

        //Assert
        SchemeConvert.ToDouble(answer, "pure answer").Should().Be(2.0);
        Epg8TestHarness.Eval("(length epg15-pure-args)").Should().Be(3L);
        Epg8TestHarness.Eval("(car epg15-pure-args)").Should().BeSameAs(item);
        SchemeConvert.ToDouble(
            Epg8TestHarness.Eval("(cadr epg15-pure-args)"), "start").Should().Be(11.0);
        SchemeConvert.ToDouble(
            Epg8TestHarness.Eval("(caddr epg15-pure-args)"), "end").Should().Be(29.0);
    }

    [Fact]
    public void a_container_with_no_pure_half_is_read_as_an_ordinary_property()
    {
        //Arrange
        // Upstream's own comment: "Do cache, if the function ignores 'start' and 'end'."
        // A container whose pure half was omitted is is_unchanging (), so the pure read
        // becomes an ORDINARY read -- the unpure procedure called with the grob alone,
        // and no column ranks in sight.
        Epg8TestHarness.Loaded();
        Epg8TestHarness.Eval("(define epg15-unpure-args '())");
        object unpure = Epg8TestHarness.Eval(
            "(lambda (grob) (set! epg15-unpure-args (list grob)) 5.0)");
        Item item = MakeItem(("test-property", new UnpurePureContainer(unpure, null)));

        //Act
        object answer = item.GetPureProperty(Sym("test-property"), 11, 29);

        //Assert
        SchemeConvert.ToDouble(answer, "pure answer").Should().Be(5.0);
        Epg8TestHarness.Eval("(length epg15-unpure-args)").Should().Be(1L);
        Epg8TestHarness.Eval("(car epg15-unpure-args)").Should().BeSameAs(item);
    }

    // ----- Constrained_breaking's permission lattice -----

    [Fact]
    public void the_permission_lattice_answers_upstream_min_permission()
    {
        //Arrange
        // min_permission's whole truth table, hand-read off constrained-breaking.cc:
        //   force  + anything -> the OTHER one
        //   allow  + force    -> '()          (allow is not force, so the guard fails)
        //   allow  + allow    -> allow
        //   allow  + '()      -> '()
        //   '()    + anything -> '()
        // It is not symmetric and it is not a minimum in the ordinary sense, which is
        // exactly why it is worth a table rather than a paraphrase.
        object force = Symbol.Intern("force");
        object allow = Symbol.Intern("allow");
        object none = Nil.Instance;

        //Act / Assert
        ConstrainedBreaking.MinPermission(force, allow).Should().BeSameAs(allow);
        ConstrainedBreaking.MinPermission(force, force).Should().BeSameAs(force);
        ConstrainedBreaking.MinPermission(force, none).Should().BeSameAs(none);
        ConstrainedBreaking.MinPermission(allow, allow).Should().BeSameAs(allow);
        ConstrainedBreaking.MinPermission(allow, none).Should().BeSameAs(none);
        ConstrainedBreaking.MinPermission(allow, force).Should().Be(Nil.Instance);
        ConstrainedBreaking.MinPermission(none, force).Should().Be(Nil.Instance);
        ConstrainedBreaking.MinPermission(none, allow).Should().Be(Nil.Instance);
    }

    // ----- what a grob property may hold as a skyline pair -----

    [Fact]
    public void a_skyline_pair_reaches_a_property_as_a_cons_of_two_skylines()
    {
        //Arrange
        // scm/c++.scm defines ly:skyline-pair? as "a pair whose car and cdr are both
        // skylines" -- there is no skyline-pair type in Scheme at all. A callback that
        // answers the SkylinePair object instead fails its own property's type check and
        // the property is left unset, which is what happened to vertical-skylines on
        // every hara-kiri group until this session.
        SkylinePair pair = new SkylinePair();

        //Act
        object asScheme = pair.ToScheme();

        //Assert
        asScheme.Should().BeOfType<Pair>();
        ((Pair)asScheme).Car.Should().BeOfType<Skyline>();
        ((Pair)asScheme).Cdr.Should().BeOfType<Skyline>();

        // The other side of the fence: the object itself is NOT what a property may hold,
        // so a test that only checked "a skyline pair came back" would pass on the defect.
        pair.Should().NotBeOfType<Pair>();
    }

    [Fact]
    public void a_skyline_pair_survives_the_round_trip_through_a_property()
    {
        //Arrange
        // FromScheme (ToScheme (x)) has to give the two sides back the right way round.
        // The down/left skyline is the CAR, the up/right one the CDR.
        SkylinePair original = new SkylinePair();

        //Act
        SkylinePair read = SkylinePair.FromScheme(original.ToScheme());

        //Assert
        read.Should().NotBeNull();
        read.Down.Should().BeSameAs(original.Down);
        read.Up.Should().BeSameAs(original.Up);
    }
}
