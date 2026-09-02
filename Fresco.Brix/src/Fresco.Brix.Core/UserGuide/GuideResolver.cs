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
using System.Text;
using System.Text.RegularExpressions;

namespace Fresco.Brix.UserGuide; //was previously: frescobaldi/userguide/page.py (Resolver) + resolve.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>What a page's <c>{variable}</c> turned out to be.</summary>
/// <remarks>
/// Upstream resolves a variable straight to an HTML fragment, because a
/// <c>QTextBrowser</c> is the only thing that ever reads it. There is no web
/// view here (FR8), so a resolved variable carries BOTH: the HTML, which is
/// what the parity fixtures recorded from Frescobaldi compare against, and the
/// kind and raw text, which is what <see cref="GuideRenderer"/> draws with the
/// platform's own controls.
/// </remarks>
public sealed class GuideValue
{
    /// <summary>Creates a value.</summary>
    /// <param name="kind">The variable's type, lower-cased.</param>
    /// <param name="text">The variable's raw content.</param>
    /// <param name="html">The HTML upstream would produce for it.</param>
    public GuideValue(string kind, string text, string html)
    {
        Kind = kind;
        Text = text;
        Html = html;
    }

    /// <summary>Gets the variable's type: <c>md</c>, <c>help</c>, … .</summary>
    public string Kind { get; }

    /// <summary>Gets the variable's raw content.</summary>
    public string Text { get; }

    /// <summary>Gets the HTML upstream produces for it.</summary>
    public string Html { get; }
}

/// <summary>
/// The services a page's variables are resolved against: the other pages'
/// titles, the keyboard shortcuts in force, the menu names, and the
/// application's own identity.
/// </summary>
/// <remarks>
/// Upstream reaches into <c>appinfo</c>, <c>actioncollectionmanager</c>,
/// <c>qutil</c> and <c>language_names</c> from inside the resolver. Here they
/// are handed in, so a page's HTML can be produced in a test with no window,
/// no action collections and no settings — which is what the parity tests do.
/// </remarks>
public sealed class GuideContext
{
    /// <summary>The predefined menu names a page may use.</summary>
    /// <remarks>
    /// Upstream's own table, with ONE renamed entry: <c>lilypond</c> is this
    /// application's <c>&amp;LilyPort</c> menu (ruling FR13 — no UI element
    /// names LilyPond). The msgid changes with it, which is why W-I18N's
    /// renamed-string table has to carry it. It is a PROPERTY rather than a
    /// global so a parity test can hand the resolver upstream's own table and
    /// compare a page byte for byte.
    /// </remarks>
    public static readonly IReadOnlyDictionary<string, string> DefaultMenuNames
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["file"] = "menu title|&File",
            ["edit"] = "menu title|&Edit",
            ["view"] = "menu title|&View",
            ["snippets"] = "menu title|Sn&ippets",
            ["music"] = "menu title|&Music",
            //was previously: 'lilypond': 'menu title|&LilyPond'
            ["lilypond"] = "menu title|&LilyPort",
            ["tools"] = "menu title|&Tools",
            ["window"] = "menu title|&Window",
            ["session"] = "menu title|&Session",
            ["help"] = "menu title|&Help",
        };

    /// <summary>Gets or sets the predefined menu names.</summary>
    public IReadOnlyDictionary<string, string> MenuNames { get; set; }
        = DefaultMenuNames;

    /// <summary>Gets or sets how a page's title is looked up.</summary>
    public Func<string, string> PageTitle { get; set; }

    /// <summary>
    /// Gets or sets how the shortcut of an action is looked up, by its
    /// collection name and action name.
    /// </summary>
    public Func<string, string, string> Shortcut { get; set; }

    /// <summary>Gets or sets how a language's name is looked up by code.</summary>
    /// <remarks>
    /// Upstream's <c>language_names.languageName(code, current)</c>, whose data
    /// package arrived with W-I18N and is now
    /// <see cref="Services.LanguageNames"/>.
    /// </remarks>
    public Func<string, string> LanguageName { get; set; } = DefaultLanguageName;

    /// <summary>Gets or sets the table of contents, as HTML.</summary>
    public Func<string> TableOfContents { get; set; }

    /// <summary>Gets or sets the application name.</summary>
    public string AppName { get; set; } = AppInfo.AppName;

    /// <summary>Gets or sets the application version.</summary>
    public string Version { get; set; } = AppInfo.Version;

    /// <summary>Gets or sets the author the manual names.</summary>
    public string Author { get; set; } = AppInfo.Maintainer;

    /// <summary>
    /// Gets the name of the language a translated manual would credit.
    /// </summary>
    /// <remarks>⚠ RULING FR5.6 — the guide is English-only, so upstream's
    /// "Translated by Your Name." sentence has nothing to say and resolves to
    /// nothing, exactly as it does upstream when a translator left it
    /// alone.</remarks>
    public string ManualTranslatedBy { get; set; } = string.Empty;

    /// <summary>Names a language in the interface's own language.</summary>
    /// <param name="code">The language code, e.g. <c>pt_BR</c>.</param>
    /// <returns>The name, or the code when nothing names it.</returns>
    /// <remarks>
    /// //was previously: the running framework's culture data
    /// (<c>CultureInfo.EnglishName</c>), which stood in while W-I18N was still
    /// owed. The ported table answers the way upstream's does, which for
    /// <c>pt_BR</c> is the one difference W12B's status file called out: the
    /// framework says "Portuguese (Brazil)" and the table says "Brazilian
    /// Portuguese".
    /// </remarks>
    public static string DefaultLanguageName(string code)
    {
        if (string.IsNullOrEmpty(code)) { return code; }

        return Services.LanguageNames.LanguageName(code, I18n.Language);
    }
}

