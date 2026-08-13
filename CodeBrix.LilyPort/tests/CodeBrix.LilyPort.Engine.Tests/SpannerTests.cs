// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// <see cref="Spanner"/>'s bound retrieval, and in particular the STICKY fallback.
/// <para>
/// A sticky spanner — a <c>Footnote</c> on a beam, a <c>BalloonText</c> on a hairpin, a
/// <c>Parentheses</c> around a spanner — is never given bounds of its own. Upstream
/// (<c>spanner.cc</c>'s <c>get_bound</c>) says why: there is no point in the engraver
/// cycle at which the <c>Spanner_tracking_engraver</c> could reliably set them, because
/// the engravers looking after the HOST may set its right bound, or even its left bound,
/// as late as their finalize hook, and nothing may depend on engraver order. So the
/// bounds are inherited at RETRIEVAL time instead.
/// </para>
/// <para>
/// The defect these fence (found 2026-08-12, chasing EPG23's two red ratchet rows):
/// <c>GetBound</c> answered the backing field alone, so every sticky spanner carried two
/// null bounds. Nothing threw. The grob simply failed break processing, never reached
/// any system's <c>all-elements</c>, and vanished — which is why a footnote on a NOTE
/// HEAD engraved while the same footnote on a BEAM silently did not.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class SpannerTests
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

    /// <summary>A host spanner with two DISTINCT bounds, so left and right cannot be
    /// confused for each other.</summary>
    private static Spanner HostWithBounds(out Item left, out Item right)
    {
        left = new Item(GrobBasics());
        right = new Item(GrobBasics());
        Spanner host = new Spanner(GrobBasics());
        host.SetBound(Direction.Negative, left);
        host.SetBound(Direction.Positive, right);
        return host;
    }

    [Fact]
    public void a_sticky_spanner_without_bounds_of_its_own_answers_its_hosts()
    {
        //Arrange
        Spanner host = HostWithBounds(out Item left, out Item right);
        Spanner sticky = new Spanner(GrobBasics());
        sticky.AddInterface(Sym("sticky-grob-interface"));
        sticky.SetObject(Sym("sticky-host"), host);

        //Act
        Item stickyLeft = sticky.GetBound(Direction.Negative);
        Item stickyRight = sticky.GetBound(Direction.Positive);

        //Assert
        // The RELATIONSHIP: each end answers the host's SAME end, not merely "something
        // non-null". Answering the host's left for both would satisfy a null check.
        stickyLeft.Should().BeSameAs(left);
        stickyRight.Should().BeSameAs(right);
        sticky.GetBounds()[Direction.Negative].Should().BeSameAs(left);
        sticky.GetBounds()[Direction.Positive].Should().BeSameAs(right);
    }

    [Fact]
    public void a_spanner_that_is_not_sticky_does_not_inherit_bounds()
    {
        //Arrange
        // The control, and it must come out differently: the same host, the same
        // sticky-host object, and the interface WITHHELD. Upstream gates the fallback on
        // the interface alone, so an ordinary bound-less spanner still answers null —
        // otherwise every unbound spanner in the engine would start borrowing geometry.
        Spanner host = HostWithBounds(out Item left, out Item right);
        Spanner plain = new Spanner(GrobBasics());
        plain.SetObject(Sym("sticky-host"), host);

        //Act
        Item plainLeft = plain.GetBound(Direction.Negative);
        Item plainRight = plain.GetBound(Direction.Positive);

        //Assert
        plainLeft.Should().BeNull();
        plainRight.Should().BeNull();
        left.Should().NotBeNull();
        right.Should().NotBeNull();
    }

    [Fact]
    public void a_sticky_spanners_own_bound_wins_over_its_hosts()
    {
        //Arrange
        // The fallback is a FALLBACK: upstream returns spanned_drul_[d] whenever it is
        // set and only then consults the host. A sticky spanner that has been given one
        // end explicitly must keep it, and still inherit the other.
        Spanner host = HostWithBounds(out Item hostLeft, out Item hostRight);
        Item ownLeft = new Item(GrobBasics());
        Spanner sticky = new Spanner(GrobBasics());
        sticky.AddInterface(Sym("sticky-grob-interface"));
        sticky.SetObject(Sym("sticky-host"), host);
        sticky.SetBound(Direction.Negative, ownLeft);

        //Act
        Item stickyLeft = sticky.GetBound(Direction.Negative);
        Item stickyRight = sticky.GetBound(Direction.Positive);

        //Assert
        stickyLeft.Should().BeSameAs(ownLeft);
        stickyLeft.Should().NotBeSameAs(hostLeft);
        stickyRight.Should().BeSameAs(hostRight);
    }

    [Fact]
    public void an_inherited_bound_carries_the_hosts_column_rank_interval()
    {
        //Arrange
        // The consequence that actually mattered: spanned_column_rank_interval reads
        // through get_bound, and it is what decides which LINE a footnote belongs to and
        // where it sorts within that line. With null bounds it answered (0, 0) for every
        // sticky spanner on the page.
        Item left = new Item(GrobBasics());
        Item right = new Item(GrobBasics());
        PaperColumn leftColumn = new PaperColumn(GrobBasics());
        PaperColumn rightColumn = new PaperColumn(GrobBasics());
        leftColumn.Rank = 3;
        rightColumn.Rank = 11;
        left.XParent = leftColumn;
        right.XParent = rightColumn;

        Spanner host = new Spanner(GrobBasics());
        host.SetBound(Direction.Negative, left);
        host.SetBound(Direction.Positive, right);

        Spanner sticky = new Spanner(GrobBasics());
        sticky.AddInterface(Sym("sticky-grob-interface"));
        sticky.SetObject(Sym("sticky-host"), host);

        //Act
        Slice hostRanks = host.SpannedColumnRankInterval();
        Slice stickyRanks = sticky.SpannedColumnRankInterval();

        //Assert
        // Stated as a relationship against the host rather than against 3 and 11: the
        // point is that the sticky spanner reads the SAME stretch of columns as the grob
        // it is stuck to.
        stickyRanks.Left.Should().Be(hostRanks.Left);
        stickyRanks.Right.Should().Be(hostRanks.Right);
        stickyRanks.Left.Should().NotBe(stickyRanks.Right);
    }
}
