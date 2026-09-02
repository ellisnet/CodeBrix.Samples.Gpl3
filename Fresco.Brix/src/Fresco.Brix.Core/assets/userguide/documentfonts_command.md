=== Font Settings Command ===

In LilyPond music and text fonts are selected by issuing specific commands, and
on the *Font Command* tab the way this command is generated can be configured.
Choices made in this tab are remembered throughout {appname} sessions. They
are immediately reflected in the command preview and trigger an update of the
score preview. The content of the command preview will be copied to the
clipboard or inserted in the current document upon clicking on the respective
buttons.

== Considered Text Fonts ==

Three checkboxes under *Set font families* regulate whether roman, sans, and
typewriter fonts are considered in the font-settings command. For each family
that is *not* checked LilyPort will use the default fonts. Beside each checkbox
the tab shows the font that family is currently set to; the names are the ones
the {textfonts} tab offers.

== Command generation style ==

{appname} can apply several styles of generating the font commands, which can be
selected in the *Configure command generation* group.

= Traditional Style =

In the traditional style the fonts are set as *\paper* variables, where
arbitrary fonts may be set or not, including the *music* font. So in addition to
the three text font families it is possible to selectively set the music font as
well. The generated command looks like this:

```lilypond
\paper {
  property-defaults.fonts.music = "emmentaler"
  property-defaults.fonts.serif = "LilyPond Serif"
  property-defaults.fonts.sans = "sans"
  property-defaults.fonts.typewriter = "monospace"
}
```

Only the families that are checked are written. The roman family is written to
the `serif` variable, which is the name LilyPond's own paper defaults give it,
and each variable carries its full `property-defaults.` prefix.

The brace font is no part of the command. It is derived from the music family's
own name — LilyPort looks for that name with `-brace` appended — so a font that
brings a brace face is used for braces by simply being chosen. The dialog keeps
track of which brace font that will be, because that is what tells you whether a
piano brace will draw, but there is nowhere in the command to write it.

It is possible to generate a complete *\paper* block or not, depending on where
the generated command should be used. With *Complete \paper block* unchecked
only the settings themselves are generated, without the block around them.

= openLilyLib Style

{openlilylib} has a *notation-fonts* package with advanced functionality to load
notation fonts (together with text fonts) which {appname} makes available too.
With the openLilyLib approach notation fonts are *always* loaded, therefore the
"music" font selection can not be unchecked.

*NOTE:* In order for this to work the two openLilyLib packages `oll-core` and
`notation-fonts` have to be installed and available.

* *Load openLilyLib*:
This should be unchecked if *oll-core* is already loaded/included elsewhere in
the score files.
* *Load notation-fonts package*:
The same consideration regarding the *notation-fonts* package.
* *Load font extensions:* Some fonts include additional glyphs beyond the
  regular LilyPond coverage. openLilylib provides stylesheets with supporting
  functions making the additional features available as commands or e.g.
  articulations. If this option is checked font extensions are loaded if
  available (otherwise nothing will happen).
* *Font stylesheets:* openLilyLib provides default stylesheets for each
  supported font, adjusting the visual appearance (e.g. line thickness of
  various score items) to match the characteristics of the given font. By
  default these are loaded automatically by the font command *\useNotationFont*.
  However, in some cases it may be necessary to *not* use the default stylesheet
  (e.g. for better integration with the project's "include" strategy) or to
  provide a custom stylesheet, which has to be the name of a file that LilyPort
  can find.


#VARS

openlilylib url https://github.com/openlilylib
textfonts help documentfonts_text
