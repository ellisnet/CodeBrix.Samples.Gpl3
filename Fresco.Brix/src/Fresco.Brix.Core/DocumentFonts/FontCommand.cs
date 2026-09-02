// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace Fresco.Brix.DocumentFonts; //was previously: frescobaldi/fonts/fontcommand.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Which way of setting the document's fonts is being written.</summary>
public enum FontCommandApproach
{
    /// <summary>The plain LilyPond one, a <c>\paper</c> block.</summary>
    Lily = 0,

    /// <summary>openLilyLib's <c>\useNotationFont</c>.</summary>
    OpenLilyLib = 1,
}

/// <summary>
/// The five fonts the dialog chooses, and nothing else.
/// </summary>
/// <remarks>
/// //was previously: <c>FontsDialog._selected_fonts</c>, a CLASS variable — so
/// upstream's five choices survive the dialog closing, and the next opening
/// starts where the last one left off. That is kept: the object lives on the
/// window, not on the window's window.
/// </remarks>
public sealed class FontSelection
{
    /// <summary>The dialog's own defaults.</summary>
    /// <remarks>Upstream's <c>_default_fonts</c>, verbatim, spelling included:
    /// <c>TeXGyre Schola</c> with no space is what upstream writes and what its
    /// Restore Defaults button restores.</remarks>
    public static readonly IReadOnlyDictionary<string, string> Defaults
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["music"] = "emmentaler",
            ["brace"] = "emmentaler",
            ["roman"] = "TeXGyre Schola",
            ["sans"] = "TeXGyre Heros",
            ["typewriter"] = "TeXGyre Cursor",
        };

    /// <summary>The families, in the order the command writes them.</summary>
    public static readonly IReadOnlyList<string> Families
        = new[] { "music", "brace", "roman", "sans", "typewriter" };

    private readonly Dictionary<string, string> _fonts
        = new Dictionary<string, string>(Defaults, StringComparer.Ordinal);

    /// <summary>Gets or sets one family's chosen font.</summary>
    /// <param name="family">One of <see cref="Families"/>.</param>
    /// <returns>The font name.</returns>
    public string this[string family]
    {
        get => _fonts[family];
        set => _fonts[family] = value ?? Defaults[family];
    }

    /// <summary>Puts every family back to its default.</summary>
    public void Restore()
    {
        foreach (var (family, name) in Defaults) { _fonts[family] = name; }
    }

    /// <summary>Reads the five choices out of the settings store.</summary>
    /// <param name="settings">The store.</param>
    public void Load(SettingsStore settings)
    {
        if (settings == null) { return; }

        foreach (string family in Families)
        {
            _fonts[family] = settings.GetString(
                DocumentFontSettings.Key(family + "-font"), Defaults[family]);
        }
    }

    /// <summary>Writes the five choices to the settings store.</summary>
    /// <param name="settings">The store.</param>
    public void Save(SettingsStore settings)
    {
        if (settings == null) { return; }

        foreach (string family in Families)
        {
            settings.SetString(
                DocumentFontSettings.Key(family + "-font"), _fonts[family]);
        }
    }
}

/// <summary>
/// Everything the Font Command tab's controls decide.
/// </summary>
public sealed class FontCommandOptions
{
    /// <summary>Gets or sets whether the music font is written.</summary>
    public bool SetMusic { get; set; } = true;

    /// <summary>Gets or sets whether the roman family is written.</summary>
    public bool SetRoman { get; set; }

    /// <summary>Gets or sets whether the sans family is written.</summary>
    public bool SetSans { get; set; }

    /// <summary>Gets or sets whether the typewriter family is written.</summary>
    public bool SetTypewriter { get; set; }

    /// <summary>Gets or sets whether a whole <c>\paper</c> block is written.</summary>
    public bool SetPaperBlock { get; set; } = true;

    /// <summary>Gets or sets which approach the tab is showing.</summary>
    public FontCommandApproach Approach { get; set; } = FontCommandApproach.Lily;

    /// <summary>Gets or sets whether openLilyLib itself is loaded.</summary>
    public bool LoadOll { get; set; } = true;

    /// <summary>Gets or sets whether the notation-fonts package is loaded.</summary>
    public bool LoadPackage { get; set; } = true;

    /// <summary>Gets or sets whether font extensions are asked for.</summary>
    public bool FontExtensions { get; set; }

