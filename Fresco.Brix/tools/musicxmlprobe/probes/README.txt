Probe documents for the MusicXML export oracle (Fresco.Brix board wave W11).

These are Fresco.Brix's OWN .ly files, not copies of anything. They exist
because the two corpora the oracle is otherwise built from -- python-ly's 17
test documents and the 54 ly.music fixtures -- between them never exercise
several things ly.musicxml can write, and an oracle that never sees a feature
cannot say whether the port got it right.

One file per feature area, each as small as it can be while still reaching the
code:

  tremolo.ly        \repeat tremolo, both the repeats > duration arm of
                    calc_trem_dur and the other one. NOTHING in either corpus
                    used a counted tremolo, which is how the KNOWN_FIX for
                    calc_trem_dur's missing function went unnoticed.
  ottava.ly         \ottava, its octave-shift start and stop, up and down.
  groups.ly         nested \new StaffGroup / \new GrandStaff / PianoStaff, so
                    the part-group start/stop numbering is exercised.
  lyrics.ly         \addlyrics with hyphens, a melisma extender, \skip, and
                    two stanzas.
  graces.ly         \grace, \acciaccatura and \appoggiatura.
  dynamics-extra.ly hairpins, text dynamics (cresc/dim), the ! terminator.
                    Named -extra because python-ly ships a dynamics.ly of its
                    own and the two would land on the same fixture.
  marks.ly          \mark, \mark #N, \tempo with and without a metronome mark.
  clefs.ly          the transposing clefs and the ones with no proper symbol.
  chords.ly         chords, chord repeats (q), and a glissando across one.
  devnull.ly        \\new Devnull, the one context that makes a GLOBAL section
                    and therefore the only way to reach check_voices
                    second half. Nothing in either corpus used one.

Regenerate the fixtures with tools/musicxmlprobe/gen-musicxml-fixtures.py; the
tool ships nothing and is not in the solution.
