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

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/context.cc, lily/include/context.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <summary>
/// A node in the translation tree: Score, Staff, Voice and the rest.
/// <para>
/// A context holds properties, a set of translators, and two dispatchers. Properties
/// are looked up UP the tree — a Voice that does not set <c>fontSize</c> sees its
/// Staff's, and so on to Score — which is what makes <c>\set</c> at any level work.
/// </para>
/// <para>
/// The two dispatchers are not redundant. <see cref="EventSource"/> carries events
/// aimed AT this context; <see cref="EventsBelow"/> carries events happening anywhere
/// at or under it, and each child registers its own <c>EventsBelow</c> with its
/// parent's. That is the chain that lets a Score-level engraver hear a note in a
/// Voice three levels down.
/// </para>
/// </summary>
public class Context
{
    private static readonly Symbol BottomSymbol = Symbol.Intern("Bottom");
    private static readonly Symbol TranslationTypeSymbol = Symbol.Intern("translation-type?");
    private static readonly Symbol ForbidBreakSymbol = Symbol.Intern("forbidBreak");
    private static readonly Symbol ForceBreakSymbol = Symbol.Intern("forceBreak");

    private readonly Dictionary<Symbol, object> _properties = new Dictionary<Symbol, object>();
    private Moment _nowMoment = Moment.Zero;
    private readonly List<Context> _children = new List<Context>();
    private readonly List<Symbol> _aliases = new List<Symbol>();

    /// <summary>Initializes a context.</summary>
    /// <param name="contextName">The context's type name, such as <c>Voice</c>.</param>
    /// <param name="id">The identifier from <c>\context Voice = "id"</c>, or empty.</param>
    public Context(Symbol contextName, string id = "")
    {
        ContextNameSymbol = contextName ?? throw new ArgumentNullException(nameof(contextName));
        IdString = id ?? string.Empty;
        EventSource = new Dispatcher();
        EventsBelow = new Dispatcher();
    }

    /// <summary>Gets the context's type name symbol.</summary>
    public Symbol ContextNameSymbol { get; }

    /// <summary>Gets the context's type name.</summary>
    public string ContextName => ContextNameSymbol.Name;

    /// <summary>Gets the identifier this context was created with.</summary>
    public string IdString { get; }

    /// <summary>Gets the parent context, or <see langword="null"/> at the root.</summary>
    public Context Parent { get; private set; }

    /// <summary>Gets the child contexts, in creation order.</summary>
    public IReadOnlyList<Context> Children => _children;

    /// <summary>Gets the aliases this context also answers to.</summary>
    public IReadOnlyList<Symbol> Aliases => _aliases;

    /// <summary>Gets or sets the translator group that does this context's work.</summary>
    public TranslatorGroup Implementation { get; set; }

    /// <summary>Gets the dispatcher for events aimed at this context.</summary>
    public Dispatcher EventSource { get; }

    /// <summary>
    /// Gets the dispatcher for events happening at or below this context. Children
    /// register theirs with their parent's, forming the chain events travel up.
    /// </summary>
    public Dispatcher EventsBelow { get; }

    /// <summary>
    /// Gets or sets the moment this context has reached.
    /// <para>
    /// There is ONE clock, and it lives at the top of the tree: upstream's
    /// <c>Context::now_mom</c> walks to the top context and returns its moment, while
    /// only <c>Global_context</c> stores one. A per-context clock would leave every
    /// engraver below the root reading zero forever — and reading the wrong moment is
    /// not an error anywhere, it just puts everything at the wrong time.
    /// </para>
    /// </summary>
    public virtual Moment NowMoment
    {
        get
        {
            Context top = Root;
            return ReferenceEquals(top, this) ? _nowMoment : top.NowMoment;
        }

        set => _nowMoment = value;
    }

    /// <summary>Gets the moment this context was added to the tree.</summary>
    public Moment InitMoment { get; private set; } = -Moment.Infinity;

    /// <summary>
    /// Gets the output definition this context is laid out under.
    /// <para>
    /// Only the root holds one, exactly as only <c>Global_context</c> stores an
    /// <c>Output_def</c> upstream, so every context below reads the same one by walking
    /// up. That is deliberate: a per-context layout would let two staves in one score
    /// disagree about the staff height with nothing to report it.
    /// </para>
    /// </summary>
    public virtual Layout.OutputDef OutputDef
    {
        get
        {
            Context top = Root;
            return ReferenceEquals(top, this) ? null : top.OutputDef;
        }
    }

