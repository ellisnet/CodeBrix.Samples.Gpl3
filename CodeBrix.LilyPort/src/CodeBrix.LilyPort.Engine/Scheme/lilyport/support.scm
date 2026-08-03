;;;; support.scm -- the environment LilyPond's scm/ layer assumes.
;;;;
;;;; Copyright (c) 2026 Jeremy Ellis and contributors
;;;;
;;;; CodeBrix.LilyPort is free software: you can redistribute it and/or modify
;;;; it under the terms of the GNU General Public License as published by
;;;; the Free Software Foundation, either version 3 of the License, or
;;;; (at your option) any later version.
;;;;
;;;; This file is NEW IN FAMILY. It is written against the Guile reference manual
;;;; and against what LilyPond's own Scheme reaches for, not translated from
;;;; either project's source. Anything genuinely implemented in LilyPond's C++
;;;; belongs in the engine and is reached through EnginePrimitives instead.

(define-public (index? x)
  (and (integer? x) (exact? x) (>= x 0)))
