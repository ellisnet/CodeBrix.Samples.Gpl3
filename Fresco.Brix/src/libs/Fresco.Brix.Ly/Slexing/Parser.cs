// === Python slexer (Stateful Lexer) module ===
//
// Copyright (c) 2008 - 2015 by Wilbert Berendsen
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation; either version 3
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
// See http://www.gnu.org/licenses/ for more information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Fresco.Brix.Ly.Slexing; //was previously: ly/slexer.py (classes Parser, PatternProperty, ParserMeta);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Abstract base class for parsers: a list of token rules combined into one
/// alternation, searched through the text.
/// <para>
/// Upstream builds the combined pattern in a metaclass-installed lazy property
/// (<c>PatternProperty</c>): token classes sharing the same rx string are grouped
/// under ONE alternation branch, and <c>test_match</c> decides between them — the
/// LAST class with that rx is instantiated without being asked. The combined
/// pattern is cached per PARSER CLASS, exactly as upstream writes it back onto the
/// class object.
/// </para>
/// </summary>
public abstract class Parser
{
    private static readonly ConcurrentDictionary<Type, CompiledPattern> Patterns
        = new ConcurrentDictionary<Type, CompiledPattern>();

    /// <summary>Gets the <c>re.compile</c> flags to use.</summary>
    public virtual RegexOptions ReFlags => RegexOptions.None;

    /// <summary>
    /// Gets the rule making tokens for pieces of text no item matched, or
    /// <see langword="null"/> when skipped text is simply skipped.
    /// </summary>
    public virtual TokenRule Default => null;

    /// <summary>Gets the token rules to look for in text, in precedence order.</summary>
    protected abstract TokenRule[] Items { get; }

    /// <summary>
    /// Parses text from a position: a search, returning the first match of any item
    /// at or after it, or <see langword="null"/>.
    /// </summary>
    /// <param name="text">The text.</param>
    /// <param name="pos">Where to start searching.</param>
    /// <returns>The match, or <see langword="null"/>.</returns>
    public virtual Match Parse(string text, int pos)
    {
        Match match = Pattern.Searcher.Match(text, pos);
        return match.Success ? match : null;
    }

    /// <summary>
    /// Makes the Token instance of the correct class for a match returned by
    /// <see cref="Parse"/> — upstream's <c>Parser.token()</c>, <c>test_match</c>
    /// dispatch included.
    /// </summary>
    /// <param name="match">The match.</param>
    /// <returns>The token.</returns>
    public Token MakeToken(Match match)
    {
        CompiledPattern pattern = Pattern;
        int group = pattern.MatchedGroup(match);
        IReadOnlyList<TokenRule> rules = pattern.RulesForGroup(group);
        for (int i = 0; i < rules.Count - 1; i++)
        {
            if (rules[i].TestMatch == null || rules[i].TestMatch(match))
            {
                return rules[i].Create(match.Value, match.Index);
            }
        }

        return rules[rules.Count - 1].Create(match.Value, match.Index);
    }

    /// <summary>
    /// (Internal) Called by <see cref="State.Follow"/>. Does nothing here; the
    /// fallthrough parser overrides it.
    /// </summary>
    /// <param name="token">The token being followed.</param>
    /// <param name="state">The state following it.</param>
    /// <returns>Whether the state fell through and following must continue.</returns>
    public virtual bool FollowInternal(Token token, State state) => false;

    /// <summary>Returns the parser's instance values, for freezing.</summary>
    /// <returns>The values a thaw hands back to the constructor.</returns>
    public virtual object[] Freeze() => Array.Empty<object>();

    /// <summary>
    /// Called when no match is returned by <see cref="Parse"/>. Returning
    /// <see langword="true"/> stops the tokenizer; returning <see langword="false"/>
    /// means the state was altered and parsing continues. The base implementation
    /// simply returns <see langword="true"/>.
    /// </summary>
    /// <param name="state">The state to alter.</param>
    /// <returns>Whether parsing should stop.</returns>
    public virtual bool Fallthrough(State state) => true;

    /// <summary>
    /// Called by the default implementation of <see cref="Token.UpdateState"/>.
    /// Does nothing by default.
    /// </summary>
    /// <param name="state">The state.</param>
    /// <param name="token">The token just made.</param>
    public virtual void UpdateState(State state, Token token)
    {
    }

    /// <summary>Gets this parser class's combined pattern, built once per class.</summary>
    internal CompiledPattern Pattern
        => Patterns.GetOrAdd(GetType(), _ => CompiledPattern.Build(Items, ReFlags, Anchored));

    /// <summary>Gets the item rules, for the follow test.</summary>
    internal TokenRule[] ItemRules => Items;

    /// <summary>
    /// Gets whether the combined pattern anchors at the position — the fallthrough
    /// parser's <c>match</c> against the base <c>search</c>.
    /// </summary>
    protected virtual bool Anchored => false;

