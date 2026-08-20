// This file is part of python-ly, https://pypi.python.org/pypi/python-ly
//
// Copyright (c) 2008 - 2015 by Wilbert Berendsen
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation, either version 3
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

namespace Fresco.Brix.Ly; //was previously: ly/node.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A list-like tree node: children with list access, a reference to the parent,
/// and re-parenting on append — the simple DOM the ly.dom document builder is
/// built over. Inherit to make meaningful node types.
/// </summary>
public class Node : IEnumerable<Node>
{
    private Node _parent;
    private List<Node> _children = new List<Node>();

    /// <summary>Initializes a node, optionally appending it to a parent.</summary>
    /// <param name="parent">The parent to append to, or <see langword="null"/>.</param>
    public Node(Node parent = null)
    {
        if (parent != null)
        {
            parent.Append(this);
        }
    }

    /// <summary>Returns the parent, or <see langword="null"/>.</summary>
    /// <returns>The parent node.</returns>
    public virtual Node Parent() => _parent;

    /// <summary>(Internal) Sets the node (or <see langword="null"/>) as our parent.</summary>
    /// <param name="node">The new parent.</param>
    protected virtual void SetParent(Node node) => _parent = node;

    private void Own(Node node)
    {
        Node parent = node.Parent();
        if (parent != null)
        {
            parent.Remove(node);
        }

        node.SetParent(this);
    }

    /// <summary>Returns the index of the given child node.</summary>
    /// <param name="node">The child.</param>
    /// <returns>The index.</returns>
    public int Index(Node node) => _children.IndexOf(node);

    /// <summary>Appends a node, re-parenting it away from any former parent.</summary>
    /// <param name="node">The node to append.</param>
    public void Append(Node node)
    {
        Own(node);
        _children.Add(node);
    }

    /// <summary>Appends every node from the iterable.</summary>
    /// <param name="nodes">The nodes to append.</param>
    public void Extend(IEnumerable<Node> nodes)
    {
        foreach (Node node in nodes)
        {
            Append(node);
        }
    }

    /// <summary>Inserts a node at the specified index.</summary>
    /// <param name="index">The index.</param>
    /// <param name="node">The node to insert.</param>
    public void Insert(int index, Node node)
    {
        Own(node);
        _children.Insert(index, node);
    }

    /// <summary>Inserts a node before another node.</summary>
    /// <param name="other">The child to insert before.</param>
    /// <param name="node">The node to insert.</param>
    public void InsertBefore(Node other, Node node)
    {
        int index = Index(other);
        Own(node);
        _children.Insert(index, node);
    }

    /// <summary>Removes the given child node.</summary>
    /// <param name="node">The child to remove.</param>
    public void Remove(Node node)
    {
        _children.Remove(node);
        node.SetParent(null);
    }

    /// <summary>Gets the number of children.</summary>
    public int Count => _children.Count;

    /// <summary>Gets or sets the child at an index; setting re-parents both
    /// the old and the new child.</summary>
    /// <param name="index">The index.</param>
    public Node this[int index]
    {
        get => _children[index];
        set
        {
            _children[index].SetParent(null);
            _children[index] = value;
            Own(value);
        }
    }

    /// <summary>Removes the child at an index.</summary>
    /// <param name="index">The index.</param>
    public void RemoveAt(int index)
    {
        _children[index].SetParent(null);
        _children.RemoveAt(index);
    }

    /// <summary>Returns whether the node is our child.</summary>
    /// <param name="node">The node to look for.</param>
    /// <returns>Whether it is a child.</returns>
    public bool Contains(Node node) => _children.Contains(node);

    /// <summary>Removes all children (without recursing — upstream's clear).</summary>
    public void Clear()
    {
        foreach (Node node in _children)
        {
            node.SetParent(null);
        }

        _children.Clear();
    }

    /// <summary>Removes all children and unlinks them recursively. Unlike
    /// <see cref="Clear"/> the children keep their (stale) parent pointer —
    /// upstream's unlink deletes the list without re-parenting.</summary>
    public void Unlink()
    {
        foreach (Node node in _children)
        {
            node.Unlink();
        }

        _children.Clear();
    }

