/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

  LilyPond is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  LilyPond is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/book.cc, lily/include/book.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <summary>
/// What a <c>\book</c> or a <c>\bookpart</c> block becomes: a header, an optional
/// paper block, and the scores and bookparts collected into it. Upstream both
/// constructs are the same <c>Book</c> class; a bookpart is simply a book that lives
/// inside another one.
/// <para>
/// PORTED MINIMALLY, on the demand of the parser's book/bookpart rule actions plus
/// honest storage of the type's own state. The rendering half — <c>process</c>,
/// <c>process_score</c>, <c>process_bookparts</c>, <c>set_keys</c> — and the pieces
/// that need Guile modules inside the engine (<c>Book (Book const &amp;)</c>,
/// <c>clone</c>, <c>set_parent</c>, <c>add_scores_to_bookpart</c>,
/// <c>add_bookpart</c>, <c>error_found</c>) are NOT ported; see the Engine
/// PORT-COVERAGE entry.
/// </para>
/// </summary>
public class Book
{
    /// <summary>
    /// Initializes an empty book: no paper, and the header, score list and bookpart
    /// list all the empty list — exactly upstream's default constructor. Upstream
    /// also stamps a fresh, empty <c>Input</c> as the origin; the port has no input
    /// location type yet, so <see cref="Origin"/> starts unset instead.
    /// </summary>
    public Book()
    {
        Paper = null;
        Header = Nil.Instance;
        Scores = Nil.Instance;
        Bookparts = Nil.Instance;
    }

    /// <summary>
    /// Gets or sets the book's paper block, or <see langword="null"/> when it has
    /// none — which is what decides whether a book identifier at top level is
    /// dispatched as a book or as a bookpart.
    /// <para>Upstream: the public <c>paper_</c> member.</para>
    /// </summary>
    public OutputDef Paper { get; set; }

    /// <summary>
    /// Gets or sets the book's header. A module once one exists; a <c>\book</c> body
    /// opens with one, a <c>\bookpart</c> body creates it on first demand and carries
    /// the empty list until then.
    /// <para>Upstream: the public <c>header_</c> member (an SCM).</para>
    /// </summary>
    public object Header { get; set; }

    /// <summary>
    /// Gets or sets the scores collected into the book, as a Scheme list in REVERSE
    /// order — the most recently added score first, exactly as upstream keeps
    /// <c>scores_</c>.
    /// </summary>
    public object Scores { get; set; }

    /// <summary>
    /// Gets or sets the bookparts collected into the book, as a Scheme list in
    /// REVERSE order — the most recently added part first, exactly as upstream keeps
    /// <c>bookparts_</c>.
    /// </summary>
    public object Bookparts { get; set; }

    /// <summary>
    /// Gets where in the source this book came from, or <see langword="null"/> when
    /// no location has been recorded.
    /// <para>Upstream: <c>origin ()</c> over the <c>input_location_</c> smob.</para>
    /// </summary>
    public object Origin { get; private set; }

    /// <summary>
    /// Records where in the source this book came from.
    /// <para>Upstream: <c>origin ()-&gt;set_spot (...)</c>, which the
    /// <c>book_block</c> and <c>bookpart_block</c> rule actions call on the finished
    /// block. The same shape as <see cref="Music.MusicObject.SetSpot"/>.</para>
    /// </summary>
    /// <param name="origin">The source location.</param>
    public void SetSpot(object origin) => Origin = origin;

    /// <summary>
    /// Adds a score to the book, consing it onto the FRONT of <see cref="Scores"/> —
    /// which is why the list is in reverse order.
    /// <para>Upstream: <c>Book::add_score</c>, reached from the Scheme layer's
    /// <c>ly:book-add-score!</c> book handlers.</para>
    /// </summary>
    /// <param name="score">The score (or markup list) to add.</param>
    public void AddScore(object score) => Scores = new Pair(score, Scores);

    /// <summary>Gets or sets the enclosing book, when this one is a bookpart.</summary>
    public Book Parent { get; set; }

    /// <summary>
    /// Adds a bookpart, consing it onto the FRONT of <see cref="Bookparts"/>.
    /// </summary>
    /// <param name="part">The bookpart.</param>
    public void AddBookpart(object part) => Bookparts = new Pair(part, Bookparts);

