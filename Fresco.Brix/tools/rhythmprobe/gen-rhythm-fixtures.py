#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): runs every ly.rhythm operation of
python-ly over a set of probe .ly files and records what each one produced,
as JSON fixtures the Fresco.Brix.Ly.Tests rhythm parity tests replay.

Runs against the READ-ONLY python-ly v0.9.10 checkout (the reference the port
must match). Each fixture is <name>.rhythm.json beside a copy of <name>.ly.
Every operation runs on a FRESH document over the whole file.

Usage: PYTHONDONTWRITEBYTECODE=1 python3 gen-rhythm-fixtures.py <out-dir> <ly-file>...
"""
import json
import os
import sys

sys.path.insert(0, os.path.expanduser('~/ClaudeHome/python-ly'))

import ly.document  # noqa: E402
import ly.rhythm  # noqa: E402

OVERWRITE_DURATIONS = ['4', '8', '', '16.']

OPERATIONS = [
    ('double', ly.rhythm.rhythm_double),
    ('halve', ly.rhythm.rhythm_halve),
    ('dot', ly.rhythm.rhythm_dot),
    ('undot', ly.rhythm.rhythm_undot),
    ('remove_scaling', ly.rhythm.rhythm_remove_scaling),
    ('remove_fraction_scaling', ly.rhythm.rhythm_remove_fraction_scaling),
    ('remove', ly.rhythm.rhythm_remove),
    ('implicit', ly.rhythm.rhythm_implicit),
    ('implicit_per_line', ly.rhythm.rhythm_implicit_per_line),
    ('explicit', ly.rhythm.rhythm_explicit),
]


def cursor_for(text):
    doc = ly.document.Document(text)
    return doc, ly.document.Cursor(doc)


def items_dump(text):
    """The music_items structure, as [pos, end, insert_pos, may_remove,
    tokens, duration tokens] rows."""
    doc, cursor = cursor_for(text)
    rows = []
    for item in ly.rhythm.music_items(cursor):
        rows.append([
            item.pos,
            item.end,
            item.insert_pos,
            bool(item.may_remove),
            [str(t) for t in item.tokens],
            [str(t) for t in item.dur_tokens],
        ])
    return rows


def harvest(path, out_dir):
    with open(path, encoding='utf-8') as handle:
        text = handle.read().replace('\r', '')

    results = {}
    for name, operation in OPERATIONS:
        doc, cursor = cursor_for(text)
        operation(cursor)
        results[name] = doc.plaintext()

    doc, cursor = cursor_for(text)
    ly.rhythm.rhythm_overwrite(cursor, OVERWRITE_DURATIONS)
    results['overwrite'] = doc.plaintext()

    doc, cursor = cursor_for(text)
    extracted = ly.rhythm.rhythm_extract(cursor)

    data = {
        'overwrite_durations': OVERWRITE_DURATIONS,
        'music_items': items_dump(text),
        'extract': list(extracted),
        'results': results,
    }

    name = os.path.splitext(os.path.basename(path))[0]
    with open(os.path.join(out_dir, name + '.rhythm.json'), 'w', encoding='utf-8') as out:
        json.dump(data, out, indent=1, sort_keys=True)
        out.write('\n')
    with open(os.path.join(out_dir, name + '.ly'), 'w', encoding='utf-8') as out:
        out.write(text)
    return len(data['music_items'])


def main():
    out_dir = sys.argv[1]
    os.makedirs(out_dir, exist_ok=True)
    total = 0
    for path in sys.argv[2:]:
        count = harvest(path, out_dir)
        print('{0}: {1} music items'.format(os.path.basename(path), count))
        total += count
    print('TOTAL {0} music items over {1} files'.format(total, len(sys.argv) - 2))


if __name__ == '__main__':
    main()