/// <summary>
/// Resolves the <c>{variables}</c> in a page's text against its own
/// <c>#VARS</c> block and the application.
/// </summary>
/// <remarks>Upstream's <c>userguide.page.Resolver</c> with
/// <c>userguide/resolve.py</c>'s functions folded in: a name not in the
/// <c>#VARS</c> block is looked for among the functions, and a name in neither
/// is left on the page as it was written.</remarks>
public sealed class GuideResolver
{
    private readonly Dictionary<string, (string Type, string Text)> _variables
        = new Dictionary<string, (string, string)>(StringComparer.Ordinal);

    private readonly GuideContext _context;

    /// <summary>Creates a resolver over a page's <c>#VARS</c> lines.</summary>
    /// <param name="variables">The lines, each <c>name type content…</c>.</param>
    /// <param name="context">The application services, or null for none.</param>
    public GuideResolver(IEnumerable<string> variables, GuideContext context = null)
    {
        _context = context ?? new GuideContext();
        if (variables == null) { return; }

        foreach (string line in variables)
        {
            List<string> parts = SimpleMarkdown.SplitWhitespace(line, 2);
            //⚠ A line with fewer than three words is SKIPPED, not an error.
            if (parts.Count < 3) { continue; }

            _variables[parts[0]] = (parts[1], parts[2]);
        }
    }

    /// <summary>Replaces every <c>{variable}</c> in the text with its HTML.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text with the variables replaced.</returns>
    public string Format(string text)
        => GuideReader.VariablePattern.Replace(text ?? string.Empty, match =>
        {
            GuideValue value = Resolve(match.Groups[1].Value);
            return value == null ? match.Value : value.Html;
        });

    /// <summary>Finds the value of a named variable.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The value, or null when the name means nothing here.</returns>
    public GuideValue Resolve(string name)
    {
        if (_variables.TryGetValue(name, out (string Type, string Text) variable))
        {
            return Handle(variable.Type.ToLowerInvariant(), variable.Text);
        }

        //was previously: getattr(resolve, name)() — the module of functions
        //that answer for the names no page declares.
        switch (name)
        {
            case "appname":
                return new GuideValue("function", _context.AppName,
                    SimpleMarkdown.HtmlEscape(_context.AppName));
            case "version":
                return new GuideValue("function", _context.Version,
                    SimpleMarkdown.HtmlEscape(_context.Version));
            case "author":
                return new GuideValue("function", _context.Author,
                    SimpleMarkdown.HtmlEscape(_context.Author));
            case "manual_translated_by":
                return new GuideValue("function", _context.ManualTranslatedBy,
                    SimpleMarkdown.HtmlEscape(_context.ManualTranslatedBy));
            case "table_of_contents":
                string toc = _context.TableOfContents?.Invoke() ?? string.Empty;
                return new GuideValue("table_of_contents", string.Empty, toc);
            default:
                return null;
        }
    }

    /// <summary>Formats one variable of a known type.</summary>
    /// <param name="type">The type, lower-cased.</param>
    /// <param name="text">The content.</param>
    /// <returns>The value.</returns>
    /// <remarks>⚠ An UNKNOWN type falls to <c>text</c>, which is upstream's
    /// own behaviour and is why the one page that writes <c>URL</c> in
    /// capitals still works.</remarks>
    private GuideValue Handle(string type, string text)
    {
        switch (type)
        {
            case "md":
                return new GuideValue(type, text, SimpleMarkdown.HtmlInline(text));
            case "html":
                return new GuideValue(type, text, text);
            case "url":
                return new GuideValue(type, text, FormatUrl(text));
            case "help":
                return new GuideValue(type, text, FormatHelp(text));
            case "shortcut":
                //⚠ The VALUE of a shortcut variable is the key in force, not
                //the "collection action" the page wrote: the page names an
                //action, and what the reader has to see is what to press.
                string key = ShortcutFor(text);
                return new GuideValue(
                    type,
                    key,
                    $"<span class=\"shortcut\">{SimpleMarkdown.HtmlEscape(key)}</span>");
            case "menu":
                return new GuideValue(type, text, FormatMenu(text));
            case "image":
                return new GuideValue(type, text, FormatImage(text));
            case "languagename":
                string language = _context.LanguageName?.Invoke(text) ?? text;
                return new GuideValue(type, language,
                    SimpleMarkdown.HtmlEscape(language));
            default:
                return new GuideValue("text", text, SimpleMarkdown.HtmlEscape(text));
        }
    }

