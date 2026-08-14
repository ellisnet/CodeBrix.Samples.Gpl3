/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>       (lyric-engraver.cc)
  Jan Nieuwenhuizen <janneke@gnu.org>

  Copyright (C) 1999--2026 Glen Prideaux <glenprideaux@iname.com>,   (extender-engraver.cc,
  Han-Wen Nienhuys <hanwen@xs4all.nl>,                                hyphen-engraver.cc)
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

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/lyric-engraver.cc, lily/extender-engraver.cc, lily/hyphen-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - upstream declares acknowledge_lyric_syllable (Grob_info_t<Item>), and the macro
//     layer generates both the interface test and the Item cast. The port has a single
//     AcknowledgeGrob (GrobInfo), so both are written out at the top of each override.
//   - get_voice_to_lyrics and get_current_note_head are free functions declared in
//     context.hh and DEFINED in lyric-engraver.cc, so they are the lyrics group's to port. They live
//     here as statics on LyricEngraver, which is their upstream file.

/**
   Generate texts for lyric syllables.  We only do one lyric at a time.
   Multiple copies of this engraver should be used to do multiple voices.
*/

/// <summary>
/// Engraves the syllables themselves, and — more subtly — hands each one to the note head
/// it belongs to, so the syllable moves when the note moves.
/// <para>
/// A syllable whose text is a single SPACE is not a syllable at all: it is lyric mode's
/// way of writing "hold the previous one", so it prints nothing and instead re-aligns the
/// PREVIOUS syllable by <c>lyricMelismaAlignment</c>. The same re-alignment happens when
/// the associated voice turns out to be in a melisma.
/// </para>
/// </summary>
public class LyricEngraver : Engraver
{
    private static readonly Symbol AssociatedVoiceSymbol = Symbol.Intern("associatedVoice");
    private static readonly Symbol AssociatedVoiceContextSymbol
        = Symbol.Intern("associatedVoiceContext");

    private static readonly Symbol AssociatedVoiceTypeSymbol
        = Symbol.Intern("associatedVoiceType");

    private static readonly Symbol BusyGrobsSymbol = Symbol.Intern("busyGrobs");
    private static readonly Symbol IgnoreMelismataSymbol = Symbol.Intern("ignoreMelismata");
    private static readonly Symbol LyricEventSymbol = Symbol.Intern("lyric-event");
    private static readonly Symbol LyricMelismaAlignmentSymbol
        = Symbol.Intern("lyricMelismaAlignment");

    private static readonly Symbol MelismaBusySymbol = Symbol.Intern("melismaBusy");
    private static readonly Symbol NoteHeadInterfaceSymbol = Symbol.Intern("note-head-interface");
    private static readonly Symbol SearchForVoiceSymbol = Symbol.Intern("searchForVoice");
    private static readonly Symbol SelfAlignmentXSymbol = Symbol.Intern("self-alignment-X");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");

    private StreamEvent _event;
    private Item _text;
    private Item _lastText;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public LyricEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Lyric_engraver";

    /// <summary>
    /// Returns the Voice context whose rhythm a Lyrics context follows, or
    /// <see langword="null"/> when there is none to follow.
    /// <para>
    /// Four ways to reach it, in order: the context the lyric-combine iterator already
    /// recorded in <c>associatedVoiceContext</c>; an explicit <c>associatedVoice</c> name;
    /// the Lyrics context's OWN id with everything after the last dash stripped, which is
    /// what makes <c>\new Lyrics = "sopranos-1"</c> find Voice <c>"sopranos"</c>; and
    /// finally any context of the right type at all.
    /// </para>
    /// <para>Upstream: the free function <c>get_voice_to_lyrics</c>, defined in
    /// <c>lily/lyric-engraver.cc</c>.</para>
    /// </summary>
    /// <param name="lyrics">The Lyrics context.</param>
    /// <returns>The voice context, or <see langword="null"/>.</returns>
    public static Context GetVoiceToLyrics(Context lyrics)
    {
        bool searchForVoice = SchemeUtilities.ToBool(lyrics.GetProperty(SearchForVoiceSymbol));

        object avc = lyrics.GetProperty(AssociatedVoiceContextSymbol);
        if (avc is Context c)
        {
            if (!c.IsRemovable)
            {
                return c;
            }
        }

        object voiceName = lyrics.GetProperty(AssociatedVoiceSymbol);
        string nm = lyrics.IdString;

        if (voiceName is MutableString || voiceName is string)
        {
            nm = voiceName.ToString();
        }
        else if (nm.Length == 0 || !searchForVoice)
        {
            return null;
        }
        else
        {
            int idx = nm.LastIndexOf('-');
            if (idx >= 0)
            {
                nm = nm.Substring(0, idx);
            }
        }

        object voiceType = lyrics.GetProperty(AssociatedVoiceTypeSymbol);
        if (!(voiceType is Symbol type))
        {
            return null;
        }

        Context voice = Context.FindContextNear(lyrics, type, nm);
        if (voice != null)
        {
            return voice;
        }

        return Context.FindContextNear(lyrics, type, string.Empty);
    }

