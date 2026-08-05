// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using CodeBrix.LilyPort.Parsing.Lalr;

namespace CodeBrix.LilyPort.Parsing.Driver;

/// <summary>A span of input, as <c>%locations</c> gives every symbol.</summary>
public readonly struct SourceSpan
{
    /// <summary>Initializes a span.</summary>
    /// <param name="fileName">The file the span is in.</param>
    /// <param name="startLine">The first line, counting from one.</param>
    /// <param name="startColumn">The first column, counting from one.</param>
    /// <param name="endLine">The last line.</param>
    /// <param name="endColumn">One past the last column.</param>
    /// <param name="startOffset">The first character's offset in the file, or a negative
    /// value when the span was made without one.</param>
    /// <param name="endOffset">One past the last character's offset in the file.</param>
    public SourceSpan(
        string fileName,
        int startLine,
        int startColumn,
        int endLine,
        int endColumn,
        int startOffset = -1,
        int endOffset = -1)
    {
        FileName = fileName;
        StartLine = startLine;
        StartColumn = startColumn;
        EndLine = endLine;
        EndColumn = endColumn;
        StartOffset = startOffset;
        EndOffset = endOffset;
    }

    /// <summary>Gets the file the span is in.</summary>
    public string FileName { get; }

    /// <summary>Gets the first line, counting from one.</summary>
    public int StartLine { get; }

    /// <summary>Gets the first column, counting from one.</summary>
    public int StartColumn { get; }

    /// <summary>Gets the last line.</summary>
    public int EndLine { get; }

    /// <summary>Gets one past the last column.</summary>
    public int EndColumn { get; }

    /// <summary>
    /// Gets the first character's offset in the file, or a negative value when the span
    /// carries none.
    /// <para>What turns a span into a real <c>Input</c>: upstream's <c>Input</c> holds two
    /// pointers into the source buffer, so a location can quote its own line and a
    /// diagnostic can point a caret at it. A span built by hand in a fixture has no
    /// offsets, and answers "position unknown" rather than a plausible wrong place.</para>
    /// </summary>
    public int StartOffset { get; }

    /// <summary>Gets one past the last character's offset in the file.</summary>
    public int EndOffset { get; }

    /// <summary>
    /// Returns the span covering both of two spans, which is what a reduce does to its
    /// right-hand side.
    /// </summary>
    /// <param name="first">The first span.</param>
    /// <param name="last">The last span.</param>
    /// <returns>The covering span.</returns>
    public static SourceSpan Join(SourceSpan first, SourceSpan last)
        => new SourceSpan(
            first.FileName ?? last.FileName,
            first.StartLine,
            first.StartColumn,
            last.EndLine,
            last.EndColumn,
            first.StartOffset,
            last.EndOffset);

    /// <summary>Returns the external representation.</summary>
    /// <returns>The span as <c>file:line:column</c>.</returns>
    public override string ToString()
        => (FileName ?? "<input>")
           + ":" + StartLine.ToString(CultureInfo.InvariantCulture)
           + ":" + StartColumn.ToString(CultureInfo.InvariantCulture);
}

/// <summary>One token from the lexer.</summary>
public readonly struct ParserToken
{
    /// <summary>Initializes a token.</summary>
    /// <param name="symbol">The terminal's symbol number.</param>
    /// <param name="value">The semantic value.</param>
    /// <param name="location">Where it came from.</param>
    public ParserToken(int symbol, object value, SourceSpan location)
    {
        Symbol = symbol;
        Value = value;
        Location = location;
    }

    /// <summary>Gets the terminal's symbol number.</summary>
    public int Symbol { get; }

    /// <summary>Gets the semantic value.</summary>
    public object Value { get; }

    /// <summary>Gets where it came from.</summary>
    public SourceSpan Location { get; }
}

/// <summary>
/// Where the parser gets its tokens.
/// <para>
/// <see cref="PushExtraToken"/> is not a convenience. LilyPond's grammar rewrites its
/// own input from inside rule actions — the <c>MYBACKUP</c> and <c>MYREPARSE</c> macros
/// push the current lookahead back and put different tokens in front of it — and there
/// are 99 sites doing it. A lexer that cannot be pushed back into cannot run this
/// grammar.
/// </para>
/// </summary>
public interface IParserInput
{
    /// <summary>Returns the next token, or the end-of-input token at the end.</summary>
    /// <returns>The token.</returns>
    ParserToken Next();

