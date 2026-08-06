/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2000--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/context-def.cc, lily/include/context-def.hh (and, for AcceptanceSet below, lily/acceptance-set.cc, lily/include/acceptance-set.hh);

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/* TODO: should junk this class an replace by
   a single list of context modifications?  */

/// <summary>
/// The definition of an interpretation context as given in the input — what a
/// <c>\context { ... }</c> block inside <c>\layout</c> or <c>\midi</c> builds, and
/// what <c>ly/engraver-init.ly</c> is made of. The lists are stored in order of
/// definition.
/// <para>
/// This is the type the engine's interim <c>Context.ContextFactory</c> seam stands in
/// for: a definition carries the context's name, its aliases, its translator
/// (<c>\consists</c>/<c>\remove</c>) list, its acceptance lists, its default property
/// operations and its description. This port covers the definition DATA and the pure
/// queries over it; the integration halves that read definitions back out of an output
/// definition (<c>path_to_acceptable_context</c>, <c>path_to_bottom_context</c>,
/// <c>apply_default_property_operations</c>) wait for the session that retires the
/// factory seam, and their absence is recorded in PORT-COVERAGE.
/// </para>
/// </summary>
public class ContextDef
{
    private static readonly Symbol DescriptionSymbol = Symbol.Intern("description");
    private static readonly Symbol GrobDescriptionsSymbol = Symbol.Intern("grob-descriptions");
    private static readonly Symbol DefaultChildSymbol = Symbol.Intern("default-child");
    private static readonly Symbol ConsistsSymbol = Symbol.Intern("consists");
    private static readonly Symbol RemoveSymbol = Symbol.Intern("remove");
    private static readonly Symbol AcceptsSymbol = Symbol.Intern("accepts");
    private static readonly Symbol DeniesSymbol = Symbol.Intern("denies");
    private static readonly Symbol PopSymbol = Symbol.Intern("pop");
    private static readonly Symbol PushSymbol = Symbol.Intern("push");
    private static readonly Symbol AssignSymbol = Symbol.Intern("assign");
    private static readonly Symbol UnsetSymbol = Symbol.Intern("unset");
    private static readonly Symbol ApplySymbol = Symbol.Intern("apply");
    private static readonly Symbol AliasSymbol = Symbol.Intern("alias");
    private static readonly Symbol TranslatorTypeSymbol = Symbol.Intern("translator-type");
    private static readonly Symbol ContextNameSymbol = Symbol.Intern("context-name");
    private static readonly Symbol AliasesSymbol = Symbol.Intern("aliases");
    private static readonly Symbol PropertyOpsSymbol = Symbol.Intern("property-ops");
    private static readonly Symbol GroupTypeSymbol = Symbol.Intern("group-type");
    private static readonly Symbol BottomSymbol = Symbol.Intern("Bottom");

    private object _translatorMods;
    private readonly AcceptanceSet _acceptance;
    private object _propertyOps;
    private object _description;
    private object _contextName;
    private object _contextAliases;
    private object _translatorGroupType;
    private object _origin;
    private object _grobDescriptions;

    /// <summary>Initializes an empty definition, named by the empty symbol.</summary>
    public ContextDef()
    {
        _contextAliases = Nil.Instance;
        _translatorGroupType = Nil.Instance;
        _translatorMods = Nil.Instance;
        _propertyOps = Nil.Instance;
        _description = Nil.Instance;
        _origin = Nil.Instance;
        _grobDescriptions = Nil.Instance;
        _acceptance = new AcceptanceSet();

        _contextName = Symbol.Intern("");
    }

    /// <summary>
    /// Initializes a copy of another definition — upstream's copy constructor, which
    /// <c>clone</c> and <c>ly:context-def-modify</c> go through. The acceptance set is
    /// shallow-copied; the alist-shaped members are shared, because additions only
    /// cons onto their fronts.
    /// </summary>
    /// <param name="source">The definition to copy.</param>
    public ContextDef(ContextDef source)
    {
        _description = source._description;
        _origin = source._origin;
        _acceptance = AcceptanceSet.ShallowCopy(source._acceptance);
        _propertyOps = source._propertyOps;
        _translatorMods = source._translatorMods;
        _contextAliases = source._contextAliases;
        _translatorGroupType = source._translatorGroupType;
        _contextName = source._contextName;
        _grobDescriptions = source._grobDescriptions;
    }

