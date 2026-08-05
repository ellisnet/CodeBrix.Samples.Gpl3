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
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyPort.Parsing.Lexing;
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Session; //was previously: lily/lily-lexer.cc, lily/lexer.ll (the lexer host half);

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <content>
/// The <c>Lily_lexer</c> half: the mode stack, the pitch-name tables, the keyword and
/// identifier lookups, and the embedded-Scheme reader.
/// </content>
public sealed partial class LilyParserSession
{
    // ------ the mode stack (Lily_lexer::push_*_state / pop_state) ------

    /// <inheritdoc/>
    public void PushNoteState()
    {
        // lexer.ll 894: push the CURRENT note-name alist, then the state. The alist is
        // read at push time, not at scan time, which is what makes \language inside a
        // score affect only what follows it.
        PushPitchNames(LookupLily("pitchnames"), LexerState.Notes);
        Scanner?.PushState(LexerState.Notes);
    }

    /// <inheritdoc/>
    public void PushChordState()
    {
        PushPitchNames(LookupLily("pitchnames"), LexerState.Chords);
        Scanner?.PushState(LexerState.Chords);
    }

    /// <inheritdoc/>
    public void PushDrumState()
    {
        // lexer.ll 902: the drum table comes from the PARSER's scope chain
        // (drumPitchNames is assigned in ly/drumpitch-init.ly), not from (lily) —
        // and the state pushed is NOTES, not a drum state of its own.
        PushPitchNames(LookupIdentifier("drumPitchNames"), LexerState.Notes);
        Scanner?.PushState(LexerState.Notes);
    }

    /// <inheritdoc/>
    public void PushLyricState() => Scanner?.PushState(LexerState.Lyrics);

    /// <inheritdoc/>
    public void PushFiguredBassState() => Scanner?.PushState(LexerState.Figures);

    /// <inheritdoc/>
    public void PushMarkupState() => Scanner?.PushState(LexerState.Markup);

    /// <inheritdoc/>
    public void PushInitialState() => Scanner?.PushState(LexerState.Initial);

    /// <inheritdoc/>
    public void PopLexerState()
    {
        // lexer.ll 908: the pitch-name stack is popped only when LEAVING notes or
        // chords, which is why it is kept beside the state that pushed it rather than
        // in step with the mode stack.
        LexerState state = CurrentLexerState;
        if ((state == LexerState.Notes || state == LexerState.Chords)
            && _pitchNameTables.Count > 0)
        {
            _pitchNameTables.RemoveAt(_pitchNameTables.Count - 1);
            _pitchNameStates.RemoveAt(_pitchNameStates.Count - 1);
        }

        Scanner?.PopState();
    }

    private void PushPitchNames(object alist, LexerState state)
    {
        _pitchNameTables.Add(alist);
        _pitchNameStates.Add(state);
    }

    private LexerState CurrentLexerState => Scanner?.State ?? LexerState.Initial;

    /// <inheritdoc/>
    public bool IsNoteState => CurrentLexerState == LexerState.Notes;

    /// <inheritdoc/>
    public bool IsChordState => CurrentLexerState == LexerState.Chords;

    /// <inheritdoc/>
    public bool IsLyricState => CurrentLexerState == LexerState.Lyrics;

    /// <inheritdoc/>
    public void SetChordModifiers(object modifiers) => _chordModifiers = modifiers;

    // ------ word scanning (Lily_lexer::scan_word) ------

    /// <inheritdoc/>
    public LexerLookup ScanWord(object word)
        => ScanWord(CurrentLexerState, word as Symbol);

    /// <inheritdoc/>
    LexerLookup ILexerHost.ScanWord(LexerState state, string word)
        => ScanWord(state, Symbol.Intern(word));

    private LexerLookup ScanWord(LexerState state, Symbol word)
    {
        // lexer.ll 1047. Only note and chord modes have a pitch-name table; in every
        // other mode a bare word is just a word, which is why \markup { c } is the
        // letter c and not a pitch.
        if (word == null || (state != LexerState.Notes && state != LexerState.Chords))
        {
            return LexerLookup.None;
        }

        if (_pitchNameTables.Count > 0)
        {
            object found = AssociationValue(_pitchNameTables[_pitchNameTables.Count - 1], word);
            if (found != null)
            {
                if (found is Pitch)
                {
                    return new LexerLookup(
                        state == LexerState.Notes ? "NOTENAME_PITCH" : "TONICNAME_PITCH", found);
                }

                if (found is Symbol)
                {
                    return new LexerLookup("DRUM_PITCH", found);
                }
            }
        }

        if (state == LexerState.Chords)
        {
            object modifier = AssociationValue(_chordModifiers, word);
            if (modifier != null)
            {
                return new LexerLookup("CHORD_MODIFIER", modifier);
            }
        }

        return LexerLookup.None;
    }

