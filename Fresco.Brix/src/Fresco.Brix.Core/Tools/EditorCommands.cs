// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using Fresco.Brix.Documents;
using Fresco.Brix.Editor;
using Fresco.Brix.Ly;
using Fresco.Brix.ScoreWizard;
using Fresco.Brix.Services;
using Fresco.Brix.Snippets;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Lily = Fresco.Brix.Ly.Lex.LilyPondMode;
using Token = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.Tools; //was previously: frescobaldi/snippet/builtin.py (its 22 python-typed entries)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One of the twenty-two editor commands, and everything the menus, the
/// shortcut settings and the runner need to know about it.
/// </summary>
public sealed class EditorCommandInfo
{
    /// <summary>Creates a command description.</summary>
    /// <param name="name">Upstream's own snippet name, kept.</param>
    /// <param name="title">The English title — the verbatim upstream msgid.</param>
    /// <param name="titleContext">The msgid's context, or null.</param>
    /// <param name="menuGroup">Upstream's <c>menu</c> variable, or null when
    /// upstream gives it none.</param>
    /// <param name="shortcut">Upstream's default shortcut, or null.</param>
    /// <param name="needsSelection">Whether it declines without one.</param>
    /// <param name="keepSelection">Whether what it produced stays selected.</param>
    /// <param name="stripSelection">Whether the selection is stripped first.</param>
    /// <param name="reIndent">Whether the result is re-indented.</param>
    public EditorCommandInfo(
        string name,
        string title,
        string titleContext,
        string menuGroup,
        string shortcut,
        bool needsSelection,
        bool keepSelection,
        bool stripSelection,
        bool reIndent)
    {
        Name = name;
        Title = title;
        TitleContext = titleContext;
        MenuGroup = menuGroup;
        Shortcut = shortcut;
        NeedsSelection = needsSelection;
        KeepSelection = keepSelection;
        StripSelection = stripSelection;
        ReIndent = reIndent;
    }

    /// <summary>Gets upstream's own name for the command.</summary>
    public string Name { get; }

    /// <summary>Gets the English title, which is the upstream msgid.</summary>
    public string Title { get; }

    /// <summary>Gets the msgid's context, or null.</summary>
    public string TitleContext { get; }

    /// <summary>Gets the Snippets-menu group, or null.</summary>
    public string MenuGroup { get; }

    /// <summary>Gets upstream's default shortcut, or null.</summary>
    public string Shortcut { get; }

    /// <summary>Gets whether the command needs a selection.</summary>
    public bool NeedsSelection { get; }

    /// <summary>Gets whether the result stays selected.</summary>
    public bool KeepSelection { get; }

    /// <summary>Gets whether the selection is stripped of whitespace first.</summary>
    public bool StripSelection { get; }

    /// <summary>Gets whether what was inserted is re-indented.</summary>
    public bool ReIndent { get; }

    /// <summary>Gets whether the command opens a dialog before it can run.</summary>
    public bool NeedsColor
        => string.Equals(Name, "color_dialog", StringComparison.Ordinal);

    /// <summary>Gets the translated title, with its accelerator markers.</summary>
    /// <returns>The title.</returns>
    public string TranslatedTitle()
        => TitleContext == null ? I18n.Get(Title) : I18n.Get(TitleContext, Title);
}

/// <summary>What running an editor command did.</summary>
public sealed class EditorCommandResult
{
    /// <summary>Creates a result.</summary>
    /// <param name="applied">Whether the command did anything.</param>
    /// <param name="selectionStart">Where the caret's anchor should be.</param>
    /// <param name="selectionEnd">Where the caret should be.</param>
    public EditorCommandResult(bool applied, int selectionStart, int selectionEnd)
    {
        Applied = applied;
        SelectionStart = selectionStart;
        SelectionEnd = selectionEnd;
    }

    /// <summary>Gets whether the command did anything.</summary>
    public bool Applied { get; }

    /// <summary>Gets where the caret's anchor should be.</summary>
    public int SelectionStart { get; }

    /// <summary>Gets where the caret should be.</summary>
    public int SelectionEnd { get; }
}

