// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Documents;
using Fresco.Brix.Snippets;
using Fresco.Brix.Tools;
using SilverAssertions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>One recorded case out of the snippet-command parity fixture.</summary>
public sealed class EditorCommandCase
{
    /// <summary>Creates a case from one fixture line.</summary>
    /// <param name="fields">The tab-separated fields.</param>
    public EditorCommandCase(string[] fields)
    {
        Command = fields[0];
        Label = fields[1];
        Before = Unescape(fields[2]);
        Start = int.Parse(fields[3], CultureInfo.InvariantCulture);
        End = int.Parse(fields[4], CultureInfo.InvariantCulture);
        State = fields[5].Contains('=', StringComparison.Ordinal)
            ? new[] { "lilypond" }
            : fields[5].Split(',');
        Extra = fields[5];
        After = fields[6];
        Anchor = fields[7];
        Position = fields[8];
    }

    /// <summary>Gets the command's upstream name.</summary>
    public string Command { get; }

    /// <summary>Gets the case's label.</summary>
    public string Label { get; }

    /// <summary>Gets the document before.</summary>
    public string Before { get; }

    /// <summary>Gets where the selection started.</summary>
    public int Start { get; }

    /// <summary>Gets where it ended.</summary>
    public int End { get; }

    /// <summary>Gets the recorded state at the caret.</summary>
    public IReadOnlyList<string> State { get; }

    /// <summary>Gets the sixth field verbatim — the state, or the colour or
    /// quote marks the case fed the snippet.</summary>
    public string Extra { get; }

    /// <summary>Gets the recorded document afterwards, still escaped.</summary>
    public string After { get; }

    /// <summary>Gets the recorded anchor, or <c>-</c>.</summary>
    public string Anchor { get; }

    /// <summary>Gets the recorded caret, or <c>-</c>.</summary>
    public string Position { get; }

    /// <summary>Gets whether upstream declined to run the snippet.</summary>
    public bool Refused
        => string.Equals(After, "REFUSED", StringComparison.Ordinal);

    /// <summary>Gets whether upstream RAISED instead of doing anything.</summary>
    public bool Raises => After.StartsWith("RAISES:", StringComparison.Ordinal);

    /// <summary>Gets whether the caret was recorded.</summary>
    public bool HasCaret => !string.Equals(Anchor, "-", StringComparison.Ordinal);

    /// <summary>Gets the colour the recorded dialog answered, or null.</summary>
    public (int Red, int Green, int Blue)? Color
    {
        get
        {
            if (!Extra.StartsWith("rgb=", StringComparison.Ordinal)) { return null; }

            string value = Extra.Substring(4);
            if (string.Equals(value, "none", StringComparison.Ordinal)) { return null; }

            string[] parts = value.Split(',');
            return (
                int.Parse(parts[0], CultureInfo.InvariantCulture),
                int.Parse(parts[1], CultureInfo.InvariantCulture),
                int.Parse(parts[2], CultureInfo.InvariantCulture));
        }
    }

    /// <inheritdoc/>
    public override string ToString() => Command + " / " + Label;

    /// <summary>Puts the escapes in a fixture field back.</summary>
    /// <param name="text">The field.</param>
    /// <returns>The text.</returns>
    public static string Unescape(string text)
    {
        StringBuilder builder = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\\' || i + 1 >= text.Length)
            {
                builder.Append(text[i]);
                continue;
            }

            i++;
            builder.Append(text[i] switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                _ => text[i],
            });
        }

        return builder.ToString();
    }
}

