\version "2.24.0"
\score {
  \new StaffGroup <<
    \new GrandStaff <<
      \new Staff { \set Staff.instrumentName = "Upper" c'1 }
      \new Staff { \set Staff.instrumentName = "Lower" \clef bass c1 }
    >>
    \new Staff { \set Staff.instrumentName = "Solo" e'1 }
  >>
}
