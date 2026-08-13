// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using System.Linq;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// EPG24's three vendored documentation tables, and the two entry points whose wrong
/// answers the documentation run exposed.
/// <para>
/// The tables carry data that exists only at C++ compile time upstream, so nothing in
/// the port's own behaviour depends on them and nothing else would notice them going
/// stale. These are the fences that would notice.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class DocumentationDataTests
{
    private static readonly object Gate = new object();

    private static Interpreter _interpreter;

    private static Interpreter Booted()
    {
        lock (Gate)
        {
            if (_interpreter == null)
            {
                Interpreter built = null;
                Interpreter.RunWithLargeStack(() => built = LilyPondScheme.CreateInterpreter());
                _interpreter = built;
            }

            return _interpreter;
        }
    }

    private static object Call(Interpreter interpreter, string name, object argument)
    {
        Variable variable = interpreter.GuileModule.Lookup(Symbol.Intern(name));
        variable.Should().NotBeNull();
        return interpreter.Evaluator.Apply(variable.GetValue(), new[] { argument });
    }

    [Fact]
    public void every_translator_the_port_registers_has_a_description()
    {
        //Arrange
        // Upstream's ADD_TRANSLATOR gives every translator a description; the port's C#
        // classes have nowhere to carry one, so the table supplies it. A translator the
        // table does not cover documents itself as blank, which is silent — hence the
        // assertion is over the REGISTERED roster, not over the table's own length.
        Booted();
        IReadOnlyDictionary<Symbol, object> registered = LilyPondScheme.Registries.Translators;

        //Act
        List<string> undescribed = registered.Keys
            .Select(name => name.Name)
            .Where(name => TranslatorDescriptionTable.Declaration(name) == null)
            .OrderBy(name => name, System.StringComparer.Ordinal)
            .ToList();

        //Assert
        // The Scheme-defined engravers of scheme-engravers.scm register their own
        // descriptions through ly:register-translator and are not the table's business.
        List<string> cxxSide = undescribed
            .Where(name => TranslatorDescriptionTable.All.Any(entry => entry.Name == name))
            .ToList();
        cxxSide.Should().BeEmpty();
    }

    [Fact]
    public void the_translator_table_describes_what_upstream_declares()
    {
        //Arrange / Act
        TranslatorDescriptionDeclaration tie
            = TranslatorDescriptionTable.Declaration("Tie_engraver");

        //Assert
        // Hand-read off lily/tie-engraver.cc's ADD_TRANSLATOR and its boot(): the four
        // text blocks and the one ADD_LISTENER. A control that must come out
        // differently follows, so the test cannot pass on an all-empty table.
        tie.Should().NotBeNull();
        tie.Description.Should().Be("Generate ties between note heads of equal pitch.");
        tie.GrobsCreated.Should().Equal("Tie", "TieColumn");
        tie.PropertiesRead.Should().Equal("skipTypesetting", "tieWaitForNote");
        tie.PropertiesWritten.Should().Equal("tieMelismaBusy");
        tie.EventsAccepted.Should().Equal("tie-event");

        TranslatorDescriptionDeclaration volta
            = TranslatorDescriptionTable.Declaration("Volta_engraver");
        volta.Description.Should().Be("Make volta brackets.");
        volta.PropertiesWritten.Should().BeEmpty();
    }

    [Fact]
    public void every_documented_entry_point_is_actually_bound()
    {
        //Arrange
        // The docstring table is upstream's, so it names every entry point upstream
        // documents. A name in it that the port does not bind would document a
        // procedure that is not there.
        Interpreter interpreter = Booted();

        //Act
        List<string> unbound = EntryPointDocumentationTable.All
            .Select(entry => entry.Name)
            .Where(name =>
            {
                Variable variable = interpreter.GuileModule.Lookup(Symbol.Intern(name));
                return variable == null || !variable.IsBound;
            })
            .OrderBy(name => name, System.StringComparer.Ordinal)
            .ToList();

        //Assert
        unbound.Should().BeEmpty();
    }

    [Fact]
    public void a_smob_predicate_documents_itself_the_way_smob_base_generates_it()
    {
        //Arrange / Act
        // smobs.tcc:141-145 composes this text from the C++ class name; it is not
        // written anywhere in the source to be copied, so it is generated on the
        // extraction side too. Hand-read off that expression.
        EntryPointDocumentation grob = EntryPointDocumentationTable.Documentation("ly:grob?");
        EntryPointDocumentation dir = EntryPointDocumentationTable.Documentation("ly:dir?");

        //Assert
        grob.Should().NotBeNull();
        grob.ArgumentList.Should().Be("(SCM x)");
        grob.Documentation.Should().Be("Is @var{x} a smob of class @code{Grob}?");

        // The control: an ordinary LY_DEFINE keeps its own written docstring and its
        // real argument list, so the two families cannot be confused.
        dir.ArgumentList.Should().Be("(SCM s)");
        dir.Documentation.Should().Contain("Is @var{s} a direction?");
    }

    [Fact]
    public void ly_dir_p_refuses_everything_that_is_not_a_direction()
    {
        //Arrange
        // general-scheme.cc:117-122: an integer in [-1, 1] and nothing else. The
        // non-numbers are the point — reading the argument as a C# long and defaulting
        // to 0 made every one of them answer #t, and 0 is a valid direction.
        Interpreter interpreter = Booted();

        //Act / Assert
        Call(interpreter, "ly:dir?", -1L).Should().Be(true);
        Call(interpreter, "ly:dir?", 0L).Should().Be(true);
        Call(interpreter, "ly:dir?", 1L).Should().Be(true);
        Call(interpreter, "ly:dir?", 2L).Should().Be(false);
        Call(interpreter, "ly:dir?", Symbol.Intern("AbsoluteDynamicEvent")).Should().Be(false);
        Call(interpreter, "ly:dir?", Pair.List(1L, 2L)).Should().Be(false);
        Call(interpreter, "ly:dir?", new MutableString("1")).Should().Be(false);
    }

    [Fact]
    public void camel_case_to_lisp_identifier_answers_a_symbol()
    {
        //Arrange
        // general-scheme.cc:367 returns ly_symbol2scm. A string prints identically and
        // matches nothing that looks the answer up, so the TYPE is the fact here — the
        // spelling is asserted alongside it as the control.
        Interpreter interpreter = Booted();

        //Act
        object answer = Call(
            interpreter, "ly:camel-case->lisp-identifier", Symbol.Intern("AbsoluteDynamicEvent"));

        //Assert
        answer.Should().BeOfType<Symbol>();
        ((Symbol)answer).Name.Should().Be("absolute-dynamic-event");
        answer.Should().BeSameAs(Symbol.Intern("absolute-dynamic-event"));
    }
}
