// === python-ly ly.music.items module (class Document) ===
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
using System.IO;
using System.Linq;

namespace Fresco.Brix.Ly.Music; //was previously: ly/music/items.py (class Document);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>A top-level item representing a whole document's music tree.</summary>
public class Document : Item
{
    /// <summary>Reads a document into a music tree.</summary>
    /// <param name="document">The document to read.</param>
    public Document(DocumentBase document)
    {
        SourceDocument = document;
        var cursor = new Cursor(document);
        var source = new Source(cursor, stateFromDocument: true, tokensWithPosition: true);
        Extend(new Reader(source).Read());
    }

    /// <summary>Gets or sets the include node this document was read for.</summary>
    public Include IncludeNode { get; set; }

    /// <summary>Gets or sets the directories includes are searched in.</summary>
    public IReadOnlyList<string> IncludePath { get; set; } = new List<string>();

    /// <summary>Gets or sets whether includes resolve relative to the document.</summary>
    public bool RelativeIncludes { get; set; } = true;

    /// <summary>Answers the node at, or just before, a position.</summary>
    /// <param name="position">The position.</param>
    /// <param name="depth">How deep to descend; -1 for no limit.</param>
    /// <returns>The node.</returns>
    public Item NodeAt(int position, int depth = -1) //was previously: node()
        => Bisect(this, position, depth);

    /// <summary>
    /// Answers the music events leading up to a position, as (parent, nodes,
    /// scaling) triples. An empty list means there is no music expression
    /// there.
    /// </summary>
    /// <param name="position">The position.</param>
    /// <returns>The triples.</returns>
    public IReadOnlyList<(Item Parent, IReadOnlyList<Item> Nodes, Fraction Scaling)>
        MusicEventsTilPosition(int position)
    {
        Item node = NodeAt(position);

        //Be nice and allow including an assignment.
        if (node is Assignment assignment
            && ReferenceEquals(assignment.Parent(), this)
            && assignment.Value() is Music)
        {
            return new[]
            {
                ((Item)assignment, (IReadOnlyList<Item>)new List<Item>(), Fraction.One),
            };
        }

        if (node.Parent() is Chord chord) { node = chord; }

        var result = new List<(Item, IReadOnlyList<Item>, Fraction)>();
        bool music = node is Music || node is Durable;
        if (music)
        {
            result.Add((node, new List<Item>(), Fraction.One));
        }

        foreach (Node ancestor in node.Ancestors())
        {
            var parent = (Item)ancestor;
            bool parentMusic = parent is Music;
            int end = node.EndPosition();
            if (parentMusic)
            {
                var parentAsMusic = (Music)parent;
                if (position > end)
                {
                    (IReadOnlyList<Item> nodes, Fraction scaling) =
                        parentAsMusic.Preceding((Item)node.NextSibling());
                    result = new List<(Item, IReadOnlyList<Item>, Fraction)>
                    {
                        (parent, nodes, scaling),
                    };
                }
                else if (position == end)
                {
                    (IReadOnlyList<Item> nodes, Fraction scaling) =
                        parentAsMusic.Preceding(node);
                    var including = new List<Item>(nodes) { node };
                    result = new List<(Item, IReadOnlyList<Item>, Fraction)>
                    {
                        (parent, including, scaling),
                    };
                }
                else
                {
                    (IReadOnlyList<Item> nodes, Fraction scaling) =
                        parentAsMusic.Preceding(node);
                    result.Add((parent, nodes, scaling));
                }
            }
            else if (music)
            {
                //We are at the musical top.
                if (position > end)
                {
                    return new List<(Item, IReadOnlyList<Item>, Fraction)>();
                }

                if (position == end)
                {
                    result = new List<(Item, IReadOnlyList<Item>, Fraction)>
                    {
                        (parent, new List<Item> { node }, Fraction.One),
                    };
                }
                else
                {
                    result.Add((parent, new List<Item>(), Fraction.One));
                }

                node = parent;
                break;
            }

            node = parent;
            music = parentMusic;
        }

        result.Reverse();
        return result;
    }

    /// <summary>
    /// Answers the time position in the music at a cursor position, or
    /// <see langword="null"/> when the position is not inside a music
    /// expression.
    /// </summary>
    /// <param name="position">The cursor position.</param>
    /// <returns>The time.</returns>
    public Fraction? TimePosition(int position)
    {
        IReadOnlyList<(Item Parent, IReadOnlyList<Item> Nodes, Fraction Scaling)> events
            = MusicEventsTilPosition(position);
        if (events.Count == 0) { return null; }

        var reader = new Events();
        Fraction time = Fraction.Zero;
        Fraction scaling = Fraction.One;
        foreach ((Item _, IReadOnlyList<Item> nodes, Fraction s) in events)
        {
            scaling *= s;
            foreach (Item n in nodes) { time = reader.Traverse(n, time, scaling); }
        }

        return time;
    }

