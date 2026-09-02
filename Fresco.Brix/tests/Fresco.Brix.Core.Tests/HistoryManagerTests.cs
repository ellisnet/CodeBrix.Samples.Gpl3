// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Documents;
using Fresco.Brix.Services;
using SilverAssertions;
using System;
using System.Collections.Generic;
using Xunit;

namespace Fresco.Brix.Core.Tests;

/// <summary>
/// The document history against upstream's <c>historymanager.py</c>: the order
/// documents were last active in, which one takes over when the front one
/// closes, and the CROSS-WINDOW half — a window with nothing open follows
/// whatever another window makes current.
/// </summary>
/// <remarks>The cross-window half listens to a STATIC event, so these tests do
/// not run beside each other.</remarks>
[CollectionDefinition(HistoryCollection.Name, DisableParallelization = true)]
public class HistoryCollection
{
    /// <summary>The collection name.</summary>
    public const string Name = "document history";
}

/// <summary>
/// The cross-window half of the history — the part FD5 unblocked. The
/// ordering and the successor rules are covered by
/// <c>EditorToolsTests</c>' own <c>HistoryManagerTests</c>, which is in this
/// same collection because it too touches the static signal.
/// </summary>
[Collection(HistoryCollection.Name)]
public class HistoryManagerCrossWindowTests
{
    [Fact]
    public void a_history_over_documents_that_are_already_open_has_a_current_one()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();
        documents.CreateDocument();

        //Act
        HistoryManager history = new HistoryManager(documents);
        try
        {
            //Assert — upstream's `_has_current = bool(self._documents)'.
            history.HasCurrent.Should().BeTrue();
        }
        finally
        {
            history.Detach();
        }
    }

    [Fact]
    public void a_history_over_nothing_has_no_current_document()
    {
        //Arrange
        DocumentManager documents = new DocumentManager();

        //Act
        HistoryManager history = new HistoryManager(documents);
        try
        {
            //Assert
            history.HasCurrent.Should().BeFalse();
        }
        finally
        {
            history.Detach();
        }
    }

    [Fact]
    public void a_new_window_starts_with_the_order_of_the_window_it_came_from()
    {
        //Arrange — upstream's `othermanager' argument.
        DocumentManager documents = new DocumentManager();
        HistoryManager first = new HistoryManager(documents);
        try
        {
            EditorDocument a = documents.CreateDocument();
            EditorDocument b = documents.CreateDocument();
            documents.CurrentDocument = b;
            documents.CurrentDocument = a;

            //Act
            HistoryManager second = new HistoryManager(documents, first);
            try
            {
                //Assert
                second.Documents().Should().Equal(first.Documents());
            }
            finally
            {
                second.Detach();
            }
        }
        finally
        {
            first.Detach();
        }
    }

    [Fact]
    public void a_window_with_nothing_open_follows_a_window_that_has_something()
    {
        //Arrange — upstream's cross-window half, unblocked by FD5. Two
        //histories over one document list; the second has run out of
        //documents and is therefore listening.
        DocumentManager shared = new DocumentManager();
        DocumentManager empty = new DocumentManager();
        HistoryManager leader = new HistoryManager(shared);
        HistoryManager follower = new HistoryManager(empty);
        try
        {
            List<EditorDocument> followed = new List<EditorDocument>();
            follower.SetCurrentDocumentInWindow = followed.Add;
            follower.HasCurrent.Should().BeFalse();

            //Act — the first document opened in the leading window makes
            //itself current, which is the announcement the empty one follows.
            EditorDocument document = shared.CreateDocument();

            //Assert
            followed.Should().ContainSingle();
            followed[0].Should().BeSameAs(document);
        }
        finally
        {
            leader.Detach();
            follower.Detach();
        }
    }

    [Fact]
    public void a_window_that_has_a_current_document_follows_nobody()
    {
        //Arrange
        DocumentManager first = new DocumentManager();
        DocumentManager second = new DocumentManager();
        second.CreateDocument();
        HistoryManager leader = new HistoryManager(first);
        HistoryManager other = new HistoryManager(second);
        try
        {
            other.HasCurrent.Should().BeTrue();
            List<EditorDocument> followed = new List<EditorDocument>();
            other.SetCurrentDocumentInWindow = followed.Add;

            //Act
            first.CurrentDocument = first.CreateDocument();

            //Assert
            followed.Should().BeEmpty();
        }
        finally
        {
            leader.Detach();
            other.Detach();
        }
    }

    [Fact]
    public void a_history_that_has_let_go_follows_nothing_at_all()
    {
        //Arrange — a static event has to be let go of by hand, or it keeps the
        //window that owns it alive; Detach is what does that.
        DocumentManager first = new DocumentManager();
        DocumentManager second = new DocumentManager();
        HistoryManager leader = new HistoryManager(first);
        HistoryManager follower = new HistoryManager(second);
        List<EditorDocument> followed = new List<EditorDocument>();
        follower.SetCurrentDocumentInWindow = followed.Add;

        try
        {
            //Act
            follower.Detach();
            first.CurrentDocument = first.CreateDocument();

            //Assert
            followed.Should().BeEmpty();
        }
        finally
        {
            leader.Detach();
        }
    }

    [Fact]
    public void the_last_document_leaving_makes_a_window_start_listening_again()
    {
        //Arrange — upstream's removeDocument(): the ACTIVE document going with
        //nothing behind it is what clears `_has_current'.
        DocumentManager documents = new DocumentManager();
        HistoryManager history = new HistoryManager(documents);
        try
        {
            EditorDocument document = documents.CreateDocument();
            history.HasCurrent.Should().BeTrue();

            //Act
            documents.CloseDocument(document);

            //Assert
            history.HasCurrent.Should().BeFalse();
        }
        finally
        {
            history.Detach();
        }
    }
}
