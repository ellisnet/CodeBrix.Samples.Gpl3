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

using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyPort.Parsing.Driver;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Actions; //was previously: lily/parser.yy (epilogue: add_post_events, reverse_music_list);

// Modified by Jeremy Ellis on 2026-08-04 as part of the CodeBrix port.

/// <content>
/// The epilogue helpers RULE ACTION GROUP 2 brought in: <c>reverse_music_list</c>
/// with its companion <c>add_post_events</c>, and the two libguile list operations
/// they lean on (<c>scm_last_pair</c>, the two-list <c>scm_append_x</c>). Like the
/// rest of this class, list work is implemented locally and music work runs over
/// the Engine's real <see cref="MusicObject"/> — reducing a rule must not depend
/// on an interpreter being alive.
/// </content>
internal static partial class ParserActionHelpers
{
    /// <summary>
    /// Returns a list's last pair, or the empty list for an empty one, which is
    /// <c>scm_last_pair</c>.
    /// </summary>
    /// <param name="list">The list to walk.</param>
    /// <returns>The last pair, or <see cref="Nil.Instance"/>.</returns>
    internal static object LastPair(object list)
    {
        object result = Nil.Instance;
        object current = list;
        while (current is Pair pair)
        {
            result = pair;
            current = pair.Cdr;
        }

        return result;
    }

    /// <summary>
    /// Destructively appends two lists — <c>scm_append_x</c> over the two-list call
    /// every action site makes: the first list's last pair is re-pointed at the
    /// tail, and an empty first list answers the tail itself.
    /// </summary>
    /// <param name="list">The list whose pairs are reused.</param>
    /// <param name="tail">The list appended after it.</param>
    /// <returns>The combined list.</returns>
    internal static object AppendInPlace(object list, object tail)
    {
        if (!(list is Pair))
        {
            return tail;
        }

        ((Pair)LastPair(list)).Cdr = tail;
        return list;
    }

    /// <summary>
    /// Attaches accumulated post events to the music preceding them: onto a rhythmic
    /// or caesura event's articulations, onto an event chord's elements, or —
    /// descending through sequential music's last element and through wrapper and
    /// time-scaled music — whatever such target the music ends in.
    /// <para>Upstream: <c>add_post_events</c> in <c>parser.yy</c>'s epilogue —
    /// "Return true if there are post events unaccounted for".</para>
    /// </summary>
    /// <param name="music">The music to attach to.</param>
    /// <param name="events">The post events, in document order.</param>
    /// <returns><see langword="true"/> when post events remain unaccounted for.</returns>
    internal static bool AddPostEvents(MusicObject music, object events)
    {
        if (!(events is Pair))
        {
            return false; // successfully added -- nothing
        }

        MusicObject m = music;
        while (m != null)
        {
            if (m.IsMusicType("rhythmic-event")
                || m.IsMusicType("caesura-event"))
            {
                m.SetProperty(
                    "articulations",
                    AppendInPlace(m.GetProperty("articulations"), events));
                return false;
            }

            if (m.IsMusicType("event-chord"))
            {
                m.SetProperty(
                    "elements",
                    AppendInPlace(m.GetProperty("elements"), events));
                return false;
            }

            if (m.IsMusicType("sequential-music"))
            {
                object lp = LastPair(m.GetProperty("elements"));
                if (lp is Pair lastPair)
                {
                    // upstream: m = unsmob<Music> (scm_car (lp)) — a non-music car
                    // makes m null and ends the walk unaccounted-for.
                    m = lastPair.Car as MusicObject;
                    continue;
                }

                return true;
            }

            if (m.IsMusicType("music-wrapper-music")
                || m.IsMusicType("time-scaled-music"))
            {
                m = m.GetProperty("element") as MusicObject;
                continue;
            }

            break;
        }

        return true;
    }

    // Returns either a list or a post-event
    //
    // If PRESERVE is true, unattachable post-events are not thrown away
    // but rather added attached to empty chords.  If COMPRESS is true, a
    // sequence consisting only of post-events may be returned as a single
    // post-event.

