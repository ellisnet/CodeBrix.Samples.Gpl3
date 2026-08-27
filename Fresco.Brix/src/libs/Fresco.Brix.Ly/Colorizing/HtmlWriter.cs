// === python-ly ly.colorize module (the HTML-writing half) ===
//
// Copyright (c) 2008 - 2015 by Wilbert Berendsen
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation; either version 3
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
// See http://www.gnu.org/licenses/ for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using LyToken = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.Ly.Colorizing; //was previously: ly/colorize.py (map_tokens, html, HtmlWriter)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One token, and the slice of it that the selection actually covers.
/// </summary>
/// <remarks>
/// ⚠ Upstream RE-CREATES the token — <c>type(t)(t[a:b], pos)</c> — when the
/// selection cuts through one, so everything downstream can go on reading
/// <c>t.pos</c> and the text off the token itself. Constructing an arbitrary
/// token subclass by reflection to say the same thing would be a lot of
/// machinery for a substring; the slice carries the trimmed text and the
/// position beside the token instead, and the token is still there for the
/// style lookup, which is the only other thing anybody asks it.
/// </remarks>
public readonly struct TokenSlice
{
    /// <summary>Creates a slice.</summary>
    /// <param name="token">The token.</param>
    /// <param name="text">The text the selection covers.</param>
    /// <param name="pos">Where that text starts in the document.</param>
    public TokenSlice(LyToken token, string text, int pos)
    {
        Token = token;
        Text = text;
        Pos = pos;
    }

    /// <summary>Gets the token, whose TYPE decides the style.</summary>
    public LyToken Token { get; }

    /// <summary>Gets the text the selection covers.</summary>
    public string Text { get; }

    /// <summary>Gets where that text starts in the document.</summary>
    public int Pos { get; }

    /// <summary>Gets where that text ends in the document.</summary>
    public int End => Pos + (Text?.Length ?? 0);
}

/// <summary>One run of text and the style it is drawn in.</summary>
public readonly struct StyledText
{
    /// <summary>Creates a styled run.</summary>
    /// <param name="text">The text.</param>
    /// <param name="style">Its style, or null for unstyled text.</param>
    public StyledText(string text, CssClass style)
    {
        Text = text;
        Style = style;
    }

    /// <summary>Gets the text.</summary>
    public string Text { get; }

    /// <summary>Gets the style, or null.</summary>
    public CssClass Style { get; }
}

/// <summary>
/// Turning a tokenized document into syntax-highlighted HTML.
/// </summary>
/// <remarks>
/// The half of <c>ly.colorize</c> that W2 left until something needed it, and
/// the colored-HTML export is that something. Everything here reads the SAME
/// mapping the editor's own highlighting reads, so what a user copies out
/// looks like what they were looking at.
/// </remarks>
public static class HtmlColorize
{
    /// <summary>
    /// Returns the cursor's tokens, cut down to the selection at both ends.
    /// </summary>
    /// <param name="cursor">The selection.</param>
    /// <returns>The tokens.</returns>
    /// <remarks>
    /// A token that only PARTLY falls inside the selection is re-created so it
    /// falls exactly within it — which is what makes a selection that starts
    /// halfway through a word come out as that half and not the whole word.
    /// </remarks>
    public static IReadOnlyList<TokenSlice> GetTokens(Cursor cursor)
    {
        var tokens = new List<TokenSlice>();
        foreach (LyToken t in new Source(cursor, null, false, OverlapMode.Partial, true))
        {
            tokens.Add(new TokenSlice(t, t.Text, t.Pos));
        }

        if (tokens.Count == 0) { return tokens; }

        int last = tokens.Count - 1;
        if (cursor.End != null && tokens[last].End > cursor.End.Value)
        {
            TokenSlice t = tokens[last];
            int keep = Math.Max(0, t.Text.Length + (cursor.End.Value - t.End));
            tokens[last] = new TokenSlice(t.Token, t.Text.Substring(0, keep), t.Pos);
        }

        if (cursor.Start > tokens[0].Pos)
        {
            TokenSlice t = tokens[0];
            tokens[0] = new TokenSlice(
                t.Token, t.Text.Substring(cursor.Start - t.Pos), cursor.Start);
        }

        return tokens;
    }

