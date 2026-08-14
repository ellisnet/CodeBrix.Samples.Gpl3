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

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/music-iterator.cc, lily/include/music-iterator.hh, lily/simple-music-iterator.cc, lily/event-iterator.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/**
   ---

   Music_iterator is an object type that traverses the Music structure and
   reports the events it finds to interpretation contexts. It is not yet
   user-serviceable.
*/

/// <summary>
/// Walks the music tree and reports what it finds to interpretation contexts.
/// <para>
/// Conceptually an iterator traverses a queue of pending musical events, without any
/// queue actually existing. Three members carry that model:
/// <see cref="PendingMoment"/> is when the next event comes due (infinity once the
/// queue is empty), <see cref="Ok"/> says processing is incomplete, and
/// <see cref="Process"/> handles everything due at a moment and removes it from the
/// notional queue.
/// </para>
/// <para>
/// This is the piece that connects the music tree to the already-ported dispatcher:
/// an iterator turns music into <see cref="StreamEvent"/>s and broadcasts them at a
/// context, which is where engravers hear them.
/// </para>
/// </summary>
public class MusicIterator
{
    private static readonly Symbol IteratorCtorSymbol = Symbol.Intern("iterator-ctor");
    private static readonly Symbol ElementSymbol = Symbol.Intern("element");
    private static readonly Symbol EventSymbol = Symbol.Intern("event");
    private static readonly Symbol TagsSymbol = Symbol.Intern("tags");

    // Upstream's Music_iterator holds its own context through a Context_handle
    // (`Context_handle handle_;', with get_own_context/set_own_context going through it),
    // and that is LOAD-BEARING rather than bookkeeping: the handle's client count is what
    // makes Context::is_removable answer false for a context an iterator is still
    // reporting to. The port kept a plain field here at first, so EVERY context read as
    // removable the moment it had no children, and any code asking is_removable about a
    // live context got the wrong answer -- which is how a whole \lyricsto branch was being
    // dropped at create_contexts time.
    private readonly ContextHandle _handle = new ContextHandle();

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public virtual string ClassName => "Music_iterator";

    /// <summary>Gets the iterator that created this one, or null at the top.</summary>
    public MusicIterator Parent { get; private set; }

    /// <summary>Gets the music this iterator walks.</summary>
    public MusicObject Music { get; private set; }

    /// <summary>Gets the length of the music this iterator walks.</summary>
    public Moment MusicLength { get; private set; }

    /// <summary>
    /// Gets where in the source this iterator's music came from, for a diagnostic or an
    /// event's <c>origin</c>.
    /// <para>Upstream: <c>Music_iterator::origin ()</c>, which is
    /// <c>get_music ()-&gt;origin ()</c>.</para>
    /// </summary>
    public object Origin => Music?.Origin;

    /// <summary>
    /// Gets when this music starts, relative to where the iterator sits in the stream.
    /// Non-zero only for expressions that begin with grace notes.
    /// </summary>
    public Moment MusicStartMoment { get; private set; }

    /// <summary>
    /// Gets or sets the context this iterator reports to.
    /// <para>
    /// Virtual because a subclass may address a CHILD iterator's context instead of its
    /// own — <see cref="MusicWrapperIterator"/> does. Where that must not happen, use
    /// <see cref="OwnContext"/>.
    /// </para>
    /// </summary>
    public virtual Context Context
    {
        get => OwnContext;
        set => OwnContext = value;
    }

    /// <summary>
    /// Gets or sets this very iterator's context, going around the virtual
    /// <see cref="Context"/>.
    /// </summary>
    public Context OwnContext
    {
        get => _handle.Context;
        set => _handle.Set(value);
    }

    /// <summary>
    /// Gets when the next event comes due, or infinity when the queue is empty.
    /// </summary>
    public virtual Moment PendingMoment => Moment.Infinity;

    /// <summary>
    /// Gets a value indicating whether processing is incomplete: either events remain,
    /// or the iterator wants to keep running regardless.
    /// </summary>
    public bool Ok => PendingMoment < Moment.Infinity || RunAlways;

    /// <summary>
    /// Gets a value indicating whether <see cref="Process"/> should be called even when
    /// the moment is earlier than <see cref="PendingMoment"/>.
    /// </summary>
    public virtual bool RunAlways => false;

    /// <summary>Creates an iterator with no parent.</summary>
    /// <param name="music">The music to walk.</param>
    /// <returns>The iterator.</returns>
    public static MusicIterator CreateTopIterator(MusicObject music) => CreateIterator(null, music);

    /// <summary>Creates an iterator whose parent is this one.</summary>
    /// <param name="music">The music to walk.</param>
    /// <returns>The child iterator.</returns>
    public MusicIterator CreateChild(MusicObject music) => CreateIterator(this, music);

