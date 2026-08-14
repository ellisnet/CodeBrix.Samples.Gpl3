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

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

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
/// <para>
/// EVERY context is built from a <see cref="ContextDef"/>. The definition is what
/// carries the name, the aliases, the acceptance list, the translator list and the
/// property defaults, and it is read out of the <see cref="Layout.OutputDef"/> the
/// score is laid out under — which is where <c>ly/engraver-init.ly</c> put it. The
/// name-only constructor builds a SYNTHETIC definition carrying nothing but the name;
/// it exists for fixtures that exercise one piece of the tree without an output
/// definition, and it is recorded in PORT-COVERAGE.
/// </para>
/// </summary>
public class Context
{
    private static readonly Symbol BottomSymbol = Symbol.Intern("Bottom");
    private static readonly Symbol TranslationTypeSymbol = Symbol.Intern("translation-type?");
    private static readonly Symbol ForbidBreakSymbol = Symbol.Intern("forbidBreak");
    private static readonly Symbol MelismaBusyPropertiesSymbol
        = Symbol.Intern("melismaBusyProperties");

    private static readonly Symbol RepeatCountVisibilitySymbol
        = Symbol.Intern("repeatCountVisibility");

    private static readonly Symbol ForceBreakSymbol = Symbol.Intern("forceBreak");
    private static readonly Symbol ContextNameModSymbol = Symbol.Intern("context-name");
    private static readonly Symbol AcceptsSymbol = Symbol.Intern("accepts");
    private static readonly Symbol DeniesSymbol = Symbol.Intern("denies");
    private static readonly Symbol DefaultChildSymbol = Symbol.Intern("default-child");
    private static readonly Symbol ScoreEngraverSymbol = Symbol.Intern("Score_engraver");
    private static readonly Symbol ScorePerformerSymbol = Symbol.Intern("Score_performer");
    private static readonly Symbol ContextSymbol = Symbol.Intern("context");
    private static readonly Symbol CreatorSymbol = Symbol.Intern("creator");
    private static readonly Symbol OpsSymbol = Symbol.Intern("ops");
    private static readonly Symbol TypeSymbol = Symbol.Intern("type");
    private static readonly Symbol IdSymbol = Symbol.Intern("id");
    private static readonly Symbol SymbolSymbol = Symbol.Intern("symbol");
    private static readonly Symbol ValueSymbol = Symbol.Intern("value");
    private static readonly Symbol OnceSymbol = Symbol.Intern("once");
    private static readonly Symbol CreateContextSymbol = Symbol.Intern("CreateContext");
    private static readonly Symbol RemoveContextSymbol = Symbol.Intern("RemoveContext");
    private static readonly Symbol ChangeParentSymbol = Symbol.Intern("ChangeParent");
    private static readonly Symbol SetPropertySymbol = Symbol.Intern("SetProperty");
    private static readonly Symbol UnsetPropertySymbol = Symbol.Intern("UnsetProperty");
    private static readonly Symbol AnnounceNewContextSymbol = Symbol.Intern("AnnounceNewContext");
    private static readonly Symbol SetPropertyProcSymbol = Symbol.Intern("ly:context-set-property!");
    private static readonly Symbol UnsetPropertyProcSymbol = Symbol.Intern("ly:context-unset-property");

    private readonly Dictionary<Symbol, object> _properties = new Dictionary<Symbol, object>();
    private Moment _nowMoment = Moment.Zero;
    private readonly List<Context> _children = new List<Context>();
    private readonly List<Symbol> _aliases = new List<Symbol>();
    private readonly ContextDef _definition;
    private readonly object _definitionMods;
    private readonly AcceptanceSet _acceptance;
    private readonly bool _adopts;
    private StreamEvent _infantEvent;
    private Listener _createContextListener;
    private Listener _removeContextListener;
    private Listener _changeParentListener;
    private Listener _setPropertyListener;
    private Listener _unsetPropertyListener;

    /// <summary>
    /// Initializes a context from its definition and the operations written at the
    /// instantiation site.
    /// </summary>
    /// <param name="definition">The definition to build from.</param>
    /// <param name="ops">
    /// The <c>\with</c> block's operations, applied on top of the definition's own
    /// acceptance list. The empty list for none.
    /// </param>
    public Context(ContextDef definition, object ops)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _definitionMods = ops ?? Nil.Instance;
        IdString = string.Empty;
        EventSource = new Dispatcher();
        EventsBelow = new Dispatcher();

