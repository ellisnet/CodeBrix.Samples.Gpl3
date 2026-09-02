#!/usr/bin/env python3
# Copyright (c) 2026 Jeremy Ellis and contributors
#
# Fresco.Brix is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.

"""
The i18n parity probe: records what FRESCOBALDI'S OWN CODE answers.

Nothing in this file is an expectation written by hand, and nothing is read
back out of the C# port. Three of upstream's modules are imported and run:

  i18n/mofile.py                 the MO reader the port is a port OF. It imports
                                 only `re' and `struct', so it needs no PyQt6
                                 stand-in (board trap 49).
  frescobaldi/language_names/    the generated table and its languageName().
  GNU msgfmt                     the reference PO->MO compiler, run over the
                                 same PO files, so the catalogs this repository
                                 ships can be checked entry for entry against
                                 what the standard tool would have produced.

What is written, into tests/Fresco.Brix.Core.Tests/fixtures/i18n/:

  catalogs.json      per language: the header fields, the entry counts, a
                     SHA-256 over the WHOLE decoded catalog in a canonical
                     form, and whether GNU msgfmt's own output holds exactly
                     the same entries.
  entries.json       a sample of entries per language -- singular, contextual
                     and plural -- with what upstream's MoFile answers for
                     each of the four *gettext calls.
  plurals.json       per language: the Plural-Forms header, the PYTHON source
                     upstream's parse_plural_expr compiles it into (captured
                     out of its own call to compile()), and the form it
                     answers for every n from 0 to 200 plus a spread of
                     larger ones.
  languagenames.json  languageName(code, language) for every language the port
                     keeps a table for, over a spread of codes.

Run it after tools/i18nharvest/harvest.py has written the catalogs:

    python3 gen-i18n-fixtures.py [--frescobaldi DIR] [--repo DIR]

It ships nothing and no build step runs it (board rule 3).
"""

import argparse
import hashlib
import importlib.util
import json
import os
import subprocess
import sys
import tempfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))

import pofile
from harvest import LANGUAGES, DOMAIN, NAME_TABLES, DEFAULT_FRESCOBALDI


def load_upstream(frescobaldi):
    """Imports upstream's mofile and language_names, unmodified."""
    sys.path.insert(0, os.path.join(frescobaldi, 'i18n'))
    import mofile                                        # noqa: E402

    spec = importlib.util.spec_from_file_location(
        '_lnames',
        os.path.join(frescobaldi, 'frescobaldi', 'language_names', '__init__.py'))
    names = importlib.util.module_from_spec(spec)

    # language_names/__init__.py does `from .data import language_names', which
    # needs the package to be importable; load the data module by path and put
    # it where the relative import will find it.
    data_spec = importlib.util.spec_from_file_location(
        '_lnames.data',
        os.path.join(frescobaldi, 'frescobaldi', 'language_names', 'data.py'))
    data = importlib.util.module_from_spec(data_spec)
    data_spec.loader.exec_module(data)
    sys.modules['_lnames'] = names
    sys.modules['_lnames.data'] = data
    names.__package__ = '_lnames'
    spec.loader.exec_module(names)

    return mofile, names, data.language_names


def canonical(records):
    """A stable text form of a whole decoded catalog."""
    lines = []
    for context, messages, translations in records:
        lines.append('\x1e'.join(
            [context if context is not None else '\x00NONE']
            + ['\x1f'.join(messages), '\x1f'.join(translations)]))
    lines.sort()
    return '\n'.join(lines)


def catalog_figures(mofile, repo, frescobaldi):
    """Reads every shipped catalog with upstream's reader."""
    root = os.path.join(repo, 'src', 'Fresco.Brix.Core', 'assets', 'i18n')
    po_root = os.path.join(frescobaldi, 'i18n', 'frescobaldi')
    figures = {}
    temp = tempfile.mkdtemp(prefix='frescobrix-i18n-')

    for lang in LANGUAGES:
        path = os.path.join(root, lang, 'LC_MESSAGES', DOMAIN + '.mo')
        with open(path, 'rb') as handle:
            data = handle.read()

        records = list(mofile.parse_mo_decode(data))
        catalog = mofile.MoFile.fromData(data)

        singular = 0
        contextual = 0
        plural = 0
        for context, messages, _translations in records:
            if messages[0] == '':
                continue
            if len(messages) > 1:
                plural += 1
            if context is not None:
                contextual += 1
            else:
                singular += 1

        # GNU msgfmt over the same PO file, read back with the same reader.
        reference = os.path.join(temp, lang + '.mo')
        subprocess.run(
            ['msgfmt', '-o', reference, os.path.join(po_root, lang + '.po')],
            check=True)
        with open(reference, 'rb') as handle:
            reference_records = list(mofile.parse_mo_decode(handle.read()))

        figures[lang] = {
            'file': os.path.relpath(path, repo),
            'bytes': len(data),
            'records': len(records),
            'singular': singular,
            'contextual': contextual,
            'plural': plural,
            # ⚠ upstream's MoFile.info() decodes its VALUES and leaves
            # its KEYS as bytes; they are ASCII header names, and the
            # port's Info dictionary is keyed by the same names as text.
            'info': {k.decode('ascii'): v for k, v in catalog.info().items()},
            'sha256': hashlib.sha256(
                canonical(records).encode('utf-8')).hexdigest(),
            'msgfmt_sha256': hashlib.sha256(
                canonical(reference_records).encode('utf-8')).hexdigest(),
        }
        figures[lang]['identical_to_msgfmt'] = (
            figures[lang]['sha256'] == figures[lang]['msgfmt_sha256'])

    return figures


