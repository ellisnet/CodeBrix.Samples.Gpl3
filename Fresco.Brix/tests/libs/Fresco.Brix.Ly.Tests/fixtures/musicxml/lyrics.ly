\version "2.24.0"
\score {
  <<
    \new Voice = "one" \relative c'' { c4 d e( f) g1 }
    \new Lyrics \lyricsto "one" { \set stanza = "1." Twin -- kle lit __ tle }
    \new Lyrics \lyricsto "one" { \set stanza = "2." How \skip 1 I won -- der }
  >>
}
