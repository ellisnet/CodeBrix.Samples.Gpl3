// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Parsing.Driver;

namespace CodeBrix.LilyPort.Parsing.Actions;

/// <summary>
/// What a rule action needs from the caller's state to record that the file was bad.
/// <para>
/// Upstream this is <c>Lily_parser::error_level_</c>, reached through the
/// <c>%parse-param</c>. The port keeps the parser generic and lets the caller supply
/// it, so that <c>CodeBrix.LilyPort.Parsing</c> does not have to depend on whatever
/// eventually plays the part of <c>Lily_parser</c>.
/// </para>
/// </summary>
public interface IParserErrorLevel
{
    /// <summary>Gets or sets the error level: non-zero once the file has failed.</summary>
    int ErrorLevel { get; set; }
}

/// <summary>
/// The things a rule action reaches through <c>Lily_parser</c> and <c>Lily_lexer</c>
/// upstream, behind one seam.
/// <para>
/// This is the same pattern, for the same reason, as <see cref="Lexing.ILexerHost"/>
/// and the Engine's <c>Context.ContextFactory</c>: the actions stay faithful to the
/// upstream bodies while <c>CodeBrix.LilyPort.Parsing</c> does not depend on the
/// Scheme interpreter's lifecycle. The REAL host is whatever eventually plays
/// <c>Lily_parser</c>; tests supply a scripted one.
/// </para>
/// <para>
/// The surface grows RULE-ACTION-GROUP BY RULE-ACTION-GROUP, mirroring exactly the
/// members the ported actions use — nothing is added speculatively. Every member
/// names its upstream counterpart. The interface is PARTIAL so that a group's
/// porting session adds its members in its own <c>IParserHost.RagN.cs</c> file
/// rather than editing this one — the same no-shared-file-churn rule as the
/// <c>LilyPondRuleActions.RagN.cs</c> split.
/// </para>
/// </summary>
public partial interface IParserHost : IParserErrorLevel
{
    /// <summary>
    /// Looks a parser identifier up through the scope chain — the value of
    /// <c>\name</c>, of a handler such as <c>toplevel-score-handler</c>, or of a
    /// session variable such as <c>$defaultheader</c>.
    /// <para>Upstream: <c>Lily_lexer::lookup_identifier</c> (and its
    /// <c>lookup_identifier_symbol</c> form, which differs only in taking the
    /// already-interned symbol).</para>
    /// </summary>
    /// <param name="name">The identifier's name, without any backslash.</param>
    /// <returns>The value, or <see cref="CodeBrix.LilyScheme.Values.DefaultArgument"/>
    /// when the identifier is not defined.</returns>
    object LookupIdentifier(string name);

    /// <summary>
    /// Assigns a parser identifier in the current scope.
    /// <para>Upstream: <c>Lily_lexer::set_identifier</c>. The key is either an
    /// interned <see cref="CodeBrix.LilyScheme.Values.Symbol"/> or, for
    /// <c>name.path = value</c> assignments, a pair of the base symbol and the
    /// property path.</para>
    /// </summary>
    /// <param name="key">The symbol, or a <c>(symbol . path)</c> pair.</param>
    /// <param name="value">The value to bind.</param>
    void SetIdentifier(object key, object value);

    /// <summary>
    /// Evaluates the datum a <c>SCM_TOKEN</c> carries and returns the result.
    /// <para>Upstream: <c>Lily_lexer::eval_scm_token</c>.</para>
    /// </summary>
    /// <param name="token">The token's semantic value, as the lexer produced it.</param>
    /// <param name="location">Where the token was read.</param>
    /// <returns>The evaluated value.</returns>
    object EvalSchemeToken(object token, SourceSpan location);

    /// <summary>
    /// Puts the lexer into note mode, stacking the current mode.
    /// <para>Upstream: <c>Lily_lexer::push_note_state</c>.</para>
    /// </summary>
    void PushNoteState();

    /// <summary>
    /// Returns the lexer to the stacked mode.
    /// <para>Upstream: <c>Lily_lexer::pop_state</c>.</para>
    /// </summary>
    void PopLexerState();

    /// <summary>
    /// Pushes a module onto the lexer's scope stack, so assignments land in it and
    /// lookups see it first. The current module follows the top of the stack.
    /// <para>Upstream: <c>Lily_lexer::add_scope</c>.</para>
    /// </summary>
    /// <param name="module">The module to push.</param>
    void AddScope(object module);

    /// <summary>
    /// Pops the lexer's scope stack and returns the removed module.
    /// <para>Upstream: <c>Lily_lexer::remove_scope</c>.</para>
    /// </summary>
    /// <returns>The module that was on top.</returns>
    object RemoveScope();

    /// <summary>
    /// Returns the current module — the top of the scope stack while one is pushed.
    /// <para>Upstream: <c>scm_current_module</c>, which
    /// <c>Lily_lexer::add_scope</c> keeps in step with the scope stack.</para>
    /// </summary>
    /// <returns>The current module.</returns>
    object CurrentModule();

    /// <summary>
    /// Answers whether a value is a module.
    /// <para>Upstream: <c>ly_is_module</c>.</para>
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> for a module.</returns>
    bool IsModule(object value);

    /// <summary>
    /// Makes a fresh, empty module.
    /// <para>Upstream: <c>ly_make_module</c>.</para>
    /// </summary>
    /// <returns>The new module.</returns>
    object MakeModule();

