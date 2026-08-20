\version "2.19.8"
\language "deutsch"
\include "articulate.ly"
\include "predefined-guitar-fretboards.ly"

#(set-global-staff-size 23)
#(define output-suffix "part-one")

\bookOutputName "the-whole-score"
\bookOutputSuffix "draft"

theTitle = \markup { \bold "A Title" }

otherName = \markup \italic "second"

plainVariable = "not a markup"

#(define-markup-command (boxedNote layout props text) (markup?)
  (interpret-markup layout props text))

music = { c'4 d' e' f' }

\score {
  \music
  \header { title = \theTitle }
}
