// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Ly;
using Fresco.Brix.Ly.Colorizing;
using Fresco.Brix.Services;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Windows.UI;

namespace Fresco.Brix.UserGuide;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Draws a parsed user-guide page with the platform's own controls.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ RULING FR8 — THERE IS NO WEB VIEW IN THIS APPLICATION. Upstream renders
/// the guide by handing a <c>QTextBrowser</c> the HTML its
/// <c>userguide.util.Formatter</c> writes. Here the same parse TREE is walked
/// into text blocks, panels and links, which is the same thing the Music View,
/// the log and the Documentation Browser all do: the platform draws, and no
/// browser engine is anywhere near it.
/// </para>
/// <para>
/// //was previously: <c>userguide/browser.py</c>'s <c>Browser.loadResource</c>
/// and <c>util.Formatter.html</c>, whose whole job was to produce a string of
/// HTML for a widget that parses HTML.
/// </para>
/// </remarks>
public sealed class GuideRenderer
{
    private readonly GuideLibrary _library;

    /// <summary>Creates a renderer over a library of pages.</summary>
    /// <param name="library">The library.</param>
    public GuideRenderer(GuideLibrary library)
        => _library = library ?? throw new ArgumentNullException(nameof(library));

    /// <summary>Gets or sets what a link to another page does.</summary>
    public Action<string> Navigate { get; set; }

    /// <summary>Gets or sets what a link to the outside world does.</summary>
    public Action<string> OpenExternal { get; set; }

    /// <summary>
    /// Gets or sets the colours a <c>```lilypond</c> block is drawn in.
    /// </summary>
    /// <remarks>Upstream colorizes the block through <c>highlight2html</c>
    /// against the editor's own Fonts &amp; Colors scheme; handing that scheme
    /// in here is the same thing, one step earlier.</remarks>
    public CssScheme CodeScheme { get; set; } = CssScheme.Default;

    /// <summary>Gets or sets the font a code block is drawn in.</summary>
    public FontFamily CodeFont { get; set; }

    /// <summary>Draws a whole page.</summary>
    /// <param name="page">The page.</param>
    /// <param name="withNavigation">Whether to draw the links around the body:
    /// where the page sits, what is in this chapter, what comes next and what
    /// to see also.</param>
    /// <returns>The panel to put in front of the reader.</returns>
    /// <remarks>Upstream splits this in two: <c>Page.body()</c> is the body
    /// alone, which is what the About window shows, and
    /// <c>util.Formatter.html()</c> wraps the navigation round it for the help
    /// browser. The split is the <paramref name="withNavigation"/> argument
    /// here, because both come off the same tree.</remarks>
    public UIElement Render(GuidePage page, bool withNavigation = true)
    {
        if (page == null) { throw new ArgumentNullException(nameof(page)); }

        StackPanel panel = new StackPanel { Spacing = 8 };
        GuideResolver resolver = page.Resolver();
        GuideNavigation navigation = withNavigation
            ? _library.Navigation(page.Name)
            : new GuideNavigation();

        if (navigation.Up.Count > 0)
        {
            panel.Children.Add(LinkRow(I18n.Get("Up:"), navigation.Up, " → "));
        }

        RenderBlocks(page.Tree.Root.Children, panel, resolver);

        if (navigation.Children.Count > 0)
        {
            panel.Children.Add(Heading(I18n.Get("In this chapter:"), 3));
            StackPanel list = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
            foreach (string child in navigation.Children)
            {
                list.Children.Add(BulletedLink(child));
            }

            panel.Children.Add(list);
        }

        foreach ((string kind, string target) in navigation.Next)
        {
            panel.Children.Add(LinkRow(
                kind == "chapter" ? I18n.Get("Next Chapter:") : I18n.Get("Next:"),
                new[] { target },
                ", "));
        }

        if (navigation.SeeAlso.Count > 0)
        {
            panel.Children.Add(LinkRow(I18n.Get("See also:"), navigation.SeeAlso, ", "));
        }

        return panel;
    }

    /// <summary>Draws the table of contents as a tree of links.</summary>
    /// <returns>The panel.</returns>
    public UIElement RenderContents()
    {
        StackPanel panel = new StackPanel();
        foreach ((int depth, string page) in _library.ContentsTree())
        {
            TextBlock line = BulletedLink(page);
            line.Margin = new Thickness(16 * (depth + 1), 0, 0, 0);
            panel.Children.Add(line);
        }

        return panel;
    }

