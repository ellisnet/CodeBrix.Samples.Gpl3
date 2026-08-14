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

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Audio;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/beam-performer.cc, lily/slur-performer.cc, lily/tie-performer.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - beam-performer.cc and slur-performer.cc are the SAME FILE upstream, and upstream
//     says so: slur-performer.cc opens with the comment "this is C&P from
//     beam_performer". They are kept as two separate types here, because the property
//     each writes differs and a shared base would hide exactly that one difference.

/// <summary>
/// Sets <c>beamMelismaBusy</c> across a manual beam, so lyrics under it take one syllable.
/// </summary>
public sealed class BeamPerformer : Performer
{
    private static readonly Symbol AutoBeamingSymbol = Symbol.Intern("autoBeaming");
    private static readonly Symbol BeamMelismaBusySymbol = Symbol.Intern("beamMelismaBusy");
    private static readonly Symbol BeamEventSymbol = Symbol.Intern("beam-event");

    private readonly LastSpanEventListener _beamListener = new LastSpanEventListener();

    /// <summary>Initializes the performer in a context.</summary>
    /// <param name="context">The context this performer belongs to.</param>
    public BeamPerformer(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this translator corresponds to.</summary>
    public override string ClassName => "Beam_performer";

    /// <summary>Starts listening for beams.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(BeamEventSymbol, _beamListener.Listen);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Forgets last timestep's beam events.</summary>
    public override void StartTranslationTimestep() => _beamListener.Reset();

    /// <summary>Turns the melisma on at a beam start and off at a beam stop.</summary>
    public override void ProcessMusic()
    {
        if (_beamListener.Start != null)
        {
            SetMelisma(true);
        }
        else if (_beamListener.Stop != null)
        {
            SetMelisma(false);
        }
    }

    /// <summary>
    /// Sets the melisma, but only when autobeaming is OFF.
    /// </summary>
    /// <remarks>
    /// The guard is upstream's and is easy to misread as backwards: a MANUAL beam is a
    /// deliberate phrasing mark and should hold a syllable, whereas an automatic beam is
    /// just notation and should not.
    /// </remarks>
    private void SetMelisma(bool busy)
    {
        if (!SchemeUtilities.ToBool(GetProperty(AutoBeamingSymbol)))
        {
            Context?.SetProperty(BeamMelismaBusySymbol, busy);
        }
    }
}

/// <summary>Sets <c>slurMelismaBusy</c> across a slur.</summary>
public sealed class SlurPerformer : Performer
{
    private static readonly Symbol SlurMelismaBusySymbol = Symbol.Intern("slurMelismaBusy");
    private static readonly Symbol SlurEventSymbol = Symbol.Intern("slur-event");

    private readonly LastSpanEventListener _slurListener = new LastSpanEventListener();

    /// <summary>Initializes the performer in a context.</summary>
    /// <param name="context">The context this performer belongs to.</param>
    public SlurPerformer(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this translator corresponds to.</summary>
    public override string ClassName => "Slur_performer";

    /// <summary>Starts listening for slurs.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(SlurEventSymbol, _slurListener.Listen);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Forgets last timestep's slur events.</summary>
    public override void StartTranslationTimestep() => _slurListener.Reset();

    /// <summary>Turns the melisma on at a slur start and off at a slur stop.</summary>
    public override void ProcessMusic()
    {
        if (_slurListener.Start != null)
        {
            SetMelisma(true);
        }
        else if (_slurListener.Stop != null)
        {
            SetMelisma(false);
        }
    }

    private void SetMelisma(bool busy) => Context?.SetProperty(SlurMelismaBusySymbol, busy);
}

/// <summary>Generates ties between audio notes of equal pitch.</summary>
public sealed class TiePerformer : Performer
{
    private static readonly Symbol TieMelismaBusySymbol = Symbol.Intern("tieMelismaBusy");
    private static readonly Symbol TieWaitForNoteSymbol = Symbol.Intern("tieWaitForNote");
    private static readonly Symbol PitchSymbol = Symbol.Intern("pitch");
    private static readonly Symbol TieEventSymbol = Symbol.Intern("tie-event");

    private readonly List<HeadAudioEventTuple> _nowHeads = new List<HeadAudioEventTuple>();
    private readonly List<HeadAudioEventTuple> _nowTiedHeads
        = new List<HeadAudioEventTuple>();
    private readonly List<HeadAudioEventTuple> _headsToTie
        = new List<HeadAudioEventTuple>();

    private StreamEvent _event;

