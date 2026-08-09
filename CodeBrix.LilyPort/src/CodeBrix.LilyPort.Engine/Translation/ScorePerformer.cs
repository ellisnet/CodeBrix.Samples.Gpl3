/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Jan Nieuwenhuizen <janneke@gnu.org>

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

using CodeBrix.LilyPort.Engine.Audio;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/score-performer.cc, lily/include/score-performer.hh;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - derived_mark() is not carried; see the note on AudioElement.
//   - This RETIRES PerformerGroupPlaceholder, the empty stand-in TranslatorCreator has
//     returned for both Performer_group and Score_performer since EPG2.

/// <summary>
/// The top-level performer: it owns the <see cref="Performance"/> and drives every
/// timestep on the MIDI side.
/// <para>
/// The exact counterpart of <see cref="ScoreEngraver"/>, and it listens to the same three
/// events on the top context. Where the engraver runs an announce/acknowledge round and
/// typesets into a system, this one runs the same round and drops audio items into the
/// <see cref="AudioColumn"/> for the current moment.
/// </para>
/// </summary>
public sealed class ScorePerformer : PerformerGroup
{
    private static readonly Symbol OneTimeStepSymbol = Symbol.Intern("OneTimeStep");
    private static readonly Symbol PrepareSymbol = Symbol.Intern("Prepare");
    private static readonly Symbol FinishSymbol = Symbol.Intern("Finish");
    private static readonly Symbol MomentSymbol = Symbol.Intern("moment");
    private static readonly Symbol OutputSymbol = Symbol.Intern("output");
    private static readonly Symbol MidiChannelMappingSymbol
        = Symbol.Intern("midiChannelMapping");
    private static readonly Symbol MidiSkipOffsetSymbol = Symbol.Intern("midiSkipOffset");
    private static readonly Symbol SkipTypesettingSymbol = Symbol.Intern("skipTypesetting");
    private static readonly Symbol VoiceSymbol = Symbol.Intern("voice");

    private AudioColumn _audioColumn;
    private bool _skipping;
    private Moment _skipLastMoment;
    private Moment _offsetMoment;

    private Listener _oneTimeStepListener;
    private Listener _prepareListener;
    private Listener _finishListener;

    /// <summary>Gets the performance this group built.</summary>
    public Performance Performance { get; private set; }

    /// <summary>Gets the C++ class name this group corresponds to.</summary>
    public override string ClassName => "Score_performer";

    /// <summary>Creates the performance, before any music is interpreted.</summary>
    public override void Initialize()
    {
        Performance = new Performance();
        Context?.SetProperty(OutputSymbol, Performance);
        Performance.Midi = Context?.OutputDef;

        base.Initialize();
    }

    /// <summary>Attaches to a context and starts listening to the TOP context.</summary>
    /// <param name="context">The context to attach to.</param>
    public override void ConnectToContext(Context context)
    {
        base.ConnectToContext(context);

        Dispatcher source = context?.Root?.EventSource;
        if (source == null)
        {
            return;
        }

        _oneTimeStepListener = source.AddListener(this, OneTimeStep, OneTimeStepSymbol);
        _prepareListener = source.AddListener(this, Prepare, PrepareSymbol);
        _finishListener = source.AddListener(this, Finish, FinishSymbol);
    }

    /// <summary>Detaches from the context and stops listening.</summary>
    public override void DisconnectFromContext()
    {
        Dispatcher source = Context?.Root?.EventSource;
        if (source != null)
        {
            if (_oneTimeStepListener != null)
            {
                source.RemoveListener(_oneTimeStepListener, OneTimeStepSymbol);
            }

            if (_prepareListener != null)
            {
                source.RemoveListener(_prepareListener, PrepareSymbol);
            }

            if (_finishListener != null)
            {
                source.RemoveListener(_finishListener, FinishSymbol);
            }
        }

        _oneTimeStepListener = null;
        _prepareListener = null;
        _finishListener = null;

        base.DisconnectFromContext();
    }

    /// <summary>
    /// Queues an announcement, collecting staves as tracks and every element into the
    /// performance.
    /// </summary>
    /// <param name="info">The announcement record.</param>
    public override void AnnounceElement(AudioElementInfo info)
    {
        // NOT a call to base: upstream's Score_performer::announce_element does the
        // push_back itself and does NOT walk to a parent group, because there is no
        // performer group above the Score.
        AnnounceInfos.Add(info);

        if (info.Element is AudioStaff staff)
        {
            Performance.AudioStaffs.Add(staff);
        }

        Performance.AddElement(info.Element, info.Event);
    }

    /// <summary>Starts a timestep across the whole context tree.</summary>
    /// <param name="streamEvent">The <c>Prepare</c> event.</param>
    public void Prepare(StreamEvent streamEvent)
    {
        Moment moment = streamEvent?.GetProperty(MomentSymbol) is Moment m ? m : Moment.Zero;

        _audioColumn = new AudioColumn(moment);
        AnnounceElement(new AudioElementInfo(_audioColumn, null));

        RecurseOverTranslators(
            Context, TranslatorPrecomputeIndex.StartTranslationTimestep, Direction.Negative);
    }

    /// <summary>Runs one timestep across the whole context tree.</summary>
    /// <param name="streamEvent">The <c>OneTimeStep</c> event.</param>
    public void OneTimeStep(StreamEvent streamEvent)
    {
        // audio_column_ can be 0 when prepare has not been called. The condition is
        // triggered when Simple_music_iterator implicitly creates a Score context, like
        // when writing
        //
        // \score { { | c4 c c c } \midi { } }
        //
        // The same situation happens with the Score_engraver group, but it would appear
        // not to suffer any bad side effects.
        if (_audioColumn == null)
        {
            _audioColumn = new AudioColumn(Context?.NowMoment ?? Moment.Zero);
        }

        if (_skipping)
        {
            _offsetMoment -= _audioColumn.When() - _skipLastMoment;
            Context?.SetProperty(MidiSkipOffsetSymbol, _offsetMoment);
        }

        _skipping = SchemeUtilities.ToBool(Context?.GetProperty(SkipTypesettingSymbol));

        if (_skipping)
        {
            _skipLastMoment = _audioColumn.When();
        }

        _audioColumn.OffsetWhen(_offsetMoment);

        RecurseOverTranslators(
            Context, TranslatorPrecomputeIndex.PreProcessMusic, Direction.Positive);

        if (!_skipping)
        {
            RecurseOverTranslators(
                Context, TranslatorPrecomputeIndex.ProcessMusic, Direction.Positive);
            DoAnnounces();
        }

        RecurseOverTranslators(
            Context, TranslatorPrecomputeIndex.StopTranslationTimestep, Direction.Positive);
    }

    /// <summary>Finishes the performance and finalizes every translator.</summary>
    /// <param name="streamEvent">The <c>Finish</c> event.</param>
    public void Finish(StreamEvent streamEvent)
    {
        object channelMapping = Context?.GetProperty(MidiChannelMappingSymbol);
        Performance.Ports = ReferenceEquals(channelMapping, VoiceSymbol);

        RecurseFinalize(Context, Direction.Positive);
    }

    /// <summary>
    /// Drops every announced audio item into the current column, then acknowledges as
    /// usual.
    /// </summary>
    protected override void AcknowledgeAudioElements()
    {
        for (int i = 0; i < AnnounceInfos.Count; i++)
        {
            if (AnnounceInfos[i].Element is AudioItem item)
            {
                _audioColumn?.AddAudioItem(item);
            }
        }

        base.AcknowledgeAudioElements();
    }
}
