================================================================================
CodeBrix.LilyPort -- tools/parser-baseline/
================================================================================

THE ONE-TIME FIDELITY ANCHOR for Track P, generated 2026-08-03.

This is what real GNU Bison makes of ../../parser-mirror/parser.yy at the pinned
LilyPond v2.27.2. It is COMMITTED DATA, exactly as tools/font-build/ commits the
outputs of the one-time font build, and for the same reason: the toolchain runs
once, its answer is preserved, and nothing downstream ever needs it again.

    parser.output          Bison's full report: the grammar, the symbol tables
                           and all 913 automaton states with their actions.
    productions.tsv        The 617 numbered productions, one per line, in
                           Bison's own numbering.
    automaton-facts.tsv    The figures the in-repo generator must reproduce.

Produced with:

    bison --report=all --report-file=parser.output -o parser.cc parser.yy

    GNU Bison 3.8.2.  Zero shift/reduce and zero reduce/reduce conflicts.

The generated parser.cc is deliberately NOT kept. Vendoring Bison's OUTPUT was
option (c1) and was rejected — see decision O7 in
PLAN_codebrix_lilyport_2026-08-02.md section 13. What is kept is the automaton
the port's own generator has to agree with.

--------------------------------------------------------------------------------
WHAT THIS BASELINE IS FOR
--------------------------------------------------------------------------------

Two things, and the second is the one that has already earned its keep.

  1. When the in-repo LALR(1) table construction is built, its automaton is
     diffed against parser.output: same state count, same actions, same conflict
     resolutions. That is the fidelity check the whole (c2) decision rests on.

  2. It is ground truth for the GRAMMAR READER, and it corrected it immediately.
     The reader first reported 633 productions, 32 mid-rule actions and 221
     nonterminals. Bison reports 616, 15 and 204 — all off by exactly 17.

     The cause: seventeen rules are written `... { action } %prec X`, and the
     reader treated the action as a MID-RULE action because something followed
     it. But %prec is an ANNOTATION on the enclosing rule, not a symbol, so the
     action is a final action. Reading it the other way invents seventeen
     productions and seventeen nonterminals that Bison does not have, and every
     one of them would have been a reduction point the real parser lacks.

     Nothing else would have caught that. The reader's own tests were all green,
     the grammar was structurally consistent, and the invented rules were
     individually plausible. It took the real automaton to say the number was
     wrong.

     Verified figures now match on every count: 616 productions, 130 terminals,
     204 nonterminals, 15 mid-rule actions, 39 empty productions. Asserted in
     tests/CodeBrix.LilyPort.Parsing.Tests/GrammarInventory.cs.

--------------------------------------------------------------------------------
REGENERATING IT (only on a deliberate upstream re-sync)
--------------------------------------------------------------------------------

Only ever needed when parser-mirror/parser.yy is deliberately updated to a newer
LilyPond. Not needed to build CodeBrix.LilyPort, not needed to run its tests, and
not needed by anyone consuming the package.

    cd tools/parser-baseline
    bison --report=all --report-file=parser.output \
          -o /tmp/parser.cc ../../parser-mirror/parser.yy
    # then regenerate productions.tsv and automaton-facts.tsv from it, and
    # update the figures in GrammarInventory.cs to match.

If Bison reports ANY conflict where this baseline records none, stop: upstream
has changed the grammar in a way that changes what the parser accepts, and that
is a fact to understand before porting anything.

================================================================================
