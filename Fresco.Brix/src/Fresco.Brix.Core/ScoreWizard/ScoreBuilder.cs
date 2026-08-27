// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using Dom = Fresco.Brix.Ly.Dom;

namespace Fresco.Brix.ScoreWizard; //was previously: frescobaldi/scorewiz/build.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A row of the score tree, with its children split into the ones that stack
/// vertically (parts and staff groups) and the ones that stack horizontally
/// (scores, book parts and books).
/// </summary>
public sealed class PartNode
{
    /// <summary>Reads a tree row and everything under it.</summary>
    /// <param name="item">The row.</param>
    public PartNode(PartTreeItem item)
    {
        Part = item?.Part;
        foreach (PartTreeItem child in item?.Children ?? (IReadOnlyList<PartTreeItem>)Array.Empty<PartTreeItem>())
        {
            PartNode node = new PartNode(child);
            if (node.Part is GroupPart) { Groups.Add(node); } else { Parts.Add(node); }
        }
    }

    /// <summary>Gets the part, or null for the invisible root.</summary>
    public PartBase Part { get; }

    /// <summary>Gets the rows that stack horizontally.</summary>
    public IList<PartNode> Groups { get; } = new List<PartNode>();

    /// <summary>Gets the rows that stack vertically.</summary>
    public IList<PartNode> Parts { get; set; } = new List<PartNode>();
}

/// <summary>What one part wants to add to the score.</summary>
public sealed class PartData
{
    private readonly string _name;

    /// <summary>Initializes the data for a part.</summary>
    /// <param name="part">The part.</param>
    /// <param name="parent">The data of the part this one sits inside, or null.</param>
    public PartData(PartBase part, PartData parent = null)
    {
        _name = part?.TypeName ?? string.Empty;
        parent?.Children.Add(this);
        IsChild = parent != null;
    }

    /// <summary>Gets whether this part sits inside another one.</summary>
    public bool IsChild { get; }

    /// <summary>Gets the data of the parts inside this one.</summary>
    public IList<PartData> Children { get; } = new List<PartData>();

    /// <summary>Gets or sets which of several same-type parts this is, or 0.</summary>
    public int Num { get; set; }

    /// <summary>Gets the files this part wants included.</summary>
    public IList<string> Includes { get; } = new List<string>();

    /// <summary>Gets the blocks of code this part depends on.</summary>
    public IList<Dom.LyNode> CodeBlocks { get; } = new List<Dom.LyNode>();

    /// <summary>Gets this part's variable assignments.</summary>
    public IList<Dom.Assignment> Assignments { get; } = new List<Dom.Assignment>();

    /// <summary>Gets the nodes this part adds to the score.</summary>
    public IList<Dom.LyNode> Nodes { get; } = new List<Dom.LyNode>();

    /// <summary>Gets the blocks that go after the score.</summary>
    public IList<Dom.LyNode> AfterBlocks { get; } = new List<Dom.LyNode>();

    /// <summary>Gets or sets where a container part puts its children.</summary>
    public Dom.Container Music { get; set; }

    /// <summary>
    /// Gets or sets the name of the variable holding the key and time
    /// signature: <c>global</c>, or a score's own.
    /// </summary>
    public string GlobalName { get; set; } = "global";

    /// <summary>Gets or sets the score properties in effect for this part.</summary>
    public ScoreProperties ScoreProperties { get; set; }

    /// <summary>Answers the part's name, with a roman number when it repeats.</summary>
    /// <returns>The name.</returns>
    public string Name() => Num > 0 ? _name + LyUtil.Int2Roman(Num) : _name;

    /// <summary>Makes an assignment and records it.</summary>
    /// <param name="name">The variable name, or null for the part's own.</param>
    /// <returns>The assignment.</returns>
    public Dom.Assignment Assign(string name = null)
    {
        Dom.Assignment assignment = new Dom.Assignment(
            new Dom.Reference(name ?? LyUtil.MkId(Name())));
        Assignments.Add(assignment);
        return assignment;
    }

