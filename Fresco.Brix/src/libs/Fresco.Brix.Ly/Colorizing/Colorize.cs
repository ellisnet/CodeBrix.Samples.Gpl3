// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Linq;
using Html = Fresco.Brix.Ly.Lex.HtmlMode;
using Lily = Fresco.Brix.Ly.Lex.LilyPondMode;
using Mup = Fresco.Brix.Ly.Lex.MupMode;
using Scm = Fresco.Brix.Ly.Lex.SchemeMode;
using Texi = Fresco.Brix.Ly.Lex.TexinfoMode;

namespace Fresco.Brix.Ly.Colorizing; //was previously: ly/colorize.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The mapping from token classes to highlighting styles, and the default
/// colour scheme those styles carry.
/// <para>
/// The app's Fonts &amp; Colors model (<c>textformats</c>) is built on this:
/// the mapping decides WHICH style a token gets, the scheme decides what that
/// style LOOKS like, and the user's overrides are merged over the scheme.
/// </para>
/// <para>
/// The HTML-writing half of upstream's module (<c>map_tokens</c>,
/// <c>melt_mapped_tokens</c>, <c>html()</c>, <c>HtmlWriter</c>) belongs to the
/// colored-HTML export, and is ported with it in W11; the CSS formatting
/// helpers it shares are here already.
/// </para>
/// </summary>
public static class Colorize
{
    private static IReadOnlyList<StyleGroup> _defaultMapping;

    /// <summary>
    /// Returns the default mapping from token classes to a style and its
    /// default style, per mode group.
    /// </summary>
    /// <returns>The groups, in upstream order.</returns>
    public static IReadOnlyList<StyleGroup> DefaultMapping()
        => _defaultMapping ??= new[]
        {
            new StyleGroup("lilypond",
                new Style("keyword", "keyword", typeof(Lily.Keyword)),
                new Style("command", "function", typeof(Lily.Command), typeof(Lily.Skip)),
                new Style("pitch", null, typeof(Lily.MusicItem)),
                new Style("octave", null, typeof(Lily.Octave)),
                new Style("accidental", null, typeof(Lily.Accidental), typeof(Lily.FigureAccidental)),
                new Style("duration", null, typeof(Lily.Duration)),
                new Style("dynamic", null, typeof(Lily.Dynamic)),
                new Style("check", null, typeof(Lily.OctaveCheck), typeof(Lily.PipeSymbol)),
                new Style("articulation", null, typeof(Lily.Direction), typeof(Lily.Articulation)),
                new Style("fingering", null, typeof(Lily.Fingering)),
                new Style("stringnumber", null, typeof(Lily.StringNumber)),
                new Style("slur", null, typeof(Lily.Slur)),
                new Style("beam", null, typeof(Lily.Beam), typeof(Lily.FigureBracket)),
                new Style("chord", null, typeof(Lily.Chord), typeof(Lily.ChordItem)),
                new Style("markup", "function", typeof(Lily.Markup)),
                new Style("lyricmode", "function", typeof(Lily.LyricMode)),
                new Style("lyrictext", null, typeof(Lily.Lyric)),
                new Style("repeat", "function", typeof(Lily.Repeat), typeof(Lily.Tremolo)),
                new Style("specifier", "variable", typeof(Lily.Specifier)),
                new Style("usercommand", "variable", typeof(Lily.UserCommand)),
                new Style("figbass", null, typeof(Lily.Figure)),
                new Style("figbstep", null, typeof(Lily.FigureStep)),
                new Style("figbmodif", null, typeof(Lily.FigureModifier)),
                new Style("delimiter", "keyword", typeof(Lily.Delimiter)),
                new Style("context", null, typeof(Lily.ContextName)),
                new Style("grob", null, typeof(Lily.GrobName)),
                new Style("property", "variable", typeof(Lily.ContextProperty)),
                new Style("variable", "variable", typeof(Lily.Variable)),
                new Style("uservariable", null, typeof(Lily.UserVariable)),
                new Style("value", "value", typeof(Lily.Value)),
                //upstream: lilypond.String — renamed StringBase in the port
                new Style("string", "string", typeof(Lily.StringBase)),
                new Style("stringescape", "escape", typeof(Lily.StringQuoteEscape)),
                new Style("comment", "comment", typeof(Lily.Comment)),
                new Style("error", "error", typeof(Lily.Error))),
            new StyleGroup("scheme",
                new Style("scheme", null, typeof(Lily.SchemeStart), typeof(Scm.Scheme)),
                new Style("string", "string", typeof(Scm.StringBase)),
                new Style("stringescape", "escape", typeof(Scm.StringQuoteEscape)),
                new Style("comment", "comment", typeof(Scm.Comment)),
                new Style("number", "value", typeof(Scm.Number)),
                new Style("lilypond", null, typeof(Scm.LilyPond)),
                new Style("keyword", "keyword", typeof(Scm.Keyword)),
                new Style("function", "function", typeof(Scm.Function)),
                new Style("variable", "variable", typeof(Scm.Variable)),
                new Style("constant", "variable", typeof(Scm.Constant)),
                new Style("delimiter", null, typeof(Scm.OpenParen), typeof(Scm.CloseParen))),
            new StyleGroup("html",
                new Style("tag", "keyword", typeof(Html.Tag)),
                new Style("attribute", "variable", typeof(Html.AttrName)),
                new Style("value", "value", typeof(Html.Value)),
                new Style("string", "string", typeof(Html.StringBase)),
                new Style("entityref", "escape", typeof(Html.EntityRef)),
                new Style("comment", "comment", typeof(Html.Comment)),
                new Style("lilypondtag", "function", typeof(Html.LilyPondTag))),
            new StyleGroup("texinfo",
                new Style("keyword", "keyword", typeof(Texi.Keyword)),
                new Style("block", "function", typeof(Texi.Block)),
                new Style("attribute", "variable", typeof(Texi.Attribute)),
                new Style("escapechar", "escape", typeof(Texi.EscapeChar)),
                new Style("verbatim", "string", typeof(Texi.Verbatim)),
                new Style("comment", "comment", typeof(Texi.Comment))),
            new StyleGroup("mup",
                new Style("string", "string", typeof(Mup.StringBase)),
                new Style("stringescape", "escape", typeof(Mup.StringQuoteEscape)),
                new Style("comment", "comment", typeof(Mup.Comment)),
                new Style("macro", "variable", typeof(Mup.Macro)),
                new Style("preprocessor", "keyword", typeof(Mup.Preprocessor))),
        };