    /// <summary>Replaces a child node with another node.</summary>
    /// <param name="old">The child to replace.</param>
    /// <param name="replacement">The new child.</param>
    public void Replace(Node old, Node replacement)
    {
        int index = Index(old);
        this[index] = replacement;
    }

    /// <summary>Sorts the children with a comparison.</summary>
    /// <param name="comparison">The comparison.</param>
    /// <param name="reverse">Whether to reverse the order afterwards.</param>
    public void Sort(Comparison<Node> comparison, bool reverse = false)
    {
        _children.Sort(comparison);
        if (reverse)
        {
            _children.Reverse();
        }
    }

    /// <summary>Returns a deep copy of the node and its children. Attributes a
    /// subclass carries are copied shallowly (a memberwise clone stands in for
    /// upstream's copy-the-public-attributes loop).</summary>
    /// <returns>The copy.</returns>
    public virtual Node Copy()
    {
        Node copy = (Node)MemberwiseClone();
        copy._parent = null;
        copy._children = new List<Node>();
        foreach (Node child in _children)
        {
            copy.Append(child.Copy());
        }

        return copy;
    }

    /// <summary>Climbs the tree up over the parents.</summary>
    /// <returns>The ancestors, nearest first.</returns>
    public IEnumerable<Node> Ancestors()
    {
        Node node = Parent();
        while (node != null)
        {
            yield return node;
            node = node.Parent();
        }
    }

    /// <summary>Returns the sibling just before us, or <see langword="null"/>.</summary>
    /// <returns>The previous sibling.</returns>
    public Node PreviousSibling()
    {
        foreach (Node node in Backward())
        {
            return node;
        }

        return null;
    }

    /// <summary>Returns the sibling just after us, or <see langword="null"/>.</summary>
    /// <returns>The next sibling.</returns>
    public Node NextSibling()
    {
        foreach (Node node in Forward())
        {
            return node;
        }

        return null;
    }

    /// <summary>Iterates backwards over the preceding siblings.</summary>
    /// <returns>The siblings, nearest first; empty without a parent.</returns>
    public IEnumerable<Node> Backward()
    {
        Node parent = Parent();
        if (parent != null)
        {
            int index = parent.Index(this);
            for (int i = index - 1; i >= 0; i--)
            {
                yield return parent[i];
            }
        }
    }

    /// <summary>Iterates over the following siblings.</summary>
    /// <returns>The siblings; empty without a parent.</returns>
    public IEnumerable<Node> Forward()
    {
        Node parent = Parent();
        if (parent != null)
        {
            int index = parent.Index(this);
            for (int i = index + 1; i < parent.Count; i++)
            {
                yield return parent[i];
            }
        }
    }