    /// <summary>
    /// Reverses an accumulated music list into document order, attaching each run of
    /// post events to the music element before it as it goes.
    /// <para>Upstream: <c>reverse_music_list</c> in <c>parser.yy</c>'s epilogue. Its
    /// <c>Lily_parser *</c> parameter is the host here: upstream only passes the
    /// parser to feed the <c>MAKE_SYNTAX</c>/<c>MY_MAKE_MUSIC</c> macros in the
    /// body.</para>
    /// </summary>
    /// <param name="host">The parser host.</param>
    /// <param name="location">The reducing rule's <c>@$</c> span.</param>
    /// <param name="list">The accumulated (reversed) music list.</param>
    /// <param name="preserve">Whether unattachable post events are kept attached to
    /// empty chords rather than dropped.</param>
    /// <param name="compress">Whether a pure post-event sequence may collapse to a
    /// single post event.</param>
    /// <returns>The list in document order, or a single packaged post event.</returns>
    internal static object ReverseMusicList(
        IParserHost host, SourceSpan location, object list, bool preserve, bool compress)
    {
        object res = Nil.Instance;  // Resulting reversed list
        object bad = Nil.Instance;  // Bad post events
        object post = Nil.Instance; // current unattached events
        for (object lst = list; lst is Pair lstPair; lst = lstPair.Cdr)
        {
            object elt = lstPair.Car;
            MusicObject m = (MusicObject)elt; // upstream: assert (m)
            if (m.IsMusicType("post-event"))
            {
                post = PostEventCons(elt, post);
                continue;
            }

            if (AddPostEvents(m, post))
            {
                bad = new Pair(((Pair)post).Car, bad);
                if (preserve)
                {
                    MusicObject p = (MusicObject)((Pair)post).Car;
                    res = new Pair(
                        host.MakeSyntax("event-chord", OriginSpan(p), post),
                        res);
                }
            }

            post = Nil.Instance;
            res = new Pair(elt, res);
        }

        if (post is Pair postPair)
        {
            if (res is Nil && compress) // pure postevent list
            {
                if (postPair.Cdr is Nil)
                {
                    return postPair.Car;
                }

                object m = host.MakeMusic("PostEvents", location);
                host.SetMusicProperty(m, "elements", post);
                return m;
            }

            bad = Append(post, bad);
            if (preserve)
            {
                MusicObject p = (MusicObject)postPair.Car;
                res = new Pair(
                    host.MakeSyntax("event-chord", OriginSpan(p), post),
                    res);
            }
        }

        for (object b = bad; b is Pair badPair; b = badPair.Cdr)
        {
            MusicObject what = (MusicObject)badPair.Car;
            if (preserve)
            {
                MusicWarning(what, "Unattached " + what.Name);
            }
            else
            {
                MusicWarning(what, "Dropping unattachable " + what.Name);
            }
        }

        return res;
    }

    /// <summary>
    /// Returns the span a music object's <c>origin</c> property carries, or the
    /// empty span when it carries none — which is <c>Music::origin</c> answering
    /// <c>dummy_input_global</c> for music with no location stamped.
    /// </summary>
    /// <param name="music">The music object.</param>
    /// <returns>The origin span, or the default span.</returns>
    internal static SourceSpan OriginSpan(MusicObject music)
        => music.Origin is SourceSpan span ? span : default;

    /// <summary>
    /// Issues a warning at a music object's origin, which is <c>Music::warning</c> —
    /// <c>Diagnostics::warning</c> over <c>Music::origin</c>, reaching
    /// <c>Input::warning</c> when a location is stamped and the plain
    /// <c>::warning</c> when none is.
    /// </summary>
    /// <param name="music">The music object.</param>
    /// <param name="message">The warning text.</param>
    internal static void MusicWarning(MusicObject music, string message)
    {
        if (music.Origin is SourceSpan span)
        {
            Warn.Warning(message, span.ToString());
        }
        else
        {
            Warn.Warning(message);
        }
    }
}
