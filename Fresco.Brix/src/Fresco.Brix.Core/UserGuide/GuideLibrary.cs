// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Fresco.Brix.UserGuide; //was previously: frescobaldi/userguide/util.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The user guide as a whole: where its pages live, what each one is called,
/// and how they are threaded together.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>userguide.util.Cache</c> and the parts of its
/// <c>Formatter</c> that work out a page's navigation, with the module-level
/// entry points of <c>userguide/__init__.py</c> folded in. The pages are
/// FILES, exactly as upstream keeps them — Frescobaldi's own GPL text, shipped
/// as assets under <c>assets/userguide</c>, never as string literals in a
/// source file.
/// </para>
/// <para>
/// The folder is droppable: with it emptied, the guide says so and the
/// application runs on.
/// </para>
/// </remarks>
public sealed class GuideLibrary
{
    /// <summary>The assets sub-folder the pages live in.</summary>
    public const string AssetsFolderName = "userguide";

    /// <summary>The page every path starts from.</summary>
    public const string IndexPage = "index";

    /// <summary>The page the table of contents is on.</summary>
    public const string ContentsPage = "toc";

    /// <summary>The page a missing resource falls back to.</summary>
    public const string NotFoundPage = "404";

    private readonly Dictionary<string, GuidePage> _pages
        = new Dictionary<string, GuidePage>(StringComparer.Ordinal);

    private readonly IGuidePageStore _store;
    private Dictionary<string, List<string>> _parents;

    /// <summary>Creates a library over the application's own assets folder.</summary>
    public GuideLibrary() : this(new GuideFolder(DefaultDirectory)) { }

    /// <summary>Creates a library over a folder of pages.</summary>
    /// <param name="directory">The folder holding the <c>.md</c> files.</param>
    public GuideLibrary(string directory) : this(new GuideFolder(directory)) { }

    /// <summary>Creates a library over any store of pages.</summary>
    /// <param name="store">Where the pages come from.</param>
    public GuideLibrary(IGuidePageStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        Context = new GuideContext
        {
            PageTitle = Title,
            TableOfContents = TableOfContents,
        };
    }

    /// <summary>Gets where the pages come from.</summary>
    public IGuidePageStore Store => _store;

    /// <summary>Gets the services a page's variables resolve against.</summary>
    public GuideContext Context { get; }

    /// <summary>Gets the folder the shipped pages live in.</summary>
    public static string DefaultDirectory
        => Path.Combine(AppContext.BaseDirectory, "assets", AssetsFolderName);

    /// <summary>Gets whether a page is there to be read.</summary>
    /// <param name="name">The page name.</param>
    /// <returns>Whether it exists.</returns>
    public bool Exists(string name) => _store.Read(name) != null;

    /// <summary>Gets every page name the store holds, sorted.</summary>
    /// <returns>The names.</returns>
    public IReadOnlyList<string> Names() => _store.Names();

    /// <summary>Reads a page's raw text, or the 404 page's when it is not there.</summary>
    /// <param name="name">The page name.</param>
    /// <returns>The text and whether the page was missing.</returns>
    internal (string Text, bool Missing) ReadPage(string name)
    {
        string text = _store.Read(name);
        if (text != null) { return (text, false); }

        //Even 404 can be gone: the folder is droppable. Say so where a reader
        //will see it rather than failing.
        return (_store.Read(NotFoundPage)
            ?? "=== Not Found ===\n\nCannot load the requested userguide "
                + "resource `" + name + "`.\n",
            true);
    }

    /// <summary>Gets a page, reading it the first time it is asked for.</summary>
    /// <param name="name">The page name.</param>
    /// <returns>The page; a missing one is the 404 page.</returns>
    public GuidePage Page(string name)
    {
        name ??= IndexPage;
        if (!_pages.TryGetValue(name, out GuidePage page))
        {
            page = new GuidePage(this, name);
            _pages[name] = page;
        }

        return page;
    }

    /// <summary>Forgets every page that was read.</summary>
    /// <remarks>Upstream clears the title cache when the language changes; the
    /// guide is English-only (FR5.6), so what this is for is a page folder that
    /// changed under a running application.</remarks>
    public void Clear()
    {
        _pages.Clear();
        _parents = null;
    }

    /// <summary>Gets a page's title.</summary>
    /// <param name="name">The page name.</param>
    /// <returns>The title.</returns>
    public string Title(string name) => Page(name).Title;

