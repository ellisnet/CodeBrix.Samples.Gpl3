// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;

namespace Fresco.Brix.UserGuide; //was previously: frescobaldi/userguide/page.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One page of the user guide: its parse tree, its title, its child and
/// "see also" pages, and its variables.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>userguide.page.Page</c>. It is named
/// <c>GuidePage</c> here rather than <c>Page</c> because
/// <c>Microsoft.UI.Xaml.Controls.Page</c> is a type this application's XAML
/// uses unqualified (board traps 19 and 56).
/// </para>
/// <para>
/// A page that cannot be read falls back to <c>404</c>, whose own text names
/// the page that was asked for — which is how a link to a feature that has not
/// arrived yet (the five File &gt; Import pages, which come with W-IMPORT)
/// lands somewhere sensible instead of failing.
/// </para>
/// </remarks>
public sealed class GuidePage
{
    private readonly GuideLibrary _library;
    private Dictionary<string, List<string>> _blocks;
    private MarkdownTree _tree;
    private string _title;
    private string _body;

    /// <summary>Creates a page and loads it.</summary>
    /// <param name="library">The library the page belongs to.</param>
    /// <param name="name">The page name, without its extension.</param>
    public GuidePage(GuideLibrary library, string name)
    {
        _library = library ?? throw new ArgumentNullException(nameof(library));
        Load(name);
    }

    /// <summary>Gets the page's name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets whether the page fell back to <c>404</c>.</summary>
    public bool IsMissing { get; private set; }

    /// <summary>Gets the parse tree.</summary>
    public MarkdownTree Tree => _tree;

    /// <summary>Gets the page's <c>#</c>-named blocks.</summary>
    public IReadOnlyDictionary<string, List<string>> Blocks => _blocks;

    /// <summary>Gets the page's title, or "No Title" when it has none.</summary>
    public string Title
    {
        get
        {
            if (_title == null)
            {
                _title = "No Title";
                foreach (MarkdownTree.Node heading in _tree.Find("heading"))
                {
                    _title = _tree.Text(heading);
                    break;
                }
            }

            return _title;
        }
    }

    /// <summary>Gets whether the page asks to be shown as a popup.</summary>
    /// <remarks>Upstream's <c>#PROPERTIES popup</c>. No shipped page declares
    /// it; the block is read all the same, so one could.</remarks>
    public bool IsPopup
        => _blocks.TryGetValue("PROPERTIES", out List<string> properties)
            && properties.Contains("popup");

    /// <summary>Gets the names of the page's child pages.</summary>
    public IReadOnlyList<string> Children
        => _blocks.TryGetValue("SUBDOCS", out List<string> children)
            ? children
            : Array.Empty<string>();

    /// <summary>Gets the names of the page's "see also" pages.</summary>
    public IReadOnlyList<string> SeeAlso
        => _blocks.TryGetValue("SEEALSO", out List<string> seealso)
            ? seealso
            : Array.Empty<string>();

    /// <summary>Gets the page's <c>#VARS</c> lines.</summary>
    public IReadOnlyList<string> Variables
        => _blocks.TryGetValue("VARS", out List<string> variables)
            ? variables
            : Array.Empty<string>();

    /// <summary>Gets a resolver over this page's variables.</summary>
    /// <returns>The resolver.</returns>
    public GuideResolver Resolver() => new GuideResolver(Variables, _library.Context);

    /// <summary>Gets the page's body as HTML.</summary>
    /// <remarks>⚠ NOTHING IN THE APPLICATION SHOWS THIS. There is no web view
    /// anywhere (FR8) — <see cref="GuideRenderer"/> draws the page from
    /// <see cref="Tree"/>. The HTML is upstream's own contract and is what the
    /// parity fixtures compare against.</remarks>
    /// <returns>The HTML.</returns>
    public string Body()
    {
        if (_body == null)
        {
            GuidePageHtmlOutput output = new GuidePageHtmlOutput(Resolver());
            _tree.Copy(output);
            //Empty paragraphs can fall out of a paragraph that was nothing but
            //optional text; upstream takes them out again here.
            _body = output.Html().Replace("<p></p>", string.Empty);
        }

        return _body;
    }

    private void Load(string name)
    {
        Name = name;
        (string text, bool missing) = _library.ReadPage(name);
        IsMissing = missing;

        (string document, Dictionary<string, List<string>> blocks)
            = GuideReader.SplitDocument(text);
        _blocks = blocks;

        //Upstream appends this to the VARS block of EVERY page, so 404 can name
        //the page that was asked for.
        if (!_blocks.TryGetValue("VARS", out List<string> variables))
        {
            variables = new List<string>();
            _blocks["VARS"] = variables;
        }

        variables.Add("userguide_page md `" + name + "`");

        _tree = new MarkdownTree();
        new GuideParser().Parse(document, _tree);
    }
}

/// <summary>
/// The HTML output a page's body is written through: it replaces the
/// <c>{variables}</c> as the text goes past.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>userguide.page.HtmlOutput</c>. Its <c>heading_offset</c> of 1
/// is why a page's own <c>=== title ===</c> comes out as an <c>h2</c>.
/// </para>
/// <para>
/// //was previously: a <c>```lilypond</c> block was colorized here through
/// <c>highlight2html</c>, which reads the editor's own colour scheme. Colours
/// belong to a running window and this HTML is never shown in one; the guide's
/// viewer colorizes the block itself when it DRAWS it
/// (<see cref="GuideRenderer"/>), from the same tokenizer, and the HTML kept
/// here is the plain <c>&lt;code&gt;&lt;pre&gt;</c> form.
/// </para>
/// </remarks>
internal sealed class GuidePageHtmlOutput : MarkdownHtmlOutput
{
    private readonly GuideResolver _resolver;

    internal GuidePageHtmlOutput(GuideResolver resolver)
    {
        _resolver = resolver;
        HeadingOffset = 1;
    }

    protected override void InlineTextStart(string text)
        => Raw(_resolver.Format(HtmlEscape(text)));
}