    /// <summary>
    /// Gets where in the source this definition came from.
    /// <para>Upstream: <c>Context_def::origin</c>, an <c>Input</c> smob. The port has
    /// no <c>Input</c> type, so the location is carried opaquely — the parser stores
    /// its own span here, the same convention as <c>MusicObject.Origin</c>.</para>
    /// </summary>
    public object Origin => _origin;

    /// <summary>Gets the context's name, a symbol such as <c>Staff</c>.</summary>
    public object ContextName => _contextName;

    /// <summary>Gets the property operations, newest first.</summary>
    public object PropertyOps => _propertyOps;

    /// <summary>Gets the acceptance set: which child types this context creates.</summary>
    public AcceptanceSet Acceptance => _acceptance;

    /// <summary>Gets the aliases this context also answers to, newest first.</summary>
    public object ContextAliases => _contextAliases;

    /// <summary>Gets the translator group type, or the empty list when unset.</summary>
    public object TranslatorGroupType => _translatorGroupType;

    /// <summary>Gets the grob-description overrides, or the empty list when unset.</summary>
    public object GrobDescriptions => _grobDescriptions;

    /// <summary>Gets the description text, or the empty list when unset.</summary>
    public object Description => _description;

    /// <summary>
    /// Records where in the source this definition came from.
    /// <para>Upstream: <c>origin ()-&gt;set_spot (...)</c> — the two calls are folded
    /// because the port stores the location value rather than a mutable
    /// <c>Input</c>.</para>
    /// </summary>
    /// <param name="origin">The source location.</param>
    public void SetSpot(object origin) => _origin = origin;

    /// <summary>Returns a copy of this definition.</summary>
    /// <returns>The copy.</returns>
    public virtual ContextDef Clone() => new ContextDef(this);

    /// <summary>
    /// Applies one mod, a list of a tag symbol and its argument — the workhorse every
    /// <c>\context</c> block line goes through.
    /// </summary>
    /// <param name="mod">The mod, such as <c>(consists "Some_engraver")</c>.</param>
    public void AddContextMod(object mod)
    {
        object tag = ((Pair)mod).Car;
        if (ReferenceEquals(tag, DescriptionSymbol))
        {
            _description = Cadr(mod);
            return;
        }
        else if (ReferenceEquals(tag, GrobDescriptionsSymbol))
        {
            _grobDescriptions = Cadr(mod);
            return;
        }

        /*
          other modifiers take symbols as argument.
        */
        object sym = Cadr(mod);
        if (sym is string || sym is MutableString)
        {
            sym = Symbol.Intern(sym.ToString());
        }

        if (ReferenceEquals(tag, DefaultChildSymbol))
        {
            _acceptance.AcceptDefault(sym);
        }
        else if (ReferenceEquals(tag, ConsistsSymbol) || ReferenceEquals(tag, RemoveSymbol))
        {
            _translatorMods = new Pair(Pair.List(tag, sym), _translatorMods);
        }
        else if (ReferenceEquals(tag, AcceptsSymbol))
        {
            _acceptance.Accept(sym);
        }
        else if (ReferenceEquals(tag, DeniesSymbol))
        {
            _acceptance.Deny(sym);
        }
        else if (ReferenceEquals(tag, PopSymbol)
                 || ReferenceEquals(tag, PushSymbol)
                 || ReferenceEquals(tag, AssignSymbol)
                 || ReferenceEquals(tag, UnsetSymbol)
                 || ReferenceEquals(tag, ApplySymbol))
        {
            _propertyOps = new Pair(mod, _propertyOps);
        }
        else if (ReferenceEquals(tag, AliasSymbol))
        {
            _contextAliases = new Pair(sym, _contextAliases);
        }
        else if (ReferenceEquals(tag, TranslatorTypeSymbol))
        {
            _translatorGroupType = sym;
        }
        else if (ReferenceEquals(tag, ContextNameSymbol))
        {
            _contextName = sym;
        }
        else
        {
            Warn.ProgrammingError("unknown context mod tag");
        }
    }

