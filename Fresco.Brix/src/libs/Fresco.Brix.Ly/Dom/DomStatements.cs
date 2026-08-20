// === python-ly ly.dom module (statements, sections and contexts) ===
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

using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Ly.Dom; //was previously: ly/dom.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Base class for statements with arguments: the statement is the name, the
/// arguments are the children.
/// </summary>
public class Statement : Container
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Statement(Node parent = null)
        : base(parent)
    {
    }

    /// <summary>Gets or sets the statement's name.</summary>
    public virtual object Name { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override bool IsAtom => true;

    /// <summary>Gets zero: unlike a container, a statement does not take its
    /// leading newlines from its first child.</summary>
    public override int Before => 0;

    /// <inheritdoc/>
    public override string Ly(Printer printer)
        => "\\" + Format(Name) + " " + ContainerLy(printer);
}

/// <summary>A LilyPond command whose name is supplied when it is made.</summary>
public class Command : Statement
{
    /// <summary>Initializes the command.</summary>
    /// <param name="name">The command name.</param>
    /// <param name="parent">The parent to attach to.</param>
    public Command(object name, Node parent = null)
        : base(parent)
        => Name = name;
}

/// <summary>
/// Encloses all children between brackets. When
/// <see cref="MayRemoveBrackets"/> is set, the brackets are dropped for a
/// single child that is an atom.
/// </summary>
public class Enclosed : Container
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Enclosed(Node parent = null)
        : base(parent)
    {
    }

    /// <summary>Gets whether the brackets may be dropped.</summary>
    public virtual bool MayRemoveBrackets => false;

    /// <summary>Gets the opening bracket.</summary>
    public virtual string Pre => "{";

    /// <summary>Gets the closing bracket.</summary>
    public virtual string Post => "}";

    /// <inheritdoc/>
    public override int Before => 0;

    /// <inheritdoc/>
    public override int After => 0;

    /// <inheritdoc/>
    public override bool IsAtom => true;

    /// <inheritdoc/>
    public override string Ly(Printer printer) => EnclosedLy(printer);

    /// <summary>Answers the bracketed output of the children.</summary>
    /// <param name="printer">The printer.</param>
    /// <returns>The output.</returns>
    protected string EnclosedLy(Printer printer)
    {
        if (Count == 0) { return Pre + " " + Post; }

        string text = ContainerLy(printer);
        if (MayRemoveBrackets && Count == 1 && ((LyNode)this[0]).IsAtom) { return text; }

        if (ContainerBefore > 0 || ContainerAfter > 0 || text.Contains('\n'))
        {
            return Pre
                + new string('\n', System.Math.Max(ContainerBefore, 1))
                + text
                + new string('\n', System.Math.Max(ContainerAfter, 1))
                + Post;
        }

        return Pre + " " + text + " " + Post;
    }
}

/// <summary>A sequential music expression between <c>{ }</c>.</summary>
public class Seq : Enclosed
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Seq(Node parent = null)
        : base(parent)
    {
    }
}

/// <summary>A simultaneous music expression between <c>&lt;&lt; &gt;&gt;</c>.</summary>
public class Sim : Enclosed
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Sim(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override string Pre => "<<";

    /// <inheritdoc/>
    public override string Post => ">>";
}

/// <summary>A sequential expression whose brackets may be dropped.</summary>
public class Seqr : Seq
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Seqr(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override bool MayRemoveBrackets => true;
}

/// <summary>A simultaneous expression whose brackets may be dropped.</summary>
public class Simr : Sim
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Simr(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override bool MayRemoveBrackets => true;
}

/// <summary>A LilyPond expression between <c>#{</c> and <c>#}</c>, in scheme.</summary>
public class SchemeLily : Enclosed
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public SchemeLily(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override string Pre => "#{";

    /// <inheritdoc/>
    public override string Post => "#}";
}

/// <summary>A list of items enclosed in parentheses.</summary>
public class SchemeList : Enclosed
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public SchemeList(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override string Pre => "(";

    /// <inheritdoc/>
    public override string Post => ")";

    /// <inheritdoc/>
    public override string Ly(Printer printer) => Pre + ContainerLy(printer) + Post;
}

