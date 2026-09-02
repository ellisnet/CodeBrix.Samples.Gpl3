#!/usr/bin/env python3
# Copyright (c) 2026 Jeremy Ellis and contributors
#
# Fresco.Brix is free software: you can redistribute it and/or modify
# it under the terms of the GNU General Public License as published by
# the Free Software Foundation, either version 3 of the License, or
# (at your option) any later version.

"""
The msgid extractor: an xgettext for this repository's C#.

Fresco.Brix keys every user-visible string on the VERBATIM upstream msgid
(board rule 7), so the set of msgids the application uses can be read straight
out of the source. This module lexes C# far enough to know a string literal
from a comment and then recognises the four call shapes that reach the lookup:

    I18n.Get("message")
    I18n.Get("context", "message")
    I18n.Get("singular", "plural", count)
    I18n.Get("context", "singular", "plural", count)

and the same four through the `Translator` delegate that the Score Wizard's
part types are handed (`translate(null, "Violin")`) -- upstream's own
`_(...)`, passed around as a value so a part can be titled in a language other
than the interface's.

Everything is done from the token stream rather than from a regular
expression, because msgids are routinely built out of several adjacent string
literals joined with `+` across a line break, and because a `//` inside a
string literal must not start a comment.

This module ships nothing; it is read only by tools/i18nharvest/harvest.py.
"""

import os
import re


# ---------------------------------------------------------------------------
# The lexer
# ---------------------------------------------------------------------------

# A token is (kind, value, line). kind is one of:
#   'str'   a string literal, value = its DECODED text
#   'id'    an identifier or keyword
#   'punc'  one punctuation character
#   'other' a number or anything else we do not care about
_IDENT_RE = re.compile(r'[A-Za-z_@][A-Za-z0-9_]*')
_NUMBER_RE = re.compile(r'[0-9][0-9A-Za-z_.]*')

_SIMPLE_ESCAPES = {
    '0': '\0', 'a': '\a', 'b': '\b', 'f': '\f', 'n': '\n',
    'r': '\r', 't': '\t', 'v': '\v', '\\': '\\', "'": "'", '"': '"',
}


def tokenize(text):
    """Yields (kind, value, line) tuples for one C# source file."""
    i = 0
    line = 1
    n = len(text)
    while i < n:
        c = text[i]

        if c == '\n':
            line += 1
            i += 1
            continue

        if c in ' \t\r\f\v':
            i += 1
            continue

        # comments
        if c == '/' and i + 1 < n:
            if text[i + 1] == '/':
                j = text.find('\n', i)
                i = n if j < 0 else j
                continue
            if text[i + 1] == '*':
                j = text.find('*/', i + 2)
                j = n if j < 0 else j + 2
                line += text.count('\n', i, j)
                i = j
                continue

        # raw string literal: three or more quotes
        if text.startswith('"""', i):
            k = 0
            while i + k < n and text[i + k] == '"':
                k += 1
            fence = '"' * k
            j = text.find(fence, i + k)
            if j < 0:
                j = n
            value = text[i + k:j]
            line += text.count('\n', i, j)
            yield ('str', _dedent_raw(value), line)
            i = j + k
            continue

        # verbatim string literal, plain or interpolated
        if text.startswith('@"', i) or text.startswith('$@"', i) \
                or text.startswith('@$"', i):
            interpolated = '$' in text[i:i + 2]
            start = text.index('"', i)
            j = start + 1
            out = []
            while j < n:
                if text[j] == '"':
                    if j + 1 < n and text[j + 1] == '"':
                        out.append('"')
                        j += 2
                        continue
                    break
                out.append(text[j])
                j += 1
            value = ''.join(out)
            line += value.count('\n')
            yield ('interp' if interpolated else 'str', value, line)
            i = j + 1
            continue

        # interpolated (single-quote-fenced) or plain string literal
        if c == '"' or (c == '$' and i + 1 < n and text[i + 1] == '"'):
            interpolated = c == '$'
            j = i + (2 if interpolated else 1)
            out = []
            while j < n:
                ch = text[j]
                if ch == '\\':
                    j, piece = _read_escape(text, j)
                    out.append(piece)
                    continue
                if ch == '"':
                    break
                if ch == '\n':
                    # an unterminated literal; give up on this one
                    break
                out.append(ch)
                j += 1
            # An interpolated literal is never a msgid: report it as 'interp'
            yield ('interp' if interpolated else 'str', ''.join(out), line)
            i = j + 1
            continue

        # character literal
        if c == "'":
            j = i + 1
            while j < n and text[j] != "'":
                j += 2 if text[j] == '\\' else 1
            yield ('other', text[i:j + 1], line)
            i = j + 1
            continue

        m = _IDENT_RE.match(text, i)
        if m:
            yield ('id', m.group(0), line)
            i = m.end()
            continue

        m = _NUMBER_RE.match(text, i)
        if m:
            yield ('other', m.group(0), line)
            i = m.end()
            continue

        yield ('punc', c, line)
        i += 1


def _read_escape(text, j):
    """Reads one backslash escape starting at j; returns (next index, text)."""
    n = len(text)
    if j + 1 >= n:
        return j + 1, '\\'
    e = text[j + 1]
    if e in _SIMPLE_ESCAPES:
        return j + 2, _SIMPLE_ESCAPES[e]
    if e == 'u' and j + 6 <= n:
        try:
            return j + 6, chr(int(text[j + 2:j + 6], 16))
        except ValueError:
            return j + 2, e
    if e == 'U' and j + 10 <= n:
        try:
            return j + 10, chr(int(text[j + 2:j + 10], 16))
        except ValueError:
            return j + 2, e
    if e == 'x':
        k = j + 2
        digits = ''
        while k < n and len(digits) < 4 and text[k] in '0123456789abcdefABCDEF':
            digits += text[k]
            k += 1
        if digits:
            return k, chr(int(digits, 16))
        return j + 2, e
    return j + 2, e


