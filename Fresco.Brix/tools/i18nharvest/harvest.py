#!/usr/bin/env python3
# Copyright (c) 2026 Jeremy Ellis and contributors
#
# Fresco.Brix is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.

"""
The i18n harvest tool (board wave W-I18N).

It reads Frescobaldi's own PO catalogs out of the read-only reference checkout,
matches them against the msgids THIS repository's C# actually asks for, and
writes what the application ships:

  src/Fresco.Brix.Core/assets/i18n/<lang>/LC_MESSAGES/frescobaldi.mo
  src/Fresco.Brix.Core/Services/LanguageNames.g.cs

It ships nothing itself and is not in the solution; no build, test or pack step
runs it or names the reference checkout (board rule 3).

THE RENAMED-STRING TABLE. Ruling FR13 forbids any UI element naming LilyPond,
ruling FR9 makes the application its own rather than Frescobaldi, and several
other rulings changed what a sentence describes. Every string those rulings
touched is a Fresco.Brix-ORIGINAL msgid: it does not match Frescobaldi's, no
catalog translates it, and it falls back to English on purpose. They are
recorded, one row each, in renamed-strings.tsv -- and the tool RECONCILES that
file against the code every run:

  * a row whose msgid is no longer in the code is DRIFT and is reported;
  * a row whose msgid turns out to match upstream after all is reported, so it
    can be taken out;
  * a msgid in the code that matches no catalog and is in no row is reported as
    unrecorded.

The table is a record, never a remapping: nothing here ever "fixes" one of
these back to Frescobaldi's spelling.

Usage:

    python3 harvest.py [--frescobaldi DIR] [--repo DIR] [--check] [--quiet]

    --check   report only; write nothing.
"""

import argparse
import collections
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import csextract
import pofile
import renamed


DEFAULT_FRESCOBALDI = os.path.expanduser('~/GitHome/frescobaldi')

# Ruling FR5.6: thirteen European-script languages. The five CJK catalogs
# Frescobaldi also ships (ja, ko, zh_CN, zh_HK, zh_TW) are post-v1 -- they need
# fonts the application does not carry and an input method the editor has not
# got yet.
LANGUAGES = [
    'cs', 'de', 'es', 'fr', 'gl', 'it', 'nl', 'pl',
    'pt_BR', 'ru', 'sv', 'tr', 'uk',
]

DOMAIN = 'frescobaldi'

# The outer keys of upstream's language_names data that are worth keeping:
# the interface languages this application has, plus "C", which is the English
# table every other language falls back to. Upstream's data has no "en" and no
# "sv" table at all, so those two ALREADY fall through to "C" upstream, and
# they do the same here.
NAME_TABLES = ['C'] + LANGUAGES


def read_catalogs(frescobaldi):
    """Reads the thirteen PO catalogs; returns {lang: [entries]}."""
    root = os.path.join(frescobaldi, 'i18n', 'frescobaldi')
    catalogs = {}
    for lang in LANGUAGES:
        path = os.path.join(root, lang + '.po')
        if not os.path.exists(path):
            raise SystemExit(f'missing catalog: {path}')
        catalogs[lang] = pofile.parse_po(path)
    return catalogs


def upstream_msgids(entries):
    """The (context, msgid) pairs a catalog names, header excluded."""
    return {(e.context, e.msgid) for e in entries
            if not (e.msgid == '' and e.context is None)}


def translated_msgids(entries):
    """The (context, msgid) pairs a catalog actually TRANSLATES."""
    return {(e.context, e.msgid) for e in entries
            if not (e.msgid == '' and e.context is None) and e.translated}


def header_of(entries):
    """The catalog's header fields."""
    for e in entries:
        if e.msgid == '' and e.context is None and e.msgstrs:
            return pofile.parse_header(e.msgstrs[0])
    return {}


def write_catalogs(catalogs, repo, quiet=False):
    """Writes the compiled catalogs into the assets folder."""
    root = os.path.join(repo, 'src', 'Fresco.Brix.Core', 'assets', 'i18n')
    written = {}
    for lang in LANGUAGES:
        folder = os.path.join(root, lang, 'LC_MESSAGES')
        os.makedirs(folder, exist_ok=True)
        path = os.path.join(folder, DOMAIN + '.mo')
        count = pofile.write_mo(catalogs[lang], path)
        written[lang] = (count, os.path.getsize(path))
        if not quiet:
            print(f'  wrote {os.path.relpath(path, repo)}  '
                  f'{count} entries, {written[lang][1]} bytes')
    return written


# ---------------------------------------------------------------------------
# language_names
# ---------------------------------------------------------------------------