/// <summary>
/// The twenty-two editor commands ruling FD10 makes NATIVE — Frescobaldi ships
/// them as snippets whose body is Python code, which ruling FR5.3 will not run.
/// </summary>
/// <remarks>
/// <para>
/// FR5.3 bans USER scripting, not the application doing things itself, so the
/// twenty-two are implemented here, keeping upstream's own names, default
/// shortcuts and menu placement. They are no longer snippets: they are not in
/// the library, the Snippets panel does not list them, and their keys are
/// edited on the Shortcuts page like every other command's.
/// </para>
/// <para>
/// Every one of them is verified against UPSTREAM rather than against this
/// reading of it: <c>tools/snippetprobe/</c> runs Frescobaldi's own snippet
/// bodies over a stand-in for the part of Qt's text model they use, and records
/// what they did in <c>fixtures/snippet-commands.txt</c>. Where a body's whole
/// content is a call into something already ported and verified — the token
/// matcher, the quote preferences, the colour dialog — the fixture records the
/// part around it and the test reasons about the call.
/// </para>
/// </remarks>
public static class EditorCommands
{
    /// <summary>The commands, in upstream's own name order.</summary>
    public static readonly IReadOnlyList<EditorCommandInfo> All = new[]
    {
        //                          name                          title                                  context                                    menu          shortcut          sel    keep   strip  indent
        new EditorCommandInfo("color_dialog",               "Color",                              null,                                      null,         null,             false, false, false, true),
        //⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14) — `comment' and
        //`uncomment' ship with NO default shortcut. Upstream's defaults
        //(snippet/tool.py) are the TWO-CHORD sequences "Ctrl+Alt+C, Ctrl+Alt+C"
        //and "Ctrl+Alt+C, Ctrl+Alt+U": a chord that only chooses which of the
        //two the following chord selects. Commands/KeySequence is ONE chord —
        //Qt's QKeySequence holds up to four and Frescobaldi uses more than one
        //in exactly these two places — so the type cannot represent them and
        //nothing is silently half-bound (board trap 37 is precisely about a
        //shortcut that does not parse being dropped without a word). Both
        //commands are reachable from the Snippets menu and both can be given a
        //single-chord shortcut on the Shortcuts preferences page. Chord support
        //is not v1 work; it is recorded on board §9.
        new EditorCommandInfo("comment",                    "Comment",                            "snippet: add comment characters",         "comment",    null,             false, false, false, false),
        new EditorCommandInfo("double",                     "Double selection or current line",   null,                                      null,         "Ctrl+D",         false, false, false, false),
        new EditorCommandInfo("last_note",                  "Last note or chord",                 null,                                      "music",      "Ctrl+;",         false, false, false, true),
        new EditorCommandInfo("lowercase",                  "Lower case selection",               null,                                      null,         "Ctrl+Shift+U",   true,  true,  false, true),
        new EditorCommandInfo("markup_lines_selection",     "Markup lines",                       null,                                      null,         null,             true,  true,  true,  true),
        new EditorCommandInfo("midi_tempo",                 "Midi Tempo",                         null,                                      null,         null,             false, false, false, true),
        new EditorCommandInfo("next_blank_line",            "Next Blank Line",                    null,                                      null,         "Alt+Down",       false, false, false, false),
        new EditorCommandInfo("next_blank_line_select",     "Select until Next Blank Line",       null,                                      null,         "Alt+Shift+Down", false, false, false, false),
        new EditorCommandInfo("no_barnumbers",              "No Barnumbers",                      null,                                      "properties", null,             false, false, false, true),
        new EditorCommandInfo("no_tagline",                 "No Tagline",                         null,                                      "properties", null,             false, false, false, true),
        new EditorCommandInfo("paper_a5",                   "A5 Paper",                           null,                                      null,         null,             false, false, false, true),
        new EditorCommandInfo("previous_blank_line",        "Previous Blank Line",                null,                                      null,         "Alt+Up",         false, false, false, false),
        new EditorCommandInfo("previous_blank_line_select", "Select until Previous Blank Line",   null,                                      null,         "Alt+Shift+Up",   false, false, false, false),
        new EditorCommandInfo("quotes_d",                   "Double Typographical Quotes",        null,                                      "text",       "Ctrl+\"",        false, false, false, true),
        new EditorCommandInfo("quotes_s",                   "Single Typographical Quotes",        null,                                      "text",       "Ctrl+'",         false, false, false, true),
        new EditorCommandInfo("remove_matching_pair",       "Delete Matching Pair",               null,                                      null,         null,             false, false, false, false),
        new EditorCommandInfo("removelines",                "Delete Line(s)",                     null,                                      null,         "Ctrl+K",         false, false, false, true),
        new EditorCommandInfo("staff_size",                 "Staff Size",                         null,                                      null,         null,             false, false, false, true),
        new EditorCommandInfo("titlecase",                  "Title case selection",               null,                                      null,         null,             true,  true,  false, true),
        //⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14) — see `comment'
        //above: upstream's default here is the two-chord
        //"Ctrl+Alt+C, Ctrl+Alt+U", which one chord cannot express.
        new EditorCommandInfo("uncomment",                  "Uncomment",                          "snippet: remove comment characters",      "comment",    null,             false, false, false, false),
        new EditorCommandInfo("uppercase",                  "Upper case selection",               null,                                      null,         "Ctrl+U",         true,  true,  false, true),
    };

