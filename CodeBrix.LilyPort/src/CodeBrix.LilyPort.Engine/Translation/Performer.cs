/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1996--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/performer.cc, lily/include/performer.hh, lily/performer-group.cc, lily/include/performer-group.hh;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - Upstream's `template <typename T, typename... Args> T *announce (...)' builds the
//     element and announces it in one call. C# generics cannot express "construct T from
//     these arbitrary arguments", so Announce<T> here takes an ALREADY-CONSTRUCTED
//     element and announces it. Callers read `Announce (cause, new AudioNote (...))'
//     rather than `announce<Audio_note> (cause, ...)'; the order of construction and
//     announcement is the same either way.
//   - `Performer_method' (a pointer-to-member typedef) has no analogue and no upstream
//     caller in pinned 2.27.2.

/// <summary>
/// Turns music into MIDI, the way an <see cref="Engraver"/> turns it into grobs.
/// <para>
/// A performer announces <see cref="AudioElement"/>s rather than typesetting grobs. The
/// announcement travels up the context tree so that a Staff-level performer can see what
/// the Voices below it produced, which is how notes end up assigned to tracks and
/// channels.
/// </para>
/// </summary>
public abstract class Performer : Translator
{
    /// <summary>Initializes a performer in a context.</summary>
    /// <param name="context">The context this performer belongs to.</param>
    protected Performer(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this translator corresponds to.</summary>
    public override string ClassName => "Performer";

    /// <summary>
    /// Gets a value indicating whether this translator contributes to layout. A performer
    /// never does, which is what keeps performers out of an <c>Engraver_group</c> when a
    /// context definition names both.
    /// </summary>
    public override bool IsLayout => false;

    /// <summary>Gets the performer group this performer belongs to.</summary>
    /// <remarks>
    /// Upstream's static_cast is annotated "safe: Performers belong to Performer_groups".
    /// The port uses <c>as</c>, which answers null rather than reinterpreting a group of
    /// the wrong kind — a difference that only shows up if that invariant is ever broken.
    /// </remarks>
    public PerformerGroup PerformerGroup => Context?.Implementation as PerformerGroup;

    /// <summary>Announces an audio element to this performer's group.</summary>
    /// <param name="info">The announcement record.</param>
    public void AnnounceElement(AudioElementInfo info)
    {
        if (info.OriginTranslator == null)
        {
            info.OriginTranslator = this;
        }

        PerformerGroup?.AnnounceElement(info);
    }

    /// <summary>Announces an audio element, recording the event that caused it.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="cause">The causing event, or <see langword="null"/>.</param>
    /// <param name="element">The element to announce.</param>
    /// <returns>The element, so callers can keep working with it.</returns>
    protected T Announce<T>(StreamEvent cause, T element)
        where T : AudioElement
    {
        AnnounceElement(new AudioElementInfo(element, cause));
        return element;
    }

    /// <summary>
    /// Called for every element announced anywhere at or below this performer's context,
    /// except the ones this performer announced itself.
    /// </summary>
    /// <param name="info">The announcement record.</param>
    public virtual void AcknowledgeAudioElement(AudioElementInfo info)
    {
    }
}

/// <summary>
/// The set of performers living in one context — the MIDI-side twin of
/// <see cref="EngraverGroup"/>.
/// </summary>
public class PerformerGroup : TranslatorGroup
{
    /// <summary>The elements announced here and not yet acknowledged.</summary>
    protected readonly List<AudioElementInfo> AnnounceInfos = new List<AudioElementInfo>();

    /// <summary>Gets the C++ class name this group corresponds to.</summary>
    public override string ClassName => "Performer_group";

    /// <summary>
    /// Queues an announcement here and passes it to the group above.
    /// <para>
    /// Announcements travel UP, exactly as grob announcements do: this is what lets
    /// <see cref="StaffPerformer"/> see the notes made in the Voices below it and assign
    /// them to a track.
    /// </para>
    /// </summary>
    /// <param name="info">The announcement record.</param>
    public virtual void AnnounceElement(AudioElementInfo info)
    {
        AnnounceInfos.Add(info);

        if (Context?.Parent?.Implementation is PerformerGroup parentGroup)
        {
            parentGroup.AnnounceElement(info);
        }
    }

    /// <summary>
    /// Runs the announce/acknowledge round over this group and everything below it.
    /// </summary>
    /// <remarks>
    /// Simpler than <see cref="EngraverGroup.DoAnnounces"/>, and upstream's shape is
    /// kept: the children are recursed ONCE, then this group's own queue is drained until
    /// it stops refilling. There is no process-acknowledged phase on the MIDI side.
    /// </remarks>
    public void DoAnnounces()
    {
        if (Context != null)
        {
            foreach (Context child in new List<Context>(Context.Children))
            {
                if (child.Implementation is PerformerGroup group)
                {
                    group.DoAnnounces();
                }
            }
        }

        while (true)
        {
            if (AnnounceInfos.Count == 0)
            {
                break;
            }

            AcknowledgeAudioElements();
            AnnounceInfos.Clear();
        }
    }

    /// <summary>
    /// Hands every queued announcement to every performer in this group except the one
    /// that made it.
    /// </summary>
    protected virtual void AcknowledgeAudioElements()
    {
        // Indexed rather than foreach: acknowledging may announce, which appends to the
        // list being walked. Upstream's loop reads announce_infos_.size () afresh on
        // every iteration for exactly that reason.
        for (int j = 0; j < AnnounceInfos.Count; j++)
        {
            AudioElementInfo info = AnnounceInfos[j];

            foreach (Translator translator in new List<Translator>(Translators))
            {
                if (translator is Performer performer
                    && !ReferenceEquals(performer, info.OriginTranslator))
                {
                    performer.AcknowledgeAudioElement(info);
                }
            }
        }
    }
}
