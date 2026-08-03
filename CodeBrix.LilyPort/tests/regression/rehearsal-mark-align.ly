
\header {

  texidoc = "Marks are put on top a breakable symbol,
  according to the value of @code{break-align-symbols} grob property.
  The same holds for @code{BarNumber} grobs."

}

\version "2.23.14"
  
\relative {
  c'1 \textMark "foo"
  c1
  \key cis \major
  \clef alto
  \override Score.TextMark.break-align-symbols = #'(key-signature)
  \textMark "on-key"
  cis
  \key ces \major
  \override Score.TextMark.break-align-symbols = #'(clef)
  \clef treble
  \textMark "on clef"
  ces
}