    /// <summary>The commands, by name.</summary>
    public static readonly IReadOnlyDictionary<string, EditorCommandInfo> ByName
        = All.ToDictionary(c => c.Name, StringComparer.Ordinal);

    /// <summary>The commands that decline without a selection.</summary>
    public static readonly IReadOnlyList<string> SelectionCommandNames
        = All.Where(c => c.NeedsSelection).Select(c => c.Name).ToArray();

    /// <summary>The colours upstream's colour snippet names rather than
    /// spells out as an RGB triple.</summary>
    /// <remarks>Upstream's own table, in upstream's own order.</remarks>
    public static readonly IReadOnlyDictionary<(int Red, int Green, int Blue), string>
        NamedColors = new Dictionary<(int, int, int), string>
        {
            [(0, 0, 0)] = "black",
            [(255, 255, 255)] = "white",
            [(255, 0, 0)] = "red",
            [(0, 255, 0)] = "green",
            [(0, 0, 255)] = "blue",
            [(0, 255, 255)] = "cyan",
            [(255, 0, 255)] = "magenta",
            [(255, 255, 0)] = "yellow",
            [(128, 128, 128)] = "grey",
            [(128, 0, 0)] = "darkred",
            [(0, 128, 0)] = "darkgreen",
            [(0, 0, 128)] = "darkblue",
            [(0, 128, 128)] = "darkcyan",
            [(128, 0, 128)] = "darkmagenta",
            [(128, 128, 0)] = "darkyellow",
        };

