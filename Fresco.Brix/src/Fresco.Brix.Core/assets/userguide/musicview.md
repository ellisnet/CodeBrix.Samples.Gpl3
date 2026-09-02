=== Music View ===

The Music View displays the PDF document created by LilyPond.

When LilyPond was run in preview mode (i.e. with Point & Click turned on), 
the PDF contains a clickable link for every music object, pointing to its 
definition in the text document.

The Music View uses this information to provide smart, two-way integration 
with the text document:

* Move the mouse pointer over music objects to highlight them in the text
* Click an object to move the text cursor to that object
* Shift-click an object to edit its text in a small window (see 
  {musicview_editinplace})
* Move the text cursor to highlight them in the music view, press
  {key_music_jump_to_cursor} to scroll them into view.

You can also adjust the view:

* Use the Control (or Command) key with the mouse wheel to zoom in and out
* Hold Control or Command and left-click to display a magnifier glass
* Configure the background color under {settings_preview_background}

You can copy music right from the music view to a raster image: use the right
mouse button to drag a rectangular selection, then press
{key_music_copy_image} or select {menu_music_copy_image}. A window opens
showing exactly what would be produced, where you can set the resolution and
the paper color, crop the image to what it actually draws, and then either
copy it to the clipboard or save it to a file.

Whether the clipboard can carry a picture depends on the desktop {appname} is
running on; saving the image to a file always works.

#SEEALSO
musicview_editinplace

#VARS
musicview_editinplace help musicview_editinplace
key_music_copy_image shortcut musicview music_copy_image
key_music_jump_to_cursor shortcut musicview music_jump_to_cursor
menu_music_copy_image menu music -> Copy to &Image...
settings_preview_background menu edit -> Pr&eferences... -> !Fonts & Colors -> Base Colors -> Background