    /// <summary>
    /// Puts a token in front of everything not yet read. The most recently pushed token
    /// is the next one returned.
    /// </summary>
    /// <param name="token">The token to push.</param>
    void PushExtraToken(ParserToken token);
}

/// <summary>What a rule's action does when the rule reduces.</summary>
/// <param name="context">The parse in progress, for actions that manipulate it.</param>
/// <param name="values">The right-hand side's semantic values, left to right — Bison's
/// <c>$1</c> through <c>$n</c>.</param>
/// <param name="locations">Each right-hand-side symbol's own span — Bison's <c>@1</c>
/// through <c>@n</c>. Upstream actions use these individually (an error in
/// <c>lilypond_header_body</c> points at <c>@2</c>, not at the whole rule), so they are
/// handed over rather than only their join.</param>
/// <param name="location">The span the whole right-hand side covers — Bison's <c>@$</c>.</param>
/// <returns>The left-hand side's semantic value.</returns>
public delegate object RuleAction(ParseContext context, object[] values, SourceSpan[] locations, SourceSpan location);

/// <summary>
/// What a rule action can reach: the lookahead, the input, and the error count.
/// <para>
/// The lookahead being READABLE AND CLEARABLE from an action is the whole reason the
/// port writes its own driver rather than generating one with a third-party tool
/// (decision O7). A generated parser hides it, and <c>MYBACKUP</c>/<c>MYREPARSE</c>
/// cannot be expressed without it.
/// </para>
/// </summary>
public sealed class ParseContext
{
    private readonly LalrParser _parser;

    internal ParseContext(LalrParser parser, IParserInput input)
    {
        _parser = parser;
        Input = input;
    }

    /// <summary>Gets the token source, which actions may push tokens back into.</summary>
    public IParserInput Input { get; }

    /// <summary>Gets or sets the caller's own state, threaded through every action.</summary>
    public object UserState { get; set; }

    /// <summary>Gets how many syntax errors have been reported.</summary>
    public int ErrorCount => _parser.ErrorCount;

    /// <summary>Gets a value indicating whether a lookahead token has been read.</summary>
    public bool HasLookahead => _parser.HasLookahead;

    /// <summary>Gets the lookahead token. Only meaningful when <see cref="HasLookahead"/>.</summary>
    public ParserToken Lookahead => _parser.Lookahead;

    /// <summary>
    /// Pushes the pending lookahead back into the input and forgets it, so the next
    /// token is read afresh. This is what <c>MYBACKUP</c> and <c>MYREPARSE</c> both
    /// open with.
    /// </summary>
    public void PushBackLookahead()
    {
        if (_parser.HasLookahead)
        {
            Input.PushExtraToken(_parser.Lookahead);
            _parser.ClearLookahead();
        }
    }

    /// <summary>Reports a syntax error at a location.</summary>
    /// <param name="message">The message.</param>
    /// <param name="location">Where it happened.</param>
    public void Error(string message, SourceSpan location) => _parser.ReportError(message, location);

    /// <summary>
    /// Reads a semantic value already ON THE PARSE STACK, below the production being
    /// reduced. This is how a MID-RULE action reaches the outer rule's earlier
    /// components, which Bison lets it do: in <c>A: X1 .. Xn { action } Y ..</c> the
    /// action's <c>$k</c> compiles to the stack slot <c>k − n</c> relative to the
    /// top, which is <c>StackValue(n − k)</c> here — so for the grammar's usual
    /// one-preceding-symbol case, <c>$1</c> is <c>StackValue(0)</c>. Added for the
    /// book, bookpart and score mid-rule actions (<c>$@5</c>–<c>$@7</c>), whose
    /// upstream bodies all read <c>$1</c>.
    /// </summary>
    /// <param name="depth">How many slots below the top of the stack; 0 is the top.</param>
    /// <returns>The semantic value.</returns>
    public object StackValue(int depth) => _parser.PeekValue(depth);

    /// <summary>
    /// Overwrites a semantic value already on the parse stack — the mid-rule
    /// counterpart of ASSIGNING <c>$k</c>, which upstream's <c>score_items</c>
    /// mid-rule really does (<c>$1 = scm_cons (ly_make_module (), $1);</c>). The
    /// depth convention is <see cref="StackValue"/>'s.
    /// </summary>
    /// <param name="depth">How many slots below the top of the stack; 0 is the top.</param>
    /// <param name="value">The value to store in the slot.</param>
    public void SetStackValue(int depth, object value) => _parser.PokeValue(depth, value);

