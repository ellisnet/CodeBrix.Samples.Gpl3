// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyPort.Parsing.Grammar;
using CodeBrix.LilyPort.Parsing.Lalr;

namespace CodeBrix.LilyPort.Parsing.Actions;

/// <summary>
/// The hand-ported rule actions, keyed by rule IDENTITY.
/// <para>
/// The grammar's 479 action bodies are C++ that mostly dispatch through
/// <c>MAKE_SYNTAX</c> into <c>scm/ly-syntax-constructors.scm</c> — which is already
/// vendored under <c>CodeBrix.LilyPort.Engine/Scheme/lily/</c>, so most of them are
/// thin. They are ported one at a time, and <see cref="Bind"/> turns identities into
/// the rule numbers the driver wants.
/// </para>
/// <para>
/// Keyed by identity rather than rule number on purpose: a number shifts the moment
/// anything is inserted above it in <c>parser.yy</c>, so a re-sync that added one rule
/// near the top would silently re-point every action below it.
/// </para>
/// </summary>
public sealed class RuleActionTable
{
    private readonly Dictionary<string, RuleAction> _actions
        = new Dictionary<string, RuleAction>(StringComparer.Ordinal);

    /// <summary>Gets the identities that have a ported action.</summary>
    public IReadOnlyCollection<string> Implemented => _actions.Keys;

    /// <summary>Registers a ported action for a rule.</summary>
    /// <param name="identity">The rule's identity, as the manifest records it.</param>
    /// <param name="action">The ported action.</param>
    /// <returns>This table, so registrations can be chained.</returns>
    public RuleActionTable Add(string identity, RuleAction action)
    {
        if (identity == null)
        {
            throw new ArgumentNullException(nameof(identity));
        }

        if (_actions.ContainsKey(identity))
        {
            throw new InvalidOperationException(
                "Two actions were registered for the same rule: " + identity);
        }

        _actions[identity] = action ?? throw new ArgumentNullException(nameof(action));
        return this;
    }

    /// <summary>
    /// Resolves the registered actions against a grammar's rule numbering.
    /// </summary>
    /// <param name="tables">The tables the driver will run on.</param>
    /// <returns>The actions, keyed by rule number.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when an identity matches no rule. That is the re-sync failure this design
    /// exists to catch: an action registered for a production the grammar no longer has
    /// would otherwise just never run.
    /// </exception>
    public Dictionary<int, RuleAction> Bind(ParseTables tables)
    {
        if (tables == null)
        {
            throw new ArgumentNullException(nameof(tables));
        }

        Dictionary<string, int> numbers = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (TableRule rule in tables.Rules)
        {
            if (rule.Source?.Identity != null)
            {
                numbers[rule.Source.Identity] = rule.Index;
            }
        }

        Dictionary<int, RuleAction> bound = new Dictionary<int, RuleAction>();
        List<string> unknown = new List<string>();

        foreach (KeyValuePair<string, RuleAction> entry in _actions)
        {
            if (numbers.TryGetValue(entry.Key, out int number))
            {
                bound[number] = entry.Value;
            }
            else
            {
                unknown.Add(entry.Key);
            }
        }

        if (unknown.Count > 0)
        {
            throw new InvalidOperationException(
                "These rule actions name productions the grammar does not have — the mirror"
                + " was re-synced and they need re-porting: " + string.Join("; ", unknown));
        }

        return bound;
    }

    /// <summary>
    /// Returns the rules that upstream gives an action body but the port has not ported
    /// yet. This is the porting worklist, computed rather than maintained by hand.
    /// </summary>
    /// <returns>The outstanding rule identities, in grammar order.</returns>
    public IReadOnlyList<string> NotYetPorted()
    {
        List<string> outstanding = new List<string>();

        foreach (ManifestEntry entry in RuleManifest.Entries)
        {
            if (entry.HasAction && !_actions.ContainsKey(entry.Identity))
            {
                outstanding.Add(entry.Identity);
            }
        }

        return outstanding;
    }
}