    /// <summary>Makes an assignment holding an empty <c>\relative</c> stub.</summary>
    /// <param name="name">The variable name, or null for the part's own.</param>
    /// <param name="octave">The octave the stub starts in.</param>
    /// <returns>The assignment.</returns>
    public Dom.Assignment AssignMusic(string name = null, int octave = 0)
    {
        Dom.Assignment assignment = Assign(name);
        Dom.Relative stub = new Dom.Relative(assignment);
        new Dom.Pitch(octave, 0, Fraction.Zero, stub);
        Dom.Seq sequence = new Dom.Seq(stub);
        new Dom.Identifier(GlobalName, sequence) { After = 1 };
        new Dom.LineComment(I18n.Get("Music follows here."), sequence);
        new Dom.BlankLine(sequence);
        return assignment;
    }
}

/// <summary>The three blocks one score's output is assembled in.</summary>
public sealed class BlockData
{
    /// <summary>Gets the variable assignments.</summary>
    public Dom.Block Assignments { get; } = new Dom.Block();

    /// <summary>Gets the <c>\score</c> blocks.</summary>
    public Dom.Block Scores { get; } = new Dom.Block();

    /// <summary>Gets what goes after them.</summary>
    public Dom.Block BackMatter { get; } = new Dom.Block();
}

/// <summary>
/// Builds the LilyPond document from everything the user set in the wizard.
/// </summary>
/// <remarks>
/// Reads the model once, on construction, and does not need it again — which
/// is upstream's own contract with its dialog, and what lets a test build a
/// score from a tree of parts with no window anywhere.
/// </remarks>
public sealed class ScoreBuilder
{
    private readonly List<string> _includeFiles = new List<string>();
    private readonly List<BlockData> _blocks = new List<BlockData>();
    private readonly List<Dom.Container> _midiParts = new List<Dom.Container>();
    private readonly Dom.Printer _printer = new Dom.Printer();
    private readonly Translator _translate;
    private int _currentScore;
    private bool _globalUsed;

