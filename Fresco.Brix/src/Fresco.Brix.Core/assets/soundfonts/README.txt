SoundFonts shipped with Fresco.Brix
===================================

WHAT IS HERE, AND WHERE EACH FILE CAME FROM

  Every file below was taken from the GeneralUser GS upstream distribution and
  is reproduced VERBATIM. Nothing here is modified; the only change is the
  filename prefix, added so several banks can live in one flat folder.

    GeneralUser-GS.sf2
        the in-box General MIDI bank. 32,319,396 bytes.
        md5    bfe69fe5b2702ef7c12c44bd0d34f8f1
        sha256 9575028c7a1f589f5770fccc8cff2734566af40cd26ed836944e9a5152688cfe
        from   GeneralUser-GS.sf2 (repository root)

    GeneralUser-GS_LICENSE.txt
        the bank's license, in full.  from  documentation/LICENSE.txt

    GeneralUser-GS_README.md
        upstream's own manual for the bank -- preset lists, synth-by-synth
        configuration notes, design rationale.  from  documentation/README.md

    GeneralUser-GS_CHANGELOG.md
        the bank's revision history, which is what establishes that the file
        here is v2.0.3 of 2026-02-22.  from  documentation/CHANGELOG.md

    GeneralUser-GS_percussion-map.pdf
        the drum-kit key map for bank 128.
        from  documentation/percussion map.pdf

  Upstream:  https://github.com/mrbumpy409/GeneralUser-GS  (branch main)
             https://www.schristiancollins.com/generaluser
  Retrieved: 2026-08-20
  Version:   GeneralUser GS v2.0.3, released 2026-02-22
  Author:    S. Christian Collins
  License:   "GeneralUser GS License v2.0" -- permissive, and therefore
             compatible with this application's GPL-3.0-only conveyance.
             Full text in GeneralUser-GS_LICENSE.txt and reproduced in
             ../../../../THIRD-PARTY-NOTICES.txt section 8.

WHAT IS DELIBERATELY *NOT* HERE

  This is NOT a copy of upstream's whole repository, and it must not become
  one. Two parts of that repository are excluded on purpose:

  1. THE "demo MIDIs" FOLDER (nine .mid files and nine .ogg renderings, 31 MB).
     These are MUSICAL COMPOSITIONS, and the GeneralUser GS license does not
     cover them -- it grants rights to the SoundFont bank and its samples, and
     never mentions the demos. Several are plainly third-party copyrighted
     works: "Santa Claus is Comin' to Town" (Coots/Gillespie 1934, still in US
     copyright), "Umi no Mieru Machi" (Joe Hisaishi 1989) and "Bond" (Monty
     Norman 1962). Nothing in Fresco.Brix reads them.

  2. THE "support" FOLDER. It is launcher configuration for FluidSynth -- a
     different application that Fresco.Brix does not use -- including a macOS
     .app bundle with a compiled AppleScript droplet binary that is not
     Collins' to license, plus a Cakewalk instrument-definition file and a
     timing-test recording. None of it is used here.

  The rule this folder follows: keep the licensed work and the documents that
  describe it; keep nothing whose license we cannot point at.

WHY A BANK SHIPS AT ALL

  Frescobaldi ships no SoundFont, because Frescobaldi does not synthesize: it
  sends MIDI to an external port and tells the user to run TiMidity or
  FluidSynth themselves (its userguide/midi_synth.md). Ruling FR6 removed that
  whole mechanism -- playback here is in-process SoundFont synthesis, with no
  MIDI ports -- so upstream's "go install a synth" empty state is not available
  to us as parity behaviour. A bank in the box is therefore a deliberate
  divergence from Frescobaldi, and a required one: without it, MIDI playback
  would have nothing to play with.

WHY THIS BANK

  It is the only General MIDI bank that is simultaneously permissively
  licensed, UNCOMPRESSED SF2, and a sane size. Every small permissive bank in
  circulation (FluidR3Mono, the MuseScore General variants) is SF3, whose
  Vorbis-compressed sample data CodeBrix.Audio does not read; every permissive
  bank that IS uncompressed SF2 runs to 145 MB or more. GeneralUser GS is
  32.3 MB and covers all 128 GM programs plus 13 percussion kits.

  The bank considered first, TimGM6mb, is 5.97 MB but GPL-2-ONLY (Debian's
  DEP-5 record says "License: GPL-2", not "GPL-2+"), which would have made it a
  fourth GPL-2-only row in a GPL-3-only application.

THE FOLDER IS DROPPABLE

  These are third-party data files aggregated with the application, not built
  into it. The folder can be emptied and the application still runs; playback
  then offers no bank until the user points at one of their own. Fresco.Brix
  reads any .sf2 or .sfz the user chooses, so this bank is a default, not a
  dependency. That is also why the license notice sits beside the file rather
  than only in THIRD-PARTY-NOTICES.txt -- the notice has to travel with the
  data.

UPDATING IT

  Upstream deliberately keeps the version OUT of the .sf2 filename so a bank
  can be replaced in place. Drop the new bank in, refresh the four documents
  beside it from the same upstream release, and update the version, size and
  checksums recorded here AND in ../../../../THIRD-PARTY-NOTICES.txt section 8.

  WARNING, and it is not a defect: the file's own INAM metadata string reads
  "GeneralUser GS 2.0.3 BETA" and its ICRD date reads 2024-10-15. Both are
  stale -- upstream last refreshed those strings at v2.0.1, and the changelog
  dates v2.0.3 to 2026-02-22 as a normal release. Do NOT surface INAM as the
  bank's version in any UI, or the application will tell users it is running a
  beta. Read the version from this file and the notices instead. (Board trap
  51.)
