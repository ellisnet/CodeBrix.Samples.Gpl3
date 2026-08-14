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
/// This is a PARTIAL class, one file per functional group of the grammar's rule
/// actions. <c>LilyPondRuleActions.TopLevel.cs</c>
/// holds the top-level and header rules, <c>LilyPondRuleActions.StringsAndNumbers.cs</c> the
/// strings, scalars and numbers, and so on; each file's <c>Register*</c> method is called
/// from <see cref="Create"/> here. New groups add a file and a call, nothing else, so
/// work on one group's file does not touch another's.
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

        RegisterTopLevel(table);
        RegisterEmbeddedScheme(table);
        RegisterBookBlocks(table);
        RegisterOutputDefinitions(table);
        RegisterContextDefinitions(table);
        RegisterMusicAssembly(table);
        RegisterPropertyPaths(table);
        RegisterArglistNonBackup(table);
        RegisterArglistBackup(table);
        RegisterArglistCommon(table);
        RegisterPartialFunctions(table);
        RegisterLyricMode(table);
        RegisterStringsAndNumbers(table);
        RegisterChords(table);
        RegisterPostEvents(table);
        RegisterPitchesAndDurations(table);
        RegisterFiguredBass(table);
        RegisterMarkupStructure(table);
        RegisterMarkupCommands(table);

        return table;
    }
}
