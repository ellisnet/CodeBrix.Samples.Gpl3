// === python-ly ly.dom module (the base classes and helpers) ===
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
using System.Text.RegularExpressions;

namespace Fresco.Brix.Ly.Dom; //was previously: ly/dom.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.
//
// Upstream builds this module out of python mixins (Named, HandleVars,
// AddDuration) that C# has no equivalent for. Each is folded into the class
// that needs it: Named becomes the name-prefixing Ly override on Statement and
// StatementEnclosed, HandleVars becomes VariableSection, and AddDuration
// becomes the Ly override on TextDur.

/// <summary>
/// Base class for the LilyPond document objects. A tree of these prints as a
/// LilyPond document; it is a convenience for building documents, not a
/// validator — nothing here enforces a legal file.
/// </summary>
public class LyNode : WeakNode
{
    private int? _before;
    private int? _after;

    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public LyNode(Node parent = null)
        : base(parent)
    {
    }

    /// <summary>Gets whether this element is a single LilyPond atom (word,
    /// note, …); when it is the only element inside <c>{ }</c>, the brackets
    /// can be removed.</summary>
    public virtual bool IsAtom => false;

    /// <summary>
    /// Gets or sets how many newlines this object wants before it; setting it
    /// speaks for THIS node only, leaving the type's own answer alone.
    /// </summary>
    /// <remarks>
    /// //was previously: a read-only virtual property. Upstream's
    /// <c>before</c>/<c>after</c> are plain CLASS attributes that a caller
    /// shadows on one instance — <c>ly.dom.KeySignature(…).after = 1</c> is
    /// how the score wizard spaces its output — and a read-only property has
    /// nowhere to put that. The type's answer moved to
    /// <see cref="DefaultBefore"/>, which subclasses override as before.
    /// </remarks>
    public int Before
    {
        get => _before ?? DefaultBefore;
        set => _before = value;
    }

    /// <summary>
    /// Gets or sets how many newlines this object wants after it; setting it
    /// speaks for THIS node only.
    /// </summary>
    public int After
    {
        get => _after ?? DefaultAfter;
        set => _after = value;
    }

    /// <summary>Gets the type's own answer for <see cref="Before"/>.</summary>
    protected virtual int DefaultBefore => 0;

    /// <summary>Gets the type's own answer for <see cref="After"/>.</summary>
    protected virtual int DefaultAfter => 0;

    /// <summary>Answers the printable output for this object.</summary>
    /// <param name="printer">The printer, asked for settings such as the pitch
    /// language.</param>
    /// <returns>The output.</returns>
    public virtual string Ly(Printer printer) => string.Empty;

    /// <summary>
    /// Answers the newlines that join this node to another one; the empty
    /// string when none are wanted.
    /// </summary>
    /// <param name="other">The node that follows.</param>
    /// <returns>The newlines.</returns>
    public string Concat(LyNode other) => new string('\n', Math.Max(After, other.Before));

    /// <summary>Formats a name that may be a plain string or a
    /// <see cref="Reference"/>.</summary>
    /// <param name="value">The name.</param>
    /// <returns>The text.</returns>
    protected static string Format(object value) => value?.ToString() ?? string.Empty;
}

/// <summary>A node without children.</summary>
public class Leaf : LyNode
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Leaf(Node parent = null)
        : base(parent)
    {
    }
}

/// <summary>A node that concatenates its children on output.</summary>
public class Container : LyNode
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Container(Node parent = null)
        : base(parent)
    {
    }

    /// <summary>Gets the character the children are joined with by default.</summary>
    public virtual string DefaultSpace => " ";

    /// <inheritdoc/>
    protected override int DefaultBefore => ContainerBefore;

    /// <inheritdoc/>
    protected override int DefaultAfter => ContainerAfter;

    /// <summary>Gets what the first child wants before it — upstream's
    /// <c>Container.before</c>, which subclasses reach past their own
    /// override through the super proxy.</summary>
    protected int ContainerBefore => Count > 0 ? ((LyNode)this[0]).Before : 0;

    /// <summary>Gets what the last child wants after it.</summary>
    protected int ContainerAfter => Count > 0 ? ((LyNode)this[Count - 1]).After : 0;

    /// <inheritdoc/>
    public override string Ly(Printer printer) => ContainerLy(printer);

    /// <summary>Answers the joined output of the children.</summary>
    /// <param name="printer">The printer.</param>
    /// <returns>The output.</returns>
    protected string ContainerLy(Printer printer)
    {
        if (Count == 0) { return string.Empty; }

        var node = (LyNode)this[0];
        var result = new StringBuilder(node.Ly(printer));
        for (int i = 1; i < Count; i++)
        {
            var next = (LyNode)this[i];
            string join = node.Concat(next);
            result.Append(join.Length > 0 ? join : DefaultSpace);
            result.Append(next.Ly(printer));
            node = next;
        }

        return result.ToString();
    }
}