    /// <summary>Yields every run of the selection with the style it maps to.</summary>
    /// <param name="cursor">The selection.</param>
    /// <param name="mapper">What decides a token's style.</param>
    /// <returns>The runs, in order.</returns>
    /// <remarks>
    /// Text between tokens — anything the lexer did not tokenize — is yielded
    /// with no style at all, so nothing in the selection is lost.
    /// </remarks>
    public static IEnumerable<StyledText> MapTokens(Cursor cursor, TokenMapper<CssClass> mapper)
    {
        string text = cursor.Document.PlainText();
        int start = cursor.Start;
        IReadOnlyList<TokenSlice> tokens = GetTokens(cursor);
        bool any = false;
        int lastEnd = 0;
        foreach (TokenSlice t in tokens)
        {
            if (t.Pos > start)
            {
                yield return new StyledText(text.Substring(start, t.Pos - start), null);
            }

            yield return new StyledText(t.Text, mapper.ValueFor(t.Token));
            start = t.End;
            lastEnd = t.End;
            any = true;
        }

        if (any && cursor.End != null && cursor.End.Value > lastEnd)
        {
            yield return new StyledText(
                text.Substring(lastEnd, cursor.End.Value - lastEnd), null);
        }
    }

    /// <summary>Melts neighbouring runs that share a style into one.</summary>
    /// <param name="mapped">The runs.</param>
    /// <returns>The melted runs.</returns>
    /// <remarks>
    /// ⚠ Whitespace joins whatever came before it whatever its own style says,
    /// and a run ending in a single space gives that space back UNSTYLED. Both
    /// are upstream's, and both are about the HTML: a trailing styled space
    /// inside a span underlines or colours a gap between words.
    /// </remarks>
    public static IEnumerable<StyledText> MeltMappedTokens(IEnumerable<StyledText> mapped)
    {
        var prevTokens = new List<string>();
        CssClass prevStyle = null;
        bool started = false;

        foreach (StyledText item in mapped)
        {
            bool sameStyle = started && Equals(item.Style, prevStyle);
            if (sameStyle || IsSpace(item.Text))
            {
                prevTokens.Add(item.Text);
                started = true;
                continue;
            }

            if (prevTokens.Count > 0)
            {
                if (prevTokens[prevTokens.Count - 1] == " ")
                {
                    yield return new StyledText(
                        string.Concat(prevTokens.Take(prevTokens.Count - 1)), prevStyle);
                    yield return new StyledText(" ", null);
                }
                else
                {
                    yield return new StyledText(string.Concat(prevTokens), prevStyle);
                }
            }

            prevTokens = new List<string> { item.Text };
            prevStyle = item.Style;
            started = true;
        }

        if (prevTokens.Count > 0)
        {
            yield return new StyledText(string.Concat(prevTokens), prevStyle);
        }
    }

    /// <summary>Wraps the selection's tokens in span elements.</summary>
    /// <param name="cursor">The selection.</param>
    /// <param name="mapper">What decides a token's style.</param>
    /// <param name="span">
    /// What attribute a span gets for a style; the CSS class by default.
    /// </param>
    /// <returns>The HTML, which the caller wraps in a pre element.</returns>
    public static string Html(
        Cursor cursor, TokenMapper<CssClass> mapper, Func<CssClass, string> span = null)
    {
        span ??= Colorize.FormatCssSpanClass;
        var result = new StringBuilder();
        foreach (StyledText item in MeltMappedTokens(MapTokens(cursor, mapper)))
        {
            string arg = item.Style != null ? span(item.Style) : null;
            if (!string.IsNullOrEmpty(arg))
            {
                result.Append("<span ").Append(arg).Append('>');
                result.Append(Colorize.HtmlEscape(item.Text));
                result.Append("</span>");
            }
            else
            {
                result.Append(Colorize.HtmlEscape(item.Text));
            }
        }

        return result.ToString();
    }