    private void RenderBlocks(
        IReadOnlyList<MarkdownTree.Node> nodes, Panel into, GuideResolver resolver)
    {
        foreach (MarkdownTree.Node node in nodes)
        {
            switch (node.Name)
            {
                case "heading":
                    TextBlock heading = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        FontWeight = FontWeights.SemiBold,
                        FontSize = HeadingSize(node),
                        Margin = new Thickness(0, 6, 0, 0),
                        IsTextSelectionEnabled = true,
                    };
                    FillInlines(heading, node, resolver, into);
                    into.Children.Add(heading);
                    break;

                case "paragraph":
                case "inline":
                    TextBlock paragraph = new TextBlock
                    {
                        TextWrapping = TextWrapping.Wrap,
                        IsTextSelectionEnabled = true,
                    };
                    FillInlines(
                        paragraph,
                        node.Name == "inline" ? Wrap(node) : node,
                        resolver,
                        into);
                    if (paragraph.Inlines.Count > 0) { into.Children.Add(paragraph); }

                    break;

                case "code":
                    into.Children.Add(CodeBlock(node));
                    break;

                case "unorderedlist":
                case "orderedlist":
                    into.Children.Add(ListBlock(node, resolver));
                    break;

                case "definitionlist":
                    into.Children.Add(DefinitionBlock(node, resolver));
                    break;

                default:
                    //Anything else (a stray list item at the top level, which a
                    //page's own indentation can produce) is drawn as its
                    //contents rather than dropped.
                    RenderBlocks(node.Children, into, resolver);
                    break;
            }
        }
    }

    private UIElement ListBlock(MarkdownTree.Node node, GuideResolver resolver)
    {
        StackPanel list = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
        bool ordered = node.Name == "orderedlist";
        int number = 1;
        foreach (MarkdownTree.Node item in node.Children)
        {
            if (item.Name != "unorderedlist_item" && item.Name != "orderedlist_item")
            {
                //A nested list, or the paragraph an over-indented block leaves
                //directly inside its list (which the parser does produce).
                StackPanel nested = new StackPanel();
                RenderBlocks(new[] { item }, nested, resolver);
                list.Children.Add(nested);
                continue;
            }

            Grid row = new Grid { ColumnSpacing = 6 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star),
            });

            TextBlock bullet = new TextBlock
            {
                Text = ordered
                    ? number.ToString(CultureInfo.CurrentCulture) + "."
                    : "•",
                VerticalAlignment = VerticalAlignment.Top,
            };
            row.Children.Add(bullet);

            StackPanel content = new StackPanel();
            RenderBlocks(item.Children, content, resolver);
            Grid.SetColumn(content, 1);
            row.Children.Add(content);

            list.Children.Add(row);
            number++;
        }

        return list;
    }

    private UIElement DefinitionBlock(MarkdownTree.Node node, GuideResolver resolver)
    {
        StackPanel list = new StackPanel { Spacing = 2 };
        foreach (MarkdownTree.Node item in node.Children)
        {
            if (item.Name != "definitionlist_item")
            {
                //⚠ A paragraph can land directly inside a definition list —
                //upstream's own `paragraph_start' writes a <dd> for exactly
                //that case. It is drawn as the definition it is.
                StackPanel loose = new StackPanel
                {
                    Margin = new Thickness(24, 0, 0, 0),
                };
                RenderBlocks(new[] { item }, loose, resolver);
                list.Children.Add(loose);
                continue;
            }

            foreach (MarkdownTree.Node part in item.Children)
            {
                StackPanel holder = new StackPanel
                {
                    Margin = part.Name == "definitionlist_item_definition"
                        ? new Thickness(24, 0, 0, 6)
                        : new Thickness(0, 4, 0, 0),
                };
                RenderBlocks(part.Children, holder, resolver);

                if (part.Name == "definitionlist_item_term")
                {
                    foreach (UIElement child in holder.Children)
                    {
                        if (child is TextBlock block)
                        {
                            block.FontWeight = FontWeights.SemiBold;
                        }
                    }
                }

                list.Children.Add(holder);
            }
        }

        return list;
    }

    private UIElement CodeBlock(MarkdownTree.Node node)
    {
        string code = node.Arguments.Count > 0 ? node.Arguments[0] as string : string.Empty;
        string specifier = node.Arguments.Count > 1 ? node.Arguments[1] as string : null;

        TextBlock block = new TextBlock
        {
            FontFamily = CodeFont ?? new FontFamily("Roboto Mono"),
            TextWrapping = TextWrapping.NoWrap,
            IsTextSelectionEnabled = true,
        };

        if (string.Equals(specifier, "lilypond", StringComparison.Ordinal))
        {
            ColorizeCode(block, code);
        }
        else
        {
            block.Inlines.Add(new Run { Text = code });
        }

        return new Border
        {
            Padding = new Thickness(8),
            Margin = new Thickness(8, 2, 0, 2),
            CornerRadius = new CornerRadius(3),
            Background = new SolidColorBrush(Color.FromArgb(24, 128, 128, 128)),
            Child = new ScrollViewer
            {
                Content = block,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Auto,
            },
        };
    }

    /// <summary>
    /// Colours a LilyPond block with the same mapping the editor highlights by.
    /// </summary>
    /// <param name="block">Where the runs go.</param>
    /// <param name="code">The code.</param>
    /// <remarks>Upstream reaches the same place through
    /// <c>highlight2html.html_text</c>, which writes styled HTML spans; this
    /// writes styled runs, from the same <c>ly.colorize</c> mapping and the
    /// same colour scheme.</remarks>
    private void ColorizeCode(TextBlock block, string code)
    {
        IEnumerable<StyledText> runs;
        try
        {
            Document document = new Document(code, "lilypond");
            runs = HtmlColorize.MeltMappedTokens(
                HtmlColorize.MapTokens(new Cursor(document), CssMapper));
        }
        catch (Exception)
        {
            //A block that is not valid LilyPond is still a code block; the
            //guide is not the place to report it.
            block.Inlines.Add(new Run { Text = code });
            return;
        }

        foreach (StyledText run in runs)
        {
            Run piece = new Run { Text = run.Text };
            IDictionary<string, string> properties = run.Style != null
                ? Fresco.Brix.Ly.Colorizing.Colorize.CssDict(run.Style, CodeScheme)
                : null;
            if (properties != null)
            {
                if (properties.TryGetValue("color", out string color)
                    && TryParseColor(color, out Color parsed))
                {
                    piece.Foreground = new SolidColorBrush(parsed);
                }

                if (properties.TryGetValue("font-weight", out string weight)
                    && weight == "bold")
                {
                    piece.FontWeight = FontWeights.Bold;
                }

                if (properties.TryGetValue("font-style", out string style)
                    && style == "italic")
                {
                    piece.FontStyle = Windows.UI.Text.FontStyle.Italic;
                }
            }

            block.Inlines.Add(piece);
        }
    }

    /// <summary>The token-to-style mapping, built once.</summary>
    /// <remarks>The SAME mapping the editor's own highlighting reads, which is
    /// what makes a code block in the guide look like the editor.</remarks>
    private static TokenMapper<CssClass> CssMapper
        => field ??= Fresco.Brix.Ly.Colorizing.Colorize.CssMapper();

    private void FillInlines(
        TextBlock block, MarkdownTree.Node node, GuideResolver resolver, Panel blocks)
    {
        foreach (MarkdownTree.Node child in node.Children)
        {
            AddInline(block, child, resolver, blocks, new InlineStyle());
        }
    }

    private void AddInline(
        TextBlock block,
        MarkdownTree.Node node,
        GuideResolver resolver,
        Panel blocks,
        InlineStyle style)
    {
        switch (node.Name)
        {
            case "inline":
                foreach (MarkdownTree.Node child in node.Children)
                {
                    AddInline(block, child, resolver, blocks, style);
                }

                break;

            case "inline_emphasis":
                foreach (MarkdownTree.Node child in node.Children)
                {
                    AddInline(block, child, resolver, blocks, style.WithItalic());
                }

                break;

            case "inline_code":
                foreach (MarkdownTree.Node child in node.Children)
                {
                    AddInline(block, child, resolver, blocks, style.WithCode());
                }

                break;

            case "link":
                string url = node.Arguments.Count > 0
                    ? node.Arguments[0] as string
                    : string.Empty;
                StringBuilder caption = new StringBuilder();
                Text(node, caption);
                AddLink(block, url, caption.ToString());
                break;

            case "inline_text":
                AddText(
                    block,
                    node.Arguments.Count > 0 ? node.Arguments[0] as string : string.Empty,
                    resolver,
                    blocks,
                    style);
                break;

            default:
                foreach (MarkdownTree.Node child in node.Children)
                {
                    AddInline(block, child, resolver, blocks, style);
                }

                break;
        }
    }

    /// <summary>
    /// Adds a run of text, replacing every <c>{variable}</c> as it goes.
    /// </summary>
    /// <param name="block">Where the runs go.</param>
    /// <param name="text">The text.</param>
    /// <param name="resolver">The page's variables.</param>
    /// <param name="blocks">Where a BLOCK-shaped variable goes instead.</param>
    /// <param name="style">The style in force.</param>
    private void AddText(
        TextBlock block,
        string text,
        GuideResolver resolver,
        Panel blocks,
        InlineStyle style)
    {
        int position = 0;
        foreach (System.Text.RegularExpressions.Match match
            in GuideReader.VariablePattern.Matches(text ?? string.Empty))
        {
            if (match.Index > position)
            {
                AddRun(block, text.Substring(position, match.Index - position), style);
            }

            position = match.Index + match.Length;
            GuideValue value = resolver.Resolve(match.Groups[1].Value);
            if (value == null)
            {
                //A name that means nothing stays on the page as it was
                //written, which is upstream's own answer and shows an author
                //their typo.
                AddRun(block, match.Value, style);
                continue;
            }

            AddValue(block, value, resolver, blocks, style);
        }

        if (position < (text?.Length ?? 0))
        {
            AddRun(block, text.Substring(position), style);
        }
    }

    private void AddValue(
        TextBlock block,
        GuideValue value,
        GuideResolver resolver,
        Panel blocks,
        InlineStyle style)
    {
        switch (value.Kind)
        {
            case "help":
                AddLink(block, value.Text, _library.Title(value.Text));
                break;

            case "url":
                AddLink(block, value.Text, GuideResolver.UrlText(value.Text));
                break;

            case "menu":
                AddRun(
                    block,
                    string.Join(" → ", resolver.MenuPieces(value.Text)),
                    style.WithItalic());
                break;

            case "shortcut":
                AddRun(block, value.Text, style.WithCode());
                break;

            case "md":
                //Inline markdown, which is a document of its own.
                MarkdownTree tree = SimpleMarkdown.Tree(value.Text);
                foreach (MarkdownTree.Node child in tree.Root.Children)
                {
                    AddInline(block, child, resolver, blocks, style);
                }

                break;

            case "html":
                //⚠ The one `html' variable in the corpus is a character
                //ENTITY, not markup. Decoding the entities is what it is for;
                //tags, if one ever appeared, would show as themselves rather
                //than be interpreted, because nothing here parses HTML (FR8).
                AddRun(block, DecodeEntities(value.Text), style);
                break;

            case "image":
                AddImage(blocks, value.Text);
                break;

            case "table_of_contents":
                blocks?.Children.Add(RenderContents());
                break;

            default:
                AddRun(block, value.Text, style);
                break;
        }
    }

    private void AddImage(Panel blocks, string fileName)
    {
        if (blocks == null) { return; }

        string path = _library.Store.PathOf(fileName);
        if (path == null || !System.IO.File.Exists(path)) { return; }

        blocks.Children.Add(new Image
        {
            Source = new BitmapImage(new Uri("file://" + path)),
            Stretch = Stretch.None,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(8, 4, 0, 4),
        });
    }

    private void AddLink(TextBlock block, string url, string caption)
    {
        Hyperlink link = new Hyperlink();
        link.Inlines.Add(new Run { Text = caption });
        bool external = url.Contains("://", StringComparison.Ordinal)
            || url.StartsWith("mailto:", StringComparison.Ordinal);
        if (external)
        {
            //Upstream marks an external link with a small arrow
            //(`util.markexternal'); the same mark, drawn rather than escaped.
            link.Inlines.Add(new Run { Text = "⬈" });
            link.Click += (_, _) => OpenExternal?.Invoke(url);
        }
        else
        {
            link.Click += (_, _) => Navigate?.Invoke(url);
        }

        block.Inlines.Add(link);
    }

    private static void AddRun(TextBlock block, string text, InlineStyle style)
    {
        if (string.IsNullOrEmpty(text)) { return; }

        Run run = new Run { Text = text };
        if (style.Italic) { run.FontStyle = Windows.UI.Text.FontStyle.Italic; }

        if (style.Code) { run.FontFamily = new FontFamily("Roboto Mono"); }

        block.Inlines.Add(run);
    }

    private TextBlock LinkRow(string label, IReadOnlyList<string> pages, string separator)
    {
        TextBlock row = new TextBlock { TextWrapping = TextWrapping.Wrap };
        row.Inlines.Add(new Run { Text = label + " " });
        for (int index = 0; index < pages.Count; index++)
        {
            if (index > 0) { row.Inlines.Add(new Run { Text = separator }); }

            AddLink(row, pages[index], _library.Title(pages[index]));
        }

        return row;
    }

    private TextBlock BulletedLink(string page)
    {
        TextBlock line = new TextBlock { TextWrapping = TextWrapping.Wrap };
        line.Inlines.Add(new Run { Text = "• " });
        AddLink(line, page, _library.Title(page));
        return line;
    }

    private static TextBlock Heading(string text, int level)
        => new TextBlock
        {
            Text = text,
            FontWeight = FontWeights.SemiBold,
            FontSize = SizeFor(level),
            Margin = new Thickness(0, 8, 0, 0),
            TextWrapping = TextWrapping.Wrap,
        };

    private static double HeadingSize(MarkdownTree.Node node)
        => SizeFor(node.Arguments.Count > 0 && node.Arguments[0] is int level ? level : 1);

    private static double SizeFor(int level)
    {
        switch (level)
        {
            case 1: return 20;
            case 2: return 17;
            case 3: return 15;
            default: return 14;
        }
    }

    private static MarkdownTree.Node Wrap(MarkdownTree.Node node)
    {
        MarkdownTree.Node holder = new MarkdownTree.Node("paragraph");
        holder.Children.Add(node);
        return holder;
    }

    private static void Text(MarkdownTree.Node node, StringBuilder into)
    {
        if (node.Name == "inline_text" && node.Arguments.Count > 0)
        {
            into.Append(node.Arguments[0] as string);
        }

        foreach (MarkdownTree.Node child in node.Children) { Text(child, into); }
    }

    private static bool TryParseColor(string css, out Color color)
    {
        color = default;
        if (string.IsNullOrEmpty(css) || css[0] != '#' || css.Length != 7)
        {
            return false;
        }

        if (!byte.TryParse(css.Substring(1, 2),
                NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte red)
            || !byte.TryParse(css.Substring(3, 2),
                NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte green)
            || !byte.TryParse(css.Substring(5, 2),
                NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte blue))
        {
            return false;
        }

        color = Color.FromArgb(255, red, green, blue);
        return true;
    }

    /// <summary>Decodes the numeric and named HTML entities a page may use.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The decoded text.</returns>
    internal static string DecodeEntities(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('&') < 0) { return text; }

        StringBuilder result = new StringBuilder(text.Length);
        int index = 0;
        while (index < text.Length)
        {
            int start = text.IndexOf('&', index);
            if (start < 0) { result.Append(text, index, text.Length - index); break; }

            int end = text.IndexOf(';', start + 1);
            result.Append(text, index, start - index);
            if (end < 0 || end - start > 10)
            {
                result.Append('&');
                index = start + 1;
                continue;
            }

            string entity = text.Substring(start + 1, end - start - 1);
            string decoded = Decode(entity);
            if (decoded == null)
            {
                result.Append(text, start, end - start + 1);
            }
            else
            {
                result.Append(decoded);
            }

            index = end + 1;
        }

        return result.ToString();
    }

    private static string Decode(string entity)
    {
        switch (entity)
        {
            case "amp": return "&";
            case "lt": return "<";
            case "gt": return ">";
            case "quot": return "\"";
            case "apos": return "'";
            case "nbsp": return " ";
            default: break;
        }

        if (entity.Length > 1 && entity[0] == '#')
        {
            bool hex = entity[1] == 'x' || entity[1] == 'X';
            string digits = entity.Substring(hex ? 2 : 1);
            if (int.TryParse(
                    digits,
                    hex ? NumberStyles.HexNumber : NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out int code)
                && code > 0 && code <= 0x10ffff)
            {
                return char.ConvertFromUtf32(code);
            }
        }

        return null;
    }

    /// <summary>The inline style in force while a run is being written.</summary>
    private readonly struct InlineStyle
    {
        private InlineStyle(bool italic, bool code)
        {
            Italic = italic;
            Code = code;
        }

        internal bool Italic { get; }

        internal bool Code { get; }

        internal InlineStyle WithItalic() => new InlineStyle(true, Code);

        internal InlineStyle WithCode() => new InlineStyle(Italic, true);
    }
}