    /// <summary>
    /// Looks a key up in an alist or a hash table, whichever the Scheme layer supplied.
    /// <para>Upstream both tables are Guile hash tables reached with
    /// <c>scm_hashq_get_handle</c>; the port's Scheme layer builds
    /// <c>pitchnames</c> as an ALIST and <c>chordmodifiers</c> as a hash table, so both
    /// shapes are accepted rather than one being assumed.</para>
    /// </summary>
    /// <param name="table">The table.</param>
    /// <param name="key">The key.</param>
    /// <returns>The value, or <see langword="null"/> when absent.</returns>
    private static object AssociationValue(object table, Symbol key)
    {
        if (table is SchemeHashTable hash)
        {
            Pair handle = hash.GetHandle(key);
            return handle?.Cdr;
        }

        for (object p = table; p is Pair pair; p = pair.Cdr)
        {
            if (pair.Car is Pair entry && ReferenceEquals(entry.Car, key))
            {
                return entry.Cdr;
            }
        }

        return null;
    }

    // ------ escaped words (Lily_lexer::lookup_keyword, scan_escaped_word) ------

    /// <inheritdoc/>
    LexerLookup ILexerHost.LookupKeyword(string word)
    {
        string terminal = LilyKeywords.Lookup(word);
        return terminal == null ? LexerLookup.None : new LexerLookup(terminal, null);
    }

    /// <inheritdoc/>
    LexerLookup ILexerHost.LookupIdentifier(string word)
    {
        object value = LookupIdentifier(word);
        if (value is DefaultArgument)
        {
            return LexerLookup.None;
        }

        return IdentifierToken(value);
    }

    /// <inheritdoc/>
    public LexerLookup ScanSchemeValue(object value)
        => value is DefaultArgument ? LexerLookup.None : IdentifierToken(value);

    /// <inheritdoc/>
    public LexerLookup MarkupFunctionToken(object value, out IReadOnlyList<MarkupPredicate> predicates)
    {
        predicates = EmptyPredicates;
        if (!(value is Procedure) && !(value is IApplicable))
        {
            return LexerLookup.None;
        }

        object signature = Call(LilyImport("markup-command-signature"), value);
        if (signature == null || signature is bool)
        {
            return LexerLookup.None;
        }

        List<MarkupPredicate> declared = new List<MarkupPredicate>();
        foreach (object predicate in Pair.ToList(signature))
        {
            declared.Add(new MarkupPredicate(PredicateName(predicate), predicate));
        }

        predicates = declared;
        return new LexerLookup(
            IsMarkupListFunction(value) ? "MARKUP_LIST_FUNCTION" : "MARKUP_FUNCTION", value);
    }

    private static readonly IReadOnlyList<MarkupPredicate> EmptyPredicates = new List<MarkupPredicate>();

    /// <summary>
    /// Decides which token an identifier's VALUE lexes as, and hands back the value the
    /// token should carry.
    /// <para>Upstream: <c>Lily_lexer::try_special_identifiers</c>
    /// (<c>parser.yy</c> 4394) behind <c>identifier_type</c>, plus
    /// <c>scan_scm_id</c>'s music-function branch, which the port expresses through
    /// <see cref="LexerLookup.FunctionSignature"/> so the scanner announces the
    /// signature itself.</para>
    /// <para>
    /// EVERY BRANCH THAT UPSTREAM CLONES, CLONES HERE. One <c>\foo</c> serves every
    /// use in the file, and a use that mutated the stored value — a music expression
    /// given a location, an output definition given a variable — would change what
    /// every later <c>\foo</c> means.
    /// </para>
    /// </summary>
    /// <param name="value">The identifier's value.</param>
    /// <returns>The token and its value.</returns>
    private LexerLookup IdentifierToken(object value)
    {
        // scan_scm_id's music-function branch, before every other test: a music
        // function is recognised by CARRYING A SIGNATURE, and the signature's head
        // (its RETURN predicate) decides which of the three function tokens it is.
        object signature = MusicFunctionSignature(value);
        if (signature != null)
        {
            return new LexerLookup(FunctionTokenName(signature), value, signature);
        }

        if (value is Book book)
        {
            return new LexerLookup("BOOK_IDENTIFIER", CloneBook(book));
        }

        if (SchemeNumber.IsNumber(value))
        {
            return new LexerLookup("NUMBER_IDENTIFIER", value);
        }

        if (value is ContextDef contextDef)
        {
            return new LexerLookup("SCM_IDENTIFIER", contextDef.Clone());
        }

        if (value is ContextMod contextMod)
        {
            return new LexerLookup("SCM_IDENTIFIER", new ContextMod(contextMod));
        }

        if (value is MusicObject music)
        {
            MusicObject copy = music.Clone();
            return new LexerLookup(
                copy.IsMusicType("post-event") ? "EVENT_IDENTIFIER" : "MUSIC_IDENTIFIER", copy);
        }

        if (value is Pitch)
        {
            return new LexerLookup("PITCH_IDENTIFIER", value);
        }

        if (value is Duration)
        {
            return new LexerLookup("DURATION_IDENTIFIER", value);
        }

        if (value is OutputDef outputDef)
        {
            return new LexerLookup("SCM_IDENTIFIER", outputDef.Clone());
        }

        if (value is Score score)
        {
            return new LexerLookup("SCM_IDENTIFIER", CloneScore(score));
        }

        // A property path: ((key . rest) ...) — what `\foo.bar` was assigned as.
        if (value is Pair pair && pair.Car is Pair head && IsKey(head.Car))
        {
            return new LexerLookup("LOOKUP_IDENTIFIER", value);
        }

        return new LexerLookup("SCM_IDENTIFIER", value);
    }

