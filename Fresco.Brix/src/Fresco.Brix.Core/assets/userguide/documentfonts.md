=== Document Fonts ===

The `Document Fonts` dialog which is accessible through {menu_dialog} provides
tools to browse the text fonts LilyPort can select, to browse the music fonts
that are installed and to manage their installation. It includes a configurable
font-setting command generator for use in LilyPond documents and provides a
intelligently cached preview to see and compare the impact of font settings on a
variety of provided sample scores, custom files and the "current document".

The dialog is split in two areas, a group of browsing and configuration tabs on
the left side and a preview pane on the right. In the left tabs text and music
fonts can be browsed and selected for use in a font-settings command. By
clicking on the `Copy` or `Use` buttons the generated command can be copied to
the system clipboard or inserted in the current document (overwriting a
selection if present); `Close` leaves the document as it was.

The selected fonts are remembered between {appname} sessions, but by clicking on
the `Restore Defaults` button at the bottom of the dialog's content the
selection can be reset to the dialog's own default text and music fonts.

= Text Fonts =

The tab {page_textfonts} provides a searchable tree view of the font families
LilyPort can select, each with the faces it reaches. It also allows to select
text fonts for use in the score document.

= Music Fonts =

The tab {page_musicfonts} provides a list of the music fonts that are installed
and a number of controls to manage their installation in the folder LilyPort
searches.

= Font Command =

The tab {page_fontcommand} provides controls to configure the way how the
font-setting command is generated.

= Miscellaneous =

The last tab says where LilyPort looks for fonts: the directories it searches
first (among them the folder music fonts are installed in), the fonts built into
the program, and the fonts the document itself supplied.

= Preview =

On the right side of the dialog a large area is reserved for a (cached) preview
of various scores using all combinations, described in {page_preview}.

#SUBDOCS
documentfonts_text
documentfonts_music
documentfonts_command
documentfonts_preview

#VARS
menu_dialog menu Tools -> Document Fonts...

page_textfonts help documentfonts_text
page_musicfonts help documentfonts_music
page_fontcommand help documentfonts_command
page_preview help documentfonts_preview