    /// <summary>Gets a page's child pages.</summary>
    /// <param name="name">The page name.</param>
    /// <returns>The child page names.</returns>
    public IReadOnlyList<string> Children(string name) => Page(name).Children;

    /// <summary>Gets a page's parents — the pages that list it as a child.</summary>
    /// <param name="name">The page name.</param>
    /// <returns>The parent page names, which may be none.</returns>
    public IReadOnlyList<string> Parents(string name)
    {
        _parents ??= ComputeParents();
        return _parents.TryGetValue(name, out List<string> parents)
            ? parents
            : Array.Empty<string>();
    }

    /// <summary>Works out what a page shows around its body.</summary>
    /// <param name="name">The page name.</param>
    /// <returns>The navigation.</returns>
    public GuideNavigation Navigation(string name)
    {
        GuidePage page = Page(name);
        GuideNavigation navigation = new GuideNavigation();

        IReadOnlyList<string> parents = string.Equals(name, IndexPage, StringComparison.Ordinal)
            ? Array.Empty<string>()
            : Parents(name);

        if (parents.Count > 0 && !page.IsPopup)
        {
            //The chain up to the top, following the FIRST parent each time.
            List<string> links = new List<string>();
            IReadOnlyList<string> walk = parents;
            while (walk.Count > 0)
            {
                string parent = walk[0];
                links.Add(parent);
                walk = Parents(parent);
            }

            links.Reverse();
            navigation.Up = links;
        }

        navigation.Children = page.Children;
        if (navigation.Children.Count == 0)
        {
            //No children, so offer the next page instead — the next sibling
            //under each parent, or the next chapter when this was the last.
            List<(string Kind, string Page)> next = new List<(string, string)>();
            foreach (string parent in parents)
            {
                string sibling = Sibling(parent, name);
                if (sibling != null)
                {
                    next.Add(("next", sibling));
                    continue;
                }

                Queue<(string Grandparent, string Parent)> pending
                    = new Queue<(string, string)>();
                foreach (string grandparent in Parents(parent))
                {
                    pending.Enqueue((grandparent, parent));
                }

                while (pending.Count > 0)
                {
                    (string grandparent, string child) = pending.Dequeue();
                    string chapter = Sibling(grandparent, child);
                    if (chapter != null)
                    {
                        next.Add(("chapter", chapter));
                        continue;
                    }

                    foreach (string above in Parents(grandparent))
                    {
                        pending.Enqueue((above, grandparent));
                    }
                }
            }

            navigation.Next = next;
        }

        navigation.SeeAlso = page.SeeAlso;
        return navigation;
    }

    /// <summary>Builds the table of contents, as HTML.</summary>
    /// <returns>The HTML.</returns>
    /// <remarks>Upstream's <c>resolve.table_of_contents()</c>. It is HTML
    /// because that is the shape the variable is declared in; the viewer
    /// builds its own from <see cref="ContentsTree"/>.</remarks>
    public string TableOfContents()
    {
        StringBuilder html = new StringBuilder("<ul>");
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

        void AddPage(string page)
        {
            if (!seen.Add(page)) { return; }

            html.Append("<li>").Append(FormatLink(page)).Append("</li>\n");
            IReadOnlyList<string> children = Children(page);
            if (children.Count > 0)
            {
                html.Append("<ul>");
                foreach (string child in children) { AddPage(child); }

                html.Append("</ul>\n");
            }
        }

        foreach (string page in Children(IndexPage)) { AddPage(page); }

        return html.Append("</ul>\n").ToString();
    }

    /// <summary>Builds the table of contents as a tree of page names.</summary>
    /// <returns>The entries, each with its depth.</returns>
    /// <remarks>The same walk <see cref="TableOfContents"/> makes, handed back
    /// as data so the viewer can draw it with real links.</remarks>
    public IReadOnlyList<(int Depth, string Page)> ContentsTree()
    {
        List<(int Depth, string Page)> entries = new List<(int, string)>();
        HashSet<string> seen = new HashSet<string>(StringComparer.Ordinal);

        void AddPage(string page, int depth)
        {
            if (!seen.Add(page)) { return; }

            entries.Add((depth, page));
            foreach (string child in Children(page)) { AddPage(child, depth + 1); }
        }

        foreach (string page in Children(IndexPage)) { AddPage(page, 0); }

        return entries;
    }