# The entries the C# reader is asked about, chosen to cover every shape:
# a plain msgid, one whose translation is empty upstream, contextual ones, the
# one plural entry the application uses, and two msgids ruling FR13 renamed
# (which no catalog can have, and which must therefore answer in English).
SAMPLE_SINGULAR = [
    '&File',
    'Save',
    'Preferences',
    'Zoom &In',
    'A LilyPond Music Editor',
    'Bar lines, breathing signs, etc.',
    'Original &Size',
    'Special Charac&ters',
    'Title:',
    'Manage Sessions',
    'Transpose',
    'Hyphenate Lyrics Text',
    # ruling FR13 / FR9: Fresco.Brix's own msgids, in no catalog, English always
    'LilyPort Log',
    'A LilyPort Music Editor',
    'Run LilyPort',
]

SAMPLE_CONTEXTUAL = [
    ('QPlatformTheme', 'OK'),
    ('QPlatformTheme', 'Cancel'),
    ('QPlatformTheme', 'Restore Defaults'),
    ('QDialogButtonBox', 'Save'),
    ('menu title', '&File'),
    ('menu title', '&LilyPond'),
    ('submenu title', '&Export'),
    ('dialog title', 'Close Document'),
    ('New Session', '&New...'),
    ('abbreviation for Synth bass', 'Syn.Bs.'),
    # ruling FR13: the renamed predefined menu name
    ('menu title', '&LilyPort'),
]

SAMPLE_PLURAL = [
    (None,
     'Please save the document using the "Save As..." dialog.',
     'Please save the documents using the "Save As..." dialog.'),
    ('dialog title', 'Export {num} Snippet', 'Export {num} Snippets'),
]

PLURAL_COUNTS = list(range(0, 201)) + [
    221, 222, 1000, 1001, 1002, 1011, 1021, 1101, 1111, 10001, 100000,
]


def entry_answers(mofile, repo):
    """What upstream's MoFile answers, entry by entry."""
    root = os.path.join(repo, 'src', 'Fresco.Brix.Core', 'assets', 'i18n')
    answers = {}
    for lang in LANGUAGES:
        catalog = mofile.MoFile(
            os.path.join(root, lang, 'LC_MESSAGES', DOMAIN + '.mo'))
        rows = []
        for message in SAMPLE_SINGULAR:
            rows.append({
                'call': 'gettext',
                'message': message,
                'answer': catalog.gettext(message),
            })
        for context, message in SAMPLE_CONTEXTUAL:
            rows.append({
                'call': 'pgettext',
                'context': context,
                'message': message,
                'answer': catalog.pgettext(context, message),
            })
        for context, message, plural in SAMPLE_PLURAL:
            for count in (0, 1, 2, 3, 5, 11, 21, 22, 101, 111):
                if context is None:
                    answer = catalog.ngettext(message, plural, count)
                    call = 'ngettext'
                else:
                    answer = catalog.npgettext(context, message, plural, count)
                    call = 'npgettext'
                rows.append({
                    'call': call,
                    'context': context,
                    'message': message,
                    'plural': plural,
                    'count': count,
                    'answer': answer,
                })
        answers[lang] = rows
    return answers