    /// <summary>Gets or sets the stylesheet choice: 0 default, 1 none, 2 custom.</summary>
    public int StyleType { get; set; }

    /// <summary>Gets or sets the custom stylesheet's name.</summary>
    public string FontStylesheet { get; set; } = string.Empty;

    /// <summary>Reads the options out of the settings store.</summary>
    /// <param name="settings">The store.</param>
    public void Load(SettingsStore settings)
    {
        if (settings == null) { return; }

        SetRoman = settings.GetBool(DocumentFontSettings.Key("set-roman"), false);
        SetSans = settings.GetBool(DocumentFontSettings.Key("set-sans"), false);

        //⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14).
        //fontcommand.py's loadSettings reads `set-roman' TWICE (lines 295 and
        //297) and never reads `set-typewriter' at all; saveSettings writes
        //`set-roman' twice (312, 314) and never writes `set-typewriter'. A user
        //who asks for a typewriter font therefore loses that choice the moment
        //the dialog closes, while `set-sans' is loaded into its own box and
        //then has no effect on which box the duplicate line overwrites. It is a
        //copy-and-paste slip, not a design: the widget, its caption, its
        //translation and its place in the command generator are all there and
        //correct. The key is upstream's own name, so a settings file written
        //here still means what upstream would mean by it.
        SetTypewriter = settings.GetBool(DocumentFontSettings.Key("set-typewriter"), false);

        SetMusic = settings.GetBool(DocumentFontSettings.Key("set-music"), true);
        SetPaperBlock = settings.GetBool(DocumentFontSettings.Key("set-paper-block"), true);
        Approach = settings.GetInt(DocumentFontSettings.Key("approach-index"), 0) == 1
            ? FontCommandApproach.OpenLilyLib
            : FontCommandApproach.Lily;
        LoadOll = settings.GetBool(DocumentFontSettings.Key("load-oll"), true);
        LoadPackage = settings.GetBool(DocumentFontSettings.Key("load-package"), true);
        FontExtensions = settings.GetBool(DocumentFontSettings.Key("font-extensions"), false);
        StyleType = settings.GetInt(DocumentFontSettings.Key("style-type"), 0);
        FontStylesheet = settings.GetString(
            DocumentFontSettings.Key("font-stylesheet"), string.Empty);
    }

    /// <summary>Writes the options to the settings store.</summary>
    /// <param name="settings">The store.</param>
    public void Save(SettingsStore settings)
    {
        if (settings == null) { return; }

        settings.SetBool(DocumentFontSettings.Key("set-roman"), SetRoman);
        settings.SetBool(DocumentFontSettings.Key("set-sans"), SetSans);

        //⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14) — see Load above.
        settings.SetBool(DocumentFontSettings.Key("set-typewriter"), SetTypewriter);

        settings.SetBool(DocumentFontSettings.Key("set-music"), SetMusic);
        settings.SetBool(DocumentFontSettings.Key("set-paper-block"), SetPaperBlock);
        settings.SetInt(
            DocumentFontSettings.Key("approach-index"),
            Approach == FontCommandApproach.OpenLilyLib ? 1 : 0);
        settings.SetBool(DocumentFontSettings.Key("load-oll"), LoadOll);
        settings.SetBool(DocumentFontSettings.Key("load-package"), LoadPackage);
        settings.SetBool(DocumentFontSettings.Key("font-extensions"), FontExtensions);
        settings.SetInt(DocumentFontSettings.Key("style-type"), StyleType);
        settings.SetString(
            DocumentFontSettings.Key("font-stylesheet"), FontStylesheet ?? string.Empty);
    }
}

/// <summary>A generated command, in the shown form and the full one.</summary>
/// <param name="Command">What the Font Command tab shows, with the filters applied.</param>
/// <param name="Full">What the preview engraves, without the <c>\paper</c> filter.</param>
public readonly record struct FontCommandText(string Command, string Full);