    /// <summary>
    /// Computes the effective translator list: every <c>\consists</c> that a later
    /// <c>\remove</c> did not take back, deduplicated so the LAST <c>\consists</c> of
    /// a translator decides its position. Note that the list is returned in reverse
    /// order.
    /// </summary>
    /// <param name="userMod">Extra mods from the instantiation site (a <c>\with</c>
    /// block), applied after the definition's own; the empty list for none.</param>
    /// <returns>The translator name symbols, in reverse order.</returns>
    public object GetTranslatorNames(object userMod)
    {
        object mods = ReverseInPlace(ListCopy(_translatorMods), userMod);

        // Incrementally build the set of translators for this context.  A \consists
        // command adds to the set.  Duplicates are avoided by storing the index of
        // the last \consists command for a given translator in the added_index hash
        // table.  The reason for avoiding duplicates without a warning is that this
        // can be useful if several independent commands add the same translator.
        // After the first iteration, the list is filtered for indices where the value
        // in added_index matches, to let the order keep the last \consists.  A
        // \remove command removes from the set, implemented as removing the
        // corresponding value in added_index.  (This could also be implemented with
        // just scm_memq and scm_delq, but that would be a quadratic algorithm.)
        Dictionary<object, int> addedIndex
            = new Dictionary<object, int>(ReferenceComparer.Instance);

        void IterateMods(System.Action<int, object, object> func)
        {
            int i = 0;
            for (object p = mods; p is Pair pair; p = pair.Cdr)
            {
                object entry = pair.Car;
                object tag = ((Pair)entry).Car;
                object arg = Cadr(entry);
                if (arg is string || arg is MutableString)
                {
                    arg = Symbol.Intern(arg.ToString());
                }

                func(i, tag, arg);
                i++;
            }
        }

        IterateMods((i, tag, arg) =>
        {
            if (ReferenceEquals(tag, ConsistsSymbol))
            {
                addedIndex[arg] = i;
            }
            else if (ReferenceEquals(tag, RemoveSymbol))
            {
                addedIndex.Remove(arg);
            }
        });

        object ret = Nil.Instance;
        IterateMods((i, tag, arg) =>
        {
            if (ReferenceEquals(tag, ConsistsSymbol)
                && addedIndex.TryGetValue(arg, out int index)
                && index == i)
            {
                ret = new Pair(arg, ret);
            }
        });

        // Note that the list is returned in reverse order.
        return ret;
    }

    /// <summary>
    /// Returns the definition as the alist <c>ly:context-def-lookup</c> documents:
    /// <c>consists</c>, <c>description</c>, <c>aliases</c>, <c>accepts</c>,
    /// <c>default-child</c> (when there is one), <c>property-ops</c>,
    /// <c>context-name</c>, <c>group-type</c> (when set) and
    /// <c>grob-descriptions</c>.
    /// </summary>
    /// <returns>The alist.</returns>
    public object ToAlist()
    {
        object ell = Nil.Instance;

        ell = new Pair(new Pair(ConsistsSymbol, GetTranslatorNames(Nil.Instance)), ell);
        ell = new Pair(new Pair(DescriptionSymbol, _description), ell);
        ell = new Pair(new Pair(AliasesSymbol, _contextAliases), ell);
        ell = new Pair(new Pair(AcceptsSymbol, _acceptance.GetList()), ell);
        if (_acceptance.HasDefault)
        {
            ell = new Pair(new Pair(DefaultChildSymbol, _acceptance.GetDefault()), ell);
        }

