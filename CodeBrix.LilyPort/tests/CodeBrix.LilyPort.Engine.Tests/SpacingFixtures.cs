// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The canonical builders for spacing-test fixtures.
/// <para>
/// The line-breaking entry points read a candidate line's FIRST spring off the
/// starting column's RIGHT prebroken piece and its LAST spring off the following
/// column's LEFT prebroken piece — never off the originals. A fixture of plain,
/// un-prebroken columns therefore feeds the solver silent default springs, and every
/// force comes out wrong in a way that looks exactly like a solver bug. The real
/// pipeline never sees that state because <see cref="SystemGrob.PreProcessing"/>
/// prebreaks first.
/// </para>
/// <para>
/// <see cref="PrebrokenChain"/> builds fixtures the way the pipeline does — through
/// the real <see cref="SystemGrob.PreProcessing"/> — and is the fixture for anything
/// that reaches <see cref="LineSpacing"/>. <see cref="PlainChain"/> exists only for
/// spring and rod ACCOUNTING tests that never run a solver; the solvers report a
/// programming error when handed its columns. The full story is in PORT-COVERAGE.txt
/// under "A TEST-FIXTURE TRAP WORTH KNOWING".
/// </para>
/// </summary>
internal static class SpacingFixtures
{
    private static Symbol Sym(string name) => Symbol.Intern(name);

    private static object Alist(params (string Key, object Value)[] entries)
    {
        object result = Nil.Instance;
        for (int i = entries.Length - 1; i >= 0; i--)
        {
            result = new Pair(new Pair(Sym(entries[i].Key), entries[i].Value), result);
        }

        return result;
    }

    private static object GrobBasics(params (string Key, object Value)[] extra)
    {
        List<(string, object)> entries = new List<(string, object)>
        {
            ("meta", Alist(("name", Sym("TestGrob")), ("interfaces", Nil.Instance))),
        };
        entries.AddRange(extra);
        return Alist(entries.ToArray());
    }

    /// <summary>Builds a system ready to hold columns.</summary>
    internal static SystemGrob NewSystem()
    {
        SystemGrob system = new SystemGrob(GrobBasics(("axes", Pair.List(0L, 1L))));
        system.Layout = new OutputDef();
        return system;
    }

    /// <summary>
    /// Builds a chain of breakable columns joined by springs on the ORIGINAL columns
    /// only, with no prebroken pieces.
    /// <para>
    /// For spring and rod accounting tests only. Never hand these columns to the
    /// <see cref="LineSpacing"/> solvers: their break columns have no prebroken
    /// pieces, so the solvers fall back to default springs — and say so with a
    /// programming error.
    /// </para>
    /// </summary>
    internal static List<PaperColumn> PlainChain(int count, double ideal, double minimum)
    {
        SystemGrob system = NewSystem();
        List<PaperColumn> columns = new List<PaperColumn>();

        for (int i = 0; i < count; i++)
        {
            PaperColumn column = new PaperColumn(
                GrobBasics(("line-break-permission", Sym("allow"))));
            system.TypesetGrob(column);
            system.AddColumn(column);
            columns.Add(column);
        }

        for (int i = 0; i + 1 < count; i++)
        {
            SpaceableGrob.AddSpring(columns[i], columns[i + 1], new Spring(ideal, minimum));
        }

        return columns;
    }

    /// <summary>
    /// Builds a chain of breakable columns in the state the real pipeline hands to
    /// the spacing solvers: prebroken through the real
    /// <see cref="SystemGrob.PreProcessing"/>, with every spring registered from the
    /// prebroken pieces as well as the originals.
    /// </summary>
    internal static List<PaperColumn> PrebrokenChain(int count, double ideal, double minimum)
    {
        SystemGrob system = NewSystem();
        List<PaperColumn> columns = new List<PaperColumn>();

        for (int i = 0; i < count; i++)
        {
            PaperColumn column = new PaperColumn(
                GrobBasics(("line-break-permission", Sym("allow")), ("non-musical", true)));

            // AddColumn alone does not put a column in the system's element list,
            // and PreProcessing walks the element list — both calls are needed.
            system.TypesetGrob(column);
            system.AddColumn(column);
            columns.Add(column);
        }

        system.PreProcessing();

        for (int i = 0; i + 1 < count; i++)
        {
            RegisterSpring(columns[i], columns[i + 1], new Spring(ideal, minimum));
        }

        return columns;
    }