    /// <summary>Matches a run of comment characters at the start of a line.</summary>
    private static readonly Regex LineComment
        = new Regex(@"^(\s*)%+ ?", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>Matches a run of semicolons at the start of a line.</summary>
    private static readonly Regex SchemeComment
        = new Regex(@"^(\s*);+", RegexOptions.Multiline | RegexOptions.Compiled);

    /// <summary>
    /// Runs one command over a document.
    /// </summary>
    /// <param name="name">Upstream's command name.</param>
    /// <param name="document">The document to act on.</param>
    /// <param name="selectionStart">Where the selection starts.</param>
    /// <param name="selectionEnd">Where it ends; equal to the start for none.</param>
    /// <param name="settings">The settings store, for the quote preferences.</param>
    /// <param name="color">The colour the dialog produced, for
    /// <c>color_dialog</c>; null means the dialog was cancelled.</param>
    /// <param name="reIndent">Whether to re-indent afterwards; the parity tests
    /// turn it off because the fixture records the state before it.</param>
    /// <param name="where">Where in the document the caret is, when the caller
    /// already knows — upstream's <c>state</c>. Null reads it off the
    /// document, which is what the application does.</param>
    /// <returns>What happened.</returns>
    public static EditorCommandResult Run(
        string name,
        EditorDocument document,
        int selectionStart,
        int selectionEnd,
        SettingsStore settings = null,
        (int Red, int Green, int Blue)? color = null,
        bool reIndent = true,
        IReadOnlyList<string> where = null)
    {
        if (document == null || !ByName.TryGetValue(name, out EditorCommandInfo info))
        {
            return new EditorCommandResult(false, selectionStart, selectionEnd);
        }

        DocumentEditorState state = DocumentEditorState.For(document);
        TextDocument store = document.Document;
        selectionStart = Math.Clamp(selectionStart, 0, store.TextLength);
        selectionEnd = Math.Clamp(selectionEnd, selectionStart, store.TextLength);

        if (info.NeedsSelection && selectionEnd <= selectionStart)
        {
            return new EditorCommandResult(false, selectionStart, selectionEnd);
        }

        if (info.StripSelection && selectionEnd > selectionStart)
        {
            Cursor stripped = new Cursor(state.LyDocument, selectionStart, selectionEnd);
            stripped.Strip();
            selectionStart = stripped.Start;
            selectionEnd = stripped.End ?? selectionEnd;
        }

        //The commands that move or edit through the document themselves; the
        //rest all produce a piece of text that replaces the selection.
        switch (name)
        {
            case "removelines":
                return DeleteLines(store, selectionStart, selectionEnd);

            case "remove_matching_pair":
                return DeleteMatchingPair(state, store, selectionEnd);

            case "double":
                return Duplicate(store, selectionStart, selectionEnd);

            case "next_blank_line":
                return BlankLine(store, selectionStart, forward: true, select: false);

            case "previous_blank_line":
                return BlankLine(store, selectionStart, forward: false, select: false);

            case "next_blank_line_select":
                return BlankLine(store, selectionStart, forward: true, select: true);

            case "previous_blank_line_select":
                return BlankLine(store, selectionStart, forward: false, select: true);

            case "uncomment":
                return Uncomment(
                    state,
                    store,
                    selectionStart,
                    selectionEnd,
                    where ?? StateAt(state, selectionStart));
        }

        string selected = selectionEnd > selectionStart
            ? store.GetText(selectionStart, selectionEnd - selectionStart)
            : string.Empty;
        List<object> events = Produce(
            name,
            selected,
            where ?? StateAt(state, selectionStart),
            state,
            store,
            selectionStart,
            selectionEnd,
            settings,
            color);
        if (events == null)
        {
            return new EditorCommandResult(false, selectionStart, selectionEnd);
        }

        return Insert(
            state, store, selectionStart, selectionEnd, events,
            info.KeepSelection, info.ReIndent && reIndent);
    }

    /// <summary>
    /// Says, in plain words, where in the document a position is — upstream's
    /// <c>insert.state(cursor)</c>: the lexer state at the start of the line,
    /// advanced over the tokens that end before the position.
    /// </summary>
    /// <param name="state">The document's editor state.</param>
    /// <param name="position">The position.</param>
    /// <returns>The names, outermost first.</returns>
    public static IReadOnlyList<string> StateAt(
        DocumentEditorState state, int position)
    {
        TextDocument store = state.Document.Document;
        DocumentLine line = store.GetLineByOffset(
            Math.Clamp(position, 0, store.TextLength));
        Fresco.Brix.Ly.Lex.State lexer = TokenIter.StateAt(state.Highlighter, line.LineNumber);
        int column = position - line.Offset;
        foreach (Token token in TokenIter.Tokens(state.Highlighter, line.LineNumber))
        {
            if (token.End > column) { break; }

            lexer.Follow(token);
        }

        return SimpleState.Describe(lexer);
    }

    /// <summary>
    /// Builds what one of the text-producing commands inserts: a list of
    /// strings and caret markers, exactly the shape upstream's
    /// <c>insert_python</c> walks.
    /// </summary>
    private static List<object> Produce(
        string name,
        string text,
        IReadOnlyList<string> state,
        DocumentEditorState editorState,
        TextDocument store,
        int selectionStart,
        int selectionEnd,
        SettingsStore settings,
        (int Red, int Green, int Blue)? color)
    {
        string innermost = state.Count > 0 ? state[state.Count - 1] : string.Empty;
        switch (name)
        {
            case "uppercase":
                return One(PythonCase.Upper(text));

            case "lowercase":
                return One(PythonCase.Lower(text));

            case "titlecase":
                return One(PythonCase.Title(text));

            case "markup_lines_selection":
                return One(MarkupLines(text, innermost));

            case "no_tagline":
                return One(Wrap(
                    "tagline = ##f", innermost, "header", "\\header"));

            case "no_barnumbers":
                return One(NoBarnumbers(innermost));

            case "paper_a5":
                return One(Wrap(
                    "#(set-paper-size \"a5\")", innermost, "paper", "\\paper"));

            case "midi_tempo":
                return MidiTempo(innermost);

            case "staff_size":
                return One(StaffSize(innermost));

            case "comment":
                return Comment(text, After(store, selectionEnd), state);

            case "quotes_s":
            case "quotes_d":
                return Quotes(text, name, settings);

            case "color_dialog":
                return color == null ? null : One(ColorText(color.Value));

            case "last_note":
                return One(LastNote(
                    editorState, store, selectionStart, selectionEnd, text));

            default:
                return null;
        }
    }

    private static List<object> One(string text)
        => new List<object> { text };

    /// <summary>The text on the selection's last line, after the selection —
    /// upstream's <c>cursortools.partition(cursor)[2]</c>.</summary>
    private static string After(TextDocument store, int selectionEnd)
    {
        DocumentLine line = store.GetLineByOffset(
            Math.Clamp(selectionEnd, 0, store.TextLength));
        return store.GetText(selectionEnd, line.EndOffset - selectionEnd);
    }

    /// <summary>Upstream's <c>'\\block {\n%s\n}' % text</c>, unless the caret
    /// is already inside that block.</summary>
    private static string Wrap(
        string text, string innermost, string inside, string block)
        => string.Equals(innermost, inside, StringComparison.Ordinal)
            ? text
            : block + " {\n" + text + "\n}";

    private static string MarkupLines(string text, string innermost)
    {
        string lines = string.Join(
            "\n", SplitLines(text).Select(l => "\\line { " + l + " }"));
        return Wrap(lines, innermost, "markup", "\\markup");
    }

    private static string NoBarnumbers(string innermost)
    {
        string text = "\\remove \"Bar_number_engraver\"";
        if (string.Equals(innermost, "context", StringComparison.Ordinal)
            || string.Equals(innermost, "with", StringComparison.Ordinal))
        {
            return text;
        }

        text = "\\context {\n\\Score\n" + text + "\n}";
        return Wrap(text, innermost, "layout", "\\layout");
    }

    private static List<object> MidiTempo(string innermost)
    {
        List<object> events = new List<object>
        {
            "tempoWholesPerMinute = #(ly:make-moment ",
            SnippetMarker.Cursor,
            "100 4)",
        };

        if (string.Equals(innermost, "context", StringComparison.Ordinal)
            || string.Equals(innermost, "with", StringComparison.Ordinal))
        {
            return events;
        }

        events.Insert(0, "\\context {\n\\Score\n");
        events.Add("\n}");
        if (string.Equals(innermost, "midi", StringComparison.Ordinal))
        {
            return events;
        }

        events.Insert(0, "\\midi {\n");
        events.Add("\n}");
        return events;
    }

    private static string StaffSize(string innermost)
    {
        if (string.Equals(innermost, "music", StringComparison.Ordinal))
        {
            return "\\set Staff.fontSize = #-1\n"
                + "\\override Staff.StaffSymbol.staff-space = #(magstep -1)\n";
        }

        string text = "fontSize = #-1\n"
            + "\\override StaffSymbol.staff-space = #(magstep -1)";
        if (string.Equals(innermost, "new", StringComparison.Ordinal))
        {
            return "\\with {\n" + text + "\n}";
        }

        if (string.Equals(innermost, "context", StringComparison.Ordinal)
            || string.Equals(innermost, "with", StringComparison.Ordinal))
        {
            return text;
        }

        text = "\\context {\n\\Staff\n" + text + "\n}";
        return Wrap(text, innermost, "layout", "\\layout");
    }

    /// <summary>Which of the three comment syntaxes applies where the caret
    /// is — the innermost of the three names, or LilyPond's.</summary>
    private static string CommentSyntax(IReadOnlyList<string> state)
    {
        for (int i = state.Count - 1; i >= 0; i--)
        {
            if (state[i] is "lilypond" or "html" or "scheme") { return state[i]; }
        }

        return "lilypond";
    }

    private static List<object> Comment(
        string text, string after, IReadOnlyList<string> state)
    {
        switch (CommentSyntax(state))
        {
            case "html":
                return text.Length > 0
                    ? One("<!-- " + text + " -->")
                    : new List<object> { "<!-- ", SnippetMarker.Cursor, " -->" };

            case "scheme":
                return One(text.Length > 0
                    ? "; " + text.Replace("\n", "\n; ", StringComparison.Ordinal)
                    : "; ");

            default:
                return One(CommentLilyPond(text, after));
        }
    }

    private static string CommentLilyPond(string text, string after)
    {
        bool blockNeeded = after.Length > 0 && !IsBlank(after);
        if (text.Contains('\n'))
        {
            if (text.EndsWith('\n'))
            {
                return "% "
                    + text.Substring(0, text.Length - 1)
                        .Replace("\n", "\n% ", StringComparison.Ordinal)
                    + "\n";
            }

            return blockNeeded
                ? "%{ " + text + " %}"
                : "% " + text.Replace("\n", "\n% ", StringComparison.Ordinal);
        }

        if (text.Length == 0) { return "% "; }

        //⚠ Upstream appends `after` to its own replacement here, so a selection
        //followed by whitespace has that whitespace DOUBLED. Odd, but
        //deliberate design rather than a defect — the fixture records it and
        //the port reproduces it (standing rule 4b: faithful to what upstream
        //MEANT, and nothing here says it meant otherwise).
        return blockNeeded ? "%{ " + text + " %}" : "% " + text + after;
    }

    private static List<object> Quotes(
        string text, string name, SettingsStore settings)
    {
        QuoteSet quotes = LanguageQuotes.Preferred(settings);
        QuotePair pair = string.Equals(name, "quotes_d", StringComparison.Ordinal)
            ? quotes.Primary
            : quotes.Secondary;

        return text.Length > 0
            ? One(pair.Left + text + pair.Right)
            : new List<object> { pair.Left, SnippetMarker.Cursor, pair.Right };
    }

    /// <summary>The LilyPond text for a colour — upstream's own table first,
    /// then its four-significant-digit fractions.</summary>
    /// <param name="color">The colour.</param>
    /// <returns>The text.</returns>
    public static string ColorText((int Red, int Green, int Blue) color)
    {
        if (NamedColors.TryGetValue(color, out string named)) { return "#" + named; }

        return string.Format(
            CultureInfo.InvariantCulture,
            "#(rgb-color {0} {1} {2})",
            Fraction(color.Red),
            Fraction(color.Green),
            Fraction(color.Blue));
    }

    /// <summary>
    /// One colour channel as upstream writes it: <c>format(v / 255.0, ".4")</c>
    /// — Python's general format with four SIGNIFICANT digits, which is
    /// <c>G4</c> here, and which prints 1.0 rather than 1.
    /// </summary>
    private static string Fraction(int channel)
    {
        double value = channel / 255.0;
        string text = value.ToString("G4", CultureInfo.InvariantCulture);

        //Python's general format keeps a trailing ".0" on a whole number; .NET's
        //G drops it, and LilyPond wants a real.
        return text.Contains('.', StringComparison.Ordinal)
            || text.Contains('E', StringComparison.Ordinal)
            ? text
            : text + ".0";
    }

    /// <summary>
    /// Reads BACKWARD from the caret for the last note or chord entered, and
    /// answers it again — without the octave mark on the first pitch when the
    /// music is relative.
    /// </summary>
    private static string LastNote(
        DocumentEditorState state,
        TextDocument store,
        int selectionStart,
        int selectionEnd,
        string selected)
    {
        DocumentLine line = store.GetLineByOffset(
            Math.Clamp(selectionStart, 0, store.TextLength));
        string beforeCursor = store.GetText(
            line.Offset, Math.Max(0, selectionStart - line.Offset));
        bool spaceNeeded = beforeCursor.Length > 0
            && beforeCursor[beforeCursor.Length - 1] is not '\t' and not ' ';

        int? chordStart = null;
        int? chordEnd = null;
        int? noteStart = null;
        bool relative = false;
        bool found = false;

        Cursor cursor = new Cursor(state.LyDocument, selectionStart, selectionEnd);
        Runner runner = Runner.At(cursor, afterToken: true);

        foreach (Token token in runner.Backward())
        {
            if (string.Equals(token.Text, "\\relative", StringComparison.Ordinal))
            {
                relative = true;
                break;
            }

            if (token is Lily.Score or Lily.Book or Lily.BookPart or Lily.Name)
            {
                break;
            }

            if (found) { continue; }

            if (chordEnd != null)
            {
                if (token is Lily.ChordStart)
                {
                    chordStart = runner.Position();
                    found = true;
                }

                continue;
            }

            if (token is Lily.ChordEnd)
            {
                chordEnd = runner.Position() + token.Length;
            }
            else if (token is Lily.Note
                && token.Text is not "R" and not "q" and not "s" and not "r")
            {
                noteStart = runner.Position();
                found = true;
            }
        }

        if (!found) { return selected; }

        string text;
        if (chordStart != null)
        {
            StringBuilder builder = new StringBuilder();
            int removeOctave = relative ? 1 : 0;
            cursor.Start = chordStart.Value;
            cursor.End = chordEnd;
            foreach (Token token in new Source(cursor))
            {
                if (token is Lily.Note)
                {
                    removeOctave -= 1;
                }
                else if (token is Lily.Octave && removeOctave == 0)
                {
                    continue;
                }

                builder.Append(token.Text);
            }

            text = builder.ToString();
        }
        else
        {
            StringBuilder builder = new StringBuilder();
            cursor.Start = noteStart.Value;
            cursor.End = null;
            foreach (Token token in new Source(cursor))
            {
                if (token is Lily.Note || (!relative && token is Lily.Octave))
                {
                    builder.Append(token.Text);
                }
                else
                {
                    break;
                }
            }

            text = builder.ToString();
        }

        return spaceNeeded ? " " + text : text;
    }

    /// <summary>
    /// Removes the line or lines the selection touches, with the line break
    /// that follows them.
    /// </summary>
    private static EditorCommandResult DeleteLines(
        TextDocument store, int selectionStart, int selectionEnd)
    {
        DocumentLine first = store.GetLineByOffset(selectionStart);
        DocumentLine last = first;
        //Upstream compares `end.position() + end.length()`, and a Qt block's
        //length COUNTS its paragraph separator — so the comparison is against
        //where the NEXT line starts.
        while (last.NextLine != null && last.Offset + last.Length + 1 < selectionEnd)
        {
            last = last.NextLine;
        }

        int start = first.Offset;
        int end = last.NextLine != null ? last.NextLine.Offset : last.EndOffset;
        if (end > start)
        {
            store.Remove(start, end - start);
        }

        int caret = Math.Min(selectionStart, store.TextLength);
        return new EditorCommandResult(true, caret, caret);
    }

    /// <summary>Removes both halves of the bracket pair at the caret.</summary>
    private static EditorCommandResult DeleteMatchingPair(
        DocumentEditorState state, TextDocument store, int position)
    {
        List<(int Start, int Length)> matches
            = TokenMatcher.Matches(state.LyDocument, position);
        if (matches.Count == 0)
        {
            return new EditorCommandResult(false, position, position);
        }

        store.BeginUpdate();
        try
        {
            //Removed from the end backwards, so an earlier range's offsets are
            //still the ones the matcher answered.
            foreach (var range in matches.OrderByDescending(m => m.Start))
            {
                store.Remove(range.Start, range.Length);
            }
        }
        finally
        {
            store.EndUpdate();
        }

        int caret = Math.Min(position, store.TextLength);
        return new EditorCommandResult(true, caret, caret);
    }

    /// <summary>
    /// Doubles the selection, or — with nothing selected — the current line,
    /// walking back over blank lines to find one worth doubling.
    /// </summary>
    private static EditorCommandResult Duplicate(
        TextDocument store, int selectionStart, int selectionEnd)
    {
        string text;
        int insertAt;
        bool endsWithNewline;

        if (selectionEnd > selectionStart)
        {
            text = store.GetText(selectionStart, selectionEnd - selectionStart);
            insertAt = selectionEnd;
        }
        else
        {
            DocumentLine line = store.GetLineByOffset(selectionStart);
            while (IsBlank(store.GetText(line.Offset, line.Length)))
            {
                if (line.PreviousLine == null) { break; }

                line = line.PreviousLine;
            }

            if (line.NextLine != null)
            {
                text = store.GetText(line.Offset, line.NextLine.Offset - line.Offset);
                insertAt = line.NextLine.Offset;
            }
            else
            {
                //The last line has no break of its own, so the copy brings one
                //at each end rather than running into what is already there.
                text = "\n" + store.GetText(line.Offset, line.Length) + "\n";
                insertAt = line.EndOffset;
            }
        }

        endsWithNewline = text.EndsWith('\n');
        store.Insert(insertAt, text);
        int caret = insertAt + text.Length;
        if (endsWithNewline) { caret -= 1; }

        return new EditorCommandResult(true, caret, caret);
    }

    /// <summary>
    /// Moves to the end of the next (or previous) run of blank lines, selecting
    /// on the way when asked.
    /// </summary>
    private static EditorCommandResult BlankLine(
        TextDocument store, int position, bool forward, bool select)
    {
        DocumentLine line = store.GetLineByOffset(
            Math.Clamp(position, 0, store.TextLength));
        DocumentLine target = forward ? NextBlank(store, line) : PreviousBlank(store, line);
        if (target == null)
        {
            return new EditorCommandResult(false, position, position);
        }

        int caret = target.EndOffset;
        return new EditorCommandResult(true, select ? position : caret, caret);
    }

    /// <summary>The first line of the next run of blank lines, or null.</summary>
    private static DocumentLine NextBlank(TextDocument store, DocumentLine line)
    {
        //Upstream walks ONE generator twice: past everything blank, then past
        //everything that is not, then answers the first blank line after that.
        DocumentLine current = line;
        while (current != null && IsBlankLine(store, current))
        {
            current = current.NextLine;
        }

        while (current != null && !IsBlankLine(store, current))
        {
            current = current.NextLine;
        }

        return current;
    }

    /// <summary>The first line of the previous run of blank lines, or null.</summary>
    private static DocumentLine PreviousBlank(TextDocument store, DocumentLine line)
    {
        DocumentLine current = line;
        while (current != null && IsBlankLine(store, current))
        {
            current = current.PreviousLine;
        }

        while (current != null && !IsBlankLine(store, current))
        {
            current = current.PreviousLine;
        }

        if (current == null) { return null; }

        //Back over the whole run of blanks to its FIRST line — and when the run
        //reaches the top of the document, that first line is the document's.
        while (current.PreviousLine != null && IsBlankLine(store, current.PreviousLine))
        {
            current = current.PreviousLine;
        }

        return current;
    }

    private static bool IsBlankLine(TextDocument store, DocumentLine line)
        => IsBlank(store.GetText(line.Offset, line.Length));

    /// <summary>Python's <c>not text or text.isspace()</c>.</summary>
    private static bool IsBlank(string text)
        => text.Length == 0 || text.All(char.IsWhiteSpace);

    /// <summary>Takes the comment characters off the selection, or off the
    /// line the caret is on when nothing is selected.</summary>
    private static EditorCommandResult Uncomment(
        DocumentEditorState state,
        TextDocument store,
        int selectionStart,
        int selectionEnd,
        IReadOnlyList<string> where)
    {
        string syntax = CommentSyntax(where);
        string original = selectionEnd > selectionStart
            ? store.GetText(selectionStart, selectionEnd - selectionStart)
            : string.Empty;
        string text = original;
        int start = selectionStart;
        int end = selectionEnd;

        if (string.Equals(syntax, "html", StringComparison.Ordinal))
        {
            //⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14). Upstream's
            //html() has no `else` branch, so with NOTHING selected it answers
            //None and `cursor.insertText(None)` raises TypeError — Frescobaldi
            //shows its "Snippet error" box. A native command has nowhere to put
            //an exception, so an empty selection simply does nothing. The
            //fixture records upstream's crash (case `html-empty-raises`), and
            //the test declares the divergence.
            if (text.Length == 0)
            {
                return new EditorCommandResult(false, selectionStart, selectionEnd);
            }

            text = text
                .Replace("<!-- ", string.Empty, StringComparison.Ordinal)
                .Replace(" -->", string.Empty, StringComparison.Ordinal)
                .Replace("<!--", string.Empty, StringComparison.Ordinal)
                .Replace("-->", string.Empty, StringComparison.Ordinal);
        }
        else if (string.Equals(syntax, "scheme", StringComparison.Ordinal))
        {
            text = SchemeComment.Replace(text, "$1");
        }
        else
        {
            string trimmedLeft = text.TrimStart();
            if (trimmedLeft.StartsWith("%{", StringComparison.Ordinal))
            {
                text = trimmedLeft.StartsWith("%{ ", StringComparison.Ordinal)
                    ? trimmedLeft.Substring(3)
                    : trimmedLeft.Substring(2);
                string trimmedRight = text.TrimEnd();
                if (trimmedRight.EndsWith(" %}", StringComparison.Ordinal))
                {
                    text = trimmedRight.Substring(0, trimmedRight.Length - 3);
                }
                else if (trimmedRight.EndsWith("%}", StringComparison.Ordinal))
                {
                    text = trimmedRight.Substring(0, trimmedRight.Length - 2);
                }
            }
            else
            {
                if (text.Length == 0)
                {
                    //Upstream selects the BLOCK UNDER THE CURSOR, which in Qt
                    //takes the line break in FRONT of the line with it.
                    DocumentLine line = store.GetLineByOffset(selectionStart);
                    start = line.PreviousLine == null
                        ? line.Offset
                        : line.PreviousLine.EndOffset;
                    end = line.EndOffset;
                    original = store.GetText(start, end - start);
                    text = original;
                }

                text = LineComment.Replace(text, "$1");
            }
        }

        if (string.Equals(text, original, StringComparison.Ordinal))
        {
            return new EditorCommandResult(false, selectionStart, selectionEnd);
        }

        store.Replace(start, end - start, text);
        int caret = Math.Min(selectionStart, store.TextLength);
        return new EditorCommandResult(true, caret, caret);
    }

    /// <summary>
    /// Puts a produced list of text and markers into the document, replacing
    /// the selection — upstream's <c>insert_python</c> tail, with the re-indent
    /// pass <c>insert()</c> runs after it.
    /// </summary>
    private static EditorCommandResult Insert(
        DocumentEditorState state,
        TextDocument store,
        int selectionStart,
        int selectionEnd,
        List<object> events,
        bool keepSelection,
        bool reIndent)
    {
        int start = selectionStart;
        int position = start;
        int caret = -1;
        int anchor = -1;

        store.BeginUpdate();
        try
        {
            if (selectionEnd > selectionStart)
            {
                store.Remove(selectionStart, selectionEnd - selectionStart);
            }

            foreach (var item in events)
            {
                switch (item)
                {
                    case SnippetMarker.Anchor:
                        anchor = position;
                        break;

                    case SnippetMarker.Cursor:
                        caret = position;
                        break;

                    case string text:
                        store.Insert(position, text);
                        position += text.Length;
                        break;
                }
            }

            if (reIndent)
            {
                DocumentLine first = store.GetLineByOffset(start);
                DocumentLine last = store.GetLineByOffset(
                    Math.Min(position, store.TextLength));
                if (last.LineNumber != first.LineNumber)
                {
                    Indenting.ReIndent(
                        state.LyDocument,
                        Indenting.CreateIndenter(state.Settings, state.Document.Text),
                        first.Offset,
                        Math.Min(position, store.TextLength),
                        indentBlankLines: true);
                }
            }
        }
        finally
        {
            store.EndUpdate();
        }

        if (anchor >= 0 || caret >= 0)
        {
            int from = anchor >= 0 ? anchor : caret;
            int to = caret >= 0 ? caret : anchor;
            return new EditorCommandResult(true, from, to);
        }

        return keepSelection
            ? new EditorCommandResult(true, start, position)
            : new EditorCommandResult(true, position, position);
    }

    /// <summary>
    /// Python's <c>str.splitlines()</c>: it breaks on rather more than a
    /// newline, and it does not leave an empty piece after a trailing break.
    /// </summary>
    private static IReadOnlyList<string> SplitLines(string text)
    {
        List<string> lines = new List<string>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is not ('\n' or '\r' or '\u000b' or '\u000c'
                or '\u001c' or '\u001d' or '\u001e' or '\u0085'
                or '\u2028' or '\u2029'))
            {
                continue;
            }

            lines.Add(text.Substring(start, i - start));
            if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') { i++; }

            start = i + 1;
        }

        if (start < text.Length) { lines.Add(text.Substring(start)); }

        return lines;
    }
}