        ell = new Pair(new Pair(PropertyOpsSymbol, _propertyOps), ell);
        ell = new Pair(new Pair(ContextNameSymbol, _contextName), ell);

        if (_translatorGroupType is Symbol)
        {
            ell = new Pair(new Pair(GroupTypeSymbol, _translatorGroupType), ell);
        }

        ell = new Pair(new Pair(GrobDescriptionsSymbol, _grobDescriptions), ell);
        return ell;
    }

    /// <summary>Looks one of the definition's facets up by the alist key.</summary>
    /// <param name="sym">The key, such as <c>consists</c> or <c>accepts</c>.</param>
    /// <returns>The value, or <see cref="DefaultArgument.Instance"/> for an unknown
    /// key — upstream's <c>SCM_UNDEFINED</c>.</returns>
    public object Lookup(object sym)
    {
        if (ReferenceEquals(DefaultChildSymbol, sym))
        {
            return _acceptance.GetDefault();
        }
        else if (ReferenceEquals(ConsistsSymbol, sym))
        {
            return GetTranslatorNames(Nil.Instance);
        }
        else if (ReferenceEquals(DescriptionSymbol, sym))
        {
            return _description;
        }
        else if (ReferenceEquals(AliasesSymbol, sym))
        {
            return _contextAliases;
        }
        else if (ReferenceEquals(AcceptsSymbol, sym))
        {
            return _acceptance.GetList();
        }
        else if (ReferenceEquals(PropertyOpsSymbol, sym))
        {
            return _propertyOps;
        }
        else if (ReferenceEquals(ContextNameSymbol, sym))
        {
            return _contextName;
        }
        else if (ReferenceEquals(GroupTypeSymbol, sym))
        {
            return _translatorGroupType;
        }
        else if (ReferenceEquals(GrobDescriptionsSymbol, sym))
        {
            return _grobDescriptions;
        }

        return DefaultArgument.Instance;
    }

    /// <summary>
    /// Answers whether this definition answers to a name: <c>Bottom</c> when it
    /// accepts no default child, its own name, or any declared alias.
    /// </summary>
    /// <param name="sym">The name to test.</param>
    /// <returns><see langword="true"/> when the name applies.</returns>
    public bool IsAlias(object sym)
    {
        if (ReferenceEquals(sym, BottomSymbol))
        {
            return !_acceptance.HasDefault;
        }

        if (ReferenceEquals(sym, _contextName))
        {
            return true;
        }

        return SchemeUtilities.Memq(sym, _contextAliases);
    }

    /// <summary>
    /// Looks a context definition up in an output definition by name.
    /// <para>Upstream: the free function <c>find_context_def</c> in
    /// <c>lily/output-def.cc</c>, hosted here because <see cref="ContextDef"/> is the
    /// only thing it can answer with. Returns <see langword="null"/> where upstream
    /// returns <c>SCM_EOL</c>; every caller tests the same way.</para>
    /// </summary>
    /// <param name="definition">The output definition to search.</param>
    /// <param name="name">The context name.</param>
    /// <returns>The definition, or <see langword="null"/> when there is none.</returns>
    public static ContextDef FindContextDef(Layout.OutputDef definition, object name)
    {
        if (definition == null || !(name is Symbol symbol))
        {
            return null;
        }

        return definition.LookupVariable(symbol) as ContextDef;
    }

