// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly.Colorizing;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Windows.UI;

namespace Fresco.Brix.Editor; //was previously: frescobaldi/textformats.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// How one kind of text looks: its colours and whether it is bold, italic or
/// underlined. Any of them may be unset, in which case the surrounding style
/// decides.
/// </summary>
public sealed class TextFormat
{
    /// <summary>Gets or sets the text colour, or null to inherit it.</summary>
    public Color? Foreground { get; set; }

    /// <summary>Gets or sets the background colour, or null for none.</summary>
    public Color? Background { get; set; }

    /// <summary>Gets or sets the underline colour, or null to follow the text.</summary>
    public Color? UnderlineColor { get; set; }

    /// <summary>Gets or sets boldness, or null to inherit it.</summary>
    public bool? IsBold { get; set; }

    /// <summary>Gets or sets italicness, or null to inherit it.</summary>
    public bool? IsItalic { get; set; }

    /// <summary>Gets or sets underlining, or null to inherit it.</summary>
    public bool? IsUnderlined { get; set; }

    /// <summary>Gets whether the format sets nothing at all.</summary>
    public bool IsEmpty
        => Foreground == null && Background == null && UnderlineColor == null
            && IsBold == null && IsItalic == null && IsUnderlined == null;

    /// <summary>Answers a copy.</summary>
    /// <returns>The copy.</returns>
    public TextFormat Clone()
        => new TextFormat
        {
            Foreground = Foreground,
            Background = Background,
            UnderlineColor = UnderlineColor,
            IsBold = IsBold,
            IsItalic = IsItalic,
            IsUnderlined = IsUnderlined,
        };

    /// <summary>Merges another format over this one; its set properties win.</summary>
    /// <param name="other">The format to merge in.</param>
    public void Merge(TextFormat other)
    {
        if (other == null) { return; }

        Foreground = other.Foreground ?? Foreground;
        Background = other.Background ?? Background;
        UnderlineColor = other.UnderlineColor ?? UnderlineColor;
        IsBold = other.IsBold ?? IsBold;
        IsItalic = other.IsItalic ?? IsItalic;
        IsUnderlined = other.IsUnderlined ?? IsUnderlined;
    }

    /// <summary>Reads a format from a CSS property dictionary.</summary>
    /// <param name="css">The properties.</param>
    /// <returns>The format.</returns>
    public static TextFormat FromCss(IDictionary<string, string> css)
    {
        TextFormat format = new TextFormat();
        if (css == null) { return format; }

        if (css.TryGetValue("font-style", out var style))
        {
            format.IsItalic = style == "oblique" || style == "italic";
        }

        if (css.TryGetValue("font-weight", out var weight))
        {
            if (weight == "bold") { format.IsBold = true; }
            else if (weight == "normal") { format.IsBold = false; }
            else if (int.TryParse(weight, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var numeric))
            {
                format.IsBold = numeric >= 700;
            }
        }

        if (css.TryGetValue("color", out var color))
        {
            format.Foreground = ParseColor(color);
        }

        if (css.TryGetValue("background", out var background))
        {
            format.Background = ParseColor(background);
        }

        if (css.TryGetValue("text-decoration", out var decoration))
        {
            format.IsUnderlined = decoration == "underline";
        }

        if (css.TryGetValue("text-decoration-color", out var underlineColor))
        {
            format.UnderlineColor = ParseColor(underlineColor);
        }

        return format;
    }

    /// <summary>Writes the format back out as CSS properties.</summary>
    /// <returns>The properties; only the ones actually set.</returns>
    public IDictionary<string, string> ToCss()
    {
        Dictionary<string, string> css
            = new Dictionary<string, string>(StringComparer.Ordinal);
        if (IsBold != null) { css["font-weight"] = IsBold.Value ? "bold" : "normal"; }

        if (IsItalic != null) { css["font-style"] = IsItalic.Value ? "italic" : "normal"; }

        if (IsUnderlined != null)
        {
            css["text-decoration"] = IsUnderlined.Value ? "underline" : "none";
        }

        if (Foreground != null) { css["color"] = FormatColor(Foreground.Value); }

        if (Background != null) { css["background"] = FormatColor(Background.Value); }

        if (UnderlineColor != null)
        {
            css["text-decoration-color"] = FormatColor(UnderlineColor.Value);
        }

        return css;
    }