    /// <summary>The text a <c>url</c> variable shows, as upstream trims it.</summary>
    /// <param name="url">The URL.</param>
    /// <returns>The display text.</returns>
    public static string UrlText(string url)
    {
        string text = url ?? string.Empty;
        //⚠ Upstream trims "http://" and a trailing slash, and NOT "https://" —
        //ported as written, which is why the four https rows show their scheme.
        if (text.StartsWith("http://", StringComparison.Ordinal))
        {
            text = text.Substring(7);
        }

        if (text.EndsWith("/", StringComparison.Ordinal))
        {
            text = text.Substring(0, text.Length - 1);
        }

        return text;
    }

    /// <summary>The pieces of a <c>menu</c> variable, already translated.</summary>
    /// <param name="text">The variable's content, <c>a -&gt; b -&gt; c</c>.</param>
    /// <returns>The pieces.</returns>
    public IReadOnlyList<string> MenuPieces(string text)
    {
        List<string> pieces = new List<string>();
        foreach (string raw in Regex.Split(text ?? string.Empty, "->"))
        {
            pieces.Add(MenuTitle(raw.Trim()));
        }

        return pieces;
    }

    /// <summary>Translates one piece of a menu path.</summary>
    /// <param name="name">The piece, possibly a predefined menu name.</param>
    /// <returns>The title, with its accelerator marker and dots removed.</returns>
    public string MenuTitle(string name)
    {
        if (_context.MenuNames.TryGetValue(name, out string predefined))
        {
            name = predefined;
        }

        //⚠ A leading "!" means the piece is not an action or a menu, so its
        //accelerator marker is part of the text and stays.
        bool removeAccelerator = true;
        if (name.StartsWith("!", StringComparison.Ordinal))
        {
            removeAccelerator = false;
            name = name.Substring(1);
        }

        int bar = name.IndexOf('|');
        string translation = bar >= 0
            ? I18n.Get(name.Substring(0, bar), name.Substring(bar + 1))
            : I18n.Get(name);

        return removeAccelerator
            ? SimpleMarkdown.Strip(RemoveAccelerator(translation), ".")
            : translation;
    }

    /// <summary>Removes accelerator ampersands from an action's text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text without its markers.</returns>
    public static string RemoveAccelerator(string text)
        => (text ?? string.Empty)
            .Replace("&&", "\0").Replace("&", string.Empty).Replace("\0", "&");

    private string FormatHelp(string page)
    {
        string title = _context.PageTitle?.Invoke(page) ?? page;
        return $"<a href=\"{page}\">{title}</a>";
    }

    /// <summary>The key currently bound to the action a page names.</summary>
    /// <param name="text">The variable's content, <c>collection action</c>.</param>
    /// <returns>The key, or upstream's own "(no key defined)".</returns>
    private string ShortcutFor(string text)
    {
        List<string> parts = SimpleMarkdown.SplitWhitespace(text, 1);
        string key = parts.Count > 1
            ? _context.Shortcut?.Invoke(parts[0], parts[1])
            : null;
        return string.IsNullOrEmpty(key) ? I18n.Get("(no key defined)") : key;
    }

    private static string FormatUrl(string text)
    {
        string url = SimpleMarkdown.HtmlEscape(text).Replace("\"", "&quot;");
        return $"<a href=\"{url}\">{SimpleMarkdown.HtmlEscape(UrlText(text))}</a>";
    }

    private string FormatMenu(string text)
    {
        StringBuilder builder = new StringBuilder("<em>");
        IReadOnlyList<string> pieces = MenuPieces(text);
        for (int index = 0; index < pieces.Count; index++)
        {
            if (index > 0) { builder.Append(" &#8594; "); }

            builder.Append(pieces[index]);
        }

        return builder.Append("</em>").ToString();
    }

    private static string FormatImage(string filename)
    {
        string url = SimpleMarkdown.HtmlEscape(filename).Replace("\"", "&quot;");
        return $"<img src=\"{url}\" alt=\"{url}\"/>";
    }
}