    /// <summary>Reproduces a parser from its frozen values — upstream's
    /// <c>cls.thaw(attrs)</c>, which calls <c>cls(*attrs)</c>.</summary>
    /// <param name="parserType">The parser class.</param>
    /// <param name="values">The frozen constructor values.</param>
    /// <returns>The parser.</returns>
    public static Parser Thaw(Type parserType, object[] values)
        => (Parser)Activator.CreateInstance(parserType, values);
}

/// <summary>
/// A parser class's combined regular expression and its group-to-rules index —
/// upstream's <c>PatternProperty</c> product (<c>pattern</c> + <c>index</c>).
/// </summary>
public sealed class CompiledPattern
{
    private readonly Regex _searcher;
    private readonly List<TokenRule>[] _rulesByGroup;
    private readonly int[] _groupNumbers;

    private CompiledPattern(Regex searcher, List<TokenRule>[] rulesByGroup, int[] groupNumbers)
    {
        _searcher = searcher;
        _rulesByGroup = rulesByGroup;
        _groupNumbers = groupNumbers;
    }

    /// <summary>Gets the combined regular expression.</summary>
    public Regex Searcher => _searcher;

    /// <summary>
    /// Builds the combined pattern: unique items in order, grouped by rx string, one
    /// named group <c>g_i</c> per distinct rx.
    /// </summary>
    /// <param name="items">The parser's rules, in precedence order.</param>
    /// <param name="flags">The regex options.</param>
    /// <param name="anchored">Whether to anchor at the search position
    /// (the fallthrough parser's <c>match</c> semantics), via <c>\G</c>.</param>
    /// <returns>The compiled pattern.</returns>
    public static CompiledPattern Build(TokenRule[] items, RegexOptions flags, bool anchored)
    {
        // uniq() over the items, then group by IDENTICAL rx string, keeping first
        // appearance order -- upstream's counter/patterns pair.
        List<string> patterns = new List<string>();
        Dictionary<string, List<TokenRule>> byPattern = new Dictionary<string, List<TokenRule>>();
        HashSet<TokenRule> seen = new HashSet<TokenRule>();
        foreach (TokenRule rule in items)
        {
            if (!seen.Add(rule))
            {
                continue;
            }

            string rx = rule.Pattern;
            if (byPattern.TryGetValue(rx, out List<TokenRule> rules))
            {
                rules.Add(rule);
            }
            else
            {
                byPattern[rx] = new List<TokenRule> { rule };
                patterns.Add(rx);
            }
        }

        StringBuilder combined = new StringBuilder();
        if (anchored)
        {
            // \G makes every attempt after the requested position fail at its first
            // node, which is how a SEARCHING engine expresses Python's re.match.
            combined.Append(@"\G(?:");
        }

        for (int i = 0; i < patterns.Count; i++)
        {
            if (i > 0)
            {
                combined.Append('|');
            }

            combined.Append("(?<g_").Append(i).Append('>').Append(patterns[i]).Append(')');
        }

        if (anchored)
        {
            combined.Append(')');
        }

        Regex searcher = new Regex(combined.ToString(), flags);
        List<TokenRule>[] rulesByGroup = new List<TokenRule>[patterns.Count];
        int[] groupNumbers = new int[patterns.Count];
        for (int i = 0; i < patterns.Count; i++)
        {
            rulesByGroup[i] = byPattern[patterns[i]];
            groupNumbers[i] = searcher.GroupNumberFromName("g_" + i);
        }

        return new CompiledPattern(searcher, rulesByGroup, groupNumbers);
    }

    /// <summary>
    /// Answers which alternation branch a match took — upstream reads this off
    /// <c>match.lastindex</c>; here the successful <c>g_i</c> group names it.
    /// </summary>
    /// <param name="match">The match.</param>
    /// <returns>The branch ordinal.</returns>
    public int MatchedGroup(Match match)
    {
        for (int i = 0; i < _groupNumbers.Length; i++)
        {
            Group group = match.Groups[_groupNumbers[i]];
            if (group.Success && group.Index == match.Index && group.Length == match.Length)
            {
                return i;
            }
        }

        // A group can match a strict inner part only through nested construction the
        // item lists never use; the first successful group is then the answer.
        for (int i = 0; i < _groupNumbers.Length; i++)
        {
            if (match.Groups[_groupNumbers[i]].Success)
            {
                return i;
            }
        }

        throw new InvalidOperationException("no alternation branch matched");
    }

    /// <summary>Gets the rules sharing one alternation branch, in items order.</summary>
    /// <param name="group">The branch ordinal.</param>
    /// <returns>The rules.</returns>
    public IReadOnlyList<TokenRule> RulesForGroup(int group) => _rulesByGroup[group];
}
