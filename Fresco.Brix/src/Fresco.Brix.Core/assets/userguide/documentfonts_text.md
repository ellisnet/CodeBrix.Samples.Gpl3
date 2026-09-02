=== Text Fonts ===

== Browsing Text Fonts ==

The `Text Fonts` tab lists the font family names LilyPort can *select*. This is
not a list of the fonts installed on the computer: LilyPort resolves a family
name through its own table of generic names and through whatever the document
itself registered, and {appname} never falls back to a font of the desktop
environment — that is deliberate, and a name LilyPort does not know draws
nothing rather than something that happens to be at hand. The list is therefore
the names that do something: `serif`, `sans`, `sans-serif`, `monospace`,
`LilyPond Serif`, `LilyPond Sans Serif` and `LilyPond Monospace`, plus any
family the document supplied. A line above the tree view says how many there
are and which version of LilyPort listed them.

Each name can be unfolded to show the faces it reaches, in the order LilyPort
tries them. There is no rendered sample of the face: the faces LilyPort ships
are embedded in its own assembly rather than installed on the system, so no
sample could be drawn in the face itself, and drawing it in another font would
say something untrue. The second column names the face and where its bytes
live instead — the assembly and resource for a font LilyPort brings, a real
path for one the document supplied.

Below the tree view is an entry field where a filter expression can be entered
that incrementally filters the list to quickly locate fonts. Searching is case
insensitive, and by default strings may be anywhere in the font name: the filter
`serif` will equally show `serif`, `sans-serif` and `LilyPond Serif`. Filter
expressions support regular expressions, of which particularly the "border"
characters are of interest:

* *^*: Beginning of string: *^sans* shows *sans* and *sans-serif* but hides
*LilyPond Sans Serif*
* *$*: End of string: *sans$* shows *sans* alone
* *\b*: word boundaries: *\bserif* still shows *sans-serif*, because the hyphen
is a word boundary, while *^serif* does not.

== Selecting Text Fonts ==

Below the tree view are three buttons, *Set as Roman*, *Set as Sans* and *Set as
Typewriter*. Clicking on any of the three takes the name selected in the tree
view as the corresponding font family and triggers the {preview} to be updated;
each button's tool tip says which font that family is set to at the moment.
Selecting one of a name's faces works as well — the name it sits under is what
goes into the command either way.

=== Miscellaneous Font Information ===

The last tab says where LilyPort looks for fonts. *Searched Font Directories*
are the folders it consults before anything else, among them the folder music
fonts are installed into; *Fonts built into the program* names the assembly the
faces are embedded in; and *Fonts supplied by the document* lists the faces the
document registered itself. None of this refers to the desktop environment's
fonts, which LilyPort does not read.

#VARS

preview help documentfonts_preview