    /// <summary>Reads a <c>#rrggbb</c> colour.</summary>
    /// <param name="text">The colour text.</param>
    /// <returns>The colour, or null when it does not read.</returns>
    public static Color? ParseColor(string text)
    {
        if (string.IsNullOrEmpty(text) || text[0] != '#') { return null; }

        string digits = text.Substring(1);
        if (digits.Length == 3)
        {
            //The short CSS form: each digit stands for a doubled pair.
            digits = string.Concat(digits.Select(c => new string(c, 2)));
        }

        if (digits.Length != 6
            || !uint.TryParse(digits, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var value))
        {
            return null;
        }

        return Color.FromArgb(
            255,
            (byte)((value >> 16) & 0xFF),
            (byte)((value >> 8) & 0xFF),
            (byte)(value & 0xFF));
    }

    /// <summary>Writes a colour as <c>#rrggbb</c>.</summary>
    /// <param name="color">The colour.</param>
    /// <returns>The colour text.</returns>
    public static string FormatColor(Color color)
        => $"#{color.R:x2}{color.G:x2}{color.B:x2}";
}

/// <summary>
/// A complete Fonts &amp; Colors scheme: the editor's base colours, the
/// default styles every highlighting style inherits from, and the per-mode
/// styles themselves — with the user's overrides merged over python-ly's
/// defaults.
/// </summary>
/// <remarks>
/// Upstream keeps <c>editor</c> and <c>printer</c> schemes side by side, the
/// printer one feeding the colored-HTML export (W11).
/// </remarks>
public sealed class TextFormatData
{
    /// <summary>The base colour names, in upstream's order.</summary>
    public static readonly IReadOnlyList<string> BaseColorNames = new[]
    {
        "text", "background", "selectiontext", "selectionbackground",
        "current", "mark", "error", "search", "match", "paper", "musichighlight",
    };

    /// <summary>The default styles every specific style may inherit from.</summary>
    public static readonly IReadOnlyList<string> DefaultStyleNames = new[]
    {
        "keyword", "function", "variable", "value",
        "string", "escape", "comment", "error",
    };

