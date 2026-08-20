#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): runs every ly.pitch operation of
python-ly over a set of probe .ly files and records what each one produced, as
JSON fixtures the Fresco.Brix.Ly.Tests pitch parity tests replay.

Runs against the READ-ONLY python-ly v0.9.10 checkout (the reference the port
must match). Each fixture is <name>.pitch.json beside a copy of <name>.ly.
Every operation runs on a FRESH document over the whole file.

Usage: PYTHONDONTWRITEBYTECODE=1 python3 gen-pitch-fixtures.py <out-dir> <ly-file>...
"""
import json
import os
import sys
from fractions import Fraction

sys.path.insert(0, os.path.expanduser('~/ClaudeHome/python-ly'))

import ly.document  # noqa: E402
import ly.pitch  # noqa: E402
import ly.pitch.abs2rel  # noqa: E402
import ly.pitch.rel2abs  # noqa: E402
import ly.pitch.transform  # noqa: E402
import ly.pitch.translate  # noqa: E402
import ly.pitch.transpose  # noqa: E402

# The mode definitions Frescobaldi's pitch dialog offers (dialog.py), used here
# so the ModeShifter probe runs on a scale a user could actually pick.
MAJOR = ((0, 0), (1, 1), (2, 2), (3, Fraction(5, 2)), (4, Fraction(7, 2)),
         (5, Fraction(9, 2)), (6, Fraction(11, 2)))
MINOR_HARMONIC = ((0, 0), (1, 1), (2, Fraction(3, 2)), (3, Fraction(5, 2)),
                  (4, Fraction(7, 2)), (5, 4), (6, Fraction(11, 2)))


def pitch(note, alter=0, octave=0):
    return ly.pitch.Pitch(note, Fraction(alter), octave)


def cursor_for(text):
    doc = ly.document.Document(text)
    return doc, ly.document.Cursor(doc)


def transposed(text, transposer, language='nederlands', relative_first_absolute=False):
    doc, cursor = cursor_for(text)
    ly.pitch.transpose.transpose(
        cursor, transposer, language, relative_first_absolute)
    return doc.plaintext()


def harvest(path, out_dir):
    with open(path, encoding='utf-8') as handle:
        text = handle.read().replace('\r', '')

    results = {}

    # c -> d (a whole tone up) and c -> a, (a minor third down)
    results['transpose_c_d'] = transposed(
        text, ly.pitch.transpose.Transposer(pitch(0), pitch(1)))
    results['transpose_c_a_down'] = transposed(
        text, ly.pitch.transpose.Transposer(pitch(0), pitch(5, 0, -1)))
    results['transpose_c_ees'] = transposed(
        text, ly.pitch.transpose.Transposer(pitch(0), pitch(2, -1)))
    results['transpose_c_d_relative_absolute'] = transposed(
        text, ly.pitch.transpose.Transposer(pitch(0), pitch(1)),
        relative_first_absolute=True)
    results['simplify'] = transposed(text, ly.pitch.transpose.Simplifier())
    results['modal_up_two_c_major'] = transposed(
        text, ly.pitch.transpose.ModalTransposer(
            2, ly.pitch.transpose.ModalTransposer.getKeyIndex('C')))
    results['modal_down_three_g_major'] = transposed(
        text, ly.pitch.transpose.ModalTransposer(
            -3, ly.pitch.transpose.ModalTransposer.getKeyIndex('G')))
    results['mode_shift_c_major'] = transposed(
        text, ly.pitch.transpose.ModeShifter(pitch(0), MAJOR))
    results['mode_shift_a_minor_harmonic'] = transposed(
        text, ly.pitch.transpose.ModeShifter(pitch(5), MINOR_HARMONIC))

    for name, kwargs in (
            ('rel2abs', {}),
            ('rel2abs_first_absolute', {'first_pitch_absolute': True}),
    ):
        doc, cursor = cursor_for(text)
        ly.pitch.rel2abs.rel2abs(cursor, 'nederlands', **kwargs)
        results[name] = doc.plaintext()

    for name, kwargs in (
            ('abs2rel', {}),
            ('abs2rel_no_startpitch', {'startpitch': False}),
            ('abs2rel_no_startpitch_first_absolute',
             {'startpitch': False, 'first_pitch_absolute': True}),
    ):
        doc, cursor = cursor_for(text)
        ly.pitch.abs2rel.abs2rel(cursor, 'nederlands', **kwargs)
        results[name] = doc.plaintext()

    doc, cursor = cursor_for(text)
    ly.pitch.transform.retrograde(cursor)
    results['retrograde'] = doc.plaintext()

    doc, cursor = cursor_for(text)
    ly.pitch.transform.inversion(cursor)
    results['inversion'] = doc.plaintext()

    translations = {}
    for language in ('english', 'deutsch', 'italiano', 'norsk'):
        doc, cursor = cursor_for(text)
        changed = ly.pitch.translate.translate(cursor, language)
        translations[language] = {
            'text': doc.plaintext(),
            'changed': bool(changed),
        }

    data = {
        'results': results,
        'translations': translations,
    }

    name = os.path.splitext(os.path.basename(path))[0]
    with open(os.path.join(out_dir, name + '.pitch.json'), 'w', encoding='utf-8') as out:
        json.dump(data, out, indent=1, sort_keys=True)
        out.write('\n')
    with open(os.path.join(out_dir, name + '.ly'), 'w', encoding='utf-8') as out:
        out.write(text)
    return len(results) + len(translations)


def main():
    out_dir = sys.argv[1]
    os.makedirs(out_dir, exist_ok=True)
    total = 0
    for path in sys.argv[2:]:
        count = harvest(path, out_dir)
        print('{0}: {1} operations'.format(os.path.basename(path), count))
        total += count
    print('TOTAL {0} operations over {1} files'.format(total, len(sys.argv) - 2))


if __name__ == '__main__':
    main()
