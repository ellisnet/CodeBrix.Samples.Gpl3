// === python-ly ly.music.items module (the Item base class) ===
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LyToken = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.Ly.Music; //was previously: ly/music/items.py (class Item);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.
//
// Three item classes are renamed with an "Item" suffix, because their upstream
// names collide with a member name or a ported type in this namespace:
// items.Token -> TokenItem, items.Duration -> DurationItem and
// items.String -> StringItem. Everything else keeps its upstream name.

/// <summary>
/// Any item in the music of a document — a bare token, or an interpreted
/// construct such as a note, a rest, or a sequential or simultaneous
/// expression.
/// <para>
/// Some items have one responsible token, others a list of them. Every item
/// knows the position it starts at, and (through
/// <see cref="EndPosition"/>) where it and its children end. Whitespace and
/// comments are left out of the tree.
/// </para>
/// </summary>
public class Item : WeakNode
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ReferenceProperties
        = new ConcurrentDictionary<Type, PropertyInfo[]>();

    /// <summary>Gets or sets the document this item was read from — set on the
    /// <see cref="Document"/> item, inherited in spirit by the rest.</summary>
    public DocumentBase SourceDocument { get; set; } //was previously: document

    /// <summary>Gets or sets the tokens the item is responsible for.</summary>
    public IReadOnlyList<LyToken> Tokens { get; set; } = Array.Empty<LyToken>();

    /// <summary>Gets or sets the token the expression starts with.</summary>
    public LyToken Token { get; set; }

    /// <summary>Gets or sets where the item starts in the source text.</summary>
    public int Position { get; set; } = -1;

    /// <summary>
    /// Answers a plain-text value for this node. Only items such as
    /// <see cref="Markup"/> or <see cref="StringItem"/> have one; everything
    /// else answers the empty string.
    /// </summary>
    /// <returns>The text.</returns>
    public virtual string PlainText() => string.Empty;

    /// <summary>Answers the position this node (with its children) ends at.</summary>
    /// <returns>The position.</returns>
    public int EndPosition()
    {
        int end;
        if (Tokens.Count > 0)
        {
            end = Tokens[Tokens.Count - 1].End;
        }
        else if (Token != null)
        {
            end = Token.End;
        }
        else
        {
            end = Position;
        }

        if (Count > 0)
        {
            //The end of the last child.
            end = Math.Max(end, ((Item)this[Count - 1]).EndPosition());
        }

        //The end of any Item or Token the node carries in a property, such as
        //a context id or an octave token — upstream reads vars(self) for this,
        //which is why a property declared as object still counts, and why the
        //start token counts even when the node also carries a token list.
        foreach (PropertyInfo property in ReferencePropertiesOf(GetType()))
        {
            object value = property.GetValue(this);
            if (value is Item item && !ReferenceEquals(item, this))
            {
                end = Math.Max(end, item.EndPosition());
            }
            else if (value is LyToken token)
            {
                end = Math.Max(end, token.End);
            }
        }

        return end;
    }

    /// <summary>Lets the events reader handle this node; answers the time.</summary>
    /// <param name="events">The events reader.</param>
    /// <param name="time">The time the node starts at.</param>
    /// <param name="scaling">The scaling in force.</param>
    /// <returns>The time after the node.</returns>
    public virtual Fraction Events(Events events, Fraction time, Fraction scaling) => time;

    /// <summary>Answers the musical duration of this node.</summary>
    /// <returns>The duration.</returns>
    public virtual Fraction Length() => Fraction.Zero;

    /// <summary>
    /// Yields the top-level items of the document node, backwards, starting
    /// just before the node this one descends from.
    /// </summary>
    /// <returns>The items.</returns>
    public IEnumerable<Item> IterToplevelItems()
    {
        Node node = this;
        Document document = null;
        foreach (Node ancestor in Ancestors())
        {
            if (ancestor is Document found)
            {
                document = found;
                break;
            }

            node = ancestor;
        }

        if (document == null) { yield break; }

        foreach (Node i in node.Backward()) { yield return (Item)i; }

        //Look in the parent document, before the place we were included.
        while (document.IncludeNode != null)
        {
            if (!(document.IncludeNode.Parent() is Document parent)) { break; }

            foreach (Node i in document.IncludeNode.Backward()) { yield return (Item)i; }

            document = parent;
        }
    }

    /// <summary>
    /// The same as <see cref="IterToplevelItems"/>, but following
    /// <c>\include</c> commands.
    /// </summary>
    /// <returns>The items.</returns>
    public IEnumerable<Item> IterToplevelItemsInclude() => Follow(IterToplevelItems());

    /// <summary>
    /// Walks up the parents until music is found, answering the OUTERMOST
    /// music node, or <see langword="null"/> when the node belongs to no music
    /// expression (a top-level markup or scheme object, say).
    /// </summary>
    /// <returns>The music node.</returns>
    public Item MusicParent()
    {
        Node node = this;
        bool music = node is Music;
        foreach (Node p in Ancestors())
        {
            bool parentMusic = p is Music;
            if (music && !parentMusic) { return (Item)node; }

            music = parentMusic;
            node = p;
        }

        return null;
    }

    /// <summary>
    /// Yields the children that are new music expressions, i.e. that sit
    /// inside other constructions.
    /// </summary>
    /// <param name="depth">How deep to look; -1 for no limit.</param>
    /// <returns>The music nodes.</returns>
    public IEnumerable<Item> MusicChildren(int depth = -1) => Find(this, depth);

    /// <summary>
    /// Answers whether this node has top-level music, markup, book and so on —
    /// i.e. whether LilyPond would likely generate output. Usually asked of a
    /// document, score, book part or book node.
    /// </summary>
    /// <returns>Whether it does.</returns>
    public bool HasOutput() => HasOutput(new HashSet<Item>());

    /// <summary>Answers a readable description of the item.</summary>
    /// <returns>The description.</returns>
    public override string ToString()
        => Token == null
            ? $"<{GetType().Name}>"
            : $"<{GetType().Name} '{Token.Text}'>";

    /// <summary>
    /// Answers whether this node has output, skipping the documents already
    /// visited so recursively nested includes terminate.
    /// </summary>
    /// <param name="seenDocuments">The documents already visited.</param>
    /// <returns>Whether it does.</returns>
    protected bool HasOutput(HashSet<Item> seenDocuments)
    {
        seenDocuments.Add(this);
        foreach (Node child in this)
        {
            if (child is Music || child is Markup) { return true; }

            if (child is Book || child is BookPart || child is Score)
            {
                if (((Item)child).HasOutput(seenDocuments)) { return true; }
            }
            else if (child is Include include)
            {
                Document document = (Toplevel() as Document)?.GetIncludedDocumentNode(include);
                if (document != null
                    && !seenDocuments.Contains(document)
                    && document.HasOutput(seenDocuments))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<Item> Find(Node node, int depth)
    {
        if (depth == 0) { yield break; }

        if (node is Music)
        {
            foreach (Node child in node)
            {
                foreach (Item found in Find(child, depth - 1)) { yield return found; }
            }
        }
        else
        {
            foreach (Node child in node)
            {
                if (child is Music music)
                {
                    yield return music;
                }
                else
                {
                    foreach (Item found in Find(child, depth - 1)) { yield return found; }
                }
            }
        }
    }

    private static IEnumerable<Item> Follow(IEnumerable<Item> items)
    {
        foreach (Item item in items)
        {
            if (item is Include include)
            {
                Document document =
                    (include.Parent() as Document)?.GetIncludedDocumentNode(include);
                if (document != null)
                {
                    IEnumerable<Item> reversed = Enumerable.Reverse(
                        Enumerable.Range(0, document.Count).Select(i => (Item)document[i]));
                    foreach (Item inner in Follow(reversed)) { yield return inner; }
                }
            }
            else
            {
                yield return item;
            }
        }
    }

    private static PropertyInfo[] ReferencePropertiesOf(Type type)
        => ReferenceProperties.GetOrAdd(
            type,
            t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead
                    && p.GetIndexParameters().Length == 0
                    && CanHoldReference(p.PropertyType))
                .ToArray());

    /// <summary>Whether a property could hold an item or a token — the
    /// declared type may be object, so the value is what decides.</summary>
    /// <param name="type">The declared type.</param>
    /// <returns>Whether it could.</returns>
    private static bool CanHoldReference(Type type)
        => type == typeof(object)
            || typeof(Item).IsAssignableFrom(type)
            || typeof(LyToken).IsAssignableFrom(type);
}

/// <summary>Traverses a music tree and records the music events in it.</summary>
public class Events //was previously: ly/music/event.py (class Events)
{
    /// <summary>Gets or sets whether repeats are unfolded while reading.</summary>
    public bool UnfoldRepeats { get; set; }

    /// <summary>Reads the events from a node and its children, from time zero
    /// at unit scaling.</summary>
    /// <param name="node">The node to read.</param>
    /// <returns>The time after the node.</returns>
    public Fraction Read(Item node) => Traverse(node, Fraction.Zero, Fraction.One);

    /// <summary>Reads the events from a node and its children.</summary>
    /// <param name="node">The node to read.</param>
    /// <param name="time">The time to start at.</param>
    /// <param name="scaling">The scaling in force.</param>
    /// <returns>The time after the node.</returns>
    public Fraction Read(Item node, Fraction time, Fraction scaling)
        => Traverse(node, time, scaling);

    /// <summary>Traverses a node, calling its event handler.</summary>
    /// <param name="node">The node to traverse.</param>
    /// <param name="time">The time the node starts at.</param>
    /// <param name="scaling">The scaling in force.</param>
    /// <returns>The time after the node.</returns>
    public Fraction Traverse(Item node, Fraction time, Fraction scaling)
        => node.Events(this, time, scaling);
}