    /// <summary>
    /// Concatenates every score's and bookpart's output into one <see cref="PaperBook"/>.
    /// <para>
    /// Landed with EPG16 (2026-08-08) — the rendering half this file's ledger row said was
    /// absent. This is what <c>ly:book-process</c> calls and therefore what the whole page
    /// path hangs off.
    /// </para>
    /// <para>
    /// TWO ORDERING FACTS matter here. The scores are rendered in PARSE order, which means
    /// reversing the list, because <see cref="AddScore"/> conses onto the front. And
    /// <c>Normalize</c> runs on the paper book's ALREADY-SCALED paper, not before it: the
    /// scaling happens in <see cref="PaperBook"/>'s constructor and normalize computes
    /// line-width from dimensions that must already be in output units.
    /// </para>
    /// </summary>
    /// <param name="defaultPaper">The paper to use when the book carries none.</param>
    /// <param name="defaultLayout">The layout to render scores under.</param>
    /// <param name="parentPart">The enclosing bookpart's paper book, or null.</param>
    /// <returns>The paper book, or <see langword="null"/> when there is nothing to make.</returns>
    public PaperBook Process(
        OutputDef defaultPaper, OutputDef defaultLayout, PaperBook parentPart = null)
    {
        OutputDef paper = Paper ?? defaultPaper;

        // Only the TOP book checks for score errors; a bookpart's errors were already
        // counted when its parent asked.
        if (parentPart == null && ErrorFound())
        {
            return null;
        }

        if (paper == null)
        {
            return null;
        }

        PaperBook paperBook = new PaperBook(paper, parentPart);
        paperBook.Header = Header;

        if (Bookparts is Pair)
        {
            ProcessBookparts(paperBook, paper, defaultLayout);
        }
        else
        {
            paperBook.Paper.Normalize();

            List<object> scores = Pair.ToList(Scores);
            scores.Reverse();
            foreach (object score in scores)
            {
                ProcessScore(score, paperBook, defaultLayout);
            }
        }

        return paperBook;
    }

    /// <summary>
    /// Whether this book, or any bookpart under it, holds a score the parser marked
    /// errored.
    /// </summary>
    /// <returns><see langword="true"/> when an error was found.</returns>
    public bool ErrorFound()
    {
        foreach (object entry in Pair.ToList(Scores))
        {
            if (entry is Score score && score.ErrorFound)
            {
                return true;
            }
        }

        foreach (object entry in Pair.ToList(Bookparts))
        {
            if (entry is Book bookpart && bookpart.ErrorFound())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Moves any scores added directly to this book into a child bookpart, so that a book
    /// mixing loose scores with explicit <c>\bookpart</c> blocks has one uniform shape.
    /// </summary>
    private void AddScoresToBookpart()
    {
        if (Scores is Pair)
        {
            Book part = new Book { Parent = this, Scores = Scores };
            Bookparts = new Pair(part, Bookparts);
            Scores = Nil.Instance;
        }
    }

    private void ProcessBookparts(PaperBook outputPaperBook, OutputDef paper, OutputDef layout)
    {
        AddScoresToBookpart();
        List<object> parts = Pair.ToList(Bookparts);
        parts.Reverse();
        foreach (object entry in parts)
        {
            if (entry is Book book)
            {
                PaperBook paperBookPart = book.Process(paper, layout, outputPaperBook);
                if (paperBookPart != null)
                {
                    outputPaperBook.AddBookpart(paperBookPart);
                }
            }
        }
    }

    /// <summary>
    /// Renders one entry of the score list into the paper book.
    /// <para>
    /// A performance collects THREE header layers, outermost first — the book's first
    /// header, the book's current header, then the score's own — so that the innermost
    /// wins when the metadata is written. A paper score contributes its header as a
    /// separate print element BEFORE itself, which is how <c>GetSystemSpecs</c> knows
    /// which header titles which score.
    /// </para>
    /// </summary>
    private static void ProcessScore(object scoreScm, PaperBook outputPaperBook, OutputDef layout)
    {
        if (scoreScm is Score score)
        {
            object outputs = score.BookRendering(outputPaperBook.Paper, layout);

            foreach (object output in Pair.ToList(outputs))
            {
                if (output is Layout.Performance perf)
                {
                    outputPaperBook.AddPerformance(perf);

                    if (outputPaperBook.Header0 is SchemeModule header0)
                    {
                        perf.PushHeader(header0);
                    }

                    if (outputPaperBook.Header is SchemeModule header)
                    {
                        perf.PushHeader(header);
                    }

                    if (score.GetHeader() is SchemeModule scoreHeader)
                    {
                        perf.PushHeader(scoreHeader);
                    }
                }
                else if (output is Layout.PaperScore pscore)
                {
                    if (score.GetHeader() is SchemeModule)
                    {
                        outputPaperBook.AddScore(score.GetHeader());
                    }

                    outputPaperBook.AddScore(pscore);
                }
            }
        }
        else if (TextInterface.IsMarkupList(scoreScm) || scoreScm is Layout.PageMarker)
        {
            outputPaperBook.AddScore(scoreScm);
        }
    }
}
