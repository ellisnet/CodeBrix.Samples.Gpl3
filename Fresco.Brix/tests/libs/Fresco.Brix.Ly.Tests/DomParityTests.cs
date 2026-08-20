// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly.Dom;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;
using DomDocument = Fresco.Brix.Ly.Dom.Document;
using DomVersion = Fresco.Brix.Ly.Dom.Version;

namespace Fresco.Brix.Ly.Tests;

/// <summary>
/// <see cref="Fresco.Brix.Ly.Dom"/> against python-ly itself. <c>ly.dom</c>
/// BUILDS documents rather than reading them, so parity is checked by
/// constructing the same tree on both sides: every scenario below mirrors one
/// in <c>tools/domprobe/gen-dom-fixtures.py</c>, and the fixture holds what
/// python-ly v0.9.10's printer produced for it — indented, unindented, and
/// with typographical quotes turned off.
/// </summary>
public class DomParityTests
{
    private static readonly Dictionary<string, Func<LyNode>> Scenarios
        = new Dictionary<string, Func<LyNode>>(StringComparer.Ordinal)
        {
            { "document", BuildDocument },
            { "header", BuildHeader },
            { "score", BuildScore },
            { "brackets", BuildBrackets },
            { "chords", BuildChords },
            { "markup", BuildMarkup },
            { "lyrics", BuildLyrics },
            { "comments", BuildComments },
            { "contexts", BuildContexts },
            { "tempo", BuildTempo },
            { "reference", BuildReference },
        };

