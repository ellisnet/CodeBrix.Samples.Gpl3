// === python-ly ly.musicxml module (the xml.etree element model it builds on) ===
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
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Fresco.Brix.Ly.MusicXml; //was previously: python's xml.etree.ElementTree, as ly.musicxml uses it

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One element of an XML tree, in python's <c>xml.etree</c> shape.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ WHY THIS EXISTS AND <c>System.Xml.Linq</c> DOES NOT DO. The MusicXML
/// exporter is verified byte for byte against the files python-ly itself
/// wrote, so what is needed is not "an XML tree" but python's element tree
/// exactly: an element carries <see cref="Text"/> AND <see cref="Tail"/> — the
/// text after its closing tag, which is where <c>ly.etreeutil.indent</c> puts
/// every newline — attributes keep INSERTION order, an element with no content
/// serialises as <c>&lt;tag /&gt;</c> with a space, and the escaping rules are
/// python's. An <c>XElement</c> has no tail at all, and <c>XmlWriter</c>'s
/// formatting is its own. Getting the same bytes out of either would mean
/// fighting them the whole way.
/// </para>
/// <para>
/// It is a small class because only what <c>ly.musicxml</c> uses is here: no
/// namespaces (MusicXML declares none), no comments, no processing
/// instructions, no parsing. The document declaration and the DOCTYPE are
/// written by <see cref="MusicXmlDocument"/>, exactly as upstream's
/// <c>MusicXML.write</c> writes them.
/// </para>
/// </remarks>
public sealed class ETreeElement : IEnumerable<ETreeElement>
{
    private readonly List<ETreeElement> _children = new List<ETreeElement>();

    //Insertion-ordered, which is what python 3.8 and later give an element's
    //attrib dict — and therefore the order they come out in.
    private readonly List<KeyValuePair<string, string>> _attributes
        = new List<KeyValuePair<string, string>>();

    /// <summary>Creates an element.</summary>
    /// <param name="tag">Its tag.</param>
    public ETreeElement(string tag)
        => Tag = tag ?? throw new ArgumentNullException(nameof(tag));

    /// <summary>Gets or sets the element's tag.</summary>
    public string Tag { get; set; }

    /// <summary>Gets or sets the text between the open tag and the first child.</summary>
    public string Text { get; set; }

    /// <summary>Gets or sets the text after the element's closing tag.</summary>
    public string Tail { get; set; }

    /// <summary>Gets how many children the element has.</summary>
    public int Count => _children.Count;

    /// <summary>Gets a child by index.</summary>
    /// <param name="index">The index.</param>
    /// <returns>The child.</returns>
    public ETreeElement this[int index] => _children[index];

    /// <summary>Gets the element's attributes, in the order they were set.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Attributes => _attributes;

    /// <summary>Gets an attribute's value, or null when it has none.</summary>
    /// <param name="name">The attribute.</param>
    /// <returns>The value, or null.</returns>
    public string Get(string name)
    {
        foreach (KeyValuePair<string, string> pair in _attributes)
        {
            if (string.Equals(pair.Key, name, StringComparison.Ordinal)) { return pair.Value; }
        }

        return null;
    }

    /// <summary>Sets an attribute, keeping its position when it is already there.</summary>
    /// <param name="name">The attribute.</param>
    /// <param name="value">Its value.</param>
    public void Set(string name, string value)
    {
        for (int i = 0; i < _attributes.Count; i++)
        {
            if (!string.Equals(_attributes[i].Key, name, StringComparison.Ordinal)) { continue; }

            _attributes[i] = new KeyValuePair<string, string>(name, value);
            return;
        }

        _attributes.Add(new KeyValuePair<string, string>(name, value));
    }

    /// <summary>Adds a child at the end.</summary>
    /// <param name="child">The child.</param>
    /// <returns>The child, so calls can be chained.</returns>
    public ETreeElement Append(ETreeElement child)
    {
        _children.Add(child ?? throw new ArgumentNullException(nameof(child)));
        return child;
    }

