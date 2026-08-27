#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): runs Frescobaldi's OWN hyphenator over
every bundled dictionary and writes the answers as fixtures the
Fresco.Brix.Core.Tests parity test replays.

frescobaldi/hyphenator.py imports NOTHING but codecs and re -- no PyQt, no
Frescobaldi -- so unlike variables.py (tools/varprobe) it needs no AST lifting:
it is imported straight out of the READ-ONLY checkout and called. The fixtures
are therefore literally upstream's answers, computed by upstream's code.

The dictionaries read are the copies in this repository's assets folder, not
the checkout's -- they are the files the application actually ships, and any
divergence between the two would be a copying mistake worth catching.

Usage: PYTHONDONTWRITEBYTECODE=1 python3 gen-hyphen-fixtures.py [out-dir]
"""
import importlib.util
import json
import os
import sys

HYPHENATOR = os.path.expanduser('~/GitHome/frescobaldi/frescobaldi/hyphenator.py')
HERE = os.path.dirname(os.path.abspath(__file__))
DICTS = os.path.normpath(os.path.join(
    HERE, '..', '..', 'src', 'Fresco.Brix.Core', 'assets', 'hyphdicts'))
DEFAULT_OUT = os.path.normpath(os.path.join(
    HERE, '..', '..', 'tests', 'Fresco.Brix.Core.Tests', 'fixtures'))

# Words every dictionary is asked about, so each one is really exercised, plus
# words that belong to the language of a particular dictionary. Nothing here
# needs to BE a word of the dictionary's language: a pattern set answers for
# any string, and what is being checked is that both implementations answer the
# same thing.
COMMON = [
    'hyphenation', 'lettergrepen', 'beautiful', 'computer', 'international',
    'mississippi', 'strength', 'a', 'ab', 'abc', 'abcd', 'aaaaaaaa',
    'PROJECT', 'Project', 'oxygen', 'university', 'programming',
    'concatenation', 'extraordinary', 'onomatopoeia', 'rhythm', 'crescendo',
    'allegro', 'andante', 'sostenuto', 'pianissimo', 'accelerando',
]

PER_LANGUAGE = {
    'nl_NL': ['lettergrepen', 'woordafbreking', 'schoonheid', 'aanvankelijk'],
    'de_DE': ['Silbentrennung', 'Schifffahrt', 'Freundschaft', 'Donaudampfschiff'],
    'en_US': ['hyphenation', 'wonderful', 'incomprehensible', 'representation'],
    'en_GB': ['hyphenation', 'colourful', 'organisation'],
    'en_CA': ['hyphenation', 'neighbour'],
    'fr': ['chanson', 'merveilleux', 'anticonstitutionnellement'],
    'it': ['bellissima', 'crescendo', 'meraviglioso'],
    'es_ES': ['maravilloso', 'cancion', 'trabajador'],
    'pt_BR': ['maravilhoso', 'trabalhador'],
    'pt_PT': ['maravilhoso', 'trabalhador'],
    'sv_SE': ['underbart', 'stavelse'],
    'da_DK': ['stavelse', 'vidunderlig'],
    'nn_NO': ['stavelse', 'vidunderleg'],
    'fi_FI': ['tavutus', 'ihmeellinen'],
    'is_IS': ['vidundur'],
    'ga_IE': ['iontach'],
    'id_ID': ['pemenggalan', 'indah'],
    'pl': ['przepiekny', 'sylaba'],
    'cs_CZ': ['nadherny', 'slabika'],
    'sk_SK': ['nadherny', 'slabika'],
    'hu': ['gyonyoru', 'szotag'],
    'el_GR': ['omorfos'],
    'ru_RU': ['prekrasno'],
    'uk_UA': ['prekrasno'],
}


# None of the 24 bundled dictionaries carries a non-standard alternative -- a
# pattern with a "/spelling" suffix, which says that breaking there rewrites
# some letters (German's ff -> ff-f is the classic). The code path is ported all
# the same, because a dictionary found in /usr/share/hyphen may well use it, so
# these tiny made-up dictionaries exercise it. The hex escape is real: the
# Swedish dictionary uses ^^hh.
SYNTHETIC = [
    ('alternative-short', 'ISO8859-1\nf1f/f=f,1,2\n', ['schiffahrt', 'ffff', 'off']),
    ('alternative-long', 'ISO8859-1\nf1f/ff=f,1,2\n', ['schiffahrt', 'ffff', 'off']),
    ('alternative-nocut', 'ISO8859-1\nf1f/f=f\n', ['schiffahrt', 'ffff']),
    ('alternative-dotted', 'ISO8859-1\n.a1b/b=b,1,1\n', ['abab', 'ab']),
    ('hex-escape', 'ISO8859-1\na1^^e4\n', ['a\u00e4a\u00e4', 'aa']),
    ('comments-and-blanks', 'ISO8859-1\n% a comment\n\na1b\n', ['abab']),
]


def load_hyphenator():
    spec = importlib.util.spec_from_file_location('fresco_hyphenator', HYPHENATOR)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUT
    hyphenator = load_hyphenator()

    files = sorted(f for f in os.listdir(DICTS) if f.endswith('.dic'))
    fixtures = []
    for name in files:
        language = name[5:-4]
        path = os.path.join(DICTS, name)
        dictionary = hyphenator.HyphenationDictionary(path)
        h = hyphenator.Hyphenator(path, left=2, right=2)

        words = list(COMMON) + PER_LANGUAGE.get(language, [])
        probes = []
        for word in words:
            positions = h.positions(word)
            probes.append({
                'word': word,
                # The margin-free answer, so the C# margin filter is checked too.
                'raw': [int(p) for p in dictionary.positions(word)],
                'positions': [int(p) for p in positions],
                # The data attribute, where a dictionary spells a break other
                # than as a hyphen. None of the bundled ones do, but a user's
                # own dictionary may, and the code path is ported either way.
                'data': [list(p.data) if p.data else None for p in positions],
                'inserted': h.inserted(word, ' -- '),
                'plain': h.inserted(word),
                'iterate': [list(pair) for pair in h.iterate(word)],
            })

        fixtures.append({
            'language': language,
            'file': name,
            'text': None,
            'left': 2,
            'right': 2,
            'patterns': len(dictionary.patterns),
            'maxlen': dictionary.maxlen,
            'probes': probes,
        })

    # The made-up dictionaries, written to a scratch file because upstream's
    # reader only takes a path.
    import tempfile
    for name, text, words in SYNTHETIC:
        handle, path = tempfile.mkstemp(suffix='.dic')
        with os.fdopen(handle, 'w', encoding='latin-1') as scratch:
            scratch.write(text)
        try:
            dictionary = hyphenator.HyphenationDictionary(path)
            h = hyphenator.Hyphenator(path, left=1, right=1, cache=False)
            probes = []
            for word in words:
                positions = h.positions(word)
                probes.append({
                    'word': word,
                    'raw': [int(p) for p in dictionary.positions(word)],
                    'positions': [int(p) for p in positions],
                    'data': [list(p.data) if p.data else None for p in positions],
                    'inserted': h.inserted(word, ' -- '),
                    'plain': h.inserted(word),
                    'iterate': [list(pair) for pair in h.iterate(word)],
                })

            fixtures.append({
                'language': name,
                'file': None,
                'text': text,
                'left': 1,
                'right': 1,
                'patterns': len(dictionary.patterns),
                'maxlen': dictionary.maxlen,
                'probes': probes,
            })
        finally:
            os.unlink(path)

    target = os.path.join(out_dir, 'hyphenation.json')
    with open(target, 'w', encoding='utf-8') as handle:
        json.dump(fixtures, handle, ensure_ascii=False, indent=1, sort_keys=True)
        handle.write('\n')

    total = sum(len(f['probes']) for f in fixtures)
    print('wrote %s (%d dictionaries, %d probes)' % (target, len(fixtures), total))


if __name__ == '__main__':
    sys.exit(main())
