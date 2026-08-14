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
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyPort.Parsing.Lexing;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Session; //was previously: lily/lily-parser.cc (the ly:parser-* surface);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <content>
/// What the <c>ly:parser-*</c> bindings reach — the session seen as upstream's
/// <c>Lily_parser</c> smob.
/// </content>
public sealed partial class LilyParserSession : ILilyParser, IExtraSourceFiles
{
    /// <inheritdoc/>
    public int LexerErrorLevel { get; set; }

    /// <inheritdoc/>
    public string OutputBaseName { get; set; } = string.Empty;

    /// <inheritdoc/>
    IList<string> ILilyParser.IncludePath => IncludePath;

    /// <inheritdoc/>
    public IReadOnlyList<SourceFile> SourceFiles => Sources.SourceFiles;

    /// <summary>
    /// Gets a value indicating whether this session has read anything yet.
    /// <para>Upstream: <c>Lily_lexer::is_clean</c>, which asks whether the input stack is
    /// empty. A session that has parsed something cannot be handed to
    /// <c>ly:parser-parse-string</c>, because the string would be read into the middle of
    /// whatever is already open.</para>
    /// </summary>
    public bool IsClean => Scanner == null && !_hasParsed;

    private bool _hasParsed;

    /// <inheritdoc/>
    public void ParserError(Input origin, string message)
    {
        // Upstream: Lily_parser::parser_error, which reports at the given Input or at the
        // lexer's current position, and raises error_level_ either way.
        if (origin != null)
        {
            Diagnostics.Add(origin.LocationString() + ": error: " + message);
            origin.NonFatalError(message);
        }
        else
        {
            Diagnostics.Add("error: " + message);
            Flower.Warn.NonFatalError(message);
        }

        ErrorLevel = 1;
    }

    /// <summary>
    /// Reads the note-name table currently in force — the counterpart of
    /// <see cref="SetNoteNames"/>, and the only way to snapshot it.
    /// <para>
    /// It exists for the batch runner. Upstream engraves one file per process, so
    /// <c>\language</c> and every <c>\include</c> that wraps it die with the file that
    /// asked; a runner sharing one session across a suite has to put the table back
    /// itself, or the first file that says <c>\language "italiano"</c> renames the notes
    /// for every file that follows it.
    /// </para>
    /// </summary>
    /// <returns>The note-name table, or <see langword="null"/> when none is bound.</returns>
    public object NoteNames()
    {
        Variable variable = _lilyModule.Lookup(Symbol.Intern("pitchnames"));
        return variable != null && variable.IsBound ? variable.GetValue() : null;
    }

    /// <inheritdoc/>
    public void SetNoteNames(object names)
    {
        // Upstream's body is
        //
        //   if (p->lexer_->is_note_state ()) { pop_state (); Lily::pitchnames = names;
        //                                      push_note_state (); }
        //
        // and the ASSIGNMENT is what matters: Lily::pitchnames is a handle onto (lily)'s
        // `pitchnames' variable, so the table is stored whether or not note state is open.
        // The pop/push only makes an ALREADY-OPEN note mode pick the new table up.
        // declarations-init.ly calls note-names-language at top level, outside any note
        // mode, so a port that only acted inside note state stored nothing at all and
        // every note name in the init layer read as "not a note name".
        Variable variable = _lilyModule.Lookup(Symbol.Intern("pitchnames"));
        if (variable != null)
        {
            variable.SetValue(names);
        }
        else
        {
            _lilyModule.Define(Symbol.Intern("pitchnames"), names);
        }

        if (Scanner != null && Scanner.State == LexerState.Notes)
        {
            PopLexerState();
            PushNoteState();
        }
    }

    /// <inheritdoc/>
    public void IncludeString(string code)
    {
        // Upstream: Lily_parser::include_string, which pushes the text onto the lexer's
        // input stack under the name "<included string>".
        Scanner?.BeginIncludeText("<included string>", code);
    }

    /// <inheritdoc/>
    public void ParseString(string code)
    {
        ParseOutcome outcome = ParseText(code, "<string>");
        ErrorLevel |= outcome.ErrorCount > 0 ? 1 : 0;
        LexerErrorLevel |= outcome.LexerErrors.Count > 0 ? 1 : 0;
    }

    /// <inheritdoc/>
    public object ParseStringExpression(string code, string fileName, int line)
    {
        // Upstream pushes an EMBEDDED_LILY token in front of the input so the grammar
        // enters at the music-expression production rather than at the toplevel one, and
        // renumbers the source file when a line is given.
        SourceFile file = OpenSource(fileName ?? "<string>", code);
        if (line != 0)
        {
            file.SetLine(0, line);
        }

        ParseOutcome outcome = ParseText(code, fileName ?? "<string>", "EMBEDDED_LILY");
        ErrorLevel |= outcome.ErrorCount > 0 ? 1 : 0;
        LexerErrorLevel |= outcome.LexerErrors.Count > 0 ? 1 : 0;
        return outcome.Result;
    }

    /// <inheritdoc/>
    public ILilyParser Clone(object closures, Input origin)
    {
        // Upstream: Lily_parser (Lily_parser const &, SCM closures, SCM location) — a new
        // parser over the SAME lexer scopes, carrying the precompiled closures an earlier
        // pass recorded and, when given, an override location for every music expression
        // it builds.
        LilyParserSession clone = new LilyParserSession(_interpreter, this);
        clone.Closures = closures ?? Nil.Instance;
        clone.OverrideOrigin = origin;
        clone.OutputBaseName = OutputBaseName;
        return clone;
    }

    /// <summary>
    /// Gets or sets the alist of byte offsets to precompiled closures a clone carries.
    /// <para>Upstream: <c>Lily_parser::closures_</c>, consulted by
    /// <c>parse_embedded_scheme</c> so that a <c>#</c> or <c>$</c> in re-parsed text
    /// evaluates in its ORIGINAL lexical environment rather than being read afresh.</para>
    /// </summary>
    public object Closures { get; set; } = Nil.Instance;

    /// <summary>
    /// Gets or sets the location every music expression this session builds should carry,
    /// overriding the one the text implies.
    /// <para>Upstream: <c>Lily_lexer::override_input</c>, set from
    /// <c>ly:parser-clone</c>'s location argument. It is what makes music built inside
    /// <c>#{ ... #}</c> blame the <c>.ly</c> line the block was written on rather than a
    /// position in a string nobody can see.</para>
    /// </summary>
    public Input OverrideOrigin { get; set; }

    /// <inheritdoc/>
    public void NoteExtraSourceFile(string fileName)
    {
        if (!string.IsNullOrEmpty(fileName))
        {
            OpenSource(fileName, string.Empty);
        }
    }

    /// <summary>
    /// Publishes this session in the <c>%parser</c> fluid for the duration of an action,
    /// and restores what was there before.
    /// <para>Upstream the fluid is set once per <c>Lily_parser</c> run and read by every
    /// <c>ly:parser-*</c> binding; the port scopes it, because several sessions can exist
    /// in one process and the innermost is the one those bindings mean.</para>
    /// </summary>
    /// <param name="action">What to run.</param>
    /// <returns>What the action returned.</returns>
    public object AsCurrentParser(Func<object> action)
    {
        Fluid fluid = ParserPrimitives.ParserFluidOf(_interpreter);
        if (fluid == null)
        {
            return action();
        }

        object saved = fluid.Value;
        fluid.Value = this;
        try
        {
            return action();
        }
        finally
        {
            fluid.Value = saved;
        }
    }
}
