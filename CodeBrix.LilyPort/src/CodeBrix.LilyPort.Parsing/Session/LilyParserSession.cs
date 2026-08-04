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
using CodeBrix.LilyPort.Parsing.Actions;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyPort.Parsing.Lexing;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Session; //was previously: lily/lily-parser.cc, lily/lily-lexer.cc, lily/lexer.ll (the host halves);

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

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

    /// <summary>Gets the interpreter this session runs on.</summary>
    public Interpreter Interpreter => _interpreter;

    /// <summary>Gets the <c>(lily)</c> module — where the Scheme layer's own bindings live.</summary>
    public SchemeModule LilyModule => _lilyModule;

    /// <summary>Gets or sets the scanner the mode operations drive.</summary>
    /// <remarks>
    /// Set for the duration of a parse. The lexer's mode stack is real state, and a
    /// rule action that pushes a mode is telling THIS scanner to change what it reads
    /// next — a session with no scanner attached records nothing and silently lexes
    /// the whole file in one mode, which is the failure RAG16 recorded.
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
        Symbol symbol = Symbol.Intern(name);
        for (int i = _scopes.Count - 1; i >= 0; i--)
        {
            Variable variable = _scopes[i].LookupLocal(symbol);
            if (variable != null)
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

    /// <inheritdoc/>
    public bool IsModule(object value) => value is SchemeModule;

    /// <inheritdoc/>
    public object MakeModule()
    {
        // ly_make_module: a fresh module that uses the root module and (lily), which is
        // what lets a \header or \with body call Scheme functions.
        //
        // DIVERGENCE, and it is load-bearing: upstream's module is ANONYMOUS, and this
        // one is NAMED and REGISTERED. The expander resolves an imported MACRO only in
        // a module it can name — in an anonymous one, `define-music-function` reads as
        // an ordinary variable and its argument list is evaluated, so every music
        // function in ly/music-functions-init.ly failed with an unbound variable named
        // after its first parameter. Named modules expand it correctly. Recorded in
        // PORT-COVERAGE; the underlying expander limitation is recorded there too,
        // because a fix in LilyScheme would let this go back to matching upstream.
        SchemeModule module = new SchemeModule(
            Pair.List(Symbol.Intern("lily"), Symbol.Intern("parser-scope"), ++_scopeSerial));
        module.AddUse(_interpreter.Modules.RootModule);
        module.AddUse(_lilyModule);
        _interpreter.Modules.Register(module);
        return module;
    }

    private static long _scopeSerial;

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
        try
        {
            SchemeModule scope = CurrentSchemeModule();
            SchemeModule saved = _interpreter.CurrentModule;
            try
            {
                _interpreter.CurrentModule = scope;
                return _interpreter.TreeIlEvaluator.ExpandAndEval(
                    CurriedDefinitions.Expand(token), scope);
            }
            finally
            {
                _interpreter.CurrentModule = saved;
            }
        }
        catch (Exception ex)
        {
            ParserError(location, ex.Message);
            return Unspecified.Instance;
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
        // MAKE_SYNTAX: the constructor is called with the LOCATION first, so a
        // diagnostic raised inside it points at the right place in the file.
        object[] all = new object[(arguments?.Length ?? 0) + 1];
        all[0] = SchemeLocation(location);
        if (arguments != null)
        {
            Array.Copy(arguments, 0, all, 1, arguments.Length);
        }

        return Call(SyntaxConstructor(constructor), all);
    }

    /// <inheritdoc/>
    public object ApplySyntax(object constructor, SourceSpan location, object arguments)
    {
        // FINISH_MAKE_SYNTAX: the constructor and its first arguments were consed
        // together by START_MAKE_SYNTAX, and the location goes in between.
        List<object> all = new List<object> { SchemeLocation(location) };
        all.AddRange(Pair.ToList(arguments));
        return Call(constructor, all.ToArray());
    }

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
    /// Presents a span as the value the Scheme layer expects for a location.
    /// <para>Upstream passes the <c>Input</c> smob; the port boxes the span, which is
    /// what <c>MusicObject.SetSpot</c> and the vendored constructors already store and
    /// read back.</para>
    /// </summary>
    /// <param name="location">The span.</param>
    /// <returns>The boxed span.</returns>
    private static object SchemeLocation(SourceSpan location) => location;
}
