// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Lexing; //was previously: lily/lexer.ll (the scanner), lily/includable-lexer.cc (the include stack);

/// <summary>
/// The lexer's start conditions, all thirteen of them exclusive.
/// <para>
/// LilyPond's lexer is MODAL: the same characters mean different things depending on
/// what is being read. <c>c</c> is a note in <see cref="Notes"/>, a chord root in
/// <see cref="Chords"/>, a syllable in <see cref="Lyrics"/> and ordinary text in
/// <see cref="Markup"/>. That is why the scanner is hand-ported as a state machine
/// rather than generated: the states are the design, not an implementation detail.
/// </para>
/// <para>
/// Every one is <c>%x</c> — EXCLUSIVE — so a rule not marked for the current state does
/// not apply, and <see cref="Initial"/>'s rules do not leak into the others.
/// </para>
/// </summary>
public enum LexerState
{
    /// <summary>Flex's <c>INITIAL</c>: top level, outside any mode.</summary>
    Initial,

    /// <summary>Inside <c>\chordmode</c>.</summary>
    Chords,

    /// <summary>Inside <c>\figuremode</c>.</summary>
    Figures,

    /// <summary>Reading the file name of an <c>\include</c>.</summary>
    Include,

    /// <summary>Inside <c>\lyricmode</c>.</summary>
    Lyrics,

    /// <summary>Inside a <c>%{ ... %}</c> block comment.</summary>
    LongComment,

    /// <summary>The main input, after the initialisation files.</summary>
    MainInput,

    /// <summary>Inside <c>\markup</c>.</summary>
    Markup,

    /// <summary>Inside <c>\notemode</c> — the default for music.</summary>
    Notes,

    /// <summary>Inside a double-quoted string.</summary>
    Quote,

    /// <summary>Inside a quoted string that began after a command.</summary>
    CommandQuote,

    /// <summary>Reading the line number of a <c>\sourcefileline</c>.</summary>
    SourceFileLine,

    /// <summary>Reading the file name of a <c>\sourcefilename</c>.</summary>
    SourceFileName,

    /// <summary>Reading the string of a <c>\version</c>.</summary>
    Version,
}

/// <summary>What a matched lexer rule does.</summary>
/// <param name="scanner">The scanner, for state pushes and token emission.</param>
/// <param name="text">The matched text.</param>
/// <returns>A token to emit, or null to keep scanning.</returns>
public delegate ParserToken? LexerRuleAction(ModalScanner scanner, string text);