/// <summary>
/// Performs the operations a node tree needs on its behalf — quoting strings,
/// translating pitch names, indenting output.
/// </summary>
public class Printer
{
    /// <summary>Gets or sets the opening primary quote.</summary>
    public string PrimaryQuoteLeft { get; set; } = "‘";

    /// <summary>Gets or sets the closing primary quote.</summary>
    public string PrimaryQuoteRight { get; set; } = "’";

    /// <summary>Gets or sets the opening secondary quote.</summary>
    public string SecondaryQuoteLeft { get; set; } = "“";

    /// <summary>Gets or sets the closing secondary quote.</summary>
    public string SecondaryQuoteRight { get; set; } = "”";

    /// <summary>Gets or sets whether typographical quotes are written.</summary>
    public bool TypographicalQuotes { get; set; } = true;

    /// <summary>Gets or sets the pitch-name language written.</summary>
    public string Language { get; set; } = "nederlands";

    /// <summary>Gets or sets one indent step.</summary>
    public string IndentString { get; set; } = "  ";

    /// <summary>Answers a text as a quoted LilyPond string.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The quoted string.</returns>
    public string QuoteString(string text)
    {
        text ??= string.Empty;
        if (TypographicalQuotes)
        {
            text = Regex.Replace(
                text, "\"(.*?)\"", PrimaryQuoteLeft + "$1" + PrimaryQuoteRight);
            text = Regex.Replace(
                text, "'(.*?)'", SecondaryQuoteLeft + "$1" + SecondaryQuoteRight);
            text = text.Replace("'", "‘");
        }

        //Escape the regular double quotes, then quote the string.
        text = text.Replace("\"", "\\\"");
        return "\"" + text + "\"";
    }

    /// <summary>
    /// Walks the output of a node, yielding properly indented LilyPond code.
    /// </summary>
    /// <param name="node">The node to print.</param>
    /// <param name="startIndent">The indent to start at.</param>
    /// <returns>The lines.</returns>
    public IEnumerable<string> IndentGen(LyNode node, int startIndent = 0)
    {
        int depth = startIndent;
        var lines = new List<string>(SplitLines(node.Ly(this)));
        for (int i = 0; i < node.After; i++) { lines.Add(string.Empty); }

        foreach (string line in lines)
        {
            if (depth > 0 && Regex.IsMatch(line, @"^(#?}|>|%})")) { depth -= 1; }

            yield return string.Concat(Enumerable.Repeat(IndentString, depth)) + line;
            if (Regex.IsMatch(line, @"(\{|<|%\{)$")) { depth += 1; }
        }
    }

    /// <summary>Answers a formatted printout of a node and its children.</summary>
    /// <param name="node">The node to print.</param>
    /// <returns>The text.</returns>
    public string Indent(LyNode node) => string.Join("\n", IndentGen(node));

    /// <summary>Python's <c>str.splitlines()</c>: no trailing empty line.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The lines.</returns>
    private static IEnumerable<string> SplitLines(string text)
    {
        if (text.Length == 0) { yield break; }

        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '\n') { continue; }

            yield return text.Substring(start, i - start);
            start = i + 1;
        }

        if (start < text.Length) { yield return text.Substring(start); }
    }
}

/// <summary>
/// A simple object that keeps a name, to use as a (context) identifier: set
/// its name and every place in the document that references it shows the same
/// name.
/// </summary>
public class Reference
{
    /// <summary>Initializes the reference.</summary>
    /// <param name="name">The name shown.</param>
    public Reference(string name = "") => Name = name;

    /// <summary>Gets or sets the name shown.</summary>
    public string Name { get; set; }

