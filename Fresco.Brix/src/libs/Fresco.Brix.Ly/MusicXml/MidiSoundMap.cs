// === python-ly ly.musicxml.midi_sound_map module ===
//
// Copyright (c) 2008 - 2015 by Wilbert Berendsen
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation; either version 3
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
// See http://www.gnu.org/licenses/ for more information.

using System;
using System.Collections.Generic;

namespace Fresco.Brix.Ly.MusicXml; //was previously: ly/musicxml/midi_sound_map.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// What each of LilyPond's MIDI instrument names is called in MusicXML's own
/// Standard Sounds vocabulary.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>midi_sound_map</c>, all 128 General MIDI names, carried across
/// verbatim — including the entries that are deliberately UNANSWERED (null,
/// upstream's <c>None</c>) and the question marks upstream left beside the ones
/// it was unsure of. Neither is tidied: an unanswered name means no
/// <c>&lt;instrument-sound&gt;</c> element is written at all, and a question
/// mark is a note to whoever revisits the mapping.
/// </para>
/// <para>
/// ⚠ Two of upstream's values are misspelled — <c>pitched-percusstion</c> in
/// four entries and <c>strings.contrabass</c> where the rest of the table says
/// <c>string.</c> — and they are kept as they are. Ruling FR14 is about code
/// that does not do what it says; a data table's contents are upstream's
/// STATEMENT of the mapping, a reader may already depend on these strings, and
/// correcting them here would silently make the port disagree with every
/// MusicXML file python-ly has ever written. They are raised in the wave's
/// STATUS file instead.
/// </para>
/// </remarks>
public static class MidiSoundMap
{
    /// <summary>The mapping, keyed by LilyPond's own instrument name.</summary>
    public static IReadOnlyDictionary<string, string> Sounds { get; }
        = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["accordion"] = "keyboard.accordion",
        ["acoustic bass"] = "pluck.bass.acoustic",
        ["acoustic grand"] = "keyboard.piano.grand",
        ["acoustic guitar (nylon)"] = "pluck.guitar.nylon-string",
        ["acoustic guitar (steel)"] = "pluck.guitar.steel-string",
        ["agogo"] = "wood.agogo-block",
        ["alto sax"] = "wind.reed.saxophone.alto",
        ["applause"] = "effect.applause",
        ["bagpipe"] = "wind.pipes.bagpipes",
        ["banjo"] = "pluck.banjo",
        ["baritone sax"] = "wind.reed.saxophone.baritone",
        ["bassoon"] = "brass.trombone",
        ["bird tweet"] = "effect.bird.tweet",
        ["blown bottle"] = "wind.flutes.blown-bottle",
        ["brass section"] = "brass.helicon",
        ["breath noise"] = "effect.breath",
        ["bright acoustic"] = "keyboard.piano",
        ["celesta"] = "keyboard.celesta",
        ["cello"] = "string.cello",
        ["choir aahs"] = "voice.aa",   //? voice.vocals?
        ["church organ"] = "keyboard.organ",
        ["clarinet"] = "wind.reed.clarinet",
        ["clav"] = "keyboard.clavichord",   //?
        ["concertina"] = "keyboard.concertina",
        ["contrabass"] = "strings.contrabass",
        ["distorted guitar"] = null,
        ["drawbar organ"] = "keyboard.organ.drawbar",
        ["dulcimer"] = "pluck.dulcimer",
        ["electric bass (finger)"] = null,
        ["electric bass (pick)"] = null,
        ["electric grand"] = "keyboard.electric",   //?
        ["electric guitar (clean)"] = null,
        ["electric guitar (jazz)"] = null,
        ["electric guitar (muted)"] = null,
        ["electric piano 1"] = "keyboard.electric",   //?
        ["electric piano 2"] = "keyboard.electric",   //?
        ["english horn"] = "wind.reed.english-horn",
        ["fiddle"] = "strings.fiddle",
        ["flute"] = "wind.flutes.flute",
        ["french horn"] = "brass.french-horn",
        ["fretless bass"] = "pluck.bass.fretless",
        ["fx 1 (rain)"] = "synth.effects.rain",
        ["fx 2 (soundtrack)"] = "synth.effects.soundtrack",
        ["fx 3 (crystal)"] = "synth.effects.crystal",
        ["fx 4 (atmosphere)"] = "synth.effects.atmosphere",
        ["fx 5 (brightness)"] = "synth.effects.brightness",
        ["fx 6 (goblins)"] = "synth.effects.goblins",
        ["fx 7 (echoes)"] = "synth.effects.echoes",
        ["fx 8 (sci-fi)"] = "synth.effects.sci-fi",
        ["glockenspiel"] = "pitched-percusstion.glockenspiel",
        ["guitar fret noise"] = "effect.guitar-fret",
        ["guitar harmonics"] = null,
        ["gunshot"] = "effect.gunshot",
        ["harmonica"] = "wind.reed.harmonica",
        ["harpsichord"] = "keyboard.harpsichord",
        ["helicopter"] = "effect.helicopter",
        ["honky-tonk"] = "keyboard.piano.honky-tonk",
        ["kalimba"] = "pitched-percussion.kalimba",
        ["koto"] = "pluck.koto",
        ["lead 1 (square)"] = "synth.tone.square",
        ["lead 2 (sawtooth)"] = "synth.tone.sawtooth",
        ["lead 3 (calliope)"] = "wind.flutes.calliope",
        ["lead 4 (chiff)"] = "pluck.synth.chiff",
        ["lead 5 (charang)"] = "pluck.synth.charang",
        ["lead 6 (voice)"] = "voice.synth",
        ["lead 7 (fifths)"] = "synth.group.fifths",
        ["lead 8 (bass+lead)"] = "pluck.bass.lead",
        ["marimba"] = "pitched-percusstion.marimba",
        ["melodic tom"] = "drum.tom-tom",   //?
        ["music box"] = "pitched-percussion.music-box",
        ["muted trumpet"] = "barss.trumpet",   //problematic
        ["oboe"] = "wind.reed.oboe",
        ["ocarina"] = "wind.flutes.ocarina",
        ["orchestra hit"] = "synth.group.orchestra",   //problematic?
        ["orchestral harp"] = "pluck.harp",   //?
        ["overdriven guitar"] = null,
        ["pad 1 (new age)"] = "synth.pad.polysynth",
        ["pad 2 (warm)"] = "synth.pad.warm",
        ["pad 3 (polysynth)"] = "synth.pad.polysynth",
        ["pad 4 (choir)"] = "synth.pad.choir",
        ["pad 5 (bowed)"] = "synth.pad.bowed",
        ["pad 6 (metallic)"] = "synth.pad.metallic",
        ["pad 7 (halo)"] = "synth.pad.halo",
        ["pad 8 (sweep)"] = "synth.pad.sweep",
        ["pan flute"] = "wind.flutes.panpipes",
        ["percussive organ"] = "voice.percussion",
        ["piccolo"] = "wind.flutes.flute.piccolo",
        ["pizzicato strings"] = "pluck.bass",   //problematic
        ["recorder"] = "wind.flutes.recorder",
        ["reed organ"] = "keyboard.organ.reed",
        ["reverse cymbal"] = "metal.cymbal.reverse",
        ["rock organ"] = null,
        ["seashore"] = "effect.seashore",
        ["shakuhachi"] = "wind.flutes.shakuhachi",
        ["shamisen"] = "pluck.shamisen",
        ["shanai"] = "wind.reed.shenai",
        ["sitar"] = "pluck.sitar",
        ["slap bass 1"] = "effect.bass-string-slap",
        ["slap bass 2"] = "effect.bass-string-slap",   //?
        ["soprano sax"] = "wind.reed.saxophone.soprano",
        ["steel drums"] = "metal.steel-drums",
        ["string ensemble 1"] = "strings.group",   //?
        ["string ensemble 2"] = "strings.group",   //?
        ["synth bass 1"] = null,
        ["synth bass 2"] = null,
        ["synth drum"] = "drum.tom-tom.synth",   //?
        ["synth voice"] = "voice.synth",
        ["synthbrass 1"] = "synth.brass.group",   //?
        ["synthbrass 2"] = "synth.brass.group",   //?
        ["synthstrings 1"] = "strings.group.synth",
        ["synthstrings 2"] = "strings.group.synth",
        ["taiko drum"] = "deum.taiko",
        ["telephone ring"] = "effect.telephone-ring",
        ["tenor sax"] = "wind.reed.saxophone.tenor",
        ["timpani"] = "drum.timpani",
        ["tinkle bell"] = "metal.bells.tinklebell",
        ["tremolo strings"] = null,   //?
        ["trombone"] = "brass.trombone",
        ["trumpet"] = "brass.trumpet",
        ["tuba"] = "brass.tuba",
        ["tubular bells"] = "pitched-percussion.tubular-bells",
        ["vibraphone"] = "pitched-percussion.vibraphone",
        ["viola"] = "strings.viola",
        ["violin"] = "strings.violin",
        ["voice oohs"] = "voice.oo",
        ["whistle"] = "effect.whistle",
        ["woodblock"] = "wood.wood-block",
        ["xylophone"] = "pitched-percussion.xylophone",    };
}
