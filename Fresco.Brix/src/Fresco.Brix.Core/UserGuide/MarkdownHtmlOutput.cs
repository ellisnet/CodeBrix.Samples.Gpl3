// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Text;

namespace Fresco.Brix.UserGuide; //was previously: frescobaldi/simplemarkdown.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// An output that writes the parsed document as HTML.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>simplemarkdown.HtmlOutput</c>, which dispatches by building a
/// method name from the node's own name; the dispatch is a switch here and the
/// handlers are the same handlers.
/// </para>
/// <para>
/// ⚠ NOTHING IN THE APPLICATION SHOWS THIS HTML. Ruling FR8 keeps every web
/// view out of Fresco.Brix, so the user guide is drawn from the parse TREE
/// into the platform's own controls (see <see cref="GuideRenderer"/>). This
/// class is here because it is half of the parser's contract and because it is
/// what the parity fixtures recorded from Frescobaldi's own module compare
/// against — the one output whose exact bytes upstream can be asked for.
/// </para>
/// </remarks>
public class MarkdownHtmlOutput : MarkdownOutput
{
    private readonly List<string> _html = new List<string>();
    private readonly List<(string Name, object[] Arguments)> _tags
        = new List<(string, object[])>();

    /// <summary>
    /// Gets or sets how many levels a heading is pushed down: 0 makes
    /// <c>=== title</c> an <c>h1</c>, 1 makes it an <c>h2</c>.
    /// </summary>
    public int HeadingOffset { get; set; }

    /// <summary>Gets the tags pushed and not yet popped.</summary>
    protected IReadOnlyList<(string Name, object[] Arguments)> Tags => _tags;

    /// <inheritdoc/>
    public override void Push(string name, params object[] arguments)
    {
        Start(name, arguments ?? Array.Empty<object>());
        _tags.Add((name, arguments ?? Array.Empty<object>()));
    }

    /// <inheritdoc/>
    public override void Pop()
    {
        (string Name, object[] Arguments) top = _tags[_tags.Count - 1];
        _tags.RemoveAt(_tags.Count - 1);
        End(top.Name, top.Arguments);
    }

    /// <summary>Gets the HTML written so far.</summary>
    /// <returns>The HTML.</returns>
    public string Html() => string.Concat(_html);

    /// <summary>Escapes <c>&amp;</c>, <c>&lt;</c> and <c>&gt;</c>.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The escaped text.</returns>
    public string HtmlEscape(string text) => SimpleMarkdown.HtmlEscape(text);

    /// <summary>Writes a tag; a name like <c>/p</c> writes a close tag.</summary>
    /// <param name="name">The tag name.</param>
    /// <param name="attributes">The attributes, or null.</param>
    protected void Tag(string name, IReadOnlyList<(string Name, string Value)> attributes = null)
    {
        StringBuilder builder = new StringBuilder("<").Append(name);
        if (attributes != null)
        {
            foreach ((string attribute, string value) in attributes)
            {
                builder.Append(' ').Append(attribute).Append("=\"")
                    .Append(HtmlEscape(value).Replace("\"", "&quot;")).Append('"');
            }
        }

        _html.Add(builder.Append('>').ToString());
    }

    /// <summary>Writes a newline.</summary>
    protected void NewLine() => _html.Add("\n");

    /// <summary>Writes escaped text.</summary>
    /// <param name="text">The text.</param>
    protected void Text(string text) => _html.Add(HtmlEscape(text));

    /// <summary>Writes text that is already HTML.</summary>
    /// <param name="html">The HTML.</param>
    protected void Raw(string html) => _html.Add(html);

