/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
                 Jan Nieuwenhuizen <janneke@gnu.org>

  LilyPond is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  LilyPond is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyPort.Parsing.Actions;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyPort.Parsing.Lexing;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Session; //was previously: lily/lily-parser.cc, lily/lily-lexer.cc, lily/lexer.ll (the host halves);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.
// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - SnapshotToplevelScope/RestoreToplevelScope give a shared batch session
//     upstream's one-parser-per-file semantics for the base scope: bindings a file
//     invents are removed and bindings it overwrites are reverted between files.
//     The ssaattbb templates were the measured victim (the NINTH per-file leak).

/// <summary>
/// THE REAL HOST — what actually plays <c>Lily_parser</c> and <c>Lily_lexer</c> for a
/// parse, over a live LilyScheme interpreter and the Engine's own types.
/// <para>
/// Every rule action and every lexer rule reaches the outside world through
/// <see cref="IParserHost"/> and <see cref="ILexerHost"/>. Until now the only
/// implementation was the tests' <c>ScriptedParserHost</c>, which is honest about
/// being scripted; this is the one that answers from the Scheme layer, so a <c>.ly</c>
/// FILE can be parsed rather than a token list. It is the first thing Phase 3 asked
/// for, because a regression file cannot demand anything until something can read it.
/// </para>
/// <para>
/// Upstream splits this across <c>Lily_parser</c> (identifiers, the syntax
/// constructors, the output-definition stack) and <c>Lily_lexer</c> (scopes, the mode
/// stack, the keyword and pitch-name tables). The port keeps them together because
/// the seam the actions were written against does not distinguish them — every member
/// here names its upstream counterpart.
/// </para>
/// </summary>
public sealed partial class LilyParserSession : IParserHost, ILexerHost
{
    private readonly Interpreter _interpreter;
    private readonly SchemeModule _lilyModule;

    // Lily_lexer::scopes_ — the module stack identifiers resolve through, innermost
    // last. Upstream conses onto the front; the port appends, so index order reads
    // the way the C++ list is walked.
    private readonly List<SchemeModule> _scopes = new List<SchemeModule>();

    // Lily_lexer::pitchname_tab_stack_ — pushed by note/chord/drum mode, popped when
    // leaving one, and what scan_word consults. It is a STACK because \drummode
    // inside \notemode must restore the note names on the way out.
    private readonly List<object> _pitchNameTables = new List<object>();

    // The lexer states the pitch-name stack is keyed to, so pop_state knows whether
    // this pop is leaving a mode that pushed one.
    private readonly List<LexerState> _pitchNameStates = new List<LexerState>();

    private object _chordModifiers = Nil.Instance;

    /// <summary>Initializes a session over an interpreter whose Scheme layer is loaded.</summary>
    /// <param name="interpreter">The interpreter, already through
    /// <see cref="LilyPondScheme.LoadViaLilyScm"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown when the interpreter is null.</exception>
    public LilyParserSession(Interpreter interpreter)
    {
        _interpreter = interpreter ?? throw new ArgumentNullException(nameof(interpreter));
        _lilyModule = interpreter.Modules.Resolve(Pair.List(Symbol.Intern("lily")));

        // Lily_lexer's constructor opens with `add_scope (ly_make_module ())` — a
        // FRESH module that USES (lily), not (lily) itself. The difference is not
        // cosmetic: `#(define output-def-music-handler ...)` in declarations-init.ly
        // has to land somewhere lookup_identifier_symbol can see it, and that lookup
        // is module-LOCAL per scope. Opening on (lily) instead puts every such define
        // where the local lookup does not look, and the handler reads as unbound at
        // the first \layout — which is exactly what the first run of the init layer
        // did.
        AddScope(MakeModule());
    }