    /// <summary>Builds the document described by a model.</summary>
    /// <param name="model">The wizard's state.</param>
    /// <param name="settings">The store the typographical quotes come from.</param>
    public ScoreBuilder(ScoreWizardModel model, SettingsStore settings = null)
    {
        if (model == null) { throw new ArgumentNullException(nameof(model)); }

        GeneralPreferences general = model.GeneralPreferences;
        InstrumentNamesPreferences instrumentNames = model.InstrumentNames;

        Header = model.Headers().ToList();
        LyVersionString = (model.Version ?? string.Empty).Trim();
        LyVersion = Regex.Matches(LyVersionString, @"\d+")
            .Select(match => int.Parse(match.Value, CultureInfo.InvariantCulture))
            .ToArray();
        Midi = model.MidiOutput.IsEnabled;
        SeparateMidi = model.MidiOutput.SeparateScore.Value;
        PitchLanguage = model.PitchLanguage;
        SuppressTagLine = general.RemoveTagline.Value;
        RemoveBarNumbers = general.RemoveBarNumbers.Value;
        SmartNeutralDirection = general.SmartNeutralDirection.Value;
        ShowMetronomeMark = general.ShowMetronomeMark.Value;
        PaperSize = general.PaperSize();
        PaperLandscape = general.PaperOrientation.SelectedIndex == 1;
        PaperRotated = general.PaperOrientation.SelectedIndex == 2;
        ShowInstrumentNames = instrumentNames.IsEnabled;

        string[] lengths = { "long", "short", null };
        FirstInstrumentName = lengths[Math.Max(0, instrumentNames.FirstSystem.SelectedIndex)];
        OtherInstrumentName = lengths[Math.Max(0, instrumentNames.OtherSystems.SelectedIndex)];
        if (FirstInstrumentName == null && OtherInstrumentName == null)
        {
            //Both ends off means the setting is off, whatever its tick says.
            ShowInstrumentNames = false;
        }

        _translate = I18n.Current;
        if (instrumentNames.IsEnabled)
        {
            string language = instrumentNames.LanguageCode();
            if (!string.IsNullOrEmpty(language))
            {
                _translate = I18n.TranslatorFor(language);
            }
        }

        ScoreProperties = model.ScoreProperties;
        GlobalSection = ScoreProperties.GlobalSection(this);

        _printer.IndentString = "  ";       //re-indented by the editor anyway
        _printer.TypographicalQuotes = general.TypographicalQuotes.Value;
        QuoteSet quotes = LanguageQuotes.Preferred(settings);
        _printer.PrimaryQuoteLeft = quotes.Primary.Left;
        _printer.PrimaryQuoteRight = quotes.Primary.Right;
        _printer.SecondaryQuoteLeft = quotes.Secondary.Left;
        _printer.SecondaryQuoteRight = quotes.Secondary.Right;
        if (!string.IsNullOrEmpty(PitchLanguage)) { _printer.Language = PitchLanguage; }

        PartNode globalGroup = new PartNode(model.Root);

        //Parts written above several scores belong to all of them.
        AssignParts(globalGroup);

        UsePrefix = NeedsPrefix(globalGroup);

        List<PartNode> groups = globalGroup.Parts.Count > 0
            ? new List<PartNode> { globalGroup }
            : globalGroup.Groups.ToList();

        BlockData block = null;
        foreach (PartNode group in groups)
        {
            block = new BlockData();
            MakeBlock(group, block.Scores, block);
            _blocks.Add(block);
        }

        if (!Midi || !SeparateMidi || _midiParts.Count == 0 || block == null) { return; }

        //The separate MIDI score lands in the LAST block, which is where
        //upstream's own loop variable leaves it.
        new Dom.BlankLine(block.Scores);
        Dom.Score score = new Dom.Score(block.Scores);
        Dom.Midi midi = new Dom.Midi(score);
        if (!ShowMetronomeMark) { SetMidiTempo(midi); }

        if (_midiParts.Count == 1)
        {
            score.Insert(0, _midiParts[0]);
        }
        else
        {
            Dom.Seq music = new Dom.Seq();
            foreach (Dom.Container part in _midiParts) { music.Append(part); }

            score.Insert(0, music);
        }
    }

    /// <summary>Gets the header fields that were filled in.</summary>
    public IReadOnlyList<(string Name, string Value)> Header { get; }

    /// <summary>Gets the version the document declares.</summary>
    public string LyVersionString { get; }

    /// <summary>Gets that version as its numbers.</summary>
    public IReadOnlyList<int> LyVersion { get; }

    /// <summary>Gets whether the score produces MIDI.</summary>
    public bool Midi { get; }

    /// <summary>Gets whether the MIDI goes in a <c>\score</c> of its own.</summary>
    public bool SeparateMidi { get; }

    /// <summary>Gets the pitch language, or the empty string.</summary>
    public string PitchLanguage { get; }

    /// <summary>Gets whether the default tagline is suppressed.</summary>
    public bool SuppressTagLine { get; }

    /// <summary>Gets whether measure numbers are suppressed.</summary>
    public bool RemoveBarNumbers { get; }

    /// <summary>Gets whether middle-line stems get a logical direction.</summary>
    public bool SmartNeutralDirection { get; }

    /// <summary>Gets whether the metronome mark is printed.</summary>
    public bool ShowMetronomeMark { get; }

    /// <summary>Gets the paper size, or the empty string for the default.</summary>
    public string PaperSize { get; }

    /// <summary>Gets whether the paper is landscape.</summary>
    public bool PaperLandscape { get; }

    /// <summary>Gets whether the print is rotated on regular paper.</summary>
    public bool PaperRotated { get; }

    /// <summary>Gets whether instrument names are printed.</summary>
    public bool ShowInstrumentNames { get; private set; }