    /// <summary>Handles the start of a node.</summary>
    /// <param name="name">The node name.</param>
    /// <param name="arguments">The node's arguments.</param>
    protected virtual void Start(string name, object[] arguments)
    {
        switch (name)
        {
            case "code":
                CodeStart(
                    arguments.Length > 0 ? arguments[0] as string : null,
                    arguments.Length > 1 ? arguments[1] as string : null);
                break;
            case "heading":
                Tag("h" + HeadingLevel(arguments));
                break;
            case "paragraph":
                if (_tags.Count > 0 && _tags[_tags.Count - 1].Name == "definitionlist")
                {
                    Tag("dd");
                }

                Tag("p");
                break;
            case "orderedlist": Tag("ol"); NewLine(); break;
            case "orderedlist_item": Tag("li"); break;
            case "unorderedlist": Tag("ul"); NewLine(); break;
            case "unorderedlist_item": Tag("li"); break;
            case "definitionlist": Tag("dl"); NewLine(); break;
            case "definitionlist_item": break;
            case "definitionlist_item_term": Tag("dt"); break;
            case "definitionlist_item_definition": Tag("dd"); break;
            case "inline": InlineStart(); break;
            case "inline_code": Tag("code"); break;
            case "inline_emphasis": Tag("em"); break;
            case "link":
                Tag("a", new[] { ("href", arguments.Length > 0 ? arguments[0] as string : null) });
                break;
            case "inline_text":
                InlineTextStart(arguments.Length > 0 ? arguments[0] as string : null);
                break;
            default:
                throw new InvalidOperationException(
                    "unknown markdown node: " + name);
        }
    }

    /// <summary>Handles the end of a node.</summary>
    /// <param name="name">The node name.</param>
    /// <param name="arguments">The node's arguments.</param>
    protected virtual void End(string name, object[] arguments)
    {
        switch (name)
        {
            case "code":
                CodeEnd(
                    arguments.Length > 0 ? arguments[0] as string : null,
                    arguments.Length > 1 ? arguments[1] as string : null);
                break;
            case "heading":
                Tag("/h" + HeadingLevel(arguments));
                NewLine();
                break;
            case "paragraph":
                Tag("/p");
                if (_tags.Count > 0 && _tags[_tags.Count - 1].Name == "definitionlist")
                {
                    Tag("/dd");
                }

                NewLine();
                break;
            case "orderedlist": Tag("/ol"); NewLine(); break;
            case "orderedlist_item": Tag("/li"); NewLine(); break;
            case "unorderedlist": Tag("/ul"); NewLine(); break;
            case "unorderedlist_item": Tag("/li"); NewLine(); break;
            case "definitionlist": Tag("/dl"); NewLine(); break;
            case "definitionlist_item": break;
            case "definitionlist_item_term": Tag("/dt"); NewLine(); break;
            case "definitionlist_item_definition": Tag("/dd"); NewLine(); break;
            case "inline": InlineEnd(); break;
            case "inline_code": Tag("/code"); break;
            case "inline_emphasis": Tag("/em"); break;
            case "link": Tag("/a"); break;
            case "inline_text": break;
            default:
                throw new InvalidOperationException(
                    "unknown markdown node: " + name);
        }
    }

    /// <summary>Writes the start of a code block.</summary>
    /// <param name="code">The code.</param>
    /// <param name="specifier">The language, or null.</param>
    protected virtual void CodeStart(string code, string specifier)
    {
        Tag("code");
        Tag("pre");
        Text(code);
    }

    /// <summary>Writes the end of a code block.</summary>
    /// <param name="code">The code.</param>
    /// <param name="specifier">The language, or null.</param>
    protected virtual void CodeEnd(string code, string specifier)
    {
        Tag("/pre");
        Tag("/code");
        NewLine();
    }

    /// <summary>Called when a block of inline text starts.</summary>
    protected virtual void InlineStart() { }

    /// <summary>Called when a block of inline text ends.</summary>
    protected virtual void InlineEnd() { }

    /// <summary>Writes a run of inline text.</summary>
    /// <param name="text">The text.</param>
    protected virtual void InlineTextStart(string text) => Text(text);

    private int HeadingLevel(object[] arguments)
    {
        int headingType = arguments.Length > 0 && arguments[0] is int value ? value : 1;
        return Math.Min(HeadingOffset + headingType, 6);
    }
}