    /// <summary>
    /// Gets a value indicating whether this context accepts no children.
    /// <para>
    /// Upstream's test is purely about acceptance — <c>!acceptance_.has_default ()</c>
    /// — and says nothing about whether children exist. It matters: a context that
    /// accepts nothing but has somehow acquired a child is still the bottom, and the
    /// iterators' <c>descend_to_bottom_context</c> must stop there rather than trying
    /// to create one more level.
    /// </para>
    /// </summary>
    public virtual bool IsBottomContext => AcceptedContexts.Count == 0;

    /// <summary>
    /// Gets a value indicating whether user-written music may address this context.
    /// <para>
    /// True for everything except Global, which exists to drive the timesteps and is
    /// not something a score refers to.
    /// </para>
    /// </summary>
    public virtual bool IsAccessibleToUser => true;

    /// <summary>Gets the context types this one is willing to create as children.</summary>
    public List<Symbol> AcceptedContexts { get; } = new List<Symbol>();

    /// <summary>
    /// Gets or sets how a context of a named type is built when one has to be created.
    /// <para>
    /// Upstream reads a <c>Context_def</c> — which carries the context's translator
    /// list, its own acceptance list and its property defaults — out of the output
    /// definition. Those definitions come from <c>ly/engraver-init.ly</c>, so they
    /// arrive with the PARSER (Track P) and not before. Until then this hook is the
    /// seam: the caller supplies the factory, and the port's own context creation is
    /// otherwise shaped exactly like upstream's. The divergence is recorded in
    /// PORT-COVERAGE.
    /// </para>
    /// </summary>
    public static Func<Symbol, string, Context> ContextFactory { get; set; }

    /// <summary>Gets the root of the context tree this context belongs to.</summary>
    public Context Root
    {
        get
        {
            Context context = this;
            while (context.Parent != null)
            {
                context = context.Parent;
            }

            return context;
        }
    }

    /// <summary>Adds a child context and wires its event chain to this one.</summary>
    /// <param name="child">The child to add.</param>
    public void AddContext(Context child)
    {
        if (child == null)
        {
            throw new ArgumentNullException(nameof(child));
        }

        _children.Add(child);
        child.Parent = this;
        child.InitMoment = NowMoment;

        EventsBelow.RegisterAsListener(child.EventsBelow);

        // Connecting and initialising happen only once the context is IN the hierarchy,
        // and upstream is explicit about why: "This cannot move before add_context (),
        // because \override operations require that we are in the hierarchy." The port
        // has two more reasons of its own -- Score_engraver's listeners are registered
        // on the TOP context, and its initialize() reads the output definition, which
        // only resolves upward.
        //
        // A group that a caller has already connected itself is left alone, so a test
        // fixture that wires its own translators keeps working.
        TranslatorGroup group = child.Implementation;
        if (group != null && group.Context == null)
        {
            group.ConnectToContext(child);
            group.Initialize();
        }
    }

    /// <summary>Removes a child context and unwires its event chain.</summary>
    /// <param name="child">The child to remove.</param>
    public void RemoveContext(Context child)
    {
        if (child == null || !_children.Remove(child))
        {
            return;
        }

        EventsBelow.UnregisterAsListener(child.EventsBelow);
        child.Parent = null;
    }

    /// <summary>Records an alias this context also answers to.</summary>
    /// <param name="alias">The alias symbol.</param>
    public void AddAlias(Symbol alias) => _aliases.Insert(0, alias);

    /// <summary>
    /// Determines whether this context answers to a name. <c>Bottom</c> matches any
    /// context that accepts no children, which is how <c>\change Staff</c> and
    /// friends find a leaf.
    /// </summary>
    /// <param name="name">The name to test.</param>
    /// <returns><see langword="true"/> when this context answers to it.</returns>
    public bool IsAlias(Symbol name)
    {
        if (ReferenceEquals(name, BottomSymbol))
        {
            return IsBottomContext;
        }

        if (ReferenceEquals(name, ContextNameSymbol))
        {
            return true;
        }

        return _aliases.Contains(name);
    }

