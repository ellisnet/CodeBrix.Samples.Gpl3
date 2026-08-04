// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The vendored extraction of LilyPond's C++ <c>ADD_INTERFACE</c> declarations -- plan
/// decision O8, option C.
/// <para>
/// Upstream declares 86 grob interfaces from static initialisers in <c>lily/*.cc</c> and
/// a further 88 from Scheme. The port has no static initialisers to run, so it registers
/// the C++ half from a data table. These tests fence the table's shape and the name
/// derivation, because a silent error in either would present much later as a grob
/// property that cannot find its interface.
/// </para>
/// </summary>
public class GrobInterfaceTableTests
{
    // 86 ADD_INTERFACE macros across 77 lily/*.cc files at v2.27.2.
    private const int InterfacesDeclaredInCxx = 86;

    private const int UpstreamFilesDeclaringThem = 77;

    [Fact]
    public void the_table_carries_every_upstream_add_interface_declaration()
    {
        //Arrange & Act
        IReadOnlyList<GrobInterfaceDeclaration> all = GrobInterfaceTable.All;

        //Assert
        all.Count.Should().Be(InterfacesDeclaredInCxx);
        all.Select(entry => entry.UpstreamFile).Distinct().Count().Should().Be(UpstreamFilesDeclaringThem);
    }

    [Fact]
    public void every_declaration_is_complete_and_uniquely_named()
    {
        //Arrange & Act
        IReadOnlyList<GrobInterfaceDeclaration> all = GrobInterfaceTable.All;

        //Assert
        all.Select(entry => entry.Name).Distinct().Count().Should().Be(all.Count);
        foreach (GrobInterfaceDeclaration entry in all)
        {
            entry.Name.Should().EndWith("-interface");
            entry.CxxName.Should().NotBeNullOrEmpty();
            entry.UpstreamFile.Should().EndWith(".cc");
            entry.Description.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public void the_interface_symbol_is_derived_the_way_upstream_derives_it()
    {
        //Arrange
        // lily/grob-interface.cc add_interface: camel_case_to_lisp_identifier of the C++
        // class name, then "-interface" appended unless it is already the suffix. The
        // two cases that matter are a class that already carries the suffix and one that
        // does not, so both are pinned by a real upstream example.
        //Act
        GrobInterfaceDeclaration alreadySuffixed = GrobInterfaceTable.Declaration("accidental-interface");
        GrobInterfaceDeclaration needsSuffix = GrobInterfaceTable.Declaration("accidental-placement-interface");

        //Assert
        alreadySuffixed.CxxName.Should().Be("Accidental_interface");
        needsSuffix.CxxName.Should().Be("Accidental_placement");
    }

    [Fact]
    public void the_declaration_that_document_backend_needed_carries_its_property()
    {
        //Arrange
        // accidental-grobs was the first property document-backend could not find an
        // interface for once ly:all-grob-interfaces became a real hash table. It is
        // owned by Accidental_placement, declared only in C++ -- which is what made
        // this extraction the unblocking work rather than a tidiness exercise.
        //Act
        GrobInterfaceDeclaration declaration = GrobInterfaceTable.Declaration("accidental-placement-interface");

        //Assert
        declaration.Properties.Should().Contain("accidental-grobs");
        declaration.UpstreamFile.Should().Be("accidental-placement.cc");
    }

    [Fact]
    public void descriptions_keep_the_line_structure_upstream_wrote()
    {
        //Arrange
        // The table stores newlines escaped so each record is one line. If the unescape
        // were wrong, every multi-line description would silently become one long line
        // and the generated documentation would reflow wrongly -- visible only in output
        // nobody is generating yet, which is exactly the kind of error to pin now.
        //Act
        GrobInterfaceDeclaration beam = GrobInterfaceTable.Declaration("beam-interface");

        //Assert
        beam.Description.Should().StartWith("A beam.");
        beam.Description.Should().Contain("\n");
        beam.Description.Should().Contain("@table @code");
    }

    [Fact]
    public void registering_the_table_produces_upstream_entry_shape()
    {
        //Arrange
        // internal_add_interface stores ly_list (name, description, properties), and
        // document-backend reads the third element with caddr. Getting the order wrong
        // would present as a property list that is a string.
        EngineRegistries registries = new EngineRegistries();

        //Act
        GrobInterfaceTable.Register(registries);
        object entry = registries.GrobInterfaces[Symbol.Intern("accidental-placement-interface")];

        //Assert
        registries.GrobInterfaces.Count.Should().Be(InterfacesDeclaredInCxx);
        Pair pair = entry.Should().BeOfType<Pair>().Subject;
        pair.Car.Should().Be(Symbol.Intern("accidental-placement-interface"));
        Pair rest = ((Pair)pair.Cdr).Should().BeOfType<Pair>().Subject;
        rest.Car.Should().BeOfType<MutableString>();
        ((Pair)rest.Cdr).Car.Should().BeOfType<Pair>();
    }

    [Fact]
    public void the_assert_on_port_rider_accepts_a_real_interface_and_rejects_an_invented_one()
    {
        //Arrange
        // The O8 rider: a ported grob class asserts its interfaces against this table
        // instead of re-declaring them, so drift becomes a test failure rather than a
        // silent divergence.
        //Act
        IReadOnlyList<string> clean = GrobInterfaceTable.CheckPortedGrob(
            "Accidental_placement", new[] { "accidental-placement-interface" });
        IReadOnlyList<string> invented = GrobInterfaceTable.CheckPortedGrob(
            "Nonesuch", new[] { "definitely-not-an-interface" });

        //Assert
        clean.Should().BeEmpty();
        invented.Should().ContainSingle();
        invented[0].Should().Contain("Nonesuch");
    }

    [Fact]
    public void the_ported_grob_classes_agree_with_upstream_about_their_interfaces()
    {
        //Arrange
        // The rider applied to what is ported so far. Each entry is the interface the
        // ported class's upstream ADD_INTERFACE declares. Extend this as grob classes
        // are ported -- that is the mechanism O8 chose over re-declaration.
        Dictionary<string, string[]> ported = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Axis_group_interface"] = new[] { "axis-group-interface" },
            ["Item"] = new[] { "item-interface" },
            ["Spanner"] = new[] { "spanner-interface" },
            ["Paper_column"] = new[] { "paper-column-interface" },
            ["System"] = new[] { "system-interface" },
            ["Spaceable_grob"] = new[] { "spaceable-grob-interface" },
            ["Grob"] = new[] { "grob-interface" },
            ["Line_interface"] = new[] { "line-interface" },
        };

        //Act & Assert
        foreach (KeyValuePair<string, string[]> entry in ported)
        {
            GrobInterfaceTable.CheckPortedGrob(entry.Key, entry.Value).Should().BeEmpty();
        }
    }
}
