#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): runs Frescobaldi's OWN word-boundary
expression over probe lines and writes the spans as fixtures the
Fresco.Brix.Core.Tests parity test replays.

frescobaldi/wordboundary.py imports PyQt6 at module level, and PyQt6 is not
installed here — nor may it be. The expression itself is a plain regex, so this
tool lifts it out of the READ-ONLY checkout by AST rather than retyping it, and
runs python's own finditer with it.

Usage: PYTHONDONTWRITEBYTECODE=1 python3 gen-wordboundary-fixtures.py <out-dir>
"""
import ast
import json
import os
import re
import sys

SOURCE = os.path.expanduser('~/GitHome/frescobaldi/frescobaldi/wordboundary.py')


def load_word_regexp():
    """Lift BoundaryHandler.word_regexp out of upstream's module."""
    with open(SOURCE, encoding='utf-8') as handle:
        source = handle.read()
    tree = ast.parse(source)
    for node in ast.walk(tree):
        if not isinstance(node, ast.ClassDef) or node.name != 'BoundaryHandler':
            continue
        for statement in node.body:
            if not isinstance(statement, ast.Assign):
                continue
            names = [t.id for t in statement.targets if isinstance(t, ast.Name)]
            if 'word_regexp' not in names:
                continue
            namespace = {'re': re}
            exec(compile(ast.Expression(statement.value), SOURCE, 'eval'),
                 namespace)  # noqa
            return eval(compile(ast.Expression(statement.value), SOURCE, 'eval'),
                        namespace)
    raise SystemExit('could not lift word_regexp out of wordboundary.py')


PROBES = [
    r"c-\markup { x }",
    r"\relative c'",
    "page-breaking",
    r"\version \"2.27.2\"",
    "c4 d8 e16 f32",
    r"c^\fermata d_\staccato",
    "",
    "   ",
    r"a\\b",
    "Voice.NoteHead #'style",
    r"\new Staff \with { instrumentName = \"Fl.\" }",
    "s1*4 | R1*2",
    "lyric -- text __ more",
    r"#(define foo 1)",
]


def main():
    out_dir = sys.argv[1]
    os.makedirs(out_dir, exist_ok=True)
    regexp = load_word_regexp()
    result = []
    for text in PROBES:
        spans = [list(m.span()) for m in regexp.finditer(text)]
        result.append({'text': text, 'spans': spans})
        print('{0!r}: {1}'.format(text, spans))

    with open(os.path.join(out_dir, 'wordboundary.json'), 'w', encoding='utf-8') as out:
        json.dump(result, out, indent=2)
    print('TOTAL {0} probes'.format(len(result)))


if __name__ == '__main__':
    main()
