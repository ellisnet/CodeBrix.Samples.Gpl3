#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): builds a set of documents with
python-ly's ly.dom and records what its Printer produced, as a JSON fixture
the Fresco.Brix.Ly.Tests dom parity tests replay.

Runs against the READ-ONLY python-ly v0.9.10 checkout (the reference the port
must match). ly.dom BUILDS documents rather than reading them, so parity is
checked by constructing the same tree on both sides and comparing the printed
output — the scenarios below are mirrored one for one in DomParityTests.

Usage: PYTHONDONTWRITEBYTECODE=1 python3 gen-dom-fixtures.py <out-file>
"""
import json
import os
import sys
from fractions import Fraction

sys.path.insert(0, os.path.expanduser('~/ClaudeHome/python-ly'))

import ly.dom as dom  # noqa: E402

# NOTE: ly.dom.ContextProperty is not exercised here. Its __init__ never
# calls super().__init__(), so the node has no _parent and appending it to
# any parent raises AttributeError in python-ly — there is no reference
# behaviour to record. The port initializes it properly and covers it with
# its own test.


def scenario_document():
    d = dom.Document()
    dom.Version('2.27.2', d)
    dom.Include('articulate.ly', d)
    dom.BlankLine(d)
    a = dom.Assignment('melody', d)
    s = dom.Seq(a)
    dom.Text("c'4 d' e' f'", s)
    return d


def scenario_header():
    h = dom.Header()
    h['title'] = "A Title"
    h['composer'] = "Johann Sebastian Bach"
    h['tagline'] = dom.Scheme('#f')
    return h


def scenario_score():
    score = dom.Score()
    sim = dom.Sim(score)
    staff = dom.Staff(parent=sim)
    staff.getWith()['instrumentName'] = "Violin"
    seq = dom.Seq(staff)
    dom.Clef('treble', seq)
    dom.TimeSignature(3, 4, seq)
    dom.KeySignature(4, Fraction(0), 'minor', seq)
    dom.Partial(3, 0, 1, seq)
    dom.Text("c'4 d' e'", seq)
    layout = dom.Layout(score)
    ctx = dom.Context('Staff', layout)
    ctx['fontSize'] = dom.Scheme('-2')
    midi = dom.Midi(score)
    midi['tempoWholesPerMinute'] = dom.Scheme('(ly:make-moment 100 4)')
    return score


def scenario_brackets():
    d = dom.Document()
    # Seqr with a single atom loses its brackets, Seq keeps them
    a = dom.Assignment('one', d)
    r = dom.Seqr(a)
    dom.Identifier('someMusic', r)
    b = dom.Assignment('two', d)
    s = dom.Seq(b)
    dom.Identifier('someMusic', s)
    c = dom.Assignment('three', d)
    sr = dom.Simr(c)
    dom.Text("c'4", sr)
    dom.Text("d'4", sr)
    e = dom.Assignment('four', d)
    dom.Seq(e)  # empty
    return d


def scenario_chords():
    seq = dom.Seq()
    chord = dom.Chord(seq)
    dom.Pitch(1, 0, 0, chord)
    dom.Pitch(1, 2, 0, chord)
    dom.Pitch(1, 4, Fraction(-1), chord)
    dom.Duration(2, 1, 1, chord)
    single = dom.Chord(seq)
    dom.Pitch(-2, 6, Fraction(1, 2), single)
    dom.Duration(0, 0, Fraction(2, 3), single)
    td = dom.TextDur("r", seq)
    dom.Duration(3, 0, 1, td)
    return seq


def scenario_markup():
    m = dom.Markup()
    dom.MarkupEnclosed('bold', m)
    dom.QuotedString('a "quoted" word', dom.MarkupEnclosed('italic', m))
    cmd = dom.MarkupCommand('hspace', m)
    dom.Text('2', cmd)
    return m


def scenario_lyrics():
    sim = dom.Sim()
    voice = dom.Voice('melody', True, sim)
    dom.Text("c'4 d' e' f'", dom.Seq(voice))
    lyrics = dom.Lyrics(parent=sim)
    to = dom.LyricsTo('melody', lyrics)
    dom.Text('Sing a song of six -- pence', to)
    add = dom.AddLyrics(sim)
    dom.Text('An -- oth -- er verse here', add)
    return sim


def scenario_comments():
    d = dom.Document()
    dom.LineComment('a full-line comment', d)
    dom.Comment('a trailing comment', d)
    dom.BlockComment('a block\ncomment %} with an end marker', d)
    dom.BlockComment('a short block', d)
    dom.QuotedString("he said \"hello\" and 'goodbye'", d)
    return d


def scenario_contexts():
    sim = dom.Sim()
    user = dom.UserContext('MyStaff', 'up', True, sim)
    dom.Text("c'1", dom.Seq(user))
    sc = dom.ScoreContext(None, False, sim)
    dom.Text("d'1", dom.Seq(sc))
    piano = dom.PianoStaff('pf', True, sim)
    piano.addInstrumentNameEngraverIfNecessary()
    grand = dom.GrandStaff(parent=sim)
    grand.addInstrumentNameEngraverIfNecessary()
    return sim


def scenario_tempo():
    seq = dom.Seq()
    dom.Tempo(4, 100, seq)
    t = dom.Tempo(2, 60, seq)
    dom.QuotedString('Allegro', t)
    dom.Tempo(4, None, seq)
    dom.Mark(seq).append(dom.Scheme('#f'))
    return seq


def scenario_reference():
    ref = dom.Reference('melodyName')
    d = dom.Document()
    a = dom.Assignment(ref, d)
    dom.Seq(a).append(dom.Text("c'4"))
    dom.Identifier(ref, d)
    ref.name = 'renamedMelody'
    return d


SCENARIOS = [
    ('document', scenario_document),
    ('header', scenario_header),
    ('score', scenario_score),
    ('brackets', scenario_brackets),
    ('chords', scenario_chords),
    ('markup', scenario_markup),
    ('lyrics', scenario_lyrics),
    ('comments', scenario_comments),
    ('contexts', scenario_contexts),
    ('tempo', scenario_tempo),
    ('reference', scenario_reference),
]


def main():
    out_path = sys.argv[1]
    results = {}
    for name, build in SCENARIOS:
        node = build()
        printer = dom.Printer()
        plain = dom.Printer()
        plain.typographicalQuotes = False
        results[name] = {
            'indented': printer.indent(node),
            'ly': node.ly(printer),
            'plain_quotes': plain.indent(node),
            'before': node.before,
            'after': node.after,
        }
    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, 'w', encoding='utf-8') as out:
        json.dump(results, out, indent=1, sort_keys=True)
        out.write('\n')
    print('{0} scenarios written to {1}'.format(len(results), out_path))


if __name__ == '__main__':
    main()
