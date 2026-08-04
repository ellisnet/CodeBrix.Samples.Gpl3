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

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/sequential-iterator.cc, lily/include/sequential-iterator.hh, lily/calculated-sequential-music.cc, lily/music-wrapper-iterator.cc, lily/simultaneous-music-iterator.cc, lily/event-chord-iterator.cc, lily/rhythmic-music-iterator.cc;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <summary>
/// The elements of sequential music, obtained through its own
/// <c>elements-callback</c> rather than read straight off the property.
/// <para>
/// The indirection is what lets a repeat or a part-combiner present a different
/// element list to the iterator than the one it stores.
/// </para>
/// </summary>
public static class CalculatedSequentialMusic
{
    private static readonly Symbol ElementsCallbackSymbol = Symbol.Intern("elements-callback");

    /// <summary>Returns the elements the music wants iterated.</summary>
    /// <param name="music">The sequential music.</param>
    /// <returns>The element list, or the empty list when there is no callback.</returns>
    public static object CalcElements(MusicObject music)
    {
        if (music == null)
        {
            throw new ArgumentNullException(nameof(music));
        }

        object procedure = music.GetProperty(ElementsCallbackSymbol);
        if (procedure is Procedure)
        {
            return SchemeUtilities.CallCallback(procedure, music);
        }

        Warn.ProgrammingError("calculated sequential music cannot find elements-callback");
        return Nil.Instance;
    }

    /// <summary>The <c>ly:calculated-sequential-music::length</c> callback.</summary>
    /// <param name="music">The sequential music.</param>
    /// <returns>The cumulative length of the calculated elements.</returns>
    public static Moment Length(MusicObject music)
        => MusicSequence.CumulativeLength(CalcElements(music));

    /// <summary>The <c>ly:calculated-sequential-music::start</c> callback.</summary>
    /// <param name="music">The sequential music.</param>
    /// <returns>The start moment of the calculated elements.</returns>
    public static Moment Start(MusicObject music)
        => MusicSequence.FirstStart(CalcElements(music));
}

/**
   The iterator for a #Music_wrapper#.  Since #Music_wrapper# essentially
   does nothing, this iterator creates a child iterator and delegates
   all work to that child.
*/

/// <summary>
/// The iterator for music that merely wraps one other piece of music: it makes a child
/// iterator and delegates everything to it.
/// </summary>
public class MusicWrapperIterator : MusicIterator
{
    private static readonly Symbol ElementSymbol = Symbol.Intern("element");

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Music_wrapper_iterator";

    /// <summary>
    /// Gets or sets the context, which is the CHILD's context rather than this
    /// iterator's own.
    /// <para>
    /// Answering null before the child exists is deliberate, and upstream's own
    /// choice: the code building the hierarchy knows whether the wrapped iterator has
    /// been created yet, and can ask for <see cref="MusicIterator.OwnContext"/> when it
    /// has not.
    /// </para>
    /// </summary>
    public override Context Context
    {
        get => Child?.Context;
        set
        {
            if (Child != null)
            {
                Child.Context = value;
            }

            // Keeping the wrapper's own context in step with the wrapped iterator may
            // be pure caution -- upstream records that it has found no case proving it
            // necessary, and has kept it anyway. Faithful translation keeps it too.
            OwnContext = value;
        }
    }

    /// <summary>Gets when the child's next event comes due.</summary>
    public override Moment PendingMoment => Child != null ? Child.PendingMoment : base.PendingMoment;

    /// <summary>Gets a value indicating whether the child wants to run regardless.</summary>
    public override bool RunAlways => Child != null && Child.RunAlways;

    /// <summary>Gets the child iterator.</summary>
    protected MusicIterator Child { get; private set; }

    /// <summary>Delegates processing to the child.</summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until) => Child?.Process(until);

    /// <summary>Walks this iterator and then the child.</summary>
    /// <param name="visit">The function to call.</param>
    public override void PreorderWalk(Action<MusicIterator> visit)
    {
        base.PreorderWalk(visit);
        Child?.PreorderWalk(visit);
    }

    /// <summary>Creates the child iterator for the wrapped music.</summary>
    protected override void CreateChildren()
    {
        base.CreateChildren();

        if (Music.GetProperty(ElementSymbol) is MusicObject element)
        {
            Child = CreateChild(element);
        }
    }

    /// <summary>Gives the child this iterator's own context.</summary>
    protected override void CreateContexts()
    {
        base.CreateContexts();
        Child?.InitContext(OwnContext);
    }