    /// <summary>Initializes the performer in a context.</summary>
    /// <param name="context">The context this performer belongs to.</param>
    public TiePerformer(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this translator corresponds to.</summary>
    public override string ClassName => "Tie_performer";

    /// <summary>Starts listening for ties.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(TieEventSymbol, ListenTie);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Reports whether any tie is still waiting to be closed.</summary>
    public override void StartTranslationTimestep()
        => Context?.SetProperty(TieMelismaBusySymbol, _headsToTie.Count != 0);

    /// <summary>Marks the melisma busy when a tie event is heard.</summary>
    public override void ProcessMusic()
    {
        if (_event != null)
        {
            Context?.SetProperty(TieMelismaBusySymbol, true);
        }
    }

    /// <summary>
    /// Notes an announced audio note, and ties it back to a waiting note of equal pitch.
    /// </summary>
    /// <param name="info">The announcement record.</param>
    /// <remarks>
    /// The two passes are upstream's and are not redundant: the first matches on the
    /// EVENTS' <c>pitch</c> properties being <c>equal?</c>, the second on the two pitches
    /// sounding the same TONE. A tie between enharmonically equal spellings only matches
    /// on the second pass.
    /// </remarks>
    public override void AcknowledgeAudioElement(AudioElementInfo info)
    {
        if (!(info.Element is AudioNote note))
        {
            return;
        }

        // For each tied note, store the info and its end moment, so we can later on check
        // whether (1) the note is still ongoing and (2) how long the skip is with
        // tieWaitForNote.
        HeadAudioEventTuple entry
            = new HeadAudioEventTuple(info, NowMoment + note.LengthMoment);

        if (note.TieEvent)
        {
            _nowTiedHeads.Add(entry);
        }
        else
        {
            _nowHeads.Add(entry);
        }

        StreamEvent rightMusic = info.Event;

        for (int i = 0; i < _headsToTie.Count; i++)
        {
            AudioElementInfo waiting = _headsToTie[i].Head;
            StreamEvent leftMusic = waiting.Event;

            if (waiting.Element is AudioNote && rightMusic != null && leftMusic != null
                && SchemeUtilities.IsEqual(
                    rightMusic.GetProperty(PitchSymbol), leftMusic.GetProperty(PitchSymbol)))
            {
                TieBack(note, i);
                return;
            }
        }

        for (int i = 0; i < _headsToTie.Count; i++)
        {
            AudioElementInfo waiting = _headsToTie[i].Head;
            StreamEvent leftMusic = waiting.Event;

            if (!(waiting.Element is AudioNote) || rightMusic == null || leftMusic == null)
            {
                continue;
            }

            if (leftMusic.GetProperty(PitchSymbol) is Pitch left
                && rightMusic.GetProperty(PitchSymbol) is Pitch right
                && left.TonePitch() == right.TonePitch())
            {
                TieBack(note, i);
                return;
            }
        }
    }

    /// <summary>Closes dangling ties and carries this timestep's heads forward.</summary>
    public override void StopTranslationTimestep()
    {
        // We might have dangling open ties like c~ d. Close them, unless the first note
        // is still ongoing or we have tieWaitForNote set.
        if (!SchemeUtilities.ToBool(GetProperty(TieWaitForNoteSymbol)))
        {
            Moment now = NowMoment;
            _headsToTie.RemoveAll(value => value.EndMoment <= now);
        }

        // Append now_heads_ and now_tied_heads to heads_to_tie_ for the next time step
        if (_event != null)
        {
            _headsToTie.AddRange(_nowHeads);
        }

        _headsToTie.AddRange(_nowTiedHeads);

        _event = null;
        _nowHeads.Clear();
        _nowTiedHeads.Clear();
    }

    private void TieBack(AudioNote note, int index)
    {
        HeadAudioEventTuple waiting = _headsToTie[index];

        // waiting.EndMoment already stores the end of the tied note!
        Moment skip = NowMoment - waiting.EndMoment;
        note.TieTo((AudioNote)waiting.Head.Element, skip);
        _headsToTie.RemoveAt(index);
    }

    private void ListenTie(StreamEvent ev) => _event = ev;

    /// <summary>A note waiting to be tied to, and the moment it stops sounding.</summary>
    private readonly struct HeadAudioEventTuple
    {
        internal HeadAudioEventTuple(AudioElementInfo head, Moment endMoment)
        {
            Head = head;
            EndMoment = endMoment;
        }

        internal AudioElementInfo Head { get; }

        internal Moment EndMoment { get; }
    }
}
