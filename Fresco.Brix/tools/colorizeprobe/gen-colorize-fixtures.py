#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): dumps python-ly's colorize mapping
for a set of probe files into TSV fixtures the Fresco.Brix.Ly.Tests parity
tests replay.

Runs against the READ-ONLY python-ly v0.9.10 checkout (the reference the port
must match). Two kinds of fixture are written:

  <name>.colorize.tsv  one line per token: pos, end, python class name, and the
                       css_class the default mapper resolves (mode, style,
                       base) — 'None' where the mapper answers nothing.
  _mapping.tsv         the whole default_mapping() flattened: mode, style name,
                       base, and every token class the style claims.
  _scheme.tsv          the whole default_scheme flattened: mode ('' for the
                       base styles), style name, property, value.

Usage: PYTHONDONTWRITEBYTECODE=1 python3 gen-colorize-fixtures.py <out-dir> <ly-file>...
"""
import os
import sys

sys.path.insert(0, os.path.expanduser('~/ClaudeHome/python-ly'))

import ly.lex        # noqa: E402
import ly.colorize   # noqa: E402


def dump_tokens(path, out_dir, mapper):
    with open(path, encoding='utf-8') as handle:
        text = handle.read().replace('\r', '')
    mode = ly.lex.guessMode(text)
    state = ly.lex.state(mode)
    name = os.path.splitext(os.path.basename(path))[0]
    lines = ['# mode: {0}'.format(mode)]
    for token in state.tokens(text):
        style = mapper[token]
        if style is None:
            style_cols = 'None\tNone\tNone'
        else:
            style_cols = '{0}\t{1}\t{2}'.format(
                style.mode, style.name,
                'None' if style.base is None else style.base)
        lines.append('{0}\t{1}\t{2}\t{3}'.format(
            token.pos, token.end, type(token).__name__, style_cols))
    with open(os.path.join(out_dir, name + '.colorize.tsv'), 'w',
              encoding='utf-8') as out:
        out.write('\n'.join(lines) + '\n')
    with open(os.path.join(out_dir, name + '.ly'), 'w', encoding='utf-8') as out:
        out.write(text)
    return len(lines) - 1


def dump_mapping(out_dir):
    lines = []
    for mode, styles in ly.colorize.default_mapping():
        for style in styles:
            for cls in style.classes:
                lines.append('{0}\t{1}\t{2}\t{3}'.format(
                    mode, style.name,
                    'None' if style.base is None else style.base,
                    cls.__name__))
    with open(os.path.join(out_dir, '_mapping.tsv'), 'w', encoding='utf-8') as out:
        out.write('\n'.join(lines) + '\n')
    return len(lines)


def dump_scheme(out_dir):
    lines = []
    for mode, styles in ly.colorize.default_scheme.items():
        for style_name, props in sorted(styles.items()):
            for prop, value in sorted(props.items()):
                lines.append('{0}\t{1}\t{2}\t{3}'.format(
                    '' if mode is None else mode, style_name, prop, value))
    with open(os.path.join(out_dir, '_scheme.tsv'), 'w', encoding='utf-8') as out:
        out.write('\n'.join(lines) + '\n')
    return len(lines)


def main():
    out_dir = sys.argv[1]
    os.makedirs(out_dir, exist_ok=True)
    print('mapping: {0} class entries'.format(dump_mapping(out_dir)))
    print('scheme: {0} properties'.format(dump_scheme(out_dir)))
    mapper = ly.colorize.css_mapper()
    total = 0
    for path in sys.argv[2:]:
        count = dump_tokens(path, out_dir, mapper)
        print('{0}: {1} tokens'.format(os.path.basename(path), count))
        total += count
    print('TOTAL {0} tokens over {1} files'.format(total, len(sys.argv) - 2))


if __name__ == '__main__':
    main()
