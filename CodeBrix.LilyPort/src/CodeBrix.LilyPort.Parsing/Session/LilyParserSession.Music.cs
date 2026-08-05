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

using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Session; //was previously: lily/music.cc (make_music_with_input), lily/lily-parser.cc, lily/parser.yy (loc_on_copy);

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <content>
/// The music, predicate and output-definition halves of the host: everything that
/// answers from the Engine's own types or from the vendored Scheme layer's tables.
/// </content>
public sealed partial class LilyParserSession
{
    // ------ music (MY_MAKE_MUSIC, and the operations over it) ------

    /// <inheritdoc/>
    public object MakeMusic(string name, SourceSpan location)
    {
        // lily/music.cc: make_music_with_input calls the SCHEME make-music, which is
        // what gives the object its type's properties out of the music-descriptions
        // table, and only then stamps the location. Building a MusicObject directly
        // here would produce music with no type properties at all — the
        // half-reproduced-declaration shape the engine sessions kept finding.
        object music = Call(LilyImport("make-music"), Symbol.Intern(name));
        if (music is MusicObject made)
        {
            made.SetSpot(location);
        }

        return music;
    }

    /// <inheritdoc/>
    public void SetMusicProperty(object music, string name, object value)
        => ((MusicObject)music).SetProperty(name, value);

    /// <inheritdoc/>
    public object GetMusicProperty(object music, string name)
        => ((MusicObject)music).GetProperty(name);

    /// <inheritdoc/>
    public bool IsMusic(object value) => value is MusicObject;

    /// <inheritdoc/>
    public bool IsMusicType(object music, string type)
        => music is MusicObject m && m.IsMusicType(type);

    /// <inheritdoc/>
    public object CloneMusic(object music)
        => music is MusicObject m ? m.Clone() : music;

    /// <inheritdoc/>
    public void SetMusicSpot(object music, SourceSpan location)
    {
        if (music is MusicObject m)
        {
            m.SetSpot(location);
        }
    }

    /// <inheritdoc/>
    public object LocOnCopy(object value, SourceSpan location)
    {
        // parser.yy's epilogue: clone whatever carries a location and stamp the copy;
        // return everything else untouched. The clone matters for the same reason the
        // identifier lookups clone — one \foo serves every use in the file.
        switch (value)
        {
            case MusicObject music:
                MusicObject musicCopy = music.Clone();
                musicCopy.SetSpot(location);
                return musicCopy;

            case Score score:
                Score scoreCopy = CloneScore(score);
                scoreCopy.SetSpot(location);
                return scoreCopy;

            case Book book:
                Book bookCopy = CloneBook(book);
                bookCopy.SetSpot(location);
                return bookCopy;

            case OutputDef definition:
                OutputDef definitionCopy = definition.Clone();
                definitionCopy.SetSpot(location);
                return definitionCopy;

            case ContextDef contextDef:
                return contextDef.Clone();

            case ContextMod contextMod:
                return new ContextMod(contextMod);

            default:
                return value;
        }
    }

    /// <inheritdoc/>
    public object ScorifyMusic(object music)
        => Call(LilyImport("scorify-music"), music);

    /// <inheritdoc/>
    public object ConstructChordElements(object pitch, object duration, object modifications)
        => Call(LilyImport("construct-chord-elements"), pitch, duration, modifications);

    // ------ predicates that live in the Scheme layer ------

    /// <inheritdoc/>
    public bool IsMarkup(object value) => SchemeTruth(LilyImport("markup?"), value);

    /// <inheritdoc/>
    public bool IsMarkupList(object value) => SchemeTruth(LilyImport("markup-list?"), value);

    /// <inheritdoc/>
    public bool IsMarkupFunction(object value)
        => SchemeTruth(LilyImport("markup-function?"), value)
           || IsMarkupListFunction(value);

    /// <summary>
    /// Answers whether a value is a markup-LIST command, which decides between the
    /// <c>MARKUP_LIST_FUNCTION</c> and <c>MARKUP_FUNCTION</c> tokens.
    /// <para>Upstream: <c>Lily::markup_list_function_p</c>.</para>
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> for a markup-list command.</returns>
    private bool IsMarkupListFunction(object value)
        => SchemeTruth(LilyImport("markup-list-function?"), value);

    /// <inheritdoc/>
    public bool IsScore(object value) => value is Score;

    /// <inheritdoc/>
    public bool BookHasPaper(object book) => book is Book b && b.Paper != null;

    /// <inheritdoc/>
    public bool IsKey(object value)
        => value is Symbol
           || (SchemeNumber.IsNumber(value)
               && SchemeNumber.IsExact(value)
               && SchemeNumber.IsInteger(value)
               && SchemeNumber.Compare(value, 0L) >= 0);

    /// <inheritdoc/>
    public bool IsKeyList(object value)
    {
        // Lily::key_list_p — (and (list? x) (every key? x)). Answered here rather than
        // through the Scheme layer because IsKey already is, and the two must agree.
        if (value is Nil)
        {
            return true;
        }

        object p = value;
        for (; p is Pair pair; p = pair.Cdr)
        {
            if (!IsKey(pair.Car))
            {
                return false;
            }
        }

        return p is Nil;
    }

    /// <inheritdoc/>
    public bool IsGrobSymbol(object value)
    {
        // Upstream: scm_object_property (sym, ly_symbol2scm ("is-grob?")) — an OBJECT
        // PROPERTY on the symbol, set by scm/define-grobs.scm's
        // (set-object-property! name-sym 'is-grob? #t) for every grob it declares. There
        // is no `symbol-is-grob?` procedure anywhere in LilyPond; an earlier pass invented
        // the name, and the LilyImport for it threw the moment property-init.ly reached a
        // grob property path.
        if (!(value is Symbol symbol))
        {
            return false;
        }

        Variable accessor = _interpreter.GuileModule.Lookup(Symbol.Intern("object-property"));
        if (accessor == null)
        {
            return false;
        }

        object answer = Call(accessor.GetValue(), symbol, Symbol.Intern("is-grob?"));
        return !(answer is bool flag && !flag);
    }

