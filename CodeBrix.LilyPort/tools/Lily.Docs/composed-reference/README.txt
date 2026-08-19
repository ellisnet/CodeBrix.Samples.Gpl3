================================================================================
CodeBrix.LilyPort -- tools/Lily.Docs/composed-reference/
================================================================================

THE ORACLE'S OWN COMPOSED SNIPPET SOURCES, frozen. This is the fence that says
Lily.Docs composes a documentation snippet the way lilypond-book composes it.

    probe.itely     the probe document: 28 snippets: 26 option cases plus a deduplication pair
    cases.tsv       case -> options, code, directive line, reference file
    <case>.ly       what the ORACLE's lilypond-book composed for that case

--------------------------------------------------------------------------------
WHY A FROZEN REFERENCE RATHER THAN A LIVE ORACLE RUN
--------------------------------------------------------------------------------

Standing rule 7 of the LilyPort plan: no build or test step touches
~/GitHome/lilypond. Rule 4 of the Phase-5 plan adds that the corpus gates ALWAYS
RUN rather than skipping when an input is absent (decision D49(b)). A parity test
that shelled out to lilypond-book would break both -- and would also make the
gate depend on the oracle install, which is not in the repository.

So the oracle ran ONCE, by hand, and its output is committed. That is the same
arrangement as the expected-warnings baselines one directory over: frozen from a
measured run that was then READ, asserted exactly thereafter, and re-frozen only
deliberately.

--------------------------------------------------------------------------------
PROVENANCE -- how these files were made
--------------------------------------------------------------------------------

Generated 2026-08-19 with the pinned oracle, LilyPond 2.27.2:

    cd <a scratch directory>
    cp .../composed-reference/probe.itely .
    LC_ALL=C ~/ClaudeHome/oracle/lilypond-2.27.2/bin/lilypond-book \
        --output=out \
        --process="$HOME/ClaudeHome/oracle/lilypond-2.27.2/bin/lilypond" \
        probe.itely

lilypond-book writes each composed snippet to out/<xx>/lily-<hash>.ly BEFORE it
engraves anything, so the composed sources are complete even when engraving
fails. Each file was matched to its case by its \sourcefileline value, which is
the case's @lilypond directive line MINUS ONE (measured: the directive at
probe.itely line 12 composes \sourcefileline 11).

These are the output of a GPL-3 tool, carrying template text from
python/book_snippets.py. That file is mirrored in ../../book-mirror/ and recorded
in ../../THIRD-PARTY-NOTICES.txt; this directory adds nothing new to the
licensing picture and is covered by the same entry.

--------------------------------------------------------------------------------
TWENTY-EIGHT CASES, TWENTY-SEVEN FILES -- THE DEDUPLICATION PAIR
--------------------------------------------------------------------------------

Every case is written with DISTINCT music, on purpose: two cases that happened to
compose identically would share one reference file, and a composer that ignored
an option would then pass against the wrong file without anyone noticing. Giving
each case its own music makes each reference its own claim.

The single exception is deliberate and is the point of the last two cases:

    dedup-plain      { g'2 }   no options
    dedup-verbatim   { g'2 }   [verbatim]

identical music, differing only by a PROCESSING-INDEPENDENT option. Upstream's
snippet checksum is the md5 of the relevant contents plus the output-relevant
option strings (book_snippets.py:685-698), and `verbatim' is excluded from the
output-relevant list -- so the two checksums are equal BY CONSTRUCTION and the
oracle wrote ONE file for the two.

WHICH of the pair it keyed that file to is an artefact of lilypond-book's own
processing order and is not document order: measured 2026-08-19, the shared file
carries dedup-verbatim's \sourcefileline, not dedup-plain's. cases.tsv therefore
RECORDS the direction it observed ("deduplicated-by-oracle:<kept case>") rather
than assuming one, and re-freezing re-reads it.

That deduplication is a CLAIM the suite holds, not an accident worked around: a
`verbatim' snippet that stopped composing identically to a plain one would mean
the composer had started letting a document-side option reach the engraver.

--------------------------------------------------------------------------------
WHAT THE TEST COMPARES, AND WHY NOT THE WHOLE FILE
--------------------------------------------------------------------------------

The comparison is on RELEVANT CONTENTS -- the composed source with its
\version, \sourcefilename and \sourcefileline lines removed. That is not a
convenience: it is the comparison lilypond-book itself makes when it asks whether
two composed sources are the same snippet (book_snippets.py:725-726,
`relevant_contents', used by `write_ly' to detect a hash collision). Those lines
record WHERE a snippet sat, so two identical snippets in different places compose
to different bytes; comparing what upstream compares is comparing the part that
means something.

Full-byte equality is MEASURED as well and recorded in the status file, because
it answers a second question -- whether the Texinfo package reports a snippet's
line number on the same base lilypond-book does.

--------------------------------------------------------------------------------
RE-FREEZING
--------------------------------------------------------------------------------

Only when the composition changes DELIBERATELY, and never to make a test pass.
Re-run the command above, re-map by \sourcefileline, read the diff, and say in
the commit what moved and why. A diff you cannot explain means the composer
changed behaviour you did not intend to change.
================================================================================