    /// <summary>Shuts the child down.</summary>
    protected override void DoQuit() => Child?.Quit();
}

/** Sequential_music iteration: walk each element in turn, and
    construct an iterator for every element.
*/

/// <summary>
/// The iterator for sequential music: it walks the elements one at a time, making a
/// child iterator for each and retiring it before moving on.
/// <para>
/// The look-ahead machinery is what makes grace notes work. A grace note borrows time
/// from the element it precedes, so the iterator has to know where the next element
/// with a real duration starts BEFORE it finishes the current one — which is what
/// <c>_aheadMoment</c> tracks, and why the fast-forward below exists.
/// </para>
/// </summary>
public class SequentialIterator : MusicIterator
{
    // All elements after the one the child iterator is walking.
    private object _remainingMusic = Nil.Instance;

    // The first element of the remainder that starts at a moment with a main part in
    // the future.
    private object _aheadMusic = Nil.Instance;

    // When the child is valid, its start moment relative to the zero point of the whole
    // sequence.
    private Moment _iterStartMoment;

    // Where the ahead music starts within the whole sequence; once the ahead music is
    // empty and the remainder is not, the ending point of the remainder.
    private Moment _aheadMoment = Moment.Infinity;

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Sequential_iterator";

    /// <summary>Gets when the next event comes due.</summary>
    public override Moment PendingMoment
    {
        get
        {
            if (Child == null)
            {
                // Defensive: if for any reason the full length has not been covered,
                // stay alive until the end to help keep things in sync. Normally this
                // skips to infinity.
                Moment end = MusicLength;
                return _iterStartMoment < end ? end : Moment.Infinity;
            }

            // Moments in the timeline of this sequence.
            Moment iterZero = _iterStartMoment - Child.MusicStartMoment;
            Moment iterEnd = iterZero + Child.MusicLength;
            Moment iterPending = iterZero + Child.PendingMoment;

            // Do not overshoot either: the current element's ending time might fall
            // within a span of grace notes the ahead moment already looks beyond, and
            // the ahead moment might account for grace notes that need to borrow time
            // from the current element.
            Moment nextMoment = Min(iterEnd, _aheadMoment);
            return Min(iterPending, nextMoment);
        }
    }

    /// <summary>Gets a value indicating whether the current child wants to run regardless.</summary>
    public override bool RunAlways => Child != null && Child.RunAlways;

    /// <summary>Gets the iterator for the current element.</summary>
    protected MusicIterator Child { get; private set; }

    /// <summary>Walks each element in turn, retiring its iterator before the next.</summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until)
    {
        while (Child != null)
        {
            // Moments in the timeline of this sequence.
            Moment iterZero = _iterStartMoment - Child.MusicStartMoment;
            Moment iterEnd = iterZero + Child.MusicLength;

            if (Child.Ok)
            {
                // When it is time to advance the main part, try to finish all prior
                // elements even if it is before their time once grace notes are
                // considered.
                bool fastForward = _aheadMoment <= until;
                Moment processMoment = fastForward ? iterEnd : until;
                Child.Process(processMoment - iterZero);
                if (Child.Ok)
                {
                    return;
                }
            }

            Moment nextMoment = Min(iterEnd, _aheadMoment);
            if (until < nextMoment && nextMoment < Moment.Infinity)
            {
                // The child is not ok earlier than the length of its music predicts.
                // Mitigate by waiting until the expected time so the next element
                // starts in sync.
                Warn.Warning("music is shorter than anticipated");
                return;
            }

            _iterStartMoment = nextMoment;
            DescendToChild(Child.Context);
            Child.Quit();
            Child = null;

            PopElement();
            Child?.InitContext(OwnContext);

            NextElement();
        }

        _iterStartMoment = until;
    }

    /// <summary>Walks this iterator and then the current child.</summary>
    /// <param name="visit">The function to call.</param>
    public override void PreorderWalk(Action<MusicIterator> visit)
    {
        base.PreorderWalk(visit);
        Child?.PreorderWalk(visit);
    }

    /// <summary>Reads the element list and starts on the first element.</summary>
    protected override void CreateChildren()
    {
        base.CreateChildren();

        _remainingMusic = CalculatedSequentialMusic.CalcElements(Music);
        _aheadMusic = _remainingMusic;
        _iterStartMoment = MusicStartMoment;
        _aheadMoment = _iterStartMoment;

        PopElement();
    }