    /// <summary>
    /// Initializes a session sharing another's scopes — <c>ly:parser-clone</c>.
    /// <para>Upstream's copy constructor builds a new <c>Lily_lexer</c> FROM the original,
    /// which copies the scope list rather than starting a fresh one, so a cloned parser
    /// sees every identifier the original had defined.</para>
    /// </summary>
    /// <param name="interpreter">The interpreter.</param>
    /// <param name="source">The session to clone.</param>
    private LilyParserSession(Interpreter interpreter, LilyParserSession source)
    {
        _interpreter = interpreter;
        _lilyModule = source._lilyModule;
        _scopes.AddRange(source._scopes);
        _pitchNameTables.AddRange(source._pitchNameTables);
        _pitchNameStates.AddRange(source._pitchNameStates);
        _chordModifiers = source._chordModifiers;
        IncludePath.AddRange(source.IncludePath);
        foreach (KeyValuePair<string, SourceFile> entry in source._sourceFiles)
        {
            _sourceFiles[entry.Key] = entry.Value;
            Sources.Add(entry.Value);
        }
    }

    /// <summary>Gets the interpreter this session runs on.</summary>
    public Interpreter Interpreter => _interpreter;

    /// <summary>Gets the <c>(lily)</c> module — where the Scheme layer's own bindings live.</summary>
    public SchemeModule LilyModule => _lilyModule;

    /// <summary>Gets or sets the scanner the mode operations drive.</summary>
    /// <remarks>
    /// Set for the duration of a parse. The lexer's mode stack is real state, and a
    /// rule action that pushes a mode is telling THIS scanner to change what it reads
    /// next — a session with no scanner attached records nothing and silently lexes
    /// the whole file in one mode, which is the failure the PitchesAndDurations group recorded.
    /// </remarks>
    public ModalScanner Scanner { get; set; }

    /// <summary>Gets the diagnostics this session produced, in order.</summary>
    public List<string> Diagnostics { get; } = new List<string>();

    /// <inheritdoc/>
    public int ErrorLevel { get; set; }

    /// <inheritdoc/>
    public Duration DefaultDuration { get; set; } = new Duration(2, 0);

    /// <inheritdoc/>
    public int DefaultTremoloType { get; set; } = 8;

    // ------ identifiers and scopes (Lily_lexer) ------

    /// <inheritdoc/>
    public object LookupIdentifier(string name)
    {
        // Upstream: Lily_lexer::lookup_identifier_symbol, which walks the scope stack
        // innermost-first and consults each scope with scm_module_variable — the module
        // AND WHAT IT IMPORTS. Searching only each module's own bindings, as an earlier
        // pass did, hides everything (lily) defines, so `\hspace` and every other
        // identifier that lives in the Scheme layer rather than in a .ly assignment read
        // as undefined. Since add_scope makes each new scope use the ones already open,
        // the innermost scope alone usually answers; the loop is upstream's and is kept.
        Symbol symbol = Symbol.Intern(name);
        for (int i = _scopes.Count - 1; i >= 0; i--)
        {
            Variable variable = _scopes[i].Lookup(symbol);

            // An UNBOUND variable is not an answer: psyntax reserves a slot before a
            // definition runs, and reading one would hand back nothing at all.
            if (variable != null && variable.IsBound)
            {
                return variable.GetValue();
            }
        }

        return DefaultArgument.Instance;
    }

    /// <inheritdoc/>
    public void SetIdentifier(object key, object value)
    {
        // Upstream: Lily_lexer::set_identifier. A string key becomes a symbol; a pair
        // is (symbol . property-path), and the value is folded INTO whatever the
        // symbol already holds rather than replacing it — which is what makes
        // `foo.bar = 3` extend foo instead of overwriting it.
        object path = Nil.Instance;
        object symbolPart = key;

        if (key is Pair pair)
        {
            symbolPart = pair.Car;
            path = pair.Cdr;
        }
        else if (ParserActionHelpers.IsSchemeString(key))
        {
            symbolPart = Symbol.Intern(ParserActionHelpers.SchemeStringText(key));
        }

        if (!(symbolPart is Symbol symbol))
        {
            return;
        }

        if (LilyKeywords.Lookup(symbol.Name) != null)
        {
            Warning(default, "identifier name is a keyword: `" + symbol.Name + "'");
        }

        SchemeModule target = _scopes[_scopes.Count - 1];

        if (path is Pair)
        {
            Variable previous = target.LookupLocal(symbol);
            value = previous != null
                ? NestedProperty.NestedPropertyAlist(previous.GetValue(), path, value)
                : NestedProperty.NestedCreateAlist(path, value);
        }

        target.Define(symbol, value);
    }

