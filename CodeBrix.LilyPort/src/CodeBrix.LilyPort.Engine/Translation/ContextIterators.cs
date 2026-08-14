/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
  Copyright (C) 2023 Daniel Eble <nine.fierce.ballads@gmail.com>

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
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/context-specced-music-iterator.cc, lily/initial-context-music-iterator.cc, lily/change-iterator.cc, lily/apply-context-iterator.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The iterator for <c>\new</c> and <c>\context</c>: it moves the wrapper into the
/// named context before the wrapped iterators build theirs.
/// <para>
/// This is the file that decides whether a score has any staves at all. Without it,
/// <c>\new Staff { … }</c> iterates as a bare wrapper in whatever context it was
/// handed, so no Staff is ever instantiated and the whole vertical stack — staff
/// symbol, clef, key, bar lines — has nothing to attach to.
/// </para>
/// </summary>
public sealed class ContextSpeccedMusicIterator : MusicWrapperIterator
{
    private static readonly Symbol ContextTypeSymbol = Symbol.Intern("context-type");
    private static readonly Symbol ContextIdSymbol = Symbol.Intern("context-id");
    private static readonly Symbol PropertyOperationsSymbol = Symbol.Intern("property-operations");
    private static readonly Symbol SearchDirectionSymbol = Symbol.Intern("search-direction");
    private static readonly Symbol CreateNewSymbol = Symbol.Intern("create-new");

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Context_specced_music_iterator";

    /// <summary>
    /// Changes context in the wrapper before creating contexts for the wrapped
    /// iterators.
    /// </summary>
    protected override void CreateContexts()
    {
        // Change context in the wrapper before creating contexts for the wrapped
        // iterators.

        Symbol ct = Music.GetProperty(ContextTypeSymbol) as Symbol;

        string cId = string.Empty;
        object ci = Music.GetProperty(ContextIdSymbol);
        if (ci is MutableString || ci is string)
        {
            cId = ci.ToString();
        }

        object ops = Music.GetProperty(PropertyOperationsSymbol);
        Direction dir = DirectionalElementInterface.FromScheme(
            Music.GetProperty(SearchDirectionSymbol), Direction.Center);

        Context a = null;

        if (ct != null)
        {
            if (SchemeUtilities.ToBool(Music.GetProperty(CreateNewSymbol)))
            {
                a = OwnContext.CreateUniqueContext(dir, ct, cId, ops);
                if (a == null)
                {
                    Warn.Warning("cannot create context: " + Context.DiagnosticId(ct, cId));
                }
            }
            else
            {
                a = OwnContext.FindCreateContext(ct, cId, dir, ops);
                if (a != null)
                {
                    // success
                }
                else if (dir == Direction.Center)
                {
                    Warn.Warning("cannot find or create context: " + Context.DiagnosticId(ct, cId));
                }
                else if (dir == Direction.Negative)
                {
                    // Warnings in regression tests would be pretty common if we didn't
                    // ignore them for DOWN.
                    //
                    // TODO: Not warning about a failure in DOWN mode smells funny.  It
                    // suggests that the fallback (remaining in the current context) is a
                    // fully acceptable alternative (from the perspective of the end user)
                    // in all cases; however, that seems unlikely.
                }
                else // dir == UP
                {
                    // Though we called find_create_context (), UP mode just searches
                    // existing contexts.
                    Warn.Warning("cannot find context: " + Context.DiagnosticId(ct, cId));
                }
            }
        }

        if (a != null)
        {
            OwnContext = a;
        }

        base.CreateContexts();
    }
}

// This iterator is a bit like Music_wrapper_iterator, but the only thing it
// does with the wrapped music is set the initial context.

/// <summary>
/// The iterator for music that exists only to name where iteration STARTS: it lets the
/// wrapped music pick the context, keeps that context, and throws the child away.
/// </summary>
public sealed class InitialContextMusicIterator : MusicIterator
{
    private static readonly Symbol ElementSymbol = Symbol.Intern("element");

    private MusicIterator _childIterator;

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Initial_context_music_iterator";

    /// <summary>
    /// Gets zero while the child stands and infinity afterwards — which is immediately,
    /// because <see cref="CreateContexts"/> retires the child as soon as it has done its
    /// one job.
    /// </summary>
    public override Moment PendingMoment
        => _childIterator != null ? new Moment(0) : Moment.Infinity;

    /// <summary>Creates the child iterator for the wrapped music.</summary>
    protected override void CreateChildren()
    {
        base.CreateChildren();

        if (Music.GetProperty(ElementSymbol) is MusicObject element)
        {
            _childIterator = CreateChild(element);
        }
    }