    /// <summary>
    /// Returns the note head sounding in a voice right now, or <see langword="null"/>.
    /// <para>
    /// It is found by walking <c>busyGrobs</c> and keeping the one whose recorded end
    /// moment equals now plus its own event's length — which is upstream's way of asking
    /// "did this grob START here", given that the queue records only ends.
    /// </para>
    /// <para>Upstream: the free function <c>get_current_note_head</c>, defined in
    /// <c>lily/lyric-engraver.cc</c>.</para>
    /// </summary>
    /// <param name="voice">The voice context.</param>
    /// <returns>The note head, or <see langword="null"/>.</returns>
    public static Grob GetCurrentNoteHead(Context voice)
    {
        Moment now = voice.NowMoment;
        for (object s = voice.GetProperty(BusyGrobsSymbol); s is Pair pair; s = pair.Cdr)
        {
            Pair entry = pair.Car as Pair;
            Grob g = entry?.Cdr as Grob;
            Moment? endMoment = entry?.Car as Moment?;
            if (endMoment == null || g == null)
            {
                Warn.ProgrammingError("busyGrobs invalid");
                continue;
            }

            // It's a bit irritating that we just have the length and
            // duration of the Grob.
            Moment endFromNow = now + GetEventLength(g.EventCause(), now);

            // We cannot actually include more than a single grace note
            // using busyGrobs on ungraced lyrics since a grob ending on
            // grace time will just have disappeared from busyGrobs by the
            // time our ungraced lyrics appear.  At best we may catch a
            // single grace note.
            //
            // However, a single grace note ending on a non-grace time is
            // indistinguishable from a proper note ending on a non-grace
            // time.  So we really have no way to obey includeGraceNotes
            // here.  Not with this mechanism.
            if (endMoment.Value == endFromNow
                && g is Item
                && g.HasInterface(NoteHeadInterfaceSymbol))
            {
                return g;
            }
        }

        return null;
    }

    /// <summary>Starts listening for lyric events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(LyricEventSymbol, ListenLyric);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Makes the syllable, or re-aligns the previous one for a melisma.</summary>
    public override void ProcessMusic()
    {
        if (_event != null)
        {
            object text = _event.GetProperty(TextSymbol);

            if (SchemeUtilities.IsEqual(text, new MutableString(" ")))
            {
                if (_lastText != null)
                {
                    _lastText.SetProperty(
                        SelfAlignmentXSymbol, GetProperty(LyricMelismaAlignmentSymbol));
                }
            }
            else
            {
                _text = MakeItem("LyricText", _event);
            }
        }

        Context voice = GetVoiceToLyrics(Context);
        if (_lastText != null
            && voice != null
            && SchemeUtilities.ToBool(voice.GetProperty(MelismaBusySymbol))
            && !SchemeUtilities.ToBool(Context.GetProperty(IgnoreMelismataSymbol)))
        {
            _lastText.SetProperty(
                SelfAlignmentXSymbol, GetProperty(LyricMelismaAlignmentSymbol));
        }
    }