    /// <summary>Makes a clickable link to a page.</summary>
    /// <param name="name">The page name.</param>
    /// <returns>The HTML link.</returns>
    public string FormatLink(string name)
        => $"<a href=\"{name}\">{SimpleMarkdown.HtmlEscape(Title(name))}</a>";

    private string Sibling(string parent, string name)
    {
        IReadOnlyList<string> children = Children(parent);
        for (int index = 0; index < children.Count; index++)
        {
            if (string.Equals(children[index], name, StringComparison.Ordinal))
            {
                return index < children.Count - 1 ? children[index + 1] : null;
            }
        }

        return null;
    }

    private Dictionary<string, List<string>> ComputeParents()
    {
        Dictionary<string, List<string>> parents
            = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        HashSet<string> path = new HashSet<string>(StringComparer.Ordinal);

        void Compute(string page)
        {
            //A page reached twice by different parents records BOTH, the way
            //upstream does; the guard is only against a page that is its own
            //ancestor, which upstream would recurse into for ever.
            if (!path.Add(page)) { return; }

            foreach (string child in Children(page))
            {
                if (!parents.TryGetValue(child, out List<string> list))
                {
                    list = new List<string>();
                    parents[child] = list;
                }

                list.Add(page);
                Compute(child);
            }

            path.Remove(page);
        }

        Compute(IndexPage);
        return parents;
    }
}

/// <summary>Where a user guide's pages come from.</summary>
/// <remarks>Upstream reads them straight out of its own package directory.
/// The seam is here so a parity test can hand the library the very bytes
/// Frescobaldi's pages were recorded from, and so a future store — a resource,
/// a zip — needs no change anywhere else.</remarks>
public interface IGuidePageStore
{
    /// <summary>Reads a page's text.</summary>
    /// <param name="name">The page name, without its extension.</param>
    /// <returns>The text, or null when there is no such page.</returns>
    string Read(string name);

    /// <summary>Lists every page the store holds, sorted.</summary>
    /// <returns>The page names.</returns>
    IReadOnlyList<string> Names();

    /// <summary>Gets the full path of a page's companion file.</summary>
    /// <param name="fileName">The file name, e.g. an image.</param>
    /// <returns>The path, or null when the store is not on disk.</returns>
    string PathOf(string fileName);
}

/// <summary>A folder of <c>.md</c> files, which is how the guide ships.</summary>
public sealed class GuideFolder : IGuidePageStore
{
    private readonly string _directory;

    /// <summary>Creates a store over a folder.</summary>
    /// <param name="directory">The folder.</param>
    public GuideFolder(string directory) => _directory = directory;

    /// <summary>Gets the folder.</summary>
    public string Directory => _directory;

    /// <inheritdoc/>
    public string Read(string name)
    {
        string path = PathOf(
            name.EndsWith(".md", StringComparison.Ordinal) ? name : name + ".md");
        try
        {
            return File.ReadAllText(path, Encoding.UTF8);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            //A page name that is not a legal file name reaches here from a
            //link in a page; it is a missing page, not a crash.
            return null;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> Names()
    {
        List<string> names = new List<string>();
        if (!System.IO.Directory.Exists(_directory)) { return names; }

        foreach (string path in System.IO.Directory.EnumerateFiles(_directory, "*.md"))
        {
            names.Add(Path.GetFileNameWithoutExtension(path));
        }

        names.Sort(StringComparer.Ordinal);
        return names;
    }

    /// <inheritdoc/>
    public string PathOf(string fileName) => Path.Combine(_directory, fileName);
}

/// <summary>What a page shows around its body: where it sits in the guide.</summary>
public sealed class GuideNavigation
{
    /// <summary>Gets or sets the chain of pages above this one, top first.</summary>
    public IReadOnlyList<string> Up { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets the pages in this chapter.</summary>
    public IReadOnlyList<string> Children { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Gets or sets what comes next: <c>next</c> for the next page in this
    /// chapter, <c>chapter</c> for the next chapter.
    /// </summary>
    public IReadOnlyList<(string Kind, string Page)> Next { get; set; }
        = Array.Empty<(string, string)>();

    /// <summary>Gets or sets the pages worth reading beside this one.</summary>
    public IReadOnlyList<string> SeeAlso { get; set; } = Array.Empty<string>();
}