/// <summary>
/// What Frescobaldi's twenty-two PYTHON snippets do, as recorded from
/// upstream's own bodies — the parity battery behind ruling FD10's native
/// commands.
/// </summary>
/// <remarks>
/// <para>
/// <c>fixtures/snippet-commands.txt</c> is what upstream's snippet bodies
/// THEMSELVES produced, run by <c>tools/snippetprobe/</c> over a stand-in for
/// the part of Qt's text model they use. Nothing in it was written by hand and
/// nothing was recorded from this port (board trap: W6's two "obvious"
/// hand-written expectations were both wrong).
/// </para>
/// <para>
/// The commands run with re-indentation OFF here, because the fixture records
/// the document before the re-indent pass <c>insert()</c> runs afterwards —
/// that pass is <c>Indenting</c>, verified against python-ly at W1 and shared
/// with the snippet inserter.
/// </para>
/// </remarks>
public class EditorCommandParityTests
{
    /// <summary>
    /// The one case where the port does NOT do what upstream does.
    /// </summary>
    /// <remarks>
    /// ⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14). Upstream's
    /// <c>uncomment</c> has no <c>else</c> in its <c>html()</c> branch, so with
    /// nothing selected it answers <c>None</c> and <c>insertText(None)</c>
    /// raises <c>TypeError</c>; Frescobaldi shows its "Snippet error" box and
    /// offers to edit the snippet. A native command has no snippet to edit and
    /// nowhere to put an exception, so it does nothing instead. The FIXTURE
    /// STAYS AS RECORDED — it names the crash — and this table is the
    /// declaration.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> KnownDivergences
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["uncomment / html-empty-raises"]
                = "upstream raises TypeError; the command does nothing",
        };

    /// <summary>Every recorded case.</summary>
    /// <returns>The cases, one per theory row.</returns>
    public static TheoryData<EditorCommandCase> Cases()
    {
        TheoryData<EditorCommandCase> data = new TheoryData<EditorCommandCase>();
        foreach (EditorCommandCase recorded in Load()) { data.Add(recorded); }

        return data;
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public void the_command_does_what_upstreams_snippet_did(EditorCommandCase recorded)
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(recorded.Before);

        //Act
        EditorCommandResult result = EditorCommands.Run(
            recorded.Command,
            document,
            recorded.Start,
            recorded.End,
            settings: null,
            color: recorded.Color,
            reIndent: false,
            where: recorded.State);

        //Assert
        if (recorded.Refused || recorded.Raises)
        {
            result.Applied.Should().BeFalse();
            document.Text.Should().Be(recorded.Before);
            return;
        }

        document.Text.Should().Be(EditorCommandCase.Unescape(recorded.After));
        if (!recorded.HasCaret) { return; }

        result.SelectionStart.Should().Be(
            int.Parse(recorded.Anchor, CultureInfo.InvariantCulture));
        result.SelectionEnd.Should().Be(
            int.Parse(recorded.Position, CultureInfo.InvariantCulture));
    }

    [Fact]
    public void every_declared_divergence_is_a_case_the_fixture_still_holds()
    {
        //Arrange
        List<string> recorded = Load().Select(c => c.ToString()).ToList();

        //Act, Assert
        foreach (var pair in KnownDivergences)
        {
            recorded.Should().Contain(pair.Key);
        }
    }

    [Fact]
    public void the_fixture_covers_every_one_of_the_twenty_two()
    {
        //Arrange
        HashSet<string> covered = new HashSet<string>(
            Load().Select(c => c.Command), StringComparer.Ordinal);

        //Act
        List<string> missing = EditorCommands.All
            .Select(c => c.Name)
            .Where(n => !covered.Contains(n))
            .ToList();

        //Assert — the matching-pair command is the one whose whole content is a
        //call into TokenMatcher, which has its own parity battery.
        missing.Should().Equal(new[] { "remove_matching_pair" });
    }

    private static IReadOnlyList<EditorCommandCase> Load()
        => File.ReadAllLines(Path.Combine(
                AppContext.BaseDirectory, "fixtures", "snippet-commands.txt"))
            .Where(l => l.Length > 0 && l[0] != '#')
            .Select(l => new EditorCommandCase(l.Split('\t')))
            .ToList();
}