    /// <summary>Adds a child at a position.</summary>
    /// <param name="index">Where to put it.</param>
    /// <param name="child">The child.</param>
    /// <returns>The child.</returns>
    public ETreeElement Insert(int index, ETreeElement child)
    {
        if (child == null) { throw new ArgumentNullException(nameof(child)); }

        _children.Insert(Math.Clamp(index, 0, _children.Count), child);
        return child;
    }

    /// <summary>Removes a child.</summary>
    /// <param name="child">The child.</param>
    /// <returns>True when it was there.</returns>
    public bool Remove(ETreeElement child) => _children.Remove(child);

    /// <summary>Creates a child element and adds it at the end.</summary>
    /// <param name="tag">The child's tag.</param>
    /// <param name="attributes">Its attributes, in order.</param>
    /// <returns>The child.</returns>
    public ETreeElement SubElement(
        string tag, params (string Name, string Value)[] attributes)
    {
        var child = new ETreeElement(tag);
        if (attributes != null)
        {
            foreach ((string name, string value) in attributes) { child.Set(name, value); }
        }

        return Append(child);
    }

    /// <summary>Returns the index of the first child with a tag, or -1.</summary>
    /// <param name="tag">The tag to look for.</param>
    /// <returns>The index, or -1.</returns>
    /// <remarks>Upstream's module-level <c>get_tag_index</c>.</remarks>
    public int IndexOfTag(string tag)
    {
        for (int i = 0; i < _children.Count; i++)
        {
            if (string.Equals(_children[i].Tag, tag, StringComparison.Ordinal)) { return i; }
        }

        return -1;
    }

    /// <summary>Returns the first descendant with a tag, or null.</summary>
    /// <param name="tag">The tag to look for.</param>
    /// <returns>The element, or null.</returns>
    /// <remarks>
    /// Upstream's <c>root.find('.//encoding/software')</c> shape, which is the
    /// one search <c>ly.musicxml</c>'s callers make. Only the descendant search
    /// is here, because only it is used.
    /// </remarks>
    public ETreeElement FindDescendant(string tag)
    {
        foreach (ETreeElement child in _children)
        {
            if (string.Equals(child.Tag, tag, StringComparison.Ordinal)) { return child; }

            ETreeElement found = child.FindDescendant(tag);
            if (found != null) { return found; }
        }

        return null;
    }

    /// <inheritdoc/>
    public IEnumerator<ETreeElement> GetEnumerator() => _children.GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    /// <summary>Writes the element and everything under it.</summary>
    /// <param name="builder">Where to write.</param>
    /// <remarks>
    /// python's <c>ElementTree._serialize_xml</c>, arm for arm: the open tag,
    /// the attributes in order, then either a closed tag with the text, the
    /// children and the close tag, or <c>&lt;tag /&gt;</c> when there is
    /// neither text nor child — and the TAIL last, outside the element, which
    /// is what makes the indenting work.
    /// </remarks>
    public void Serialize(StringBuilder builder)
    {
        if (builder == null) { throw new ArgumentNullException(nameof(builder)); }

        builder.Append('<').Append(Tag);
        foreach (KeyValuePair<string, string> pair in _attributes)
        {
            builder.Append(' ').Append(pair.Key).Append("=\"")
                .Append(EscapeAttribute(pair.Value)).Append('"');
        }

        if (!string.IsNullOrEmpty(Text) || _children.Count > 0)
        {
            builder.Append('>');
            if (!string.IsNullOrEmpty(Text)) { builder.Append(EscapeText(Text)); }

            foreach (ETreeElement child in _children) { child.Serialize(builder); }

            builder.Append("</").Append(Tag).Append('>');
        }
        else
        {
            builder.Append(" />");
        }

        if (!string.IsNullOrEmpty(Tail)) { builder.Append(EscapeText(Tail)); }
    }

