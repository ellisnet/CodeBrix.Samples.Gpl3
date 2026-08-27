#!/usr/bin/env python3
"""Fresco.Brix repo tool (ships nothing): writes the small set of MIDI files the
LilyPort regression corpus does not contain, so the midifile.song parity oracle
covers the code paths a real engraved file never reaches.

The corpus is 90 files a real LilyPond wrote, and every one of them is format 1
with a 384-tick division, a tempo event at time 0 and a time signature at time
0. Upstream's song.py has arms for all the other cases -- SMPTE divisions,
format 0 and 2, a missing tempo map, a missing time signature, a first event of
either kind arriving LATE, a tempo event on more than one track at the same
time -- and the port carries them, so the oracle has to be asked about them.

These are the counterpart of the six made-up hyphenation dictionaries in
tools/hyphenprobe: nothing here has to be musically sensible, because what is
being checked is that two implementations answer the same thing.

Usage: PYTHONDONTWRITEBYTECODE=1 python3 make-synthetic-midi.py [out-dir]
"""
import os
import struct
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
DEFAULT_OUT = os.path.join(HERE, 'synthetic')


def var_len(value):
    """Encodes a MIDI variable-length quantity."""
    out = bytearray()
    out.append(value & 0x7F)
    value >>= 7
    while value:
        out.append((value & 0x7F) | 0x80)
        value >>= 7
    return bytes(reversed(out))


def meta(delta, kind, data):
    """A meta event: delta time, 0xFF, type, length, data."""
    return var_len(delta) + bytes([0xFF, kind]) + var_len(len(data)) + data


def tempo(delta, usec_per_quarter):
    """A Set Tempo meta event (0x51)."""
    return meta(delta, 0x51, usec_per_quarter.to_bytes(3, 'big'))


def time_sig(delta, num, den_power, clocks=24, n32s=8):
    """A Time Signature meta event (0x58); den_power is dd in 2**dd."""
    return meta(delta, 0x58, bytes([num, den_power, clocks, n32s]))


def end_of_track(delta=0):
    """The End of Track meta event (0x2F)."""
    return meta(delta, 0x2F, b'')


def note(delta, pitch, on=True, channel=0, velocity=90):
    """A Note On or Note Off channel event."""
    status = (0x90 if on else 0x80) | channel
    return var_len(delta) + bytes([status, pitch, velocity])


def track(*events):
    """Wraps event bytes in an MTrk chunk, adding End of Track."""
    data = b''.join(events) + end_of_track()
    return b'MTrk' + struct.pack('>i', len(data)) + data


def midi_file(fmt, division, tracks):
    """Assembles a whole MIDI file."""
    header = struct.pack('>hhh', fmt, len(tracks), division)
    return b'MThd' + struct.pack('>i', 6) + header + b''.join(tracks)


def notes(*deltas_and_pitches):
    """A run of quarter-length notes at 384 ticks each."""
    out = []
    for pitch in deltas_and_pitches:
        out.append(note(0, pitch, on=True))
        out.append(note(384, pitch, on=False))
    return b''.join(out)


FILES = {}

# Format 0: one track carrying everything. song.load keeps it as it is.
FILES['syn-format0.midi'] = midi_file(0, 384, [
    track(tempo(0, 500000), time_sig(0, 3, 2), notes(60, 62, 64, 65)),
])

# Format 2: independent sequences. song.load keeps ONLY THE FIRST track, so the
# second track's much faster tempo must not reach the answer.
FILES['syn-format2.midi'] = midi_file(2, 384, [
    track(tempo(0, 600000), time_sig(0, 2, 2), notes(60, 62)),
    track(tempo(0, 100000), time_sig(0, 7, 3), notes(72, 74, 76)),
    track(tempo(0, 250000), notes(48)),
])

# An SMPTE division: the top bit is set, the high byte is -frames as a signed
# char and the low byte is ticks per frame. 0xE828 is 24 frames x 40 ticks,
# which smpte_division() turns into 960.
FILES['syn-smpte.midi'] = midi_file(1, struct.unpack('>h', b'\xe8\x28')[0], [
    track(tempo(0, 500000), time_sig(0, 4, 2), end_of_track()),
    track(note(0, 60, on=True), note(960, 60, on=False),
          note(0, 62, on=True), note(1920, 62, on=False)),
])

# No tempo event anywhere: TempoMap inserts (0, 500000) -- 120 crotchets a
# minute, the MIDI default.
FILES['syn-no-tempo.midi'] = midi_file(1, 384, [
    track(time_sig(0, 4, 2), notes(60, 62, 64, 65)),
])

