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
    LexerLookup ILexerHost.LookupMarkupCommand(string word, out IReadOnlyList<string> predicates)
    {
        predicates = new List<string>();

        object value = LookupIdentifier(word);
        if (value is DefaultArgument || !IsMarkupFunction(value))
        {
            return LexerLookup.None;
        }

        // The signature is a procedure property on the markup command; the scanner
        // turns it into the EXPECT_* announcement.
        List<string> declared = new List<string>();
        object signature = Call(LilyImport("markup-command-signature"), value);
        foreach (object predicate in Pair.ToList(signature))
        {
            declared.Add(PredicateName(predicate));
        }

        predicates = declared;
        return new LexerLookup(
            IsMarkupListFunction(value) ? "MARKUP_LIST_FUNCTION" : "MARKUP_FUNCTION", value);
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
    public object ParseEmbeddedScheme(string input, int position, out int consumed)
    {
        // Upstream hands the input port straight to Guile's reader, which leaves the
        // port positioned after the datum. LilyScheme's reader starts at 0, so the
        // port reads a SUFFIX and reports how much of it was used — the same
        // answer, and the only visible difference is that a datum's recorded column
        // is relative to the expression rather than to the line. Recorded in
        // PORT-COVERAGE.
        SchemeReader reader = new SchemeReader(input.Substring(position), "<embedded>");
        object datum = reader.ReadDatum();
        consumed = reader.Position;
        return datum;
    }

    private object LookupLily(string name)
    {
        Variable variable = _lilyModule.Lookup(Symbol.Intern(name));
        return variable?.GetValue();
    }
}
