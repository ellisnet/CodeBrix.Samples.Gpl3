#!/usr/bin/env python3
"""compare-midi.py -- grade CodeBrix.LilyPort's MIDI output against the oracle's.

This file is part of CodeBrix.LilyPort.
Copyright (c) 2026 Jeremy Ellis and contributors

CodeBrix.LilyPort is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

------------------------------------------------------------------------------
WHAT THIS GRADES, AND THE NORMALISATION RULES

A Standard MIDI File is a byte format, so a byte comparison is tempting and
wrong: two files can carry the identical performance and differ in bytes. The
comparator therefore parses both files into EVENT STREAMS and compares those.
Exactly four normalisations are applied, and no others -- every one of them is a
place where the format admits more than one spelling of the same music:

  1. DELTA TIMES BECOME ABSOLUTE TICKS.  A delta time is a distance from the
     previous event, so the same event at the same moment is spelled differently
     depending on how the preceding events were split.  Absolute ticks are what
     the music actually says.

  2. RUNNING STATUS IS EXPANDED.  MIDI lets a channel event omit its status byte
     when it repeats the previous one.  Whether a writer uses that is a choice
     about compactness, not about the performance.

  3. THE VERSION STAMP IS ELIDED.  Every control track carries a text event
     reading "LilyPond <version>" padded to 30 characters.  Comparing it would
     make the whole suite fail whenever either side's version string moved, which
     measures the build and not the port.  The event's PRESENCE and position are
     still compared -- only its payload is replaced by a marker.

  4. END-OF-TRACK IS DROPPED.  It carries no musical information and its tick is
     already implied by the events before it.

Everything else is compared exactly, INCLUDING the order of events that share a
tick.  That order is not incidental: lily/midi-chunk.cc's Midi_track::add exists
solely to force instrument changes ahead of the notes they apply to, so a
comparator that sorted within a tick would grade away the thing that code is for.

VERDICTS, deliberately the same vocabulary compare-output.py uses:

  MATCH           every track's event stream is identical
  EVENTS-DIFFER   both files parse, the streams differ
  MISSING         the port produced no file at all
  UNPARSEABLE     the port produced a file that is not a Standard MIDI File

SELF-CHECK.  `compare-midi.py <ref> <ref>` must report every row MATCH.  That is
what would catch this script reading zero events out of every file -- the failure
the SVG comparator actually had for four sessions before EPG13 found it.
------------------------------------------------------------------------------
"""

import argparse
import os
import sys

VERSION_STAMP_MARKER = "<lilypond-version-stamp>"


class MidiParseError(Exception):
    """Raised when a file is not a well-formed Standard MIDI File."""


def read_varint(data, pos):
    """Read a MIDI variable-length quantity. Returns (value, new_pos)."""
    value = 0
    for _ in range(4):
        if pos >= len(data):
            raise MidiParseError("truncated variable-length quantity")
        byte = data[pos]
        pos += 1
        value = (value << 7) | (byte & 0x7F)
        if not byte & 0x80:
            return value, pos
    raise MidiParseError("over-long variable-length quantity")


def parse_track(data, events):
    """Parse one track chunk body into (abs_tick, event-tuple) entries."""
    pos = 0
    tick = 0
    status = None

    while pos < len(data):
        delta, pos = read_varint(data, pos)
        tick += delta  # NORMALISATION 1: deltas become absolute ticks.

        if pos >= len(data):
            raise MidiParseError("truncated event")

        byte = data[pos]

        if byte == 0xFF:  # meta event
            pos += 1
            if pos >= len(data):
                raise MidiParseError("truncated meta event")
            meta_type = data[pos]
            pos += 1
            length, pos = read_varint(data, pos)
            payload = data[pos:pos + length]
            if len(payload) != length:
                raise MidiParseError("truncated meta payload")
            pos += length

            if meta_type == 0x2F:  # NORMALISATION 4: end of track carries nothing.
                continue

            if meta_type == 0x01 and payload.startswith(b"LilyPond "):
                # NORMALISATION 3: the version stamp.
                events.append((tick, "meta", meta_type, VERSION_STAMP_MARKER))
                continue

            events.append((tick, "meta", meta_type, payload.hex()))
            continue

        if byte in (0xF0, 0xF7):  # sysex
            pos += 1
            length, pos = read_varint(data, pos)
            payload = data[pos:pos + length]
            if len(payload) != length:
                raise MidiParseError("truncated sysex payload")
            pos += length
            events.append((tick, "sysex", byte, payload.hex()))
            continue

        if byte & 0x80:
            status = byte
            pos += 1
        elif status is None:
            raise MidiParseError("running status with no preceding status byte")
        # NORMALISATION 2: else the status byte is omitted and `status` stands.

        high = status & 0xF0
        data_bytes = 1 if high in (0xC0, 0xD0) else 2
        if pos + data_bytes > len(data):
            raise MidiParseError("truncated channel event")

        payload = data[pos:pos + data_bytes]
        pos += data_bytes
        events.append((tick, "chan", status, payload.hex()))

    return events


