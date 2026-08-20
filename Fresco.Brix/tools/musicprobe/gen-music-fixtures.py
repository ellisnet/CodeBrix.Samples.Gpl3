#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): dumps the ly.music tree python-ly
builds for a set of probe .ly files into JSON fixtures the
Fresco.Brix.Ly.Tests music parity tests replay.

Runs against the READ-ONLY python-ly v0.9.10 checkout (the reference the port
must match). Each fixture is <name>.music.json beside a copy of <name>.ly, and
records the whole item tree (class, position, end position, token text), the
musical length of every node that has one, and the time position at a spread
of cursor positions.

Usage: PYTHONDONTWRITEBYTECODE=1 python3 gen-music-fixtures.py <out-dir> <ly-file>...
"""
import json
import os
import sys
from fractions import Fraction

sys.path.insert(0, os.path.expanduser('~/ClaudeHome/python-ly'))

import ly.document  # noqa: E402
import ly.music  # noqa: E402
import ly.music.items as items  # noqa: E402


def fraction(value):
    """A fraction (or int) as "n/d", or None."""
    if value is None:
        return None
    f = Fraction(value)
    return '{0}/{1}'.format(f.numerator, f.denominator)


def dump(node):
    """The node as a nested dict."""
    token = node.token
    return {
        'cls': type(node).__name__,
        'position': node.position,
        'end': node.end_position(),
        'token': str(token) if token is not None else None,
        'tokens': [str(t) for t in node.tokens],
        'length': fraction(node.length()),
        'plaintext': node.plaintext(),
        'children': [dump(child) for child in node],
    }


def harvest(path, out_dir):
    with open(path, encoding='utf-8') as handle:
        text = handle.read().replace('\r', '')
    doc = ly.document.Document(text)
    music = ly.music.document(doc)

    # A spread of cursor positions: every 7th character, plus the document end.
    positions = list(range(0, len(text), 7)) + [len(text)]
    time_positions = []
    for p in positions:
        time_positions.append([p, fraction(music.time_position(p))])

    node_positions = []
    for p in positions:
        n = music.node(p)
        node_positions.append([p, type(n).__name__, n.position])

    data = {
        'tree': dump(music),
        'has_output': bool(music.has_output()),
        'time_positions': time_positions,
        'node_positions': node_positions,
    }

    name = os.path.splitext(os.path.basename(path))[0]
    with open(os.path.join(out_dir, name + '.music.json'), 'w', encoding='utf-8') as out:
        json.dump(data, out, indent=1, sort_keys=True)
        out.write('\n')
    with open(os.path.join(out_dir, name + '.ly'), 'w', encoding='utf-8') as out:
        out.write(text)

    def count(n):
        return 1 + sum(count(c) for c in n['children'])

    return count(data['tree'])


def main():
    out_dir = sys.argv[1]
    os.makedirs(out_dir, exist_ok=True)
    total = 0
    for path in sys.argv[2:]:
        n = harvest(path, out_dir)
        print('{0}: {1} items'.format(os.path.basename(path), n))
        total += n
    print('TOTAL {0} items over {1} files'.format(total, len(sys.argv) - 2))


if __name__ == '__main__':
    main()
