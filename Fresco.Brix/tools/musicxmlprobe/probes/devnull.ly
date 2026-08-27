\version "2.24.0"
\score {
  <<
    \new Staff { c'4 d' e' f' }
    \new Devnull { c'4 d' e' f' }
    \new Staff { \clef bass c4 d e f }
  >>
}
