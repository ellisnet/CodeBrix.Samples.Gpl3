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

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyPort.Parsing.Lexing;
using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (prologue FINISH_MAKE_SYNTAX macro; epilogue: make_music_from_simple, make_duration, make_chord_elements);

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <content>
/// The helpers RULE ACTION GROUP 6 brought in: the <c>FINISH_MAKE_SYNTAX</c> macro
/// (the applying half of RAG5's <c>START_MAKE_SYNTAX</c>), and the epilogue's
/// <c>make_music_from_simple</c> with the two helpers it reaches,
/// <c>make_duration</c> and <c>make_chord_elements</c>. As everywhere in this class,
/// list work is local and everything upstream reaches through <c>Lily_parser</c>,
/// <c>Lily_lexer</c> or a <c>Lily::</c> import goes through the host.
/// </content>
internal static partial class ParserActionHelpers
{
    /// <summary>
    /// Applies a constructor list built by <c>START_MAKE_SYNTAX</c> (RAG5's
    /// <c>context_prefix</c>) to its remaining arguments, with a location.
    /// <para>Upstream: the <c>FINISH_MAKE_SYNTAX (start, location, ...)</c> macro —
    /// <c>make_syntax (parser, Guile_user::apply, location, scm_car (start),
    /// scm_append_x (ly_list (scm_cdr (start), ly_list (...))))</c>. The two-list
    /// <c>scm_append_x</c> is the destructive
    /// <see cref="AppendInPlace(object, object)"/>, exactly as
    /// upstream re-points the start list's own last pair; the application itself is
    /// <see cref="IParserHost.ApplySyntax"/>.</para>
    /// </summary>
    /// <param name="host">The parser host.</param>
    /// <param name="start">The <c>(constructor arg...)</c> list the prefix built.</param>
    /// <param name="location">The span the constructed music is located at.</param>
    /// <param name="arguments">The arguments appended after the start list's own.</param>
    /// <returns>The constructed value.</returns>
    internal static object FinishMakeSyntax(
        IParserHost host, object start, SourceSpan location, params object[] arguments)
    {
        Pair pair = (Pair)start;
        return host.ApplySyntax(
            pair.Car, location, AppendInPlace(pair.Cdr, Pair.List(arguments)));
    }

    /// <summary>
    /// Interprets a "simple" value as music where possible: music passes through, a
    /// symbol is scanned against the current mode's pitch and drum tables, a pitch or
    /// duration in note mode becomes a NoteEvent, a markup in lyric mode a lyric
    /// event, and a pitch in chord mode an event chord — anything else comes back
    /// unchanged for the caller to judge.
    /// <para>Upstream: <c>make_music_from_simple</c> in <c>parser.yy</c>'s epilogue.
    /// The <c>default_duration_.smobbed_copy ()</c> reads are
    /// <see cref="IParserHost.DefaultDuration"/> boxed — a fresh copy each time, like
    /// the smob copies.</para>
    /// </summary>
    /// <param name="host">The parser host.</param>
    /// <param name="location">The <c>loc</c> the made music is located at.</param>
    /// <param name="simple">The value to interpret.</param>
    /// <returns>The music, or the value itself when no interpretation fits.</returns>
    internal static object MakeMusicFromSimple(IParserHost host, SourceSpan location, object simple)
    {
        // if (unsmob<Music> (simple)) return simple;
        if (simple is MusicObject)
        {
            return simple;
        }

        // if (scm_is_symbol (simple)) { ... switch (parser->lexer_->scan_word ...) }
        if (simple is Symbol)
        {
            LexerLookup found = host.ScanWord(simple);
            switch (found.TokenName)
            {
                case "DRUM_PITCH":
                {
                    object drum = host.MakeMusic("NoteEvent", location);
                    host.SetMusicProperty(drum, "duration", host.DefaultDuration);
                    host.SetMusicProperty(drum, "drum-type", found.Value);
                    return drum;
                }

                case "NOTENAME_PITCH":
                case "TONICNAME_PITCH":
                    // Take the parsed pitch
                    simple = found.Value;
                    break;

                // Don't scan CHORD_MODIFIER etc.
            }
        }

