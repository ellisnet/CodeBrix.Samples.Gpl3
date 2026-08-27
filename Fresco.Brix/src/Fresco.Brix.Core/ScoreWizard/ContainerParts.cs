// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using System;
using Dom = Fresco.Brix.Ly.Dom;

namespace Fresco.Brix.ScoreWizard; //was previously: frescobaldi/scorewiz/parts/containers.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Several staves braced or bracketed together.</summary>
public sealed class StaffGroup : ContainerPart
{
    /// <summary>Initializes the part and its settings.</summary>
    public StaffGroup()
    {
        SystemStart = Add(new ChoiceSetting(
            "systemStart",
            new[]
            {
                //L10N: Brace like a piano staff
                new ChoiceItem(() => I18n.Get("Brace"), "system_start_brace"),

                //L10N: Bracket like a choir staff
                new ChoiceItem(() => I18n.Get("Bracket"), "system_start_bracket"),

                //L10N: Square bracket like a sub-group
                new ChoiceItem(() => I18n.Get("Square"), "system_start_square"),
            },
            1)
        {
            Label = () => I18n.Get("Type:"),
        });
        ConnectBarLines = Add(new BoolSetting("connectBarLines", true)
        {
            Label = () => I18n.Get("Connect barlines"),
            ToolTip = () => I18n.Get(
                "If checked, barlines are connected between the staves."),
        });
    }

    /// <summary>Gets which delimiter opens the system.</summary>
    public ChoiceSetting SystemStart { get; }

    /// <summary>Gets whether bar lines run between the staves.</summary>
    public BoolSetting ConnectBarLines { get; }

    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Staff group");

    /// <inheritdoc/>
    public override bool Accepts(PartBase part) => part is StaffGroup or PartType;

    /// <inheritdoc/>
    public override void Build(PartData data, ScoreBuilder builder)
    {
        int start = SystemStart.SelectedIndex;
        bool connect = ConnectBarLines.Value;
        Dom.ContextType node;
        if (start == 0)
        {
            node = new Dom.GrandStaff();
            if (!connect)
            {
                new Dom.Line("\\remove Span_bar_engraver", node.GetWith());
            }
        }
        else
        {
            node = connect ? new Dom.StaffGroup() : new Dom.ChoirStaff();
            if (start == 2)
            {
                node.GetWith()["systemStartDelimiter"] =
                    new Dom.Scheme("'SystemStartSquare");
            }
        }

        data.Nodes.Add(node);
        data.Music = new Dom.Simr(node);
    }
}

/// <summary>One <c>\score</c> block, optionally with properties of its own.</summary>
public sealed class Score : GroupPart
{
    /// <summary>Initializes the part and its settings.</summary>
    public Score()
    {
        Piece = Add(new TextSetting("piece") { Label = () => I18n.Get("Piece:") });
        Opus = Add(new TextSetting("opus") { Label = () => I18n.Get("Opus:") });
        ScoreProps = Add(new GroupSetting("scoreProps", isCheckable: true, isChecked: false)
        {
            Label = () => I18n.Get("Properties"),
        });
        Properties = new ScoreProperties();
        foreach (PartSetting setting in Properties.Settings) { ScoreProps.Add(setting); }
    }

    /// <summary>Gets the piece name printed above this score.</summary>
    public TextSetting Piece { get; }

    /// <summary>Gets the opus number printed above this score.</summary>
    public TextSetting Opus { get; }

    /// <summary>Gets the group holding this score's own properties.</summary>
    public GroupSetting ScoreProps { get; }

    /// <summary>Gets this score's own key, time and tempo settings.</summary>
    public ScoreProperties Properties { get; }

    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Score");

    /// <inheritdoc/>
    public override bool Accepts(PartBase part) => part is StaffGroup or PartType;

    /// <inheritdoc/>
    public override Dom.LyNode MakeNode(Dom.LyNode node)
    {
        Dom.Score score = new Dom.Score(node);
        Dom.Header header = new Dom.Header();
        string piece = (Piece.Value ?? string.Empty).Trim();
        string opus = (Opus.Value ?? string.Empty).Trim();
        if (piece.Length > 0) { header["piece"] = new Dom.QuotedString(piece); }

        if (opus.Length > 0) { header["opus"] = new Dom.QuotedString(opus); }

        if (header.Count > 0) { score.Append(header); }

        return score;
    }

    /// <summary>Answers this score's own properties, when it has any.</summary>
    /// <param name="builder">The builder.</param>
    /// <returns>The expression, or null when the score follows the wizard's.</returns>
    public Dom.Seq GlobalSection(ScoreBuilder builder)
        => ScoreProps.IsChecked ? Properties.GlobalSection(builder) : null;
}

/// <summary>One <c>\bookpart</c> block.</summary>
public sealed class BookPart : GroupPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Book part");

    /// <inheritdoc/>
    public override bool Accepts(PartBase part)
        => part is Score or StaffGroup or PartType;

    /// <inheritdoc/>
    public override Dom.LyNode MakeNode(Dom.LyNode node) => new Dom.BookPart(node);
}

/// <summary>One <c>\book</c> block, which is one output file.</summary>
public sealed class Book : GroupPart
{
    /// <summary>Initializes the part and its settings.</summary>
    public Book()
    {
        Add(new NoticeSetting(
            "bookOutputInfo",
            () => I18n.Get(
                "Here you can specify a filename or suffix (without extension) "
                + "to set the names of generated output files for this book.")
                + " "
                + I18n.Get(
                    "If you choose \"Suffix\" the entered name will be appended "
                    + "to the document's file name; if you choose \"Filename\", just "
                    + "the entered name will be used.")));
        BookOutput = Add(new TextSetting("bookOutput")
        {
            Label = () => I18n.Get("Output filename:"),
        });
        OutputMode = Add(new ChoiceSetting(
            "bookOutputMode",
            new[]
            {
                new ChoiceItem(() => I18n.Get("Filename"), "bookOutputName"),
                new ChoiceItem(() => I18n.Get("Suffix"), "bookOutputSuffix"),
            },
            1));
    }

    /// <summary>Gets the file name or suffix the book writes to.</summary>
    public TextSetting BookOutput { get; }

    /// <summary>Gets whether that is a whole name or a suffix.</summary>
    /// <remarks>//was previously: two radio buttons, <c>bookOutputFileName</c>
    /// and <c>bookOutputSuffix</c>. One either-or setting says the same thing
    /// and cannot end up with both or neither chosen.</remarks>
    public ChoiceSetting OutputMode { get; }

    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Book");

    /// <inheritdoc/>
    public override bool Accepts(PartBase part)
        => part is BookPart or Score or StaffGroup or PartType;

    /// <inheritdoc/>
    public override Dom.LyNode MakeNode(Dom.LyNode node)
    {
        Dom.Book book = new Dom.Book(node);
        string name = (BookOutput.Value ?? string.Empty).Trim();
        if (name.Length > 0)
        {
            string command = OutputMode.SelectedTag as string ?? "bookOutputSuffix";
            new Dom.Line(
                "\\" + command + " \"" + name.Replace("\"", "\\\"", StringComparison.Ordinal) + "\"",
                book);
        }

        return book;
    }
}