    /// <summary>Returns the inline style attribute for a style, or null.</summary>
    /// <param name="cssClass">The style.</param>
    /// <param name="scheme">The scheme, or null for the default.</param>
    /// <returns>The attribute, or null when the scheme says nothing.</returns>
    public static string CssStyleAttribute(CssClass cssClass, CssScheme scheme = null)
    {
        IDictionary<string, string> d = Colorize.CssDict(cssClass, scheme);
        if (d.Count == 0) { return null; }

        return "style=\"" + string.Join(
            " ",
            d.OrderBy(i => i.Key, StringComparer.Ordinal).Select(Colorize.CssItem)) + "\"";
    }

    /// <summary>Formats an attribute dictionary, each with a leading space.</summary>
    /// <param name="attributes">The attributes.</param>
    /// <returns>The formatted text.</returns>
    public static string HtmlFormatAttrs(IEnumerable<KeyValuePair<string, string>> attributes)
    {
        var result = new StringBuilder();
        foreach (KeyValuePair<string, string> pair in attributes)
        {
            result.Append(' ').Append(pair.Key).Append("=\"")
                .Append(Colorize.HtmlEscapeAttr(pair.Value)).Append('"');
        }

        return result.ToString();
    }

    /// <summary>Puts line numbers beside the highlighted document, in a table.</summary>
    /// <param name="cursor">The selection.</param>
    /// <param name="html">The highlighted document.</param>
    /// <param name="linenumAttrs">The attributes for the numbers cell.</param>
    /// <param name="documentAttrs">The attributes for the document cell.</param>
    /// <returns>The table.</returns>
    public static string AddLineNumbers(
        Cursor cursor, string html,
        IDictionary<string, string> linenumAttrs = null,
        IDictionary<string, string> documentAttrs = null)
    {
        var numbers = linenumAttrs != null
            ? new OrderedAttributes(linenumAttrs)
            : new OrderedAttributes(new Dictionary<string, string> { ["style"] = "background: #eeeeee;" });
        var document = documentAttrs != null
            ? new OrderedAttributes(documentAttrs)
            : new OrderedAttributes();

        numbers.SetDefault("id", "linenumbers");
        document.SetDefault("id", "document");
        numbers["valign"] = "top";
        numbers["align"] = "right";
        numbers["style"] = (numbers["style"] ?? string.Empty)
            + "vertical-align: top; text-align: right;";
        document["valign"] = "top";
        document["style"] = (document["style"] ?? string.Empty) + "vertical-align: top;";

        int startNum = cursor.Document.Index(cursor.StartBlock()) + 1;
        int endNum = cursor.Document.Index(cursor.EndBlock()) + 1;

        //⚠ range(start, end) STOPS BEFORE end, so the last line of the
        //selection gets no number. It is upstream's arithmetic and it is kept:
        //the numbers are a decoration beside the code, and changing where they
        //stop would change every file Frescobaldi has ever exported.
        var lines = new List<string>();
        for (int n = startNum; n < endNum; n++)
        {
            lines.Add(n.ToString(CultureInfo.InvariantCulture));
        }

        string linenumbers = "<pre>" + string.Join("\n", lines) + "</pre>";
        string body = "<pre>" + html + "</pre>";
        return "<table border=\"0\" cellpadding=\"4\" cellspacing=\"0\">"
            + "<tbody><tr>"
            + "<td" + HtmlFormatAttrs(numbers.Items) + ">"
            + "\n" + linenumbers + "\n"
            + "</td>"
            + "<td" + HtmlFormatAttrs(document.Items) + ">"
            + "\n" + body + "\n"
            + "</td></tr></tbody></table>\n";
    }