    /// <inheritdoc/>
    public void AddScope(object module)
    {
        // Lily_lexer::add_scope: the incoming module USES every scope already open, so
        // a \with block can see what the \layout around it declared, and only then
        // becomes the innermost scope.
        SchemeModule opened = (SchemeModule)module;
        foreach (SchemeModule scope in _scopes)
        {
            opened.AddUse(scope);
        }

        _scopes.Add(opened);
        SetCurrentScope();
    }

    /// <inheritdoc/>
    public object RemoveScope()
    {
        SchemeModule top = _scopes[_scopes.Count - 1];
        _scopes.RemoveAt(_scopes.Count - 1);
        SetCurrentScope();
        return top;
    }

    /// <summary>
    /// Points the interpreter's current module at the innermost scope.
    /// <para>Upstream: <c>Lily_lexer::set_current_scope</c>, which every scope push and
    /// pop ends with. It is what makes an embedded <c>#(define ...)</c> land in the
    /// block it was written in rather than in whatever module happened to be
    /// current.</para>
    /// </summary>
    private void SetCurrentScope()
    {
        if (_scopes.Count > 0)
        {
            _interpreter.CurrentModule = _scopes[_scopes.Count - 1];
        }
    }

    /// <inheritdoc/>
    public object CurrentModule() => _scopes[_scopes.Count - 1];

    private Dictionary<Symbol, ToplevelBinding> _toplevelSnapshot;

    private struct ToplevelBinding
    {
        public Variable Variable;
        public bool IsBound;
        public object Value;
    }

    /// <summary>
    /// Records the base scope's bindings — names, variable identities and values — as
    /// the state every file's parse should start from.
    /// <para>
    /// Upstream never needs this: it makes ONE PARSER PER FILE, so a file's toplevel
    /// assignments and <c>#(define ...)</c>s die with the parser. This session is
    /// shared across a whole batch, and the base scope is where those definitions
    /// land — take the snapshot right after the init layer loads, and
    /// <see cref="RestoreToplevelScope"/> gives each file upstream's fresh-parser
    /// semantics.
    /// </para>
    /// </summary>
    public void SnapshotToplevelScope()
    {
        SchemeModule scope = _scopes[0];
        Dictionary<Symbol, ToplevelBinding> snapshot
            = new Dictionary<Symbol, ToplevelBinding>();
        foreach (KeyValuePair<Symbol, Variable> entry in scope.Bindings)
        {
            snapshot[entry.Key] = new ToplevelBinding
            {
                Variable = entry.Value,
                IsBound = entry.Value.IsBound,
                Value = entry.Value.IsBound ? entry.Value.GetValue() : null,
            };
        }

        _toplevelSnapshot = snapshot;
    }