def plural_answers(mofile, frescobaldi):
    """Upstream's own plural rule, its compiled source and its answers."""
    root = os.path.join(frescobaldi, 'i18n', 'frescobaldi')
    captured = {}

    real_compile = compile

    def capturing_compile(source, filename, mode, *args, **kwargs):
        if filename == '<plural_expression>':
            captured['source'] = source
        return real_compile(source, filename, mode, *args, **kwargs)

    mofile.compile = capturing_compile
    try:
        answers = {}
        for lang in LANGUAGES:
            entries = pofile.parse_po(os.path.join(root, lang + '.po'))
            header = {}
            for entry in entries:
                if entry.msgid == '' and entry.context is None and entry.msgstrs:
                    header = pofile.parse_header(entry.msgstrs[0])
                    break

            forms = header.get('plural-forms', '')
            expression = forms.split(';')[1].split('plural=')[1] \
                if ';' in forms and 'plural=' in forms.split(';')[1] else ''

            captured.pop('source', None)
            rule = mofile.parse_plural_expr(expression)
            answers[lang] = {
                'plural_forms': forms,
                'expression': expression,
                # the PYTHON source upstream compiled, captured out of its own
                # call to compile()
                'python': captured.get('source'),
                'nplurals': int(forms.split(';')[0].split('=')[-1].strip()),
                'answers': {str(n): rule(n) for n in PLURAL_COUNTS},
            }

        # A few expressions no catalog here uses, to pin the parser's shape:
        # every operator its token regular expression knows.
        extra = {}
        for expression in [
            '0',
            '(n != 1)',
            'n != 1',
            '(n > 1)',
            '(n==1) ? 0 : (n>=2 && n<=4) ? 1 : 2',
            '(n==1 ? 0 : n%10>=2 && n%10<=4 && (n%100<10 || n%100>=20) ? 1 : 2)',
            '(n%10==1 && n%100!=11 ? 0 : n%10>=2 && n%10<=4 && '
            '(n%100<10 || n%100>=20) ? 1 : 2)',
            'n==1 ? 0 : n==2 ? 1 : n != 8 && n != 11 ? 2 : 3',
            '(n==0 ? 0 : n==1 ? 1 : n==2 ? 2 : n%100>=3 && n%100<=10 ? 3 : '
            'n%100>=11 ? 4 : 5)',
            'n==1 ? 0 : (n==0 || (n%100 > 0 && n%100 < 20)) ? 1 : 2',
            '(n + 1) % 3',
            'n / 2',
            '!n',
            'n > 3 ? 1 : 0',
            '(n << 1) % 3',
            '(n & 1) | (n >> 2)',
            'n ^ 3',
        ]:
            captured.pop('source', None)
            rule = mofile.parse_plural_expr(expression)
            extra[expression] = {
                'python': captured.get('source'),
                'answers': (None if rule is None
                            else {str(n): rule(n) for n in PLURAL_COUNTS}),
            }
        answers['#expressions'] = extra
        return answers
    finally:
        mofile.compile = real_compile


NAME_CODES = [
    'aa', 'cs', 'de', 'de_DE', 'el', 'en', 'en_GB', 'es', 'fr', 'fr_CA', 'gl',
    'hu', 'it', 'ja', 'ko', 'nl', 'nl_NL', 'nn', 'pl', 'pt', 'pt_BR', 'ru',
    'sv', 'tr', 'uk', 'zh', 'zh_CN', 'zh_TW', 'xx', 'xx_YY', '',
]


def name_answers(names, table):
    """Upstream's languageName(), over every table the port keeps."""
    rows = []
    for language in ['C', 'en', 'sv', None] + LANGUAGES:
        for code in NAME_CODES:
            rows.append({
                'code': code,
                'language': language,
                'answer': names.languageName(code, language),
            })
    return {
        'rows': rows,
        'tables_upstream': sorted(table),
        'tables_kept': [t for t in NAME_TABLES if t in table],
        'entries_per_table': {k: len(v) for k, v in sorted(table.items())},
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--frescobaldi', default=DEFAULT_FRESCOBALDI)
    parser.add_argument(
        '--repo',
        default=os.path.abspath(os.path.join(
            os.path.dirname(os.path.abspath(__file__)), '..', '..')))
    args = parser.parse_args()

    mofile, names, table = load_upstream(args.frescobaldi)

    out = os.path.join(
        args.repo, 'tests', 'Fresco.Brix.Core.Tests', 'fixtures', 'i18n')
    os.makedirs(out, exist_ok=True)

    def write(name, payload):
        path = os.path.join(out, name)
        with open(path, 'w', encoding='utf-8') as handle:
            json.dump(payload, handle, ensure_ascii=False,
                      indent=1, sort_keys=True)
            handle.write('\n')
        print(f'  {name}  {os.path.getsize(path)} bytes')

    print('recording from upstream:')
    figures = catalog_figures(mofile, args.repo, args.frescobaldi)
    write('catalogs.json', figures)
    write('entries.json', entry_answers(mofile, args.repo))
    write('plurals.json', plural_answers(mofile, args.frescobaldi))
    write('languagenames.json', name_answers(names, table))

    mismatched = [l for l, f in figures.items() if not f['identical_to_msgfmt']]
    print()
    print('catalogs identical to GNU msgfmt:',
          'all 13' if not mismatched else f'NO -- {mismatched}')
    return 1 if mismatched else 0


if __name__ == '__main__':
    raise SystemExit(main())