    /// <summary>Gives the first child a context and follows it down the tree.</summary>
    protected override void CreateContexts()
    {
        base.CreateContexts();

        if (Child != null)
        {
            Child.InitContext(OwnContext);
            DescendToChild(Child.Context);
        }
    }

    /// <summary>Shuts the current child down.</summary>
    protected override void DoQuit() => Child?.Quit();

    /// <summary>Subclass hook, run each time the iterator moves to the next element.</summary>
    protected virtual void NextElement()
    {
    }

    private static Moment Min(Moment left, Moment right) => left < right ? left : right;

    private void LookAhead()
    {
        // Move past elements that have no main duration, then past the first one that
        // has some.
        while (_aheadMusic is Pair pair)
        {
            MusicObject music = pair.Car as MusicObject;
            _aheadMusic = pair.Cdr;

            if (music != null)
            {
                // Paranoia; other things should have complained already.
                Moment endMoment = music.GetLength();
                if (endMoment.MainPart > Rational.Zero)
                {
                    _aheadMoment = new Moment(
                        _aheadMoment.MainPart + endMoment.MainPart,
                        _aheadMoment.GracePart);
                    break;
                }
            }
        }

        // The state now resembles that of sequential music before its start-callback
        // ran, with fewer elements. The accumulated main part is kept.
        Moment startMoment = MusicSequence.FirstStart(_aheadMusic);
        _aheadMoment = new Moment(_aheadMoment.MainPart, startMoment.GracePart);
    }

    private void PopElement()
    {
        Child = null;

        if (!HaveReadyMusic())
        {
            LookAhead();
        }

        if (HaveReadyMusic())
        {
            Pair remaining = (Pair)_remainingMusic;
            object musicValue = remaining.Car;
            _remainingMusic = remaining.Cdr;

            if (!(_remainingMusic is Pair))
            {
                // That was the last one.
                _aheadMoment = Moment.Infinity;
            }

            if (musicValue is MusicObject music)
            {
                Child = CreateChild(music);
            }
        }

        if (Child == null && _iterStartMoment != MusicLength)
        {
            // End of the sequence, and a callback may have provided music inconsistent
            // with the precomputed length.
            Warn.Warning("total length of sequential music elements is different than anticipated");
        }
    }

    private bool HaveReadyMusic() => !ReferenceEquals(_remainingMusic, _aheadMusic);
}

/// <summary>
/// The iterator for simultaneous music: every element gets its own child iterator, and
/// they all run at once.
/// </summary>
public class SimultaneousMusicIterator : MusicIterator
{
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");

    private readonly List<MusicIterator> _children = new List<MusicIterator>();

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Simultaneous_music_iterator";

    /// <summary>Gets when the earliest child's next event comes due.</summary>
    public override Moment PendingMoment
    {
        get
        {
            Moment next = Moment.Infinity;
            foreach (MusicIterator child in _children)
            {
                Moment pending = child.PendingMoment;
                if (pending < next)
                {
                    next = pending;
                }
            }

            return next;
        }
    }