    /// <summary>Hands the syllable to the note head it belongs under.</summary>
    public override void StopTranslationTimestep()
    {
        if (_text != null)
        {
            Context voice = GetVoiceToLyrics(Context);

            if (voice != null)
            {
                Grob head = GetCurrentNoteHead(voice);

                if (head != null)
                {
                    _text.XParent = head.XParent;
                    if (Context.MelismaBusy(voice)
                        && !SchemeUtilities.ToBool(GetProperty(IgnoreMelismataSymbol)))
                    {
                        _text.SetProperty(
                            SelfAlignmentXSymbol, GetProperty(LyricMelismaAlignmentSymbol));
                    }
                }
            }

            _lastText = _text;
            _text = null;
        }

        _event = null;
    }

    private void ListenLyric(StreamEvent ev) => StreamEvent.AssignEventOnce(ref _event, ev);
}

/// <summary>
/// Creates lyric extenders, both the ones written <c>__</c> and the ones
/// <c>autoExtenders</c> generates.
/// <para>
/// The auto side is speculative by necessity and says so: whether a syllable is held over
/// a melisma cannot be known until the NEXT lyric event arrives, because a melisma may be
/// written as a bare <c>_</c> in lyric mode. So an extender is created unconditionally and
/// KILLED when the next syllable turns out not to be an underscore — which is why
/// <c>_pendingAutoextenderMelismaBusy</c> exists and why the melisma test is delayed to
/// <see cref="StopTranslationTimestep"/>.
/// </para>
/// </summary>
public class ExtenderEngraver : Engraver
{
    private static readonly Symbol AutoExtendersSymbol = Symbol.Intern("autoExtenders");
    private static readonly Symbol AutoGeneratedSymbol = Symbol.Intern("auto-generated");
    private static readonly Symbol CompletizeExtenderEventSymbol
        = Symbol.Intern("completize-extender-event");

    private static readonly Symbol ExtenderEventSymbol = Symbol.Intern("extender-event");
    private static readonly Symbol ExtendersOverRestsSymbol = Symbol.Intern("extendersOverRests");
    private static readonly Symbol HeadsSymbol = Symbol.Intern("heads");
    private static readonly Symbol HyphenEventSymbol = Symbol.Intern("hyphen-event");
    private static readonly Symbol LyricEventSymbol = Symbol.Intern("lyric-event");
    private static readonly Symbol LyricSyllableInterfaceSymbol
        = Symbol.Intern("lyric-syllable-interface");

    private static readonly Symbol NextSymbol = Symbol.Intern("next");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");

    private StreamEvent _extenderEvent;
    private StreamEvent _lyricEvent;
    private StreamEvent _hyphenEvent;
    private Spanner _extender;
    private Spanner _pendingExtender;

    private bool _pendingAutoextender;
    private bool _pendingAutoextenderMelismaBusy;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public ExtenderEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Extender_engraver";

    /// <summary>Starts listening for the four events this engraver reacts to.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(LyricEventSymbol, ListenLyric);
        ListenTo(HyphenEventSymbol, ListenHyphen);
        ListenTo(ExtenderEventSymbol, ListenExtender);
        ListenTo(CompletizeExtenderEventSymbol, ListenCompletizeExtender);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Creates the extender, explicitly asked for or speculatively.</summary>
    public override void ProcessMusic()
    {
        bool createExtender = false;
        StreamEvent causingEvent = null;

        if (_extenderEvent != null)
        {
            createExtender = true;
            causingEvent = _extenderEvent;
            _pendingAutoextender = false;
        }
        else if (_lyricEvent != null
                 && _hyphenEvent == null
                 && SchemeUtilities.ToBool(GetProperty(AutoExtendersSymbol)))
        {
            Context voice = LyricEngraver.GetVoiceToLyrics(Context);

            if (voice != null)
            {
                /*
                  We'd basically like to create an auto-extender only if the
                  voice is in a melisma.  But melismata indicated by _ in lyric
                  mode should also work, and these can be detected only when
                  the *next* lyric event comes along.  So we create an
                  auto-extender unconditionally and kill it off when the next
                  lyric event comes along if that isn't an underscore _.
                */
                createExtender = true;
                causingEvent = _lyricEvent;
                _pendingAutoextender = true;

                // clean up pending_autoextender_melisma_busy_ from inherited state;
                // this boolean will be set to true, if necessary, in
                // stop_translation_timestep or even (in case of a Lyric melisma _)
                // in a future timestep.
                _pendingAutoextenderMelismaBusy = false;
            }
        }

        if (createExtender)
        {
            _extender = MakeSpanner("LyricExtender", causingEvent);
            _extender.SetProperty(AutoGeneratedSymbol, _pendingAutoextender);
        }
    }