    /// <summary>Gets what is printed before the first system, or null.</summary>
    public string FirstInstrumentName { get; }

    /// <summary>Gets what is printed before the other systems, or null.</summary>
    public string OtherInstrumentName { get; }

    /// <summary>Gets or sets the longest instrument name written so far.</summary>
    public int LongestInstrumentNameLength { get; set; }

    /// <summary>Gets the score-wide key, time and tempo settings.</summary>
    public ScoreProperties ScoreProperties { get; }

    /// <summary>Gets the expression the <c>global</c> variable holds.</summary>
    public Dom.Seq GlobalSection { get; }

    /// <summary>Gets whether music variables need a per-score prefix.</summary>
    public bool UsePrefix { get; }

    /// <summary>Gets the printer that turns the tree into text.</summary>
    public Dom.Printer Printer => _printer;

    /// <summary>Answers whether the document's version is at least this one.</summary>
    /// <param name="version">The version to compare with.</param>
    /// <returns>Whether it is.</returns>
    /// <remarks>Compared the way python compares tuples, which is what
    /// upstream's <c>self.lyVersion &gt;= (2, 13, 38)</c> does: element by
    /// element, and a shorter one loses a tie.</remarks>
    public bool LyVersionAtLeast(params int[] version)
    {
        int shared = Math.Min(LyVersion.Count, version.Length);
        for (int index = 0; index < shared; index++)
        {
            if (LyVersion[index] != version[index])
            {
                return LyVersion[index] > version[index];
            }
        }

        return LyVersion.Count >= version.Length;
    }

    /// <summary>Answers the whole document as LilyPond text.</summary>
    /// <param name="document">The document, or null to build one.</param>
    /// <returns>The text.</returns>
    public string Text(Dom.LyNode document = null)
        => _printer.Indent(document ?? Document());

    /// <summary>Builds the document.</summary>
    /// <returns>The tree.</returns>
    public Dom.Document Document()
    {
        Dom.Document document = new Dom.Document();

        new Dom.Version(LyVersionString, document);

        if (!string.IsNullOrEmpty(PitchLanguage))
        {
            if (LyVersionAtLeast(2, 13, 38))
            {
                new Dom.Line("\\language \"" + PitchLanguage + "\"", document);
            }
            else
            {
                new Dom.Include(PitchLanguage + ".ly", document);
            }
        }

        new Dom.BlankLine(document);

        if (_includeFiles.Count > 0)
        {
            foreach (string file in _includeFiles) { new Dom.Include(file, document); }

            new Dom.BlankLine(document);
        }

        Dom.Header header = new Dom.Header();
        foreach ((string name, string value) in Header)
        {
            header.SetVariable(name, value);
        }

        if (!header.Contains("tagline") && SuppressTagLine)
        {
            new Dom.Comment(I18n.Get("Remove default LilyPond tagline"), header);
            header["tagline"] = new Dom.Scheme("#f");
        }

        if (header.Count > 0)
        {
            document.Append(header);
            new Dom.BlankLine(document);
        }

        if (!string.IsNullOrEmpty(PaperSize) || ShowInstrumentNames)
        {
            Dom.Paper paper = new Dom.Paper(document);

            if (!string.IsNullOrEmpty(PaperSize))
            {
                new Dom.Scheme(
                    string.Concat(
                        "(set-paper-size \"",
                        PaperSize,
                        PaperLandscape ? "landscape" : string.Empty,
                        "\"",
                        PaperRotated ? " 'landscape" : string.Empty,
                        ")"),
                    paper)
                {
                    After = 1,
                };
            }

            if (ShowInstrumentNames)
            {
                //Upstream's own arithmetic: 5mm fits about three characters at
                //the default size, and 10mm of padding keeps the whole name
                //inside the left margin.
                new Dom.LineComment(I18n.Get("Add space for instrument names"), paper);
                int longIndent = Math.Min(35, 10 + ((5 * LongestInstrumentNameLength) / 3));
                const int defaultShortIndent = 10;
                if (FirstInstrumentName != null)
                {
                    int indent = string.Equals(
                        FirstInstrumentName, "long", StringComparison.Ordinal)
                        ? longIndent
                        : defaultShortIndent;
                    new Dom.Line(
                        string.Create(CultureInfo.InvariantCulture, $"indent = {indent}\\mm"),
                        paper);
                }

                if (OtherInstrumentName != null)
                {
                    int indent = string.Equals(
                        OtherInstrumentName, "long", StringComparison.Ordinal)
                        ? longIndent
                        : defaultShortIndent;
                    new Dom.Line(
                        string.Create(
                            CultureInfo.InvariantCulture, $"short-indent = {indent}\\mm"),
                        paper);
                }
            }

            new Dom.BlankLine(document);
        }

        Dom.Layout layout = new Dom.Layout();

        if (RemoveBarNumbers)
        {
            new Dom.Line(
                "\\remove \"Bar_number_engraver\"",
                new Dom.ContextSection("Score", layout));
        }

        if (SmartNeutralDirection)
        {
            Dom.ContextSection voice = new Dom.ContextSection("Voice", layout);
            new Dom.Line("\\consists \"Melody_engraver\"", voice);
            new Dom.Line("\\override Stem.neutral-direction = #'()", voice);
        }

        if (layout.Count > 0)
        {
            document.Append(layout);
            new Dom.BlankLine(document);
        }

        if (_globalUsed)
        {
            Dom.Assignment assignment = new Dom.Assignment("global");
            assignment.Append(GlobalSection);
            document.Append(assignment);
            new Dom.BlankLine(document);
        }

        foreach (BlockData block in _blocks)
        {
            document.Append(block.Assignments);
            document.Append(block.Scores);
            new Dom.BlankLine(document);
            if (block.BackMatter.Count > 0)
            {
                document.Append(block.BackMatter);
                new Dom.BlankLine(document);
            }
        }

        return document;
    }

