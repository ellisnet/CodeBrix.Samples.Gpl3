#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): runs Frescobaldi's OWN midifile.song
over a corpus of real LilyPond-engraved MIDI files and writes the answers as
fixtures the Fresco.Brix.Core.Tests parity test replays.

frescobaldi/midifile/ imports NOTHING but the standard library -- collections,
struct and sys -- so it needs neither an AST lift (tools/varprobe) nor a Qt
costume (tools/scorewizprobe): it is imported straight out of the READ-ONLY
checkout and CALLED, which is board trap 49. The fixtures are therefore
literally upstream's answers, computed by upstream's code.

What is recorded per file is everything the port has to reproduce:

  division / ntracks / length   the header and the song's own length in msec
  tempo_times                   the TempoMap's (midi_time, usec-per-quarter)
                                list, including the 500000 default it inserts
  beats                         every beat: (msec, measnum, beat, num, den)
  music_times                   the msec of EVERY distinct event time in the
                                file, which is the tempo map applied tick by
                                tick -- the strongest check available of
                                real_time()'s piecewise arithmetic
  beat_queries                  Song.beat() at sampled times, including 0, the
                                exact time of every beat, one msec either side
                                of each, and past the end

Every file is probed in its OWN subprocess under a timeout, because upstream's
song.py can fail to return at all (board trap 48: a hang is not an answer). A
file it cannot answer is recorded as {"answered": false} with the reason, which
is what makes the fixture an honest oracle rather than a shorter one; the C#
parity test asserts the declared FR14 divergence for exactly those.

The corpus is the LilyPort regression harness's reference MIDI -- files a real
LilyPond wrote, carrying tempo changes, accelerandi, time-signature changes,
partial measures, multiple tracks and (deliberately) a file with no notes at
all -- PLUS the synthetic set from make-synthetic-midi.py, which covers what no
engraved file ever is: format 0 and 2, an SMPTE division, a missing tempo map,
a missing time signature, a first event of either kind arriving late, and two
tracks setting the tempo at the same instant. The .midi files are COPIED beside
the fixture so the C# test reads the same bytes upstream was given.

Usage: PYTHONDONTWRITEBYTECODE=1 python3 gen-midi-fixtures.py [out-dir]
       (run make-synthetic-midi.py first if tools/midiprobe/synthetic is empty)
"""
import json
import os
import shutil
import subprocess
import sys
import types

#Upstream can loop forever (see the SMPTE case), so no file gets longer than
#this to answer in.
TIMEOUT_SECONDS = 20

FRESCOBALDI = os.path.expanduser('~/GitHome/frescobaldi/frescobaldi')
HERE = os.path.dirname(os.path.abspath(__file__))
CORPUS = os.path.normpath(os.path.join(
    HERE, '..', '..', '..', 'CodeBrix.LilyPort', 'tools', 'regression-harness',
    'reference-midi', 'midi'))
SYNTHETIC = os.path.join(HERE, 'synthetic')
DEFAULT_OUT = os.path.normpath(os.path.join(
    HERE, '..', '..', 'tests', 'Fresco.Brix.Core.Tests', 'fixtures'))

sys.path.insert(0, FRESCOBALDI)

#The declared FR14 divergences the oracle is generated WITH -- the route the
#board calls (i) and W8a set the precedent for. The checkout stays read-only:
#the source is read, the one declared substitution is made in memory, and the
#result is compiled under the ORIGINAL file name into a module registered under
#the real name. The fixture records this list and the C# parity test asserts it,
#so a fix added here and not declared there fails.
KNOWN_FIXES = [
    {
        # midifile/song.py:173, in beats(). Every OTHER entry in time_sigs comes
        # from get_time_signature(), which returns the raw bytes of a MIDI Time
        # Signature event -- where byte 1 is dd, the POWER, so a quarter note is
        # 2. The default this line inserts writes 4 in that slot, which means a
        # SIXTEENTH note: a file with no time signature of its own gets a beat
        # grid four times too fine, four times too many measures, and an LCD
        # display reading "4/16" (widget.py builds it as f"{num}/{2 ** den}").
        # The other two members of the tuple, 24 and 8, are exactly 4/4's
        # standard clocks and 32nds-per-quarter, so what was meant is the
        # standard 4/4 default event and what was written is its numerator
        # twice.
        'module': 'midifile.song',
        'path': 'midifile/song.py',
        'old': 'time_sigs.insert(0, (0, (4, 4, 24, 8)))',
        'new': 'time_sigs.insert(0, (0, (4, 2, 24, 8)))',
        'why': "beats() inserts its default time signature with the numerator "
               "in the denominator byte, so a file with no time signature is "
               "gridded in 16ths and displays 4/16 instead of 4/4 (FR14)",
    },
]


def apply_known_fixes():
    """Loads each patched module in place of the reference's own."""
    import midifile  # noqa: F401  -- the package, so relative imports resolve

    for fix in KNOWN_FIXES:
        path = os.path.join(FRESCOBALDI, fix['path'])
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
        print('KNOWN FIX applied: {0} -- {1}'.format(fix['module'], fix['why']),
              file=sys.stderr)