    /// <summary>Attaches the extender's left edge to the syllable, and closes a pending one.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!(info.Grob is Item item)
            || !item.HasInterface(LyricSyllableInterfaceSymbol))
        {
            return;
        }

        if (_extender != null)
        {
            _extender.SetBound(Direction.Negative, item);
        }

        if (_pendingExtender != null)
        {
            _pendingExtender.SetObject(NextSymbol, item);
            CompletizeExtender(_pendingExtender);
            _pendingExtender = null;
        }
    }

    /// <summary>Collects the note heads the extender covers and rolls it forward.</summary>
    public override void StopTranslationTimestep()
    {
        Context voice = LyricEngraver.GetVoiceToLyrics(Context);

        if (voice != null && _pendingAutoextender)
        {
            // delay call to melisma_busy () until here since a melisma might have
            // started during timestep's processing
            _pendingAutoextenderMelismaBusy
                = _pendingAutoextenderMelismaBusy || Context.MelismaBusy(voice);
        }

        if (_extender != null || _pendingExtender != null)
        {
            Grob h = voice != null ? LyricEngraver.GetCurrentNoteHead(voice) : null;

            if (h != null)
            {
                if (_extender != null)
                {
                    PointerGroupInterface.AddGrob(_extender, HeadsSymbol, h);
                }

                if (_pendingExtender != null)
                {
                    PointerGroupInterface.AddGrob(_pendingExtender, HeadsSymbol, h);
                }
            }
            else
            {
                if (_pendingExtender != null
                    && !SchemeUtilities.ToBool(GetProperty(ExtendersOverRestsSymbol)))
                {
                    CompletizeExtender(_pendingExtender);
                    _pendingExtender = null;
                }
            }

            if (_extender != null)
            {
                // We guarantee that after stop_translation_timestep,
                // extender_ is always null.
                _pendingExtender = _extender;
                _extender = null;
            }
        }

        _extenderEvent = null;
        _lyricEvent = null;
        _hyphenEvent = null;
    }

    /// <summary>Closes or discards an extender left open at the end of the score.</summary>
    public override void FinalizeTranslation()
    {
        if (_pendingExtender != null)
        {
            if (_pendingAutoextender && !_pendingAutoextenderMelismaBusy)
            {
                _pendingExtender.Suicide();
            }
            else
            {
                CompletizeExtender(_pendingExtender);

                if (_pendingExtender.GetBound(Direction.Positive) == null)
                {
                    ((IDiagnostics)_pendingExtender).Warning("unterminated extender");
                }
            }

            _pendingExtender = null;
        }

        base.FinalizeTranslation();
    }

    private static void CompletizeExtender(Spanner sp)
    {
        if (sp.GetBound(Direction.Positive) == null)
        {
            IReadOnlyList<Grob> heads = PointerGroupInterface.ExtractGrobSet(sp, HeadsSymbol);
            if (heads.Count > 0)
            {
                // extract_item_set () would clean this up, but would be wasteful
                // given that we need to use only one of the elements
                if (heads[heads.Count - 1] is Item head)
                {
                    sp.SetBound(Direction.Positive, head);
                }
                else
                {
                    ((IDiagnostics)heads[heads.Count - 1]).ProgrammingError("non-item among heads");
                }
            }
        }
    }

    private void ListenLyric(StreamEvent ev)
    {
        // Do not use assign_event_once - we let the listener in lyric-engraver.cc
        // warn about conflicting events instead.
        if (_lyricEvent == null)
        {
            _lyricEvent = ev;
        }

        object text = _lyricEvent.GetProperty(TextSymbol);

        if (SchemeUtilities.IsEqual(text, new MutableString(" ")))
        {
            // Don't register _ as a lyric starting an autoextender ...
            _lyricEvent = null;

            // ... but note that we actually are inside a melisma now.
            if (_pendingAutoextender)
            {
                _pendingAutoextenderMelismaBusy = true;
            }
        }
        else if (_pendingExtender != null
                 && _pendingAutoextender
                 && !_pendingAutoextenderMelismaBusy)
        {
            _pendingExtender.Suicide();
            _pendingExtender = null;
        }
    }

    private void ListenHyphen(StreamEvent ev)
    {
        // Do not use assign_event_once - we let the listener in hyphen-engraver.cc
        // warn about conflicting events instead.
        if (_hyphenEvent == null)
        {
            _hyphenEvent = ev;
        }
    }

    private void ListenExtender(StreamEvent ev)
        => StreamEvent.AssignEventOnce(ref _extenderEvent, ev);

    /*
      A CompletizeExtenderEvent is sent at the end of each lyrics block
      to ensure any pending extender can be correctly terminated if the lyrics
      end before the associated voice (this prevents the right bound being extended
      to the next note-column if no lyric follows the extender)
    */
    private void ListenCompletizeExtender(StreamEvent ev)
    {
        if (_pendingExtender != null)
        {
            CompletizeExtender(_pendingExtender);
            _pendingExtender = null;
        }
    }
}

