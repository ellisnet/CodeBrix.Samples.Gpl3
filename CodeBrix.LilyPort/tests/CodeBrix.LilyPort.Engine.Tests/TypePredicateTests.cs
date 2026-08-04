// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The fence for the smob type predicates, and for the getter/setter pairs.
/// <para>
/// Both exist because of the same failure mode, met twice. A type predicate stubbed to
/// <see langword="false"/> is CORRECT while its type is unported and silently WRONG the
/// moment the type arrives — every type check on a real instance fails, and the error
/// names the property rather than the predicate. A getter declared with
/// <c>LY_DEFINE_WITH_SETTER</c> works perfectly for reading and fails only under
/// generalized <c>set!</c>, which is the form <c>make-music</c> uses.
/// </para>
/// <para>
/// Neither would be caught by a test that exercises the primitives themselves. What
/// catches them is insisting that every declared entry point is accounted for: either
/// implemented, or NAMED as not yet ported.
/// </para>
/// </summary>
public class TypePredicateTests
{
    private static HashSet<string> DeclaredTypePredicates()
    {
        HashSet<string> declared = new HashSet<string>(StringComparer.Ordinal);
        foreach (EntryPoint entry in EnginePrimitives.LoadEntryPoints())
        {
            if (entry.Kind == EntryPointKind.TypePredicate)
            {
                declared.Add(entry.Name);
            }
        }

        return declared;
    }

    [Fact]
    public void every_declared_type_predicate_is_implemented_or_named_as_unported()
    {
        //Arrange
        // Nothing may fall between the two lists. A predicate in neither is one whose
        // status nobody has decided, which is how ly:duration? came to answer #f about
        // real durations for as long as it did.
        HashSet<string> declared = DeclaredTypePredicates();

        //Act
        HashSet<string> accounted = new HashSet<string>(TypePredicates.Ported, StringComparer.Ordinal);
        accounted.UnionWith(TypePredicates.NotYetPorted);

        //Assert
        declared.Should().NotBeEmpty();
        accounted.Should().BeEquivalentTo(declared);
    }

    [Fact]
    public void the_two_predicate_lists_do_not_overlap()
    {
        //Arrange
        HashSet<string> ported = new HashSet<string>(TypePredicates.Ported, StringComparer.Ordinal);

        //Act
        List<string> both = new List<string>();
        foreach (string name in TypePredicates.NotYetPorted)
        {
            if (ported.Contains(name))
            {
                both.Add(name);
            }
        }

        //Assert
        both.Should().BeEmpty();
    }

    [Fact]
    public void the_thirty_six_type_predicates_the_extraction_once_missed_are_all_present()
    {
        //Arrange
        // The original entry-point extraction found none of these, because they are
        // declared by a C++ class's type_p_name_ member rather than by LY_DEFINE or
        // MAKE_SCHEME_CALLBACK. Losing them again would be silent.
        //Act
        HashSet<string> declared = DeclaredTypePredicates();

        //Assert
        declared.Count.Should().Be(36);
    }

    [Fact]
    public void every_getter_with_a_setter_is_a_recorded_entry_point()
    {
        //Arrange
        // The six LY_DEFINE_WITH_SETTER entry points. Upstream binds each name to a
        // procedure-with-setter, and the port has to do both halves.
        HashSet<string> known = new HashSet<string>(StringComparer.Ordinal);
        foreach (EntryPoint entry in EnginePrimitives.LoadEntryPoints())
        {
            known.Add(entry.Name);
        }

        //Act & Assert
        SetterBindings.GettersWithSetters.Count.Should().Be(6);
        foreach (string name in SetterBindings.GettersWithSetters)
        {
            known.Contains(name).Should().BeTrue();
        }
    }
}
