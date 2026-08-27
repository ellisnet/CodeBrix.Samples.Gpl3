#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): runs Frescobaldi's OWN MIDI note-entry
code and writes the answers as fixtures the Fresco.Brix.Core.Tests parity test
replays.

Two of upstream's traps meet here, and each half needs a different one:

  midiinput/elements.py  imports PyQt6 and ly.pitch and is otherwise pure, so
                         tools/scorewizprobe/qtshim.py is REUSED as a module
                         (board trap 46) and upstream's Note, Chord,
                         NoteMappings and NoteMapping run UNCHANGED. Two small
                         additions on top of the shim: QApplication
                         .keyboardModifiers(), which Note.output() consults for
                         the Shift-held octave check, and Qt.KeyboardModifier
                         .ShiftModifier to compare it against.

  midiinput/__init__.py  imports midihub, which imports portmidi, which is not
                         installed and never will be (FR6). Its LY_REG_EXPR --
                         the pattern re-pitch mode replaces with -- is lifted
                         out by AST and compiled on its own (board trap 21, the
                         tools/varprobe pattern).

Usage: PYTHONDONTWRITEBYTECODE=1 python3 gen-midiinput-fixtures.py [out-dir]
"""
import ast
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PYTHON_LY = os.path.expanduser('~/ClaudeHome/python-ly')
FRESCOBALDI = os.path.expanduser('~/GitHome/frescobaldi/frescobaldi')
DEFAULT_OUT = os.path.normpath(os.path.join(
    HERE, '..', '..', 'tests', 'Fresco.Brix.Core.Tests', 'fixtures'))

sys.path.insert(0, os.path.join(HERE, '..', 'scorewizprobe'))
import qtshim  # noqa: E402

qtshim.install(PYTHON_LY, FRESCOBALDI)

import PyQt6.QtCore as _QtCore        # noqa: E402
import PyQt6.QtWidgets as _QtWidgets  # noqa: E402


class _KeyboardModifier:
    """Just the one modifier Note.output() asks about."""
    ShiftModifier = 1
    NoModifier = 0


_QtCore.Qt.KeyboardModifier = _KeyboardModifier

#Note.output() reads this at the moment the note arrives; the probe drives it.
SHIFT_HELD = [False]
_QtWidgets.QApplication.keyboardModifiers = staticmethod(
    lambda: 1 if SHIFT_HELD[0] else 0)

import ly.pitch  # noqa: E402


def load_elements():
    """Loads midiinput/elements.py WITHOUT importing the package around it.

    `midiinput/__init__.py` imports QThread and midihub, and midihub imports
    portmidi, which is not installed and never will be (FR6) -- so importing
    `midiinput.elements` the ordinary way drags in a module that cannot load.
    elements.py itself imports only PyQt6 (shimmed above) and ly.pitch, so the
    source is compiled under its OWN file name into a module of its own and
    upstream's classes then run unchanged.
    """
    import types
    path = os.path.join(FRESCOBALDI, 'midiinput', 'elements.py')
    with open(path, encoding='utf-8') as handle:
        source = handle.read()

    module = types.ModuleType('midiinput_elements')
    module.__file__ = path
    exec(compile(source, path, 'exec'), module.__dict__)
    return module


elements = load_elements()


def lift_regex():
    """Lifts LY_REG_EXPR out of midiinput/__init__.py without importing it."""
    path = os.path.join(FRESCOBALDI, 'midiinput', '__init__.py')
    with open(path, encoding='utf-8') as handle:
        source = handle.read()

    tree = ast.parse(source, path)
    wanted = {'LY_REG_EXPR', 'NOTE_OFF_EVENT', 'NOTE_ON_EVENT'}
    picked = [node for node in tree.body
              if isinstance(node, ast.Assign)
              and any(isinstance(t, ast.Name) and t.id in wanted
                      for t in node.targets)]
    if len(picked) != len(wanted):
        raise SystemExit(
            f'expected {len(wanted)} lifted assignments, found {len(picked)} -- '
            'the reference has moved')

    namespace = {}
    exec(compile(ast.Module(body=picked, type_ignores=[]), path, 'exec'),
         {'re': __import__('re')}, namespace)
    return namespace


LIFTED = lift_regex()

LANGUAGES = ['nederlands', 'english', 'deutsch', 'italiano', 'catalan',
             'espanol', 'norsk', 'svenska', 'portugues', 'vlaams', 'suomi']

#A tune that walks up, walks down, leaps more than a fourth in both directions
#(which is where relative octaves actually get decided) and repeats a note.
RELATIVE_SEQUENCE = [60, 62, 64, 65, 67, 65, 64, 62, 60, 72, 48, 60, 60, 71, 61,
                     73, 59, 84, 36, 61]

CHORDS = [
    [60, 64, 67],            # C major, played bottom up
    [67, 64, 60],            # the same chord played top down
    [61, 65, 68],            # black keys
    [48, 60, 64, 67, 72],    # wide spacing
    [60, 60, 64],            # a doubled note, which sorted() keeps stable
    [59, 62, 65, 69],        # a leading-note seventh
]

REPITCH_TEXTS = [
    "c4 d e f | g a b c |",
    "\\relative c' { c4 d8 e r4 f | g2 a4 b }",
    "<c e g>4 <d f a>8 r8 c2",
    "  \\key c \\major\n  c4^\\markup { hi } d-. e_5 f\n",
    "cis'4 des,,8 ees16 fis32 r64 g'''",
    "R1*4 s2 r4 c4",
    "\\override NoteHead.color = #red c4",
    "a4 \\bar \"|.\"",
    "",
    "   ",
]


def note_output(midinote, keysig, sharps, language, shift):
    """One note, written the way upstream writes it."""
    SHIFT_HELD[0] = shift
    mapping = elements.NoteMapping(keysig, sharps)
    return elements.Note(midinote, mapping).output(False, language)


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUT
    os.makedirs(out_dir, exist_ok=True)

    #1. The fifteen tables, both preferences.
    mappings = []
    for sharps in (True, False):
        for keysig in range(15):
            mapping = elements.NoteMapping(keysig, sharps)
            mappings.append({
                'key_signature': keysig,
                'sharps': sharps,
                'entries': [[mapping[i][0], str(mapping[i][1])] for i in range(12)],
            })

    #2. Every MIDI note, in every key signature, in the default language --
    #plus every language at C major, and the Shift-held octave check.
    notes = []
    for sharps in (True, False):
        for keysig in range(15):
            notes.append({
                'key_signature': keysig,
                'sharps': sharps,
                'language': 'nederlands',
                'shift': False,
                'outputs': [note_output(n, keysig, sharps, 'nederlands', False)
                            for n in range(128)],
            })
    for language in LANGUAGES:
        for sharps in (True, False):
            notes.append({
                'key_signature': 7,
                'sharps': sharps,
                'language': language,
                'shift': False,
                'outputs': [note_output(n, 7, sharps, language, False)
                            for n in range(128)],
            })
    notes.append({
        'key_signature': 7,
        'sharps': True,
        'language': 'nederlands',
        'shift': True,
        'outputs': [note_output(n, 7, True, 'nederlands', True)
                    for n in range(128)],
    })

    #3. Relative mode, which is STATEFUL: Note.LastPitch carries from note to
    #note, so each run resets it and records the whole sequence in order.
    relative = []
    for sharps in (True, False):
        for keysig in (0, 3, 7, 11, 14):
            for language in ('nederlands', 'english', 'deutsch'):
                for shift in (False, True):
                    elements.Note.LastPitch = ly.pitch.Pitch()
                    SHIFT_HELD[0] = shift
                    mapping = elements.NoteMapping(keysig, sharps)
                    outputs = [elements.Note(n, mapping).output(True, language)
                               for n in RELATIVE_SEQUENCE]
                    relative.append({
                        'key_signature': keysig,
                        'sharps': sharps,
                        'language': language,
                        'shift': shift,
                        'sequence': RELATIVE_SEQUENCE,
                        'outputs': outputs,
                        'last_pitch': [elements.Note.LastPitch.note,
                                       elements.Note.LastPitch.octave],
                    })

    #4. Chords, in both modes. A chord in relative mode leaves LastPitch at its
    #LOWEST note, which is what the note after it is written against, so each
    #run records a following note too.
    chords = []
    for relmode in (False, True):
        for sharps in (True, False):
            for keysig in (0, 7, 14):
                for notes_played in CHORDS:
                    elements.Note.LastPitch = ly.pitch.Pitch()
                    SHIFT_HELD[0] = False
                    mapping = elements.NoteMapping(keysig, sharps)
                    chord = elements.Chord()
                    for n in notes_played:
                        chord.add(elements.Note(n, mapping))
                    text = chord.output(relmode, 'nederlands')
                    after = elements.Note(60, mapping).output(relmode, 'nederlands')
                    chords.append({
                        'key_signature': keysig,
                        'sharps': sharps,
                        'relative': relmode,
                        'notes': notes_played,
                        'output': text,
                        'next_note': after,
                    })

    #5. What re-pitch mode replaces, searched the way upstream searches it: in
    #the SLICE from the caret onwards, so a lookbehind cannot see before it.
    repitch = []
    for text in REPITCH_TEXTS:
        for caret in range(0, len(text) + 1):
            match = LIFTED['LY_REG_EXPR'].search(text[caret:])
            repitch.append({
                'text': text,
                'caret': caret,
                'start': None if match is None else caret + match.start(),
                'end': None if match is None else caret + match.end(),
                'matched': None if match is None else match.group(0),
            })

    record = {
        'generated_by': 'tools/midiinputprobe/gen-midiinput-fixtures.py',
        'oracle': 'frescobaldi/midiinput/elements.py (imported and called '
                  'through the scorewizprobe Qt shim) + LY_REG_EXPR lifted from '
                  'midiinput/__init__.py by AST',
        'note_off_event': LIFTED['NOTE_OFF_EVENT'],
        'note_on_event': LIFTED['NOTE_ON_EVENT'],
        'pattern': LIFTED['LY_REG_EXPR'].pattern,
        'relative_sequence': RELATIVE_SEQUENCE,
        'mappings': mappings,
        'notes': notes,
        'relative': relative,
        'chords': chords,
        'repitch': repitch,
    }

    out_path = os.path.join(out_dir, 'midiinput.json')
    with open(out_path, 'w', encoding='utf-8') as handle:
        json.dump(record, handle, indent=1, ensure_ascii=False)
        handle.write('\n')

    print(f'{len(mappings)} mappings, {len(notes)} note runs '
          f'({sum(len(n["outputs"]) for n in notes)} notes), '
          f'{len(relative)} relative runs, {len(chords)} chords, '
          f'{len(repitch)} re-pitch probes')
    print(f'-> {out_path}')


if __name__ == '__main__':
    main()
