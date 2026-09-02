=== Paths ===

== Hyphenation dictionaries ==

Here, directories can be added that contain `hyph_*.dic` files,
where the `*` stands for different language codes.

These hyphenation dictionaries are used by {appname} to break
lyrics text into syllables. {appname} comes with a set of them and also looks
in the usual places on your system.

== Music fonts ==

LilyPort supports the selection of alternative music fonts, and {appname}
supports both the management of their installation and generating the command
to choose a set of fonts in a document. Fonts are installed into {appname}'s
own font folder, which LilyPort searches before its own built-in fonts; the
page names the folder, and nothing is put there until you ask for it in the
{fonts_dialog} dialog.

* *Music Font Repository:* Typically one will want to store music fonts
  in a dedicated directory. If this directory is selected here, the
  Document Fonts dialog can install fonts from it with one button.
* *Music Font Preview Cache:* The Document Fonts dialog engraves and displays
  preview samples using the selected music and text fonts. In order to
  provide a seamless user experience these samples are cached, so if
  a combination of content and fonts has already been engraved the
  resulting scores can simply be swapped. If no directory is set here the
  samples are cached in a temporary directory, which the system cleans up;
  a writable directory set here caches them persistently.

#VARS
fonts_dialog menu tools -> &Document Fonts...

#SEEALSO
documentfonts