    /// <summary>
    /// Determines whether one context is the same as, or below, another.
    /// </summary>
    /// <param name="me">The context to test against.</param>
    /// <param name="child">The candidate descendant.</param>
    /// <returns><see langword="true"/> when <paramref name="child"/> is at or below <paramref name="me"/>.</returns>
    public static bool IsChildContext(Context me, Context child)
    {
        while (child != null && !ReferenceEquals(child, me))
        {
            child = child.Parent;
        }

        return ReferenceEquals(child, me);
    }

    /// <summary>
    /// Gives this iterator the context it reports to, and lets it build its children's
    /// contexts. Called once.
    /// </summary>
    /// <param name="report">The context to report to.</param>
    public void InitContext(Context report)
    {
        if (OwnContext == null)
        {
            OwnContext = report;
            CreateContexts();
        }
        else
        {
            Warn.ProgrammingError("context already initialized; skipping");
        }
    }

    /// <summary>Replaces one context with another. Not recursive.</summary>
    /// <param name="from">The context to replace.</param>
    /// <param name="to">The replacement.</param>
    public virtual void SubstituteContext(Context from, Context to)
    {
        if (!ReferenceEquals(from, to) && ReferenceEquals(OwnContext, from))
        {
            OwnContext = to;
        }
    }

    /// <summary>Processes everything due at a moment, relative to this iterator's start.</summary>
    /// <param name="until">The moment to process up to.</param>
    public virtual void Process(Moment until)
    {
    }

    /// <summary>Shuts the iterator down and releases its context.</summary>
    public void Quit()
    {
        DoQuit();
        OwnContext = null;
    }

    /// <summary>Calls a function on this iterator and then on all of its children.</summary>
    /// <param name="visit">The function to call.</param>
    public virtual void PreorderWalk(Action<MusicIterator> visit) => visit?.Invoke(this);

    /// <summary>
    /// Searches this iterator's music and then its ancestors' for one of a music type.
    /// </summary>
    /// <param name="type">The music type to look for.</param>
    /// <returns>The first iterator whose music has the type, or null.</returns>
    public MusicIterator FindAboveByMusicType(Symbol type)
    {
        for (MusicIterator scope = this; scope != null; scope = scope.Parent)
        {
            if (scope.Music != null && scope.Music.IsMusicType(type))
            {
                return scope;
            }
        }

        return null;
    }

    /// <summary>
    /// Reads a property from this iterator's music, then from its ancestors'.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <returns>The value, or the empty list when set nowhere.</returns>
    public object GetProperty(Symbol name)
    {
        MusicIterator where = WhereDefined(name, out object value);
        return where != null ? value : Nil.Instance;
    }

    /// <summary>
    /// Returns the iterator whose music defines a property, searching up the ancestors.
    /// </summary>
    /// <param name="name">The property name.</param>
    /// <param name="value">Receives the value when found.</param>
    /// <returns>The iterator that defines it, or null.</returns>
    public MusicIterator WhereDefined(Symbol name, out object value)
    {
        for (MusicIterator scope = this; scope != null; scope = scope.Parent)
        {
            if (scope.Music != null)
            {
                object candidate = scope.Music.GetProperty(name);
                if (!(candidate is Nil))
                {
                    value = candidate;
                    return scope;
                }
            }
        }

        value = Nil.Instance;
        return null;
    }

    /// <summary>
    /// Finds the nearest enclosing iterator whose music carries a tag, or this one.
    /// </summary>
    /// <param name="tag">The tag symbol.</param>
    /// <returns>The tagged iterator, or this iterator when none is tagged.</returns>
    public MusicIterator WhereTagged(Symbol tag)
    {
        MusicIterator scope = this;
        if (tag == null)
        {
            return scope;
        }

        for (MusicIterator candidate = scope; candidate != null; /* in loop */)
        {
            MusicIterator where = candidate.WhereDefined(TagsSymbol, out object tags);
            if (where == null)
            {
                break;
            }

            if (SchemeUtilities.Memq(tag, tags))
            {
                scope = where;
                break;
            }

            candidate = where.Parent;
        }

        return scope;
    }

