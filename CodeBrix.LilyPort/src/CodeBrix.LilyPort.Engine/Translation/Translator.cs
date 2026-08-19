/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/translator.cc, lily/include/translator.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The points in one timestep at which a translator can act, in the order they run.
/// <para>
/// Upstream precomputes the method bindings for these five, because they are called
/// for every translator at every timestep and the virtual dispatch showed up in
/// profiles.
/// </para>
/// </summary>
public enum TranslatorPrecomputeIndex
{
    /// <summary>Before anything else in the timestep.</summary>
    StartTranslationTimestep = 0,

    /// <summary>After everything else in the timestep.</summary>
    StopTranslationTimestep = 1,

    /// <summary>Before <see cref="ProcessMusic"/>, for translators that must go first.</summary>
    PreProcessMusic = 2,

    /// <summary>Where most engravers create their grobs.</summary>
    ProcessMusic = 3,

    /// <summary>After acknowledging, for reacting to what others created.</summary>
    ProcessAcknowledged = 4,
}

/*
  Translate music into grobs.
*/

/// <summary>
/// Turns music into something else: an engraver turns it into grobs, a performer into
/// MIDI.
/// <para>
/// A translator lives in a <see cref="Context"/> and is driven by the timestep
/// protocol below. It hears stream events through listeners its context's dispatcher
/// routes to it, and it reads context properties to decide what to do.
/// </para>
/// </summary>
public abstract class Translator
{
    private static readonly Symbol LengthSymbol = Symbol.Intern("length");

    private readonly List<(Symbol EventClass, Listener Listener)> _listeners
        = new List<(Symbol, Listener)>();

    /// <summary>Initializes a translator in a context.</summary>
    /// <param name="context">The context this translator belongs to.</param>
    protected Translator(Context context) => Context = context;

    /// <summary>Gets the context this translator lives in.</summary>
    public Context Context { get; internal set; }

    /// <summary>Gets the C++ class name this translator corresponds to.</summary>
    public virtual string ClassName => "Translator";

    /// <summary>Gets the translator's name, as contexts refer to it.</summary>
    public virtual string Name => ClassName;

    /// <summary>
    /// Gets a value indicating whether this translator must run last among its
    /// siblings.
    /// </summary>
    public virtual bool MustBeLast => false;

    /// <summary>Gets a value indicating whether this translator contributes to MIDI.</summary>
    public virtual bool IsMidi => true;

    /// <summary>Gets a value indicating whether this translator contributes to layout.</summary>
    public virtual bool IsLayout => true;

    /// <summary>Gets the moment the context is currently at.</summary>
    public virtual Moment NowMoment => Context != null ? Context.NowMoment : Moment.Zero;

    /// <summary>Gets the translator group this translator belongs to.</summary>
    public TranslatorGroup Group => Context?.Implementation;

    /// <summary>Reads a context property.</summary>
    /// <param name="symbol">The property name.</param>
    /// <returns>The value, or the empty list when unset.</returns>
    public object GetProperty(Symbol symbol)
        => Context != null ? Context.GetProperty(symbol) : Nil.Instance;

    /// <summary>Reads a context property by name.</summary>
    /// <param name="name">The property name.</param>
    /// <returns>The value.</returns>
    public object GetProperty(string name) => GetProperty(Symbol.Intern(name));

    /// <summary>Returns how long an event lasts.</summary>
    /// <param name="e">The event.</param>
    /// <returns>The length, or zero when the event declares none.</returns>
    public static Moment GetEventLength(StreamEvent e)
        => e?.GetProperty(LengthSymbol) is Moment length ? length : Moment.Zero;

    /// <summary>
    /// Returns how long an event lasts, as measured at a given moment: inside grace
    /// time the whole length moves into the GRACE part, because grace notes take no
    /// main-part time at all.
    /// </summary>
    /// <param name="e">The event.</param>
    /// <param name="now">The moment being translated.</param>
    /// <returns>The length.</returns>
    public static Moment GetEventLength(StreamEvent e, Moment now)
    {
        Moment len = GetEventLength(e);

        if (now.GracePart.IsNonZero)
        {
            return new Moment(Rational.Zero, len.MainPart);
        }

        return len;
    }

