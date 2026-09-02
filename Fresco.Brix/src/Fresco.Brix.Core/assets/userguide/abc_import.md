=== Import ABC ===

Using {menu_import}, you can import an ABC file. The conversion is done by
`abc2ly`, which is built into {appname} and needs nothing installed.

ABC is a notation standard which like LilyPond is designed to notate music in
plain text. It was designed primarily for folk and traditional tunes of Western
European origin.

In this dialog there are two tabs. In the first you can set some parameters
for the abc2ly import. In the second you can set some actions on the
imported LilyPond source code.

Your settings in both tabs are remembered until the next time you use this dialog.

== The abc2ly tab ==

In this tab you have the following options:

 * Import beaming

This option can be used to retain the beaming from the ABC notation.

== The *after import* tab ==

After `abc2ly` is run and the new ly-file is created you can set
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

Support for the ABC standard is deliberately as incomplete as the original
tool's: several tunes in one file, block comments, PostScript commands and a
number of header fields are not handled, and ABC line breaks are ignored.

Whatever the converter had to say while it worked is shown in {menu_log}.

#VARS
menu_import menu file -> submenu title|&Import/Export -> Import abc...
menu_format menu tools -> submenu title|Code &Formatting -> &Format
menu_implicit menu tools -> submenu title|Musical &Transformations -> submenu title|Rhythm -> Make implicit (per &line)
menu_preview menu lilypond -> &Engrave (preview)
menu_log menu tools -> submenu title|&Viewers -> LilyPort &Log
