/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2004--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/lyric-combine-music-iterator.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - derived_mark () has no counterpart. It exists to keep the four SCM members and the
//     child iterator reachable for Guile's garbage collector; the CLR reaches them
//     through the fields themselves, so there is nothing to mark.

/*
  This iterator is hairy.  It tracks both lyric and melody contexts,
  and has a complicated communication route, reading/writing
  properties in both.

  In the future, this should rather be done with

     \interpretAsMelodyFor { MUSIC } { LYRICS LYRICS LYRICS }

  This can run an interpret step on MUSIC, generating a stream.  Then
  the stream can be perused at leisure to apply durations to all of
  the LYRICS.
*/

/// <summary>
/// The iterator for <c>\lyricsto</c> and <c>\addlyrics</c>: it drives a lyrics iterator
/// off the rhythm of a SEPARATE melody context, one syllable per note that starts a new
/// syllable.
/// <para>
/// The inversion is the whole point and is worth stating plainly. Every other iterator
/// advances on its own music's time; this one advances its child only when the melody
/// says so. That is why it is <see cref="RunAlways"/>: its own pending moment says
/// nothing about when the next syllable is due, because the melody decides.
/// </para>
/// <para>
/// It watches the melody context through two listeners on that context's
/// <c>EventsBelow</c> — one to learn that a note happened at all
/// (<c>melodic-event</c> → <c>_busyMoment</c>), one to relay <c>structural-event</c>s to
/// the lyrics context — and re-checks on every <c>Process</c> whether
/// <c>associatedVoice</c> has moved it to a different melody.
/// </para>
/// </summary>
public sealed class LyricCombineMusicIterator : MusicIterator
{
    private static readonly Symbol AssociatedContextSymbol = Symbol.Intern("associated-context");
    private static readonly Symbol AssociatedContextTypeSymbol
        = Symbol.Intern("associated-context-type");

    private static readonly Symbol AssociatedVoiceSymbol = Symbol.Intern("associatedVoice");
    private static readonly Symbol AssociatedVoiceContextSymbol
        = Symbol.Intern("associatedVoiceContext");

    private static readonly Symbol AssociatedVoiceTypeSymbol
        = Symbol.Intern("associatedVoiceType");

    private static readonly Symbol CreateContextSymbol = Symbol.Intern("CreateContext");
    private static readonly Symbol ElementSymbol = Symbol.Intern("element");
    private static readonly Symbol IgnoreMelismataSymbol = Symbol.Intern("ignoreMelismata");
    private static readonly Symbol IncludeGraceNotesSymbol = Symbol.Intern("includeGraceNotes");
    private static readonly Symbol LyricsSymbol = Symbol.Intern("Lyrics");
    private static readonly Symbol MelodicEventSymbol = Symbol.Intern("melodic-event");
    private static readonly Symbol StructuralEventSymbol = Symbol.Intern("structural-event");
    private static readonly Symbol VoiceSymbol = Symbol.Intern("Voice");

    private bool _musicFound;
    private bool _lyricsFound;
    private Context _lyricsContext;
    private Context _musicContext;
    private object _lyricsToVoiceName = Nil.Instance;
    private object _lyricsToVoiceType = Nil.Instance;

    private Moment _busyMoment = -Moment.Infinity;
    private Moment _pendingGraceMoment = Moment.Infinity;

    private MusicIterator _lyricIterator;

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Lyric_combine_music_iterator";

    /// <summary>
    /// Gets a value indicating whether this iterator must keep running: the lyrics still
    /// have syllables left and the melody context is still alive.
    /// </summary>
    public override bool RunAlways
        => _lyricIterator != null
           && _lyricIterator.Ok
           && !(_musicContext != null && _musicContext.IsRemovable);

    /// <summary>Replaces one context with another, in this iterator's own two as well.</summary>
    /// <param name="from">The context to replace.</param>
    /// <param name="to">The replacement.</param>
    public override void SubstituteContext(Context from, Context to)
    {
        if (!ReferenceEquals(from, to))
        {
            base.SubstituteContext(from, to);
            if (_lyricsContext != null && ReferenceEquals(_lyricsContext, from))
            {
                _lyricsContext = to;
            }

            if (_musicContext != null && ReferenceEquals(_musicContext, from))
            {
                SetMusicContext(to);
            }
        }
    }