    /// <summary>Sets a context's MIDI instrument, when MIDI is wanted.</summary>
    /// <param name="node">The context.</param>
    /// <param name="midiInstrument">The instrument.</param>
    public void SetMidiInstrument(Dom.ContextType node, string midiInstrument)
    {
        if (!Midi || node == null || string.IsNullOrEmpty(midiInstrument)) { return; }

        node.GetWith().SetVariable("midiInstrument", midiInstrument);
    }

    /// <summary>Writes the tempo into a MIDI block when no mark is printed.</summary>
    /// <param name="node">The block.</param>
    public void SetMidiTempo(Dom.VariableSection node)
    {
        if (LyVersionAtLeast(2, 16, 0))
        {
            ScoreProperties.LySimpleMidiTempo(node);
            ((Dom.LyNode)node[0]).After = 1;
            return;
        }

        ScoreProperties.LyMidiTempo(new Dom.ContextSection("Score", node));
    }

    /// <summary>Sets a staff's or group's instrument names.</summary>
    /// <param name="staff">The context.</param>
    /// <param name="longName">The long name: text or a markup node.</param>
    /// <param name="shortName">The short name: text or a markup node.</param>
    public void SetInstrumentNames(Dom.ContextType staff, object longName, object shortName)
    {
        if (!ShowInstrumentNames || staff == null) { return; }

        staff.AddInstrumentNameEngraverIfNecessary();
        Dom.With with = staff.GetWith();
        object first = null;
        if (FirstInstrumentName != null)
        {
            first = string.Equals(FirstInstrumentName, "long", StringComparison.Ordinal)
                ? longName
                : shortName;
            SetName(with, "instrumentName", first);
        }

        if (OtherInstrumentName == null) { return; }

        object other = string.Equals(OtherInstrumentName, "long", StringComparison.Ordinal)
            ? longName
            : shortName;

        //A markup node can only sit in one place, so the second use gets a copy.
        if (ReferenceEquals(other, first) && first is Dom.LyNode node)
        {
            other = node.Copy();
        }

        SetName(with, "shortInstrumentName", other);
    }