/// <summary>
/// Base class for the LilyPond commands that take a single bracket-enclosed
/// list of arguments.
/// </summary>
public class StatementEnclosed : Enclosed
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public StatementEnclosed(Node parent = null)
        : base(parent)
    {
    }

    /// <summary>Gets or sets the statement's name.</summary>
    public virtual object Name { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override bool MayRemoveBrackets => true;

    /// <inheritdoc/>
    public override string Ly(Printer printer)
        => "\\" + Format(Name) + " " + EnclosedLy(printer);
}

/// <summary>A LilyPond command with a bracket-enclosed argument list, whose
/// name is supplied when it is made.</summary>
public class CommandEnclosed : StatementEnclosed
{
    /// <summary>Initializes the command.</summary>
    /// <param name="name">The command name.</param>
    /// <param name="parent">The parent to attach to.</param>
    public CommandEnclosed(object name, Node parent = null)
        : base(parent)
        => Name = name;
}

/// <summary>
/// Base class for <c>\book { }</c>, <c>\score { }</c> and their relatives:
/// never removes the brackets and always starts on a new line.
/// </summary>
public class Section : StatementEnclosed
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Section(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override bool MayRemoveBrackets => false;

    /// <inheritdoc/>
    public override int Before => 1;

    /// <inheritdoc/>
    public override int After => 1;
}

/// <summary>
/// A section that handles unique variable assignments among its children —
/// upstream's <c>HandleVars</c> mixin. Setting a name creates (or replaces)
/// an <see cref="Assignment"/> child, wrapping a plain value in a
/// <see cref="QuotedString"/>.
/// </summary>
public class VariableSection : Section //was previously: the HandleVars mixin
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public VariableSection(Node parent = null)
        : base(parent)
    {
    }

    /// <summary>Gets or sets the assignment for a variable name.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The assignment, or <see langword="null"/> when unset.</returns>
    public LyNode this[string name]
    {
        get => FindAssignment(name);
        set => SetVariable(name, value);
    }

    /// <summary>Sets a variable to a value, wrapping a plain one in a quoted
    /// string.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value node, or <see langword="null"/>.</param>
    public void SetVariable(string name, LyNode value)
    {
        LyNode node = value ?? ImportNode(null);
        Assignment assignment = FindAssignment(name);
        if (assignment != null)
        {
            assignment.SetValue(node);
        }
        else
        {
            _ = new Assignment(name, this, node);
        }
    }

    /// <summary>Sets a variable to a text value.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The text.</param>
    public void SetVariable(string name, string value) => SetVariable(name, ImportNode(value));

    /// <summary>Answers whether a variable is set.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>Whether it is.</returns>
    public bool Contains(string name) => FindAssignment(name) != null;

    /// <summary>Removes a variable.</summary>
    /// <param name="name">The variable name.</param>
    public void RemoveVariable(string name)
    {
        Assignment assignment = FindAssignment(name);
        if (assignment != null) { Remove(assignment); }
    }

    /// <summary>Answers the assignment for a name, or <see langword="null"/>.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The assignment.</returns>
    public Assignment FindAssignment(string name)
        => FindChildren<Assignment>(1).FirstOrDefault(a => Format(a.Name) == name);

    /// <summary>Turns a plain value into the node it should be written as.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The node.</returns>
    protected virtual LyNode ImportNode(object value) => new QuotedString(value);
}

/// <summary>A <c>\book { }</c> construct.</summary>
public class Book : Section
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Book(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "book";
}

/// <summary>A <c>\bookpart { }</c> construct.</summary>
public class BookPart : Section
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public BookPart(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "bookpart";
}

/// <summary>A <c>\score { }</c> construct.</summary>
public class Score : Section
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Score(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "score";
}

/// <summary>A <c>\paper { }</c> construct.</summary>
public class Paper : VariableSection
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Paper(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "paper";
}