    /// <summary>Called once when the translator is attached to its context.</summary>
    public virtual void ConnectToContext()
    {
    }

    /// <summary>Called once before the first timestep.</summary>
    public virtual void Initialize()
    {
    }

    /// <summary>
    /// Called once after the last timestep.
    /// <para>
    /// Named <c>FinalizeTranslation</c>, not <c>Finalize</c>: in C# that name is
    /// <see cref="object.Finalize"/>, the destructor, and overriding it would hand
    /// this method to the garbage collector instead of to the timestep protocol.
    /// </para>
    /// </summary>
    public virtual void FinalizeTranslation()
    {
    }

    /// <summary>Called once when the translator is detached from its context.</summary>
    public virtual void DisconnectFromContext()
    {
    }

    /// <summary>Called at the start of every timestep.</summary>
    public virtual void StartTranslationTimestep()
    {
    }

    /// <summary>Called before <see cref="ProcessMusic"/>.</summary>
    public virtual void PreProcessMusic()
    {
    }

    /// <summary>
    /// Called once per timestep. This is where most engravers create their grobs.
    /// </summary>
    public virtual void ProcessMusic()
    {
    }

    /// <summary>Called after grobs have been acknowledged.</summary>
    public virtual void ProcessAcknowledged()
    {
    }

    /// <summary>Called at the end of every timestep.</summary>
    public virtual void StopTranslationTimestep()
    {
    }

    /// <summary>
    /// Registers a handler for an event class on this translator's context.
    /// <para>
    /// Upstream builds this table at class-definition time from
    /// <c>ADD_LISTENER</c>; the port registers at connect time instead, which is the
    /// same set of listeners reached by a route C# can express without macros. The
    /// divergence is recorded in PORT-COVERAGE.
    /// </para>
    /// <para>
    /// THE DISPATCHER IS <see cref="Context.EventsBelow"/>, not
    /// <see cref="Context.EventSource"/>, and that is upstream's choice rather than a
    /// convenience: <c>Translator_group::connect_to_context</c> takes
    /// <c>c-&gt;events_below ()</c> and registers every translator's listeners there. It
    /// is what lets a Staff-level engraver hear an event that happened in a Voice below
    /// it — a Staff engraver registered on the context's own event source hears only
    /// what was addressed AT the Staff, which is silently less. Events sent to this
    /// context still arrive exactly once, because <c>SendStreamEvent</c> broadcasts on
    /// both dispatchers.
    /// </para>
    /// </summary>
    /// <param name="eventClass">The event class to listen for.</param>
    /// <param name="handler">The handler.</param>
    protected void ListenTo(string eventClass, Action<StreamEvent> handler)
        => ListenTo(Symbol.Intern(eventClass), handler);

    /// <summary>Registers a handler for an event class on this translator's context.</summary>
    /// <param name="eventClass">The event class to listen for.</param>
    /// <param name="handler">The handler.</param>
    protected void ListenTo(Symbol eventClass, Action<StreamEvent> handler)
    {
        if (Context == null)
        {
            Warn.ProgrammingError("cannot listen without a context");
            return;
        }

        Listener listener = Context.EventsBelow.AddListener(this, handler, eventClass);
        _listeners.Add((eventClass, listener));
    }

    /// <summary>Removes every listener this translator registered.</summary>
    protected void RemoveListeners()
    {
        if (Context == null)
        {
            return;
        }

        foreach ((Symbol EventClass, Listener Listener) entry in _listeners)
        {
            Context.EventsBelow.RemoveListener(entry.Listener, entry.EventClass);
        }

        _listeners.Clear();
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The translator's name.</returns>
    //was previously: => "#<Translator " + Name + ">";  Upstream's
    // Translator::print_smob (lily/translator.cc:194-200) puts a SPACE before the
    // closing bracket, exactly as Grob's does.
    public override string ToString() => "#<Translator " + Name + " >";
}
