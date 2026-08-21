// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Ly.Colorizing; //was previously: ly/colorize.py (default_scheme)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A collection of styles: the base (default) styles, plus a set of
/// per-mode style dictionaries, each mapping a style name to its CSS
/// properties.
/// </summary>
/// <remarks>
/// Upstream a scheme is a plain dict whose <c>None</c> key holds the base
/// styles. A .NET dictionary cannot key on null, so the base styles get their
/// own property here; everything else keeps upstream's shape.
/// </remarks>
public sealed class CssScheme
{
    private readonly Dictionary<string, IDictionary<string, string>> _baseStyles;
    private readonly Dictionary<string, Dictionary<string, IDictionary<string, string>>> _modeStyles;

    /// <summary>Creates an empty scheme.</summary>
    public CssScheme()
    {
        _baseStyles = new Dictionary<string, IDictionary<string, string>>();
        _modeStyles = new Dictionary<string, Dictionary<string, IDictionary<string, string>>>();
    }

    /// <summary>
    /// Gets python-ly's default scheme: the base styles plus the LilyPond
    /// styles that draw distinctly (the other modes carry no overrides).
    /// </summary>
    public static CssScheme Default { get; } = CreateDefault();

    /// <summary>Gets the base (default) styles, by style name.</summary>
    public IReadOnlyDictionary<string, IDictionary<string, string>> BaseStyles
        => _baseStyles;

    /// <summary>Gets the mode names the scheme carries styles for.</summary>
    public IEnumerable<string> Modes => _modeStyles.Keys;

    /// <summary>Gets a base style's properties, or null.</summary>
    /// <param name="name">The base style name; null answers null.</param>
    /// <returns>The properties, or null.</returns>
    public IDictionary<string, string> BaseStyle(string name)
        => name != null && _baseStyles.TryGetValue(name, out var style) ? style : null;

    /// <summary>Gets a mode style's properties, or null.</summary>
    /// <param name="mode">The mode name.</param>
    /// <param name="name">The style name.</param>
    /// <returns>The properties, or null.</returns>
    public IDictionary<string, string> ModeStyle(string mode, string name)
        => mode != null && name != null
            && _modeStyles.TryGetValue(mode, out var styles)
            && styles.TryGetValue(name, out var style)
                ? style
                : null;

    /// <summary>Gets all of a mode's styles, by style name.</summary>
    /// <param name="mode">The mode name.</param>
    /// <returns>The styles; empty when the mode is unknown.</returns>
    public IReadOnlyDictionary<string, IDictionary<string, string>> ModeStyles(string mode)
        => mode != null && _modeStyles.TryGetValue(mode, out var styles)
            ? styles
            : new Dictionary<string, IDictionary<string, string>>();

    /// <summary>Sets a base style's properties.</summary>
    /// <param name="name">The base style name.</param>
    /// <param name="properties">The CSS properties.</param>
    public void SetBaseStyle(string name, IDictionary<string, string> properties)
        => _baseStyles[name] = properties
            ?? throw new ArgumentNullException(nameof(properties));

    /// <summary>Sets a mode style's properties.</summary>
    /// <param name="mode">The mode name.</param>
    /// <param name="name">The style name.</param>
    /// <param name="properties">The CSS properties.</param>
    public void SetModeStyle(
        string mode, string name, IDictionary<string, string> properties)
    {
        if (!_modeStyles.TryGetValue(mode, out var styles))
        {
            styles = _modeStyles[mode] =
                new Dictionary<string, IDictionary<string, string>>();
        }

        styles[name] = properties ?? throw new ArgumentNullException(nameof(properties));
    }

    /// <summary>Ensures a mode is present, even with no styles of its own.</summary>
    /// <param name="mode">The mode name.</param>
    public void AddMode(string mode)
    {
        if (!_modeStyles.ContainsKey(mode))
        {
            _modeStyles[mode] = new Dictionary<string, IDictionary<string, string>>();
        }
    }

    private static CssScheme CreateDefault()
    {
        CssScheme scheme = new CssScheme();

        scheme.SetBaseStyle("keyword", Css(("font-weight", "bold")));
        scheme.SetBaseStyle("function", Css(("font-weight", "bold"), ("color", "#0000c0")));
        scheme.SetBaseStyle("variable", Css(("color", "#0000ff")));
        scheme.SetBaseStyle("value", Css(("color", "#808000")));
        scheme.SetBaseStyle("string", Css(("color", "#c00000")));
        scheme.SetBaseStyle("escape", Css(("color", "#008080")));
        scheme.SetBaseStyle("comment", Css(("color", "#808080"), ("font-style", "italic")));
        scheme.SetBaseStyle("error", Css(
            ("color", "#ff0000"),
            ("text-decoration", "underline"),
            ("text-decoration-color", "#ff0000")));

        scheme.SetModeStyle("lilypond", "duration", Css(("color", "#008080")));
        scheme.SetModeStyle("lilypond", "markup", Css(
            ("color", "#008000"), ("font-weight", "normal")));
        scheme.SetModeStyle("lilypond", "lyricmode", Css(("color", "#006000")));
        scheme.SetModeStyle("lilypond", "lyrictext", Css(("color", "#006000")));
        scheme.SetModeStyle("lilypond", "grob", Css(("color", "#c000c0")));
        scheme.SetModeStyle("lilypond", "context", Css(("font-weight", "bold")));
        scheme.SetModeStyle("lilypond", "slur", Css(("font-weight", "bold")));
        scheme.SetModeStyle("lilypond", "articulation", Css(
            ("font-weight", "bold"), ("color", "#ff8000")));
        scheme.SetModeStyle("lilypond", "dynamic", Css(
            ("font-weight", "bold"), ("color", "#ff8000")));
        scheme.SetModeStyle("lilypond", "fingering", Css(("color", "#ff8000")));
        scheme.SetModeStyle("lilypond", "stringnumber", Css(("color", "#ff8000")));

        //Upstream declares these modes with empty style dicts.
        foreach (var mode in new[] { "scheme", "html", "texinfo", "mup" })
        {
            scheme.AddMode(mode);
        }

        return scheme;
    }

    private static IDictionary<string, string> Css(params (string Name, string Value)[] items)
        => items.ToDictionary(i => i.Name, i => i.Value, StringComparer.Ordinal);
}
