#!/usr/bin/env python3
# Copyright (c) 2026 Jeremy Ellis and contributors
#
# Fresco.Brix is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.

"""
Reading Frescobaldi's PO catalogs and writing GNU MO catalogs.

Frescobaldi keeps its translations as PO files and compiles them to MO with
GNU msgfmt (i18n/mo-gen.py, tox -e mo-generate); the running application then
reads the MO. Fresco.Brix keeps that runtime model -- the application reads MO
files through a port of upstream's own i18n/mofile.py -- so this module stands
in for msgfmt: it reads the PO and writes the MO the port will read.

The rules it follows are msgfmt's own:

  * an entry with an EMPTY msgstr is untranslated and is left out;
  * an entry marked `#, fuzzy' is left out (except the header, whose fuzzy
    flag msgfmt ignores);
  * an obsolete entry (`#~') is left out;
  * a msgctxt is joined to its msgid with EOT (\\x04);
  * a plural entry's forms are joined with NUL, both in the msgid (singular
    NUL plural) and in the msgstr (form 0 NUL form 1 NUL ...);
  * the entries are sorted by their key bytes, which is what lets a reader
    binary-search them.

The hash table msgfmt also writes is NOT written: it is an optional
acceleration that GNU gettext, Python's gettext and upstream's own mofile.py
all ignore, and leaving it out keeps the writer honest about what the reader
actually uses. `msgunfmt' reads the result, and tools/i18nharvest's probe
checks our bytes against msgfmt's own output entry for entry.

This module ships nothing; it is read only by tools/i18nharvest/harvest.py.
"""

import re
import struct


LE_MAGIC = 0x950412de

#: gettext joins a context to its message with EOT.
EOT = '\x04'


class Entry:
    """One PO entry."""

    __slots__ = ('context', 'msgid', 'msgid_plural', 'msgstrs', 'flags',
                 'comments', 'line')

    def __init__(self):
        self.context = None
        self.msgid = None
        self.msgid_plural = None
        self.msgstrs = []
        self.flags = []
        self.comments = []
        self.line = 0

    @property
    def fuzzy(self):
        return 'fuzzy' in self.flags

    @property
    def translated(self):
        """True when msgfmt would put this entry in the MO file."""
        if self.fuzzy:
            return False
        return any(s for s in self.msgstrs)

    @property
    def key(self):
        return (self.context, self.msgid)


_ESCAPES = {
    'n': '\n', 't': '\t', 'r': '\r', 'a': '\a', 'b': '\b',
    'f': '\f', 'v': '\v', '\\': '\\', '"': '"', "'": "'", '?': '?',
}

_MSGSTR_INDEX_RE = re.compile(r'^msgstr\[(\d+)\]\s*(.*)$')


def unquote(text):
    """Decodes one PO quoted string (including its surrounding quotes)."""
    text = text.strip()
    if not text.startswith('"'):
        return ''
    body = text[1:]
    if body.endswith('"'):
        body = body[:-1]
    out = []
    i = 0
    n = len(body)
    while i < n:
        c = body[i]
        if c != '\\':
            out.append(c)
            i += 1
            continue
        if i + 1 >= n:
            out.append('\\')
            break
        e = body[i + 1]
        if e in _ESCAPES:
            out.append(_ESCAPES[e])
            i += 2
        elif e == 'x':
            j = i + 2
            digits = ''
            while j < n and body[j] in '0123456789abcdefABCDEF':
                digits += body[j]
                j += 1
            out.append(chr(int(digits, 16)) if digits else 'x')
            i = j
        elif e in '01234567':
            j = i + 1
            digits = ''
            while j < n and len(digits) < 3 and body[j] in '01234567':
                digits += body[j]
                j += 1
            out.append(chr(int(digits, 8)))
            i = j
        else:
            out.append(e)
            i += 2
    return ''.join(out)