    /// <summary>
    /// The bases the lex port dropped when it flattened python's multiple
    /// inheritance, for the classes where the DROPPED base is one this module
    /// maps to a style.
    /// <para>
    /// In every entry python declares <c>class X(Mapped, _token.Kept)</c>: the
    /// mapped base comes FIRST in python's MRO, so the style resolver has to
    /// see it before the base the port actually derives from. The port keeps
    /// the <c>_token</c> base because the lexer machinery tests for it; the
    /// style side compensates here rather than re-shaping the token tree.
    /// </para>
    /// <para>
    /// Not every flattening needs an entry — most drop an unmapped base
    /// (<c>_token.Leaver</c>, <c>_token.Item</c>) or drop a base that python's
    /// MRO puts SECOND anyway (<c>MarkupStart(Markup, Command)</c> resolves to
    /// <c>markup</c> in both). Only these four pairs change an answer.
    /// </para>
    /// </summary>
    private static readonly Dictionary<Type, Type[]> FlattenedBases
        = new Dictionary<Type, Type[]>
        {
            //lilypond: StringQuotedStart(String, _token.StringStart) etc.
            { typeof(Lily.StringQuotedStart), new[] { typeof(Lily.StringBase) } },
            { typeof(Lily.StringQuotedEnd), new[] { typeof(Lily.StringBase) } },
            //lilypond: LineComment(Comment, _token.LineComment) etc.
            { typeof(Lily.LineComment), new[] { typeof(Lily.Comment) } },
            { typeof(Lily.BlockComment), new[] { typeof(Lily.Comment) } },
            { typeof(Lily.BlockCommentStart), new[] { typeof(Lily.Comment) } },
            { typeof(Lily.BlockCommentEnd), new[] { typeof(Lily.Comment) } },
            //scheme: the same two shapes
            { typeof(Scm.StringQuotedStart), new[] { typeof(Scm.StringBase) } },
            { typeof(Scm.StringQuotedEnd), new[] { typeof(Scm.StringBase) } },
            { typeof(Scm.LineComment), new[] { typeof(Scm.Comment) } },
            { typeof(Scm.BlockComment), new[] { typeof(Scm.Comment) } },
            { typeof(Scm.BlockCommentStart), new[] { typeof(Scm.Comment) } },
            { typeof(Scm.BlockCommentEnd), new[] { typeof(Scm.Comment) } },
        };

    /// <summary>
    /// The base-class walk that reproduces python's MRO for style lookup:
    /// the declared C# chain with the <see cref="FlattenedBases"/> entries
    /// spliced in ahead of the base that survived the flattening.
    /// </summary>
    /// <param name="tokenClass">The token class.</param>
    /// <returns>The bases, in python MRO order.</returns>
    public static IEnumerable<Type> PythonBases(Type tokenClass)
    {
        for (var t = tokenClass;
             t != null && t != typeof(Lex.Token);
             t = t.BaseType)
        {
            if (FlattenedBases.TryGetValue(t, out var dropped))
            {
                //The dropped base's own bases are already further along the
                //declared chain, so only the class itself is spliced in.
                foreach (var b in dropped)
                {
                    yield return b;
                }
            }

            if (t.BaseType != null && t.BaseType != typeof(Lex.Token))
            {
                yield return t.BaseType;
            }
        }
    }

    /// <summary>
    /// Returns a mapper from token classes to their <see cref="CssClass"/>.
    /// </summary>
    /// <param name="mapping">The mapping, or null for
    /// <see cref="DefaultMapping"/>.</param>
    /// <returns>The mapper.</returns>
    public static TokenMapper<CssClass> CssMapper(IReadOnlyList<StyleGroup> mapping = null)
        => new TokenMapper<CssClass>(
            (mapping ?? DefaultMapping())
                .SelectMany(group => group.Styles
                    .SelectMany(style => style.Classes
                        .Select(cls => new KeyValuePair<Type, CssClass>(
                            cls, new CssClass(group.Mode, style.Name, style.Base))))),
            PythonBases);