    /// <summary>Answers the name.</summary>
    /// <returns>The name.</returns>
    public override string ToString() => Name ?? string.Empty;
}

/// <summary>A vertical container that puts everything on a new line.</summary>
public class Block : Container
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Block(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override string DefaultSpace => "\n";

    /// <inheritdoc/>
    protected override int DefaultBefore => 1;

    /// <inheritdoc/>
    protected override int DefaultAfter => 1;
}

/// <summary>A whole LilyPond document: everything on a new line.</summary>
public class Document : Container
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Document(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override string DefaultSpace => "\n";

    /// <inheritdoc/>
    protected override int DefaultAfter => 1;
}

/// <summary>A leaf node with arbitrary text.</summary>
public class Text : Leaf
{
    /// <summary>Initializes the node.</summary>
    /// <param name="text">The text.</param>
    /// <param name="parent">The parent to attach to.</param>
    public Text(object text = null, Node parent = null)
        : base()
    {
        TextValue = text ?? string.Empty;
        parent?.Append(this);
    }

    /// <summary>Gets or sets the text — a string, or a
    /// <see cref="Reference"/>.</summary>
    public object TextValue { get; set; } //was previously: text

    /// <inheritdoc/>
    public override string Ly(Printer printer) => Format(TextValue);
}

/// <summary>A text note with an optional duration as a child.</summary>
public class TextDur : Text
{
    /// <summary>Initializes the node.</summary>
    /// <param name="text">The text.</param>
    /// <param name="parent">The parent to attach to.</param>
    public TextDur(object text = null, Node parent = null)
        : base(text, parent)
    {
    }

    /// <inheritdoc/>
    public override string Ly(Printer printer)
    {
        string s = base.Ly(printer);
        Duration duration = FindChild<Duration>(1);
        return duration != null ? s + duration.Ly(printer) : s;
    }
}

/// <summary>A text node that claims its own line.</summary>
public class Line : Text
{
    /// <summary>Initializes the node.</summary>
    /// <param name="text">The text.</param>
    /// <param name="parent">The parent to attach to.</param>
    public Line(object text = null, Node parent = null)
        : base(text, parent)
    {
    }

    /// <inheritdoc/>
    protected override int DefaultBefore => 1;

    /// <inheritdoc/>
    protected override int DefaultAfter => 1;
}

/// <summary>A LilyPond comment at the end of a line.</summary>
public class Comment : Text
{
    /// <summary>Initializes the node.</summary>
    /// <param name="text">The text.</param>
    /// <param name="parent">The parent to attach to.</param>
    public Comment(object text = null, Node parent = null)
        : base(text, parent)
    {
    }

    /// <inheritdoc/>
    protected override int DefaultAfter => 1;

    /// <inheritdoc/>
    public override string Ly(Printer printer)
        => Regex.Replace(Format(TextValue), "^", "% ", RegexOptions.Multiline);
}

/// <summary>A LilyPond comment that takes a full line.</summary>
public class LineComment : Comment
{
    /// <summary>Initializes the node.</summary>
    /// <param name="text">The text.</param>
    /// <param name="parent">The parent to attach to.</param>
    public LineComment(object text = null, Node parent = null)
        : base(text, parent)
    {
    }

    /// <inheritdoc/>
    protected override int DefaultBefore => 1;
}

/// <summary>A block comment between <c>%{</c> and <c>%}</c>.</summary>
public class BlockComment : Comment
{
    /// <summary>Initializes the node.</summary>
    /// <param name="text">The text.</param>
    /// <param name="parent">The parent to attach to.</param>
    public BlockComment(object text = null, Node parent = null)
        : base(text, parent)
    {
    }

    /// <inheritdoc/>
    protected override int DefaultBefore => Format(TextValue).Contains('\n') ? 1 : 0;

    /// <inheritdoc/>
    protected override int DefaultAfter => Format(TextValue).Contains('\n') ? 1 : 0;

    /// <inheritdoc/>
    public override string Ly(Printer printer)
    {
        string text = Format(TextValue).Replace("%}", string.Empty);
        return text.Contains('\n') ? "%{\n" + text + "\n%}" : "%{ " + text + " %}";
    }
}

/// <summary>A string that is written inside double quotes.</summary>
public class QuotedString : Text
{
    /// <summary>Initializes the node.</summary>
    /// <param name="text">The text.</param>
    /// <param name="parent">The parent to attach to.</param>
    public QuotedString(object text = null, Node parent = null)
        : base(text, parent)
    {
    }