    /// <summary>Walks this iterator and then the lyrics iterator.</summary>
    /// <param name="visit">The function to call.</param>
    public override void PreorderWalk(Action<MusicIterator> visit)
    {
        base.PreorderWalk(visit);
        _lyricIterator?.PreorderWalk(visit);
    }

    /// <summary>
    /// Advances the lyrics by at most one syllable, if the melody has started one.
    /// </summary>
    /// <param name="until">The moment to process up to; unused, as the melody decides.</param>
    public override void Process(Moment until)
    {
        /* see if associatedVoice has been changed */
        Context newVoice = FindVoice();
        if (newVoice != null)
        {
            SetMusicContext(newVoice);
        }

        _lyricsFound = true;
        if (_musicContext == null)
        {
            return;
        }

        if (_musicContext.Parent == null)
        {
            /*
              The melody has died.
              We die too.
            */
            _lyricsContext?.UnsetProperty(AssociatedVoiceContextSymbol);

            _lyricIterator = null;
            SetMusicContext(null);
        }

        if (_musicContext != null
            && (StartNewSyllable() || _busyMoment >= _pendingGraceMoment)
            && _lyricIterator.Ok)
        {
            Moment now = _musicContext.NowMoment;
            if (now.GracePart.IsNonZero
                && !SchemeUtilities.ToBool(_lyricsContext.GetProperty(IncludeGraceNotesSymbol)))
            {
                _pendingGraceMoment = new Moment(now.MainPart, Rational.Zero);
                return;
            }
            else
            {
                _pendingGraceMoment = new Moment(Rational.Infinity, _pendingGraceMoment.GracePart);
            }

            Moment m = _lyricIterator.PendingMoment;
            _lyricsContext.SetProperty(AssociatedVoiceContextSymbol, _musicContext);
            _lyricIterator.Process(m);

            _musicFound = true;
        }

        newVoice = FindVoice();
        if (newVoice != null)
        {
            SetMusicContext(newVoice);
        }
    }

    /// <summary>Creates the iterator for the lyrics half of the expression.</summary>
    protected override void CreateChildren()
    {
        base.CreateChildren();

        MusicObject element = Music.GetProperty(ElementSymbol) as MusicObject;
        if (element != null)
        {
            _lyricIterator = CreateChild(element);
        }
    }

    /// <summary>
    /// Finds the Lyrics context the syllables go to and the Voice context the rhythm
    /// comes from, and arranges to be told about voices created later.
    /// </summary>
    protected override void CreateContexts()
    {
        base.CreateContexts();

        if (_lyricIterator == null)
        {
            return;
        }

        _lyricIterator.InitContext(Context);
        _lyricsContext = Context.FindContextBelow(_lyricIterator.Context, LyricsSymbol, string.Empty);

        if (_lyricsContext == null)
        {
            if (Music.GetProperty(ElementSymbol) is IDiagnostics m)
            {
                m.Warning("argument of \\lyricsto should contain Lyrics context");
            }
        }

        _lyricsToVoiceName = Music.GetProperty(AssociatedContextSymbol);
        _lyricsToVoiceType = Music.GetProperty(AssociatedContextTypeSymbol);
        if (!(_lyricsToVoiceType is Symbol))
        {
            _lyricsToVoiceType = VoiceSymbol;
        }

        Context voice = FindVoice();
        if (voice != null)
        {
            SetMusicContext(voice);
        }

        /*
          Wait for a Create_context event. If this isn't done, lyrics can be
          delayed when voices are created implicitly.
        */
        Context g = Context?.Root;
        g?.EventsBelow.AddListener(
            new Listener(this, CheckNewContext), CreateContextSymbol);

        /*
          We do not create a Lyrics context, because the user might
          create one with a different name, and then we will not find that
          one.
        */
    }

