\version "2.23.11"

#(ly:set-option 'warning-as-error #t)

\header {
  texidoc="Customizing @code{measureBarType} is effective when
appropriate bar lines are defined.  The system should end with a
single thick bar line with a dashed span."
}

\layout {
  ragged-right = ##t
}

\defineBarLine ".-test" #'(#t #f "!")

staff = \new Staff \fixed c' {
  R1
}

piece = \new PianoStaff << \staff \staff >>

\new Score \with { measureBarType = ".-test" } << \piece >>