def parse_midi(path):
    """Parse a Standard MIDI File into (format, division, [track event lists])."""
    with open(path, "rb") as handle:
        data = handle.read()

    if len(data) < 14 or data[0:4] != b"MThd":
        raise MidiParseError("no MThd header")

    header_length = int.from_bytes(data[4:8], "big")
    if header_length < 6:
        raise MidiParseError("short MThd chunk")

    file_format = int.from_bytes(data[8:10], "big")
    track_count = int.from_bytes(data[10:12], "big")
    division = int.from_bytes(data[12:14], "big")

    pos = 8 + header_length
    tracks = []
    while pos + 8 <= len(data):
        tag = data[pos:pos + 4]
        length = int.from_bytes(data[pos + 4:pos + 8], "big")
        pos += 8
        body = data[pos:pos + length]
        if len(body) != length:
            raise MidiParseError("truncated track chunk")
        pos += length
        if tag == b"MTrk":
            tracks.append(parse_track(body, []))

    if len(tracks) != track_count:
        raise MidiParseError(
            "header declares %d track(s), file holds %d" % (track_count, len(tracks)))

    return file_format, division, tracks


def describe(event):
    """Render one event for a human reading a difference report."""
    tick, kind, code, payload = event
    if kind == "meta":
        return "t=%d meta %02x %s" % (tick, code, payload)
    if kind == "sysex":
        return "t=%d sysex %02x %s" % (tick, code, payload)
    return "t=%d %02x %s" % (tick, code, payload)


def compare_one(reference_path, candidate_path):
    """Return (verdict, detail) for one reference file."""
    reference = parse_midi(reference_path)

    if candidate_path is None or not os.path.exists(candidate_path):
        return "MISSING", "no output produced"

    try:
        candidate = parse_midi(candidate_path)
    except MidiParseError as error:
        return "UNPARSEABLE", str(error)

    ref_format, ref_division, ref_tracks = reference
    can_format, can_division, can_tracks = candidate

    if ref_format != can_format:
        return "EVENTS-DIFFER", "format %d, expected %d" % (can_format, ref_format)

    if ref_division != can_division:
        return "EVENTS-DIFFER", (
            "division %d, expected %d" % (can_division, ref_division))

    if len(ref_tracks) != len(can_tracks):
        return "EVENTS-DIFFER", (
            "%d track(s), expected %d" % (len(can_tracks), len(ref_tracks)))

    for index, (ref_events, can_events) in enumerate(zip(ref_tracks, can_tracks)):
        if ref_events == can_events:
            continue

        for position, (ref_event, can_event) in enumerate(zip(ref_events, can_events)):
            if ref_event != can_event:
                return "EVENTS-DIFFER", (
                    "track %d event %d: %s, expected %s"
                    % (index, position, describe(can_event), describe(ref_event)))

        shorter, longer = (
            ("candidate", ref_events) if len(can_events) < len(ref_events)
            else ("reference", can_events))
        return "EVENTS-DIFFER", (
            "track %d: %s ends after %d event(s); next is %s"
            % (index,
               shorter,
               min(len(ref_events), len(can_events)),
               describe(longer[min(len(ref_events), len(can_events))])))

    return "MATCH", ""


def main():
    parser = argparse.ArgumentParser(
        description="Grade MIDI output against the oracle's, event by event.")
    parser.add_argument("reference", help="directory of reference .midi files")
    parser.add_argument("candidate", help="directory of candidate .midi files")
    parser.add_argument("--tsv", help="write per-file verdicts here")
    parser.add_argument(
        "--show", type=int, default=15,
        help="how many differing files to describe (default 15)")
    arguments = parser.parse_args()

    names = sorted(
        name for name in os.listdir(arguments.reference) if name.endswith(".midi"))

    if not names:
        print("no reference .midi files in %s" % arguments.reference, file=sys.stderr)
        return 2

    verdicts = []
    for name in names:
        reference_path = os.path.join(arguments.reference, name)
        candidate_path = os.path.join(arguments.candidate, name)
        try:
            verdict, detail = compare_one(reference_path, candidate_path)
        except MidiParseError as error:
            print("reference %s is unreadable: %s" % (name, error), file=sys.stderr)
            return 2
        verdicts.append((name, verdict, detail))

    counts = {}
    for _, verdict, _ in verdicts:
        counts[verdict] = counts.get(verdict, 0) + 1

    print("reference : %s (%d files)" % (arguments.reference, len(names)))
    print("candidate : %s" % arguments.candidate)
    print("%-16s %6s %6s" % ("VERDICT", "COUNT", "SHARE"))
    for verdict in sorted(counts):
        share = 100.0 * counts[verdict] / len(names)
        print("%-16s %6d %5.1f%%" % (verdict, counts[verdict], share))
    print("%-16s %6d %5.1f%%" % ("TOTAL", len(names), 100.0))

    matched = counts.get("MATCH", 0)
    print("*** %d of %d match (%.2f%%) ***"
          % (matched, len(names), 100.0 * matched / len(names)))

    shown = 0
    for name, verdict, detail in verdicts:
        if verdict == "MATCH" or shown >= arguments.show:
            continue
        if shown == 0:
            print("\nDIFFERENCES -- first %d:" % arguments.show)
        print("  %-46s %-14s %s" % (name, verdict, detail))
        shown += 1

    if arguments.tsv:
        with open(arguments.tsv, "w", encoding="utf-8") as handle:
            handle.write("name\tverdict\tdetail\n")
            for name, verdict, detail in verdicts:
                handle.write("%s\t%s\t%s\n" % (name, verdict, detail))

    return 0


if __name__ == "__main__":
    sys.exit(main())
