=== Import Music XML ===

Using {menu_import}, you can import a Music XML file. The conversion is done by
`musicxml2ly`, which is built into {appname} and needs nothing installed.

In this dialog there are two tabs. In the first you can set some parameters 
for the musicxml2ly import. In the second you can set some actions on the 
imported LilyPond source code.

Your settings in both tabs are remembered until the next time you use this dialog.

== The musicxml2ly tab ==

In this tab you have the following options:

 * Import articulation directions
 * Import rest positions
 * Import page layout
 * Import beaming
 * Pitches in absolute mode
 * Language for pitch names

The first four options are score elements you can retrieve from the 
musicXML-file if they are present and if you prefer not to use LilyPond's 
automatic handling of these elements.

The next two options can be used if you prefer to have the source code in 
absolute mode or if you prefer a different pitch name language than the 
default.

== The *after import* tab ==

After `musicxml2ly` is run and the new ly-file is created you can set 
{appname} to automatically do some adjustments on the new file.

Reformat source:
:  the code is reformatted.
   (This is identical to running {menu_format}.)

Trim durations (Make implicit per line):
:  repeated time duration on the same line are deleted.
   (This is identical to running {menu_implicit}.)

Remove fraction duration scaling:
:  if single notes, rests or chords are multiplied by a fraction `N/M` by
   appending `*N/M` this scaling is removed.
   (This can be useful to prevent unwanted scaling to emerge from ill-formatted
   musicXML-files.)

Engrave directly:
:  the engraver runs on the imported source at once.
   (This is identical to running {menu_preview}.)

The imported source is written beside the file it came from, under the same
name with a `.ly` extension. A name that is already taken — on disk or in an
open tab — is stepped past, so an import never overwrites anything.

Whatever the converter had to say while it worked is shown in {menu_log}.

#VARS
menu_import menu file -> submenu title|&Import/Export -> Import MusicXML...
menu_format menu tools -> submenu title|Code &Formatting -> &Format
menu_implicit menu tools -> submenu title|Musical &Transformations -> submenu title|Rhythm -> Make implicit (per &line)
menu_preview menu lilypond -> &Engrave (preview)
menu_log menu tools -> submenu title|&Viewers -> LilyPort &Log