    /// <summary>
    /// Given a name of a context that we want to create, finds a list of context
    /// definitions such that:
    /// <list type="bullet">
    /// <item><description>the first element in the list defines a context that is a
    /// valid child of the context defined by this <see cref="ContextDef"/>;</description></item>
    /// <item><description>each subsequent element in the list defines a context that is
    /// a valid child of the context defined by the preceding element;</description></item>
    /// <item><description>the last element in the list defines a context with the given
    /// name.</description></item>
    /// </list>
    /// </summary>
    /// <param name="typeSym">The context type wanted.</param>
    /// <param name="odef">The output definition the definitions live in.</param>
    /// <param name="accepted">
    /// The list of contexts the caller accepts. The caller is a <see cref="Context"/>
    /// instantiated from this definition, but its acceptance list may have been modified
    /// from the defined default.
    /// </param>
    /// <returns>The path, empty when there is none.</returns>
    public List<ContextDef> PathToAcceptableContext(
        object typeSym,
        Layout.OutputDef odef,
        object accepted)
    {
        HashSet<ContextDef> seen = new HashSet<ContextDef>(ReferenceComparer.Instance);
        ContextDef t = FindContextDef(odef, typeSym);
        return InternalPathToAcceptableContext(typeSym, t != null, odef, accepted, seen);
    }

    /// <summary>
    /// Returns the chain of definitions from this one down to a context that accepts no
    /// default child — how <c>Bottom</c> is resolved.
    /// </summary>
    /// <param name="odef">The output definition the definitions live in.</param>
    /// <param name="firstChildTypeSym">
    /// The type to descend into first, normally the caller's default child. A non-symbol
    /// means the caller IS the bottom, and the empty path is the right answer.
    /// </param>
    /// <returns>The path, empty when the descent failed.</returns>
    public static List<ContextDef> PathToBottomContext(
        Layout.OutputDef odef,
        object firstChildTypeSym)
    {
        List<ContextDef> path = new List<ContextDef>();
        if (!InternalPathToBottomContext(odef, path, firstChildTypeSym))
        {
            path.Clear();
        }

        return path;
    }

    /// <summary>
    /// Applies this definition's own property operations to a freshly built context.
    /// <para>The list is stored newest-first, so it is REVERSED before being applied —
    /// a definition that sets a property twice must end on the later value.</para>
    /// </summary>
    /// <param name="target">The context to apply them to.</param>
    public void ApplyDefaultPropertyOperations(Context target)
        => GrobPropertyInfo.ApplyPropertyOperations(target, Reverse(_propertyOps));

    /// <summary>Returns the external representation, in upstream's debug wording.</summary>
    /// <returns>The name and, when one was recorded, the origin.</returns>
    public override string ToString()
        => _origin is Nil
            ? "#<Context_def " + _contextName + ">"
            : "#<Context_def " + _contextName + " " + _origin + ">";

    /// <summary>
    /// The recursive half of <see cref="PathToAcceptableContext"/>.
    /// <para>
    /// The <paramref name="seen"/> set keeps track of visited contexts, allowing
    /// contexts of the same type to be nested.
    /// </para>
    /// <para>
    /// When the leaf is instantiable (the usual), we ignore aliases and thereby use the
    /// requested context or nothing. Example: if the caller requests a Staff, we do not
    /// substitute a RhythmicStaff.
    /// </para>
    /// <para>
    /// When the leaf is not instantiable, since there would otherwise be nothing worth
    /// doing, we allow substituting an instantiable context that aliases the requested
    /// context. Example: the caller requests a Timing and the current context would
    /// accept a Score, for which Timing is an alias, so substitute a Score.
    /// </para>
    /// </summary>
    private List<ContextDef> InternalPathToAcceptableContext(
        object typeSym,
        bool instantiable,
        Layout.OutputDef odef,
        object accepted,
        HashSet<ContextDef> seen)
    {
        List<ContextDef> accepteds = new List<ContextDef>();
        for (object s = accepted; s is Pair pair; s = pair.Cdr)
        {
            ContextDef t = FindContextDef(odef, pair.Car);
            if (t != null)
            {
                accepteds.Add(t);
            }
        }

        List<ContextDef> bestResult = new List<ContextDef>();
        foreach (ContextDef candidate in accepteds)
        {
            bool valid = instantiable
                ? SchemeUtilities.IsEqual(candidate.ContextName, typeSym)
                : candidate.IsAlias(typeSym);
            if (valid)
            {
                bestResult.Add(candidate);
                return bestResult;
            }
        }

        seen.Add(this);
        int bestDepth = int.MaxValue;
        for (int i = 0; bestDepth > 1 && i < accepteds.Count; i++)
        {
            ContextDef g = accepteds[i];

            if (!seen.Contains(g))
            {
                object acc = g._acceptance.GetList();
                List<ContextDef> result = g.InternalPathToAcceptableContext(
                    typeSym, instantiable, odef, acc, seen);
                if (result.Count > 0 && result.Count < bestDepth)
                {
                    bestDepth = result.Count;
                    result.Insert(0, g);
                    bestResult = result;
                }
            }
        }

        seen.Remove(this);

        return bestResult;
    }