    /// <summary>Answers an instrument name, numbered when it repeats.</summary>
    /// <param name="name">What names the instrument, given a translator.</param>
    /// <param name="num">Which of several it is, or 0.</param>
    /// <returns>The name.</returns>
    public string InstrumentName(Func<Translator, string> name, int num = 0)
    {
        string text = name(_translate);
        return num > 0 ? text + " " + LyUtil.Int2Roman(num) : text;
    }

    /// <summary>Sets a context's instrument names from the part itself.</summary>
    /// <param name="node">The context.</param>
    /// <param name="part">The part.</param>
    /// <param name="data">The part's data, read for its number.</param>
    public void SetInstrumentNamesFromPart(Dom.LyNode node, PartBase part, PartData data)
    {
        if (node is not Dom.ContextType context) { return; }

        string longName = InstrumentName(part.Title, data.Num);
        string shortName = InstrumentName(part.Short, data.Num);
        SetInstrumentNames(context, longName, shortName);
        if (ShowInstrumentNames)
        {
            LongestInstrumentNameLength = Math.Max(
                LongestInstrumentNameLength, longName?.Length ?? 0);
        }
    }

    /// <summary>Writes the transposition of a transposing instrument.</summary>
    /// <param name="node">The node the staff's music is going into.</param>
    /// <param name="transposition">The sounding pitch of a written <c>c'</c>.</param>
    /// <returns>The node the music now goes into.</returns>
    public Dom.Seqr SetStaffTransposition(
        Dom.Container node, (int Octave, int Note, int Alter) transposition)
    {
        Fraction alter = new Fraction(transposition.Alter, 2);

        //Transpose the MIDI output from the written c' to the sounding pitch,
        new Dom.Pitch(
            transposition.Octave,
            transposition.Note,
            alter,
            new Dom.Transposition(node));

        //and both notation and MIDI back from there to the written c', which
        //cancels the previous \transposition for the MIDI.
        Dom.Command stub = new Dom.Command("transpose", node);
        new Dom.Pitch(transposition.Octave, transposition.Note, alter, stub);
        new Dom.Pitch(0, 0, Fraction.Zero, stub);
        return new Dom.Seqr(stub);
    }

    /// <summary>Sets one of the two instrument-name variables.</summary>
    /// <param name="with">The <c>\with</c> block.</param>
    /// <param name="name">The variable.</param>
    /// <param name="value">Text, or a markup node.</param>
    private static void SetName(Dom.With with, string name, object value)
    {
        switch (value)
        {
            case null:
                //Upstream would write a broken assignment here; no part type
                //reaches it, because every part that names itself has both.
                return;
            case Dom.LyNode node:
                with[name] = node;
                return;
            default:
                with.SetVariable(name, Convert.ToString(value, CultureInfo.InvariantCulture));
                return;
        }
    }

