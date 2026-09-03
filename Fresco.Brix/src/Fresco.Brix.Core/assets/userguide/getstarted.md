=== Getting Started ===

The default screen of {appname} shows a text document on the left and an
empty Music View window on the right.

Now, in the text view, enter some LilyPond code, like this:

```lilypond
\relative c'' {
  \time 7/4
  c2 bes4 a2 g a bes4 a( g) f2
}
\addlyrics {
  Join us now and share the soft -- ware!
}
```

Then click the engrave button on the toolbar or press {key_engrave}.
{appname} will engrave your file and the music will be displayed in the Music
View on the right. If the engraver reports any errors or warnings they will be
displayed in the Log at the bottom of the screen.

The Music View has many possibilities:

* Hovering over notes and other music objects will highlight them in the text
  on the left window; clicking on them will place a cursor to the left of the
  object also in the left window.

* Use the Ctrl key and your mouse wheel to zoom in and out. Zooming will center
  around the mouse pointer.

* Ctrl-left-click-and-hold the mouse to magnify a small section of the Music
  View without zooming in the whole view.

* Selecting text in the main text window will highlight corresponding notes in
  the Music View; press {key_jump} to explicitly center and highlight a note or
  other objects in the Music View.

* Right-click a selection with the mouse and then press {key_copy_image} or {menu_copy_image}
  to copy the selected music as a raster image to the clipboard, a file or
  another application.

When your music score is complete, engrave it once more but with clickable
notes turned off: menu {menu_engrave}. This significantly reduces the size of
an exported PDF and keeps the folder names on your computer out of it.

If the engraver encounters any warnings or errors in your document they will
show up in the Log window at the bottom of the screen. {appname}
will then highlight these lines in the text view where the errors are.
Clicking the error in the Log window or pressing {key_error} immediately
brings the text cursor to the offending line in your text view. Pressing
{key_error} again will move to the next error message, and so on. The
error line highlights are removed the next time the document is engraved, and
you can also remove them by hand with the option {menu_clear_error_marks}.


#SUBDOCS
scorewiz
quickinsert
musicview
manuscriptview
outline

#VARS
key_engrave    shortcut engrave engrave_preview
key_jump       shortcut musicview music_jump_to_cursor
key_copy_image shortcut musicview music_copy_image
key_error      shortcut logtool log_next_error

menu_clear_error_marks    menu view -> Clear &Error Marks
menu_copy_image           menu music -> Copy to &Image...
menu_engrave              menu lilypond -> Engrave (&publish)