    private static bool InternalPathToBottomContext(
        Layout.OutputDef odef,
        List<ContextDef> path,
        object nextTypeSym)
    {
        if (!(nextTypeSym is Symbol))
        {
            // the caller is the bottom
            return true;
        }

        ContextDef t = FindContextDef(odef, nextTypeSym);
        if (t == null)
        {
            Warn.Warning(
                "cannot create default child context: "
                + Context.DiagnosticId((Symbol)nextTypeSym, string.Empty));
            return false;
        }

        if (path.Contains(t))
        {
            Warn.Warning(
                "default child context begins a cycle: "
                + Context.DiagnosticId((Symbol)nextTypeSym, string.Empty));
            return false;
        }

        path.Add(t);
        return InternalPathToBottomContext(odef, path, t._acceptance.GetDefault());
    }

    /// <summary>Returns a list reversed, which is <c>scm_reverse</c>.</summary>
    /// <param name="list">The list to reverse; it is not modified.</param>
    /// <returns>The reversed list.</returns>
    private static object Reverse(object list)
    {
        object result = Nil.Instance;
        for (object p = list; p is Pair pair; p = pair.Cdr)
        {
            result = new Pair(pair.Car, result);
        }

        return result;
    }

    /// <summary>Returns a list's second element, which is <c>scm_cadr</c>.</summary>
    /// <param name="list">The list.</param>
    /// <returns>The second element.</returns>
    private static object Cadr(object list) => ((Pair)((Pair)list).Cdr).Car;

    /// <summary>Copies a list's spine, which is <c>scm_list_copy</c>.</summary>
    /// <param name="list">The list to copy.</param>
    /// <returns>The copy, sharing the elements.</returns>
    private static object ListCopy(object list)
    {
        object head = Nil.Instance;
        Pair last = null;
        for (object p = list; p is Pair pair; p = pair.Cdr)
        {
            Pair copy = new Pair(pair.Car, Nil.Instance);
            if (last == null)
            {
                head = copy;
            }
            else
            {
                last.Cdr = copy;
            }

            last = copy;
        }

        return head;
    }

    /// <summary>
    /// Destructively reverses a list onto a new tail, which is <c>scm_reverse_x</c>.
    /// </summary>
    /// <param name="list">The list to reverse; its pairs are reused.</param>
    /// <param name="newTail">The tail the reversed list ends in.</param>
    /// <returns>The reversed list.</returns>
    private static object ReverseInPlace(object list, object newTail)
    {
        object result = newTail;
        object current = list;
        while (current is Pair pair)
        {
            object rest = pair.Cdr;
            pair.Cdr = result;
            result = pair;
            current = rest;
        }

        return result;
    }
}

//was previously: lily/acceptance-set.cc, lily/include/acceptance-set.hh;
//  Copyright (C) 2018--2026 Daniel Eble <nine.fierce.ballads@gmail.com>
//  Hosted in this file rather than its own because Context_def is its only owner in
//  the port so far; upstream's Context also carries one, and when the integration
//  session ports that half this type is already public and ready.

/// <summary>
/// An ordered list of scheme values identifying things that are "acceptable." It
/// optionally includes a default, which is kept at the front of the list.
/// </summary>
public sealed class AcceptanceSet
{
    private object _accepted = Nil.Instance;
    private object _default = Nil.Instance;

    /// <summary>Initializes an empty set.</summary>
    public AcceptanceSet()
    {
    }