    /// <summary>
    /// Reads a SPAN already on the parse stack — <see cref="StackValue"/>'s companion,
    /// for a mid-rule action's <c>@k</c> rather than its <c>$k</c>. The depth
    /// convention is the same: after one preceding symbol, <c>@1</c> is depth 0.
    /// <para>
    /// A mid-rule reduces as an empty production, so its own <c>locations</c> array is
    /// empty and <c>@$</c> is the point-span Bison gives an empty rule — neither of
    /// which is <c>@1</c>. <c>markup_scm</c>'s mid-rule needs the real thing: it
    /// reports "not a markup" AT the offending expression and hands the same span to
    /// three <c>MYBACKUP</c> calls.
    /// </para>
    /// </summary>
    /// <param name="depth">How many slots below the top of the stack; 0 is the top.</param>
    /// <returns>The span.</returns>
    public SourceSpan StackLocation(int depth) => _parser.PeekLocation(depth);
}

/// <summary>Raised when the parse cannot continue.</summary>
public sealed class ParseAbortedException : Exception
{
    /// <summary>Initializes the exception.</summary>
    /// <param name="message">What went wrong.</param>
    public ParseAbortedException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The LALR parsing engine: Bison's runtime, reimplemented.
/// <para>
/// Reimplemented rather than generated, and the reason is
/// <c>MYBACKUP</c>/<c>MYREPARSE</c> — 99 sites in <c>parser.yy</c> that read and
/// rewrite the parser's own lookahead. A third-party generator's output does not
/// expose it, which is why option (b) was rejected in decision O7.
/// </para>
/// <para>
/// The algorithm is Bison's <c>yyparse</c>, including its error recovery: on a syntax
/// error, pop states until one can shift the <c>error</c> token, shift it, then discard
/// input until something can follow. The three-token quiet period
/// (<see cref="ErrorStatus"/>) is what stops one mistake reporting as a cascade.
/// </para>
/// <para>
/// It also reproduces <c>yyparse</c>'s LAZY LOOKAHEAD: a state whose only action is
/// its default reduction reduces WITHOUT reading a token first. That is behaviour,
/// not optimization — rule actions switch the lexer's mode, and the grammar's
/// mode-switch heads rely on the next token being lexed AFTER they have run.
/// </para>
/// </summary>
public sealed class LalrParser
{
    /// <summary>How many good shifts suppress further error messages after an error.</summary>
    private const int ErrorRecoveryShifts = 3;

    private readonly ParseTables _tables;
    private readonly IReadOnlyDictionary<int, RuleAction> _actions;

    private readonly List<int> _stateStack = new List<int>();
    private readonly List<object> _valueStack = new List<object>();
    private readonly List<SourceSpan> _locationStack = new List<SourceSpan>();

    private IParserInput _input;
    private ParseContext _context;

    private bool _hasLookahead;
    private ParserToken _lookahead;
    private int _errorStatus;

    /// <summary>Initializes a parser over a set of tables and rule actions.</summary>
    /// <param name="tables">The LALR tables.</param>
    /// <param name="actions">The rule actions, keyed by rule number.</param>
    public LalrParser(ParseTables tables, IReadOnlyDictionary<int, RuleAction> actions)
    {
        _tables = tables ?? throw new ArgumentNullException(nameof(tables));
        _actions = actions ?? new Dictionary<int, RuleAction>();
    }

    /// <summary>Gets how many syntax errors were reported.</summary>
    public int ErrorCount { get; private set; }

    /// <summary>Gets the diagnostics produced, in order.</summary>
    public List<string> Diagnostics { get; } = new List<string>();

    /// <summary>Gets a value indicating whether a lookahead token has been read.</summary>
    public bool HasLookahead => _hasLookahead;

    /// <summary>Gets the lookahead token.</summary>
    public ParserToken Lookahead => _lookahead;

    /// <summary>Gets how many more shifts until error messages resume.</summary>
    public int ErrorStatus => _errorStatus;

    /// <summary>Forgets the lookahead, so the next one is read from the input.</summary>
    public void ClearLookahead() => _hasLookahead = false;