        // ORDER IS LOAD-BEARING, and upstream says so out loud. Both places where
        // upstream wires a context's dispatchers — create_context_from_event
        // (lily/context.cc:375-395) and Global_context's constructor
        // (lily/global-context.cc:50-55) — register the context's OWN listeners first
        // and register events_below_ LAST, under the comment "We want to be the first
        // ones to hear our own events. Therefore, wait before registering
        // events_below_". A Dispatcher hands an event to its listeners in increasing
        // priority, and priority is the order of registration, so the two orders are
        // NOT equivalent: with events_below_ registered first, an event broadcast AT a
        // context reaches every outside listener BEFORE the context has acted on it.
        // That is not cosmetic — CreateContext is the event that CREATES a context, and
        // LyricCombineIterator.CheckNewContext (upstream's check_new_context) listens
        // for it on the top context precisely to catch the Voice the moment it exists.
        // Relayed early, it ran a FindVoice that could not yet succeed, so \lyricsto
        // bound its melody one timestep late and dropped the first syllable of every
        // stanza in the suite.
        RegisterContextListeners();

        // events_below ()->register_as_listener (event_source_) — what makes an event
        // broadcast AT a context also travel up: without it, a translator in an ancestor
        // never hears anything from below, and the port's earlier workaround —
        // SendStreamEvent broadcasting on both dispatchers — could not help, because the
        // iterators broadcast on the event source DIRECTLY, which is upstream's route too.
        EventsBelow.RegisterAsListener(EventSource);

        for (object cursor = _definition.ContextAliases; cursor is Pair pair; cursor = pair.Cdr)
        {
            if (pair.Car is Symbol alias)
            {
                _aliases.Add(alias);
            }
        }

        _acceptance = AcceptanceSet.ShallowCopy(_definition.Acceptance);

        // TODO: Set this with "\adopts ##t" in the ly code.
        object typeSymbol = _definition.TranslatorGroupType;
        _adopts = ReferenceEquals(typeSymbol, ScoreEngraverSymbol)
                  || ReferenceEquals(typeSymbol, ScorePerformerSymbol);