    private AcceptanceSet(object accepted, object dflt)
    {
        _accepted = accepted;
        _default = dflt;
    }

    /// <summary>Gets a value indicating whether the set has a default item.</summary>
    public bool HasDefault => !(_default is Nil);

    /// <summary>
    /// Returns a shallow copy: the list spine is copied, the default shared.
    /// <para>Upstream: the <c>shallow_copy</c> friend — the only copying the class
    /// permits.</para>
    /// </summary>
    /// <param name="other">The set to copy.</param>
    /// <returns>The copy.</returns>
    public static AcceptanceSet ShallowCopy(AcceptanceSet other)
        => new AcceptanceSet(ListCopy(other._accepted), other._default);

    /// <summary>
    /// Gets the full list of acceptable items; if there is a default, it is first.
    /// This set still owns the returned list and may later mutate it.
    /// </summary>
    /// <returns>The list.</returns>
    public object GetList() => _accepted;

    /// <summary>Gets the default item (the empty list if there isn't one).</summary>
    /// <returns>The default.</returns>
    public object GetDefault() => _default;

    /// <summary>
    /// Puts the given item at the front of the list, but not in front of the default.
    /// </summary>
    /// <param name="item">The item to accept.</param>
    public void Accept(object item)
    {
        // Ignore \accept C when C is already the default child.  It seems perfectly
        // sane for a user to express both, but it should have no practical effect.
        if (!SchemeUtilities.IsEqual(item, _default))
        {
            // We do not bother deduplicating \accepts.  It would most often be a
            // waste of time because it is not likely that a user would \accept a
            // context that was already accepted.
            //
            // Resetting the priority is a reason to repeat an \accept intentionally.
            // If desired, explicit deduplication is an option: \denies C \accepts C.
            if (!(_default is Nil))
            {
                // insert the new item after the default
                Pair front = (Pair)_accepted;
                front.Cdr = new Pair(item, front.Cdr);
            }
            else
            {
                _accepted = new Pair(item, _accepted);
            }
        }
    }

    /// <summary>Accepts the given item and sets it as the default.</summary>
    /// <param name="item">The item to make the default.</param>
    public void AcceptDefault(object item)
    {
        _accepted = new Pair(item, DeleteInPlace(item, _accepted));
        _default = item;
    }

    /// <summary>Removes the given item from the set.</summary>
    /// <param name="item">The item to deny.</param>
    public void Deny(object item)
    {
        _accepted = DeleteInPlace(item, _accepted);
        if (SchemeUtilities.IsEqual(item, _default))
        {
            _default = Nil.Instance;
        }
    }

    /// <summary>
    /// Destructively removes every element equal to an item, which is
    /// <c>scm_delete_x</c>.
    /// </summary>
    /// <param name="item">The item to remove.</param>
    /// <param name="list">The list to remove from; its pairs are reused.</param>
    /// <returns>The list without the item.</returns>
    private static object DeleteInPlace(object item, object list)
    {
        while (list is Pair head && SchemeUtilities.IsEqual(head.Car, item))
        {
            list = head.Cdr;
        }

        Pair previous = list as Pair;
        while (previous != null && previous.Cdr is Pair current)
        {
            if (SchemeUtilities.IsEqual(current.Car, item))
            {
                previous.Cdr = current.Cdr;
            }
            else
            {
                previous = current;
            }
        }

        return list;
    }

    /// <summary>Copies a list's spine, which is <c>scm_list_copy</c>.</summary>
    /// <param name="list">The list to copy.</param>
    /// <returns>The copy, sharing the elements.</returns>
    private static object ListCopy(object list)
    {
        object head = Nil.Instance;
        Pair last = null;
        for (object p = list; p is Pair pair; p = pair.Cdr)
        {
            Pair copy = new Pair(pair.Car, Nil.Instance);
            if (last == null)
            {
                head = copy;
            }
            else
            {
                last.Cdr = copy;
            }

            last = copy;
        }

        return head;
    }
}