/// <summary>
/// Writes the command that sets a document's fonts.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14), and it is the whole
/// point of <see cref="GenerateLily"/>.
/// </para>
/// <para>
/// Frescobaldi 4.0.7 writes
/// <c>#(define fonts (set-global-fonts #:music "…" #:brace "…" #:roman "…"
/// #:sans "…" #:typewriter "…" #:factor (/ staff-height pt 20)))</c> inside a
/// <c>\paper</c> block. <c>set-global-fonts</c> was REMOVED from LilyPond at
/// 2.25.4, replaced by ordinary paper variables; against 2.25.4 or later —
/// which is every LilyPond a user of Frescobaldi 4.0.7 is likely to have, and
/// is what this application engraves with — that command is an unbound
/// variable. So Frescobaldi's Document Fonts dialog, as shipped, generates dead
/// syntax. The STATUS file for this wave writes it up as a bug report.
/// </para>
/// <para>
/// What replaces it is what <c>paper-defaults-init.ly</c> itself writes, name
/// for name:
/// <c>property-defaults.fonts.{music,serif,sans,typewriter}</c>.
/// </para>
/// <para>
/// ⚠ BOARD TRAP 67: the LONG name is the only one that does anything. The
/// short <c>fonts.serif = "…"</c> form is a way-station inside convert-ly's
/// three-rule chain and is a SILENT no-op in a real <c>\paper</c> block —
/// it assigns a paper variable nothing reads. Never write the short form, and
/// never "fix" convert-ly to emit the long one directly; the port carries
/// upstream's chain verbatim and that is LilyPort's business, not this
/// application's.
/// </para>
/// <para>
/// ⚠ AND THERE IS NO <c>brace</c> PROPERTY. <c>set-global-fonts</c> took the
/// brace font as its own argument; the paper variables do not have one, because
/// the engine derives it — <c>FontInterface.SelectFont</c> appends
/// <c>-brace</c> to the MUSIC family's name for the <c>fetaBraces</c> encoding.
/// The dialog still tracks a brace font (the music-font list sets it to the
/// chosen family when that family has a brace face and to emmentaler when it
/// has not), because that is what tells the user whether the piano brace will
/// draw; it simply has nowhere to be written.
/// </para>
/// </remarks>
public static class FontCommand
{
    /// <summary>The <c>\paper</c> wrapper, upstream's own template.</summary>
    private const string PaperBlock = "\\paper {\n<<<command>>>\n}";

    /// <summary>The openLilyLib command, upstream's own template.</summary>
    private const string OllTemplate = "\\useNotationFont {0}\"{1}\"";

    /// <summary>The openLilyLib property clause, upstream's own template.</summary>
    private const string OllProperties = "\\with {\n<<<properties>>>\n} ";

    /// <summary>
    /// The paper variable each of the dialog's families is written to, and the
    /// order they are written in.
    /// </summary>
    /// <remarks>
    /// <c>roman</c> became <c>serif</c> in the same LilyPond release that
    /// removed <c>set-global-fonts</c>; convert-ly's own rule for 2.25.4 says
    /// <c>fonts.roman = ... -&gt; fonts.serif = ...</c>. The dialog keeps
    /// calling the family "Roman" because that is what its caption says and
    /// what its setting key is.
    /// </remarks>
    private static readonly (string Family, string Property)[] PaperProperties =
    {
        ("music", "music"),
        ("roman", "serif"),
        ("sans", "sans"),
        ("typewriter", "typewriter"),
    };

    /// <summary>Writes the command for one approach.</summary>
    /// <param name="approach">The approach.</param>
    /// <param name="fonts">The chosen fonts.</param>
    /// <param name="options">What the tab's controls decided.</param>
    /// <returns>The shown command and the full one.</returns>
    public static FontCommandText Generate(
        FontCommandApproach approach, FontSelection fonts, FontCommandOptions options)
        => approach == FontCommandApproach.OpenLilyLib
            ? GenerateOll(fonts, options)
            : GenerateLily(fonts, options);

    /// <summary>
    /// Writes the plain LilyPond command — a <c>\paper</c> block of font
    /// properties. See the class remarks for the FR14 divergence.
    /// </summary>
    /// <param name="fonts">The chosen fonts.</param>
    /// <param name="options">What the tab's controls decided.</param>
    /// <returns>The shown command and the full one.</returns>
    public static FontCommandText GenerateLily(
        FontSelection fonts, FontCommandOptions options)
    {
        if (fonts == null) { throw new ArgumentNullException(nameof(fonts)); }

        if (options == null) { throw new ArgumentNullException(nameof(options)); }

        List<string> definitions = new List<string>();
        foreach ((string family, string property) in PaperProperties)
        {
            if (!Included(family, options)) { continue; }

            definitions.Add(string.Format(
                CultureInfo.InvariantCulture,
                "  property-defaults.fonts.{0} = \"{1}\"", property, fonts[family]));
        }

        //Upstream builds ONE list and uses it for both commands — its
        //`add_font_def' appends to the full list only when the entry is
        //checked too, so the two are the same text. Kept, because the
        //preview is then a preview of the command that was written rather
        //than of a command nobody asked for.
        string command = string.Join("\n", definitions);
        return new FontCommandText(
            options.SetPaperBlock ? Wrap(command) : command, Wrap(command));
    }