    /// <summary>Gets a value indicating whether any child wants to run regardless.</summary>
    public override bool RunAlways
    {
        get
        {
            foreach (MusicIterator child in _children)
            {
                if (child.RunAlways)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>Gets the child iterators still running.</summary>
    protected IReadOnlyList<MusicIterator> Children => _children;

    /// <summary>Runs every child that is due, and retires the ones that are finished.</summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until)
    {
        bool finite = !PendingMoment.MainPart.IsInfinite;

        for (int index = 0; index < _children.Count; /* in loop */)
        {
            MusicIterator child = _children[index];
            if (child.RunAlways || child.PendingMoment == until)
            {
                child.Process(until);
            }

            if (!child.Ok)
            {
                child.Quit();
                _children.RemoveAt(index);
            }
            else
            {
                index++;
            }
        }

        // If there were definite-ended iterators and all of them died, take the rest
        // along with them: they have likely lost their reference iterators. Basing this
        // on the actual music contexts is not reliable, because simultaneous music
        // containing a named Voice and lyrics addressed to it cannot wait for that
        // context to die before ending.
        if (finite && PendingMoment.MainPart.IsInfinite)
        {
            foreach (MusicIterator child in _children)
            {
                child.Quit();
            }

            _children.Clear();
        }
    }

    /// <summary>Walks this iterator and then every child.</summary>
    /// <param name="visit">The function to call.</param>
    public override void PreorderWalk(Action<MusicIterator> visit)
    {
        base.PreorderWalk(visit);
        foreach (MusicIterator child in new List<MusicIterator>(_children))
        {
            child.PreorderWalk(visit);
        }
    }

    /// <summary>Creates one child iterator per element.</summary>
    protected override void CreateChildren()
    {
        base.CreateChildren();

        _children.Clear();
        object cursor = Music.GetProperty(ElementsSymbol);
        while (cursor is Pair pair)
        {
            if (pair.Car is MusicObject element)
            {
                _children.Add(CreateChild(element));
            }

            cursor = pair.Cdr;
        }
    }

    /// <summary>Gives every child a context, dropping the ones that have nothing to do.</summary>
    protected override void CreateContexts()
    {
        base.CreateContexts();

        Context myContext = Context;
        for (int index = 0; index < _children.Count; /* in loop */)
        {
            MusicIterator child = _children[index];
            child.InitContext(myContext);

            // Why might a newly created iterator not be ok? A Sequential_iterator with
            // no elements, for one.
            if (!child.Ok)
            {
                child.Quit();
                _children.RemoveAt(index);
            }
            else
            {
                index++;
            }
        }

        // A sequential iterator follows its children into their contexts. This one does
        // not -- which child would it follow? At least avoid squatting in Global.
        DescendToUserAccessibleContext();
    }

    /// <summary>Shuts every child down.</summary>
    protected override void DoQuit()
    {
        foreach (MusicIterator child in _children)
        {
            child.Quit();
        }
    }
}

/// <summary>
/// The iterator for a chord: it reports every element and every articulation once, at
/// the start.
/// </summary>
public class EventChordIterator : SimpleMusicIterator
{
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol ArticulationsSymbol = Symbol.Intern("articulations");

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Event_chord_iterator";

    /// <summary>Reports every note and articulation in the chord, then advances.</summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until)
    {
        if (!HasStarted)
        {
            ReportAll(Music.GetProperty(ElementsSymbol));
            ReportAll(Music.GetProperty(ArticulationsSymbol));
        }

        base.Process(until);
    }

    /// <summary>Descends to a leaf context after the base class has set up.</summary>
    protected override void CreateContexts()
    {
        base.CreateContexts();
        DescendToBottomContext();
    }

    private void ReportAll(object list)
    {
        object cursor = list;
        while (cursor is Pair pair)
        {
            if (pair.Car is MusicObject music)
            {
                ReportEvent(music);
            }

            cursor = pair.Cdr;
        }
    }
}

/// <summary>
/// The iterator for a single rhythmic event — a note or a rest.
/// <para>
/// It differs from <see cref="EventIterator"/> in how it treats articulations. An
/// articulation nobody listens for stays attached to the note event, while one that
/// some engraver does listen for is broadcast separately. That split is not
/// cosmetic: a harmonic event works only as an attached articulation, and
/// broadcasting it would be noise.
/// </para>
/// </summary>
public class RhythmicMusicIterator : SimpleMusicIterator
{
    private static readonly Symbol ArticulationsSymbol = Symbol.Intern("articulations");
    private static readonly Symbol ClassSymbol = Symbol.Intern("class");

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Rhythmic_music_iterator";

    /// <summary>Broadcasts the note event, splitting listened-for articulations out.</summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until)
    {
        if (!HasStarted)
        {
            DescendToBottomContext();

            Context context = Context;
            StreamEvent streamEvent = Music.ToEvent();
            object articulations = streamEvent.GetProperty(ArticulationsSymbol);

            if (articulations is Pair)
            {
                List<object> listened = new List<object>();
                List<object> unlistened = new List<object>();

                object cursor = articulations;
                while (cursor is Pair pair)
                {
                    object articulation = pair.Car;
                    bool isListened = articulation is StreamEvent candidate
                                      && context != null
                                      && context.EventSource.IsListenedClass(
                                          candidate.GetProperty(ClassSymbol));

                    (isListened ? listened : unlistened).Add(articulation);
                    cursor = pair.Cdr;
                }

                streamEvent.SetProperty(ArticulationsSymbol, Pair.ListFrom(unlistened));
                context?.EventSource.Broadcast(streamEvent);

                foreach (object articulation in listened)
                {
                    if (articulation is StreamEvent listenedEvent)
                    {
                        context?.EventSource.Broadcast(listenedEvent);
                    }
                }
            }
            else
            {
                context?.EventSource.Broadcast(streamEvent);
            }
        }

        base.Process(until);
    }

    /// <summary>Descends to a leaf context after the base class has set up.</summary>
    protected override void CreateContexts()
    {
        base.CreateContexts();
        DescendToBottomContext();
    }
}