/// <summary>A <c>\layout { }</c> construct.</summary>
public class Layout : VariableSection
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Layout(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "layout";
}

/// <summary>A <c>\midi { }</c> construct.</summary>
public class Midi : VariableSection
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Midi(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "midi";
}

/// <summary>A <c>\header { }</c> construct.</summary>
public class Header : VariableSection
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Header(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "header";
}

/// <summary>A <c>\with { }</c> construct; prints nothing when it is empty.</summary>
public class With : VariableSection
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public With(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "with";

    /// <inheritdoc/>
    public override int Before => 0;

    /// <inheritdoc/>
    public override int After => 0;

    /// <inheritdoc/>
    public override string Ly(Printer printer)
        => Count > 0 ? base.Ly(printer) : string.Empty;
}

/// <summary>A context name, written as <c>\Score</c>.</summary>
public class ContextName : Text
{
    /// <summary>Initializes the node.</summary>
    /// <param name="text">The context name.</param>
    /// <param name="parent">The parent to attach to.</param>
    public ContextName(object text = null, Node parent = null)
        : base(text, parent)
    {
    }

    /// <inheritdoc/>
    public override string Ly(Printer printer) => "\\" + Format(TextValue);
}

/// <summary>A <c>\context</c> section, for use inside a layout or midi block.</summary>
public class ContextSection : VariableSection //was previously: dom.Context
{
    /// <summary>Initializes the section.</summary>
    /// <param name="contextName">The context name, if any.</param>
    /// <param name="parent">The parent to attach to.</param>
    public ContextSection(string contextName = "", Node parent = null)
        : base(parent)
    {
        if (!string.IsNullOrEmpty(contextName)) { _ = new ContextName(contextName, this); }
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "context";
}

/// <summary>
/// A <c>\new</c> or <c>\context</c> music context, e.g.
/// <c>\new Staff = 'bla' \with { } &lt;&lt; music &gt;&gt;</c>. A
/// <see cref="With"/> element is added as the first child by the convenience
/// methods; an empty one prints nothing.
/// </summary>
public class ContextType : Container
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public ContextType(object contextId = null, bool isNew = true, Node parent = null)
    {
        ContextId = contextId;
        IsNew = isNew;
        parent?.Append(this);
    }

    /// <summary>Gets or sets whether the context is a <c>\new</c> one.</summary>
    public bool IsNew { get; set; } //was previously: new

    /// <summary>Gets or sets the context id.</summary>
    public object ContextId { get; set; } //was previously: cid

    /// <summary>Gets the context type name written; the class name when
    /// unset.</summary>
    public virtual string ContextTypeName => null; //was previously: ctype

    /// <inheritdoc/>
    public override int Before => 1;

    /// <inheritdoc/>
    public override int After => 1;

    /// <inheritdoc/>
    public override bool IsAtom => true;

    /// <inheritdoc/>
    public override string Ly(Printer printer)
    {
        var result = new List<string> { IsNew ? "\\new" : "\\context" };
        result.Add(ContextTypeName ?? GetType().Name);
        if (ContextId != null && Format(ContextId).Length > 0)
        {
            result.Add("=");
            result.Add(printer.QuoteString(Format(ContextId)));
        }

        result.Add(ContainerLy(printer));
        return string.Join(" ", result);
    }

    /// <summary>Answers the attached with-clause, creating it when absent.</summary>
    /// <returns>The clause.</returns>
    public With GetWith()
    {
        foreach (Node node in this)
        {
            if (node is With found) { return found; }
        }

        Insert(0, new With());
        return (With)this[0];
    }

    /// <summary>
    /// Adds the instrument-name engraver when this context would need it to
    /// print instrument names.
    /// </summary>
    public void AddInstrumentNameEngraverIfNecessary()
    {
        if (this is Staff || this is RhythmicStaff || this is PianoStaff
            || this is Lyrics || this is FretBoards)
        {
            return;
        }

        _ = new Line("\\consists \"Instrument_name_engraver\"", GetWith());
    }
}