    /// <summary>Takes the context the child settled on, then retires the child.</summary>
    protected override void CreateContexts()
    {
        base.CreateContexts();

        if (_childIterator != null)
        {
            _childIterator.InitContext(OwnContext);
            OwnContext = _childIterator.Context; // mission accomplished
            _childIterator.Quit();
            _childIterator = null;
        }
    }

    /// <summary>Shuts the child down when there is still one.</summary>
    protected override void DoQuit() => _childIterator?.Quit();
}

/// <summary>
/// The iterator for <c>\change</c>: it moves this iterator, and everything under it,
/// from one context to another.
/// <para>
/// The move happens two ways depending on where the iterator sits. An iterator whose
/// OWN context is the one being left is simply re-pointed. An iterator further down is
/// left alone and its BRANCH is re-parented instead, through a <c>ChangeParent</c>
/// event — grafting the subtree rather than re-pointing each iterator in it.
/// </para>
/// </summary>
public sealed class ChangeIterator : SimpleMusicIterator
{
    private static readonly Symbol ChangeToTypeSymbol = Symbol.Intern("change-to-type");
    private static readonly Symbol ChangeToIdSymbol = Symbol.Intern("change-to-id");
    private static readonly Symbol ChangeTagSymbol = Symbol.Intern("change-tag");
    private static readonly Symbol ChangeParentSymbol = Symbol.Intern("ChangeParent");
    private static readonly Symbol ContextSymbol = Symbol.Intern("context");

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Change_iterator";

    /// <summary>Performs the context change, then behaves as simple music.</summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until)
    {
        Symbol toType = Music.GetProperty(ChangeToTypeSymbol) as Symbol;
        object rawId = Music.GetProperty(ChangeToIdSymbol);
        string toId = rawId is MutableString || rawId is string ? rawId.ToString() : string.Empty;

        // Find the context to change from.
        Context last = toType == null
            ? null
            : Context.FindContext(Direction.Positive, toType, string.Empty);
        if (last != null)
        {
            // Find the context to change to.
            Context dest = Context.FindContextNear(Context, toType, toId);
            if (dest != null)
            {
                MusicIterator scope = WhereTagged(Music.GetProperty(ChangeTagSymbol) as Symbol);
                scope.PreorderWalk(iterator => Change(iterator, last, dest));
            }
            else
            {
                Warn.Warning(
                    "cannot find context to change to: " + Context.DiagnosticId(toType, toId));
            }
        }
        else if (toType != null)
        {
            Warn.Warning(
                "cannot find context to change from: "
                + Context.DiagnosticId(toType, string.Empty));
        }

        base.Process(until);
    }

    private static void Change(MusicIterator it, Context source, Context target)
    {
        if (ReferenceEquals(it.Context, source))
        {
            // The iterator's immediate context is the one to be changed.  This can't
            // be done by pruning and grafting contexts; the iterator must be changed
            // to refer to the new context.
            it.SubstituteContext(source, target);
        }
        else
        {
            // The iterator's context might be a descendant of the one to be changed.
            // Find the branch to prune, if any, and announce the change.
            for (Context branch = it.Context; branch != null; branch = branch.Parent)
            {
                Context parent = branch.Parent;
                if (parent == null)
                {
                    break;
                }

                if (ReferenceEquals(parent, target))
                {
                    break; // already in its proper place
                }

                if (ReferenceEquals(parent, source))
                {
                    StreamEvent change = Context.MakeEvent(ChangeParentSymbol, it.Origin);
                    change.SetProperty(ContextSymbol, target);
                    branch.SendStreamEvent(change);
                    break;
                }
            }
        }
    }
}

/// <summary>
/// The iterator for <c>\applyContext</c>: it calls the user's procedure with the
/// context, once.
/// </summary>
public sealed class ApplyContextIterator : SimpleMusicIterator
{
    private static readonly Symbol ProcedureSymbol = Symbol.Intern("procedure");
    private static readonly Symbol OriginSymbol = Symbol.Intern("origin");

    /// <summary>Gets the C++ class name this iterator corresponds to.</summary>
    public override string ClassName => "Apply_context_iterator";

    /// <summary>Calls the procedure, then behaves as simple music.</summary>
    /// <param name="until">The moment to process up to.</param>
    public override void Process(Moment until)
    {
        object procedure = Music.GetProperty(ProcedureSymbol);
        if (SchemeUtilities.IsProcedure(procedure))
        {
            Context context = Context;
            Input.WithLocation(
                Music.GetProperty(OriginSymbol),
                () => SchemeUtilities.CallCallback(procedure, context));
        }
        else
        {
            Warn.Warning("\\applycontext argument is not a procedure");
        }

        base.Process(until);
    }
}
