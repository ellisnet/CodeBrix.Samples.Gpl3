// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Tests;

/// <summary>
/// EPG16's rules — the context-definition bindings a <c>\layout</c> block runs through,
/// the deprecated-property redirection and the staff accessors — asserted against
/// HAND-COMPUTED values and against upstream's documented contracts. Page COUNTING is
/// fenced end to end in <c>PageBreakingEndToEndTests</c>, because the decision it makes
/// is only meaningful over a real score.
/// </summary>
/// <remarks>
/// <para>
/// Same rule EPG10 through EPG20 set: never assert what the port happens to produce.
/// </para>
/// <para>
/// EPG16 needs one guarantee the earlier groups did not, and the whole file is shaped
/// around it: THIS GROUP'S FAILURES ARE SILENT AND LOOK LIKE LAYOUT OPINIONS. A page
/// breaker that answers one page for any book produces a page; a <c>\layout</c> block
/// that destroys its context definitions produces no diagnostic naming either; an unset
/// deprecated property simply stays set. None of those raises. So every fact below is
/// paired with the case that must come out DIFFERENTLY — a control — because a test that
/// only asserts "something plausible happened" passes just as happily against each of
/// those bugs.
/// </para>
/// </remarks>
[Collection("engine-global-state")]
public class Epg16Tests
{
    private static Symbol Sym(string name) => Symbol.Intern(name);

    /// <summary>
    /// Boots the FULL init layer, parser included. This class lives in the LilyPort test
    /// project rather than beside the other Epg*Tests for exactly that reason: every fact
    /// here is about <c>$defaultlayout</c>, which the <c>ly/</c> layer builds THROUGH THE
    /// PARSER, and the Engine test project cannot reach the parser at all.
    /// </summary>
    private static Interpreter Booted()
    {
        LilyPondInit.DefaultLayout();
        return LilyPondScheme.Current;
    }

    private static object Eval(string source)
    {
        Interpreter interpreter = Booted();
        object result = Nil.Instance;
        foreach (object form in CodeBrix.LilyScheme.Reader.SchemeReader.ReadAll(source, "<epg16>"))
        {
            result = interpreter.TreeIlEvaluator.ExpandAndEval(form, interpreter.CurrentModule);
        }

        return result;
    }

    // ----- context-def.cc's two bindings -----

    /// <summary>
    /// <c>ly:context-def-modify</c> answers a context definition, not a placeholder.
    /// </summary>
    [Fact]
    public void modifying_a_context_definition_answers_a_context_definition()
    {
        //Arrange & Act
        // The shape of the defect this closed: the binding was never registered, so it
        // answered the inert UnportedValue — and `context-defs-from-music' writes that
        // straight back into the output definition with ly:output-def-set-variable!.
        object isDef = Eval(
            "(let* ((defs (ly:output-find-context-def $defaultlayout 'Score))"
            + "       (cd (cdar defs)))"
            + "  (ly:context-def? (ly:context-def-modify cd (ly:make-context-mod))))");

        //Assert
        isDef.Should().Be(true);
    }

    /// <summary>The documented contract: "Does not change @var{def}."</summary>
    [Fact]
    public void modifying_a_context_definition_leaves_the_original_alone()
    {
        //Arrange & Act
        // Load-bearing, not tidiness: the definition being modified is the one the INIT
        // LAYER built and every later file in a sweep shares. A version that mutated in
        // place would leak one file's \layout into every file swept after it — the exact
        // shape of the $defaultpaper leak EPG13 found and the \language leak RATCHET-FIX
        // found. Fenced from BOTH sides: a fresh object AND an unchanged original.
        object sameObject = Eval(
            "(let* ((cd (cdar (ly:output-find-context-def $defaultlayout 'Score)))"
            + "       (out (ly:context-def-modify cd (ly:make-context-mod))))"
            + "  (eq? cd out))");
        object originalUnchanged = Eval(
            "(let* ((cd (cdar (ly:output-find-context-def $defaultlayout 'Score)))"
            + "       (before (length (ly:context-def-lookup cd 'property-ops)))"
            + "       (mods (ly:make-context-mod (list (list 'assign 'skipTypesetting #t)))))"
            + "  (ly:context-def-modify cd mods)"
            + "  (= before (length (ly:context-def-lookup cd 'property-ops))))");
        object copyGrew = Eval(
            "(let* ((cd (cdar (ly:output-find-context-def $defaultlayout 'Score)))"
            + "       (before (length (ly:context-def-lookup cd 'property-ops)))"
            + "       (mods (ly:make-context-mod (list (list 'assign 'skipTypesetting #t))))"
            + "       (out (ly:context-def-modify cd mods)))"
            + "  (> (length (ly:context-def-lookup out 'property-ops)) before))");

        //Assert
        // not the same object; the original's ops are unchanged; the copy's have grown.
        sameObject.Should().Be(false);
        originalUnchanged.Should().Be(true);
        copyGrew.Should().Be(true);
    }