        if (host.IsNoteState)
        {
            if (simple is Pitch)
            {
                object note = host.MakeMusic("NoteEvent", location);
                host.SetMusicProperty(note, "duration", host.DefaultDuration);
                host.SetMusicProperty(note, "pitch", simple);
                return note;
            }

            object d = simple;
            if (SchemeNumber.IsInteger(simple))
            {
                d = MakeDuration(simple, 0, DefaultArgument.Instance);
            }

            if (d is Duration)
            {
                object note = host.MakeMusic("NoteEvent", location);
                host.SetMusicProperty(note, "duration", d);
                return note;
            }

            return simple;
        }
        else if (host.IsLyricState)
        {
            if (host.IsMarkup(simple))
            {
                return host.MakeSyntax(
                    "lyric-event", location, simple, host.DefaultDuration);
            }
        }
        else if (host.IsChordState)
        {
            if (simple is Pitch)
            {
                return host.MakeSyntax(
                    "event-chord",
                    location,
                    MakeChordElements(
                        host, location, simple, host.DefaultDuration, Nil.Instance));
            }
        }

        return simple;
    }

    /// <summary>
    /// Makes a duration: an existing <see cref="Duration"/> gains dots and a scaling
    /// factor, and a power-of-two integer becomes the duration of that denominator —
    /// anything else answers <c>SCM_UNDEFINED</c>.
    /// <para>Upstream: <c>make_duration</c> in <c>parser.yy</c>'s epilogue. Its
    /// defaulted parameters (<c>dots = 0, factor = SCM_UNDEFINED</c>) are passed
    /// explicitly here; RAG16's duration rules are the callers that vary them.</para>
    /// </summary>
    /// <param name="d">A boxed <see cref="Duration"/> or an integer.</param>
    /// <param name="dots">How many dots to add.</param>
    /// <param name="factor">The scaling factor, or
    /// <see cref="DefaultArgument.Instance"/> for none.</param>
    /// <returns>The boxed duration, or <see cref="DefaultArgument.Instance"/>.</returns>
    internal static object MakeDuration(object d, int dots, object factor)
    {
        Duration k;

        if (d is Duration dur)
        {
            if (dots == 0 && factor is DefaultArgument)
            {
                return d;
            }

            k = dur;
            if (dots != 0)
            {
                k = new Duration(k.DurationLog, k.DotCount + dots).Compressed(k.Factor);
            }
        }
        else
        {
            int t = SchemeConvert.ToInt(d, "make-duration");
            if (t > 0 && (t & (t - 1)) == 0)
            {
                k = new Duration(IntLog2(t), dots);
            }
            else
            {
                return DefaultArgument.Instance;
            }
        }

        if (!(factor is DefaultArgument))
        {
            k = k.Compressed(SchemeConvert.ToRational(factor, "make-duration"));
        }

        return k;
    }

    /// <summary>
    /// Builds the note events of a chord and stamps each with a location.
    /// <para>Upstream: <c>make_chord_elements</c> in <c>parser.yy</c>'s epilogue —
    /// <c>Lily::construct_chord_elements</c> (the vendored
    /// <c>scm/chord-entry.scm</c>'s <c>construct-chord-elements</c>, behind
    /// <see cref="IParserHost.ConstructChordElements"/>) followed by
    /// <c>set_spot (loc)</c> over every element.</para>
    /// </summary>
    /// <param name="host">The parser host.</param>
    /// <param name="location">The span every element is stamped with.</param>
    /// <param name="pitch">The chord's root pitch.</param>
    /// <param name="duration">The chord's duration.</param>
    /// <param name="modificationList">The chord modifications.</param>
    /// <returns>The list of elements.</returns>
    internal static object MakeChordElements(
        IParserHost host, SourceSpan location, object pitch, object duration, object modificationList)
    {
        object result = host.ConstructChordElements(pitch, duration, modificationList);
        for (object s = result; s is Pair pair; s = pair.Cdr)
        {
            // upstream: unsmob<Music> (scm_car (s))->set_spot (loc) — a non-music
            // element would be a null dereference there and an InvalidCastException
            // here: same failure, new spelling.
            ((MusicObject)pair.Car).SetSpot(location);
        }

        return result;
    }

    // Upstream's intlog2 (lily/include/misc.hh), for the power-of-two branch of
    // make_duration; the caller has already established value > 0.
    private static int IntLog2(int value)
    {
        int result = 0;
        while (value > 1)
        {
            value >>= 1;
            result++;
        }

        return result;
    }
}