        for (object cursor = _definitionMods; cursor is Pair pair; cursor = pair.Cdr)
        {
            if (!(pair.Car is Pair op))
            {
                continue;
            }

            object tag = op.Car;
            object argument = op.Cdr is Pair rest ? rest.Car : Nil.Instance;
            Symbol name = AsAcceptanceSymbol(argument);
            if (name == null)
            {
                continue;
            }

            if (ReferenceEquals(tag, AcceptsSymbol))
            {
                _acceptance.Accept(name);
            }
            else if (ReferenceEquals(tag, DeniesSymbol))
            {
                _acceptance.Deny(name);
            }
            else if (ReferenceEquals(tag, DefaultChildSymbol))
            {
                _acceptance.AcceptDefault(name);
            }
        }
    }

    /// <summary>
    /// Initializes a context from a name alone, on a synthetic definition.
    /// <para>
    /// DIVERGENCE, recorded in PORT-COVERAGE: upstream has no such constructor because
    /// upstream always has an output definition to read a real <c>Context_def</c> out
    /// of. The port keeps this one for fixtures that exercise the dispatcher, the
    /// property lookup or one engraver without standing up the whole init layer. Such a
    /// context has an EMPTY acceptance set, so it is a bottom context until the fixture
    /// says otherwise through <see cref="Acceptance"/>.
    /// </para>
    /// </summary>
    /// <param name="contextName">The context's type name, such as <c>Voice</c>.</param>
    /// <param name="id">The identifier from <c>\context Voice = "id"</c>, or empty.</param>
    public Context(Symbol contextName, string id = "")
        : this(SyntheticDefinition(contextName), Nil.Instance)
    {
        IdString = id ?? string.Empty;
    }

    /// <summary>Gets the definition this context was built from.</summary>
    public ContextDef Definition => _definition;

    /// <summary>Gets the operations the instantiation site supplied — the <c>\with</c> block.</summary>
    public object DefinitionMods => _definitionMods;

    /// <summary>Gets the context's type name symbol.</summary>
    public Symbol ContextNameSymbol => _definition.ContextName as Symbol;

    /// <summary>Gets the context's type name.</summary>
    public string ContextName => ContextNameSymbol?.Name ?? string.Empty;

    /// <summary>Gets the identifier this context was created with.</summary>
    public string IdString { get; private set; }

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
    /// Gets the child types this context creates, and which of them is the default.
    /// <para>Copied from the definition at construction and then edited by the
    /// instantiation site's <c>\accepts</c>, <c>\denies</c> and <c>\defaultchild</c>, so
    /// a <c>\with</c> block can change what one context accepts without touching the
    /// definition every other context of that type shares.</para>
    /// </summary>
    public AcceptanceSet Acceptance => _acceptance;

    /// <summary>
    /// Gets how many <see cref="ContextHandle"/>s currently hold this context.
    /// <para>A context with clients is not removable even when it has no children —
    /// an ossia staff outlives the music that made it for exactly as long as an
    /// iterator still points at it.</para>
    /// </summary>
    public int ClientCount { get; internal set; }

    /// <summary>
    /// Gets a value indicating whether this context takes over new descendants that
    /// would otherwise get an intermediate context of its own type.
    /// </summary>
    public bool Adopts => _adopts;

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
    /// Upstream's test is purely about the DEFAULT CHILD —
    /// <c>!acceptance_.has_default ()</c> — and says nothing about whether children
    /// exist, nor about what the context merely <c>\accepts</c>. It matters: a context
    /// that has no default child but has somehow acquired a child is still the bottom,
    /// and the iterators' <c>descend_to_bottom_context</c> must stop there rather than
    /// trying to create one more level.
    /// </para>
    /// </summary>
    public virtual bool IsBottomContext => !_acceptance.HasDefault;

    /// <summary>
    /// Gets a value indicating whether user-written music may address this context.
    /// <para>
    /// True for everything except Global, which exists to drive the timesteps and is
    /// not something a score refers to.
    /// </para>
    /// </summary>
    public virtual bool IsAccessibleToUser => true;

    /// <summary>
    /// Gets a value indicating whether this context can be taken out of the tree: no
    /// children, no clients, and not a direct child of Global.
    /// </summary>
    public bool IsRemovable
        => _children.Count == 0 && ClientCount == 0 && !(Parent is GlobalContext);

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

    /// <summary>Gets the global context at the root of this tree, or null when there is none.</summary>
    public GlobalContext GlobalContext => Root as GlobalContext;

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
        // fixture that wires its own translators keeps working. On the REAL path the
        // implementation is still null here: Translator_group::create_child_translator
        // builds it when it hears AnnounceNewContext, which happens after this returns.
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
        // The CHECKED symbol and value are what get written, not the ones passed in: a
        // deprecated property redirects to its replacement and converts its value on the
        // way.
        if (!SchemeUtilities.TypeCheckAssignment(
                symbol, value, TranslationTypeSymbol,
                out Symbol checkedSymbol, out object checkedValue))
        {
            return;
        }

        _properties[checkedSymbol] = checkedValue;
    }

    /// <summary>Sets a property by name.</summary>
    /// <param name="name">The property name.</param>
    /// <param name="value">The value to store.</param>
    public void SetProperty(string name, object value) => SetProperty(Symbol.Intern(name), value);

    /// <summary>Removes a property from this context.</summary>
    /// <param name="symbol">The property name.</param>
    public void UnsetProperty(Symbol symbol)
    {
        Symbol checkedSymbol = SchemeUtilities.TypeCheckUnset(symbol, TranslationTypeSymbol);
        if (checkedSymbol != null)
        {
            _properties.Remove(checkedSymbol);
        }
    }

    /// <summary>Returns this context's own properties as an alist.</summary>
    /// <returns>The alist, newest binding first.</returns>
    public object PropertiesAsAlist()
    {
        object result = Nil.Instance;
        foreach (KeyValuePair<Symbol, object> entry in _properties)
        {
            result = new Pair(new Pair(entry.Key, entry.Value), result);
        }

        return result;
    }

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
    /// Returns the nearest ancestor (starting at this context) answering to a name.
    /// <para>Upstream: the free function <c>find_context_above</c>, which is what
    /// <c>ly:context-find</c> calls.</para>
    /// </summary>
    /// <param name="name">The context name or alias to find.</param>
    /// <returns>The context, or <see langword="null"/> when there is none.</returns>
    public Context FindContextAbove(Symbol name)
    {
        for (Context context = this; context != null; context = context.Parent)
        {
            if (context.IsAlias(name))
            {
                return context;
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

        // It's interesting that this goes straight to creating a new hierarchy even if
        // there might be an existing partial (or even full?) path to a bottom context.
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

        // PathToBottomContext is a ready way to avoid hard-coding "Score".
        List<ContextDef> path = ContextDef.PathToBottomContext(
            OutputDef, _acceptance.GetDefault());

        Context context = this;
        foreach (ContextDef definition in path)
        {
            context = context.FindCreateContext(
                definition.ContextName as Symbol, string.Empty, Direction.Negative);
            if (context == null || context.IsAccessibleToUser)
            {
                return context;
            }
        }

        return null;
    }

    /// <summary>
    /// Creates a context of a named type, without reusing an existing one.
    /// </summary>
    /// <param name="name">The context type to create, or <c>Bottom</c>.</param>
    /// <param name="id">The identifier to give it.</param>
    /// <param name="ops">The instantiation site's operations, or the empty list.</param>
    /// <returns>The new context, or <see langword="null"/> when none could be created.</returns>
    public Context CreateUniqueContext(Symbol name, string id = "", object ops = null)
        => CreateUniqueContext(Direction.Center, name, id, ops);

    /// <summary>
    /// Creates a context of a named type in a given direction, without reusing an
    /// existing one.
    /// <para>Upstream: <c>create_unique_context (dir, name, id, ops)</c>, which
    /// <c>\new</c> reaches with the music's <c>search-direction</c>.</para>
    /// </summary>
    /// <param name="direction">
    /// Negative creates downward only, positive upward only, centre both ways.
    /// </param>
    /// <param name="name">The context type to create, or <c>Bottom</c>.</param>
    /// <param name="id">The identifier to give it.</param>
    /// <param name="ops">The instantiation site's operations, or the empty list.</param>
    /// <returns>The new context, or <see langword="null"/> when none could be created.</returns>
    public Context CreateUniqueContext(Direction direction, Symbol name, string id, object ops)
        => Find(FindMode.CreateOnly, name, id ?? string.Empty, direction, ops ?? Nil.Instance);

    /// <summary>
    /// Finds an existing context of a named type in a given direction, creating nothing.
    /// <para>Upstream: <c>find_context (dir, name, id)</c>. The direction-free
    /// <see cref="FindContext(Symbol, string)"/> is this with
    /// <see cref="Direction.Center"/>.</para>
    /// </summary>
    /// <param name="direction">
    /// Negative searches downward only, positive upward only, centre both ways.
    /// </param>
    /// <param name="name">The context name or alias to find.</param>
    /// <param name="id">The identifier to match, or empty to match any.</param>
    /// <returns>The context, or <see langword="null"/> when not found.</returns>
    public Context FindContext(Direction direction, Symbol name, string id)
        => Find(FindMode.FindOnly, name, id ?? string.Empty, direction, Nil.Instance);

    /// <summary>
    /// Finds an existing context of a named type nearest a possibly-null context.
    /// <para>Upstream: the free function <c>find_context_near</c>.</para>
    /// </summary>
    /// <param name="where">The context to search from, which may be null.</param>
    /// <param name="name">The context name or alias to find.</param>
    /// <param name="id">The identifier to match, or empty to match any.</param>
    /// <returns>The context, or <see langword="null"/>.</returns>
    public static Context FindContextNear(Context where, Symbol name, string id)
        => where?.FindContext(Direction.Center, name, id);

    /// <summary>
    /// Finds an existing context of a named type at or below a possibly-null context.
    /// <para>Upstream: the free function <c>find_context_below</c>.</para>
    /// </summary>
    /// <param name="where">The context to search from, which may be null.</param>
    /// <param name="name">The context name or alias to find.</param>
    /// <param name="id">The identifier to match, or empty to match any.</param>
    /// <returns>The context, or <see langword="null"/>.</returns>
    public static Context FindContextBelow(Context where, Symbol name, string id)
        => where?.FindContext(Direction.Negative, name, id);

    /// <summary>
    /// Determines whether a context is inside a melisma — the state that tells lyrics to
    /// hold the current syllable rather than start a new one.
    /// <para>
    /// Two rules, both upstream's. When a context HAS children they are the authority and
    /// EVERY one of them must be busy, because a melisma in one voice of a divided staff
    /// does not hold the whole staff. When it has none, any property named in
    /// <c>melismaBusyProperties</c> that reads true makes it busy.
    /// </para>
    /// <para>Upstream: the free function <c>melisma_busy</c> in <c>lily/context.cc</c>.
    /// It was never carried when that file was ported; the lyrics group is its first caller.</para>
    /// </summary>
    /// <param name="context">The context to test, which may be null.</param>
    /// <returns><see langword="true"/> when the context is in a melisma.</returns>
    public static bool MelismaBusy(Context context)
    {
        if (context == null)
        {
            return false;
        }

        // When there are subcontexts, they are responsible for maintaining melismata.
        IReadOnlyList<Context> children = context.Children;
        if (children.Count > 0)
        {
            // all contexts need to have a busy melisma for this to evaluate to true.
            foreach (Context child in children)
            {
                if (!MelismaBusy(child))
                {
                    return false;
                }
            }

            return true;
        }

        for (object properties = context.GetProperty(MelismaBusyPropertiesSymbol);
             properties is Pair pair;
             properties = pair.Cdr)
        {
            if (pair.Car is Symbol name && SchemeUtilities.ToBool(context.GetProperty(name)))
            {
                return true;
            }
        }

        return false;
    }

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
    /// <param name="ops">The instantiation site's operations, or the empty list.</param>
    /// <returns>The context, or <see langword="null"/> when none was found or created.</returns>
    public Context FindCreateContext(
        Symbol name,
        string id = "",
        Direction direction = default,
        object ops = null)
        => Find(FindMode.FindCreate, name, id ?? string.Empty, direction, ops ?? Nil.Instance);

    /// <summary>
    /// Returns the chain of definitions to instantiate to reach a context answering to
    /// a name, or an empty list when this context cannot lead there.
    /// </summary>
    /// <param name="name">The wanted context type, or <c>Bottom</c>.</param>
    /// <returns>The path of definitions.</returns>
    public List<ContextDef> PathToAcceptableContext(Symbol name)
    {
        Layout.OutputDef odef = OutputDef;

        if (ReferenceEquals(name, BottomSymbol))
        {
            return ContextDef.PathToBottomContext(odef, _acceptance.GetDefault());
        }

        return _definition.PathToAcceptableContext(name, odef, _acceptance.GetList());
    }

    /// <summary>
    /// Creates one child context from a definition, through the event protocol.
    /// <para>
    /// The round trip through <c>CreateContext</c> and <c>AnnounceNewContext</c> is not
    /// ceremony: it is what gives every translator group above the new context a chance
    /// to build the new context's own translators, which is where
    /// <see cref="TranslatorGroup.CreateChildTranslator"/> does its work.
    /// </para>
    /// </summary>
    /// <param name="definition">The definition to instantiate.</param>
    /// <param name="id">The identifier to give the new context.</param>
    /// <param name="ops">The instantiation site's operations.</param>
    /// <returns>The new context, or <see langword="null"/> on failure.</returns>
    public Context CreateContext(ContextDef definition, string id, object ops)
    {
        _infantEvent = null;

        /* TODO: This is fairly misplaced. We can fix this when we have taken out all
           iterator specific stuff from the Context class */
        Listener acknowledge = EventSource.AddListener(
            this, AcknowledgeInfant, AnnounceNewContextSymbol);

        /* The CreateContext creates a new context, and sends an announcement of the
           new context through another event. That event will be stored in
           _infantEvent to create a return value. */
        StreamEvent create = MakeEvent(CreateContextSymbol);
        create.SetProperty(OpsSymbol, ops ?? Nil.Instance);
        create.SetProperty(TypeSymbol, definition.ContextName);
        create.SetProperty(IdSymbol, new MutableString(id ?? string.Empty));
        SendStreamEvent(create);

        EventSource.RemoveListener(acknowledge, AnnounceNewContextSymbol);

        if (_infantEvent == null)
        {
            Warn.ProgrammingError("create_context: can't locate newly created context");
            return null;
        }

        Context infant = _infantEvent.GetProperty(ContextSymbol) as Context;
        _infantEvent = null;

        if (infant == null || !ReferenceEquals(infant.Parent, this))
        {
            Warn.ProgrammingError("create_context: can't locate newly created context");
            return null;
        }

        return infant;
    }

    /// <summary>
    /// Determines whether a repeat count should be printed, by asking the context's
    /// <c>repeatCountVisibility</c> procedure.
    /// <para>
    /// The percent-repeat engravers ask this before making their counter grobs, which is
    /// what makes <c>\set repeatCountVisibility = #(every-nth-repeat-slash-visible 3)</c>
    /// work.
    /// </para>
    /// </summary>
    /// <param name="context">The context to ask.</param>
    /// <param name="count">The repeat count in question.</param>
    /// <returns><see langword="true"/> when the count should be printed.</returns>
    public static bool CheckRepeatCountVisibility(Context context, object count)
    {
        if (context == null)
        {
            return false;
        }

        object procedure = context.GetProperty(RepeatCountVisibilitySymbol);
        if (!Objects.SchemeUtilities.IsProcedure(procedure))
        {
            return false;
        }

        object answer = Objects.SchemeUtilities.CallCallback(procedure, count, context);
        return answer is bool flag && flag;
    }

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

    /// <summary>
    /// Broadcasts an event to this context and everything above it.
    /// <para>
    /// ONE broadcast, on <see cref="EventSource"/> — upstream's
    /// <c>internal_send_stream_event</c>. It reaches <see cref="EventsBelow"/>, and
    /// through it every ancestor, because the two are chained at construction. Sending
    /// on both would deliver twice to anything listening below.
    /// </para>
    /// </summary>
    /// <param name="streamEvent">The event to send.</param>
    public void SendStreamEvent(StreamEvent streamEvent)
    {
        if (streamEvent == null)
        {
            throw new ArgumentNullException(nameof(streamEvent));
        }

        EventSource.Broadcast(streamEvent);
    }

    /// <summary>
    /// Registers the five listeners every context answers to: creating a child,
    /// removing itself, changing parent, and setting or unsetting a property.
    /// <para>Upstream does this inline in <c>create_context_from_event</c>. It is a
    /// method here so that a hand-built root — a <see cref="GlobalContext"/> a caller
    /// made directly — can be given the same protocol.</para>
    /// <para>The constructor calls it, because these five MUST be registered before
    /// <c>EventsBelow</c> is registered as a listener of <c>EventSource</c> — see the
    /// note there. It is idempotent, so the explicit calls that predate that (here and
    /// in <see cref="GlobalContext"/>) are now no-ops and are kept as documentation of
    /// where upstream does the work.</para>
    /// </summary>
    public void RegisterContextListeners()
    {
        if (_createContextListener != null)
        {
            return;
        }

        _createContextListener = EventSource.AddListener(
            this, CreateContextFromEvent, CreateContextSymbol);
        _removeContextListener = EventSource.AddListener(
            this, RemoveContextFromEvent, RemoveContextSymbol);
        _changeParentListener = EventSource.AddListener(
            this, ChangeParent, ChangeParentSymbol);
        _setPropertyListener = EventSource.AddListener(
            this, SetPropertyFromEvent, SetPropertySymbol);
        _unsetPropertyListener = EventSource.AddListener(
            this, UnsetPropertyFromEvent, UnsetPropertySymbol);
    }

    /// <summary>
    /// Walks the subtree and sends a <c>RemoveContext</c> to every context that has
    /// become removable — no children, no clients, not a child of Global.
    /// </summary>
    public void CheckRemoval()
    {
        // Removing a context takes it out of the child list, so the walk is over a
        // snapshot.
        foreach (Context child in new List<Context>(_children))
        {
            child.CheckRemoval();
            if (child.IsRemovable)
            {
                TranslatorGroup.RecurseFinalize(child, Direction.Positive);
                child.SendStreamEvent(MakeEvent(RemoveContextSymbol));
            }
        }
    }

    /// <summary>Takes this context out of its parent's child list and event chain.</summary>
    public void DisconnectFromParent()
    {
        Context parent = Parent;
        if (parent == null)
        {
            return;
        }

        parent.EventsBelow.UnregisterAsListener(EventsBelow);
        parent._children.Remove(this);
        Parent = null;
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The context's name and identifier.</returns>
    public override string ToString()
        => IdString.Length > 0
            ? "#<Context " + ContextName + " = " + IdString + ">"
            : "#<Context " + ContextName + ">";

    /// <summary>
    /// Builds an internal stream event, expanding its class name into the full
    /// ancestry <c>ly:make-event-class</c> gives it.
    /// </summary>
    /// <param name="className">The event class, such as <c>CreateContext</c>.</param>
    /// <returns>The event.</returns>
    internal static StreamEvent MakeEvent(Symbol className)
        => new StreamEvent(StreamEvent.MakeEventClass(className), Nil.Instance);

    /// <summary>
    /// Makes an event of a class, carrying a source location.
    /// <para>
    /// Upstream's <c>send_stream_event</c> macro is one
    /// <c>Stream_event (ly_make_event_class (type), origin)</c> followed by a
    /// <c>set_property</c> per named argument and a broadcast. The port unrolls it: this
    /// makes the event, the caller sets its properties, and
    /// <see cref="SendStreamEvent(StreamEvent)"/> broadcasts it.
    /// </para>
    /// </summary>
    /// <param name="className">The event class.</param>
    /// <param name="origin">The source location, or null for none.</param>
    /// <returns>The event.</returns>
    internal static StreamEvent MakeEvent(Symbol className, object origin)
    {
        StreamEvent result = MakeEvent(className);
        if (origin != null && !(origin is Nil))
        {
            result.SetSpot(origin);
        }

        return result;
    }

    /// <summary>How far <see cref="Find"/> may go: search only, create only, or both.</summary>
    private enum FindMode
    {
        FindOnly,
        FindCreate,
        CreateOnly,
    }

    private static Symbol AsAcceptanceSymbol(object value)
    {
        if (value is Symbol symbol)
        {
            return symbol;
        }

        if (value is MutableString || value is string)
        {
            return Symbol.Intern(value.ToString());
        }

        return null;
    }

    private static ContextDef SyntheticDefinition(Symbol contextName)
    {
        if (contextName == null)
        {
            throw new ArgumentNullException(nameof(contextName));
        }

        ContextDef definition = new ContextDef();
        definition.AddContextMod(Pair.List(ContextNameModSymbol, contextName));
        return definition;
    }

    private Context Find(FindMode mode, Symbol name, string id, Direction direction, object ops)
    {
        Context found = UncheckedFind(mode, name, id, direction, ops);
        return found != null && found.IsAccessibleToUser ? found : null;
    }

    private Context UncheckedFind(
        FindMode mode,
        Symbol name,
        string id,
        Direction direction,
        object ops)
    {
        bool allowCreate = mode != FindMode.FindOnly;
        bool allowFind = mode != FindMode.CreateOnly;

        if (allowFind && direction == Direction.Center)
        {
            // Search everything in and below this context first -- a \context Staff = "RH"
            // inside a PianoStaff has to find the staff its siblings already made -- and
            // only then the path to the top, before anything more distantly related.
            Context below = CoreFind(FindMode.FindOnly, name, id, Direction.Negative, Nil.Instance);
            if (below != null)
            {
                return below;
            }

            Context above = CoreFind(FindMode.FindOnly, name, id, Direction.Positive, Nil.Instance);
            if (above != null)
            {
                return above;
            }
        }

        return allowCreate
            ? CoreFind(mode, name, id, direction, ops)
            : CoreFind(FindMode.FindOnly, name, id, direction, Nil.Instance);
    }

    private Context CoreFind(FindMode mode, Symbol name, string id, Direction direction, object ops)
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
            foreach (Context child in new List<Context>(_children))
            {
                Context found = child.CoreFind(
                    FindMode.FindOnly, name, id, Direction.Negative, Nil.Instance);
                if (found != null)
                {
                    return found;
                }
            }
        }

        if (walkDown && allowCreate)
        {
            List<ContextDef> path = PathToAcceptableContext(name);
            if (path.Count > 0)
            {
                // TODO: Would it be OK to use one intermediate ID for all cases? It
                // changes the output of ly->midi->ly regression tests.
                string intermediateId = allowFind ? string.Empty : "\\new";
                return CreateHierarchy(path, intermediateId, id, ops);
            }
        }

        return walkUp && Parent != null ? Parent.CoreFind(mode, name, id, direction, ops) : null;
    }

    /// <summary>
    /// Creates a new context at the end of a given path below this context, using
    /// <paramref name="leafId"/> and <paramref name="leafOperations"/> for it.
    /// <para>
    /// Intermediate contexts in the path are reused or created. Contexts configured to
    /// "adopt" new descendants are considered for reuse. When necessary, contexts are
    /// created using <paramref name="intermediateId"/> and no operations.
    /// </para>
    /// </summary>
    private Context CreateHierarchy(
        List<ContextDef> path,
        string intermediateId,
        string leafId,
        object leafOperations)
    {
        Context leaf = this;

        if (path.Count > 0)
        {
            // choose or create the intermediate contexts
            for (int i = 0; i < path.Count - 1; i++)
            {
                object childName = path[i].ContextName;
                object grandchildName = path[i + 1].ContextName;
                Context adopter = leaf.FindChildToAdoptGrandchild(
                    childName as Symbol, grandchildName as Symbol);
                if (adopter != null)
                {
                    leaf = adopter;
                }
                else
                {
                    leaf = leaf.CreateContext(path[i], intermediateId, Nil.Instance);
                    if (leaf == null)
                    {
                        return null; // expect that CreateContext logged failure
                    }
                }
            }

            leaf = leaf.CreateContext(path[path.Count - 1], leafId, leafOperations);
        }

        return leaf;
    }

    /// <summary>
    /// Finds an existing child of the exact given type (not an alias), which will adopt
    /// the given type of grandchild.
    /// </summary>
    private Context FindChildToAdoptGrandchild(Symbol childName, Symbol grandchildName)
    {
        foreach (Context child in _children)
        {
            if (child._adopts
                && ReferenceEquals(child.ContextNameSymbol, childName)

                // Is this way of checking acceptance too heavy?
                && child.PathToAcceptableContext(grandchildName).Count == 1)
            {
                return child;
            }
        }

        return null;
    }

    /// <summary>
    /// Creates a new context from a <c>CreateContext</c> event, and sends an
    /// <c>AnnounceNewContext</c> event to this context.
    /// </summary>
    private void CreateContextFromEvent(StreamEvent streamEvent)
    {
        object idScm = streamEvent.GetProperty(IdSymbol);
        string id = idScm is MutableString || idScm is string ? idScm.ToString() : string.Empty;
        object ops = streamEvent.GetProperty(OpsSymbol);
        object typeScm = streamEvent.GetProperty(TypeSymbol);

        List<ContextDef> path = PathToAcceptableContext(typeScm as Symbol);

        if (path.Count != 1)
        {
            Warn.ProgrammingError(
                "Invalid CreateContext event: Cannot create " + typeScm + " context");
            return;
        }

        Context newContext = new Context(path[0], ops) { IdString = id };

        /* Register various listeners:
            - Make the new context hear events that universally affect contexts
            - connect events_below etc. properly */
        newContext.RegisterContextListeners();

        AddContext(newContext);

        /* This cannot move before AddContext, because \override operations require
           that we are in the hierarchy. */
        newContext._definition.ApplyDefaultPropertyOperations(newContext);
        GrobPropertyInfo.ApplyPropertyOperations(newContext, ops);

        StreamEvent announce = MakeEvent(AnnounceNewContextSymbol);
        announce.SetProperty(ContextSymbol, newContext);
        announce.SetProperty(CreatorSymbol, streamEvent);
        SendStreamEvent(announce);
    }

    private void AcknowledgeInfant(StreamEvent streamEvent) => _infantEvent = streamEvent;

    private void ChangeParent(StreamEvent streamEvent)
    {
        Context target = streamEvent.GetProperty(ContextSymbol) as Context;
        if (target == null)
        {
            return;
        }

        DisconnectFromParent();
        target.AddContext(this);
    }

    /// <summary>
    /// Die. The context is taken out of the tree; nothing else refers to it afterwards.
    /// </summary>
    private void RemoveContextFromEvent(StreamEvent streamEvent)
    {
        /* ugh, the translator group should listen to RemoveContext events by itself */
        Implementation?.DisconnectFromContext();
        DisconnectFromParent();
    }

    private void SetPropertyFromEvent(StreamEvent streamEvent)
    {
        if (!(streamEvent.GetProperty(SymbolSymbol) is Symbol symbol))
        {
            return;
        }

        object value = streamEvent.GetProperty(ValueSymbol);
        if (!SchemeUtilities.TypeCheckAssignment(
                symbol, value, TranslationTypeSymbol,
                out Symbol checkedSymbol, out object checkedValue))
        {
            return;
        }

        // The finalization reverts what was WRITTEN. Recording the deprecated name here
        // would restore nothing and leave the replacement set past its \once.
        if (SchemeUtilities.ToBool(streamEvent.GetProperty(OnceSymbol)))
        {
            AddGlobalFinalization(MakeRevertFinalization(checkedSymbol));
        }

        // Bypassing SetProperty avoids repeating the type check.
        _properties[checkedSymbol] = checkedValue;
    }

    private void UnsetPropertyFromEvent(StreamEvent streamEvent)
    {
        if (!(streamEvent.GetProperty(SymbolSymbol) is Symbol symbol))
        {
            return;
        }

        // Upstream type-checks the UNSET too, and it is not a formality: it is the only
        // thing that turns a deprecated name into the name the value actually lives
        // under. Without it `\unset Timing.<deprecated>' silently removed nothing.
        Symbol checkedSymbol = SchemeUtilities.TypeCheckUnset(symbol, TranslationTypeSymbol);
        if (checkedSymbol == null)
        {
            return;
        }

        if (SchemeUtilities.ToBool(streamEvent.GetProperty(OnceSymbol)))
        {
            AddGlobalFinalization(MakeRevertFinalization(checkedSymbol));
        }

        _properties.Remove(checkedSymbol);
    }

    /// <summary>
    /// Makes a finalization that restores (or removes) the current value of a property.
    /// </summary>
    private object MakeRevertFinalization(Symbol symbol)
    {
        if (HereDefined(symbol, out object value))
        {
            return Pair.List(Bootstrap.LilyPondScheme.LookupProcedure(SetPropertyProcSymbol),
                this, symbol, value);
        }

        return Pair.List(Bootstrap.LilyPondScheme.LookupProcedure(UnsetPropertyProcSymbol),
            this, symbol);
    }

    private void AddGlobalFinalization(object finalization)
        => GlobalContext?.AddFinalization(finalization);

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