import midifile.parser as parser  # noqa: E402

apply_known_fixes()

import midifile.song as song      # noqa: E402


def probe(path):
    """Returns the record of one MIDI file, as upstream's own code answers."""
    with open(path, 'rb') as handle:
        data = handle.read()
    fmt, division, tracks = parser.parse_midi_data(data)
    if fmt == 2:
        tracks = tracks[:1]
    s = song.Song(division, tracks)

    beats = [list(b) for b in s.beats]

    # Every distinct event time, mapped through the tempo map. This is what
    # the player's position arithmetic rides on.
    music_times = [msec for msec, _ in s.music]

    # Ask beat() where the answer can change: at 0, at each beat's own time,
    # one millisecond either side of it, and beyond the last beat.
    queries = {0}
    for msec, _, _, _, _ in s.beats:
        queries.add(msec)
        queries.add(max(0, msec - 1))
        queries.add(msec + 1)
    queries.add(s.length)
    queries.add(s.length + 1000)
    beat_queries = [[t, list(s.beat(t))] for t in sorted(queries)]

    return {
        'name': os.path.basename(path),
        'answered': True,
        'format': fmt,
        'division': division,
        'ntracks': s.ntracks,
        'length': s.length,
        'tempo_times': [list(t) for t in s.tempo_map.times],
        'beats': beats,
        'music_times': music_times,
        'beat_queries': beat_queries,
    }


def probe_isolated(path):
    """Probes one file in its own process, so a hang costs one file, not the run."""
    try:
        finished = subprocess.run(
            [sys.executable, os.path.abspath(__file__), '--one', path],
            capture_output=True, timeout=TIMEOUT_SECONDS,
            env=dict(os.environ, PYTHONDONTWRITEBYTECODE='1'))
    except subprocess.TimeoutExpired:
        return {
            'name': os.path.basename(path),
            'answered': False,
            'reason': f'song.py did not return within {TIMEOUT_SECONDS} seconds',
        }

    if finished.returncode != 0:
        return {
            'name': os.path.basename(path),
            'answered': False,
            'reason': finished.stderr.decode('utf-8', 'replace').strip().splitlines()[-1]
                      if finished.stderr.strip() else f'exit code {finished.returncode}',
        }

    return json.loads(finished.stdout.decode('utf-8'))


def main():
    if len(sys.argv) == 3 and sys.argv[1] == '--one':
        json.dump(probe(sys.argv[2]), sys.stdout)
        return

    out_dir = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUT
    midi_dir = os.path.join(out_dir, 'midi')
    files_dir = os.path.join(midi_dir, 'files')
    os.makedirs(files_dir, exist_ok=True)

    sources = []
    for directory in (CORPUS, SYNTHETIC):
        if not os.path.isdir(directory):
            raise SystemExit(f'no such directory: {directory}')

        sources += [os.path.join(directory, n)
                    for n in sorted(os.listdir(directory)) if n.endswith('.midi')]
    if not sources:
        raise SystemExit('no .midi files found')

    songs = []
    unanswered = []
    for source in sources:
        record = probe_isolated(source)
        songs.append(record)
        shutil.copyfile(source, os.path.join(files_dir, os.path.basename(source)))
        if record['answered']:
            print(f'  {record["name"]}: {record["length"]} ms, '
                  f'{len(record["beats"])} beats, '
                  f'{len(record["music_times"])} event times')
        else:
            unanswered.append(record)
            print(f'  {record["name"]}: NO ANSWER -- {record["reason"]}')

    record = {
        'generated_by': 'tools/midiprobe/gen-midi-fixtures.py',
        'oracle': 'frescobaldi/midifile/song.py (imported and called, '
                  'with the declared known_fixes below)',
        'corpus': 'CodeBrix.LilyPort/tools/regression-harness/reference-midi/midi'
                  ' + tools/midiprobe/synthetic',
        'known_fixes': [
            {'module': f['module'], 'old': f['old'], 'new': f['new'], 'why': f['why']}
            for f in KNOWN_FIXES
        ],
        'songs': songs,
    }

    out_path = os.path.join(midi_dir, 'song.json')
    with open(out_path, 'w', encoding='utf-8') as handle:
        json.dump(record, handle, indent=1)
        handle.write('\n')

    print(f'\n{len(songs)} songs -> {out_path}')
    print(f'{len(sources)} .midi files -> {files_dir}')
    if unanswered:
        print(f'\n{len(unanswered)} file(s) upstream could not answer:')
        for record in unanswered:
            print(f'  {record["name"]}: {record["reason"]}')


if __name__ == '__main__':
    main()