/// <summary>
/// Python's own case mappings, code point by code point — the three commands
/// whose whole body is <c>text.upper()</c>, <c>text.lower()</c> or
/// <c>text.title()</c> depend on them being Python's and not .NET's.
/// </summary>
public class PythonCaseParityTests
{
    [Fact]
    public void upper_matches_python_for_every_code_point_in_unicode()
    {
        //Arrange, Act
        List<string> wrong = Load()
            .Where(c => PythonCase.Upper(c.Char) != c.Upper)
            .Select(c => c.Describe(PythonCase.Upper(c.Char), c.Upper))
            .ToList();

        //Assert
        wrong.Should().BeEmpty();
    }

    [Fact]
    public void lower_matches_python_for_every_code_point_in_unicode()
    {
        //Arrange, Act
        List<string> wrong = Load()
            .Where(c => PythonCase.Lower(c.Char) != c.Lower)
            .Select(c => c.Describe(PythonCase.Lower(c.Char), c.Lower))
            .ToList();

        //Assert
        wrong.Should().BeEmpty();
    }

    [Fact]
    public void title_matches_python_for_every_code_point_in_unicode()
    {
        //Arrange, Act
        List<string> wrong = Load()
            .Where(c => PythonCase.Title(c.Char) != c.Title)
            .Select(c => c.Describe(PythonCase.Title(c.Char), c.Title))
            .ToList();

        //Assert
        wrong.Should().BeEmpty();
    }

    [Fact]
    public void the_cased_property_matches_pythons_own()
    {
        //Arrange, Act
        List<string> wrong = Load()
            .Where(c => PythonCase.IsCased(c.CodePoint) != c.Cased)
            .Select(c => c.Describe(
                PythonCase.IsCased(c.CodePoint).ToString(), c.Cased.ToString()))
            .ToList();

        //Assert
        wrong.Should().BeEmpty();
    }

    [Fact]
    public void the_final_sigma_rule_is_pythons_own()
    {
        //Arrange, Act, Assert — a sigma at the end of a word is written ς, one
        //in the middle σ, and a combining mark does not end the word.
        PythonCase.Lower("ΑΣ").Should().Be("ας");
        PythonCase.Lower("ΣΟΦΟΣ").Should().Be("σοφος");
        PythonCase.Lower("ΑΣΑ").Should().Be("ασα");
        PythonCase.Lower("Σ").Should().Be("σ");
    }

    [Fact]
    public void a_full_mapping_makes_the_string_longer()
    {
        //Arrange, Act, Assert — .NET's invariant casing leaves both alone.
        PythonCase.Upper("straße").Should().Be("STRASSE");
        PythonCase.Upper("aﬁn").Should().Be("AFIN");
        PythonCase.Title("ßa").Should().Be("Ssa");
    }

    [Fact]
    public void title_case_follows_the_previous_character_being_cased()
    {
        //Arrange, Act, Assert — python's own, odd, deliberate rule.
        PythonCase.Title("they're here").Should().Be("They'Re Here");
        PythonCase.Title("abc4 de5f").Should().Be("Abc4 De5F");
        PythonCase.Title("well-known name").Should().Be("Well-Known Name");
    }

    private sealed class CaseRecord
    {
        public CaseRecord(string line)
        {
            string[] fields = line.Split('\t');
            CodePoint = int.Parse(fields[0], NumberStyles.HexNumber,
                CultureInfo.InvariantCulture);
            Char = char.ConvertFromUtf32(CodePoint);
            Upper = FromHex(fields[1]);
            Lower = FromHex(fields[2]);
            Title = FromHex(fields[3]);
            Cased = fields[4] == "1";
        }

        public int CodePoint { get; }

        public string Char { get; }

        public string Upper { get; }

        public string Lower { get; }

        public string Title { get; }

        public bool Cased { get; }

        public string Describe(string got, string wanted)
            => string.Format(
                CultureInfo.InvariantCulture,
                "U+{0:X4}: got {1}, python says {2}",
                CodePoint,
                got,
                wanted);

