=== Music Fonts ===

LilyPort can use alternative notation fonts, and the *Document Fonts* dialog can
manage their installation. Fonts are installed into a folder of {appname}'s
own — `fonts/music` inside its application data directory, and the tab says
which folder that is — which is registered into LilyPort's font search path and
is searched *before* the fonts built into LilyPort. There is no LilyPond
installation to install into here, so nothing outside {appname}'s own data is
written to and no administrator privileges are involved. Please refer to
{notationfontswiki} for more information on LilyPond's music font handling.

Freely available notation fonts can be downloaded from {notationfonts}, but it
is recommended to also visit {mtf}, an online store with a collection of
extraordinary non-free music fonts.

== Browsing Music Fonts ==

The main area of the *Music Fonts* tab is a list with the music fonts installed
in that folder. An empty list is the normal state: LilyPort carries
*emmentaler* built into the program, and installing a font here adds it to what
LilyPort can find. For each font the list reveals whether font files are present
in OTF, SVG and WOFF format, which design sizes are missing, and whether the
font brings a dedicated brace font. Most fonts include one but several don't,
and for these the dialog falls back to *emmentaler*.

Selecting a row in the list will select the given font (and its *-brace* font if
available) as the current music font and trigger the {preview} to
be updated.

== Managing Music Fonts ==

Above the font list is a row with buttons to manage music font installation.

= Local Font Repository =

If a directory is made known as a local repository for music fonts (see
{paths}) its content can easily be used as a source for font installation. The
easiest way to make use of this is to download or clone the font repository from
GitHub ({notationfonts}) and save it in this location.

= Installing Music Fonts =

{appname} installs a music font by *copying* its files into its own font folder,
rather than by linking to them: a music font LilyPort cannot find is a fatal
error, so the bytes are kept where the application can vouch for them. The
files a font was installed from may afterwards be moved or deleted.

If a Local Font Repository is defined all fonts from that repository can be
added by clicking on the *Install (repo)* button; anything the folder already
holds is not copied again. This is also done upon *every* start of the dialog,
so a font added to the repository is there the next time the dialog is opened,
without anyone having to think about it.

Clicking the *Install...* button will open a directory chooser dialog, and the
selected directory will be recursively searched, installing all fonts found
there. This may be used for randomly downloaded music fonts.

The *Download...* button, which would fetch fonts from a repository on GitHub,
is disabled: that function is not implemented.

= Removing Music Fonts =

The *Remove...* button removes the currently selected music font from
{appname}'s font folder by deleting its files. Only fonts inside that folder are
removed: if the family holds a file from elsewhere — a file linked in by hand,
say — the removal is refused with a message and the family is left as it was.
The fonts built into the program are not in the folder and never appear in this
list, so *emmentaler* cannot be removed by accident.

#VARS

notationfonts url https://github.com/openlilylib-resources/lilypond-notation-fonts
notationfontswiki url https://github.com/openlilylib-resources/lilypond-notation-fonts/wiki
mtf url https://www.musictypefoundry.com
preview help documentfonts_preview
paths help prefs_paths
