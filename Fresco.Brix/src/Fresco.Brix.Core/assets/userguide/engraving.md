=== Engraving Scores ===

To engrave a score, {appname} runs LilyPort — its built-in engraver — with the
correct options. There is no LilyPond to install and no version to choose: the
engraver is part of the program. There are four modes {appname} can engrave
scores in: *Preview*, *Publish*, *Layout Control* and *Custom*.

The *Preview mode* renders the PDF with point and click information enabled, so
that there is two-way navigation between the music view and the LilyPond source.

The *Publish mode* is used to produce the final PDF intended to be shared.
Not generating the point and click information reduces the size of the resulting PDF file
and doesn't reveal hard-coded information about the directories on your computer.

The *Layout Control mode* can be used to enable specific features that can help
in controlling and fine-tuning the layout of a score.

The *Custom mode* opens a dialog allowing you to specify the engraving options
in detail.

All engraving commands can be found in the *LilyPort* menu.
Pressing the engrave button in the toolbar engraves the document
in preview mode, or in custom mode if Shift is held. And if you enable the
*Automatic engrave* option, {appname} will engrave the document automatically
every time it is modified (in preview mode).

#SUBDOCS
engrave_preview
engrave_publish
engrave_layout
engrave_custom

#SUBDOCS_TODO
engrave_automatic
engrave_partial