        private static string FromHex(string field)
            => string.Concat(field.Split(' ').Select(h => char.ConvertFromUtf32(
                int.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture))));
    }

    private static IReadOnlyList<CaseRecord> Load()
        => _records ??= File.ReadAllLines(Path.Combine(
                AppContext.BaseDirectory, "fixtures", "python-case.txt"))
            .Where(l => l.Length > 0 && l[0] != '#')
            .Select(l => new CaseRecord(l))
            .ToList();

    private static IReadOnlyList<CaseRecord> _records;
}

/// <summary>
/// The catalogue of the twenty-two commands, and the two whose behaviour the
/// fixture cannot record.
/// </summary>
public class EditorCommandCatalogueTests
{
    /// <summary>The python-typed entries of upstream's
    /// <c>snippet/builtin.py</c>, read off that file.</summary>
    private static readonly string[] UpstreamNames =
    {
        "color_dialog", "comment", "double", "last_note", "lowercase",
        "markup_lines_selection", "midi_tempo", "next_blank_line",
        "next_blank_line_select", "no_barnumbers", "no_tagline", "paper_a5",
        "previous_blank_line", "previous_blank_line_select", "quotes_d",
        "quotes_s", "remove_matching_pair", "removelines", "staff_size",
        "titlecase", "uncomment", "uppercase",
    };

    [Fact]
    public void there_are_exactly_twenty_two_and_they_are_upstreams_own()
    {
        //Arrange, Act
        string[] names = EditorCommands.All.Select(c => c.Name).ToArray();

        //Assert
        names.Length.Should().Be(22);
        names.Should().Equal(UpstreamNames);
    }

    [Fact]
    public void none_of_the_twenty_two_is_a_snippet_any_more()
    {
        //Arrange, Act, Assert
        foreach (string name in UpstreamNames)
        {
            BuiltinSnippets.ByName.ContainsKey(name).Should().BeFalse();
            SnippetShortcuts.UpstreamDefaults.ContainsKey(name).Should().BeFalse();
        }
    }

    [Fact]
    public void the_commands_carry_upstreams_own_default_shortcuts()
    {
        //Arrange
        Dictionary<string, string> upstream = new Dictionary<string, string>
        {
            ["next_blank_line"] = "Alt+Down",
            ["previous_blank_line"] = "Alt+Up",
            ["next_blank_line_select"] = "Alt+Shift+Down",
            ["previous_blank_line_select"] = "Alt+Shift+Up",
            ["removelines"] = "Ctrl+K",
            ["quotes_s"] = "Ctrl+'",
            ["quotes_d"] = "Ctrl+\"",
            ["uppercase"] = "Ctrl+U",
            ["lowercase"] = "Ctrl+Shift+U",
            ["last_note"] = "Ctrl+;",
            ["double"] = "Ctrl+D",
        };

        //Act
        Dictionary<string, string> ours = EditorCommands.All
            .Where(c => c.Shortcut != null)
            .ToDictionary(c => c.Name, c => c.Shortcut);

        //Assert
        ours.Should().Equal(upstream);
    }

    [Fact]
    public void every_default_shortcut_parses()
    {
        //Arrange, Act, Assert — a shortcut that does not parse is silently
        //dropped (board trap 37).
        foreach (EditorCommandInfo info in EditorCommands.All.Where(c => c.Shortcut != null))
        {
            KeySequence.Parse(info.Shortcut).Should().NotBeNull();
        }
    }

    [Fact]
    public void the_eight_with_a_menu_variable_keep_upstreams_group()
    {
        //Arrange, Act
        Dictionary<string, string> grouped = EditorCommands.All
            .Where(c => c.MenuGroup != null)
            .ToDictionary(c => c.Name, c => c.MenuGroup);

        //Assert
        grouped.Should().Equal(new Dictionary<string, string>
        {
            ["comment"] = "comment",
            ["uncomment"] = "comment",
            ["last_note"] = "music",
            ["no_tagline"] = "properties",
            ["no_barnumbers"] = "properties",
            ["quotes_s"] = "text",
            ["quotes_d"] = "text",
        });
    }

