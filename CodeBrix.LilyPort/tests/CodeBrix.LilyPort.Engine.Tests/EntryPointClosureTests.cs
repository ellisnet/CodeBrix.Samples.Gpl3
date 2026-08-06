// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.LilyPort.Engine;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyScheme;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// Gate G3's number, measured rather than remembered: how many of upstream's
/// Scheme-visible entry points the engine actually implements.
/// <para>
/// The measurement works because <see cref="EnginePrimitives.InstallStubs"/> registers
/// every declared entry point FIRST and the ported primitives overwrite the bindings
/// afterwards. So "is this name still bound to the stub object we installed" answers the
/// question by reference, and no hand-maintained list can drift away from it.
/// </para>
/// </summary>
[Collection(EngineGlobalStateCollection.Name)]
public class EntryPointClosureTests
{
    // Process-global engine state: one interpreter, under a lock, read by every fact
    // here -- the same reason LilyPondSchemeLoadTests does it.
    private static readonly object Gate = new object();

    private static EntryPointClosure _closure;

    private static EntryPointClosure Measure()
    {
        lock (Gate)
        {
            if (_closure == null)
            {
                EntryPointClosure measured = null;

                // psyntax recurses hard enough to overflow the default stack.
                Interpreter.RunWithLargeStack(() =>
                {
                    Interpreter interpreter = LilyPondScheme.CreateInterpreter();
                    measured = EntryPointClosure.Measure(interpreter);
                });

                _closure = measured;
            }

            return _closure;
        }
    }

    [Fact]
    public void every_declared_entry_point_is_either_implemented_or_stubbed()
    {
        //Arrange
        EntryPointClosure closure = Measure();

        //Act / Assert
        // 737 is what entry-points.tsv declares: 408 LY_DEFINE, 293 callbacks and 36 smob
        // type predicates. The closure partitions them -- nothing may fall out of both
        // buckets, which is what would happen if a name were registered twice or lost.
        closure.Total.Should().Be(737);
        (closure.Implemented.Count + closure.Stubbed.Count).Should().Be(737);
    }

    [Fact]
    public void the_closure_records_how_far_the_engine_has_got()
    {
        //Arrange
        EntryPointClosure closure = Measure();

        //Act / Assert
        // Asserted as a FLOOR, not equality. Every EPG session moves this up, and a
        // session that moved it should not have to come back and edit a number here --
        // but a session that moved it DOWN has broken a registration, and that must fail.
        //
        // EPG0 baseline, 2026-08-04: 234 implemented of 737, so 503 still answer from a
        // stub. That is gate G3's starting position, measured on the day. EPG1 left it
        // at 267; EPG2 plus EPG3's first slice took it to 308 on 2026-08-05, which is
        // the whole of engraver-scheme, translator-scheme, global-context-scheme,
        // score-scheme, page-marker-scheme, paper-system-scheme and most of book-scheme
        // and output-def-scheme. EPG3's finish took it to 323 the same day: the six
        // ly:outputter-* bindings, ly:format, ly:rename-file, ly:stderr-redirect,
        // ly:base64-encode, ly:get-all-function-documentation gone real, and the
        // Ghostscript trio answering as LOUD N/A per D25 rather than as stubs.
        // EPG4 took it to 329: ly:spacing-spanner::set-springs,
        // ly:spacing-spanner::calc-common-shortest-duration,
        // ly:separation-item::calc-skylines and ly:item-break-dir -- the last pulled
        // forward out of EPG23 by the demand loop.
        // EPG13 (2026-08-05) took this from 329 to 364: the text interface and its
        // grob callbacks, the transform constructors, the ten skyline callbacks, the
        // font introspection surface, and ly:duration-compress pulled forward out of
        // EPG23 by the demand loop.
        closure.Implemented.Count.Should().BeGreaterThanOrEqualTo(364);
    }

    [Fact]
    public void every_bindings_complete_scheme_file_is_marked_ported()
    {
        //Arrange
        // A *-scheme.cc file IS its LY_DEFINE surface — it carries no types or algorithms
        // of its own. So once every entry point it declares is implemented, the file is
        // ported, and a ledger row still claiming a group OVERSTATES the remaining work.
        //
        // This fence exists because that is exactly what happened: the ledger was built by
        // scraping `was previously:` comments, binding files correctly carry no such
        // comment (standing rule 5), and six fully-implemented files sat on the worklist
        // claiming to be owed. Comment-scraping alone cannot see them; this can.
        EntryPointClosure closure = Measure();

        HashSet<string> stubbedFiles = new HashSet<string>(
            closure.Stubbed.Select(entry => entry.UpstreamFile), StringComparer.Ordinal);
        HashSet<string> implementedFiles = new HashSet<string>(
            closure.Implemented.Select(entry => entry.UpstreamFile), StringComparer.Ordinal);

        //Act
        List<string> overstated = PortLedger.Rows
            .Where(row => row.Disposition == LedgerDisposition.Group)
            .Where(row => row.File.EndsWith("-scheme.cc", StringComparison.Ordinal))
            .Where(row => implementedFiles.Contains(row.File) && !stubbedFiles.Contains(row.File))
            .Select(row => row.File + " (owed by " + row.Detail + ", but fully implemented)")
            .ToList();

        //Assert
        overstated.Should().BeEmpty();
    }

    [Fact]
    public void the_outstanding_entry_points_are_attributed_to_upstream_files()
    {
        //Arrange
        EntryPointClosure closure = Measure();

        //Act
        IReadOnlyList<KeyValuePair<string, int>> byFile = closure.StubbedByFile();

        //Assert
        // Every stub names the upstream file that declares it, so the worklist is always
        // one grep from the source. A stub attributed to nothing would be work with no
        // home -- the same failure the ledger's cross-check catches from the other side.
        byFile.Should().NotBeEmpty();
        foreach (KeyValuePair<string, int> pair in byFile)
        {
            pair.Key.Should().NotBeNullOrWhiteSpace();
            pair.Key.Should().NotBe("-");
        }
    }
}