    /// <summary>Returns whether this node descends from the given parent.</summary>
    /// <param name="parent">The candidate ancestor.</param>
    /// <returns>Whether it is an ancestor.</returns>
    public bool IsDescendantOf(Node parent)
    {
        foreach (Node node in Ancestors())
        {
            if (ReferenceEquals(node, parent))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns the toplevel parent node of this node.</summary>
    /// <returns>The root.</returns>
    public Node Toplevel()
    {
        Node node = this;
        Node parent = Parent();
        while (parent != null)
        {
            node = parent;
            parent = node.Parent();
        }

        return node;
    }

    /// <summary>Yields all descendants, in tree order.</summary>
    /// <param name="depth">The depth to restrict to; -1 is unrestricted.</param>
    /// <returns>The descendants.</returns>
    public IEnumerable<Node> Descendants(int depth = -1) => IterDepth(depth);

    /// <summary>Iterates over children, their children, etc, depth-first.</summary>
    /// <param name="depth">The depth to restrict to; -1 is unrestricted.</param>
    /// <returns>The descendants.</returns>
    public IEnumerable<Node> IterDepth(int depth = -1)
    {
        if (depth != 0)
        {
            foreach (Node child in _children)
            {
                yield return child;
                foreach (Node descendant in child.IterDepth(depth - 1))
                {
                    yield return descendant;
                }
            }
        }
    }

    /// <summary>Iterates over the children in rings, closest descendants
    /// first.</summary>
    /// <param name="depth">The depth to restrict to; -1 is unrestricted.</param>
    /// <returns>The descendants, breadth-first.</returns>
    public IEnumerable<Node> IterRings(int depth = -1)
    {
        List<Node> children = new List<Node>(_children);
        while (children.Count > 0 && depth != 0)
        {
            depth -= 1;
            List<Node> next = new List<Node>();
            foreach (Node child in children)
            {
                yield return child;
                next.AddRange(child._children);
            }

            children = next;
        }
    }

    /// <summary>Yields all descendants that are an instance of a class,
    /// depth-first.</summary>
    /// <typeparam name="T">The class to find.</typeparam>
    /// <param name="depth">The depth to restrict to; -1 is unrestricted.</param>
    /// <returns>The matching descendants.</returns>
    public IEnumerable<T> Find<T>(int depth = -1)
        where T : Node
    {
        foreach (Node node in IterDepth(depth))
        {
            if (node is T found)
            {
                yield return found;
            }
        }
    }

    /// <summary>Yields all matching descendants, closest rings first.</summary>
    /// <typeparam name="T">The class to find.</typeparam>
    /// <param name="depth">The depth to restrict to; -1 is unrestricted.</param>
    /// <returns>The matching descendants.</returns>
    public IEnumerable<T> FindChildren<T>(int depth = -1)
        where T : Node
    {
        foreach (Node node in IterRings(depth))
        {
            if (node is T found)
            {
                yield return found;
            }
        }
    }

    /// <summary>Returns the first matching descendant, closest rings first, or
    /// <see langword="null"/>.</summary>
    /// <typeparam name="T">The class to find.</typeparam>
    /// <param name="depth">The depth to restrict to; -1 is unrestricted.</param>
    /// <returns>The descendant or <see langword="null"/>.</returns>
    public T FindChild<T>(int depth = -1)
        where T : Node
    {
        foreach (Node node in IterRings(depth))
        {
            if (node is T found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Finds an ancestor of a class, or <see langword="null"/>.</summary>
    /// <typeparam name="T">The class to find.</typeparam>
    /// <returns>The ancestor or <see langword="null"/>.</returns>
    public T FindParent<T>()
        where T : Node
    {
        foreach (Node node in Ancestors())
        {
            if (node is T found)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>Returns a string representation of the tree, one node per
    /// line, indented by depth.</summary>
    /// <returns>The dump.</returns>
    public string Dump()
    {
        List<string> lines = new List<string>();
        void Line(Node node, int indent)
        {
            lines.Add(new string(' ', indent * 2) + node);
            foreach (Node child in node._children)
            {
                Line(child, indent + 1);
            }
        }

        Line(this, 0);
        return string.Join("\n", lines);
    }

    /// <summary>Gets the enumerator over the children.</summary>
    /// <returns>The enumerator.</returns>
    public IEnumerator<Node> GetEnumerator() => _children.GetEnumerator();

    /// <summary>Gets the non-generic enumerator.</summary>
    /// <returns>The enumerator.</returns>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>A node type using a weak reference to the parent, so no cyclic
/// references are created.</summary>
public class WeakNode : Node
{
    private WeakReference<Node> _weakParent;

    /// <summary>Initializes a node, optionally appending it to a parent.</summary>
    /// <param name="parent">The parent to append to, or <see langword="null"/>.</param>
    public WeakNode(Node parent = null)
        : base(parent)
    {
    }

    /// <summary>(Internal) Keeps the parent weakly.</summary>
    /// <param name="node">The new parent.</param>
    protected override void SetParent(Node node)
        => _weakParent = node == null ? null : new WeakReference<Node>(node);

    /// <summary>Returns the parent, or <see langword="null"/> (also when the
    /// weakly-held parent has been collected).</summary>
    /// <returns>The parent node.</returns>
    public override Node Parent()
        => _weakParent != null && _weakParent.TryGetTarget(out Node parent)
            ? parent
            : null;
}