    /// <summary>Fills a block with everything one group holds.</summary>
    /// <param name="group">The group.</param>
    /// <param name="node">The node its contents go into.</param>
    /// <param name="block">The block being filled.</param>
    private void MakeBlock(PartNode group, Dom.LyNode node, BlockData block)
    {
        if (group.Part is ContainerPart container) { node = container.MakeNode(node); }

        if (group.Parts.Count > 0)
        {
            _currentScore++;
            string prefix = "score" + LyUtil.Int2Letter(_currentScore);

            string globalName = "global";
            ScoreProperties scoreProperties = ScoreProperties;
            if (group.Part is Score scorePart)
            {
                Dom.Seq globalSection = scorePart.GlobalSection(this);
                if (globalSection != null)
                {
                    scoreProperties = scorePart.Properties;
                    globalName = prefix + "Global";
                    Dom.Assignment assignment =
                        new Dom.Assignment(globalName, block.Assignments);
                    assignment.Append(globalSection);
                    new Dom.BlankLine(block.Assignments);
                }
            }

            if (string.Equals(globalName, "global", StringComparison.Ordinal))
            {
                _globalUsed = true;
            }

            Dom.Score score = node as Dom.Score ?? new Dom.Score(node);
            new Dom.Layout(score);
            if (Midi && !SeparateMidi)
            {
                Dom.Midi midi = new Dom.Midi(score);
                if (!ShowMetronomeMark) { SetMidiTempo(midi); }
            }

            Dom.Simr music = new Dom.Simr();
            score.Insert(0, music);

            IReadOnlyList<PartData> partData =
                MakeParts(group.Parts, globalName, scoreProperties);

            foreach (PartData part in partData)
            {
                foreach (string include in part.Includes)
                {
                    if (!_includeFiles.Contains(include)) { _includeFiles.Add(include); }
                }
            }

            List<Dom.Assignment> assignments = partData
                .SelectMany(part => part.Assignments)
                .ToList();

            foreach (PartData part in partData)
            {
                foreach (Dom.Assignment assignment in part.Assignments)
                {
                    block.Assignments.Append(assignment);
                    new Dom.BlankLine(block.Assignments);
                }

                block.BackMatter.Extend(part.AfterBlocks);
            }

            bool assignPerPart = partData.Count(part => part.Assignments.Count > 0) > 1;

            void Make(PartData part, Dom.Container into)
            {
                if (assignPerPart && part.Assignments.Count > 0)
                {
                    Dom.Assignment assignment = new Dom.Assignment(
                        new Dom.Reference(LyUtil.MkId(part.Name() + "Part")));
                    new Dom.Simr(assignment).Extend(part.Nodes);
                    new Dom.Identifier(assignment.Name, into) { After = 1 };
                    block.Assignments.Append(assignment);
                    new Dom.BlankLine(block.Assignments);
                    assignments.Add(assignment);
                    return;
                }

                into.Extend(part.Nodes);
            }

            void MakeRecursive(IEnumerable<PartData> items, Dom.Container into)
            {
                foreach (PartData part in items)
                {
                    Make(part, into);
                    if (part.Children.Count > 0)
                    {
                        MakeRecursive(part.Children, part.Music);
                    }
                }
            }

            MakeRecursive(partData.Where(part => !part.IsChild), music);

            if (Midi && SeparateMidi)
            {
                //The list of parts is duplicated rather than assigned to a
                //variable on purpose: the point of a separate \score is that it
                //can be edited on its own — a part muted for the MIDI by
                //commenting out its line stays visible in print.
                _midiParts.Add((Dom.Container)music.Copy());
            }

            if (UsePrefix)
            {
                foreach (Dom.Assignment assignment in assignments)
                {
                    if (assignment.Name is Dom.Reference reference)
                    {
                        reference.Name = LyUtil.MkId(prefix, reference.Name);
                    }
                }
            }
        }

        foreach (PartNode child in group.Groups) { MakeBlock(child, node, block); }
    }