    /// <summary>
    /// Reads the semantic value <paramref name="depth"/> slots below the top of the
    /// value stack — the mid-rule access <see cref="ParseContext.StackValue"/> exposes.
    /// </summary>
    /// <param name="depth">How many slots below the top; 0 is the top.</param>
    /// <returns>The semantic value.</returns>
    internal object PeekValue(int depth) => _valueStack[_valueStack.Count - 1 - depth];

    /// <summary>
    /// Overwrites the semantic value <paramref name="depth"/> slots below the top of
    /// the value stack — the mid-rule assignment
    /// <see cref="ParseContext.SetStackValue"/> exposes.
    /// </summary>
    /// <param name="depth">How many slots below the top; 0 is the top.</param>
    /// <param name="value">The value to store.</param>
    internal void PokeValue(int depth, object value) => _valueStack[_valueStack.Count - 1 - depth] = value;

    /// <summary>
    /// Reads the span <paramref name="depth"/> slots below the top of the location
    /// stack — the mid-rule access <see cref="ParseContext.StackLocation"/> exposes.
    /// </summary>
    /// <param name="depth">How many slots below the top; 0 is the top.</param>
    /// <returns>The span.</returns>
    internal SourceSpan PeekLocation(int depth) => _locationStack[_locationStack.Count - 1 - depth];

    /// <summary>Records a syntax error.</summary>
    /// <param name="message">The message.</param>
    /// <param name="location">Where it happened.</param>
    public void ReportError(string message, SourceSpan location)
    {
        ErrorCount++;
        Diagnostics.Add(location + ": " + message);
    }

    /// <summary>
    /// Parses an input to completion.
    /// </summary>
    /// <param name="input">The token source.</param>
    /// <param name="userState">The caller's state, reachable from every action.</param>
    /// <returns>The start symbol's semantic value.</returns>
    public object Parse(IParserInput input, object userState = null)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _context = new ParseContext(this, input) { UserState = userState };

        _stateStack.Clear();
        _valueStack.Clear();
        _locationStack.Clear();
        _hasLookahead = false;
        _errorStatus = 0;

        _stateStack.Add(0);
        _valueStack.Add(null);
        _locationStack.Add(default);

        while (true)
        {
            int state = _stateStack[_stateStack.Count - 1];
            LalrState current = _tables.States[state];

            // Bison first tries to decide WITHOUT the lookahead (yybackup's
            // yypact_value_is_default test): a state whose only action is its default
            // reduction reduces before any token is read. Deferring the read is not an
            // optimization — rule actions can switch the LEXER'S MODE, and reading the
            // next token before such a reduction lexes it in the old mode. parser.yy
            // leans on the deferral ("symbol_list_part ... can be parsed without
            // lookahead"), and the mode-switch heads of the grammar are where an eager
            // read produced observably wrong tokens.
            if (!_hasLookahead && current.Actions.Count == 0 && current.DefaultReduction >= 0)
            {
                Reduce(current.DefaultReduction);
                continue;
            }

            if (!_hasLookahead)
            {
                _lookahead = _input.Next();
                _hasLookahead = true;
            }

            ParseAction action = Action(state, _lookahead.Symbol);

            switch (action.Kind)
            {
                case ActionKind.Shift:
                    Shift(action.Value);
                    continue;

                case ActionKind.Reduce:
                    Reduce(action.Value);
                    continue;

                case ActionKind.Accept:
                    // The value below $accept's own slot is the start symbol's.
                    return _valueStack[_valueStack.Count - 1];

                default:
                    Recover();
                    continue;
            }
        }
    }

    /// <summary>
    /// Returns what to do in a state on a terminal.
    /// <para>
    /// An ABSENT entry falls through to the state's default reduction; an EXPLICIT
    /// error entry does not. That distinction is what <c>%nonassoc</c> buys: it plants
    /// a real error where a default reduce would otherwise have swallowed it.
    /// </para>
    /// </summary>
    private ParseAction Action(int state, int terminal)
    {
        LalrState target = _tables.States[state];

        if (target.Actions.TryGetValue(terminal, out ParseAction action))
        {
            return action;
        }

        return target.DefaultReduction >= 0
            ? new ParseAction(ActionKind.Reduce, target.DefaultReduction)
            : ParseAction.Error;
    }