    /// <summary>
    /// Answers the length of the music between two positions, or
    /// <see langword="null"/> when they are not in the same expression.
    /// </summary>
    /// <param name="start">The first position.</param>
    /// <param name="end">The second position.</param>
    /// <returns>The length.</returns>
    public Fraction? TimeLength(int start, int end)
    {
        if (start > end) { (start, end) = (end, start); }

        IReadOnlyList<(Item Parent, IReadOnlyList<Item> Nodes, Fraction Scaling)> startEvents
            = MusicEventsTilPosition(start);
        if (startEvents.Count == 0) { return null; }

        IReadOnlyList<(Item Parent, IReadOnlyList<Item> Nodes, Fraction Scaling)> endEvents
            = MusicEventsTilPosition(end);
        if (endEvents.Count == 0
            || !ReferenceEquals(startEvents[0].Parent, endEvents[0].Parent))
        {
            return null;
        }

        //The same top-level expression: flatten both and traverse the shared
        //prefix only once.
        List<(Item Node, Fraction Scaling)> startList = Flatten(startEvents);
        List<(Item Node, Fraction Scaling)> endList = Flatten(endEvents);

        var reader = new Events();
        Fraction time = Fraction.Zero;
        int i = 0;
        for (; i < startList.Count && i < endList.Count; i++)
        {
            if (!ReferenceEquals(startList[i].Node, endList[i].Node)) { break; }

            time = reader.Traverse(startList[i].Node, time, startList[i].Scaling);
        }

        Fraction endTime = time;
        for (int n = i; n < startList.Count; n++)
        {
            time = reader.Traverse(startList[n].Node, time, startList[n].Scaling);
        }

        for (int n = i; n < endList.Count; n++)
        {
            endTime = reader.Traverse(endList[n].Node, endTime, endList[n].Scaling);
        }

        return endTime - time;
    }

    /// <summary>
    /// Answers the node that replaces a node — a variable reference answers
    /// its value, an include answers its document. Answers
    /// <see langword="null"/> when the node is not substitutable, and the node
    /// itself when the substitution failed.
    /// </summary>
    /// <param name="node">The node to substitute.</param>
    /// <returns>The replacement.</returns>
    public Item SubstituteForNode(Item node)
    {
        if (node is UserCommand command)
        {
            Item value = command.Value();
            if (value != null) { return SubstituteForNode(value) ?? value; }

            return node;
        }

        if (node is Include include)
        {
            return (Item)GetIncludedDocumentNode(include) ?? node;
        }

        return null;
    }

    /// <summary>
    /// Iterates over the music, following references to other assignments.
    /// </summary>
    /// <param name="node">The node to start at, or <see langword="null"/> for
    /// the whole document.</param>
    /// <returns>The nodes.</returns>
    public IEnumerable<Item> IterMusic(Item node = null)
    {
        foreach (Node child in (Node)node ?? this)
        {
            Item n = SubstituteForNode((Item)child) ?? (Item)child;
            yield return n;
            foreach (Item inner in IterMusic(n)) { yield return inner; }
        }
    }

    /// <summary>Answers the music document an include resolves to.</summary>
    /// <param name="node">The include node.</param>
    /// <returns>The document, or <see langword="null"/>.</returns>
    public Document GetIncludedDocumentNode(Include node)
    {
        if (node.IncludeResolved) { return node.IncludedDocument; }

        node.IncludeResolved = true;
        node.IncludedDocument = null;
        string filename = node.Filename();
        if (!string.IsNullOrEmpty(filename))
        {
            string resolved = ResolveFilename(filename);
            if (resolved != null)
            {
                Document documentNode = GetMusic(resolved);
                documentNode.IncludeNode = node;
                documentNode.IncludePath = IncludePath;
                node.IncludedDocument = documentNode;
            }
        }

        return node.IncludedDocument;
    }

    /// <summary>Resolves a file name against this document and the include path.</summary>
    /// <param name="filename">The name to resolve.</param>
    /// <returns>The full path, or <see langword="null"/>.</returns>
    public string ResolveFilename(string filename)
    {
        if (Path.IsPathRooted(filename)) { return filename; }

        var path = new List<string>(IncludePath);
        if (!string.IsNullOrEmpty(SourceDocument?.Filename))
        {
            string baseDirectory = Path.GetDirectoryName(SourceDocument.Filename);
            path.Remove(baseDirectory);
            path.Insert(0, baseDirectory);
        }

        foreach (string directory in path)
        {
            string full = Path.Combine(directory ?? string.Empty, filename);
            if (File.Exists(full)) { return full; }
        }

        return null;
    }

    /// <summary>
    /// Answers the music document for a file. This implementation loads it as
    /// UTF-8; override to load differently or to cache.
    /// </summary>
    /// <param name="filename">The file to load.</param>
    /// <returns>The document.</returns>
    public virtual Document GetMusic(string filename)
        => new Document(Ly.Document.Load(filename));

    private static List<(Item Node, Fraction Scaling)> Flatten(
        IReadOnlyList<(Item Parent, IReadOnlyList<Item> Nodes, Fraction Scaling)> events)
    {
        var result = new List<(Item, Fraction)>();
        Fraction scaling = Fraction.One;
        foreach ((Item _, IReadOnlyList<Item> nodes, Fraction s) in events)
        {
            scaling *= s;
            foreach (Item n in nodes) { result.Add((n, scaling)); }
        }

        return result;
    }

    private static Item Bisect(Item node, int position, int depth)
    {
        int end = node.Count;
        if (depth == 0 || end == 0) { return node; }

        int pos = 0;
        while (pos < end)
        {
            int mid = (pos + end) / 2;
            if (position < ((Item)node[mid]).Position) { end = mid; } else { pos = mid + 1; }
        }

        pos -= 1;
        if (pos < 0) { return node; }

        var child = (Item)node[pos];
        if (child.Position == position) { return child; }

        if (child.Position > position) { return node; }

        return Bisect(child, position, depth - 1);
    }
}

/// <summary>The entry point of the music reading api.</summary>
public static class MusicReader
{
    /// <summary>Answers the music tree of a document.</summary>
    /// <param name="document">The document to read.</param>
    /// <returns>The music document node.</returns>
    public static Document ReadDocument(DocumentBase document) //was previously: ly.music.document()
        => new Document(document);
}