    /// <summary>Lets the parts build their music stubs and assignments.</summary>
    /// <param name="parts">The parts of one score.</param>
    /// <param name="globalName">The name of the key/time variable they read.</param>
    /// <param name="scoreProperties">The properties in effect.</param>
    /// <returns>Each part's data, parents before children.</returns>
    private IReadOnlyList<PartData> MakeParts(
        IEnumerable<PartNode> parts, string globalName, ScoreProperties scoreProperties)
    {
        Dictionary<PartNode, PartData> data = new Dictionary<PartNode, PartData>();
        Dictionary<string, List<PartNode>> types =
            new Dictionary<string, List<PartNode>>(StringComparer.Ordinal);

        void Search(IEnumerable<PartNode> nodes, PartData parent)
        {
            foreach (PartNode node in nodes)
            {
                PartData partData = new PartData(node.Part, parent)
                {
                    GlobalName = globalName,
                    ScoreProperties = scoreProperties,
                };
                data[node] = partData;
                if (!types.TryGetValue(partData.Name(), out List<PartNode> same))
                {
                    same = new List<PartNode>();
                    types[partData.Name()] = same;
                }

                same.Add(node);
                Search(node.Parts, partData);
            }
        }

        Search(parts, null);

        //Number the parts of the same type: Choir I and Choir II.
        foreach (List<PartNode> same in types.Values)
        {
            if (same.Count <= 1) { continue; }

            for (int index = 0; index < same.Count; index++)
            {
                data[same[index]].Num = index + 1;
            }
        }

        List<PartNode> ordered = AllParts(parts).ToList();
        foreach (PartNode node in ordered)
        {
            node.Part?.Build(data[node], this);
        }

        //Two parts may have asked for the same variable name; the ones that
        //clash take the part's class name and number.
        Dictionary<string, List<(Dom.Reference Reference, PartNode Node)>> references =
            new Dictionary<string, List<(Dom.Reference, PartNode)>>(StringComparer.Ordinal);
        foreach (PartNode node in ordered)
        {
            foreach (Dom.Assignment assignment in data[node].Assignments)
            {
                if (assignment.Name is not Dom.Reference reference) { continue; }

                string name = reference.Name;
                if (!references.TryGetValue(
                    name, out List<(Dom.Reference, PartNode)> same))
                {
                    same = new List<(Dom.Reference, PartNode)>();
                    references[name] = same;
                }

                same.Add((reference, node));
            }
        }

        foreach (List<(Dom.Reference Reference, PartNode Node)> same in references.Values)
        {
            if (same.Count <= 1) { continue; }

            foreach ((Dom.Reference reference, PartNode node) in same)
            {
                reference.Name = LyUtil.MkId(reference.Name, data[node].Name());
            }
        }

        return ordered.Select(node => data[node]).ToList();
    }

    /// <summary>
    /// Moves the parts down into the subgroups that have none of their own.
    /// </summary>
    /// <param name="group">The group to start at.</param>
    /// <remarks>This is what lets a user name some parts and then several
    /// scores, and have every score use those parts.</remarks>
    private static void AssignParts(PartNode group)
    {
        bool used = false;
        foreach (PartNode child in group.Groups)
        {
            if (child.Parts.Count == 0)
            {
                child.Parts = group.Parts.ToList();
                used = true;
            }

            AssignParts(child);
        }

        if (used) { group.Parts = new List<PartNode>(); }
    }

    /// <summary>Walks a group and its subgroups.</summary>
    /// <param name="group">The group.</param>
    /// <returns>The groups.</returns>
    private static IEnumerable<PartNode> IterGroups(PartNode group)
    {
        yield return group;
        foreach (PartNode child in group.Groups)
        {
            foreach (PartNode node in IterGroups(child)) { yield return node; }
        }
    }

    /// <summary>
    /// Answers whether several scores share a part type, so that the music
    /// variables need a per-score prefix (<c>scoreAsoprano</c>).
    /// </summary>
    /// <param name="globalGroup">The whole tree.</param>
    /// <returns>Whether they do.</returns>
    private static bool NeedsPrefix(PartNode globalGroup)
    {
        Dictionary<Type, int> counter = new Dictionary<Type, int>();
        foreach (PartNode group in IterGroups(globalGroup))
        {
            foreach (PartNode part in group.Parts)
            {
                Type type = part.Part?.GetType();
                if (type == null) { continue; }

                counter[type] = counter.TryGetValue(type, out int count) ? count + 1 : 1;
            }
        }

        return counter.Count > 0 && counter.Values.Max() > 1;
    }

    /// <summary>Walks parts and the parts inside them.</summary>
    /// <param name="parts">The parts.</param>
    /// <returns>All of them, parents before children.</returns>
    private static IEnumerable<PartNode> AllParts(IEnumerable<PartNode> parts)
    {
        foreach (PartNode part in parts)
        {
            yield return part;
            foreach (PartNode child in AllParts(part.Parts)) { yield return child; }
        }
    }
}