    /// <summary>
    /// Reads a property, walking UP the tree until it is found.
    /// </summary>
    /// <param name="symbol">The property name.</param>
    /// <returns>The value, or the empty list when set nowhere.</returns>
    public object GetProperty(Symbol symbol)
    {
        Context where = WhereDefined(symbol, out object value);
        return where != null ? value : Nil.Instance;
    }

    /// <summary>Reads a property by name.</summary>
    /// <param name="name">The property name.</param>
    /// <returns>The value.</returns>
    public object GetProperty(string name) => GetProperty(Symbol.Intern(name));

    /// <summary>
    /// Returns the context a property is actually set in, walking up the tree.
    /// </summary>
    /// <param name="symbol">The property name.</param>
    /// <param name="value">Receives the value when found.</param>
    /// <returns>The context that defines it, or <see langword="null"/>.</returns>
    public Context WhereDefined(Symbol symbol, out object value)
    {
        for (Context context = this; context != null; context = context.Parent)
        {
            if (context._properties.TryGetValue(symbol, out value))
            {
                return context;
            }
        }

        value = null;
        return null;
    }

    /// <summary>
    /// Returns a property set in THIS context only, without walking up the tree.
    /// </summary>
    /// <param name="symbol">The property name.</param>
    /// <param name="value">Receives the value when found.</param>
    /// <returns><see langword="true"/> when this context sets it.</returns>
    public bool HereDefined(Symbol symbol, out object value)
        => _properties.TryGetValue(symbol, out value);

    /// <summary>Sets a property on this context, type-checking it first.</summary>
    /// <param name="symbol">The property name.</param>
    /// <param name="value">The value to store.</param>
    public void SetProperty(Symbol symbol, object value)
    {
        if (!SchemeUtilities.TypeCheckAssignment(symbol, value, TranslationTypeSymbol))
        {
            return;
        }

        _properties[symbol] = value;
    }

    /// <summary>Sets a property by name.</summary>
    /// <param name="name">The property name.</param>
    /// <param name="value">The value to store.</param>
    public void SetProperty(string name, object value) => SetProperty(Symbol.Intern(name), value);

    /// <summary>Removes a property from this context.</summary>
    /// <param name="symbol">The property name.</param>
    public void UnsetProperty(Symbol symbol) => _properties.Remove(symbol);