# No time signature anywhere: beats() inserts (0, (4, 4, 24, 8)).
FILES['syn-no-timesig.midi'] = midi_file(1, 384, [
    track(tempo(0, 500000), notes(60, 62, 64, 65, 67, 69)),
])

# Neither one. Both defaults fire together.
FILES['syn-bare.midi'] = midi_file(1, 384, [
    track(notes(60, 62, 64)),
])

# The first tempo event arrives LATE, so TempoMap prepends its default and the
# piecewise arithmetic really has two segments.
FILES['syn-late-tempo.midi'] = midi_file(1, 384, [
    track(time_sig(0, 4, 2), tempo(768, 250000), tempo(768, 1000000),
          end_of_track()),
    track(notes(60, 62, 64, 65, 67, 69, 71, 72)),
])

# The first time signature arrives late, which is the branch where beats()
# yields default-signature beats and then JUMPS back to the signature's time.
FILES['syn-late-timesig.midi'] = midi_file(1, 384, [
    track(tempo(0, 500000), time_sig(1152, 3, 2), end_of_track()),
    track(notes(60, 62, 64, 65, 67, 69, 71, 72)),
])

# Several time signatures, one of them landing off the beat grid the previous
# one was laying down.
FILES['syn-timesig-changes.midi'] = midi_file(1, 384, [
    track(tempo(0, 500000), time_sig(0, 4, 2), time_sig(1536, 3, 2),
          time_sig(1152, 6, 3), time_sig(1152, 2, 1), end_of_track()),
    track(notes(*([60, 62, 64, 65] * 4))),
])

# Tempo events on TWO tracks at the SAME midi time. Upstream walks the tracks in
# number order and takes the first tempo it finds at each time, so track 0's
# 400000 must win over track 1's 800000.
FILES['syn-multi-tempo-tracks.midi'] = midi_file(1, 384, [
    track(tempo(0, 400000), time_sig(0, 4, 2), tempo(1536, 300000),
          end_of_track()),
    track(tempo(0, 800000), tempo(1536, 900000), notes(60, 62, 64, 65, 67)),
])

# A denominator of 2**8, where the step between beats comes out at 6 ticks, and
# one of 2**0, where a "beat" is four whole notes long.
FILES['syn-den-extremes.midi'] = midi_file(1, 384, [
    track(tempo(0, 500000), time_sig(0, 5, 8), time_sig(1536, 2, 0),
          end_of_track()),
    track(notes(60, 62, 64, 65, 67, 69, 71, 72)),
])

# A track with nothing in it but its end marker, beside a track with music.
FILES['syn-empty-track.midi'] = midi_file(1, 384, [
    track(end_of_track()),
    track(tempo(0, 500000), time_sig(0, 4, 2), notes(60, 62)),
])

# One event, at time zero, and nothing else: the shortest song there can be.
FILES['syn-single-event.midi'] = midi_file(1, 384, [
    track(tempo(0, 500000), time_sig(0, 4, 2)),
])

# --- The four headers the MIDI format permits and song.py cannot survive. ---
# Each is legal in the sense that a file can carry it and a reader must cope;
# each makes upstream hang or raise, which is why the probe runs every file in
# its own process under a timeout. The port answers all four (ruling FR14) and
# the parity test declares that it does.

# A time-signature denominator of 2**255, which makes upstream's beat step
# (4 * division) // (2 ** den) come out ZERO -- so `time += step` never moves
# and beats() spins forever.
FILES['syn-den-huge.midi'] = midi_file(1, 384, [
    track(tempo(0, 500000), time_sig(0, 4, 255), notes(60, 62)),
])

# A time-signature numerator of zero, which makes `beat % num` a division by
# zero on the second beat.
FILES['syn-num-zero.midi'] = midi_file(1, 384, [
    track(tempo(0, 500000), time_sig(0, 0, 2), notes(60, 62, 64)),
])

# A header declaring no tracks at all. Song.__init__ ends with
# max(self.events) over an empty dict.
FILES['syn-no-tracks.midi'] = b'MThd' + struct.pack('>i', 6) + \
    struct.pack('>hhh', 1, 0, 384)


def main():
    out_dir = sys.argv[1] if len(sys.argv) > 1 else DEFAULT_OUT
    os.makedirs(out_dir, exist_ok=True)
    for name, data in sorted(FILES.items()):
        path = os.path.join(out_dir, name)
        with open(path, 'wb') as handle:
            handle.write(data)
        print(f'  {name}: {len(data)} bytes')
    print(f'\n{len(FILES)} files -> {out_dir}')


if __name__ == '__main__':
    main()
