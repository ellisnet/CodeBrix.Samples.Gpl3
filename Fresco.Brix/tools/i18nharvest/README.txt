tools/i18nharvest -- the interface-translation tools
====================================================

These ship NOTHING. They are not in the solution, no build, test or pack step
runs them, and no such step names the read-only Frescobaldi checkout they read
(board rule 3). Run them by hand when the strings change.


THE FILES

  harvest.py            the tool. Reads Frescobaldi's PO catalogs and this
                        repository's C#, reconciles the renamed-string table,
                        and writes
                          src/Fresco.Brix.Core/assets/i18n/<lang>/LC_MESSAGES/
                              frescobaldi.mo
                          src/Fresco.Brix.Core/Services/LanguageNames.g.cs
                        --check reports without writing anything.

  csextract.py          the msgid extractor: an xgettext for this repository's
                        C#. It lexes far enough to know a string literal from a
                        comment, then recognises I18n.Get(...) and the
                        Translator delegate the Score Wizard's part types are
                        handed, including msgids built from several literals
                        joined with +.

  pofile.py             the PO reader and the MO writer -- this tool's stand-in
                        for GNU msgfmt, following msgfmt's own rules (fuzzy and
                        untranslated entries left out, context joined with EOT,
                        plural forms joined with NUL, entries sorted by key
                        bytes, POT-Creation-Date dropped from the header).

  renamed.py            reading, writing and seeding renamed-strings.tsv.

  renamed-strings.tsv   THE RENAMED-STRING TABLE: every msgid this application
                        asks for that no Frescobaldi catalog has, and why. A
                        record, never a remapping.

  annotate.py           the judgements that cannot be derived -- which rows are
                        a RULING's rewrite and which are a substituted control
                        -- kept re-appliable after a re-seed.

  gen-i18n-fixtures.py  the parity probe. Imports upstream's own i18n/mofile.py
                        and language_names, runs GNU msgfmt over the same PO
                        files, and records the answers into
                        tests/Fresco.Brix.Core.Tests/fixtures/i18n/.


THE USUAL RUN

    python3 harvest.py --check          # what would change
    python3 harvest.py                  # write the catalogs and the table
    python3 gen-i18n-fixtures.py        # re-record the parity fixtures

If the reconciliation reports a problem, fix the CODE or the TABLE -- never by
renaming a Fresco.Brix msgid back to Frescobaldi's spelling, which is what
rulings FR13 and FR9 forbid.


AFTER A RE-SEED

    python3 harvest.py --seed-table
    python3 annotate.py

--seed-table throws the table away and writes a fresh one from the current
code, guessing FR13 and FR9 by undoing the substitution. annotate.py then puts
the judgements back. Read the diff before keeping it.