    /// <inheritdoc/>
    public override bool IsAtom => true;

    /// <inheritdoc/>
    public override string Ly(Printer printer) => printer.QuoteString(Format(TextValue));
}

/// <summary>A newline.</summary>
public class Newline : LyNode
{
    /// <summary>Initializes the node.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Newline(Node parent = null) => parent?.Append(this);

    /// <inheritdoc/>
    protected override int DefaultAfter => 1;
}

/// <summary>A blank line.</summary>
public class BlankLine : Newline
{
    /// <summary>Initializes the node.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public BlankLine(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    protected override int DefaultBefore => 1;
}

/// <summary>A scheme expression, written with the <c>#</c> prepended.</summary>
public class Scheme : Text
{
    /// <summary>Initializes the node.</summary>
    /// <param name="text">The text.</param>
    /// <param name="parent">The parent to attach to.</param>
    public Scheme(object text = null, Node parent = null)
        : base(text, parent)
    {
    }

    /// <inheritdoc/>
    public override bool IsAtom => true;

    /// <inheritdoc/>
    public override string Ly(Printer printer) => "#" + Format(TextValue);
}

/// <summary>A LilyPond <c>\version</c> instruction.</summary>
public class Version : Line
{
    /// <summary>Initializes the node.</summary>
    /// <param name="text">The version.</param>
    /// <param name="parent">The parent to attach to.</param>
    public Version(object text = null, Node parent = null)
        : base(text, parent)
    {
    }

    /// <inheritdoc/>
    public override string Ly(Printer printer)
        => string.Format(CultureInfo.InvariantCulture, "\\version \"{0}\"", Format(TextValue));
}

/// <summary>A LilyPond <c>\include</c> statement.</summary>
public class Include : Line
{
    /// <summary>Initializes the node.</summary>
    /// <param name="text">The file included.</param>
    /// <param name="parent">The parent to attach to.</param>
    public Include(object text = null, Node parent = null)
        : base(text, parent)
    {
    }

    /// <inheritdoc/>
    public override string Ly(Printer printer)
        => string.Format(CultureInfo.InvariantCulture, "\\include \"{0}\"", Format(TextValue));
}

/// <summary>
/// A <c>varname = value</c> construct, with the value as its first child. The
/// name may be a string or a <see cref="Reference"/>, so that every place the
/// variable is referenced shows the same name.
/// </summary>
public class Assignment : Container
{
    /// <summary>Initializes the node.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="parent">The parent to attach to.</param>
    /// <param name="value">The value node, if any.</param>
    public Assignment(object name = null, Node parent = null, LyNode value = null)
    {
        Name = name;
        parent?.Append(this);
        if (value != null) { Append(value); }
    }

    /// <summary>Gets or sets the variable name.</summary>
    public object Name { get; set; }

    /// <inheritdoc/>
    protected override int DefaultBefore => 1;

    /// <inheritdoc/>
    protected override int DefaultAfter => 1;

    /// <summary>Sets the assigned value.</summary>
    /// <param name="value">The value node.</param>
    public void SetValue(LyNode value)
    {
        if (Count > 0) { Replace(this[0], value); } else { Append(value); }
    }

    /// <summary>Answers the assigned value.</summary>
    /// <returns>The value node, or <see langword="null"/>.</returns>
    public LyNode Value() => Count > 0 ? (LyNode)this[0] : null;

    /// <inheritdoc/>
    public override string Ly(Printer printer)
        => Format(Name) + " = " + ContainerLy(printer);
}

/// <summary>An identifier, written as <c>\name</c>.</summary>
public class Identifier : Leaf
{
    /// <summary>Initializes the node.</summary>
    /// <param name="name">The name, a string or a <see cref="Reference"/>.</param>
    /// <param name="parent">The parent to attach to.</param>
    public Identifier(object name = null, Node parent = null)
    {
        Name = name;
        parent?.Append(this);
    }

    /// <summary>Gets or sets the name.</summary>
    public object Name { get; set; }

    /// <inheritdoc/>
    public override bool IsAtom => true;

    /// <inheritdoc/>
    public override string Ly(Printer printer) => "\\" + Format(Name);
}