    /// <summary>
    /// Returns the CSS property dictionary for a style, taken from a scheme:
    /// the base style's properties with the mode-specific ones merged over.
    /// </summary>
    /// <param name="cssClass">The style.</param>
    /// <param name="scheme">The scheme, or null for
    /// <see cref="CssScheme.Default"/>.</param>
    /// <returns>A new dictionary; empty when the scheme says nothing.</returns>
    public static IDictionary<string, string> CssDict(
        CssClass cssClass, CssScheme scheme = null)
    {
        Dictionary<string, string> result = new Dictionary<string, string>();
        if (cssClass == null) { return result; }

        scheme ??= CssScheme.Default;
        Merge(result, scheme.BaseStyle(cssClass.Base));
        Merge(result, scheme.ModeStyle(cssClass.Mode, cssClass.Name));
        return result;
    }

    /// <summary>Formats one CSS property as <c>name: value;</c>.</summary>
    /// <param name="item">The property.</param>
    /// <returns>The formatted item.</returns>
    public static string CssItem(KeyValuePair<string, string> item)
        => $"{item.Key}: {item.Value};";

    /// <summary>
    /// Returns the inline <c>style</c> attribute value for a property
    /// dictionary, or null when it is empty.
    /// </summary>
    /// <param name="properties">The properties.</param>
    /// <returns>The attribute value, or null.</returns>
    public static string CssAttr(IDictionary<string, string> properties)
        => properties == null || properties.Count == 0
            ? null
            : string.Join(" ", Sorted(properties).Select(CssItem));

    /// <summary>Returns a <c>selector { … }</c> stylesheet section.</summary>
    /// <param name="selector">The CSS selector.</param>
    /// <param name="properties">The properties.</param>
    /// <returns>The formatted group.</returns>
    public static string CssGroup(string selector, IDictionary<string, string> properties)
        => selector + " {\n  "
            + string.Join("\n  ", Sorted(properties).Select(CssItem))
            + "\n}\n";

    /// <summary>
    /// Returns a formatted stylesheet for a scheme — base styles first, then
    /// each mode's styles, both sorted by name as upstream sorts them.
    /// </summary>
    /// <param name="scheme">The scheme, or null for
    /// <see cref="CssScheme.Default"/>.</param>
    /// <returns>The stylesheet text.</returns>
    public static string FormatStylesheet(CssScheme scheme = null)
    {
        scheme ??= CssScheme.Default;
        List<string> sheet = new List<string>();

        //Upstream sorts the scheme items with '' standing in for the None key,
        //so the base styles always come first.
        AppendSection(sheet, null, scheme.BaseStyles);
        foreach (var mode in scheme.Modes.OrderBy(m => m, StringComparer.Ordinal))
        {
            AppendSection(sheet, mode, scheme.ModeStyles(mode));
        }

        return string.Join("\n", sheet);
    }

    /// <summary>Escapes <c>&amp;</c>, <c>&lt;</c> and <c>&gt;</c>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The escaped text.</returns>
    public static string HtmlEscape(string text)
        => (text ?? string.Empty)
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

    /// <summary>Escapes <c>&amp;</c>, <c>"</c>, <c>&lt;</c> and <c>&gt;</c>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The escaped text.</returns>
    public static string HtmlEscapeAttr(string text)
        => HtmlEscape(text).Replace("\"", "&quot;");

    /// <summary>
    /// Returns the <c>class="mode-style base"</c> attribute for a style.
    /// </summary>
    /// <param name="cssClass">The style.</param>
    /// <returns>The attribute text.</returns>
    public static string FormatCssSpanClass(CssClass cssClass)
    {
        var c = cssClass.Mode + "-" + cssClass.Name;
        if (cssClass.Base != null)
        {
            c += " " + cssClass.Base;
        }

        return $"class=\"{c}\"";
    }

    private static void AppendSection(
        List<string> sheet, string mode,
        IReadOnlyDictionary<string, IDictionary<string, string>> styles)
    {
        if (styles == null || styles.Count == 0) { return; }

        sheet.Add("/* " + (mode == null ? "base styles" : "mode: " + mode) + " */");
        foreach (var name in styles.Keys.OrderBy(k => k, StringComparer.Ordinal))
        {
            var selector = mode == null ? "." + name : $"span.{mode}-{name}";
            sheet.Add(CssGroup(selector, styles[name]));
        }
    }

    private static IEnumerable<KeyValuePair<string, string>> Sorted(
        IDictionary<string, string> properties)
        => (properties ?? new Dictionary<string, string>())
            .OrderBy(p => p.Key, StringComparer.Ordinal);

    private static void Merge(
        IDictionary<string, string> target, IDictionary<string, string> source)
    {
        if (source == null) { return; }

        foreach (var item in source)
        {
            target[item.Key] = item.Value;
        }
    }
}
