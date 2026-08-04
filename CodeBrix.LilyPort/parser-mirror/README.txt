================================================================================
CodeBrix.LilyPort -- parser-mirror/
================================================================================

A BYTE-IDENTICAL MIRROR of LilyPond's grammar and lexer sources, at the pinned
release v2.27.2 (commit 2d621459bd44cb1758f822a69757242eab843060).

    parser.yy   <- lilypond/lily/parser.yy    4,935 lines
    lexer.ll    <- lilypond/lily/lexer.ll     1,354 lines

    sha256  de1c8fba1e532f0d7b7696dfb8c236467a5061adf322d5f2ebdede89f28b50ba  parser.yy
    sha256  574509fb79bcfbd7636c2e2eb26f63b0ccab9acd474de29b5c355f2ee9e24bc1  lexer.ll

Copyright (C) 1997--2026 Han-Wen Nienhuys and Jan Nieuwenhuizen.
GPL-3.0-or-later. Recorded in ../THIRD-PARTY-NOTICES.txt section 1.

--------------------------------------------------------------------------------
NEVER EDIT ANYTHING IN THIS DIRECTORY
--------------------------------------------------------------------------------

This is the same rule, for the same reason, as mf/ -- see decision D10 in
PLAN_codebrix_lilyport_2026-08-02.md. A mirror that has been edited is no longer
a mirror, and the whole re-sync workflow below depends on it being one.

The port does not TRANSLATE these files by hand. It READS them:

  * A repo-owned C# tool (src/CodeBrix.LilyPort.Parsing) reads parser.yy
    directly -- its token declarations, its 36 precedence declarations, its
    ~187 rules, and each action body extracted as opaque text keyed by rule
    identity -- and constructs the LALR(1) tables itself.

  * The rule ACTIONS are hand-ported C#, keyed by rule identity. The reader
    emits a manifest of every rule it found; a fence test asserts that each one
    is either implemented or on a recorded not-yet list. That is the same fence
    pattern as Bootstrap/TypePredicates.cs, and it exists for the same reason:
    a rule that silently does nothing is invisible.

  * The lexer is hand-ported as a modal scanner (13 exclusive start
    conditions). lexer.ll stays mirrored so that each upstream sync's lexer
    delta is a mechanical diff rather than a re-reading.

--------------------------------------------------------------------------------
WHY A MIRROR RATHER THAN GENERATED OUTPUT
--------------------------------------------------------------------------------

This is decision O7, made 2026-08-03; the full record with the measured
evidence is in PLAN_codebrix_lilyport_2026-08-02.md section 13. The short
version:

  (a) Hand-porting the grammar to recursive descent was rejected: re-deriving
      an LL grammar from an LALR one drifts, and this grammar leans hard on
      lookahead.

  (b) A third-party C# LALR/GLR tool was rejected because its generated parser
      does not expose the lookahead directly, and parser.yy has 99
      MYBACKUP/MYREPARSE sites that manipulate it. Our own driver reproduces
      Bison's runtime semantics -- pure parser, error-token recovery, location
      stack, direct lookahead access -- which is what lets those 99 sites port
      as written.

  (c1) Vendoring real Bison's OUTPUT was rejected by Jeremy because it re-runs
      an external toolchain on every upstream re-sync.

  (c2) CHOSEN: vendor the SOURCE, generate the tables in-repo. The external
      toolchain is then never needed again -- not even on re-sync, which was
      the deciding requirement.

--------------------------------------------------------------------------------
THE ONE-TIME FIDELITY ANCHOR
--------------------------------------------------------------------------------

Real GNU Bison runs ONCE, at this pin, and its automaton -- state count,
actions, conflict resolutions -- is diffed against the in-repo generator's
output. Everything that one-time run produces is preserved under

    ../tools/parser-baseline/

as committed data with its own README, exactly as tools/font-build/ preserves
the one-time font build. Nothing downstream needs Bison, and neither does a
re-sync.

--------------------------------------------------------------------------------
RE-SYNCING TO A NEWER LILYPOND
--------------------------------------------------------------------------------

  1. Copy the new lily/parser.yy and lily/lexer.ll over the two files here.
  2. `diff` against the previous mirror to see exactly what changed.
  3. Run the in-repo generator. It hard-fails on any Bison feature it does not
     support, so a grammar that starts using a new one says so at sync time
     rather than mis-parsing quietly.
  4. Hand-port exactly the action bodies the generator names as changed.
  5. Let the rule fence and the regression suite catch the rest.

Working in our favour: 71 of the action sites dispatch through MAKE_SYNTAX into
scm/ly-syntax-constructors.scm, which is ALREADY VENDORED under
src/CodeBrix.LilyPort.Engine/Scheme/lily/. Most action bodies are thin.

================================================================================
