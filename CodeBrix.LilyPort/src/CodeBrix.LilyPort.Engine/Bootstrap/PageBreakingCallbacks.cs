// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The page-breaking Scheme surface: the six page-breaking strategies, the <c>Paper_book</c>
/// accessors, <c>ly:get-spacing-spec</c>, and the two <c>ly:book-process</c> entry points.
/// </summary>
/// <remarks>
/// <para>
/// The six breaking strategies are how a paper block CHOOSES one: <c>scm/paper.scm</c>
/// sets <c>page-breaking</c> to one of these procedures, and
/// <see cref="PaperBook.Pages"/> calls whatever it finds there. Registering them is
/// therefore not decoration — with none of them bound, every book produces no pages at
/// all and the failure looks like a missing page rather than a missing binding.
/// </para>
/// <para>
/// <c>ly:book-process</c> and <c>ly:book-process-to-systems</c> differ in ONE step
/// upstream: the first calls <c>Paper_book::output</c> and the second
/// <c>classic_output</c>. Neither of those is ported (see the PORT-COVERAGE entry for
/// <c>paper-book.cc</c>), because both dispatch into <c>lily framework-&lt;backend&gt;</c>
/// modules over program options this port does not carry. What both DO here is run
/// <c>Book::process</c> and force the pages, which is the part every caller needs and the
/// part D20 moved the batch runner onto — the runner then takes the pages off the paper
/// book itself rather than having them written to a channel.
/// </para>
/// <para>
/// The two engravers this group adds, <c>Footnote_engraver</c> and
/// <c>Page_turn_engraver</c>, carry no Scheme surface and are registered in
/// <c>TranslatorCreator</c> instead.
/// </para>
/// </remarks>
public static class PageBreakingCallbacks
{
    /// <summary>Registers the page-breaking bindings.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            return;
        }

        InstallBreakers(interpreter);
        InstallPaperBook(interpreter);
        InstallBookProcess(interpreter);
        InstallStaffAccessors(interpreter);

        // FORCED FORWARD by the demand loop, not chosen: lily/page.scm asks for it on the
        // very first page it builds. Its file, output-def-scheme.cc, has read `ported'
        // since the output pipeline landed — this one binding was simply absent, and nothing noticed because
        // nothing had ever reached page.scm. The file's disposition is UNCHANGED; this is
        // the stencil-scheme.cc pattern.
        interpreter.DefinePrimitive("ly:paper-get-number", 2, 2, a =>
        {
            OutputDef def = a[0] as OutputDef
                ?? throw SchemeErrors.WrongType(
                    "ly:paper-get-number", "output definition", a[0]);
            Symbol sym = a[1] as Symbol
                ?? throw SchemeErrors.WrongType("ly:paper-get-number", "symbol", a[1]);

            return def.GetDimension(sym);
        });

        interpreter.DefinePrimitive("ly:get-spacing-spec", 2, 2, a =>
        {
            Grob from = a[0] as Grob
                ?? throw SchemeErrors.WrongType("ly:get-spacing-spec", "grob", a[0]);
            Grob to = a[1] as Grob
                ?? throw SchemeErrors.WrongType("ly:get-spacing-spec", "grob", a[1]);

            return PageLayoutSpacing.GetSpacingSpec(from, to, false, 0, 0);
        });
    }

    private static void InstallBreakers(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:page-turn-breaking", 1, 1, a =>
            new PageTurnPageBreaking(AsPaperBook(a[0], "ly:page-turn-breaking")).Solve());

        interpreter.DefinePrimitive("ly:optimal-breaking", 1, 1, a =>
            new OptimalPageBreaking(AsPaperBook(a[0], "ly:optimal-breaking")).Solve());

        interpreter.DefinePrimitive("ly:minimal-breaking", 1, 1, a =>
            new MinimalPageBreaking(AsPaperBook(a[0], "ly:minimal-breaking")).Solve());

        interpreter.DefinePrimitive("ly:one-page-breaking", 1, 1, a =>
            new OnePageBreaking(AsPaperBook(a[0], "ly:one-page-breaking")).Solve());

        interpreter.DefinePrimitive("ly:one-line-breaking", 1, 1, a =>
            new OneLinePageBreaking(AsPaperBook(a[0], "ly:one-line-breaking")).Solve());

        interpreter.DefinePrimitive("ly:one-line-auto-height-breaking", 1, 1, a =>
            new OneLineAutoHeightBreaking(
                AsPaperBook(a[0], "ly:one-line-auto-height-breaking")).Solve());
    }

    private static void InstallPaperBook(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:paper-book?", 1, 1, a => a[0] is PaperBook);

        interpreter.DefinePrimitive("ly:paper-book-pages", 1, 1, a =>
            AsPaperBook(a[0], "ly:paper-book-pages").Pages());

        interpreter.DefinePrimitive("ly:paper-book-scopes", 1, 1, a =>
            AsPaperBook(a[0], "ly:paper-book-scopes").GetScopes());

        interpreter.DefinePrimitive("ly:paper-book-performances", 1, 1, a =>
            AsPaperBook(a[0], "ly:paper-book-performances").Performances());

        interpreter.DefinePrimitive("ly:paper-book-systems", 1, 1, a =>
            AsPaperBook(a[0], "ly:paper-book-systems").Systems());

        interpreter.DefinePrimitive("ly:paper-book-paper", 1, 1, a =>
            AsPaperBook(a[0], "ly:paper-book-paper").Paper);

        interpreter.DefinePrimitive("ly:paper-book-header", 1, 1, a =>
            AsPaperBook(a[0], "ly:paper-book-header").Header);
    }

    private static void InstallBookProcess(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:book-process", 4, 4, a =>
            ProcessBook(a, "ly:book-process"));

        interpreter.DefinePrimitive("ly:book-process-to-systems", 4, 4, a =>
            ProcessBook(a, "ly:book-process-to-systems"));
    }

    /// <summary>
    /// Runs <c>Book::process</c> and FORCES the pages.
    /// <para>
    /// Forcing them is the whole effect: upstream's next step is
    /// <c>Paper_book::output</c>, which asks for <c>pages ()</c> and hands the stencils to
    /// a backend module. The port's caller collects the pages off the paper book instead,
    /// so this must leave them computed rather than merely computable — a lazily-empty
    /// paper book would look exactly like a book with nothing in it.
    /// </para>
    /// </summary>
    private static object ProcessBook(object[] a, string procedureName)
    {
        Book book = a[0] as Book
            ?? throw SchemeErrors.WrongType(procedureName, "book", a[0]);
        OutputDef paper = a[1] as OutputDef
            ?? throw SchemeErrors.WrongType(procedureName, "output definition", a[1]);
        OutputDef layout = a[2] as OutputDef
            ?? throw SchemeErrors.WrongType(procedureName, "output definition", a[2]);

        PaperBook paperBook = book.Process(paper, layout);
        if (paperBook == null)
        {
            return Unspecified.Instance;
        }

        paperBook.Pages();

        // The paper book is ANSWERED rather than discarded, which is the port's one
        // deliberate difference from upstream's SCM_UNSPECIFIED. Upstream can throw it
        // away because output() has already written the files; here the caller is the
        // batch runner or Lily.Shell, which needs the pages back. Scheme callers that
        // ignore the result — every one in ly/init.ly — are unaffected.
        return paperBook;
    }

    /// <summary>
    /// <c>system.cc</c>'s three staff accessors — one upstream function under a filter.
    /// <para>
    /// They are page-layout surface rather than system surface, which is why they land
    /// with this group: <c>scm/paper-system.scm</c>'s <c>paper-system-annotate</c> is
    /// their only caller and it runs for <c>annotate-spacing</c>. Left unported they
    /// answered the inert placeholder, and the <c>(length spaceable-staves)</c> on the
    /// next line took the whole book down with "Not a proper list" — naming neither the
    /// paper variable nor the callback. Two words in a <c>\paper</c> block, no output.
    /// </para>
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    private static void InstallStaffAccessors(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:system::get-staves", 1, 1, a =>
            AsSystem(a[0], "ly:system::get-staves").GetMaybeSpaceableStaves(StaffFilter.All));

        interpreter.DefinePrimitive("ly:system::get-spaceable-staves", 1, 1, a =>
            AsSystem(a[0], "ly:system::get-spaceable-staves")
                .GetMaybeSpaceableStaves(StaffFilter.Spaceable));

        interpreter.DefinePrimitive("ly:system::get-nonspaceable-staves", 1, 1, a =>
            AsSystem(a[0], "ly:system::get-nonspaceable-staves")
                .GetMaybeSpaceableStaves(StaffFilter.NonSpaceable));
    }

    private static SystemGrob AsSystem(object value, string procedureName)
        => value as SystemGrob
            ?? throw SchemeErrors.WrongType(procedureName, "system", value);

    private static PaperBook AsPaperBook(object value, string procedureName)
        => value as PaperBook
            ?? throw SchemeErrors.WrongType(procedureName, "paper book", value);
}
