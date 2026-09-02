=== Document Fonts Preview ===

The right hand side of the *Document Fonts* dialog provides a preview pane to
see the effect of the font selection in the actual score context. Whenever a
choice is made in one of the tabs on the left (change of a font or modification
of the command generation settings) the preview is updated. {appname} caches the
results to allow instant switching between versions where possible. See the
section at the bottom of this page for details about the caching strategy.

== Example Source ==

There are three main sources for the score to be previewed: provided default
examples, the "current document", and arbitrary custom files, each with their
own considerations.

= Default Examples =

Six examples have been provided to demonstrate a range of typical notation use
cases: *Bach (Piano)*, *Scriabin (Piano)*, *Berg (String Quartet)*, *Real Book
(Lead Sheet)*, *Schenker Diagram* and *Glyphs*. They have been chosen and
tailored to showcase the use of fonts in short examples. Of particular interest
is the "Glyphs" example that shows a representative selection of available
glyphs as a traditional specimen sheet.

= Custom Files =

Choosing *Custom* in the example chooser makes it possible to select a custom
file and preview the effects of font choices in the context of a real-world
score, e.g. the one for which the fonts are actually selected.

*NOTE:* Due to a limitation that cannot fully be handled by {appname} the font
preview doesn't work properly when the compiled example includes a *\paper*
block (which rules out a lot of real-world files). This is because that *\paper*
block will be read *after* the font setting command injected by {appname}, which
will reset LilyPort's internal font settings. If the file or any of its included
files does contain a *\paper* block the compilation will work, but the font
selections are not applied. This may be especially confusing if the document
itself specifies alternative fonts which will then be used.


= Current Document =

Choosing *Current Document* will compile the document that is currently being
edited. Note that this may or may not complete successfully, resulting in an
empty preview and the message "Engraving failed."

The considerations about a *\paper* block outlined above apply here as well.

== Preview Caching ==

In order to optimize the user experience when comparing font choices {appname}
caches the compiled previews. Note that engravings are produced for *all*
combinations of selected fonts and configuration options, and also for any
modified versions of the input files, so each one is filed under a digest of the
score that produced it. A custom file and the current document can change
underneath such a cache, so those two are engraved into a scratch directory that
is thrown away when the dialog is closed.

The *provided default* examples are cached by default in a directory below the
system's temporary directory, which will be purged upon computer shutdown, so
they have to be recreated in each new {appname} session. However, if the `Music
Fonts Preview Cache` option in {paths} points to a user-writable persistent
directory the renderings of those examples are cached there and will be
displayed instantly across multiple {appname} sessions.

#VARS
paths help prefs_paths