    /// <summary>
    /// <c>ly:context-def-lookup</c>'s three-way answer, fenced on all three.
    /// </summary>
    [Fact]
    public void looking_a_context_definition_facet_up_falls_back_only_when_asked()
    {
        //Arrange & Act
        // Upstream folds SCM_UNDEFINED to '() FIRST and only THEN substitutes the
        // caller's fallback, so an unknown KEY and a genuinely empty value both take the
        // fallback. Collapsing those two tests into one would answer '() where upstream
        // answers the fallback, which is a difference no caller could see until it read
        // the answer as a list.
        object known = Eval("(ly:context-def-lookup"
            + " (cdar (ly:output-find-context-def $defaultlayout 'Score)) 'context-name)");
        object unknownNoFallback = Eval("(ly:context-def-lookup"
            + " (cdar (ly:output-find-context-def $defaultlayout 'Score)) 'no-such-facet)");
        object unknownWithFallback = Eval("(ly:context-def-lookup"
            + " (cdar (ly:output-find-context-def $defaultlayout 'Score)) 'no-such-facet 'fallback)");

        //Assert
        known.Should().Be(Sym("Score"));
        unknownNoFallback.Should().Be(Nil.Instance);
        unknownWithFallback.Should().Be(Sym("fallback"));
    }

    // ----- the deprecated-property path (lily-guile.cc's internal_type_check) -----

    /// <summary>
    /// Unsetting a deprecated property answers the REPLACEMENT, which is where the value is.
    /// </summary>
    [Fact]
    public void unsetting_a_deprecated_property_redirects_to_its_replacement()
    {
        //Arrange
        // The measurable half of the defect: type_check_unset is the ONLY thing that
        // turns the deprecated name into the name the value lives under. Without it the
        // unset removed a property nobody had set and left the real one in place — so a
        // file that switched skipTypesetting on and then off through the alias never
        // switched it off, and engraved nothing at all with no diagnostic.
        Eval("(define-deprecated-property 'translation-type? 'epg16TestUnset boolean?"
            + " #:new-symbol 'skipTypesetting)");

        //Act
        Symbol answered = SchemeUtilities.TypeCheckUnset(
            Sym("epg16TestUnset"), Sym("translation-type?"));

        //Assert
        answered.Should().Be(Sym("skipTypesetting"));
    }

    /// <summary>The control: an ordinary property is not redirected anywhere.</summary>
    [Fact]
    public void unsetting_an_ordinary_property_keeps_its_own_name()
    {
        //Arrange & Act
        // Without this, a redirection that pointed EVERYTHING at skipTypesetting would
        // pass the test above.
        Symbol answered = SchemeUtilities.TypeCheckUnset(
            Sym("skipBars"), Sym("translation-type?"));

        //Assert
        answered.Should().Be(Sym("skipBars"));
    }

    /// <summary>A name that was never a property at all is still REFUSED.</summary>
    [Fact]
    public void unsetting_a_name_that_is_no_property_is_refused()
    {
        //Arrange & Act
        // The third side, and the one a widening would break: the deprecation path must
        // not turn "unknown property" into "sure, here you go". Upstream warns and
        // answers SCM_BOOL_F; the port answers null and the caller writes nothing.
        Symbol answered = SchemeUtilities.TypeCheckUnset(
            Sym("epg16NoSuchPropertyAnywhere"), Sym("translation-type?"));

        //Assert
        answered.Should().BeNull();
    }

    /// <summary>
    /// SETTING a deprecated property converts the value on the way, not just the name.
    /// </summary>
    [Fact]
    public void setting_a_deprecated_property_converts_its_value_too()
    {
        //Arrange
        // A rename is not always only a rename, which is why the description carries an
        // old->new function at all. Here the deprecated property counts in HALVES of what
        // the new one counts in, so a redirection that moved the name and kept the value
        // would write 3 where upstream writes 6 — a wrong answer that looks completely
        // ordinary downstream.
        Eval("(define-deprecated-property 'translation-type? 'epg16TestDoubled integer?"
            + " #:new-symbol 'currentBarNumber"
            + " #:old->new (lambda (x) (* 2 x)))");

        //Act
        bool ok = SchemeUtilities.TypeCheckAssignment(
            Sym("epg16TestDoubled"), 3L, Sym("translation-type?"),
            out Symbol checkedSymbol, out object checkedValue);

        //Assert
        ok.Should().Be(true);
        checkedSymbol.Should().Be(Sym("currentBarNumber"));
        checkedValue.Should().Be(6L);
    }

    // ----- system.cc's staff accessors -----

    /// <summary>
    /// The three staff accessors answer PROPER LISTS, which is all their caller needs.
    /// </summary>
    [Fact]
    public void the_staff_accessors_answer_lists_even_when_there_is_no_alignment()
    {
        //Arrange
        // A bare System with no vertical_alignment object — upstream's `if (align)' case,
        // which answers SCM_EOL. The reason this matters is the caller: paper-system.scm
        // takes (length spaceable-staves) on the very next line, so ANY non-list answer
        // takes the whole book down with "Not a proper list" and names neither the paper
        // variable nor the callback. That is precisely what the unported placeholder did.
        SystemGrob system = new SystemGrob(
            new Pair(new Pair(Sym("meta"),
                new Pair(new Pair(Sym("name"), Sym("System")),
                    new Pair(new Pair(Sym("interfaces"), Nil.Instance), Nil.Instance))),
                Nil.Instance));

        //Act & Assert
        system.GetMaybeSpaceableStaves(StaffFilter.All).Should().Be(Nil.Instance);
        system.GetMaybeSpaceableStaves(StaffFilter.Spaceable).Should().Be(Nil.Instance);
        system.GetMaybeSpaceableStaves(StaffFilter.NonSpaceable).Should().Be(Nil.Instance);
    }

    private static List<object> Flatten(object tree)
    {
        List<object> flat = new List<object>();
        void Walk(object node)
        {
            if (node is Pair pair)
            {
                Walk(pair.Car);
                Walk(pair.Cdr);
                return;
            }

            if (!(node is Nil))
            {
                flat.Add(node);
            }
        }

        Walk(tree);
        return flat;
    }
}