    private readonly Dictionary<string, Color> _baseColors
        = new Dictionary<string, Color>(StringComparer.Ordinal);
    private readonly Dictionary<string, TextFormat> _defaultStyles
        = new Dictionary<string, TextFormat>(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, TextFormat>> _allStyles
        = new Dictionary<string, Dictionary<string, TextFormat>>(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, string>> _inherits
        = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

    /// <summary>Loads a scheme.</summary>
    /// <param name="scheme">The scheme name, e.g. <c>default</c>.</param>
    /// <param name="settings">The store the user's overrides live in, or null
    /// for python-ly's defaults alone.</param>
    /// <param name="kind">Which scheme set to read — <c>editor</c> or
    /// <c>printer</c>.</param>
    public TextFormatData(
        string scheme = "default", SettingsStore settings = null, string kind = "editor")
    {
        Scheme = scheme ?? "default";
        Kind = kind ?? "editor";
        Load(settings);
    }

    /// <summary>The setting naming the scheme the editor is drawn with.</summary>
    /// <remarks>Upstream's own key, read by <c>SchemeSelector</c>.</remarks>
    public const string SchemeSettingKey = "editor_scheme";

    /// <summary>The setting group holding the user's scheme names.</summary>
    /// <remarks>Upstream's own key: <c>editor_schemes/&lt;key&gt;</c> holds the
    /// name the user gave the scheme <c>&lt;key&gt;</c>.</remarks>
    public const string SchemeNamesKey = "editor_schemes";

    /// <summary>
    /// The setting naming the scheme used for exported source, which is where
    /// upstream's printing went (FR5.5 removed printing itself).
    /// </summary>
    public const string PrinterSchemeSettingKey = "printer_scheme";

    /// <summary>Gets the scheme the editor is currently drawn with.</summary>
    /// <param name="settings">The settings store, or null.</param>
    /// <returns>The scheme name; <c>default</c> when none is chosen.</returns>
    public static string CurrentScheme(SettingsStore settings)
    {
        string scheme = settings?.GetString(SchemeSettingKey, "default");
        return string.IsNullOrEmpty(scheme) ? "default" : scheme;
    }

    /// <summary>Gets the scheme exported source is coloured with.</summary>
    /// <param name="settings">The settings store, or null.</param>
    /// <returns>The scheme name; the editor's own when none is chosen.</returns>
    public static string PrinterScheme(SettingsStore settings)
    {
        string scheme = settings?.GetString(PrinterSchemeSettingKey);
        return string.IsNullOrEmpty(scheme) ? CurrentScheme(settings) : scheme;
    }

    /// <summary>Gets the scheme name.</summary>
    public string Scheme { get; }

    /// <summary>Gets which scheme set this is (<c>editor</c>/<c>printer</c>).</summary>
    public string Kind { get; }

    /// <summary>
    /// The editor's size when the user has chosen none.
    /// </summary>
    /// <remarks>//was previously: 10.0, which was a placeholder — nothing read
    /// the value. The editor itself has always been drawn at 13, and W12A's
    /// Fonts &amp; Colors page makes the setting real, so the default IS what
    /// the editor does.</remarks>
    public const double DefaultFontSize = 13.0;

    /// <summary>
    /// Gets or sets the editor font family, or null for the application's own
    /// monospace face.
    /// </summary>
    /// <remarks>A family here is one of the faces the application SHIPS: there
    /// is no system-font fallback anywhere in Fresco.Brix (standing rule 6),
    /// so the Fonts &amp; Colors page offers what is in the box and nothing
    /// else.</remarks>
    public string FontFamily { get; set; }

    /// <summary>Gets or sets the editor font size, in points.</summary>
    public double FontSize { get; set; } = DefaultFontSize;

    /// <summary>Gets the base colours, by name.</summary>
    public IReadOnlyDictionary<string, Color> BaseColors => _baseColors;

    /// <summary>Gets a base colour.</summary>
    /// <param name="name">The colour name.</param>
    /// <returns>The colour.</returns>
    public Color BaseColor(string name)
        => _baseColors.TryGetValue(name, out var color)
            ? color
            : Color.FromArgb(255, 0, 0, 0);

    /// <summary>
    /// Gets the format for a style — its default style with the mode-specific
    /// overrides merged over it, which is what actually gets drawn.
    /// </summary>
    /// <param name="mode">The mode, e.g. <c>lilypond</c>.</param>
    /// <param name="name">The style name, e.g. <c>markup</c>.</param>
    /// <returns>The format, or an empty one for an unknown style.</returns>
    public TextFormat TextFormatFor(string mode, string name)
    {
        string inherited = _inherits.TryGetValue(mode ?? string.Empty, out var group)
            && group.TryGetValue(name ?? string.Empty, out var baseName)
                ? baseName
                : null;

        TextFormat format = inherited != null
            && _defaultStyles.TryGetValue(inherited, out var defaultStyle)
                ? defaultStyle.Clone()
                : new TextFormat();

        if (_allStyles.TryGetValue(mode ?? string.Empty, out var styles)
            && styles.TryGetValue(name ?? string.Empty, out var specific))
        {
            format.Merge(specific);
        }

        return format;
    }

    /// <summary>Gets the format for a colorize style.</summary>
    /// <param name="cssClass">The style.</param>
    /// <returns>The format.</returns>
    public TextFormat TextFormatFor(CssClass cssClass)
        => cssClass == null
            ? new TextFormat()
            : TextFormatFor(cssClass.Mode, cssClass.Name);

    /// <summary>Gets a default style's format, for editing.</summary>
    /// <param name="name">The default style name.</param>
    /// <returns>The format, or null when the name is unknown.</returns>
    public TextFormat DefaultStyle(string name)
        => _defaultStyles.TryGetValue(name ?? string.Empty, out var format)
            ? format
            : null;

    /// <summary>Gets a mode style's own format, for editing.</summary>
    /// <param name="mode">The mode.</param>
    /// <param name="name">The style name.</param>
    /// <returns>The format, or null when either name is unknown.</returns>
    public TextFormat ModeStyle(string mode, string name)
        => _allStyles.TryGetValue(mode ?? string.Empty, out var styles)
            && styles.TryGetValue(name ?? string.Empty, out var format)
                ? format
                : null;

    /// <summary>Sets a base colour.</summary>
    /// <param name="name">The colour name.</param>
    /// <param name="color">The colour.</param>
    public void SetBaseColor(string name, Color color) => _baseColors[name] = color;

    /// <summary>
    /// Writes the scheme out, storing only what differs from the defaults.
    /// </summary>
    /// <param name="settings">The store.</param>
    public void Save(SettingsStore settings)
    {
        if (settings == null) { throw new ArgumentNullException(nameof(settings)); }

        Dictionary<string, string> values = ReadScheme(settings, Kind, Scheme);
        if (FontFamily != null) { values["fontfamily"] = FontFamily; }

        if (FontSize > 0)
        {
            values["fontsize"] = FontSize.ToString("R", CultureInfo.InvariantCulture);
        }

        foreach (var pair in _baseColors)
        {
            values["basecolors/" + pair.Key] = TextFormat.FormatColor(pair.Value);
        }

        foreach (var pair in _defaultStyles)
        {
            SaveFormat(values, "defaultstyles/" + pair.Key, pair.Value);
        }

        foreach (var group in _allStyles)
        {
            foreach (var pair in group.Value)
            {
                SaveFormat(values,
                    "allstyles/" + group.Key + "/" + pair.Key, pair.Value);
            }
        }

        WriteScheme(settings, Kind, Scheme, values);
    }

    /// <summary>
    /// The settings key ONE scheme's fonts and colours live under — one key
    /// holding every value as a JSON object.
    /// </summary>
    /// <param name="kind">Which scheme set — <c>editor</c> or <c>printer</c>.</param>
    /// <param name="scheme">The scheme name.</param>
    /// <returns>The key.</returns>
    /// <remarks>//was previously: the same text as a settings key PREFIX, with
    /// a key per value under it (upstream's <c>fontscolors/{kind}/{scheme}/…</c>
    /// group), which the flat store this replaced could delete by prefix when a
    /// scheme was removed. The settings add-in has no prefix-scan API by design
    /// (board W13 item 9, route (a)).</remarks>
    public static string SchemeKey(string kind, string scheme)
        => $"fontscolors/{kind}/{scheme}";

    /// <summary>Forgets everything one scheme remembers.</summary>
    /// <param name="settings">The settings store.</param>
    /// <param name="kind">Which scheme set — <c>editor</c> or <c>printer</c>.</param>
    /// <param name="scheme">The scheme being removed.</param>
    /// <remarks>What the Fonts &amp; Colors preferences page hands
    /// <c>SchemeSelector.SaveSettings</c> so that a removed scheme's colours go
    /// with it.</remarks>
    public static void ForgetScheme(SettingsStore settings, string kind, string scheme)
    {
        if (settings == null || string.IsNullOrEmpty(scheme)) { return; }

        settings.Remove(SchemeKey(kind, scheme));
    }

    /// <summary>
    /// Answers the scheme as a colorize CSS scheme, which is what the
    /// colored-HTML export writes its stylesheet from.
    /// </summary>
    /// <returns>The CSS scheme.</returns>
    public CssScheme ToCssScheme()
    {
        CssScheme scheme = new CssScheme();
        foreach (var pair in _defaultStyles)
        {
            scheme.SetBaseStyle(pair.Key, pair.Value.ToCss());
        }

        foreach (var group in _allStyles)
        {
            scheme.AddMode(group.Key);
            foreach (var pair in group.Value)
            {
                scheme.SetModeStyle(group.Key, pair.Key, pair.Value.ToCss());
            }
        }

        return scheme;
    }

    private void Load(SettingsStore settings)
    {
        Dictionary<string, string> values = ReadScheme(settings, Kind, Scheme);
        FontFamily = Value(values, "fontfamily");
        FontSize = double.TryParse(Value(values, "fontsize"), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var size)
            ? size
            : DefaultFontSize;

        foreach (var name in BaseColorNames)
        {
            Color? stored = TextFormat.ParseColor(Value(values, "basecolors/" + name));
            _baseColors[name] = stored ?? DefaultBaseColor(name);
        }

        //Which styles exist, and what each inherits, comes from python-ly's
        //mapping — so a style added upstream shows up here without a change.
        IReadOnlyList<StyleGroup> mapping = Colorize.DefaultMapping();
        HashSet<string> defaultStyles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in mapping)
        {
            Dictionary<string, string> inherits
                = new Dictionary<string, string>(StringComparer.Ordinal);
            _inherits[group.Mode] = inherits;
            foreach (var style in group.Styles.Where(s => s.Base != null))
            {
                defaultStyles.Add(style.Base);
                inherits[style.Name] = style.Base;
            }
        }

        foreach (var name in defaultStyles)
        {
            TextFormat format = TextFormat.FromCss(
                CssScheme.Default.BaseStyle(name));
            LoadFormat(values, "defaultstyles/" + name, format);
            _defaultStyles[name] = format;
        }

        foreach (var group in mapping)
        {
            Dictionary<string, TextFormat> styles
                = new Dictionary<string, TextFormat>(StringComparer.Ordinal);
            _allStyles[group.Mode] = styles;
            foreach (var style in group.Styles)
            {
                TextFormat format = TextFormat.FromCss(
                    CssScheme.Default.ModeStyle(group.Mode, style.Name));
                LoadFormat(values,
                    "allstyles/" + group.Mode + "/" + style.Name, format);
                styles[style.Name] = format;
            }
        }
    }

    private static Dictionary<string, string> ReadScheme(
        SettingsStore settings, string kind, string scheme)
        => settings?.Get<Dictionary<string, string>>(SchemeKey(kind, scheme))
            ?? new Dictionary<string, string>(StringComparer.Ordinal);

    private static void WriteScheme(
        SettingsStore settings, string kind, string scheme,
        Dictionary<string, string> values)
    {
        if (values.Count == 0)
        {
            settings.Remove(SchemeKey(kind, scheme));
        }
        else
        {
            settings.Set(SchemeKey(kind, scheme), values);
        }
    }

    private static string Value(Dictionary<string, string> values, string name)
        => values.TryGetValue(name, out var value) ? value : null;

    private static void LoadFormat(
        Dictionary<string, string> values, string key, TextFormat format)
    {
        string bold = Value(values, key + "/bold");
        if (bold != null) { format.IsBold = bold == "1"; }

        string italic = Value(values, key + "/italic");
        if (italic != null) { format.IsItalic = italic == "1"; }

        string underline = Value(values, key + "/underline");
        if (underline != null) { format.IsUnderlined = underline == "1"; }

        Color? foreground = TextFormat.ParseColor(Value(values, key + "/textColor"));
        if (foreground != null) { format.Foreground = foreground; }

        Color? background =
            TextFormat.ParseColor(Value(values, key + "/backgroundColor"));
        if (background != null) { format.Background = background; }

        Color? underlineColor =
            TextFormat.ParseColor(Value(values, key + "/underlineColor"));
        if (underlineColor != null) { format.UnderlineColor = underlineColor; }
    }

    private static void SaveFormat(
        Dictionary<string, string> values, string key, TextFormat format)
    {
        Write(values, key + "/bold", format.IsBold);
        Write(values, key + "/italic", format.IsItalic);
        Write(values, key + "/underline", format.IsUnderlined);
        Write(values, key + "/textColor", format.Foreground);
        Write(values, key + "/backgroundColor", format.Background);
        Write(values, key + "/underlineColor", format.UnderlineColor);
    }

    private static void Write(
        Dictionary<string, string> values, string key, bool? value)
    {
        if (value == null) { values.Remove(key); } else { values[key] = value.Value ? "1" : "0"; }
    }

    private static void Write(
        Dictionary<string, string> values, string key, Color? value)
    {
        if (value == null)
        {
            values.Remove(key);
        }
        else
        {
            values[key] = TextFormat.FormatColor(value.Value);
        }
    }

    /// <summary>
    /// The colour a base name falls back to.
    /// </summary>
    /// <param name="name">The colour name.</param>
    /// <returns>The colour.</returns>
    /// <remarks>
    /// Upstream takes text/background/selection from the desktop palette; the
    /// house rule against depending on the host's look applies to colours as
    /// much as to fonts, so these are fixed values matching Qt's usual light
    /// palette. The Fonts &amp; Colors page (W12) is where a user changes them,
    /// and a dark scheme ships as a scheme rather than as a palette read.
    /// </remarks>
    private static Color DefaultBaseColor(string name)
        => name switch
        {
            "text" => Color.FromArgb(255, 0, 0, 0),
            "background" => Color.FromArgb(255, 255, 255, 255),
            "selectiontext" => Color.FromArgb(255, 255, 255, 255),
            "selectionbackground" => Color.FromArgb(255, 0x30, 0x8C, 0xC6),
            "current" => Color.FromArgb(255, 255, 252, 149),
            "mark" => Color.FromArgb(255, 192, 192, 255),
            "error" => Color.FromArgb(255, 255, 192, 192),
            "search" => Color.FromArgb(255, 192, 255, 192),
            "match" => Color.FromArgb(255, 0, 192, 255),
            "paper" => Color.FromArgb(255, 255, 253, 240),
            "musichighlight" => Color.FromArgb(255, 0x30, 0x8C, 0xC6),
            _ => Color.FromArgb(255, 0, 0, 0),
        };
}