/*
   In a given time step, we expect not to have both a hyphen and a
   vowel transition as they would obviously collide.  If we have
   neither of these two, create a LyricSpace to put a constraint
   on the minimum distance between lyric words through spacing rods.

   We do not expect a LyricText in every time step, however: think
   of _ skips in lyrics.  We support "some _ _ -- words" just as
   well as "some -- _ _ words" (but "some -- _ -- _ words", with a
   duplicate hyphen, prints a warning).
*/

/// <summary>
/// Creates the things that sit BETWEEN syllables: hyphens, vowel transitions, and — when
/// neither was asked for — an invisible <c>LyricSpace</c> whose only job is to keep two
/// lyric words from colliding.
/// <para>
/// Every one of the three is the same shape of spanner bounded by the syllable on each
/// side, which is why one engraver makes all three and why a hyphen and a vowel transition
/// in the same time step are treated as a conflict rather than drawn on top of each other.
/// </para>
/// </summary>
public class HyphenEngraver : Engraver
{
    private static readonly Symbol HeadsSymbol = Symbol.Intern("heads");
    private static readonly Symbol HyphenEventSymbol = Symbol.Intern("hyphen-event");
    private static readonly Symbol LyricSpaceInterfaceSymbol
        = Symbol.Intern("lyric-space-interface");

    private static readonly Symbol LyricSyllableInterfaceSymbol
        = Symbol.Intern("lyric-syllable-interface");

    private static readonly Symbol VowelTransitionEventSymbol
        = Symbol.Intern("vowel-transition-event");

    private StreamEvent _event;
    private StreamEvent _finishedEvent;

    private Item _syllable;
    private Item _lastSyllable;

    // A LyricHyphen or VowelTransition or LyricSpace, reset at every time step.
    private Spanner _hyphen;

    // A previously created LyricHyphen or ... awaiting completion.  Forgotten
    // about as soon as it finds a right bound.
    private Spanner _finishedHyphen;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public HyphenEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Hyphen_engraver";

    /// <summary>Starts listening for hyphens and vowel transitions.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(HyphenEventSymbol, ListenHyphen);
        ListenTo(VowelTransitionEventSymbol, ListenVowelTransition);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Makes the hyphen or vowel transition the event asked for.</summary>
    public override void ProcessMusic()
    {
        if (_event != null)
        {
            _hyphen = _event.IsInEventClass(VowelTransitionEventSymbol)
                ? MakeSpanner("VowelTransition", _event)
                : MakeSpanner("LyricHyphen", _event);
        }
    }