    private void Shift(int target)
    {
        _stateStack.Add(target);
        _valueStack.Add(_lookahead.Value);
        _locationStack.Add(_lookahead.Location);
        _hasLookahead = false;

        if (_errorStatus > 0)
        {
            _errorStatus--;
        }
    }

    private void Reduce(int ruleNumber)
    {
        TableRule rule = _tables.Rules[ruleNumber];
        int length = rule.Length;

        object[] values = new object[length];
        SourceSpan[] locations = new SourceSpan[length];
        SourceSpan location;

        if (length > 0)
        {
            int first = _valueStack.Count - length;
            for (int i = 0; i < length; i++)
            {
                values[i] = _valueStack[first + i];
                locations[i] = _locationStack[first + i];
            }

            location = SourceSpan.Join(
                _locationStack[first],
                _locationStack[_locationStack.Count - 1]);

            _stateStack.RemoveRange(_stateStack.Count - length, length);
            _valueStack.RemoveRange(_valueStack.Count - length, length);
            _locationStack.RemoveRange(_locationStack.Count - length, length);
        }
        else
        {
            // An empty rule's span is a point: where the next thing starts. Bison uses
            // the end of the previous symbol, which is what makes an error on an empty
            // production point at the right place rather than at the file start.
            SourceSpan previous = _locationStack[_locationStack.Count - 1];
            location = new SourceSpan(
                previous.FileName,
                previous.EndLine,
                previous.EndColumn,
                previous.EndLine,
                previous.EndColumn,
                previous.EndOffset,
                previous.EndOffset);
        }

        object result = _actions.TryGetValue(ruleNumber, out RuleAction ruleAction)
            ? ruleAction(_context, values, locations, location)
            : DefaultAction(values);

        int state = _stateStack[_stateStack.Count - 1];
        if (!_tables.States[state].Transitions.TryGetValue(rule.LeftHandSide, out int target))
        {
            throw new ParseAbortedException(
                "no goto for " + _tables.Symbols[rule.LeftHandSide]
                + " in state " + state.ToString(CultureInfo.InvariantCulture)
                + "; the tables are inconsistent");
        }

        _stateStack.Add(target);
        _valueStack.Add(result);
        _locationStack.Add(location);
    }

    /// <summary>
    /// The value of a rule with no action: <c>$$ = $1</c>, which is Bison's default and
    /// is why so many of this grammar's pass-through rules can carry no action at all.
    /// </summary>
    private static object DefaultAction(object[] values) => values.Length > 0 ? values[0] : null;

    /// <summary>
    /// Bison's error recovery, verbatim in shape.
    /// </summary>
    private void Recover()
    {
        if (_errorStatus == 0)
        {
            ReportError(
                "syntax error, unexpected " + Describe(_lookahead.Symbol),
                _lookahead.Location);
        }

        if (_errorStatus == ErrorRecoveryShifts)
        {
            // We already tried to recover here and the lookahead did not help, so drop
            // it. End of input at this point cannot be dropped, so the parse is over.
            if (_lookahead.Symbol == 0)
            {
                throw new ParseAbortedException("syntax error at end of input");
            }

            _hasLookahead = false;
            return;
        }

        _errorStatus = ErrorRecoveryShifts;

        int errorSymbol = ErrorSymbol();

        while (true)
        {
            int state = _stateStack[_stateStack.Count - 1];

            if (errorSymbol >= 0)
            {
                ParseAction action = Action(state, errorSymbol);
                if (action.Kind == ActionKind.Shift)
                {
                    // Shift the error token itself, standing in for whatever was wrong.
                    _stateStack.Add(action.Value);
                    _valueStack.Add(null);
                    _locationStack.Add(_lookahead.Location);
                    return;
                }
            }

            if (_stateStack.Count <= 1)
            {
                throw new ParseAbortedException("syntax error; no rule could recover");
            }

            _stateStack.RemoveAt(_stateStack.Count - 1);
            _valueStack.RemoveAt(_valueStack.Count - 1);
            _locationStack.RemoveAt(_locationStack.Count - 1);
        }
    }

    private int ErrorSymbol()
    {
        for (int i = 0; i < _tables.TerminalCount; i++)
        {
            if (string.Equals(_tables.Symbols[i], "error", StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private string Describe(int symbol)
        => symbol >= 0 && symbol < _tables.Symbols.Count
            ? _tables.Symbols[symbol]
            : "token " + symbol.ToString(CultureInfo.InvariantCulture);
}