    /// <summary>
    /// Copies every binding of one module into another.
    /// <para>Upstream: <c>ly_module_copy</c>.</para>
    /// </summary>
    /// <param name="destination">The module copied into.</param>
    /// <param name="source">The module copied from.</param>
    void ModuleCopy(object destination, object source);

    /// <summary>
    /// Looks a name up in a module.
    /// <para>Upstream: <c>scm_module_variable</c> followed by
    /// <c>scm_variable_ref</c> — the two are folded into one call because every
    /// action site uses them together.</para>
    /// </summary>
    /// <param name="module">The module to search.</param>
    /// <param name="name">The interned symbol to look up.</param>
    /// <param name="value">Receives the bound value when found.</param>
    /// <returns><see langword="true"/> when the module binds the name.</returns>
    bool TryModuleVariable(object module, object name, out object value);

    /// <summary>
    /// Calls a Scheme procedure.
    /// <para>Upstream: <c>ly_call</c>.</para>
    /// </summary>
    /// <param name="procedure">The procedure.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>The procedure's result.</returns>
    object Call(object procedure, params object[] arguments);

    /// <summary>
    /// Dispatches to a constructor in <c>scm/ly-syntax-constructors.scm</c>.
    /// <para>Upstream: the <c>MAKE_SYNTAX</c> macro. The name is the SCHEME-side
    /// name — <c>partial-music-function</c>, with dashes — because that file is
    /// already vendored and the C++ identifier is derived from it.</para>
    /// </summary>
    /// <param name="constructor">The constructor's Scheme name.</param>
    /// <param name="location">The <c>@$</c> span, which upstream passes first.</param>
    /// <param name="arguments">The constructor's arguments.</param>
    /// <returns>The constructed value.</returns>
    object MakeSyntax(string constructor, SourceSpan location, params object[] arguments);

    /// <summary>
    /// Clones a location-carrying value (music, book, output definition, score,
    /// context definition or modification) and stamps a location on the copy;
    /// returns anything else unchanged.
    /// <para>Upstream: <c>loc_on_copy</c> in <c>parser.yy</c>'s epilogue. It lives on
    /// the host because the clone dispatch runs over engine types the parser
    /// assembly does not otherwise own.</para>
    /// </summary>
    /// <param name="value">The value to copy.</param>
    /// <param name="location">The span to stamp.</param>
    /// <returns>The copy, or the value itself.</returns>
    object LocOnCopy(object value, SourceSpan location);

    /// <summary>
    /// Makes a music object of a named type, with its type's properties from the
    /// music-descriptions table, stamped with a location.
    /// <para>Upstream: the <c>MY_MAKE_MUSIC</c> macro —
    /// <c>make_music_with_input (name, loc)</c>. On the host because the
    /// descriptions table lives in the Scheme layer.</para>
    /// </summary>
    /// <param name="name">The music type's name, such as <c>PostEvents</c>.</param>
    /// <param name="location">The <c>@$</c> span.</param>
    /// <returns>The music object.</returns>
    object MakeMusic(string name, SourceSpan location);

    /// <summary>
    /// Sets a property on a music object made by <see cref="MakeMusic"/>.
    /// <para>Upstream: <c>set_property</c> on <c>Music</c>.</para>
    /// </summary>
    /// <param name="music">The music object.</param>
    /// <param name="name">The property name.</param>
    /// <param name="value">The value.</param>
    void SetMusicProperty(object music, string name, object value);

    /// <summary>
    /// Answers whether a value is a markup.
    /// <para>Upstream: <c>Text_interface::is_markup</c>.</para>
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> for a markup.</returns>
    bool IsMarkup(object value);

    /// <summary>
    /// Answers whether a value is a markup list.
    /// <para>Upstream: <c>Text_interface::is_markup_list</c>.</para>
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> for a markup list.</returns>
    bool IsMarkupList(object value);

    /// <summary>
    /// Answers whether a value is a markup function.
    /// <para>Upstream: <c>Lily::markup_function_p</c>.</para>
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> for a markup function.</returns>
    bool IsMarkupFunction(object value);

    /// <summary>
    /// Registers a markup command under a name.
    /// <para>Upstream: <c>Lily::define_markup_command_internal</c> with its third
    /// argument <c>#f</c>, which is the only way the grammar calls it.</para>
    /// </summary>
    /// <param name="name">The command's name, an interned symbol.</param>
    /// <param name="function">The markup function.</param>
    void DefineMarkupCommand(object name, object function);

    /// <summary>
    /// Answers whether a value is a score.
    /// <para>Upstream: <c>unsmob&lt;Score&gt;</c>. On the host because the engine
    /// has no <c>Score</c> type yet; when one is ported this member keeps its
    /// shape.</para>
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> for a score.</returns>
    bool IsScore(object value);

    /// <summary>
    /// Answers whether a book value carries its own paper block — which is what
    /// decides whether a <c>BOOK_IDENTIFIER</c> at top level is handled as a book
    /// or as a bookpart.
    /// <para>Upstream: <c>unsmob&lt;Book&gt;($1)-&gt;paper_</c>. On the host
    /// because the engine has no <c>Book</c> type yet.</para>
    /// </summary>
    /// <param name="book">The book value.</param>
    /// <returns><see langword="true"/> when the book has a paper block.</returns>
    bool BookHasPaper(object book);

    /// <summary>
    /// Answers whether a value is a key — a symbol or a non-negative exact integer.
    /// <para>Upstream: <c>Lily::key_p</c>, defined in <c>scm/c++.scm</c> as
    /// <c>(or (symbol? x) (index? x))</c>.</para>
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> for a key.</returns>
    bool IsKey(object value);
}