    /// <summary>Wraps a body in a complete HTML document.</summary>
    /// <param name="body">The body, put in unchanged.</param>
    /// <param name="title">The title, which is escaped.</param>
    /// <param name="stylesheet">A stylesheet to put in verbatim, or null.</param>
    /// <param name="stylesheetRef">A stylesheet to link to, or null.</param>
    /// <param name="encoding">The encoding to declare.</param>
    /// <returns>The document.</returns>
    public static string FormatHtmlDocument(
        string body, string title = "", string stylesheet = null,
        string stylesheetRef = null, string encoding = "UTF-8")
    {
        var css = new StringBuilder();
        if (!string.IsNullOrEmpty(stylesheetRef))
        {
            css.Append("<link rel=\"stylesheet\" type=\"text/css\" href=\"")
                .Append(Colorize.HtmlEscapeAttr(stylesheetRef)).Append("\"/>\n");
        }

        if (!string.IsNullOrEmpty(stylesheet))
        {
            css.Append("<style type=\"text/css\">\n").Append(stylesheet).Append("\n</style>\n");
        }

        return "<!DOCTYPE HTML PUBLIC \"-//W3C//DTD HTML 4.01//EN\" "
            + "\"http://www.w3.org/TR/html4/strict.dtd\">\n"
            + "<html><head>\n"
            + "<title>" + Colorize.HtmlEscape(title) + "</title>\n"
            + "<meta http-equiv=\"Content-Type\" content=\"text/html; charset="
            + encoding + "\" />\n"
            + css
            + "</head>\n"
            + "<body>\n" + body + "</body>\n</html>\n";
    }

    private static bool IsSpace(string text)
        => !string.IsNullOrEmpty(text) && string.IsNullOrWhiteSpace(text);

    /// <summary>An attribute dictionary that keeps its insertion order.</summary>
    /// <remarks>
    /// python's dict does, and the attributes come out of it in that order.
    /// </remarks>
    internal sealed class OrderedAttributes
    {
        private readonly List<KeyValuePair<string, string>> _items
            = new List<KeyValuePair<string, string>>();

        internal OrderedAttributes()
        {
        }

        internal OrderedAttributes(IDictionary<string, string> from)
        {
            foreach (KeyValuePair<string, string> pair in from) { this[pair.Key] = pair.Value; }
        }

        internal IReadOnlyList<KeyValuePair<string, string>> Items => _items;

        internal string this[string key]
        {
            get
            {
                foreach (KeyValuePair<string, string> pair in _items)
                {
                    if (pair.Key == key) { return pair.Value; }
                }

                return null;
            }

            set
            {
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_items[i].Key != key) { continue; }

                    _items[i] = new KeyValuePair<string, string>(key, value);
                    return;
                }

                _items.Add(new KeyValuePair<string, string>(key, value));
            }
        }

        internal void SetDefault(string key, string value)
        {
            if (this[key] == null) { this[key] = value; }
        }
    }
}

/// <summary>
/// Everything the colored-HTML export can be told, and the HTML it produces.
/// </summary>
/// <remarks>
/// Upstream's <c>HtmlWriter</c>, attribute for attribute. It exists because
/// there are a dozen switches and the caller sets whichever it cares about.
/// </remarks>
public sealed class HtmlWriter
{
    /// <summary>Gets or sets the document's foreground colour, or null.</summary>
    public string Foreground { get; set; }

    /// <summary>Gets or sets the document's background colour, or null.</summary>
    public string Background { get; set; }

    /// <summary>Gets or sets the line numbers' foreground colour, or null.</summary>
    public string LineNumbersForeground { get; set; }

    /// <summary>Gets or sets the line numbers' background colour.</summary>
    public string LineNumbersBackground { get; set; } = "#eeeeee";

    /// <summary>Gets or sets whether styles are written inline on each span.</summary>
    public bool InlineStyle { get; set; }

    /// <summary>Gets or sets whether line numbers are shown.</summary>
    public bool NumberLines { get; set; }

    /// <summary>Gets the tag the document is wrapped in.</summary>
    public string WrapperTag { get; private set; } = "pre";

    /// <summary>Gets the attribute the wrapper is identified by.</summary>
    public string WrapperAttribute { get; private set; } = "id";

    /// <summary>Gets or sets the document wrapper's identifier.</summary>
    public string DocumentId { get; set; } = "document";

    /// <summary>Gets or sets the line numbers wrapper's identifier.</summary>
    public string LineNumbersId { get; set; } = "linenumbers";