    /// <summary>
    /// Warns when lyrics were asked for but no melody was ever found, then shuts the
    /// lyrics iterator down.
    /// </summary>
    protected override void DoQuit()
    {
        /* Don't print a warning for empty lyrics (in which case we don't try
           to find the proper voice, so it will not be found) */
        if (_lyricsFound && !_musicFound)
        {
            MusicObject m = Music;

            // ugh: defaults are repeated elsewhere
            object voiceType = m.GetProperty(AssociatedContextTypeSymbol);
            if (!(voiceType is Symbol))
            {
                voiceType = VoiceSymbol;
            }

            string id = AsIdString(m.GetProperty(AssociatedContextSymbol));
            Input origin = ((IDiagnostics)m).Origin();
            string message = "cannot find context: "
                             + Context.DiagnosticId(voiceType as Symbol, id);

            if (origin != null)
            {
                origin.Warning(message);
            }
            else
            {
                Warn.Warning(message);
            }
        }

        _lyricIterator?.Quit();
    }

    private static string AsIdString(object value)
        => value is MutableString || value is string ? value.ToString() : string.Empty;

    /*
      Forward an event to the lyrics context.
    */
    private void ForwardEvent(StreamEvent streamEvent)
    {
        if (_lyricsContext != null)
        {
            if (streamEvent != null)
            {
                _lyricsContext.EventSource.Broadcast(streamEvent);
            }
        }
    }

    /*
      It's dubious whether we can ever make this fully work.  Due to
      associatedVoice switching, this routine may be triggered for
      the wrong music_context_
     */
    private void SetBusy(StreamEvent streamEvent)
    {
        if (_musicContext != null)
        {
            Moment now = _musicContext.NowMoment;
            _busyMoment = now > _busyMoment ? now : _busyMoment;
        }
    }

    private void SetMusicContext(Context to)
    {
        if (_musicContext != null)
        {
            Dispatcher d = _musicContext.EventsBelow;
            d.RemoveListener(new Listener(this, ForwardEvent), StructuralEventSymbol);
            d.RemoveListener(new Listener(this, SetBusy), MelodicEventSymbol);
        }

        _musicContext = to;

        if (_musicContext != null)
        {
            Dispatcher d = _musicContext.EventsBelow;
            d.AddListener(new Listener(this, SetBusy), MelodicEventSymbol);

            // Forward structural events from the music context to the lyrics
            // context.  The iterators for \repeat and \alternative refrain from
            // announcing their events when they are inside LyricCombineMusic because
            // the way Lyric_combine_music_iterator advances time for the lyrics
            // iterator tends to place them at the wrong point in time.
            d.AddListener(new Listener(this, ForwardEvent), StructuralEventSymbol);
        }
    }

    private bool StartNewSyllable()
    {
        if (_lyricsContext == null)
        {
            return false;
        }

        if (_busyMoment < _musicContext.NowMoment)
        {
            return false;
        }

        if (!SchemeUtilities.ToBool(_lyricsContext.GetProperty(IgnoreMelismataSymbol)))
        {
            bool m = Context.MelismaBusy(_musicContext);
            if (m)
            {
                return false;
            }
        }

        return true;
    }

    private void CheckNewContext(StreamEvent streamEvent)
    {
        if (!Ok)
        {
            return;
        }

        // Search for a possible candidate voice to attach the lyrics to. If none
        // is found, we'll try next time again.
        Context voice = FindVoice();
        if (voice != null)
        {
            SetMusicContext(voice);
        }
    }

    /*
      Look for a suitable voice to align lyrics to.

      Returns 0 if nothing should change; i.e., if we already listen to the
      right voice, or if we don't yet listen to a voice but no appropriate
      voice could be found.
    */
    private Context FindVoice()
    {
        object voiceName = _lyricsToVoiceName;
        object running = _lyricsContext != null
            ? _lyricsContext.GetProperty(AssociatedVoiceSymbol)
            : Nil.Instance;

        object voiceType = _lyricsToVoiceType;
        if (running is MutableString || running is string)
        {
            voiceName = running;
            voiceType = _lyricsContext.GetProperty(AssociatedVoiceTypeSymbol);
        }

        if ((voiceName is MutableString || voiceName is string)
            && (_musicContext == null
                || !string.Equals(
                    voiceName.ToString(), _musicContext.IdString, StringComparison.Ordinal))
            && voiceType is Symbol type)
        {
            return Context.FindContextBelow(Context?.Root, type, voiceName.ToString());
        }

        return null;
    }
}