def _dedent_raw(value):
    """Applies C#'s raw-string-literal indentation rule, roughly."""
    if '\n' not in value:
        return value
    lines = value.split('\n')
    if lines and lines[0].strip() == '':
        lines = lines[1:]
    if lines and lines[-1].strip() == '':
        indent = len(lines[-1])
        lines = lines[:-1]
        lines = [l[indent:] if l[:indent].strip() == '' else l.lstrip()
                 for l in lines]
    return '\n'.join(lines)


# ---------------------------------------------------------------------------
# The call recogniser
# ---------------------------------------------------------------------------

class Call:
    """One recognised lookup call."""

    __slots__ = ('context', 'message', 'plural', 'path', 'line', 'kind')

    def __init__(self, context, message, plural, path, line, kind):
        self.context = context
        self.message = message
        self.plural = plural
        self.path = path
        self.line = line
        self.kind = kind

    @property
    def key(self):
        return (self.context, self.message)

    def __repr__(self):
        return f'Call({self.context!r}, {self.message!r}, {self.plural!r})'


def _read_string_expression(tokens, i):
    """Reads a run of string literals joined by '+'.

    Returns (next index, text or None). None means the argument was not a
    concatenation of plain string literals -- an interpolated literal, a
    variable, a method call.
    """
    parts = []
    while True:
        if i >= len(tokens):
            return i, None
        kind, value, _ = tokens[i]
        if kind != 'str':
            return i, None
        parts.append(value)
        i += 1
        if i < len(tokens) and tokens[i][:2] == ('punc', '+'):
            i += 1
            continue
        return i, ''.join(parts)


def _split_arguments(tokens, i):
    """Splits the argument list starting at the '(' at index i.

    Returns (index after the matching ')', list of (start, end) token spans).
    """
    assert tokens[i][:2] == ('punc', '(')
    i += 1
    depth = 0
    start = i
    spans = []
    while i < len(tokens):
        kind, value, _ = tokens[i]
        if kind == 'punc':
            if value in '([{':
                depth += 1
            elif value in ')]}':
                if depth == 0:
                    spans.append((start, i))
                    return i + 1, spans
                depth -= 1
            elif value == ',' and depth == 0:
                spans.append((start, i))
                start = i + 1
        i += 1
    return i, spans


def extract_file(path, translator_names=('translate',)):
    """Extracts every lookup call from one C# file."""
    with open(path, 'r', encoding='utf-8') as handle:
        text = handle.read()

    tokens = list(tokenize(text))
    calls = []
    dynamic = []

    for i in range(len(tokens)):
        kind, value, line = tokens[i]
        if kind != 'id':
            continue

        # I18n.Get(...)   or   Services.I18n.Get(...)
        is_i18n = (
            value == 'Get'
            and i >= 2
            and tokens[i - 1][:2] == ('punc', '.')
            and tokens[i - 2] == ('id', 'I18n', tokens[i - 2][2]))
        is_translator = value in translator_names and (
            i == 0 or tokens[i - 1][:2] != ('punc', '.'))

        if not (is_i18n or is_translator):
            continue
        if i + 1 >= len(tokens) or tokens[i + 1][:2] != ('punc', '('):
            continue

        _, spans = _split_arguments(tokens, i + 1)
        if not spans or spans == [(i + 2, i + 2)]:
            continue

        texts = []
        for start, end in spans:
            j, value_text = _read_string_expression(tokens, start)
            texts.append(value_text if j == end else None)

        kindname = 'I18n.Get' if is_i18n else 'translator'
        call = _classify(texts, spans, tokens, path, line, kindname)
        if call is None:
            if is_i18n:
                dynamic.append((path, line, [t for t in texts]))
            continue
        calls.append(call)

    return calls, dynamic


def _classify(texts, spans, tokens, path, line, kindname):
    """Turns an argument list into a Call, or None when it is not literal."""
    count = len(texts)

    # A `null` first argument is upstream's "no context".
    first_is_null = (
        spans[0][1] - spans[0][0] == 1
        and tokens[spans[0][0]][:2] == ('id', 'null'))

    if count == 1:
        if texts[0] is None:
            return None
        return Call(None, texts[0], None, path, line, kindname)

    if count == 2:
        if texts[1] is None:
            return None
        if first_is_null:
            return Call(None, texts[1], None, path, line, kindname)
        if texts[0] is None:
            return None
        return Call(texts[0], texts[1], None, path, line, kindname)

    if count == 3:
        # message, plural, count
        if texts[0] is None or texts[1] is None:
            return None
        return Call(None, texts[0], texts[1], path, line, kindname)

    if count == 4:
        if texts[1] is None or texts[2] is None:
            return None
        context = None if first_is_null else texts[0]
        if context is None and not first_is_null:
            return None
        return Call(context, texts[1], texts[2], path, line, kindname)

    return None


def extract_tree(root, skip_dirs=('obj', 'bin')):
    """Extracts every lookup call under a source root."""
    calls = []
    dynamic = []
    for base, dirs, files in os.walk(root):
        dirs[:] = sorted(d for d in dirs if d not in skip_dirs)
        for name in sorted(files):
            if not name.endswith('.cs'):
                continue
            found, missed = extract_file(os.path.join(base, name))
            calls.extend(found)
            dynamic.extend(missed)
    return calls, dynamic
