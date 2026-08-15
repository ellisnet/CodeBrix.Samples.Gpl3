// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The accessors <see cref="SchemeUtilities"/> exists to keep in ONE place, where the
/// engine would otherwise write a C# type pattern that reads like the upstream
/// predicate and answers differently.
/// </summary>
public class SchemeUtilitiesTests
{
    /// <summary>
    /// <c>scm_is_string</c> over the port's TWO string shapes.
    /// <para>
    /// The fence is the RELATIONSHIP between the helper and a bare <c>is string</c>
    /// pattern: they must DISAGREE about a <see cref="MutableString"/>, because that
    /// disagreement is the whole defect — a property's string is a
    /// <see cref="MutableString"/>, so <c>value is string</c> answers #f for the
    /// ordinary case and the guarded block never runs. It cost the ledger-line
    /// shortening range, the sustain-pedal sign and both spanner annotations.
    /// </para>
    /// </summary>
    [Fact]
    public void is_string_answers_for_both_string_shapes_where_a_type_pattern_does_not()
    {
        //Arrange
        object fromScheme = new MutableString("accidentals.sharp");
        object fromClr = "accidentals.sharp";

        //Act / Assert
        SchemeUtilities.IsString(fromScheme).Should().BeTrue();
        SchemeUtilities.IsString(fromClr).Should().BeTrue();

        // The disagreement this helper exists for.
        (fromScheme is string).Should().BeFalse();
        (fromClr is string).Should().BeTrue();
    }

    /// <summary>
    /// The CONTROL for the above: a non-string must answer #f, or "everything is a
    /// string" would pass every case in it.
    /// </summary>
    [Fact]
    public void is_string_refuses_a_symbol_a_number_and_the_empty_list()
    {
        //Arrange / Act / Assert
        SchemeUtilities.IsString(Symbol.Intern("accidentals.sharp")).Should().BeFalse();
        SchemeUtilities.IsString(42L).Should().BeFalse();
        SchemeUtilities.IsString(Nil.Instance).Should().BeFalse();
        SchemeUtilities.IsString(null).Should().BeFalse();
    }

    /// <summary>
    /// <c>StringText</c> reads BOTH shapes and answers null — not the empty string —
    /// for anything else, so a caller can tell "no glyph name" from "the empty glyph
    /// name" the way upstream's <c>scm_is_string</c> guard does.
    /// </summary>
    [Fact]
    public void string_text_reads_both_shapes_and_answers_null_for_anything_else()
    {
        //Arrange / Act / Assert
        SchemeUtilities.StringText(new MutableString("Ped")).Should().Be("Ped");
        SchemeUtilities.StringText("Ped").Should().Be("Ped");
        SchemeUtilities.StringText(Symbol.Intern("Ped")).Should().BeNull();
        SchemeUtilities.StringText(Nil.Instance).Should().BeNull();
    }
}
