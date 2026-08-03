\version "2.25.4"

#(ly:set-option 'warning-as-error #t)

\header {
  texidoc = "This piece consists of three consecutive sections using
@code{\\repeat segno 1}.  Because of the count, no repeat notation
should appear."
}

\layout {
  \context {
    \Score
    barNumberVisibility = #(every-nth-bar-number-visible 1)
    \override BarNumber.break-visibility = #all-visible
  }
}

piece = \fixed c' {
  \set Score.printTrivialVoltaRepeats = ##t % to prove no impact on segno style
  \repeat segno 1 f1
  \repeat segno 1 g1
  \repeat segno 1 { a1 \alternative { b1 } }
}

\new Score {
  \new Staff \with { instrumentName = "segno" } { \piece }
}

\new Score {
  \new Staff \with { instrumentName = "unfolded" } { \unfoldRepeats \piece }
}