    /// <summary>
    /// Puts the base scope back the way <see cref="SnapshotToplevelScope"/> recorded
    /// it: a binding a file INVENTED is removed, and a binding a file overwrote gets
    /// its init-layer value back.
    /// <para>
    /// The removal half is the one that bites. The built-in vocal templates read OPTIONAL variables
    /// (<c>Time</c>, <c>TwoVoicesPerStaff</c>, instrument names) with
    /// <c>ly:parser-lookup</c>, so one template file's leftovers changed what the
    /// next template file BUILT: a leaked <c>Time = { s1 \break s1 }</c> forced a
    /// line break inside every later template in the sweep, and
    /// <c>ssaattbb-template-*</c> wrote two pages where the oracle writes one —
    /// while producing exactly one page run alone.
    /// </para>
    /// </summary>
    public void RestoreToplevelScope()
    {
        if (_toplevelSnapshot == null)
        {
            return;
        }

        SchemeModule scope = _scopes[0];

        List<Symbol> current = new List<Symbol>(scope.Bindings.Keys);
        foreach (Symbol symbol in current)
        {
            if (!_toplevelSnapshot.ContainsKey(symbol))
            {
                scope.Remove(symbol);
            }
        }

        foreach (KeyValuePair<Symbol, ToplevelBinding> entry in _toplevelSnapshot)
        {
            Variable local = scope.LookupLocal(entry.Key);
            if (local == null || !ReferenceEquals(local, entry.Value.Variable))
            {
                // The snapshot's VARIABLE OBJECT is restored, not just its value:
                // psyntax and the Scheme layer capture variables by identity, so a
                // fresh Variable under the same name would strand every captured
                // reference on the file's stale one.
                scope.Remove(entry.Key);
                scope.AddVariable(entry.Key, entry.Value.Variable);
                local = entry.Value.Variable;
            }

            if (entry.Value.IsBound)
            {
                local.SetValue(entry.Value.Value);
            }
        }
    }

    /// <inheritdoc/>
    public bool IsModule(object value) => value is SchemeModule;

    /// <inheritdoc/>
    public object MakeModule()
    {
        // ly_make_module, which the Engine carries as LilyModules.Make so that a parser
        // scope, a \header block and an output definition's scope are all built one way.
        // The named-module divergence and the expander limitation behind it are recorded
        // there and in PORT-COVERAGE.
        return LilyModules.Make(_interpreter, "parser-scope");
    }

    /// <inheritdoc/>
    public void ModuleCopy(object destination, object source)
    {
        SchemeModule from = (SchemeModule)source;
        SchemeModule to = (SchemeModule)destination;
        foreach (KeyValuePair<Symbol, Variable> binding in from.Bindings)
        {
            to.Define(binding.Key, binding.Value.GetValue());
        }
    }

    /// <inheritdoc/>
    public bool TryModuleVariable(object module, object name, out object value)
    {
        Variable variable = ((SchemeModule)module).Lookup((Symbol)name);
        value = variable?.GetValue();
        return variable != null;
    }

    // ------ evaluation (Lily_lexer::eval_scm_token, ly_call) ------

    /// <inheritdoc/>
    public object EvalSchemeToken(object token, SourceSpan location)
        => EvalScheme(token, location, '#');

    /// <summary>
    /// <c>Lily_lexer::eval_scm</c> — evaluates what the embedded-Scheme reader produced,
    /// and spreads a multiple-values result into extra tokens.
    /// <para>
    /// The <paramref name="extraToken"/> parameter is upstream's, and it decides how the
    /// EXTRA values of a <c>#@</c> / <c>$@</c> form are delivered: <c>'#'</c> pushes each
    /// as a plain <c>SCM_IDENTIFIER</c>, while <c>'$'</c> asks what each value IS and
    /// pushes the matching <c>*_IDENTIFIER</c>. Values are pushed from the LAST back to
    /// the second and the first is returned, so the pushback queue hands them out in
    /// written order.
    /// </para>
    /// <para>
    /// An unreadable expression arrives as <see cref="DefaultArgument"/> — the reader's
    /// <c>SCM_UNDEFINED</c> — and is NOT evaluated; it only raises the error level, which
    /// is what keeps one bad <c>#(...)</c> from being reported twice.
    /// </para>
    /// </summary>
    /// <param name="token">The datum the reader produced.</param>
    /// <param name="location">Where the expression began.</param>
    /// <param name="extraToken">Upstream's extra-token discriminator: <c>'#'</c> or
    /// <c>'$'</c>.</param>
    /// <returns>The value, or <see cref="Unspecified"/> when evaluation failed.</returns>
    public object EvalScheme(object token, SourceSpan location, char extraToken)
    {
        if (token is DefaultArgument)
        {
            ErrorLevel = 1;
            return Unspecified.Instance;
        }