    /// <inheritdoc/>
    public bool IsScale(object value) => SchemeTruth(LilyImport("scale?"), value);

    /// <inheritdoc/>
    public object ScaleToFactor(object value) => Call(LilyImport("scale->factor"), value);

    /// <inheritdoc/>
    public void DefineMarkupCommand(object name, object function)
        => Call(LilyImport("define-markup-command-internal"), name, function, false);

    /// <summary>
    /// Calls a Scheme predicate and answers <c>scm_is_true</c> over its result.
    /// <para>Everything is true except <c>#f</c> — which is <em>not</em> the
    /// <c>from_scm&lt;bool&gt;</c> rule the rule actions use for
    /// <c>exclamations</c>/<c>questions</c>; that one is exactly <c>#t</c>, and the
    /// difference is deliberate on both sides (see PORT-COVERAGE).</para>
    /// </summary>
    /// <param name="predicate">The predicate; a missing one answers no.</param>
    /// <param name="value">The value to test.</param>
    /// <returns>The predicate's truth.</returns>
    private bool SchemeTruth(object predicate, object value)
    {
        if (predicate == null)
        {
            return false;
        }

        object result = Call(predicate, value);
        return !(result is bool flag && !flag);
    }

    /// <summary>
    /// Returns a music function's signature, or <see langword="null"/> when the value
    /// is not one.
    /// <para>Upstream: <c>unsmob&lt;Music_function&gt;(sid)-&gt;get_signature ()</c>.
    /// The port has no <c>Music_function</c> type; the vendored Scheme layer marks a
    /// music function with a procedure property, which is the same protocol
    /// <c>procedure-arguments</c> already reads (§7b).</para>
    /// </summary>
    /// <param name="value">The candidate.</param>
    /// <returns>The signature list, or null.</returns>
    private object MusicFunctionSignature(object value)
    {
        object predicate = LookupLily("ly:music-function?");
        if (predicate == null || !SchemeTruth(predicate, value))
        {
            return null;
        }

        object accessor = LookupLily("ly:music-function-signature");
        return accessor == null ? null : Call(accessor, value);
    }

    /// <summary>
    /// Names the token a music function lexes as, from its signature's head — the
    /// RETURN predicate.
    /// <para>Upstream: <c>scan_scm_id</c> (lexer.ll 993). <c>ly:music?</c> gives a
    /// <c>MUSIC_FUNCTION</c>, <c>ly:event?</c> an <c>EVENT_FUNCTION</c>, and any other
    /// procedure a plain <c>SCM_FUNCTION</c>.</para>
    /// </summary>
    /// <param name="signature">The signature.</param>
    /// <returns>The terminal's name.</returns>
    private string FunctionTokenName(object signature)
    {
        object head = signature is Pair pair ? pair.Car : null;
        if (head is Pair headPair)
        {
            head = headPair.Car;
        }

        if (ReferenceEquals(head, LookupLily("ly:music?")))
        {
            return "MUSIC_FUNCTION";
        }

        return ReferenceEquals(head, LookupLily("ly:event?")) ? "EVENT_FUNCTION" : "SCM_FUNCTION";
    }

    // ------ output definitions (Lily_parser's scope handling) ------

    /// <inheritdoc/>
    public void AddOutputDefScope(OutputDef definition)
    {
        // Upstream: parser->lexer_->add_scope (p->scope_). The SAME add_scope every other
        // block uses, on the definition's own module.
        //
        // An earlier pass kept output-definition scopes in a list of their own, because
        // the scope was a dictionary rather than a module. That left every \paper and
        // \layout block UNBALANCED — one push on the private list, one pop from the module
        // stack at the closing brace — so a session emptied its scope stack on the first
        // output definition it read, and every assignment written inside the block landed
        // outside it. Both halves are gone now that OutputDef.Scope is a real module.
        AddScope(definition.Scope);
    }

    // ------ the two engine types with no Clone of their own ------

    /// <summary>
    /// Copies a score, which upstream does with <c>Score::clone</c>.
    /// <para>The Engine's <c>Score</c> has no copy constructor yet — the first-light
    /// path never needed one — so the copy is made member by member here, and it is
    /// SHALLOW over the music and header exactly as upstream's is.</para>
    /// </summary>
    /// <param name="score">The score.</param>
    /// <returns>The copy.</returns>
    private static Score CloneScore(Score score)
    {
        Score copy = new Score();
        copy.SetMusic(score.GetMusic());
        copy.SetHeader(score.GetHeader());
        copy.SetSpot(score.Origin);
        copy.ErrorFound = score.ErrorFound;
        foreach (OutputDef definition in score.Defs)
        {
            copy.AddOutputDef(definition);
        }

        return copy;
    }

    /// <summary>
    /// Copies a book, which upstream does with <c>Book::clone</c>.
    /// <para>Same reasoning as <see cref="CloneScore"/>: shallow over the score and
    /// bookpart lists, which is what upstream's copy constructor does.</para>
    /// </summary>
    /// <param name="book">The book.</param>
    /// <returns>The copy.</returns>
    private static Book CloneBook(Book book)
        => new Book
        {
            Paper = book.Paper,
            Header = book.Header,
            Scores = book.Scores,
            Bookparts = book.Bookparts,
        };
}