    /// <summary>
    /// Registers a spring between two columns from every piece the solvers can read
    /// it off. A line STARTER is the left column's RIGHT prebroken piece and a line
    /// ENDER the right column's LEFT one, so all combinations with the originals are
    /// registered — the state a real score is in once the spacing engraver has run
    /// over prebroken columns.
    /// </summary>
    internal static void RegisterSpring(PaperColumn left, PaperColumn right, Spring spring)
    {
        foreach (PaperColumn from in new[] { left, left.FindPrebrokenPiece(Direction.Positive) })
        {
            if (from == null)
            {
                continue;
            }

            foreach (PaperColumn to in new[] { right, right.FindPrebrokenPiece(Direction.Negative) })
            {
                if (to != null)
                {
                    SpaceableGrob.AddSpring(from, to, spring);
                }
            }
        }
    }

    /// <summary>Builds a bare grob to hang spacing properties off.</summary>
    internal static Grob NewSpacingGrob(params (string Key, object Value)[] properties)
        => new Item(GrobBasics(properties));

    /// <summary>
    /// Builds two columns, each holding one item, with the system parenting the real
    /// pipeline gives them — which is what <see cref="Rod.AddToColumns"/> walks.
    /// </summary>
    internal static (PaperColumn Left, PaperColumn Right, Item LeftItem, Item RightItem)
        TwoColumnsWithItems()
    {
        SystemGrob system = NewSystem();
        PaperColumn left = NewColumn(system);
        PaperColumn right = NewColumn(system);
        return (left, right, AddItemTo(left), AddItemTo(right));
    }

    /// <summary>Adds an item to a column, parented the way the pipeline parents it.</summary>
    internal static Item AddItemTo(PaperColumn column)
    {
        Item item = new Item(GrobBasics());
        item.XParent = column;
        SeparationItem.AddItem(column, item);
        return item;
    }

    /// <summary>Builds two breakable columns a measure apart.</summary>
    internal static (PaperColumn Left, PaperColumn Right) TwoBreakableColumns(Moment measureLength)
    {
        SystemGrob system = NewSystem();
        PaperColumn left = NewColumn(system, ("line-break-permission", Sym("allow")));
        PaperColumn right = NewColumn(system, ("line-break-permission", Sym("allow")));
        left.SetProperty(Sym("measure-length"), measureLength);
        return (left, right);
    }

    /// <summary>Builds two non-breakable columns stamped with the given moments.</summary>
    internal static (PaperColumn Left, PaperColumn Right) TwoColumnsAtMoments(
        Moment leftWhen, Moment rightWhen)
    {
        SystemGrob system = NewSystem();
        PaperColumn left = NewColumn(system);
        PaperColumn right = NewColumn(system);
        left.SetProperty(Sym("when"), leftWhen);
        right.SetProperty(Sym("when"), rightWhen);
        return (left, right);
    }

    /// <summary>
    /// Builds two musical columns: the left one carries the duration that RULES the
    /// spacing between them, which is the shortest note still sounding rather than the
    /// shortest note starting there.
    /// </summary>
    internal static (PaperColumn Left, PaperColumn Right) TwoMusicalColumns(
        Rational ruling, Moment leftWhen, Moment rightWhen)
    {
        SystemGrob system = NewSystem();
        PaperColumn left = NewColumn(system);
        PaperColumn right = NewColumn(system);
        left.SetProperty(Sym("shortest-playing-duration"),
            Bootstrap.SchemeConvert.FromRational(ruling));
        left.SetProperty(Sym("when"), leftWhen);
        right.SetProperty(Sym("when"), rightWhen);
        return (left, right);
    }

    /// <summary>Returns the rod distance one column records to another, or NaN.</summary>
    internal static double RodDistance(PaperColumn left, PaperColumn right)
    {
        object cursor = SpaceableGrob.GetMinimumDistances(left);
        while (cursor is Pair pair)
        {
            if (pair.Car is Pair entry && ReferenceEquals(entry.Car, right))
            {
                return entry.Cdr is double value ? value : double.NaN;
            }

            cursor = pair.Cdr;
        }

        return double.NaN;
    }

    private static PaperColumn NewColumn(
        SystemGrob system, params (string Key, object Value)[] extra)
    {
        PaperColumn column = new PaperColumn(GrobBasics(extra));
        system.TypesetGrob(column);
        system.AddColumn(column);
        return column;
    }
}