        object value = EvaluateEmbedded(token, location);
        if (value is DefaultArgument)
        {
            ErrorLevel = 1;
            return Unspecified.Instance;
        }

        if (!(value is MultipleValues values))
        {
            return value;
        }

        if (values.Items.Length == 0)
        {
            return Unspecified.Instance;
        }

        for (int i = values.Items.Length - 1; i >= 1; i--)
        {
            object extra = values.Items[i];
            LexerLookup announced = extraToken == '$'
                ? IdentifierToken(extra)
                : new LexerLookup("SCM_IDENTIFIER", extra);
            if (!announced.Found)
            {
                continue;
            }

            if (announced.FunctionSignature != null)
            {
                Scanner?.PushFunctionSignature(announced.FunctionSignature);
            }

            Scanner?.PushExtraToken(new ParserToken(
                Scanner.Terminal(announced.TokenName), announced.Value, location));
        }

        return values.Items[0];
    }

    /// <summary>
    /// <c>evaluate_embedded_scheme</c> — runs one embedded form with <c>(*location*)</c>
    /// bound to where it was written, and turns a Scheme-level failure into a located
    /// diagnostic instead of an abort.
    /// </summary>
    /// <param name="form">The form to evaluate.</param>
    /// <param name="location">Where it was written.</param>
    /// <returns>The value, or <see cref="DefaultArgument"/> when it raised.</returns>
    private object EvaluateEmbedded(object form, SourceSpan location)
    {
        // The scanner hands over the DATUM it read; upstream evaluates it in the
        // current module, which is why an embedded #(...) sees the identifiers a
        // \header or \with block has opened.
        //
        // THROUGH THE EXPANDER, not the bare evaluator. Almost everything a .ly file
        // embeds is a MACRO USE — define-music-function and define-markup-command are
        // the whole of ly/music-functions-init.ly, and `use-modules` is a macro too.
        // Evaluated without expansion they read as procedure calls and die on an
        // unbound variable named after their first argument, which is what the first
        // run of the init layer did, once per definition.
        //
        // WRAPPED IN THE LOCATION FLUID, because evaluate_embedded_scheme's whole
        // prologue is `scm_dynwind_fluid (Lily::f_location, start.smobbed_copy ())`:
        // an embedded expression that asks (*location*) — every music function that
        // reports about its own argument does — must be told where IT was written and
        // not where the enclosing construct happened to leave the fluid.
        try
        {
            return WithLocation(location, () =>
            {
                // A closure recorded by the #{ ... #} reader is already a thunk over its
                // original lexical environment; upstream calls it rather than evaluating
                // it (`if (ly_is_procedure (ps->form_)) return ly_call (ps->form_);`).
                if (form is Procedure || form is IApplicable)
                {
                    return Call(form);
                }

                SchemeModule scope = CurrentSchemeModule();
                SchemeModule saved = _interpreter.CurrentModule;
                try
                {
                    _interpreter.CurrentModule = scope;
                    return _interpreter.TreeIlEvaluator.ExpandAndEval(
                        CurriedDefinitions.Expand(form), scope);
                }
                finally
                {
                    _interpreter.CurrentModule = saved;
                }
            });
        }
        catch (Exception ex)
        {
            ParserError(location, ex.Message);
            return DefaultArgument.Instance;
        }
    }

    /// <summary>
    /// Returns the module embedded Scheme evaluates in: the innermost pushed SCOPE, so
    /// that a <c>#(...)</c> inside a <c>\header</c> sees that header's own bindings.
    /// <para>Upstream keeps <c>scm_current_module</c> in step with
    /// <c>Lily_lexer::scopes_</c> through <c>add_scope</c>/<c>remove_scope</c>.</para>
    /// </summary>
    /// <returns>The module.</returns>
    private SchemeModule CurrentSchemeModule() => _scopes[_scopes.Count - 1];