    /// <inheritdoc/>
    LexerLookup ILexerHost.LookupMarkupCommand(
        string word, out IReadOnlyList<MarkupPredicate> predicates)
    {
        predicates = new List<MarkupPredicate>();

        // Upstream: lexer.ll's <markup>{COMMAND} rule calls lookup_markup_command, which
        // is scm/markup-macros.scm's `lookup-markup-command` —
        //
        //   (module-ref (current-module) (string->symbol (format #f "~a-markup" code)))
        //
        // — and falls back to `lookup-markup-list-command` on `<code>-markup-list`. THE
        // SUFFIX IS THE WHOLE POINT: define-markup-command binds `hspace-markup`, never
        // `hspace`. An earlier pass looked the bare word up, so every markup command in
        // the vendored layer read as an unknown command, and the only tests that covered
        // this path used a scripted host with commands registered under bare names.
        object command = LookupIdentifier(word + "-markup");
        bool isList = false;
        if (command is DefaultArgument || !SchemeTruth(LilyImport("markup-function?"), command))
        {
            command = LookupIdentifier(word + "-markup-list");
            isList = true;
            if (command is DefaultArgument || !IsMarkupListFunction(command))
            {
                return LexerLookup.None;
            }
        }

        // The signature is a procedure property on the markup command; the scanner
        // turns it into the EXPECT_* announcement. Each entry carries BOTH the name the
        // token choice is made from and the predicate itself, which EXPECT_SCM hands to
        // the arglist rules.
        List<MarkupPredicate> declared = new List<MarkupPredicate>();
        object signature = Call(LilyImport("markup-command-signature"), command);
        foreach (object predicate in Pair.ToList(signature))
        {
            declared.Add(new MarkupPredicate(PredicateName(predicate), predicate));
        }

        predicates = declared;
        return new LexerLookup(isList ? "MARKUP_LIST_FUNCTION" : "MARKUP_FUNCTION", command);
    }

    /// <summary>
    /// Names a signature predicate the way the scanner's <c>EXPECT_*</c> mapping wants.
    /// <para>Upstream compares the predicate against <c>Lily::markup_p</c> and
    /// <c>Lily::markup_list_p</c> BY IDENTITY (<c>push_markup_predicates</c>,
    /// lexer.ll 920); the port's scanner takes a name, so the identity comparison
    /// happens here and everything else is announced as a plain <c>EXPECT_SCM</c>
    /// carrying the predicate.</para>
    /// </summary>
    /// <param name="predicate">The predicate procedure.</param>
    /// <returns>Its name for the announcement.</returns>
    private string PredicateName(object predicate)
    {
        if (ReferenceEquals(predicate, LookupLily("markup?")))
        {
            return "markup?";
        }

        return ReferenceEquals(predicate, LookupLily("markup-list?")) ? "markup-list?" : "scm?";
    }

    // ------ embedded Scheme (Lily_lexer's #{ } / # reader hand-off) ------

