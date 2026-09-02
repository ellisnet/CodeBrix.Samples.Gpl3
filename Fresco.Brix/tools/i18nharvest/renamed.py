#!/usr/bin/env python3
# Copyright (c) 2026 Jeremy Ellis and contributors
#
# Fresco.Brix is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.

"""
The RENAMED-STRING TABLE: reading, writing and seeding renamed-strings.tsv.

The table records every msgid this application asks for that Frescobaldi's
catalogs do not have, and WHY. It is a record and never a remapping: the tool
does not translate one of these through its upstream original, and nothing in
the repository ever "fixes" one back to Frescobaldi's spelling.

The file is a tab-separated table with four columns:

    CATEGORY    why it diverges (see CATEGORIES below)
    ORIGIN      the upstream msgid it replaced, or "-" when there is none
    CONTEXT     the disambiguating context, or "-"
    MSGID       the msgid the code asks for

Newlines and tabs inside a value are written \\n and \\t; a literal backslash
is written \\\\.
"""

import os


HERE = os.path.dirname(os.path.abspath(__file__))
TABLE = os.path.join(HERE, 'renamed-strings.tsv')

CATEGORIES = {
    'FR13': 'no UI element names LilyPond -- the engine is LilyPort',
    'FR9': 'the application is not Frescobaldi -- {appname} goes in',
    'RULING': 'a ruling removed or changed what the sentence described',
    'PLATFORM': 'a Qt control this application does not have was substituted',
    'NEW': 'Fresco.Brix-original: no upstream msgid at all',
}


class Row:
    """One row of the table."""

    __slots__ = ('category', 'origin', 'context', 'msgid')

    def __init__(self, category, origin, context, msgid):
        self.category = category
        self.origin = origin
        self.context = context
        self.msgid = msgid

    @property
    def key(self):
        return (self.context, self.msgid)


def escape(text):
    """Escapes one field for the TSV."""
    if text is None:
        return '-'
    return (text.replace('\\', '\\\\')
                .replace('\t', '\\t')
                .replace('\n', '\\n')
                .replace('\r', '\\r'))


def unescape(text):
    """Un-escapes one field from the TSV."""
    if text == '-':
        return None
    out = []
    i = 0
    while i < len(text):
        if text[i] == '\\' and i + 1 < len(text):
            nxt = text[i + 1]
            out.append({'n': '\n', 't': '\t', 'r': '\r', '\\': '\\'}.get(nxt, nxt))
            i += 2
        else:
            out.append(text[i])
            i += 1
    return ''.join(out)


def show(context, msgid):
    """One msgid as the report prints it."""
    body = escape(msgid)
    if len(body) > 96:
        body = body[:93] + '...'
    return (f'{context}|' if context else '') + repr(body)


def load(path=TABLE):
    """Reads the table."""
    rows = []
    if not os.path.exists(path):
        return rows
    with open(path, 'r', encoding='utf-8') as handle:
        for line in handle:
            line = line.rstrip('\n')
            if not line or line.startswith('#'):
                continue
            fields = line.split('\t')
            if len(fields) != 4:
                raise SystemExit(f'malformed row in {path}: {line!r}')
            rows.append(Row(fields[0], unescape(fields[1]),
                            unescape(fields[2]), unescape(fields[3])))
    return rows


HEADER = """\
# THE RENAMED-STRING TABLE -- Fresco.Brix, board wave W-I18N
#
# Every msgid this application asks for that Frescobaldi's catalogs do not
# have, and why. These strings FALL BACK TO ENGLISH by design, in every one of
# the thirteen languages, and they stay that way until somebody translates
# them: nothing here is ever "fixed" back to Frescobaldi's own spelling.
#
# tools/i18nharvest/harvest.py reconciles this file against the code on every
# run -- a row that is no longer in the code, a row that turns out to match
# upstream after all, and a code msgid that is in no row are all reported.
#
# Columns, tab separated:
#   CATEGORY  FR13     no UI element names LilyPond -- the engine is LilyPort
#             FR9      the application is not Frescobaldi -- {appname} goes in
#             RULING   a ruling removed or changed what the sentence described
#             PLATFORM a Qt control this application does not have was replaced
#             NEW      Fresco.Brix-original: there is no upstream msgid at all
#   ORIGIN    the upstream msgid it replaced, or - when there is none
#   CONTEXT   the disambiguating context, or -
#   MSGID     the msgid the code asks for
#
# \\n, \\t and \\\\ are escapes. Manual TITLES (decision FD12) are NOT in this
# table: they are data the Documentation Browser reads off the files, never
# msgids, and they say "LilyPond" because that is what the documents are
# called.
"""


def save(rows, path=TABLE):
    """Writes the table."""
    with open(path, 'w', encoding='utf-8') as handle:
        handle.write(HEADER)
        for row in sorted(rows, key=lambda r: (r.category, r.context or '', r.msgid)):
            handle.write('\t'.join((
                row.category, escape(row.origin),
                escape(row.context), escape(row.msgid))) + '\n')


def guess(context, msgid, upstream):
    """Guesses a row's category and upstream origin, for --seed-table."""
    by_msgid = {}
    for c, m in upstream:
        by_msgid.setdefault(m, []).append(c)

    candidates = [
        ('FR13', msgid.replace('LilyPort', 'LilyPond')),
        ('FR9', msgid.replace('{appname}', 'Frescobaldi')
                     .replace('Fresco.Brix', 'Frescobaldi')),
        ('FR9', msgid.replace('{appname}', 'Frescobaldi')
                     .replace('Fresco.Brix', 'Frescobaldi')
                     .replace('LilyPort', 'LilyPond')),
    ]
    for category, candidate in candidates:
        if candidate == msgid:
            continue
        if (context, candidate) in upstream:
            return category, candidate
        if candidate in by_msgid:
            return category, candidate

    return 'NEW', None


def seed(unmatched, sites, upstream, repo):
    """Writes a fresh table from the current code, for a human to annotate."""
    rows = []
    for context, msgid in unmatched:
        category, origin = guess(context, msgid, upstream)
        rows.append(Row(category, origin, context, msgid))
    save(rows)
    print(f'seeded {len(rows)} rows into {os.path.relpath(TABLE, repo)}')
    print('   ', ', '.join(
        f'{c}={len([r for r in rows if r.category == c])}'
        for c in sorted({r.category for r in rows})))
