// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Engrave;
using Fresco.Brix.Services;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The version report the About window shows and a bug report is pasted from.
/// </summary>
public class DebugInfoTests
{
    [Fact]
    public void the_report_names_the_application_first()
    {
        //Act
        var rows = DebugInfo.VersionInfoNamed().ToList();

        //Assert
        rows[0].Name.Should().Be(AppInfo.AppName);
        rows[0].Version.Should().Be(AppInfo.Version);
    }

    [Fact]
    public void the_engine_and_the_release_it_implements_are_separate_rows()
    {
        //Arrange — ruling FR13: the port's own package version and the LilyPond
        //release whose language it implements are never conflated.
        var rows = DebugInfo.VersionInfoNamed().ToList();

        //Act
        var engine = rows.Where(r => r.Name == "CodeBrix.LilyPort").ToList();
        var compatible = rows.Where(r => r.Name == "compatible with").ToList();

        //Assert
        engine.Count.Should().Be(1);
        compatible.Count.Should().Be(1);
        engine[0].Version.Should().Be(LilyPortEngine.PortVersion);
        compatible[0].Version.Should().Be(LilyPortEngine.CompatibleWithVersion);
        (engine[0].Version == compatible[0].Version).Should().Be(false);
    }

    [Fact]
    public void the_report_names_the_runtime_and_the_operating_system()
    {
        //Act
        var rows = DebugInfo.VersionInfoNamed().ToDictionary(r => r.Name, r => r.Version);

        //Assert
        rows.ContainsKey(".NET").Should().Be(true);
        rows.ContainsKey("OS").Should().Be(true);
        rows[".NET"].Should().NotBe(DebugInfo.Unknown);
        rows["OS"].Should().NotBe(DebugInfo.Unknown);
    }

    [Fact]
    public void the_report_names_the_platform_package_it_is_running_on()
    {
        //Act
        var rows = DebugInfo.VersionInfoNamed().ToDictionary(r => r.Name, r => r.Version);

        //Assert
        rows.ContainsKey("CodeBrix.Platform").Should().Be(true);
    }

    [Fact]
    public void every_row_reads_as_a_name_and_a_version()
    {
        //Act
        string text = DebugInfo.VersionInfoString();

        //Assert
        foreach (var line in text.Split('\n'))
        {
            line.Contains(": ").Should().Be(true);
        }
    }

    [Fact]
    public void an_assembly_that_is_not_there_answers_nothing()
    {
        //Act
        string version = DebugInfo.PackageVersion("No.Such.Assembly");

        //Assert
        version.Should().BeNull();
    }

    [Fact]
    public void a_report_separator_of_the_callers_choosing_is_used()
    {
        //Act
        string text = DebugInfo.VersionInfoString(" | ");

        //Assert
        text.Contains(" | ").Should().Be(true);
        text.Contains("\n").Should().Be(false);
    }
}