    /// <inheritdoc/>
    public object Call(object procedure, params object[] arguments)
        => _interpreter.Evaluator.Apply(procedure, arguments ?? new object[0]);

    /// <inheritdoc/>
    public object LilyImport(string name)
    {
        Variable variable = _lilyModule.Lookup(Symbol.Intern(name));
        if (variable == null)
        {
            throw new InvalidOperationException(
                "the (lily) module does not bind '" + name
                + "' — the Scheme layer is not loaded, or the name moved upstream");
        }

        return variable.GetValue();
    }

    /// <inheritdoc/>
    public object SyntaxConstructor(string constructor)
    {
        // Syntax:: lives in (lily ly-syntax-constructors), a different module from
        // Lily:: — see the note on IParserHost.LilyImport.
        SchemeModule module = _interpreter.Modules.Resolve(
            Pair.List(Symbol.Intern("lily"), Symbol.Intern("ly-syntax-constructors")));
        Variable variable = module.Lookup(Symbol.Intern(constructor));
        if (variable == null)
        {
            throw new InvalidOperationException(
                "scm/ly-syntax-constructors.scm does not define '" + constructor + "'");
        }

        return variable.GetValue();
    }

    /// <inheritdoc/>
    public object MakeSyntax(string constructor, SourceSpan location, params object[] arguments)
    {
        // MAKE_SYNTAX -> make_syntax -> with_location, which is
        //
        //   ly_with_fluid (Lily::f_location, <the Input>, [] { scm_call_n (proc, ...); })
        //
        // THE LOCATION IS NOT AN ARGUMENT. It is bound to the %location fluid for the
        // dynamic extent of the call, and the constructor reads it back as (*location*)
        // when it needs one — which is why not one constructor in
        // scm/ly-syntax-constructors.scm declares a location parameter. An earlier pass
        // passed it as the first argument, so every constructor was called with one
        // argument too many; nothing caught it because the rule-action tests drive a
        // scripted host whose MakeSyntax never reaches the real Scheme.
        return WithLocation(location, () => Call(SyntaxConstructor(constructor), arguments));
    }

    /// <inheritdoc/>
    public object ApplySyntax(object constructor, SourceSpan location, object arguments)
    {
        // FINISH_MAKE_SYNTAX: Guile's `apply` spreads the argument list over the
        // constructor, under the same location binding as MAKE_SYNTAX.
        object[] all = Pair.ToList(arguments).ToArray();
        return WithLocation(location, () => Call(constructor, all));
    }

    /// <summary>
    /// Runs an action with <c>(*location*)</c> bound to a span, and restores whatever was
    /// bound before.
    /// <para>Upstream: <c>with_location_n</c> in <c>lily/input.cc</c>, over
    /// <c>Lily::f_location</c> — the <c>%location</c> fluid <c>scm/lily.scm</c> defines.
    /// A location that is not a real <see cref="Input"/> binds <see langword="false"/>,
    /// exactly as upstream's <c>unsmob&lt;Input&gt; (loc) ? loc : SCM_BOOL_F</c> does.</para>
    /// </summary>
    /// <param name="location">The span to bind.</param>
    /// <param name="action">What to run under it.</param>
    /// <returns>What the action returned.</returns>
    public object WithLocation(SourceSpan location, Func<object> action)
    {
        Fluid fluid = LocationFluid();
        if (fluid == null)
        {
            return action();
        }

        object origin = SchemeLocation(location);
        object saved = fluid.Value;
        fluid.Value = origin ?? (object)false;
        try
        {
            return action();
        }
        finally
        {
            fluid.Value = saved;
        }
    }

    private Fluid LocationFluid()
        => _locationFluid ??= _lilyModule.Lookup(Symbol.Intern("%location"))?.GetValue() as Fluid;

