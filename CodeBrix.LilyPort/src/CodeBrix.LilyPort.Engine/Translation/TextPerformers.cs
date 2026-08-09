/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1996--2026 Jan Nieuwenhuizen <janneke@gnu.org>

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
using CodeBrix.LilyPort.Engine.Audio;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/lyric-performer.cc, lily/mark-performer.cc;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port.

/// <summary>Turns lyric events into MIDI lyric meta-events.</summary>
public sealed class LyricPerformer : Performer
{
    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol MakeTiedLyricMarkupSymbol
        = Symbol.Intern("make-tied-lyric-markup");
    private static readonly Symbol LyricEventSymbol = Symbol.Intern("lyric-event");

    private StreamEvent _event;

    /// <summary>Initializes the performer in a context.</summary>
    /// <param name="context">The context this performer belongs to.</param>
    public LyricPerformer(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this translator corresponds to.</summary>
    public override string ClassName => "Lyric_performer";

    /// <summary>Starts listening for lyrics.</summary>
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

    /// <summary>Emits the syllable heard this timestep.</summary>
    public override void ProcessMusic()
    {
        if (_event == null)
        {
            return;
        }

        object text = _event.GetProperty(TextSymbol);

        // Mimic lyric-text::print by wrapping text in \tied-lyric if a string. This
        // ensures that the custom markup->string handler of \tied-lyric will convert
        // tildes to Unicode underties.
        if (text is MutableString || text is string)
        {
            object procedure = LilyPondScheme.LookupProcedure(MakeTiedLyricMarkupSymbol);
            if (procedure != null)
            {
                text = SchemeUtilities.CallCallback(procedure, text);
            }
        }

        if (!(text is Nil))
        {
            Announce(_event, new AudioText(AudioTextType.Lyric, text));
        }

        _event = null;
    }

    /// <summary>Forgets this timestep's event.</summary>
    public override void StopTranslationTimestep() => _event = null;

    private void ListenLyric(StreamEvent ev)
    {
        if (_event == null)
        {
            _event = ev;
        }
    }
}

/// <summary>
/// Emits MIDI markers for rehearsal marks, segno and coda marks, and section labels.
/// </summary>
/// <remarks>
/// The markup is generated exactly as in <see cref="MarkEngraver"/> — this performer
/// calls the engraver's own getters rather than repeating the rules, which is upstream's
/// arrangement too.
/// </remarks>
public sealed class MarkPerformer : Performer
{
    private static readonly Symbol CurrentRehearsalMarkEventSymbol
        = Symbol.Intern("currentRehearsalMarkEvent");
    private static readonly Symbol CurrentPerformanceMarkEventSymbol
        = Symbol.Intern("currentPerformanceMarkEvent");

    /// <summary>Initializes the performer in a context.</summary>
    /// <param name="context">The context this performer belongs to.</param>
    public MarkPerformer(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this translator corresponds to.</summary>
    public override string ClassName => "Mark_performer";

    /// <summary>Emits a marker for each current mark that carries text.</summary>
    public override void ProcessMusic()
    {
        ProcessMark(
            MarkEngraver.GetCurrentRehearsalMarkText(Context),
            CurrentRehearsalMarkEventSymbol);

        ProcessMark(
            MarkEngraver.GetCurrentPerformanceMarkText(Context),
            CurrentPerformanceMarkEventSymbol);
    }

    private void ProcessMark(object text, Symbol propertySymbol)
    {
        if (text is Nil || text == null)
        {
            return;
        }

        // We could change the Mark_engraver's getter to give us this event too, since it
        // has to look it up internally. It's not a big deal.   (upstream's comment, kept)
        StreamEvent ev = Context?.GetProperty(propertySymbol) as StreamEvent;
        Announce(ev, new AudioText(AudioTextType.Marker, text));
    }
}
