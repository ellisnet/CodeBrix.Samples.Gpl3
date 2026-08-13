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

;; Nothing is defined here at present, and the file is kept for the next thing that
;; genuinely belongs in it.
;;
;; It used to carry index?, character for character what scm/lily.scm:221 defines:
;;
;;   (define-public (index? x)
;;     (and (integer? x) (exact? x) (>= x 0)))
;;
;; Removed 2026-08-13 (EPG24). A duplicate of a binding the vendored layer already
;; provides is not harmless, even when the two bodies are identical. This file is
;; loaded into the ROOT module before lily.scm runs, and lily.scm's own definition
;; goes into (lily) -- so the name ended up bound to two DIFFERENT procedure objects,
;; and everything that identifies a type by the IDENTITY of its predicate silently
;; picked whichever one its module saw first.
;;
;; scm/lily.scm's type-p-name-alist is exactly that kind of table: it is keyed by the
;; predicate OBJECT, and type-name (scm/c++.scm:309) falls back to the procedure's own
;; name with the "?" trimmed when the lookup misses. So five paper variables documented
;; their type as "index" where upstream says "non-negative, exact integer" -- no error,
;; no warning, and a plausible-looking word in the manual.
;;
;; Before adding anything here, check whether the vendored layer already defines it.
