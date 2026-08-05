// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;

namespace CodeBrix.LilyPort.Engine.Origins;

/// <summary>
/// What the <c>ly:parser-*</c> bindings need from whatever is playing
/// <c>Lily_parser</c> — the seam that lets <c>lily-parser-scheme.cc</c>'s entry points
/// live in the Engine while the parser itself lives in
/// <c>CodeBrix.LilyPort.Parsing</c>, which references the Engine and not the other way
/// round.
/// <para>
/// Upstream every one of these bindings starts with
/// <c>scm_fluid_ref (Lily::f_parser)</c> and unsmobs a <c>Lily_parser</c>. The port keeps
/// the fluid — it is <c>%parser</c> in <c>scm/lily.scm</c>, and <c>(*parser*)</c> reads
/// it — and holds one of these in it.
/// </para>
/// </summary>
public interface ILilyParser
{
    /// <summary>Gets or sets the parser's error level, which any error raises to one.</summary>
    int ErrorLevel { get; set; }

    /// <summary>Gets or sets the lexer's error level.</summary>
    /// <remarks>Upstream <c>ly:parser-has-error?</c> and <c>ly:parser-clear-error</c> read
    /// and clear BOTH levels, because a lexer error is as fatal to the run as a parse
    /// error and neither one alone is the whole answer.</remarks>
    int LexerErrorLevel { get; set; }

    /// <summary>Gets or sets the base name output files are derived from.</summary>
    /// <remarks>Upstream: <c>Lily_parser::output_basename_</c>.</remarks>
    string OutputBaseName { get; set; }

    /// <summary>Gets the directories an <c>\include</c> searches.</summary>
    /// <remarks>Upstream: <c>Lily_parser::sources_-&gt;path_</c>, appended to by
    /// <c>ly:parser-append-to-include-path</c>.</remarks>
    IList<string> IncludePath { get; }

    /// <summary>Gets the source files this parser has opened, in order.</summary>
    /// <remarks>What <c>ly:source-files</c> reports.</remarks>
    IReadOnlyList<SourceFile> SourceFiles { get; }

    /// <summary>Binds a name in the parser's innermost scope.</summary>
    /// <param name="key">The symbol, or a <c>(symbol . path)</c> pair.</param>
    /// <param name="value">The value.</param>
    void SetIdentifier(object key, object value);

    /// <summary>Looks a name up through the parser's scope stack.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The value, or <c>DefaultArgument.Instance</c> when unbound.</returns>
    object LookupIdentifier(string name);

    /// <summary>Replaces the note-name table, if the lexer is currently in note mode.</summary>
    /// <param name="names">The alist of note names.</param>
    /// <remarks>Upstream <c>ly:parser-set-note-names</c> pops and re-pushes note state so
    /// the new table takes effect for the mode already open. Outside note mode it only
    /// records the table, which is what <c>declarations-init.ly</c> relies on: it calls
    /// <c>note-names-language</c> at top level, long before any <c>\notemode</c>.</remarks>
    void SetNoteNames(object names);

    /// <summary>Reports a parse error at a location, and raises the error level.</summary>
    /// <param name="origin">Where the error is, or <see langword="null"/> for the parser's
    /// current position.</param>
    /// <param name="message">The message.</param>
    void ParserError(Input origin, string message);

    /// <summary>Pushes a string into the input stream, as <c>\include</c> would a file.</summary>
    /// <param name="code">The LilyPond source.</param>
    /// <remarks>Upstream: <c>Lily_parser::include_string</c>, reachable only from an
    /// IMMEDIATE Scheme expression (<c>$</c>), because by the time a <c>#</c> is evaluated
    /// the lexer has moved on.</remarks>
    void IncludeString(string code);

    /// <summary>Parses a string as a complete input, running its toplevel expressions.</summary>
    /// <param name="code">The LilyPond source.</param>
    void ParseString(string code);

    /// <summary>Parses a string as one music expression and returns it.</summary>
    /// <param name="code">The LilyPond source.</param>
    /// <param name="fileName">The file name locations should carry.</param>
    /// <param name="line">The line number the text starts at, or zero.</param>
    /// <returns>The music expression.</returns>
    object ParseStringExpression(string code, string fileName, int line);

    /// <summary>Gets a value indicating whether this parser has read anything yet.</summary>
    /// <remarks>Upstream: <c>Lily_lexer::is_clean</c>. <c>ly:parser-parse-string</c> and
    /// <c>ly:parse-string-expression</c> both refuse a parser that is not clean, and
    /// point the caller at <c>ly:parser-include-string</c> instead.</remarks>
    bool IsClean { get; }

    /// <summary>Returns a fresh parser sharing this one's scopes and Scheme layer.</summary>
    /// <param name="closures">An alist of port positions to precompiled closures.</param>
    /// <param name="origin">The location every music expression inside should carry, or
    /// <see langword="null"/>.</param>
    /// <returns>The clone.</returns>
    ILilyParser Clone(object closures, Input origin);
}
