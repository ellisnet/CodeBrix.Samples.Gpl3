// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.LilyPort.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The fence over the engine porting effort: every upstream <c>lily/*.cc</c> file has
/// exactly one disposition, and the remaining work is COMPUTED from that.
/// <para>
/// This is EPG0's reason to exist. Before it, "what is left" lived in a plan document,
/// which is a record that can only be wrong in the flattering direction — a session lands
/// a file, forgets to cross it off, and the remaining work silently overstates itself;
/// or a plan lists a file upstream no longer has and the work is never questioned. Here a
/// file cannot be in two groups, cannot be in none, and cannot claim a C# file that is
/// not on disk.
/// </para>
/// </summary>
public class LedgerTests
{
    /// <summary>
    /// The groups that can own outstanding files. EPG0 is absent on purpose: it builds
    /// this machinery and owes no upstream file.
    /// </summary>
    private static readonly IReadOnlyList<string> KnownGroups =
        Enumerable.Range(1, 23).Select(n => "EPG" + n.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToList();

    [Fact]
    public void every_upstream_file_has_exactly_one_ledger_row()
    {
        //Arrange
        IReadOnlyList<LedgerRow> rows = PortLedger.Rows;

        //Act
        List<string> duplicated = rows
            .GroupBy(row => row.File, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        //Assert
        // 448 is the pinned upstream's lily/*.cc count. If a re-sync moves it, this fails
        // FIRST -- which is the point: the ledger is the vendored record of that file set,
        // and standing rule 7 keeps the test out of ~/GitHome/lilypond entirely.
        rows.Should().HaveCount(PortLedger.UpstreamFileCount);
        duplicated.Should().BeEmpty();
    }

    [Fact]
    public void the_ledger_accounts_for_every_file_exactly_once()
    {
        //Arrange / Act
        int ported = PortLedger.Ported.Count;
        int owed = PortLedger.NotYetPorted.Count;
        int noPort = PortLedger.NoPort.Count;

        //Assert
        (ported + owed + noPort).Should().Be(PortLedger.UpstreamFileCount);
    }

    [Fact]
    public void the_worklist_is_computed_rather_than_remembered()
    {
        //Arrange
        // NotYetPorted is derived from the rows, so porting a file moves it off the list
        // by construction. Nothing maintains a second copy that could disagree.
        IReadOnlyList<string> outstanding = PortLedger.NotYetPorted;

        //Act
        IEnumerable<string> fromRows = PortLedger.Rows
            .Where(row => row.Disposition == LedgerDisposition.Group)
            .Select(row => row.File);

        //Assert
        // The derivation is what this test is really about, and it still holds when both
        // sides are empty: NotYetPorted must equal the group-disposition rows, whatever
        // they are.
        outstanding.Should().BeEquivalentTo(fromRows);

        // ⚠ Was NotBeEmpty until EPG23 closed the ledger on 2026-08-12. Both sides are
        // empty now, and asserting emptiness on BOTH is what keeps this test from passing
        // vacuously: if a later session adds a group row and forgets to derive it, the
        // equivalence above fails; if it adds one deliberately, this line fails and the
        // session says so on purpose.
        outstanding.Should().BeEmpty();
    }

    [Fact]
    public void every_ported_row_names_c_sharp_files_that_exist()
    {
        //Arrange
        string root = RepositoryRoot();

        //Act
        List<string> missing = new List<string>();
        foreach (LedgerRow row in PortLedger.Rows.Where(r => r.Disposition == LedgerDisposition.Ported))
        {
            foreach (string relative in row.PortedFiles())
            {
                string full = Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(full))
                {
                    missing.Add(row.File + " -> " + relative);
                }
            }
        }

        //Assert
        // A 'ported' row that names a file nobody can find is the exact failure this
        // ledger replaces: a claim of done-ness with nothing behind it.
        missing.Should().BeEmpty();
    }

    [Fact]
    public void every_ported_row_names_at_least_one_file()
    {
        //Arrange / Act
        List<string> empty = PortLedger.Rows
            .Where(row => row.Disposition == LedgerDisposition.Ported && row.PortedFiles().Count == 0)
            .Select(row => row.File)
            .ToList();

        //Assert
        empty.Should().BeEmpty();
    }

    [Fact]
    public void every_group_row_names_a_known_engine_port_group()
    {
        //Arrange / Act
        List<string> unknown = PortLedger.Rows
            .Where(row => row.Disposition == LedgerDisposition.Group)
            .Select(row => row.Detail)
            .Distinct(StringComparer.Ordinal)
            .Where(group => !KnownGroups.Contains(group, StringComparer.Ordinal))
            .ToList();

        //Assert
        unknown.Should().BeEmpty();
    }

    [Fact]
    public void every_no_port_row_records_a_reason()
    {
        //Arrange / Act
        List<string> unreasoned = PortLedger.Rows
            .Where(row => row.Disposition == LedgerDisposition.NoPort
                          && string.IsNullOrWhiteSpace(row.Detail))
            .Select(row => row.File)
            .ToList();

        //Assert
        // "We are not porting this" is only a decision if the reason travels with it.
        unreasoned.Should().BeEmpty();
    }

    [Fact]
    public void the_ledger_records_the_size_of_the_remaining_job()
    {
        //Arrange / Act
        int ported = PortLedger.Ported.Count;
        int noPort = PortLedger.NoPort.Count;

        //Assert
        // The baseline, asserted with EQUALITY so progress has to be re-stated here rather
        // than silently absorbed. Moving these numbers is the point of the work; moving
        // them without noticing is what the fence prevents.
        //
        // EPG0 opened at 75 / 344 / 29. EPG1's first session moved seven files across
        // (input.cc, source-file.cc, sources.cc, diagnostics.cc, music-function.cc, and
        // the input-scheme.cc / music-function-scheme.cc bindings), and six
        // bindings-complete *-scheme.cc files were found to have been ported all along —
        // see every_bindings_complete_scheme_file_is_marked_ported for what now stops
        // that from recurring.
        //
        // EPG1's second session closed parse-scm.cc and pulled context-mod-scheme.cc
        // forward out of EPG2, because the init layer demanded it: ly:make-context-mod
        // being a stub is what made every \omit and \grobdescriptions in a \context
        // block read as "not a context mod".
        //
        // EPG2 moved ten (the whole group: context-handle, context-scheme,
        // deprecated-property, engraver-scheme, global-context-scheme, scheme-engraver,
        // translator-ctors, translator-group-ctors, translator-dispatch-list,
        // translator-scheme), and EPG3's first slice moved eight more (music-output,
        // paper-system, paper-system-scheme, page-marker, page-marker-scheme,
        // point-and-click, score-scheme, paper-score-scheme). book-scheme.cc and
        // output-def-scheme.cc deliberately stayed GROUPED with PARTIAL notes: both
        // still owe bindings that go through Paper_book, which is page layout.
        // glib-regex-scheme.cc came with them: closing ly:regex-replace's rest-list
        // replacements is what unblocked hyphenate-internal-words.scm, and finishing
        // the file's other two bindings was a line each once that was understood.
        //
        // EPG3's finish moved seven more the same day: paper-outputter.cc(+scheme),
        // function-documentation.cc (mechanism; docstring content is EPG24's),
        // lily-version.cc, general-scheme.cc — whose ly:spawn / ly:shutdown-gs /
        // ly:gs-api are N/A per D25 (category ps-backend, loud throwing bindings,
        // rows in entry-point-na-candidates.tsv) — and then, once the batch runner
        // existed and the first full sweep named its demands, output-def-scheme.cc
        // (ly:paper-get-font / ly:paper-fonts last) and lily-parser-scheme.cc
        // (ly:parse-file / ly:parse-init ride the runner's session lifecycle).
        //
        // EPG4 moved twelve -- the whole group: spacing-options, rod,
        // spacing-interface, note-spacing, staff-spacing, spacing-basic,
        // spacing-spanner, spacing-determine-loose-columns, spacing-loose-columns,
        // spacing-engraver, note-spacing-engraver and
        // separating-line-group-engraver. separation-item.cc was already counted as
        // ported and stays so; EPG4 completed the skyline half it had been carrying
        // as a partial, which is why the total moves by exactly twelve.
        // EPG13 moved nine on 2026-08-05: text-interface, transform, transform-scheme,
        // stencil-expression (the stencil-head registry had already landed with the
        // registries -- this row just stopped claiming otherwise), stencil-integral,
        // and the four font *-scheme files. pango-font.cc and pango-select.cc stay
        // no-port: they are REPLACED by the port's own font layer rather than ported.
        //
        // Wave A moved SIXTY-FOUR on 2026-08-07 in one parallel session: EPG5's
        // nineteen (columns/rests/dots/collisions), EPG6's four (stems/flags),
        // EPG7's ten (vertical organization), EPG8's nineteen (bars/meter/keys/
        // marks, Timing_translator included) and EPG9's twelve (accidentals/pitch).
        //
        // EPG22 moved SIXTEEN on 2026-08-07 and EMPTIED its own bucket: its fifteen
        // (music-wrapper, the four context/change/apply iterators, property-iterator,
        // quote-iterator, the part-combine pair, articulations, grob-interface, and the
        // four sibling-provenance rows that closed against files already carrying them),
        // plus dispatcher-scheme.cc pulled forward out of EPG23 because \addQuote cannot
        // run without ly:add-listener. break-substitution.cc is NOT among them: EPG22
        // landed only its Direction half, so the row stays grouped with a note.
        //
        // EPG17's FIRST SLICE moved FIVE on 2026-08-07: fine-iterator,
        // premeasure-iterator, measure-remainder-iterator, grace-iterator and
        // grace-music. The group's other nineteen stayed grouped -- three of the
        // remaining iterators are built on repeat-styler.cc and two more read state off
        // Alternative_sequence_iterator, so they land together with the styler rather
        // than one at a time behind stand-ins.
        //
        // EPG17's REMAINDER moved TWENTY-ONE the same day and EMPTIED the group's
        // bucket: its own nineteen, plus two pull-forwards the demand loop forced.
        // bracket.cc comes from EPG14 because Volta_bracket_interface::print and
        // Tuplet_bracket::print both draw through Bracket::make_bracket, and it declares
        // no Scheme callbacks, so EPG14 loses nothing. spanner-scheme.cc comes from
        // EPG23 under standing rule 3: a *-scheme.cc file is never a work item of its
        // own, and ly:spanner-bound is reached the instant EPG17's grobs exist -- before
        // it landed, `\times 2/3 { c8 d e }' died on #<unported ly:spanner-bound>.
        //
        // bezier-bow.cc is deliberately NOT among them: only its closed-form shape half
        // was pulled forward (Layout/BezierBow.cs, for \tupletSlur), and Bezier_bow
        // itself -- the curve-fitting the faithfulness rule is about -- stays EPG12's,
        // so the row stays grouped.
        //
        // EPG18 moved TEN on 2026-08-07 and emptied its own bucket with no pull-forwards
        // at all -- the first group on this board to need nothing from a neighbour. Its
        // three engravers share one C# file, and lyric-combine-music.cc is a single
        // length callback, so the ten rows land in six files. What it needed from
        // ELSEWHERE was not files but three free functions its own callers demanded and
        // that already-ported files had never carried: melisma_busy and
        // find_context_below out of context.cc, and spanned_time_interval out of item.cc.
        // Those are recorded in PORT-COVERAGE against their owning rows, which stay
        // `ported', because the file was ported -- the statics were the gap.
        //
        // ONE PULL-FORWARD came with EPG18 and it was forced rather than chosen:
        // grob-pq-engraver.cc, from EPG10. busyGrobs has exactly one writer in the whole
        // engine and that is it, so lyric extenders could not find the note heads they
        // cover -- and it was separately the second-most-demanded unported translator in
        // the sweep. The file holds no beam code; it sat in EPG10 only because
        // Beam_engraver is its most conspicuous consumer.
        // A SECOND pull-forward followed from the first: bezier-scheme.cc, from EPG23,
        // under the bindings rule. Flower's Bezier has been ported since the Flower
        // milestone and both LY_DEFINEs are one-line wrappers over methods it already
        // has, so that surface was owed all along -- it simply had no caller until the
        // context-handle fix let tie files reach their own drawing code.
        //
        // EPG10 moved TEN on 2026-08-07 and emptied its own bucket. The group is one
        // file lighter than its work order says, because grob-pq-engraver.cc had
        // already gone across with EPG18. Its ten rows land in seven C# files: the two
        // pure-math helpers under Layout/, the Beam grob and the scorer under Objects/,
        // and four engraver files under Translation/ -- Template_engraver_for_beams,
        // Beam_engraver, Grace_beam_engraver and Chord_tremolo_engraver share one.
        //
        // No pull-forward from any other group. What EPG10 needed from ELSEWHERE was
        // again free functions of already-ported files, four of them: grob.cc's
        // pure_relative_y_coordinate, spanned_column_rank_interval and Grob::less, and
        // grob-property.cc's call_pure_function, plus item.cc's and spanner.cc's
        // spanned_column_rank_interval overrides and engraver.cc's four announce_grob
        // variants. Those rows stay `ported' for the same reason EPG18's did.
        //
        // EPG11 and EPG12 moved EIGHTEEN together on 2026-08-08 -- eleven tie rows and
        // seven slur rows -- and emptied both buckets. NO pull-forward from any group:
        // what the pair needed from elsewhere was, once again, code belonging to files
        // whose rows already said `ported'. FIVE groups of it, each silently absent
        // since EPG0. The largest is not a tie or slur gap at all: grob.cc's
        // CONSTRUCTOR installs default X-extent / Y-extent / skyline callbacks on any
        // grob that does not name them, and the port's constructor never did -- so
        // every NoteHead, the one common grob with no explicit X-extent, has been
        // answering an EMPTY width since EPG0. The other four are the ordinary kind,
        // where ties and slurs are simply the only callers in the engine: misc.cc's
        // peak_around and convex_amplifier (plus misc.hh's inline linear_interpolate
        // and normalize), axis-group-interface.cc's staff_extent, grob.cc's
        // pure_y_extent, and pitch.cc's five alteration constants. Those rows stay
        // `ported' for the same reason EPG10's and EPG18's did.
        //
        // bezier-bow.cc is among the eighteen and is the one row that needed no new
        // code at all. EPG17 pulled its "shape half" forward and recorded that EPG12
        // still owed `Bezier_bow itself'. There is no such class in the pinned 2.27.2 --
        // no bezier-bow.hh, no definition, no reference -- so the file was already
        // WHOLE and the row had been overstating the remaining work ever since.
        //
        // EPG14 moved THIRTY-FIVE on 2026-08-08 -- the largest single group on the board
        // -- and emptied its own bucket with no pull-forward of any other GROUP's rows.
        // What it did pull forward is bindings, not files: ly:line-interface::line from
        // line-interface-scheme.cc and the whole of skyline-scheme.cc plus five
        // stencil-scheme.cc leaves, all owed to EPG23 and all FORCED by the demand loop
        // rather than chosen. THREE of those binding files came across WHOLE and so
        // change disposition with them -- line-interface-scheme.cc and
        // note-head-scheme.cc (one binding each) and skyline-scheme.cc (all eleven) --
        // which is why the total moves by 38 and not 35. stencil-scheme.cc does NOT: five
        // of its leaves landed and the rest did not, so a binding there is still not the
        // file, and EntryPointClosureTests is what keeps that distinction honest.
        //
        // EPG19 moved THIRTY on 2026-08-08: its entire group, with NO pull-forward of any
        // other group's FILES. One LEAF came forward from music-scheme.cc
        // (ly:transpose-key-alist, which Key_performer cannot work without), and that file
        // keeps its EPG23 disposition because the rest of its surface did not land -- the
        // same distinction stencil-scheme.cc draws above, and the reason this test asserts
        // a file count and EntryPointClosureTests asserts a binding count.
        //
        // EPG20 moved FOURTEEN on 2026-08-08: its entire group, with NO pull-forward of
        // any other group's files and no binding forced forward either -- the group is
        // almost entirely TRANSLATORS, which have no Scheme surface, so the entry-point
        // count moves by fourteen against fourteen files rather than by some larger
        // number. Its bucket is EMPTY.
        //
        // EPG15 moved TWELVE at its close-out (2026-08-08): its entire group, with no
        // pull-forward of any other group's FILES. Three LEAVES of grob-scheme.cc came
        // forward — ly:grob-pure-height, ly:grob-pure-property and
        // ly:grob-pure-relative-coordinate — and that file keeps its EPG23 disposition
        // for the same reason stencil-scheme.cc and music-scheme.cc do above. The group
        // is far larger than twelve rows makes it look: five files whose rows have said
        // `ported' since EPG0 were hollow on their break-processing halves and were
        // filled in without changing any disposition (grob.cc, spanner.cc, item.cc,
        // system.cc, axis-group-interface.cc).
        //
        // EPG21 moved TWELVE on 2026-08-09: its entire group, with NO pull-forward of any
        // other group's files and NO binding forced forward -- the demand loop asked this
        // group for nothing it did not already own, which is unusual and worth reading as
        // a property of the group rather than of the session. Ancient notation is a LEAF
        // of the engine: eight of the twelve rows are translators reached only by an
        // ancient context's \consists list, and the four grobs are drawn by their own
        // stencils and referenced by nothing else.
        //
        // EPG23 is what remains, at THIRTEEN rows -- every one of them a *-scheme.cc
        // binding file plus lily-random.cc. That the tail is now entirely bindings is the
        // shape EPG0 predicted, and it is why EntryPointClosureTests rather than this test
        // is the measurement that closes G3.
        //
        // The fine-vertical-geometry session moved ONE on 2026-08-12: pitch-scheme.cc,
        // whose single remaining stub was ly:set-middle-C! -- the binding parser-clef.scm
        // applies after every \clef to fold middleCClefPosition + middleCOffset into
        // middleCPosition. Stubbed, it returned the polite UnportedValue and every staff
        // in the port placed notes with the treble context default; registering it is
        // what brought bend-spanner-simple and ssaattbb-men-women-and-descant back to one
        // page. TWELVE rows remain, all EPG23's.
        //
        // EPG23 moved the LAST TWELVE on 2026-08-12, and the ledger is now CLOSED: 419
        // ported, 29 no-port, ZERO owed. All twelve were leaf *-scheme.cc binding files
        // plus lily-random.cc, so all twelve closed by landing their LY_DEFINE surface
        // rather than by porting an algorithm -- exactly the shape EPG0 predicted for the
        // tail.
        //
        // ⚠ ZERO owed is the reading from here. This assertion is now the fence that a
        // later session does not re-open a row silently: any new 'group' row fails it, and
        // that is intended -- a genuinely new work item should be a deliberate edit here,
        // not a quiet regression.
        ported.Should().Be(419);
        noPort.Should().Be(29);
        PortLedger.NotYetPorted.Should().BeEmpty();
    }

    [Fact]
    public void the_translator_manifest_records_what_upstream_declares()
    {
        //Arrange / Act
        int cpp = TranslatorManifest.Cpp.Count;
        int groups = TranslatorManifest.Groups.Count;
        int scheme = TranslatorManifest.Scheme.Count;

        //Assert
        // Gate G4's denominator. NOTE: the condensed plan says "34 Scheme"; the real
        // count is 37 -- 35 in scm/scheme-engravers.scm plus 2 in scm/scheme-performers.scm.
        cpp.Should().Be(126);
        groups.Should().Be(4);
        scheme.Should().Be(37);
    }

    [Fact]
    public void every_cpp_translator_is_declared_by_a_file_the_ledger_knows()
    {
        //Arrange
        HashSet<string> ledgerFiles = new HashSet<string>(
            PortLedger.Rows.Select(row => row.File), StringComparer.Ordinal);

        //Act
        List<string> orphans = TranslatorManifest.Entries
            .Where(entry => entry.Kind != TranslatorKind.Scheme)
            .Where(entry => !ledgerFiles.Contains(entry.File))
            .Select(entry => entry.Name + " (" + entry.File + ")")
            .ToList();

        //Assert
        // Cross-check between the two manifests: a translator whose declaring file has no
        // ledger row would be work with no home.
        orphans.Should().BeEmpty();
    }

    [Fact]
    public void no_translator_is_declared_twice()
    {
        //Arrange / Act
        List<string> duplicated = TranslatorManifest.Entries
            .GroupBy(entry => entry.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        //Assert
        duplicated.Should().BeEmpty();
    }

    /// <summary>
    /// Finds the repository root by walking up from the test binaries, the same way
    /// <c>BaselineAgreementTests</c> reaches committed repository data. The ledger's
    /// 'ported' column names repo-relative paths, and checking them means touching the
    /// working tree — this repo's own tree only, never the upstream reference.
    /// </summary>
    /// <returns>The absolute path of the repository root.</returns>
    private static string RepositoryRoot()
    {
        string directory = AppContext.BaseDirectory;

        for (int level = 0; level < 8 && directory != null; level++)
        {
            if (File.Exists(Path.Combine(directory, "CodeBrix.LilyPort.slnx")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new DirectoryNotFoundException(
            "CodeBrix.LilyPort.slnx was not found above " + AppContext.BaseDirectory);
    }
}