    /// <summary>Escapes character data, as python's <c>_escape_cdata</c> does.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The escaped text.</returns>
    internal static string EscapeText(string text)
    {
        if (string.IsNullOrEmpty(text)) { return text; }

        //Ampersand first, or the escapes escape each other.
        return text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
    }

    /// <summary>Escapes an attribute value, as python's <c>_escape_attrib</c> does.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The escaped value.</returns>
    /// <remarks>
    /// The three whitespace characters become NUMERIC references, with the tab
    /// written as two digits — <c>&amp;#09;</c>, not <c>&amp;#9;</c> — because
    /// that is the literal python writes.
    /// </remarks>
    internal static string EscapeAttribute(string value)
    {
        if (string.IsNullOrEmpty(value)) { return value; }

        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;")
            .Replace("\r", "&#13;")
            .Replace("\n", "&#10;")
            .Replace("\t", "&#09;");
    }
}

/// <summary>
/// Indenting an element tree in place.
/// </summary>
public static class ETreeUtil
{
    /// <summary>Gets whether a string is empty or nothing but whitespace.</summary>
    /// <param name="text">The string.</param>
    /// <returns>True when it is.</returns>
    /// <remarks>python's <c>str.isspace()</c> is false for the empty string, so
    /// upstream writes <c>not s or s.isspace()</c>; this is that.</remarks>
    public static bool IsBlank(string text)
        => string.IsNullOrEmpty(text) || string.IsNullOrWhiteSpace(text);

    /// <summary>Adds newlines and indenting to a tree, in place.</summary>
    /// <param name="element">The element to indent.</param>
    /// <param name="indentString">One level of indent.</param>
    /// <param name="level">How deep this element is.</param>
    /// <remarks>
    /// <para>
    /// Upstream's <c>ly.etreeutil.indent</c>, itself from effbot's prettyprint
    /// recipe. Text that is already NOT blank is left alone, which is what keeps
    /// a <c>&lt;words&gt;</c> or a lyric syllable exactly as it was written.
    /// </para>
    /// <para>
    /// ⚠ The last loop rebinds the loop variable in python (<c>for elem in
    /// elem:</c>), so the trailing <c>elem.tail</c> it fixes up afterwards is
    /// the LAST CHILD's, not the element's. That is not a slip — it is what
    /// closes the indent before the parent's closing tag — and the port keeps
    /// it by naming the child separately and using the child after the loop.
    /// </para>
    /// </remarks>
    public static void Indent(ETreeElement element, string indentString = "  ", int level = 0)
    {
        if (element == null) { return; }

        string i = "\n" + Repeat(indentString, level);
        if (element.Count > 0)
        {
            if (IsBlank(element.Text)) { element.Text = i + indentString; }

            if (IsBlank(element.Tail)) { element.Tail = i; }

            ETreeElement child = null;
            foreach (ETreeElement each in element)
            {
                child = each;
                Indent(child, indentString, level + 1);
            }

            if (IsBlank(child.Tail)) { child.Tail = i; }
        }
        else if (level != 0 && IsBlank(element.Tail))
        {
            element.Tail = i;
        }
    }

    private static string Repeat(string text, int times)
    {
        if (times <= 0 || string.IsNullOrEmpty(text)) { return string.Empty; }

        var builder = new StringBuilder(text.Length * times);
        for (int i = 0; i < times; i++) { builder.Append(text); }

        return builder.ToString();
    }

    /// <summary>Formats a number the way python's <c>str()</c> does.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The text.</returns>
    /// <remarks>
    /// Invariant culture, and a whole double comes out without a decimal point
    /// only where upstream's value was an int to begin with — so callers pass
    /// the type upstream had. This is here so no call site has to remember the
    /// culture (standing rule 7's other half).
    /// </remarks>
    public static string Str(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Formats a number the way python's <c>str()</c> does.</summary>
    /// <param name="value">The number.</param>
    /// <returns>The text.</returns>
    public static string Str(double value)
        => value == Math.Floor(value) && !double.IsInfinity(value)
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("R", CultureInfo.InvariantCulture);
}