    /// <summary>Gets or sets the document's title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the colour scheme.</summary>
    public CssScheme CssScheme { get; set; }

    /// <summary>Gets or sets what decides a token's style, or null for the default.</summary>
    public TokenMapper<CssClass> CssMapper { get; set; }

    /// <summary>Gets or sets the encoding to declare.</summary>
    public string Encoding { get; set; } = "UTF-8";

    /// <summary>Gets or sets a stylesheet to link to instead of embedding one.</summary>
    public string StylesheetRef { get; set; }

    /// <summary>Gets or sets whether a whole document is produced, not just a body.</summary>
    public bool FullHtml { get; set; } = true;

    /// <summary>Gets or sets where invalid settings are reported, or null.</summary>
    public Action<string> Warn { get; set; }

    /// <summary>Sets the attribute the wrapper is identified by.</summary>
    /// <param name="attr">Either <c>id</c> or <c>class</c>.</param>
    public void SetWrapperAttribute(string attr)
    {
        if (attr == "id" || attr == "class") { WrapperAttribute = attr; }
        else { Warn?.Invoke("Invalid attribute, has to be one of [id, class]"); }
    }

    /// <summary>Sets the tag the document is wrapped in.</summary>
    /// <param name="tag">One of <c>pre</c>, <c>code</c> or <c>div</c>.</param>
    public void SetWrapperTag(string tag)
    {
        if (tag == "pre" || tag == "code" || tag == "div") { WrapperTag = tag; }
        else { Warn?.Invoke("Invalid tag, has to be one of [pre, code, div]"); }
    }

    /// <summary>Produces the HTML for a selection.</summary>
    /// <param name="cursor">The selection.</param>
    /// <returns>The HTML.</returns>
    public string Html(Cursor cursor)
    {
        var docStyle = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(Foreground)) { docStyle["color"] = Foreground; }

        if (!string.IsNullOrEmpty(Background)) { docStyle["background"] = Background; }

        var numStyle = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(LineNumbersForeground))
        {
            numStyle["color"] = LineNumbersForeground;
        }

        if (!string.IsNullOrEmpty(LineNumbersBackground))
        {
            numStyle["background"] = LineNumbersBackground;
        }

        var numAttrs = new Dictionary<string, string> { [WrapperAttribute] = LineNumbersId };
        var docAttrs = new Dictionary<string, string> { [WrapperAttribute] = DocumentId };

        var css = new List<string>();
        Func<CssClass, string> formatter;
        if (InlineStyle)
        {
            formatter = style => HtmlColorize.CssStyleAttribute(style, CssScheme);
            AddStyleAttribute(numAttrs, numStyle);
            AddStyleAttribute(docAttrs, docStyle);
        }
        else
        {
            formatter = Colorize.FormatCssSpanClass;
            string wrapType = WrapperAttribute == "id" ? "#" : ".";
            css.Add(Colorize.CssGroup(wrapType + DocumentId, docStyle));
            if (NumberLines) { css.Add(Colorize.CssGroup(wrapType + LineNumbersId, numStyle)); }

            css.Add(Colorize.FormatStylesheet(CssScheme));
        }

        string body = HtmlColorize.Html(cursor, CssMapper ?? Colorize.CssMapper(), formatter);

        body = NumberLines
            ? HtmlColorize.AddLineNumbers(cursor, body, numAttrs, docAttrs)
            : "<" + WrapperTag + HtmlColorize.HtmlFormatAttrs(docAttrs) + ">"
                + body + "</" + WrapperTag + ">";

        if (!FullHtml) { return body; }

        string sheet = string.IsNullOrEmpty(StylesheetRef) ? string.Join("\n", css) : null;
        return HtmlColorize.FormatHtmlDocument(body, Title, sheet, StylesheetRef, Encoding);
    }

    private static void AddStyleAttribute(
        Dictionary<string, string> attributes, IDictionary<string, string> style)
    {
        string value = Colorize.CssAttr(style);
        if (!string.IsNullOrEmpty(value)) { attributes["style"] = value; }
    }
}