    /// <summary>Every scenario name, as test data.</summary>
    /// <returns>The names.</returns>
    public static IEnumerable<object[]> ScenarioNames()
        => Scenarios.Keys.OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => new object[] { n });

    [Theory]
    [MemberData(nameof(ScenarioNames))]
    public void the_printed_document_matches_python_ly(string name)
    {
        //Arrange
        using JsonDocument fixture = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(
                AppContext.BaseDirectory, "fixtures", "dom", "dom.json")));
        JsonElement expected = fixture.RootElement.GetProperty(name);

        //Act
        LyNode node = Scenarios[name]();
        var printer = new Printer();
        var plain = new Printer { TypographicalQuotes = false };

        //Assert
        Label(name, "indented", printer.Indent(node))
            .Should().Be(Label(name, "indented", expected.GetProperty("indented").GetString()));
        Label(name, "ly", node.Ly(printer))
            .Should().Be(Label(name, "ly", expected.GetProperty("ly").GetString()));
        Label(name, "plain_quotes", plain.Indent(Scenarios[name]()))
            .Should().Be(Label(
                name, "plain_quotes", expected.GetProperty("plain_quotes").GetString()));
        Label(name, "before", node.Before.ToString())
            .Should().Be(Label(
                name, "before", expected.GetProperty("before").GetInt32().ToString()));
        Label(name, "after", node.After.ToString())
            .Should().Be(Label(
                name, "after", expected.GetProperty("after").GetInt32().ToString()));
    }

    [Fact]
    public void a_context_property_writes_its_context_and_name()
    {
        //Arrange
        //Upstream's ContextProperty never calls its base constructor, so in
        //python-ly appending one raises and there is no reference output; the
        //port initializes it properly, and this records what it writes.
        var lyric = new LyricMode();
        lyric.Append(new ContextProperty("aDueText", "Staff"));
        _ = new Text("la la", lyric);

        var seq = new Seq();
        seq.Append(new ContextProperty("aDueText", "Staff"));
        seq.Append(new ContextProperty("barAlways"));

        var printer = new Printer();

        //Act + Assert
        //In lyric mode the dot gets spaces around it.
        lyric.Ly(printer).Should().Be("\\lyricmode { Staff . aDueText la la }");
        seq.Ly(printer).Should().Be("{ Staff.aDueText barAlways }");
    }

    [Fact]
    public void a_variable_section_replaces_rather_than_repeats_an_assignment()
    {
        //Arrange
        var header = new Header();
        header.SetVariable("title", "First");

        //Act
        header.SetVariable("title", "Second");

        //Assert
        header.Count.Should().Be(1);
        header.Contains("title").Should().BeTrue();
        header.Ly(new Printer()).Should().Be("\\header {\ntitle = \"Second\"\n}");

        header.RemoveVariable("title");
        header.Contains("title").Should().BeFalse();
    }

    private static string Label(string name, string what, string value)
        => $"--- {name}.{what} ---\n{value}";

    private static LyNode BuildDocument()
    {
        var d = new DomDocument();
        _ = new DomVersion("2.27.2", d);
        _ = new Include("articulate.ly", d);
        _ = new BlankLine(d);
        var a = new Assignment("melody", d);
        var s = new Seq(a);
        _ = new Text("c'4 d' e' f'", s);
        return d;
    }

    private static LyNode BuildHeader()
    {
        var h = new Header();
        h.SetVariable("title", "A Title");
        h.SetVariable("composer", "Johann Sebastian Bach");
        h.SetVariable("tagline", new Scheme("#f"));
        return h;
    }

    private static LyNode BuildScore()
    {
        var score = new Score();
        var sim = new Sim(score);
        var staff = new Staff(parent: sim);
        staff.GetWith().SetVariable("instrumentName", "Violin");
        var seq = new Seq(staff);
        _ = new Clef("treble", seq);
        _ = new TimeSignature(3, 4, seq);
        _ = new KeySignature(4, new Fraction(0), "minor", seq);
        _ = new Partial(3, 0, Fraction.One, seq);
        _ = new Text("c'4 d' e'", seq);
        var layout = new Layout(score);
        var ctx = new ContextSection("Staff", layout);
        ctx.SetVariable("fontSize", new Scheme("-2"));
        var midi = new Midi(score);
        midi.SetVariable("tempoWholesPerMinute", new Scheme("(ly:make-moment 100 4)"));
        return score;
    }

    private static LyNode BuildBrackets()
    {
        var d = new DomDocument();

        //A Seqr with a single atom loses its brackets; a Seq keeps them.
        var a = new Assignment("one", d);
        var r = new Seqr(a);
        _ = new Identifier("someMusic", r);
        var b = new Assignment("two", d);
        var s = new Seq(b);
        _ = new Identifier("someMusic", s);
        var c = new Assignment("three", d);
        var sr = new Simr(c);
        _ = new Text("c'4", sr);
        _ = new Text("d'4", sr);
        var e = new Assignment("four", d);
        _ = new Seq(e);
        return d;
    }

    private static LyNode BuildChords()
    {
        var seq = new Seq();
        var chord = new Chord(seq);
        _ = new Pitch(1, 0, new Fraction(0), chord);
        _ = new Pitch(1, 2, new Fraction(0), chord);
        _ = new Pitch(1, 4, new Fraction(-1), chord);
        _ = new Duration(2, 1, Fraction.One, chord);
        var single = new Chord(seq);
        _ = new Pitch(-2, 6, new Fraction(1, 2), single);
        _ = new Duration(0, 0, new Fraction(2, 3), single);
        var td = new TextDur("r", seq);
        _ = new Duration(3, 0, Fraction.One, td);
        return seq;
    }

    private static LyNode BuildMarkup()
    {
        var m = new Markup();
        _ = new MarkupEnclosed("bold", m);
        _ = new QuotedString("a \"quoted\" word", new MarkupEnclosed("italic", m));
        var cmd = new MarkupCommand("hspace", m);
        _ = new Text("2", cmd);
        return m;
    }

    private static LyNode BuildLyrics()
    {
        var sim = new Sim();
        var voice = new Voice("melody", true, sim);
        _ = new Text("c'4 d' e' f'", new Seq(voice));
        var lyrics = new Lyrics(parent: sim);
        var to = new LyricsTo("melody", lyrics);
        _ = new Text("Sing a song of six -- pence", to);
        var add = new AddLyrics(sim);
        _ = new Text("An -- oth -- er verse here", add);
        return sim;
    }

    private static LyNode BuildComments()
    {
        var d = new DomDocument();
        _ = new LineComment("a full-line comment", d);
        _ = new Comment("a trailing comment", d);
        _ = new BlockComment("a block\ncomment %} with an end marker", d);
        _ = new BlockComment("a short block", d);
        _ = new QuotedString("he said \"hello\" and 'goodbye'", d);
        return d;
    }

    private static LyNode BuildContexts()
    {
        var sim = new Sim();
        var user = new UserContext("MyStaff", "up", true, sim);
        _ = new Text("c'1", new Seq(user));
        var sc = new ScoreContext(null, false, sim);
        _ = new Text("d'1", new Seq(sc));
        var piano = new PianoStaff("pf", true, sim);
        piano.AddInstrumentNameEngraverIfNecessary();
        var grand = new GrandStaff(parent: sim);
        grand.AddInstrumentNameEngraverIfNecessary();
        return sim;
    }

    private static LyNode BuildTempo()
    {
        var seq = new Seq();
        _ = new Tempo(4, 100, seq);
        var t = new Tempo(2, 60, seq);
        _ = new QuotedString("Allegro", t);
        _ = new Tempo(4, null, seq);
        new Mark(seq).Append(new Scheme("#f"));
        return seq;
    }

    private static LyNode BuildReference()
    {
        var reference = new Reference("melodyName");
        var d = new DomDocument();
        var a = new Assignment(reference, d);
        new Seq(a).Append(new Text("c'4"));
        _ = new Identifier(reference, d);
        reference.Name = "renamedMelody";
        return d;
    }
}