    [Fact]
    public void the_four_that_need_a_selection_are_upstreams_four()
    {
        //Arrange, Act, Assert — upstream's `selection: yes`.
        EditorCommands.SelectionCommandNames.Should().Equal(new[]
        {
            "lowercase", "markup_lines_selection", "titlecase", "uppercase",
        });
    }

    [Fact]
    public void the_collection_registers_every_command()
    {
        //Arrange
        EditorCommandActions actions = new EditorCommandActions();
        List<string> triggered = new List<string>();
        actions.Apply = triggered.Add;

        //Act
        foreach (EditorCommandInfo info in EditorCommands.All)
        {
            actions.Action(info.Name).Trigger();
        }

        //Assert
        actions.Actions.Count.Should().Be(22);
        triggered.Should().Equal(EditorCommands.All.Select(c => c.Name).ToList());
    }

    [Fact]
    public void every_command_has_a_translated_title()
    {
        //Arrange
        EditorCommandActions actions = new EditorCommandActions();

        //Act, Assert
        foreach (EditorCommandInfo info in EditorCommands.All)
        {
            actions.Action(info.Name).Text.Should().Be(info.Title);
        }
    }
}

/// <summary>The two commands whose behaviour comes from something already
/// ported and verified.</summary>
public class EditorCommandDelegatedTests
{
    [Fact]
    public void the_matching_pair_command_removes_both_halves()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("\\relative c' { c4 d }\n");

        //Act — the caret is on the opening brace.
        EditorCommandResult result = EditorCommands.Run(
            "remove_matching_pair", document, 14, 14, reIndent: false);

        //Assert
        result.Applied.Should().BeTrue();
        document.Text.Should().Be("\\relative c'  c4 d \n");
    }

    [Fact]
    public void the_matching_pair_command_does_nothing_away_from_a_pair()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("\\relative c' { c4 d }\n");

        //Act
        EditorCommandResult result = EditorCommands.Run(
            "remove_matching_pair", document, 17, 17, reIndent: false);

        //Assert
        result.Applied.Should().BeFalse();
        document.Text.Should().Be("\\relative c' { c4 d }\n");
    }

    [Fact]
    public void the_colour_command_writes_a_named_colour_or_a_triple()
    {
        //Arrange, Act, Assert — upstream's own table, then its four
        //significant digits.
        EditorCommands.ColorText((255, 0, 0)).Should().Be("#red");
        EditorCommands.ColorText((128, 128, 0)).Should().Be("#darkyellow");
        EditorCommands.ColorText((12, 34, 56))
            .Should().Be("#(rgb-color 0.04706 0.1333 0.2196)");
        EditorCommands.ColorText((255, 128, 1))
            .Should().Be("#(rgb-color 1.0 0.502 0.003922)");
    }

    [Fact]
    public void the_state_at_a_position_names_where_the_caret_is()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open(
            "\\header {\n  title = \"x\"\n}\n");

        //Act — inside the header block.
        IReadOnlyList<string> inside = EditorCommands.StateAt(
            DocumentEditorState.For(document), 12);
        IReadOnlyList<string> outside = EditorCommands.StateAt(
            DocumentEditorState.For(document), 0);

        //Assert
        inside.Should().Contain("header");
        outside[outside.Count - 1].Should().Be("lilypond");
    }

    [Fact]
    public void the_quotes_come_from_the_quote_preferences()
    {
        //Arrange
        EditorDocument document = ToolDocument.Open("say hello now");

        //Act — nothing configured, so the neutral C locale's marks.
        EditorCommands.Run(
            "quotes_d", document, 4, 9, reIndent: false,
            where: new[] { "lilypond" });

        //Assert
        document.Text.Should().Be("say “hello” now");
    }
}
