// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Documents;
using Fresco.Brix.Services;
using System.Collections.Generic;

namespace Fresco.Brix.Tools; //was previously: frescobaldi/documentactions.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The commands that transform the document the user is in: Cut and Assign,
/// the indent and format commands, and the whole Quick Remove family.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's class also holds the two per-document preferences that live in
/// the metainfo — syntax highlighting and automatic indent — because their
/// menu entries are here. Both names are declared here for the same reason;
/// upstream declares them in <c>highlighter.py</c> and <c>indent.py</c>
/// respectively, next to the code that reads them.
/// </para>
/// <para>
/// Every command here is live. Format and Remove Trailing Whitespace got their
/// <c>ly.reformat</c> handlers at W6, and Update with convert-ly got its own at
/// W8 — <see cref="PendingActionNames"/> is empty now, and stays as the seam a
/// future wave uses to show a command's finished shape before it works.
/// </para>
/// </remarks>
public sealed class DocumentActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "documentactions";

    /// <summary>The metainfo value syntax highlighting is remembered in.</summary>
    public const string HighlightingName = "highlighting";

    /// <summary>The metainfo value automatic indent is remembered in.</summary>
    public const string AutoIndentName = "auto_indent";

    /// <summary>Creates the collection.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public DocumentActions(SettingsStore settings = null)
        : base(CollectionName, settings) => Initialize();

    /// <summary>Declares the per-document preferences.</summary>
    public static void Define()
    {
        MetaInfo.Define(HighlightingName, "1");
        MetaInfo.Define(AutoIndentName, "1");
    }

    /// <summary>The commands whose waves have not arrived yet.</summary>
    /// <remarks>
    /// //was previously: <c>tools_convert_ly</c>, which W8 implemented. Empty is
    /// the correct state; the list is kept because it is how a menu shows its
    /// finished shape before the wave that fills it in arrives.
    /// </remarks>
    public static readonly IReadOnlyList<string> PendingActionNames
        = System.Array.Empty<string>();

    /// <summary>The commands that need a selection to act on.</summary>
    public static readonly IReadOnlyList<string> SelectionActionNames = new[]
    {
        "edit_cut_assign", "edit_move_to_include_file",
        "tools_quick_remove_comments", "tools_quick_remove_articulations",
        "tools_quick_remove_ornaments", "tools_quick_remove_instrument_scripts",
        "tools_quick_remove_slurs", "tools_quick_remove_beams",
        "tools_quick_remove_ligatures", "tools_quick_remove_dynamics",
        "tools_quick_remove_fingerings", "tools_quick_remove_markup",
        "tools_directions_force_up", "tools_directions_force_neutral",
        "tools_directions_force_down",
    };

    /// <summary>Gets the "cut the selection and name it" command.</summary>
    public AppAction EditCutAssign { get; private set; }

    /// <summary>Gets the "move the selection to its own file" command.</summary>
    public AppAction EditMoveToIncludeFile { get; private set; }

    /// <summary>Gets the syntax-highlighting toggle.</summary>
    public AppAction ViewHighlighting { get; private set; }

    /// <summary>Gets the "follow the thing under the caret" command.</summary>
    public AppAction ViewGotoFileOrDefinition { get; private set; }

    /// <summary>Gets the automatic-indent toggle.</summary>
    public AppAction ToolsIndentAuto { get; private set; }

    /// <summary>Gets the re-indent command.</summary>
    public AppAction ToolsIndentIndent { get; private set; }

    /// <summary>Gets the reformat command (W6).</summary>
    public AppAction ToolsReformat { get; private set; }

    /// <summary>Gets the trailing-whitespace command (W6).</summary>
    public AppAction ToolsRemoveTrailingWhitespace { get; private set; }

    /// <summary>Gets the convert-ly command (W8).</summary>
    public AppAction ToolsConvertLy { get; private set; }

    /// <summary>Gets the Quick Remove commands, by the removal they perform.</summary>
    public IReadOnlyDictionary<string, AppAction> QuickRemove => _quickRemove;

    /// <summary>Gets the direction-forcing commands, by direction.</summary>
    public IReadOnlyDictionary<string, AppAction> ForceDirections => _forceDirections;

    private readonly Dictionary<string, AppAction> _quickRemove
        = new Dictionary<string, AppAction>();
    private readonly Dictionary<string, AppAction> _forceDirections
        = new Dictionary<string, AppAction>();

    /// <inheritdoc/>
    public override string Title => I18n.Get("Document Actions");

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        EditCutAssign = Add("edit_cut_assign")
            .WithIcon("edit-cut").WithShortcut("Ctrl+Shift+X");
        EditMoveToIncludeFile = Add("edit_move_to_include_file").WithIcon("edit-cut");
        ViewHighlighting = Add("view_highlighting").AsToggle(true);
        ViewGotoFileOrDefinition = Add("view_goto_file_or_definition")
            .WithShortcut("Alt+Return");
        ToolsIndentAuto = Add("tools_indent_auto").AsToggle(true);
        ToolsIndentIndent = Add("tools_indent_indent");
        ToolsReformat = Add("tools_reformat");
        ToolsRemoveTrailingWhitespace = Add("tools_remove_trailing_whitespace");
        ToolsConvertLy = Add("tools_convert_ly");

        foreach (var kind in new[]
        {
            "comments", "articulations", "ornaments", "instrument_scripts",
            "slurs", "beams", "ligatures", "dynamics", "fingerings", "markup",
        })
        {
            _quickRemove[kind] = Add("tools_quick_remove_" + kind);
        }

        foreach (var direction in new[] { "up", "neutral", "down" })
        {
            _forceDirections[direction] = Add("tools_directions_force_" + direction);
        }
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        EditCutAssign.Text = I18n.Get("Cut and Assign...");
        EditMoveToIncludeFile.Text = I18n.Get("Move to Include File...");
        ViewHighlighting.Text = I18n.Get("Syntax &Highlighting");
        ViewGotoFileOrDefinition.Text = I18n.Get("View File or Definition at &Cursor");
        ToolsIndentAuto.Text = I18n.Get("&Automatic Indent");
        ToolsIndentIndent.Text = I18n.Get("Re-&Indent");
        ToolsReformat.Text = I18n.Get("&Format");
        ToolsRemoveTrailingWhitespace.Text = I18n.Get("Remove Trailing &Whitespace");
        ToolsConvertLy.Text = I18n.Get("&Update with convert-ly...");
        _quickRemove["comments"].Text = I18n.Get("Remove &Comments");
        _quickRemove["articulations"].Text = I18n.Get("Remove &Articulations");
        _quickRemove["ornaments"].Text = I18n.Get("Remove &Ornaments");
        _quickRemove["instrument_scripts"].Text
            = I18n.Get("Remove &Instrument Scripts");
        _quickRemove["slurs"].Text = I18n.Get("Remove &Slurs");
        _quickRemove["beams"].Text = I18n.Get("Remove &Beams");
        _quickRemove["ligatures"].Text = I18n.Get("Remove &Ligatures");
        _quickRemove["dynamics"].Text = I18n.Get("Remove &Dynamics");
        _quickRemove["fingerings"].Text = I18n.Get("Remove &Fingerings");
        _quickRemove["markup"].Text = I18n.Get("Remove Text &Markup (from music)");
        _forceDirections["up"].Text = I18n.Get("Force Directions &Up");
        _forceDirections["neutral"].Text = I18n.Get("Make Directions &Neutral");
        _forceDirections["down"].Text = I18n.Get("Force Directions &Down");
    }
}
