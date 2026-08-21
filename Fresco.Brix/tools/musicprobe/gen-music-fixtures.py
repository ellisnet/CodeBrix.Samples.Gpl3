#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): dumps the ly.music tree python-ly
builds for a set of probe .ly files into JSON fixtures the
Fresco.Brix.Ly.Tests music parity tests replay.

Runs against the READ-ONLY python-ly v0.9.10 checkout (the reference the port
must match). Each fixture is <name>.music.json beside a copy of <name>.ly, and
records the whole item tree (class, position, end position, token text), the
musical length of every node that has one, and the time position at a spread
of cursor positions.

KNOWN FIXES (ruling FR14). The oracle answers "what python-ly produces with
its demonstrable defects fixed", not "what python-ly ships with", because that
is what the port is required to implement. Each entry below is a ONE-LINE
source patch applied to the reference IN MEMORY at generation time -- the
checkout stays read-only (standing rule 3) -- and every fixture records the
applied list in its own header, which MusicParityTests asserts. A fix whose
`old` line is no longer present, or is present more than once, is a hard
failure here: the day python-ly fixes the defect itself, this tool stops rather
than silently generating a differently-shaped oracle.

Usage: PYTHONDONTWRITEBYTECODE=1 python3 gen-music-fixtures.py <out-dir> <ly-file>...
"""
import json
import os
import sys
import types
from fractions import Fraction

PYTHON_LY = os.path.expanduser('~/ClaudeHome/python-ly')
sys.path.insert(0, PYTHON_LY)

KNOWN_FIXES = [
    {
        # ly/music/read.py:646, in Reader.handle_repeat. The guard tests the
        # BOUND METHOD `item.specifier`, which is always truthy, where the line
        # above it and the branch body both use the FIELD `item._specifier`. The
        # branch is therefore dead and a QUOTED repeat specifier is never read:
        # `\repeat "unfold" 5 { ... }` makes the repeat take the String as its
        # one child and END there, spilling the count and the whole body into
        # the surrounding music and leaving the repeat's length at 0. Comparing
        # the field -- plainly what upstream meant -- gives specifier "unfold",
        # count 5, the music as the repeat's one child, and length 5 x body.
        'module': 'ly.music.read',
        'path': 'ly/music/read.py',
        'old': 'elif not item.specifier and isinstance(t, lex.StringStart):',
        'new': 'elif not item._specifier and isinstance(t, lex.StringStart):',
        'why': 'handle_repeat guards on the bound method item.specifier instead '
               'of the field item._specifier, so a quoted repeat specifier is '
               'never read and the repeat ends at the string (FR14)',
    },
]


def apply_known_fixes():
    """Loads each patched module in place of the reference's own.

    The reference checkout is READ-ONLY: the source is read, the declared
    one-line substitution is made in memory, and the result is compiled under
    the ORIGINAL file name (so line numbers and tracebacks still point at the
    real file) into a module registered under the real module name. Anything
    importing it afterwards -- ly.music.items does `from .read import Reader`
    inside Document.__init__, at call time -- gets the patched one.
    """
    for fix in KNOWN_FIXES:
        path = os.path.join(PYTHON_LY, fix['path'])
        with open(path, encoding='utf-8') as handle:
            source = handle.read()

        found = source.count(fix['old'])
        if found != 1:
            raise SystemExit(
                'KNOWN_FIXES: {0} occurrences of the patched line in {1} '
                '(expected exactly 1). The reference has moved; re-verify the '
                'defect before regenerating.\n  line: {2}'.format(
                    found, fix['path'], fix['old']))

        module = types.ModuleType(fix['module'])
        module.__file__ = path
        module.__package__ = fix['module'].rsplit('.', 1)[0]
        exec(compile(source.replace(fix['old'], fix['new']), path, 'exec'),
             module.__dict__)
        sys.modules[fix['module']] = module
        print('KNOWN FIX applied: {0} -- {1}'.format(fix['module'], fix['why']))


apply_known_fixes()

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
        'known_fixes': [
            {'module': f['module'], 'old': f['old'], 'new': f['new'], 'why': f['why']}
            for f in KNOWN_FIXES
        ],
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