def read_language_names(frescobaldi):
    """Reads upstream's generated language_names data."""
    import importlib.util
    path = os.path.join(frescobaldi, 'frescobaldi', 'language_names', 'data.py')
    spec = importlib.util.spec_from_file_location('_lndata', path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module.language_names


def cs_string(text):
    """Quotes a string as a C# literal."""
    out = ['"']
    for ch in text:
        if ch == '\\':
            out.append('\\\\')
        elif ch == '"':
            out.append('\\"')
        elif ch == '\n':
            out.append('\\n')
        elif ch == '\t':
            out.append('\\t')
        elif ch == '\r':
            out.append('\\r')
        elif ord(ch) < 0x20:
            out.append(f'\\u{ord(ch):04x}')
        else:
            out.append(ch)
    out.append('"')
    return ''.join(out)


LANGUAGE_NAMES_HEADER = '''// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;

namespace Fresco.Brix.Services; //was previously: frescobaldi/language_names/

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.
//
// GENERATED FILE -- do not edit. Regenerate with tools/i18nharvest/harvest.py,
// which reads frescobaldi/language_names/data.py out of the read-only
// reference checkout. See THIRD-PARTY-NOTICES.txt section 6.

/// <summary>
/// The human-readable name of a language, from its code.
/// </summary>
/// <remarks>
/// <para>
/// //was previously: <c>frescobaldi/language_names/</c> -- one function,
/// <c>languageName()</c>, over a generated table. Upstream's table is 3,404
/// lines and names every language in eighteen: the {tables} kept here are
/// <c>C</c> and this application's own interface languages (ruling FR5.6), and
/// the five CJK tables go with the five catalogs that ruling leaves out.
/// Upstream's own data has no table for <c>en</c> or <c>sv</c> either -- both
/// already fall through to <c>C</c> there, and they do the same here.
/// </para>
/// <para>
/// The names themselves are KDE's translated language names, extracted from
/// <c>/usr/share/locale/all_languages</c> by Frescobaldi's own
/// <c>generate.py</c>. Upstream's credit is repeated here as it is written:
/// "Thanks go to the KDE developers for their translated language names which
/// are used currently in data.py." See also THIRD-PARTY-NOTICES.txt section 6.
/// </para>
/// </remarks>
public static class LanguageNames
{{
    private static readonly Dictionary<string, Dictionary<string, string>> Tables
        = Build();

    /// <summary>Gets the languages a table of names exists in.</summary>
    public static IReadOnlyCollection<string> NamingLanguages => Tables.Keys;

    /// <summary>Names a language.</summary>
    /// <param name="code">The language's code, e.g. <c>nl</c> or
    /// <c>pt_BR</c>.</param>
    /// <param name="language">The language to name it IN; null for the
    /// application's own.</param>
    /// <returns>The name, or the code when nothing names it.</returns>
    /// <remarks>Upstream's <c>languageName(code, language)</c>, verbatim: the
    /// naming language is tried, then its base, then <c>C</c>; within the
    /// first table that exists the code is tried, then ITS base; and a table
    /// that exists but does not have the code ends the search rather than
    /// falling through to the next one.</remarks>
    public static string LanguageName(string code, string language = null)
    {{
        if (string.IsNullOrEmpty(code)) {{ return code; }}

        if (language == null) {{ language = I18n.Language; }}

        List<string> languages = new List<string>();
        if (!string.IsNullOrEmpty(language))
        {{
            languages.Add(language);
            if (language.IndexOf('_') >= 0)
            {{
                languages.Add(language.Substring(0, language.IndexOf('_')));
            }}
        }}

        languages.Add("C");

        List<string> codes = new List<string> {{ code }};
        if (code.IndexOf('_') >= 0)
        {{
            codes.Add(code.Substring(0, code.IndexOf('_')));
        }}

        foreach (var naming in languages)
        {{
            if (!Tables.TryGetValue(naming, out var table)) {{ continue; }}

            foreach (var wanted in codes)
            {{
                if (table.TryGetValue(wanted, out var name)) {{ return name; }}
            }}

            //⚠ Upstream breaks here rather than trying the next table: a
            //table that exists is the answer, whether or not it has the code.
            break;
        }}

        return code;
    }}

    private static Dictionary<string, Dictionary<string, string>> Build()
    {{
        Dictionary<string, Dictionary<string, string>> tables
            = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

'''

LANGUAGE_NAMES_FOOTER = '''        return tables;
    }
}
'''


def write_language_names(names, repo, quiet=False):
    """Writes Services/LanguageNames.g.cs."""
    path = os.path.join(
        repo, 'src', 'Fresco.Brix.Core', 'Services', 'LanguageNames.g.cs')
    kept = [k for k in NAME_TABLES if k in names]
    total = 0
    with open(path, 'w', encoding='utf-8') as out:
        out.write(LANGUAGE_NAMES_HEADER.format(tables=len(kept)))
        for key in kept:
            table = names[key]
            total += len(table)
            out.write(f'        tables[{cs_string(key)}] = '
                      f'new Dictionary<string, string>(StringComparer.Ordinal)\n')
            out.write('        {\n')
            for code in sorted(table):
                out.write(f'            [{cs_string(code)}] = '
                          f'{cs_string(table[code])},\n')
            out.write('        };\n\n')
        out.write(LANGUAGE_NAMES_FOOTER)

    if not quiet:
        print(f'  wrote {os.path.relpath(path, repo)}  '
              f'{len(kept)} tables, {total} names, {os.path.getsize(path)} bytes')
    return kept, total


# ---------------------------------------------------------------------------
# The report
# ---------------------------------------------------------------------------

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--frescobaldi', default=DEFAULT_FRESCOBALDI)
    parser.add_argument(
        '--repo',
        default=os.path.abspath(os.path.join(
            os.path.dirname(os.path.abspath(__file__)), '..', '..')))
    parser.add_argument('--check', action='store_true')
    parser.add_argument('--quiet', action='store_true')
    parser.add_argument('--seed-table', action='store_true',
                        help='write a fresh renamed-strings.tsv from the '
                             'current code, for a human to annotate')
    args = parser.parse_args()

    print('Fresco.Brix i18n harvest')
    print('  reference:', args.frescobaldi)
    print('  repository:', args.repo)
    print()

    calls, dynamic = csextract.extract_tree(os.path.join(args.repo, 'src'))
    ours = collections.OrderedDict()
    for call in calls:
        ours.setdefault(call.key, []).append(call)
    plurals = {c.key: c.plural for c in calls if c.plural}

    catalogs = read_catalogs(args.frescobaldi)
    known = {lang: upstream_msgids(entries) for lang, entries in catalogs.items()}
    done = {lang: translated_msgids(entries) for lang, entries in catalogs.items()}

    # Any catalog names the same msgids -- they all come from one POT -- but
    # they differ by a few dozen because they were regenerated at different
    # times, so "upstream has it" means ANY catalog has it.
    everything = set()
    for names in known.values():
        everything |= names

    matched = [k for k in ours if k in everything]
    unmatched = [k for k in ours if k not in everything]

    print(f'MSGIDS')
    print(f'  lookup calls in src/           {len(calls):6d}')
    print(f'  distinct msgids               {len(ours):6d}')
    print(f'    with a context              {len([k for k in ours if k[0]]):6d}')
    print(f'    with a plural form          {len(plurals):6d}')
    print(f'  matched upstream              {len(matched):6d}'
          f'   ({100.0 * len(matched) / max(1, len(ours)):.1f}%)')
    print(f'  Fresco.Brix-original          {len(unmatched):6d}')
    print(f'  calls whose msgid is not a literal (reported, never harvested): '
          f'{len(dynamic)}')
    for path, line, _ in dynamic:
        print(f'      {os.path.relpath(path, args.repo)}:{line}')
    print()

    if args.seed_table:
        renamed.seed(unmatched, ours, everything, args.repo)
        return 0

    # --- reconcile the renamed-string table -------------------------------
    table = renamed.load()
    in_code = set(ours)
    recorded = {(row.context, row.msgid) for row in table}

    stale = sorted(recorded - in_code, key=lambda k: (k[0] or '', k[1]))
    matching = sorted(
        k for k in recorded if k in everything)
    unrecorded = sorted(
        (k for k in unmatched if k not in recorded),
        key=lambda k: (k[0] or '', k[1]))

    print('RENAMED-STRING TABLE (renamed-strings.tsv)')
    print(f'  rows                          {len(table):6d}')
    counts = collections.Counter(row.category for row in table)
    for category in sorted(counts):
        print(f'    {category:<26s}{counts[category]:6d}')
    print(f'  rows no longer in the code    {len(stale):6d}')
    for context, msgid in stale:
        print(f'      DRIFT  {renamed.show(context, msgid)}')
    print(f'  rows that DO match upstream   {len(matching):6d}')
    for context, msgid in matching:
        print(f'      REMOVE {renamed.show(context, msgid)}')
    print(f'  code msgids not in the table  {len(unrecorded):6d}')
    for context, msgid in unrecorded:
        site = ours[(context, msgid)][0]
        print(f'      MISSING {renamed.show(context, msgid)}'
              f'   @{os.path.relpath(site.path, args.repo)}:{site.line}')
    print()

    # --- per-language figures ---------------------------------------------
    print('CATALOGS')
    print(f'  {"lang":<7}{"msgids":>8}{"translated":>12}{"ours hit":>10}'
          f'{"untranslated":>14}{"plural forms":>14}')
    figures = {}
    for lang in LANGUAGES:
        entries = catalogs[lang]
        header = header_of(entries)
        forms = header.get('plural-forms', '')
        nplurals = forms.split(';')[0].split('=')[-1].strip() if forms else '?'
        hit = len([k for k in matched if k in done[lang]])
        figures[lang] = (len(known[lang]), len(done[lang]), hit,
                         len(ours) - hit, nplurals)
        print(f'  {lang:<7}{len(known[lang]):>8}{len(done[lang]):>12}'
              f'{hit:>10}{len(ours) - hit:>14}{nplurals:>14}')
    print()

    if args.check:
        print('(--check: nothing written)')
    else:
        print('WRITING')
        write_catalogs(catalogs, args.repo, args.quiet)
        names = read_language_names(args.frescobaldi)
        write_language_names(names, args.repo, args.quiet)
        print()

    problems = len(stale) + len(matching) + len(unrecorded)
    print('RESULT:', 'clean' if problems == 0
          else f'{problems} table problem(s) -- see above')
    return 1 if problems else 0


if __name__ == '__main__':
    sys.exit(main())
