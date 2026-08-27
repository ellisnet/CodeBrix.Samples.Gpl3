#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): dumps python-ly's token stream for a
set of probe .ly files into TSV fixtures the Fresco.Brix.Ly.Tests parity tests
replay.

Runs against the READ-ONLY python-ly v0.9.10 checkout (the reference the port
must match). Each fixture line is: pos, end, python module, class name, and
the token text JSON-encoded. The first line records the mode used.

Usage: PYTHONDONTWRITEBYTECODE=1 python3 gen-token-fixtures.py <out-dir> <ly-file>...
"""
import json
import os
import sys

sys.path.insert(0, os.path.expanduser('~/ClaudeHome/python-ly'))

import ly.lex  # noqa: E402


def dump(path, out_dir):
    with open(path, encoding='utf-8') as handle:
        text = handle.read().replace('\r', '')
    mode = ly.lex.guessMode(text)
    state = ly.lex.state(mode)
    name = os.path.splitext(os.path.basename(path))[0]
    lines = ['# mode: {0}'.format(mode)]
    for token in state.tokens(text):
        cls = type(token)
        module = cls.__module__.rsplit('.', 1)[-1]
        lines.append('{0}\t{1}\t{2}\t{3}\t{4}'.format(
            token.pos, token.end, module, cls.__name__, json.dumps(token)))
    with open(os.path.join(out_dir, name + '.tokens.tsv'), 'w', encoding='utf-8') as out:
        out.write('\n'.join(lines) + '\n')
    with open(os.path.join(out_dir, name + '.ly'), 'w', encoding='utf-8') as out:
        out.write(text)
    return len(lines) - 1


def main():
    out_dir = sys.argv[1]
    os.makedirs(out_dir, exist_ok=True)
    total = 0
    for path in sys.argv[2:]:
        count = dump(path, out_dir)
        print('{0}: {1} tokens'.format(os.path.basename(path), count))
        total += count
    print('TOTAL {0} tokens over {1} files'.format(total, len(sys.argv) - 2))


if __name__ == '__main__':
    main()
