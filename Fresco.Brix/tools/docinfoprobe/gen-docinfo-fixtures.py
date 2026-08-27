#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): dumps what python-ly's ly.docinfo
harvests from a set of probe .ly files into JSON fixtures the
Fresco.Brix.Ly.Tests DocInfo parity tests replay.

Runs against the READ-ONLY python-ly v0.9.10 checkout (the reference the port
must match). Each fixture is <name>.docinfo.json beside a copy of <name>.ly,
so the fixture set is self-contained.

Usage: PYTHONDONTWRITEBYTECODE=1 python3 gen-docinfo-fixtures.py <out-dir> <ly-file>...
"""
import json
import os
import sys

sys.path.insert(0, os.path.expanduser('~/ClaudeHome/python-ly'))

import ly.document  # noqa: E402
import ly.docinfo  # noqa: E402


def class_name(cls):
    """The token class as the port names it: module tail plus class name."""
    return '{0}.{1}'.format(cls.__module__.rsplit('.', 1)[-1], cls.__name__)


def token_list(tokens):
    """Tokens as [text, pos] pairs (a python-ly token IS its text)."""
    return [[str(t), t.pos] for t in tokens]


def harvest(path, out_dir):
    with open(path, encoding='utf-8') as handle:
        text = handle.read().replace('\r', '')
    doc = ly.document.Document(text)
    info = ly.docinfo.DocInfo(doc)

    counted = {}
    for cls, count in info.counted_tokens().items():
        counted[class_name(cls)] = count

    data = {
        'mode': info.mode(),
        'token_count': len(info.tokens),
        'version_string': info.version_string(),
        'version': list(info.version()),
        'include_args': list(info.include_args()),
        'scheme_load_args': list(info.scheme_load_args()),
        'output_args': [list(pair) for pair in info.output_args()],
        'definitions': token_list(info.definitions()),
        'markup_definitions': token_list(info.markup_definitions()),
        'language': info.language(),
        'global_staff_size': info.global_staff_size(),
        'complete': info.complete(),
        'has_output': info.has_output(),
        'counted_tokens': counted,
    }

    name = os.path.splitext(os.path.basename(path))[0]
    with open(os.path.join(out_dir, name + '.docinfo.json'), 'w', encoding='utf-8') as out:
        json.dump(data, out, indent=1, sort_keys=True)
        out.write('\n')
    with open(os.path.join(out_dir, name + '.ly'), 'w', encoding='utf-8') as out:
        out.write(text)
    return data['token_count']


def main():
    out_dir = sys.argv[1]
    os.makedirs(out_dir, exist_ok=True)
    total = 0
    for path in sys.argv[2:]:
        count = harvest(path, out_dir)
        print('{0}: {1} tokens'.format(os.path.basename(path), count))
        total += count
    print('TOTAL {0} tokens over {1} files'.format(total, len(sys.argv) - 2))


if __name__ == '__main__':
    main()