    /// <summary>Turns a piece of music into an event and broadcasts it at the context.</summary>
    /// <param name="music">The music to report.</param>
    public void ReportEvent(MusicObject music)
    {
        DescendToBottomContext();

        /*
          FIXME: then don't do it.
        */
        if (!music.IsMusicType(EventSymbol))
        {
            Warn.ProgrammingError("Sending non-event to context");
        }

        music.SendToContext(Context);
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The iterator's class name.</returns>
    public override string ToString() => "#<" + ClassName + ">";

    /// <summary>
    /// Called after the iterator has its music but before it has its context. The first
    /// chance to set up state that depends on music properties, and the last chance to
    /// set up state timing needs — which is why an iterator that manages children
    /// creates them here.
    /// </summary>
    protected virtual void CreateChildren()
    {
    }

    /// <summary>
    /// Called after this iterator's own context is set. It must initialise the contexts
    /// of any child iterators.
    /// <para>
    /// Think twice before reading context properties here: other iterators'
    /// <see cref="Process"/> may run after this and before this iterator's does.
    /// </para>
    /// </summary>
    protected virtual void CreateContexts()
    {
    }

    /// <summary>Subclass hook for shutdown, run before the context is released.</summary>
    protected virtual void DoQuit()
    {
    }

    /// <summary>Descends to a context that accepts no children, creating one if needed.</summary>
    protected void DescendToBottomContext()
    {
        if (Context == null)
        {
            Warn.ProgrammingError("no context to descend from");
            return;
        }

        if (!Context.IsBottomContext)
        {
            Context bottom = Context.GetDefaultInterpreter();
            if (bottom != null)
            {
                Context = bottom;
            }
        }
    }

    /// <summary>Moves into a child iterator's context when it is deeper down the tree.</summary>
    /// <param name="childReport">The child's context.</param>
    protected void DescendToChild(Context childReport)
    {
        if (IsChildContext(Context, childReport))
        {
            Context = childReport;
        }
    }

    /// <summary>
    /// Concretely: if the current context is Global, descend to Score.
    /// </summary>
    protected void DescendToUserAccessibleContext()
    {
        Context context = Context;
        if (context == null)
        {
            Warn.ProgrammingError("no context to descend from");
            return;
        }

        if (!context.IsAccessibleToUser)
        {
            Context accessible = context.GetUserAccessibleInterpreter();
            if (accessible != null)
            {
                Context = accessible;
            }
            else
            {
                Warn.ProgrammingError("cannot find an accessible context");
            }
        }
    }

    private static MusicIterator CreateIterator(MusicIterator parent, MusicObject music)
    {
        if (music == null)
        {
            throw new ArgumentNullException(nameof(music));
        }

        MusicIterator iterator = null;

        // Upstream reads an iterator-ctor property naming a C++ constructor callback,
        // set by scm/define-music-types.scm. The port registers those same callbacks as
        // real primitives (MusicIteratorPrimitives), so this path is upstream's path
        // rather than a lookup table of our own -- and a music type whose iterator is
        // not ported yet falls through to the same defaults upstream falls through to,
        // which is what keeps unported types merely limited instead of fatal.
        object constructor = music.GetProperty(IteratorCtorSymbol);
        if (constructor is Procedure)
        {
            iterator = SchemeUtilities.CallCallback(constructor) as MusicIterator;
        }

        if (iterator == null)
        {
            if (music.GetProperty(ElementSymbol) is MusicObject)
            {
                iterator = new MusicWrapperIterator();
            }
            else if (music.IsMusicType(EventSymbol))
            {
                iterator = new EventIterator();
            }
            else
            {
                iterator = new SimpleMusicIterator();
            }
        }

        iterator.Parent = parent;
        iterator.Music = music;
        iterator.MusicLength = music.GetLength();
        iterator.MusicStartMoment = music.StartMoment();

        iterator.CreateChildren();

        return iterator;
    }
}

/*
  Iterator for atomic music objects: events are generated at the
  beginning and at the end of the music.
*/

/// <summary>
/// The iterator for music with no internal structure: it comes due once at the start
/// and once at the end, and does nothing in between.
/// </summary>
public class SimpleMusicIterator : MusicIterator
{
    private Moment _pendingMoment;

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Simple_music_iterator";

    /// <summary>Gets when the next event comes due.</summary>
    public sealed override Moment PendingMoment => _pendingMoment;

    /// <summary>Gets a value indicating whether the start has already been reported.</summary>
    protected bool HasStarted => _pendingMoment > MusicStartMoment;

    /// <summary>Advances past the music, then reports nothing further.</summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until)
    {
        Moment length = MusicLength;
        _pendingMoment = until < length ? length : Moment.Infinity;
    }

    /// <summary>Starts the pending moment at the music's own start.</summary>
    protected override void CreateChildren()
    {
        base.CreateChildren();
        _pendingMoment = MusicStartMoment;
    }
}

/// <summary>
/// The iterator for a single event: it reports its music once, at the start.
/// </summary>
public class EventIterator : SimpleMusicIterator
{
    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Event_iterator";

    /// <summary>Reports the event the first time round, then behaves as simple music.</summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until)
    {
        if (!HasStarted)
        {
            ReportEvent(Music);
        }

        base.Process(until);
    }

    /// <summary>Descends to a leaf context before reporting.</summary>
    protected override void CreateContexts()
    {
        DescendToBottomContext();
        base.CreateContexts();
    }
}
