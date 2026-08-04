// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace CodeBrix.LilyPort.Parsing.Actions;

/// <summary>
/// The port's hand-written versions of <c>parser.yy</c>'s rule actions.
/// <para>
/// 479 of the grammar's 616 productions carry an action body. They are C++, and they
/// are ported one at a time rather than translated mechanically — most are thin,
/// because 71 of the action sites dispatch through <c>MAKE_SYNTAX</c> into
/// <c>scm/ly-syntax-constructors.scm</c>, which is already vendored under
/// <c>CodeBrix.LilyPort.Engine/Scheme/lily/</c> and needs no porting at all.
/// </para>
/// <para>
/// This is a PARTIAL class, one file per RULE ACTION GROUP — the session-sized,
/// disjoint groups the porting effort is organized by. <c>LilyPondRuleActions.Rag1.cs</c>
/// holds the top-level and header rules, <c>LilyPondRuleActions.Rag13.cs</c> the
/// strings, scalars and numbers, and so on; each file's <c>RegisterRagN</c> is called
/// from <see cref="Create"/> here. New groups add a file and a call, nothing else, so
/// parallel porting sessions do not touch each other's files.
/// </para>
/// <para>
/// THE VALUE MODEL, which every action follows. <c>SCM</c> is <see cref="object"/>:
/// <c>SCM_UNSPECIFIED</c> is <see cref="CodeBrix.LilyScheme.Values.Unspecified"/>.Instance,
/// <c>SCM_UNDEFINED</c> is <see cref="CodeBrix.LilyScheme.Values.DefaultArgument"/>.Instance,
/// <c>SCM_EOL</c> is <see cref="CodeBrix.LilyScheme.Values.Nil"/>.Instance, booleans are
/// <see cref="bool"/>, symbols are interned
/// <see cref="CodeBrix.LilyScheme.Values.Symbol"/>s, strings are CLR strings (with
/// <see cref="CodeBrix.LilyScheme.Values.MutableString"/> accepted wherever a string is
/// tested for), and numbers are the
/// <see cref="CodeBrix.LilyScheme.Numeric.SchemeNumber"/> tower. Bison runs every
/// action with <c>$$</c> already set to <c>$1</c>, so an upstream body that never
/// assigns <c>$$</c> is ported as an explicit <c>return values[0]</c>.
/// </para>
/// <para>
/// Everything not registered here is on <see cref="RuleActionTable.NotYetPorted"/>,
/// which is COMPUTED from the committed manifest rather than maintained by hand — so
/// porting an action removes it from the worklist by construction, and a rule that
/// stops existing on a re-sync cannot linger on it. <c>RuleActionFenceTests</c> holds
/// both ends of that.
/// </para>
/// <para>
/// A rule with no registered action still reduces: the driver applies Bison's default,
/// <c>$$ = $1</c>. That is the correct behaviour for the 137 productions upstream also
/// leaves actionless, and it is why the pass-through rules need nothing here.
/// </para>
/// </summary>
public static partial class LilyPondRuleActions
{
    /// <summary>Builds the action table.</summary>
    /// <returns>The table, ready to bind against the parse tables.</returns>
    public static RuleActionTable Create()
    {
        RuleActionTable table = new RuleActionTable();

        RegisterRag1(table);
        RegisterRag2(table);
        RegisterRag3(table);
        RegisterRag4(table);
        RegisterRag5(table);
        RegisterRag6(table);
        RegisterRag7(table);
        RegisterRag8(table);
        RegisterRag9(table);
        RegisterRag10(table);
        RegisterRag11(table);
        RegisterRag12(table);
        RegisterRag13(table);
        RegisterRag14(table);
        RegisterRag15(table);
        RegisterRag16(table);
        RegisterRag17(table);
        RegisterRag18(table);
        RegisterRag19(table);

        return table;
    }
}