    private Fluid _locationFluid;

    // ------ diagnostics ------

    /// <inheritdoc/>
    public void Warning(SourceSpan location, string message)
    {
        // Input::warning — a diagnostic that does NOT move the error level.
        Diagnostics.Add(Describe(location) + "warning: " + message);
        Flower.Warn.Warning(message, Describe(location));
    }

    /// <inheritdoc/>
    public void MusicWarning(object music, string message)
    {
        object origin = music is MusicObject m ? m.Origin : null;
        Diagnostics.Add(
            (origin is SourceSpan span ? Describe(span) : string.Empty) + "warning: " + message);
        Flower.Warn.Warning(message);
    }

    /// <summary>Reports a parse error and raises the error level.</summary>
    /// <param name="location">Where it is.</param>
    /// <param name="message">The message.</param>
    public void ParserError(SourceSpan location, string message)
    {
        Diagnostics.Add(Describe(location) + "error: " + message);
        ErrorLevel = 1;
    }

    private static string Describe(SourceSpan location)
        => location.FileName == null
            ? string.Empty
            : location.FileName + ":" + location.StartLine + ":" + location.StartColumn + ": ";

    /// <summary>
    /// Turns a span into the <see cref="Input"/> the Scheme layer expects for a location.
    /// <para>
    /// Upstream every location IS an <c>Input</c> — <c>ly:input-location?</c> answers on
    /// it, <c>ly:input-file-line-char-column</c> reads it, <c>Input::warning</c> quotes
    /// the line it points at. A span with no offsets — one built by hand in a fixture, or
    /// from a file this session never opened — becomes an <see cref="Input"/> with no
    /// source file, which reports "position unknown" rather than a plausible wrong place.
    /// </para>
    /// </summary>
    /// <param name="location">The span.</param>
    /// <returns>The origin.</returns>
    public Input SchemeLocation(SourceSpan location)
    {
        if (location.StartOffset < 0 || location.FileName == null)
        {
            return new Input();
        }

        SourceFile file = SourceFileFor(location.FileName);
        return file == null
            ? new Input()
            : new Input(
                file,
                Math.Min(location.StartOffset, file.Length),
                Math.Min(Math.Max(location.EndOffset, location.StartOffset), file.Length));
    }

    /// <summary>
    /// Gets the source files this session has opened, in the order it opened them.
    /// <para>Upstream: the <c>Sources</c> object <c>Lily_parser</c> is constructed with,
    /// which every <c>Input</c> points into and which <c>ly:source-files</c> reports.</para>
    /// </summary>
    public Sources Sources { get; } = new Sources();

    private readonly Dictionary<string, SourceFile> _sourceFiles
        = new Dictionary<string, SourceFile>(StringComparer.Ordinal);

    /// <summary>
    /// Records a file's text so locations in it can be turned into real origins, and adds
    /// it to <see cref="Sources"/>.
    /// <para>Upstream: <c>Includable_lexer::new_input</c>, which calls
    /// <c>Sources::get_file</c> and keeps the <c>Source_file</c> for the rest of the
    /// run — an origin made while a file was open must still be able to quote it when
    /// the error surfaces much later.</para>
    /// </summary>
    /// <param name="fileName">The name locations in the text will carry.</param>
    /// <param name="text">The file's text.</param>
    /// <returns>The source file.</returns>
    public SourceFile OpenSource(string fileName, string text)
    {
        string key = fileName ?? "<input>";
        if (_sourceFiles.TryGetValue(key, out SourceFile existing)
            && string.Equals(existing.Text, text, StringComparison.Ordinal))
        {
            return existing;
        }

        SourceFile file = new SourceFile(key, text ?? string.Empty);
        _sourceFiles[key] = file;
        Sources.Add(file);
        return file;
    }

    private SourceFile SourceFileFor(string fileName)
        => _sourceFiles.TryGetValue(fileName, out SourceFile file) ? file : null;
}