def parse_po(path):
    """Parses a PO file; returns the list of entries, header first."""
    entries = []
    entry = Entry()
    field = None          # which list the continuation lines append to
    index = 0
    started = False

    def flush():
        nonlocal entry, field, started
        if started and entry.msgid is not None:
            entries.append(entry)
        entry = Entry()
        field = None
        started = False

    with open(path, 'r', encoding='utf-8') as handle:
        for number, raw in enumerate(handle, start=1):
            line = raw.rstrip('\n')
            stripped = line.strip()

            if not stripped:
                flush()
                continue

            if stripped.startswith('#~'):
                # obsolete entry: msgfmt drops it, and so do we
                flush()
                continue

            if stripped.startswith('#'):
                if started and entry.msgid is not None:
                    # a comment starts the NEXT entry
                    flush()
                if stripped.startswith('#,'):
                    entry.flags.extend(
                        f.strip() for f in stripped[2:].split(','))
                else:
                    entry.comments.append(stripped)
                field = None
                continue

            if stripped.startswith('msgctxt'):
                if started and entry.msgid is not None:
                    flush()
                entry.line = number
                entry.context = unquote(stripped[len('msgctxt'):])
                field = 'context'
                started = True
                continue

            if stripped.startswith('msgid_plural'):
                entry.msgid_plural = unquote(stripped[len('msgid_plural'):])
                field = 'msgid_plural'
                continue

            if stripped.startswith('msgid'):
                if started and entry.msgid is not None:
                    flush()
                if not entry.line:
                    entry.line = number
                entry.msgid = unquote(stripped[len('msgid'):])
                field = 'msgid'
                started = True
                continue

            match = _MSGSTR_INDEX_RE.match(stripped)
            if match:
                index = int(match.group(1))
                while len(entry.msgstrs) <= index:
                    entry.msgstrs.append('')
                entry.msgstrs[index] = unquote(match.group(2))
                field = 'msgstr'
                continue

            if stripped.startswith('msgstr'):
                index = 0
                entry.msgstrs = [unquote(stripped[len('msgstr'):])]
                field = 'msgstr'
                continue

            if stripped.startswith('"'):
                piece = unquote(stripped)
                if field == 'context':
                    entry.context += piece
                elif field == 'msgid':
                    entry.msgid += piece
                elif field == 'msgid_plural':
                    entry.msgid_plural += piece
                elif field == 'msgstr':
                    entry.msgstrs[index] += piece
                continue

    flush()
    return entries


def parse_header(text):
    """Splits a PO/MO header msgstr into a name -> value dictionary."""
    info = {}
    last = None
    for line in text.splitlines():
        line = line.strip()
        if not line:
            continue
        if ':' in line:
            name, value = line.split(':', 1)
            last = name.strip().lower()
            info[last] = value.strip()
        elif last:
            info[last] += '\n' + line
    return info


#: The header fields GNU msgfmt leaves out of the compiled catalog.
DROPPED_HEADER_FIELDS = ('pot-creation-date',)


def strip_header_fields(text):
    """Removes the header fields msgfmt does not put in the MO file."""
    kept = []
    for line in text.split('\n'):
        name = line.split(':', 1)[0].strip().lower() if ':' in line else None
        if name in DROPPED_HEADER_FIELDS:
            continue
        kept.append(line)
    return '\n'.join(kept)


def write_mo(entries, path):
    """Writes the translated entries to a little-endian GNU MO file.

    Returns the number of entries written (the header included).
    """
    pairs = []
    for entry in entries:
        if entry.msgid == '' and entry.context is None:
            # the header: msgfmt keeps it even when it is marked fuzzy, but
            # drops POT-Creation-Date, which says nothing at run time.
            if entry.msgstrs and entry.msgstrs[0]:
                pairs.append((b'', strip_header_fields(
                    entry.msgstrs[0]).encode('utf-8')))
            continue

        if not entry.translated:
            continue

        key = entry.msgid
        if entry.msgid_plural is not None:
            key = entry.msgid + '\0' + entry.msgid_plural
            value = '\0'.join(entry.msgstrs)
        else:
            value = entry.msgstrs[0]

        if entry.context is not None:
            key = entry.context + EOT + key

        pairs.append((key.encode('utf-8'), value.encode('utf-8')))

    pairs.sort(key=lambda pair: pair[0])

    count = len(pairs)
    keystart = 7 * 4 + 16 * count
    offsets = []
    keys = bytearray()
    values = bytearray()
    for key, value in pairs:
        offsets.append((len(keys), len(key), len(values), len(value)))
        keys += key + b'\0'
        values += value + b'\0'
    valuestart = keystart + len(keys)

    table = bytearray()
    for start, length, _, _ in offsets:
        table += struct.pack('<II', length, keystart + start)
    for _, _, start, length in offsets:
        table += struct.pack('<II', length, valuestart + start)

    with open(path, 'wb') as handle:
        handle.write(struct.pack(
            '<Iiiiiii',
            LE_MAGIC,       # magic
            0,              # revision
            count,          # number of strings
            7 * 4,          # offset of the key table
            7 * 4 + 8 * count,   # offset of the value table
            0,              # hash table size (none)
            keystart))      # hash table offset (unused)
        handle.write(bytes(table))
        handle.write(bytes(keys))
        handle.write(bytes(values))

    return count