    /// <summary>
    /// Finds a context by name, searching this context, then its children, then its
    /// parent's subtree, and so on outward.
    /// </summary>
    /// <param name="name">The context name or alias to find.</param>
    /// <param name="id">The identifier to match, or empty to match any.</param>
    /// <returns>The context, or <see langword="null"/> when not found.</returns>
    public Context FindContext(Symbol name, string id = "")
    {
        Context found = FindDescendant(name, id);
        if (found != null)
        {
            return found;
        }

        // Walk outward: try each ancestor's subtree, skipping the branch we came from.
        Context child = this;
        for (Context parent = Parent; parent != null; child = parent, parent = parent.Parent)
        {
            if (parent.Matches(name, id))
            {
                return parent;
            }

            foreach (Context sibling in parent._children)
            {
                if (ReferenceEquals(sibling, child))
                {
                    continue;
                }

                Context result = sibling.FindDescendant(name, id);
                if (result != null)
                {
                    return result;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Returns a context that accepts no children, creating the path down to one when
    /// this context is not already it.
    /// <para>
    /// This is what <c>\new Voice</c>-less music relies on: a note event addressed at a
    /// Staff has to reach a Voice, and if none exists one is made.
    /// </para>
    /// </summary>
    /// <param name="id">The identifier to match, or empty for any.</param>
    /// <returns>The bottom context, or this context when none could be created.</returns>
    public Context GetDefaultInterpreter(string id = "")
    {
        if (IsBottomContext && (id.Length == 0 || string.Equals(IdString, id, StringComparison.Ordinal)))
        {
            // This is where we want to be.
            return this;
        }

        Context created = CreateUniqueContext(BottomSymbol, id);
        if (created != null)
        {
            return created;
        }

        // Upstream logs here rather than returning null, because a null return is not
        // detected by its callers. Same choice, same reason.
        Warn.Warning("cannot find or create context: " + DiagnosticId(BottomSymbol, id));
        return this;
    }

    /// <summary>
    /// Returns the nearest context user-written music may address, descending when this
    /// one may not be addressed. Concretely: if this is Global, descend to Score.
    /// </summary>
    /// <returns>The accessible context, or <see langword="null"/> when there is none.</returns>
    public Context GetUserAccessibleInterpreter()
    {
        if (IsAccessibleToUser)
        {
            return this;
        }

        Context context = this;
        while (context != null)
        {
            Context next = null;
            foreach (Symbol accepted in context.AcceptedContexts)
            {
                next = context.FindCreateContext(accepted, string.Empty, Direction.Negative);
                if (next != null)
                {
                    break;
                }
            }

            if (next == null || ReferenceEquals(next, context))
            {
                return null;
            }

            if (next.IsAccessibleToUser)
            {
                return next;
            }

            context = next;
        }

        return null;
    }

    /// <summary>
    /// Creates a context of a named type, without reusing an existing one.
    /// </summary>
    /// <param name="name">The context type to create, or <c>Bottom</c>.</param>
    /// <param name="id">The identifier to give it.</param>
    /// <returns>The new context, or <see langword="null"/> when none could be created.</returns>
    public Context CreateUniqueContext(Symbol name, string id = "")
        => Find(FindMode.CreateOnly, name, id ?? string.Empty, Direction.Center);

    /// <summary>
    /// Finds a context of a named type, creating one when there is none.
    /// </summary>
    /// <param name="name">The context type to find or create, or <c>Bottom</c>.</param>
    /// <param name="id">The identifier to match or give.</param>
    /// <param name="direction">
    /// Negative searches downward only, positive upward only, centre both ways first.
    /// The default is centre — <c>default(Direction)</c> is zero, which
    /// <see cref="Direction.Center"/> also is, so the default cannot drift apart from it.
    /// </param>
    /// <returns>The context, or <see langword="null"/> when none was found or created.</returns>
    public Context FindCreateContext(Symbol name, string id = "", Direction direction = default)
        => Find(FindMode.FindCreate, name, id ?? string.Empty, direction);

    /// <summary>
    /// Determines whether a line break is allowed here.
    /// <para>
    /// It is allowed unless something forbade it, and a user request overrides the
    /// forbidding. In practice a <c>Bar_engraver</c> sets <c>forbidBreak</c> wherever
    /// there is no bar line, so this is what confines break-only grobs — a clef, a key
    /// signature — to places one could actually be seen.
    /// </para>
    /// </summary>
    /// <param name="context">The context to ask.</param>
    /// <returns><see langword="true"/> when a break may happen here.</returns>
    public static bool BreakAllowed(Context context)
    {
        if (context == null)
        {
            return true;
        }

        // A break is allowed if nothing prevented it, or if the user
        // explicitly requested it.
        return !SchemeUtilities.ToBool(context.GetProperty(ForbidBreakSymbol))
               || SchemeUtilities.ToBool(context.GetProperty(ForceBreakSymbol));
    }

    /// <summary>Describes a context type and identifier, for a diagnostic message.</summary>
    /// <param name="name">The context type.</param>
    /// <param name="id">The identifier, possibly empty.</param>
    /// <returns>The description.</returns>
    public static string DiagnosticId(Symbol name, string id)
        => string.IsNullOrEmpty(id) ? name.Name : name.Name + " = \"" + id + "\"";

    /// <summary>Broadcasts an event to this context and everything above it.</summary>
    /// <param name="streamEvent">The event to send.</param>
    public void SendStreamEvent(StreamEvent streamEvent)
    {
        if (streamEvent == null)
        {
            throw new ArgumentNullException(nameof(streamEvent));
        }

        EventSource.Broadcast(streamEvent);
        EventsBelow.Broadcast(streamEvent);
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The context's name and identifier.</returns>
    public override string ToString()
        => IdString.Length > 0
            ? "#<Context " + ContextName + " = " + IdString + ">"
            : "#<Context " + ContextName + ">";

    /// <summary>How far <see cref="Find"/> may go: search only, create only, or both.</summary>
    private enum FindMode
    {
        FindOnly,
        FindCreate,
        CreateOnly,
    }

    private Context Find(FindMode mode, Symbol name, string id, Direction direction)
    {
        Context found = UncheckedFind(mode, name, id, direction);
        return found != null && found.IsAccessibleToUser ? found : null;
    }

    private Context UncheckedFind(FindMode mode, Symbol name, string id, Direction direction)
    {
        bool allowCreate = mode != FindMode.FindOnly;
        bool allowFind = mode != FindMode.CreateOnly;

        if (allowFind && direction == Direction.Center)
        {
            // Search everything in and below this context first -- a \context Staff = "RH"
            // inside a PianoStaff has to find the staff its siblings already made -- and
            // only then the path to the top, before anything more distantly related.
            Context below = CoreFind(FindMode.FindOnly, name, id, Direction.Negative);
            if (below != null)
            {
                return below;
            }

            Context above = CoreFind(FindMode.FindOnly, name, id, Direction.Positive);
            if (above != null)
            {
                return above;
            }
        }

        return allowCreate
            ? CoreFind(mode, name, id, direction)
            : CoreFind(FindMode.FindOnly, name, id, direction);
    }

    private Context CoreFind(FindMode mode, Symbol name, string id, Direction direction)
    {
        bool allowCreate = mode != FindMode.FindOnly;
        bool allowFind = mode != FindMode.CreateOnly;
        bool walkDown = direction != Direction.Positive;
        bool walkUp = direction != Direction.Negative;

        if (allowFind && Matches(name, id))
        {
            return this;
        }

        if (walkDown && allowFind)
        {
            foreach (Context child in _children)
            {
                Context found = child.CoreFind(FindMode.FindOnly, name, id, Direction.Negative);
                if (found != null)
                {
                    return found;
                }
            }
        }

        if (walkDown && allowCreate)
        {
            List<Symbol> path = PathToAcceptableContext(name);
            if (path.Count > 0)
            {
                return CreateHierarchy(path, id);
            }
        }

        return walkUp && Parent != null ? Parent.CoreFind(mode, name, id, direction) : null;
    }

    /// <summary>
    /// Returns the chain of context types to create to reach one that answers to a
    /// name, or an empty list when this context cannot lead there.
    /// <para>
    /// Upstream walks <c>Context_def</c>s out of the output definition, so it knows
    /// every type's own acceptance list before creating anything. The port only knows
    /// the acceptance list of contexts that already exist, so it can descend one level
    /// at a time -- enough for <c>Bottom</c> and for a directly accepted type, which is
    /// what the iterators ask for. See PORT-COVERAGE; the rest arrives with Track P.
    /// </para>
    /// </summary>
    private List<Symbol> PathToAcceptableContext(Symbol name)
    {
        List<Symbol> path = new List<Symbol>();

        if (ReferenceEquals(name, BottomSymbol))
        {
            // Descend by default acceptance until a type that accepts nothing further.
            // Without Context_defs the port cannot see an unbuilt type's acceptance
            // list, so it takes the first accepted type and lets the recursion in
            // CreateHierarchy continue from the context once it exists.
            if (AcceptedContexts.Count > 0)
            {
                path.Add(AcceptedContexts[0]);
            }

            return path;
        }

        if (AcceptedContexts.Contains(name))
        {
            path.Add(name);
        }

        return path;
    }

    private Context CreateHierarchy(List<Symbol> path, string id)
    {
        Func<Symbol, string, Context> factory = ContextFactory;
        if (factory == null)
        {
            // Honest failure. Silently returning this context would leave an event
            // broadcast at the wrong level, which produces no error and no output.
            Warn.ProgrammingError(
                "no context factory installed; cannot create " + path[0].Name);
            return null;
        }

        Context current = this;
        foreach (Symbol type in path)
        {
            Context child = factory(type, id);
            if (child == null)
            {
                return null;
            }

            current.AddContext(child);
            current = child;
        }

        // Bottom means "keep going until nothing more is accepted", and the port can
        // only see the next level at a time -- so recurse now that the child exists and
        // its own acceptance list is readable.
        return current.IsBottomContext ? current : current.GetDefaultInterpreter(id);
    }

    private Context FindDescendant(Symbol name, string id)
    {
        if (Matches(name, id))
        {
            return this;
        }

        foreach (Context child in _children)
        {
            Context found = child.FindDescendant(name, id);
            if (found != null)
            {
                return found;
            }
        }

        return null;
    }

    private bool Matches(Symbol name, string id)
        => IsAlias(name) && (id.Length == 0 || string.Equals(IdString, id, StringComparison.Ordinal));
}