    /// <summary>Bounds the pending spanner on its right, and opens a LyricSpace if needed.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!(info.Grob is Item item)
            || !item.HasInterface(LyricSyllableInterfaceSymbol))
        {
            return;
        }

        _syllable = item;

        if (_hyphen == null)
        {
            _hyphen = MakeSpanner("LyricSpace", _syllable);
        }

        if (_finishedHyphen != null)
        {
            _finishedHyphen.SetBound(Direction.Positive, _syllable);
            AnnounceEndGrob(_finishedHyphen, _syllable);
            _finishedHyphen = null;
            _finishedEvent = null;
        }
    }

    /// <summary>Bounds the spanner on its left and rolls it forward, warning on conflicts.</summary>
    public override void StopTranslationTimestep()
    {
        if (_syllable != null)
        {
            _lastSyllable = _syllable;
        }

        if (_hyphen != null)
        {
            if (_lastSyllable != null)
            {
                _hyphen.SetBound(Direction.Negative, _lastSyllable);
            }
            else
            {
                ((IDiagnostics)_hyphen).Warning(
                    "hyphen or vowel transition has no syllable to attach to on its left; "
                    + "removing it");

                _hyphen.Suicide();
            }
        }

        if (_finishedHyphen != null && _hyphen != null)
        {
            // When we reach this, a hyphen is pending completion, and another
            // hyphen was created in the time step, conflicting with it.  The
            // one pending completion may be an automatically created LyricSpace,
            // in which case it is just removed.  This happens with "some _ -- words".
            // Otherwise, there are extraneous hyphens in the input (e.g.,
            // "some \vowelTransition _ -- words") and we should warn.
            if (!_finishedHyphen.HasInterface(LyricSpaceInterfaceSymbol))
            {
                ((IDiagnostics)_finishedHyphen).Warning(
                    "this hyphen or vowel transition was overridden by a later one");
            }

            _finishedHyphen.Suicide();
        }

        if (_hyphen != null)
        {
            _finishedHyphen = _hyphen;
            _finishedEvent = _event;
        }

        _hyphen = null;
        _event = null;
        _syllable = null;
    }

    /// <summary>Closes or discards hyphens left open at the end of the score.</summary>
    public override void FinalizeTranslation()
    {
        if (_hyphen != null)
        {
            CompletizeHyphen(_hyphen);

            if (_hyphen.GetBound(Direction.Positive) == null)
            {
                ((IDiagnostics)_hyphen).Warning("removing unterminated hyphen");
                _hyphen.Suicide();
            }

            _hyphen = null;
        }

        if (_finishedHyphen != null)
        {
            CompletizeHyphen(_finishedHyphen);

            if (_finishedHyphen.GetBound(Direction.Positive) == null)
            {
                if (_finishedEvent != null)
                {
                    ((IDiagnostics)_finishedHyphen).Warning("unterminated hyphen; removing");
                }

                _finishedHyphen.Suicide();
            }

            _finishedHyphen = null;
        }

        base.FinalizeTranslation();
    }

    private static void CompletizeHyphen(Spanner sp)
    {
        if (sp.GetBound(Direction.Positive) == null)
        {
            IReadOnlyList<Grob> heads = PointerGroupInterface.ExtractGrobSet(sp, HeadsSymbol);
            if (heads.Count > 0)
            {
                // extract_item_set () would look clean this up, but would be wasteful
                // given that we need to use only one of the elements
                if (heads[heads.Count - 1] is Item head)
                {
                    sp.SetBound(Direction.Positive, head);
                }
                else
                {
                    ((IDiagnostics)heads[heads.Count - 1]).ProgrammingError("non-item among heads");
                }
            }
        }
    }

    private void ListenHyphen(StreamEvent ev) => StreamEvent.AssignEventOnce(ref _event, ev);

    private void ListenVowelTransition(StreamEvent ev)
        => StreamEvent.AssignEventOnce(ref _event, ev);
}