/// <summary>One rule of the scanner: a pattern, the states it applies in, and an action.</summary>
public sealed class LexerRule
{
    /// <summary>Initializes a rule.</summary>
    /// <param name="pattern">The pattern, as a .NET regular expression.</param>
    /// <param name="states">The states it applies in, or null for all of them.</param>
    /// <param name="action">What to do on a match.</param>
    public LexerRule(string pattern, LexerState[] states, LexerRuleAction action)
    {
        Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
        States = states;
        Action = action ?? throw new ArgumentNullException(nameof(action));

        // \G anchors the match at the scan position, which is what gives flex's
        // "match here, longest wins" rather than "search forwards".
        Regex = new Regex(@"\G(?:" + pattern + ")", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    }

    /// <summary>Gets the pattern as written.</summary>
    public string Pattern { get; }

    /// <summary>Gets the states this rule applies in, or null for flex's <c>&lt;*&gt;</c>.</summary>
    public LexerState[] States { get; }

    /// <summary>Gets what to do on a match.</summary>
    public LexerRuleAction Action { get; }

    /// <summary>Gets the compiled pattern.</summary>
    internal Regex Regex { get; }

    /// <summary>Determines whether this rule applies in a state.</summary>
    /// <param name="state">The state.</param>
    /// <returns><see langword="true"/> when it applies.</returns>
    public bool AppliesIn(LexerState state)
    {
        if (States == null)
        {
            return true;
        }

        foreach (LexerState candidate in States)
        {
            if (candidate == state)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The pattern.</returns>
    public override string ToString() => Pattern;
}

/// <summary>
/// The scanner engine: flex's runtime, reimplemented for a hand-ported rule set.
/// <para>
/// Three things it has to get right, all of them observable:
/// </para>
/// <list type="number">
/// <item>LONGEST MATCH WINS, and on a tie the rule written EARLIER in
/// <c>lexer.ll</c> wins. Every flex scanner depends on this, and LilyPond's leans on it
/// hard — <c>\repeat</c> beats <c>\re</c> only because it matches more.</item>
/// <item>The START CONDITION STACK. <c>lexer.ll</c> declares <c>%option stack</c> and
/// uses <c>yy_push_state</c>/<c>yy_pop_state</c> throughout, so modes NEST: a
/// <c>\markup</c> inside <c>\lyricmode</c> returns to lyrics, not to the top.</item>
/// <item>The EXTRA-TOKEN QUEUE. The grammar pushes tokens back into the lexer from
/// inside rule actions — <c>MYBACKUP</c> and <c>MYREPARSE</c>, 99 sites — so the
/// scanner must hand those out before reading any more input.</item>
/// </list>
/// </summary>
public sealed class ModalScanner : IParserInput, CodeBrix.LilyPort.Engine.Origins.ILilyLexer
{
    private readonly List<LexerRule> _rules;
    private readonly Stack<LexerState> _stateStack = new Stack<LexerState>();
    private readonly Stack<ParserToken> _extraTokens = new Stack<ParserToken>();
    private readonly int _endOfInputSymbol;

    // The INPUT STACK. Upstream this is Includable_lexer, which \include pushes onto
    // and end-of-file pops: one lexer, one mode stack, many input buffers. Keeping the
    // mode stack shared is not an implementation convenience — an included file
    // inherits the mode it was included FROM, which is what lets ly/ init files be
    // \include'd from inside \notemode.
    private readonly Stack<IncludedInput> _includes = new Stack<IncludedInput>();

    private string _input;
    private string _fileName;

    private readonly Dictionary<string, int> _terminals = new Dictionary<string, int>(StringComparer.Ordinal);
    private System.Text.StringBuilder _stringBuilder;

    private int _position;
    private int _line = 1;
    private int _column;

    /// <summary>One suspended input on the include stack.</summary>
    private struct IncludedInput
    {
        internal string Input;
        internal string FileName;
        internal int Position;
        internal int Line;
        internal int Column;
    }

    /// <summary>Initializes a scanner over an input.</summary>
    /// <param name="rules">The rules, in the order <c>lexer.ll</c> writes them.</param>
    /// <param name="input">The text to scan.</param>
    /// <param name="fileName">The file name, for locations.</param>
    /// <param name="endOfInputSymbol">The symbol number to emit at the end.</param>
    public ModalScanner(
        IReadOnlyList<LexerRule> rules,
        string input,
        string fileName = "<input>",
        int endOfInputSymbol = 0)
    {
        _rules = new List<LexerRule>(rules ?? throw new ArgumentNullException(nameof(rules)));
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _fileName = fileName;
        _endOfInputSymbol = endOfInputSymbol;
        State = LexerState.Initial;
    }

    /// <summary>Gets the current start condition.</summary>
    public LexerState State { get; private set; }

    /// <summary>Gets how deep the start-condition stack is.</summary>
    public int StateDepth => _stateStack.Count;

    /// <summary>Gets a value indicating whether the whole input has been read.</summary>
    public bool AtEnd => _position >= _input.Length;

    /// <summary>Gets the location the current token started at.</summary>
    public SourceSpan CurrentLocation { get; private set; }

    /// <summary>Gets the diagnostics the scanner produced.</summary>
    public List<string> Diagnostics { get; } = new List<string>();

    /// <summary>
    /// Gets or sets the most recent <c>\version</c> string read.
    /// <para>
    /// Upstream hands it straight to <c>Lily::parse_and_check_version</c>. The port
    /// records it and leaves the checking to the caller, so that the scanner does not
    /// have to reach into the Scheme layer.
    /// </para>
    /// </summary>
    public string LastVersionString { get; set; }

    /// <summary>
    /// The <c>\version</c> string of the MAIN input, ignoring any an included file
    /// declares.
    /// <para>
    /// Upstream records <c>version-seen</c> only when
    /// <c>is_main_input_ &amp;&amp; include_stack_.size () == main_input_level_</c>
    /// (<c>lexer.ll:255</c>) — an included file's version says nothing about whether the
    /// file the user asked for declared one. This is the value the run's version check
    /// is answered from; <see cref="LastVersionString"/> is whichever came last from
    /// anywhere and is NOT that question's answer.
    /// </para>
    /// </summary>
    public string MainInputVersionString { get; set; }

    /// <summary>
    /// Switches start condition without stacking, which is flex's <c>BEGIN</c>.
    /// </summary>
    /// <param name="state">The state to switch to.</param>
    public void Begin(LexerState state) => State = state;

    /// <summary>Pushes the current start condition and switches, which is <c>yy_push_state</c>.</summary>
    /// <param name="state">The state to switch to.</param>
    public void PushState(LexerState state)
    {
        _stateStack.Push(State);
        State = state;
    }

    /// <summary>Returns to the previous start condition, which is <c>yy_pop_state</c>.</summary>
    public void PopState()
    {
        if (_stateStack.Count == 0)
        {
            Warn("popped the lexer state stack when it was empty");
            State = LexerState.Initial;
            return;
        }

        State = _stateStack.Pop();
    }

    /// <summary>Returns the state below the current one, which is <c>yy_top_state</c>.</summary>
    /// <returns>The state, or the current one when the stack is empty.</returns>
    public LexerState TopState() => _stateStack.Count > 0 ? _stateStack.Peek() : State;

    /// <summary>Gets or sets a value indicating whether the main input has been reached.</summary>
    public bool IsMainInput { get; set; }

    /// <summary>Gets or sets the file an include asked for, for the caller to open.</summary>
    public string RequestedInclude { get; set; }

    /// <summary>
    /// Gets or sets how an <c>\include</c> is resolved to source text — the caller's
    /// file system, or the vendored <c>ly/</c> resources.
    /// <para>
    /// Upstream: <c>Includable_lexer::new_input (name, sources)</c>, where
    /// <c>Sources</c> owns the search path. With no resolver set an <c>\include</c>
    /// is a lexer error rather than a silent skip, because a skipped include produces
    /// a file that parses and means something else.
    /// </para>
    /// </summary>
    /// <returns>The included file's text, or <see langword="null"/> when it cannot be
    /// found.</returns>
    public Func<string, string> IncludeResolver { get; set; }

    /// <summary>Gets the file currently being scanned, which an include changes.</summary>
    public string CurrentFileName => _fileName;

    /// <summary>Gets how deep the include stack is.</summary>
    public int IncludeDepth => _includes.Count;

    /// <summary>
    /// Switches the scanner to an included file, remembering where to resume.
    /// <para>Upstream: <c>Includable_lexer::new_input</c>.</para>
    /// </summary>
    /// <param name="name">The file's name, as written in the <c>\include</c>.</param>
    /// <returns><see langword="true"/> when the file was found and opened.</returns>
    public bool BeginInclude(string name)
    {
        string text = IncludeResolver?.Invoke(name);
        if (text == null)
        {
            Error("cannot find file: `" + name + "'");
            return false;
        }

        BeginIncludeText(name, text);
        return true;
    }

    /// <summary>
    /// Switches the scanner to text already in hand, remembering where to resume.
    /// <para>Upstream: the <c>Includable_lexer::new_input</c> overload taking a string,
    /// which <c>ly:parser-include-string</c> reaches — the caller has the text, so there
    /// is nothing to resolve.</para>
    /// </summary>
    /// <param name="name">The name locations in the text should carry.</param>
    /// <param name="text">The text.</param>
    public void BeginIncludeText(string name, string text)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        _includes.Push(new IncludedInput
        {
            Input = _input,
            FileName = _fileName,
            Position = _position,
            Line = _line,
            Column = _column,
        });

        _input = text;
        _fileName = name;
        _position = 0;
        _line = 1;
        _column = 0;
    }

    /// <summary>Gets or sets the name a sourcefilename renamed the input to.</summary>
    public string SourceFileName { get; set; }

    /// <summary>
    /// Registers the terminal names the rules ask for by name, so a rule can say
    /// <c>Terminal("UNSIGNED")</c> rather than carrying a number the grammar owns.
    /// </summary>
    /// <param name="symbols">Every symbol, indexed by symbol number.</param>
    /// <param name="terminalCount">How many of them are terminals.</param>
    public void UseSymbols(IReadOnlyList<string> symbols, int terminalCount)
    {
        _terminals.Clear();
        for (int i = 0; i < terminalCount && i < symbols.Count; i++)
        {
            _terminals[symbols[i]] = i;
        }
    }

    /// <summary>Returns a terminal's symbol number by name.</summary>
    /// <param name="name">The terminal's name in the grammar.</param>
    /// <returns>The number, or -1 when the scanner has not been given the symbols.</returns>
    public int Terminal(string name)
        => _terminals.TryGetValue(name, out int number) ? number : -1;

    /// <summary>
    /// Returns a token for a character that is its own terminal, such as a brace.
    /// <para>The terminal's NAME is the character literal exactly as the grammar writes
    /// it, because that is the text the grammar reader kept — so the quote and the
    /// backslash have to be escaped here the way Bison escapes them. Getting that wrong
    /// is silent: <c>Terminal</c> answers -1 for a name it does not know, the scanner
    /// delivers token -1, and the driver reports "unexpected token -1" at a position that
    /// looks like an ordinary syntax error. The octave mark <c>'</c> was 25 of the init
    /// layer's errors for exactly this reason — <c>&lt;e, a, d g b e'&gt;</c> in
    /// <c>string-tunings-init.ly</c>, where the grammar's terminal is <c>'\''</c> and the
    /// port asked for <c>'''</c>.</para>
    /// </summary>
    /// <param name="character">The character.</param>
    /// <returns>The token.</returns>
    public ParserToken CharacterToken(char character)
        => Token(Terminal(CharacterTerminalName(character)), character);

    /// <summary>Names a character terminal the way the grammar's text spells it.</summary>
    /// <param name="character">The character.</param>
    /// <returns>The terminal name, such as <c>'{'</c> or <c>'\''</c>.</returns>
    internal static string CharacterTerminalName(char character)
        => character == '\'' || character == '\\'
            ? "'\\" + character + "'"
            : "'" + character + "'";

    /// <summary>Opens a quoted string, which is a start condition of its own.</summary>
    public void StartQuote()
    {
        _stringBuilder = new System.Text.StringBuilder();
        PushState(LexerState.Quote);
    }

    /// <summary>Opens a quoted string that stands for a command name.</summary>
    public void StartCommandQuote()
    {
        _stringBuilder = new System.Text.StringBuilder();
        PushState(LexerState.CommandQuote);
    }

    /// <summary>Appends to the string being read.</summary>
    /// <param name="text">The text to append.</param>
    public void AppendToString(string text)
        => (_stringBuilder ??= new System.Text.StringBuilder()).Append(text);

    /// <summary>Returns and clears the string being read.</summary>
    /// <returns>The string.</returns>
    public string FinishString()
    {
        string text = _stringBuilder?.ToString() ?? string.Empty;
        _stringBuilder = null;
        return text;
    }

    /// <summary>Reads one embedded Scheme expression at the scan position and steps past it.</summary>
    /// <param name="host">The reader to use.</param>
    /// <returns>The value read, or <see cref="DefaultArgument"/> when it could not be read.</returns>
    public object ReadEmbeddedScheme(ILexerHost host)
    {
        // The scan position is already past the `#' or `$', which is upstream's
        // `Input hi = here_input (); hi.step_forward ();' — the offset of the '(' in
        // "... #(bla)", and the key the closures alist is built on.
        object value = host.ParseEmbeddedScheme(_input, _position, PointLocation(), out int consumed);
        Advance(consumed);
        return value;
    }

    /// <summary>
    /// Reads and EVALUATES one immediate-Scheme (<c>$</c>) expression, and delivers the
    /// token its value calls for — lexer.ll 424.
    /// <para>
    /// Two branches, in upstream's order. In markup mode a procedure carrying a markup
    /// signature becomes a <c>MARKUP_FUNCTION</c> (or <c>MARKUP_LIST_FUNCTION</c>) with
    /// its predicates announced, so <c>$my-markup-command</c> can be written wherever
    /// <c>\my-markup-command</c> could. Otherwise the value goes through
    /// <c>scan_scm_id</c>, which is what makes <c>$</c> type-directed rather than a
    /// second spelling of <c>#</c>.
    /// </para>
    /// <para>
    /// A value of <see cref="Unspecified"/> produces NO TOKEN: upstream's rule ends in
    /// <c>if (!scm_is_eq (yylval, SCM_UNSPECIFIED)) return token;</c> and otherwise falls
    /// off the end of the action, which in flex means "having consumed the text, carry on
    /// scanning". That is the path a failed evaluation takes — the error is already
    /// reported, and inventing a token for it would produce a second, spurious syntax
    /// error.
    /// </para>
    /// </summary>
    /// <param name="host">The host that reads, evaluates and classifies.</param>
    /// <returns>The token, or <see langword="null"/> to keep scanning.</returns>
    public ParserToken? ReadImmediateScheme(ILexerHost host)
    {
        SourceSpan start = PointLocation();
        object datum = ReadEmbeddedScheme(host);
        object value = host.EvalScheme(datum, start, '$');

        if (State == LexerState.Markup)
        {
            LexerLookup markup = host.MarkupFunctionToken(value, out IReadOnlyList<MarkupPredicate> predicates);
            if (markup.Found)
            {
                PushMarkupPredicates(predicates);
                return Token(Terminal(markup.TokenName), markup.Value);
            }
        }

        if (value is Unspecified)
        {
            return null;
        }

        LexerLookup found = host.ScanSchemeValue(value);
        if (!found.Found)
        {
            return null;
        }

        if (found.FunctionSignature != null)
        {
            PushFunctionSignature(found.FunctionSignature);
        }

        return Token(Terminal(found.TokenName), found.Value);
    }

    /// <summary>Looks a bare word up in the current mode's tables, falling back to a symbol.</summary>
    /// <param name="host">The tables.</param>
    /// <param name="word">The word.</param>
    /// <returns>The token.</returns>
    public ParserToken ScanBareWord(ILexerHost host, string word)
    {
        LexerLookup found = host.ScanWord(State, word);
        return found.Found
            ? Token(Terminal(found.TokenName), found.Value)
            : Token(Terminal("SYMBOL"), SchemeText(word));
    }

    /// <summary>
    /// Looks a backslash-word up: a reserved word first, then a defined identifier.
    /// <para>
    /// An unknown one is an ERROR that still produces a token — upstream returns STRING
    /// rather than SYMBOL, deliberately, because "SYMBOL would cause additional
    /// processing" — so the parse continues and every error in the file is reported.
    /// </para>
    /// </summary>
    /// <param name="host">The tables.</param>
    /// <param name="word">The word, without its backslash.</param>
    /// <returns>The token.</returns>
    public ParserToken ScanEscapedWord(ILexerHost host, string word)
    {
        LexerLookup keyword = host.LookupKeyword(word);
        if (keyword.Found)
        {
            return Token(Terminal(keyword.TokenName), keyword.Value);
        }

        LexerLookup identifier = host.LookupIdentifier(word);
        if (identifier.Found)
        {
            // Upstream this branch is scan_scm_id: a music, event or Scheme function
            // announces its signature before its own token is delivered.
            if (identifier.FunctionSignature != null)
            {
                PushFunctionSignature(identifier.FunctionSignature);
            }

            return Token(Terminal(identifier.TokenName), identifier.Value);
        }

        Error("unknown command: `\\" + word + "'");
        return Token(Terminal("STRING"), SchemeText(word));
    }

    /// <summary>Looks a shorthand up as an identifier.</summary>
    /// <param name="host">The tables.</param>
    /// <param name="text">The shorthand as written.</param>
    /// <returns>The token.</returns>
    public ParserToken ScanShorthand(ILexerHost host, string text)
    {
        LexerLookup found = host.LookupIdentifier(text);
        if (found.Found)
        {
            // scan_shorthand routes through scan_scm_id upstream, exactly like the
            // escaped words, so a shorthand bound to a function announces its
            // signature the same way.
            if (found.FunctionSignature != null)
            {
                PushFunctionSignature(found.FunctionSignature);
            }

            return Token(Terminal(found.TokenName), found.Value);
        }

        Error("undefined character or shorthand: " + text);
        return Token(Terminal("STRING"), SchemeText(text));
    }

    /// <summary>
    /// Presents matched text as the SCHEME STRING a token carries.
    /// <para>
    /// Upstream every such value is <c>yylval = to_scm (str)</c> — a real Guile string,
    /// and <c>markup?</c>, <c>string?</c> and every predicate built on them answer on it.
    /// An earlier pass carried the CLR string the rule matched, on the recorded reasoning
    /// that "MutableString is accepted wherever a value is TESTED for stringness". That
    /// holds inside the port and NOT ONE STEP FURTHER: the Scheme layer's <c>string?</c>
    /// is <c>value is MutableString</c>, so `\markup { \italic "cresc." }` handed
    /// <c>composed-markup-list</c> a list whose element was not a markup, and the init
    /// layer died inside a markup constructor rather than anywhere near the lexer.
    /// </para>
    /// </summary>
    /// <param name="text">The text.</param>
    /// <returns>The Scheme string.</returns>
    public static MutableString SchemeText(string text) => new MutableString(text ?? string.Empty);

    /// <summary>
    /// Pushes the <c>EXPECT_*</c> tokens a markup command's signature calls for.
    /// <para>
    /// In REVERSE order, so the first pushed is the last read: a signature of
    /// <c>(number? number? markup?)</c> has to produce
    /// <c>EXPECT_MARKUP EXPECT_SCM EXPECT_SCM EXPECT_NO_MORE_ARGS</c>, and the pushback
    /// queue hands out the most recently pushed first.
    /// </para>
    /// </summary>
    /// <param name="predicates">The command's argument predicates, in signature order.</param>
    public void PushMarkupPredicates(IReadOnlyList<MarkupPredicate> predicates)
    {
        PushExtraToken(Token(Terminal("EXPECT_NO_MORE_ARGS")));

        foreach (MarkupPredicate predicate in predicates)
        {
            // EXPECT_MARKUP and EXPECT_MARKUP_LIST carry nothing — the grammar knows what
            // they mean. EXPECT_SCM carries THE PREDICATE, which the arglist rules call.
            switch (predicate.Name)
            {
                case "markup-list?":
                    PushExtraToken(Token(Terminal("EXPECT_MARKUP_LIST")));
                    break;
                case "markup?":
                    PushExtraToken(Token(Terminal("EXPECT_MARKUP")));
                    break;
                default:
                    PushExtraToken(Token(Terminal("EXPECT_SCM"), predicate.Value));
                    break;
            }
        }
    }

    /// <summary>
    /// Pushes the <c>EXPECT_*</c> tokens a music, event or Scheme function's signature
    /// calls for — <c>Lily_lexer::scan_scm_id</c>'s announcement loop.
    /// <para>
    /// The signature's head is the RETURN predicate, which decides the function's own
    /// token and is skipped here; each tail entry is an argument's predicate, or a
    /// <c>(predicate . default)</c> pair for an optional argument. Pushed in reverse
    /// delivery order: <c>EXPECT_NO_MORE_ARGS</c> first (so it is read LAST), then per
    /// argument the <c>EXPECT_SCM</c> carrying the predicate and, for an optional one,
    /// the <c>EXPECT_OPTIONAL</c> carrying the default — pushed after the
    /// <c>EXPECT_SCM</c> so it is delivered before it, which is the
    /// <c>EXPECT_OPTIONAL EXPECT_SCM</c> order the grammar's arglist rules expect.
    /// Upstream guards each entry with <c>ly_is_procedure</c> and programming-errors
    /// otherwise; the port trusts the host's signature, since only the host knows what
    /// counts as a procedure.
    /// </para>
    /// </summary>
    /// <param name="signature">The signature list, head the return predicate. An
    /// optional argument whose recorded default is
    /// <see cref="DefaultArgument"/>.Instance gets no <c>EXPECT_OPTIONAL</c>, exactly
    /// as upstream skips <c>SCM_UNDEFINED</c> defaults.</param>
    public void PushFunctionSignature(object signature)
    {
        PushExtraToken(Token(Terminal("EXPECT_NO_MORE_ARGS")));

        for (object s = ((Pair)signature).Cdr; s is Pair pair; s = pair.Cdr)
        {
            object optional = DefaultArgument.Instance;
            object predicate = pair.Car;

            if (predicate is Pair entry)
            {
                optional = entry.Cdr;
                predicate = entry.Car;
            }

            PushExtraToken(Token(Terminal("EXPECT_SCM"), predicate));

            if (!(optional is DefaultArgument))
            {
                PushExtraToken(Token(Terminal("EXPECT_OPTIONAL"), optional));
            }
        }
    }

    /// <summary>Records an error at the current location.</summary>
    /// <param name="message">The message.</param>
    public void Error(string message) => Warn(message);

    /// <summary>Records a diagnostic at the current location.</summary>
    /// <param name="message">The message.</param>
    public void Warn(string message)
        => Diagnostics.Add(
            _fileName + ":" + _line.ToString(CultureInfo.InvariantCulture)
            + ":" + _column.ToString(CultureInfo.InvariantCulture) + ": " + message);

    /// <summary>Builds a token at the location the current match started.</summary>
    /// <param name="symbol">The terminal's symbol number.</param>
    /// <param name="value">The semantic value.</param>
    /// <returns>The token.</returns>
    public ParserToken Token(int symbol, object value = null)
        => new ParserToken(symbol, value, CurrentLocation);

    /// <summary>Puts a token in front of everything not yet read.</summary>
    /// <param name="token">The token.</param>
    public void PushExtraToken(ParserToken token) => _extraTokens.Push(token);

    /// <summary>
    /// Returns the next token: from the pushback queue first, then by scanning.
    /// </summary>
    /// <returns>The token, or end-of-input at the end.</returns>
    public ParserToken Next()
    {
        while (true)
        {
            if (_extraTokens.Count > 0)
            {
                return _extraTokens.Pop();
            }

            // An include that has been asked for is opened HERE rather than inside the
            // rule, so the rule stays a translation of lexer.ll's action and the input
            // switch happens between tokens.
            if (RequestedInclude != null)
            {
                // The rule has already popped the Include state; all that is left is
                // to switch input.
                string requested = RequestedInclude;
                RequestedInclude = null;
                BeginInclude(requested);
                continue;
            }

            if (AtEnd)
            {
                // End of an included file: resume the includer where it left off.
                // Upstream does this in Includable_lexer::close_input.
                if (_includes.Count > 0)
                {
                    IncludedInput resumed = _includes.Pop();
                    _input = resumed.Input;
                    _fileName = resumed.FileName;
                    _position = resumed.Position;
                    _line = resumed.Line;
                    _column = resumed.Column;
                    continue;
                }

                return new ParserToken(_endOfInputSymbol, null, PointLocation());
            }

            LexerRule matched = null;
            int length = 0;

            // Longest match wins; on a tie the earlier rule wins, which is why this
            // keeps a strict > rather than >=.
            foreach (LexerRule rule in _rules)
            {
                if (!rule.AppliesIn(State))
                {
                    continue;
                }

                Match match = rule.Regex.Match(_input, _position);
                if (match.Success && match.Length > length)
                {
                    matched = rule;
                    length = match.Length;
                }
            }

            if (matched == null || length == 0)
            {
                // flex's %option nodefault: an unmatched character is an error rather
                // than being echoed, which is what stops a typo scanning as text.
                Warn("no lexer rule matches '" + _input[_position] + "' in state " + State);
                Advance(1);
                continue;
            }

            string text = _input.Substring(_position, length);
            CurrentLocation = SpanOf(text);
            Advance(length);

            ParserToken? token = matched.Action(this, text);
            if (token.HasValue)
            {
                return token.Value;
            }
        }
    }

    private SourceSpan SpanOf(string text)
    {
        int endLine = _line;
        int endColumn = _column;

        foreach (char c in text)
        {
            if (c == '\n')
            {
                endLine++;
                endColumn = 0;
            }
            else
            {
                endColumn++;
            }
        }

        return new SourceSpan(
            _fileName,
            _line,
            _column + 1,
            endLine,
            endColumn + 1,
            _position,
            _position + text.Length);
    }

    private SourceSpan PointLocation()
        => new SourceSpan(
            _fileName, _line, _column + 1, _line, _column + 1, _position, _position);

    private void Advance(int count)
    {
        for (int i = 0; i < count && _position < _input.Length; i++)
        {
            if (_input[_position] == '\n')
            {
                _line++;
                _column = 0;
            }
            else
            {
                _column++;
            }

            _position++;
        }
    }
}