    /// <summary>
    /// Writes the openLilyLib command. Character for character upstream's — the
    /// syntax belongs to the <c>notation-fonts</c> package rather than to
    /// LilyPond, so nothing about it changed at 2.25.4.
    /// </summary>
    /// <param name="fonts">The chosen fonts.</param>
    /// <param name="options">What the tab's controls decided.</param>
    /// <returns>The shown command and the full one.</returns>
    public static FontCommandText GenerateOll(
        FontSelection fonts, FontCommandOptions options)
    {
        if (fonts == null) { throw new ArgumentNullException(nameof(fonts)); }

        if (options == null) { throw new ArgumentNullException(nameof(options)); }

        List<string> command = new List<string>();
        List<string> fullCommand = new List<string>();
        List<string> properties = new List<string>();
        List<string> fullProperties = new List<string>();

        void AddProperty(string key, string value, bool included, bool force = true)
        {
            string property = "  " + key + " = " + value;
            if (included) { properties.Add(property); }

            if (included || force) { fullProperties.Add(property); }
        }

        const string OllInclude = "\\include \"oll-core/package.ily\"";
        fullCommand.Add(OllInclude);
        if (options.LoadOll) { command.Add(OllInclude); }

        const string PackageInclude = "\\loadPackage notation-fonts";
        fullCommand.Add(PackageInclude);
        if (options.LoadPackage) { command.Add(PackageInclude); }

        //Upstream's own TODO: "Support independent explicit brace font". The
        //property is never in the SHOWN command — it is added with
        //`checked=False' — but `force' defaults to true, so it IS in the full
        //command the preview engraves.
        AddProperty("brace", Quote(fonts["brace"]), included: false);

        AddProperty("roman", Quote(fonts["roman"]), options.SetRoman);
        AddProperty("sans", Quote(fonts["sans"]), options.SetSans);
        AddProperty("typewriter", Quote(fonts["typewriter"]), options.SetTypewriter);

        if (options.FontExtensions) { AddProperty("extensions", "##t", included: true); }

        //style_type == 0 is the default stylesheet and is not written.
        if (options.StyleType == 1)
        {
            AddProperty("style", "none", included: true);
        }
        else if (options.StyleType == 2)
        {
            AddProperty("style", Quote(options.FontStylesheet ?? string.Empty), true);
        }

        string fullClause = OllProperties.Replace(
            "<<<properties>>>", string.Join("\n", fullProperties), StringComparison.Ordinal);
        string clause = properties.Count > 0
            ? OllProperties.Replace(
                "<<<properties>>>", string.Join("\n", properties), StringComparison.Ordinal)
            : string.Empty;

        command.Add(string.Format(
            CultureInfo.InvariantCulture, OllTemplate, clause, fonts["music"]));
        fullCommand.Add(string.Format(
            CultureInfo.InvariantCulture, OllTemplate, fullClause, fonts["music"]));

        return new FontCommandText(
            string.Join("\n", command), string.Join("\n", fullCommand));
    }

    /// <summary>Answers whether a family is written at all.</summary>
    /// <param name="family">The family.</param>
    /// <param name="options">The options.</param>
    /// <returns>Whether it is.</returns>
    private static bool Included(string family, FontCommandOptions options)
        => family switch
        {
            "music" => options.SetMusic,
            "brace" => options.SetMusic,
            "roman" => options.SetRoman,
            "sans" => options.SetSans,
            "typewriter" => options.SetTypewriter,
            _ => false,
        };

    /// <summary>Puts a command inside a <c>\paper</c> block.</summary>
    /// <param name="command">The command.</param>
    /// <returns>The block.</returns>
    private static string Wrap(string command)
        => PaperBlock.Replace("<<<command>>>", command, StringComparison.Ordinal);

    /// <summary>Puts a font name in LilyPond quotes.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The quoted name.</returns>
    private static string Quote(string name) => "\"" + name + "\"";
}