    /// <inheritdoc/>
    public object ParseEmbeddedScheme(string input, int position, SourceSpan start, out int consumed)
    {
        // Upstream hands the input port straight to Guile's reader, which leaves the
        // port positioned after the datum. LilyScheme's reader starts at 0, so the
        // port reads a SUFFIX and reports how much of it was used — the same
        // answer, and the only visible difference is that a datum's recorded column
        // is relative to the expression rather than to the line. Recorded in
        // PORT-COVERAGE.

        // `#@' / `$@' — the multiple-values prefix. The '@' is consumed before the
        // datum is read and the form is wrapped in (apply values FORM) afterwards, so
        // the value the token carries is a MultipleValues that eval_scm can spread into
        // extra tokens.
        bool multiple = position < input.Length && input[position] == '@';
        int readFrom = multiple ? position + 1 : position;

        object datum;
        SchemeReader reader = new SchemeReader(input.Substring(readFrom), start.FileName);
        try
        {
            datum = reader.ReadDatum();
            consumed = (readFrom - position) + reader.Position;
        }
        catch (Exception error)
        {
            // parse_embedded_scheme's scm_c_catch, which is the whole reason the
            // function is written around one: a `#(...)' the reader cannot make sense of
            // must become a LOCATED diagnostic and let the parse carry on, so that a
            // file reports every one of its errors instead of the first. The pre-unwind
            // handler prints where the expression BEGAN — not where the reader gave up,
            // which is usually inside a construct the author did not think they were
            // writing — and the post-unwind handler returns SCM_UNDEFINED, which the
            // lexer turns into error_level_ = 1.
            ParserError(start, SchemeErrorText(error));

            // ZERO characters are consumed, and that is upstream's behaviour rather than
            // an approximation of it: `Parse_start::parsed_` is only `.set()` on the
            // success path, so after a throw it is still a default-constructed Input,
            // `parsed.size ()` is 0, and `skip_chars (0)` leaves the scan position right
            // after the `#'. The text is then re-scanned as ordinary LilyPond, which
            // produces further syntax errors — and is exactly right, because the reader
            // has no idea how much of what follows was meant to be Scheme. Swallowing to
            // the reader's stopping point instead would eat the rest of the file, since
            // an unterminated list runs to end of input.
            consumed = 0;
            return DefaultArgument.Instance;
        }

        // SCM_EOF_OBJECT_P: `#' with nothing after it. Upstream returns SCM_UNDEFINED
        // without a diagnostic of its own — the grammar reports the missing expression.
        if (ReferenceEquals(datum, EofObject.Instance))
        {
            return DefaultArgument.Instance;
        }

        // The closures lookup. A parser CLONE carries an alist of offset-to-thunk built
        // by the `#{ ... #}' reader, and a `#' at a recorded offset evaluates the THUNK
        // rather than the text — which is what lets embedded Scheme see the lexical
        // environment of the Scheme code the block was written in, the entire point of
        // the construct. Only while reading the top input: an \include'd file has its
        // own offsets and nothing to do with the clone's.
        if (Scanner != null && Scanner.IncludeDepth < 2)
        {
            object closure = ClosureAt(position);
            if (closure != null)
            {
                return closure;
            }
        }

        return multiple
            ? Pair.List(Symbol.Intern("apply"), Symbol.Intern("values"), datum)
            : datum;
    }

    /// <summary>
    /// Looks a precompiled closure up by the offset of the expression it stands for —
    /// <c>scm_assv_ref (parser-&gt;closures_, offset)</c>, whose <c>eqv?</c> over two
    /// exact integers is an equality test here.
    /// </summary>
    /// <param name="offset">The offset of the first character after the <c>#</c>.</param>
    /// <returns>The thunk, or <see langword="null"/> when nothing is recorded there.</returns>
    private object ClosureAt(int offset)
    {
        for (object p = Closures; p is Pair pair; p = pair.Cdr)
        {
            if (pair.Car is Pair entry && entry.Car is long key && key == offset)
            {
                return entry.Cdr;
            }
        }

        return null;
    }

    /// <summary>
    /// Names what a Scheme-level failure was, for a parser diagnostic.
    /// <para>Upstream prints the error with <c>scm_print_exception</c> under the heading
    /// "Guile signaled an error for the expression beginning here"; the port keeps the
    /// heading and appends what the interpreter said, because the two together are what
    /// makes the message actionable.</para>
    /// </summary>
    /// <param name="error">The failure.</param>
    /// <returns>The message.</returns>
    private static string SchemeErrorText(Exception error)
        => "Guile signaled an error for the expression beginning here: " + error.Message;

    private object LookupLily(string name)
    {
        Variable variable = _lilyModule.Lookup(Symbol.Intern(name));
        return variable?.GetValue();
    }
}
