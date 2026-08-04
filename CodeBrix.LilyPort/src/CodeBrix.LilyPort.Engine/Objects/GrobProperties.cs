/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2004--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/context-property.cc;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

// This module is concerned with managing grob-properties (more
// exactly, grob property templates, as they are not yet part of a
// grob) inside of context properties, in a context-hierarchical
// manner, with one stack for properties and subproperties per
// context.

/// <summary>
/// The override stack for one grob type in one context — what a context property named
/// <c>NoteHead</c> or <c>Beam</c> actually holds.
/// <para>
/// The four alists are not redundant. <see cref="Alist"/> is what this context has
/// pushed, and its TAIL is physically the enclosing context's result, which is what
/// <see cref="BasedOn"/> remembers so a change up the tree can be detected.
/// <see cref="Cooked"/> is the same list with nested overrides expanded, cached
/// against <see cref="CookedFrom"/> so the expansion is not redone on every grob.
/// </para>
/// </summary>
public sealed class GrobProperties
{
    /// <summary>Initializes an override stack.</summary>
    /// <param name="alist">The overrides, possibly carrying unexpanded nested ones.</param>
    /// <param name="basedOn">The enclosing context's result this list is built on.</param>
    public GrobProperties(object alist, object basedOn)
    {
        Alist = alist ?? Nil.Instance;
        BasedOn = basedOn ?? Nil.Instance;

        // Initialising the cache from alist rather than from basedOn assumes the
        // constructor is never handed a list that already carries partial overrides.
        // Upstream records the same assumption, and that it should never happen.
        Cooked = Alist;
        CookedFrom = Alist;
        Nested = 0;
    }

    /// <summary>Gets or sets the overrides, which may carry unexpanded nested entries.</summary>
    public object Alist { get; set; }

    /// <summary>Gets or sets the enclosing context's result that the overrides sit on.</summary>
    public object BasedOn { get; set; }

    /// <summary>Gets or sets the expanded overrides.</summary>
    public object Cooked { get; set; }

    /// <summary>Gets or sets the list the expansion was made from.</summary>
    public object CookedFrom { get; set; }

    /// <summary>
    /// Gets or sets how many entries must not appear in the expanded list.
    /// <para>
    /// Nested overrides, and also temporary overrides and reverts, which are
    /// identified by a key that is not a symbol.
    /// </para>
    /// </summary>
    public int Nested { get; set; }

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description of the stack.</returns>
    public override string ToString() => "#<Grob_properties>";
}

/// <summary>
/// The algorithms over a <see cref="GrobProperties"/> stack, together with the context
/// and grob name that identify it.
/// <para>
/// Upstream keeps this separate from the data for a reason worth preserving: the
/// algorithms need to know which context and which grob they are working on, and there
/// is no point storing that in every stack.
/// </para>
/// <para>
/// This is what <c>\override</c>, <c>\revert</c>, <c>\once \override</c> and
/// <c>\temporary</c> are implemented in terms of, and what an engraver reads through
/// <see cref="Updated"/> when it creates a grob.
/// </para>
/// </summary>
public struct GrobPropertyInfo
{
    private readonly Context _context;
    private readonly Symbol _symbol;
    private GrobProperties _properties;

    /// <summary>Initializes the accessor.</summary>
    /// <param name="context">The context to work in.</param>
    /// <param name="symbol">The grob name, such as <c>NoteHead</c>.</param>
    /// <param name="properties">The stack, when it is already known.</param>
    public GrobPropertyInfo(Context context, Symbol symbol, GrobProperties properties = null)
    {
        _context = context;
        _symbol = symbol;
        _properties = properties;
    }

    /// <summary>Gets a value indicating whether a stack has been found or created.</summary>
    public bool HasProperties => _properties != null;

    /// <summary>Gets the stack, or null when there is none.</summary>
    public GrobProperties Properties => _properties;

    /// <summary>Gets the context this accessor works in.</summary>
    public Context Context => _context;

    /// <summary>
    /// Finds the stack, looking up the context tree. The result may name a DIFFERENT
    /// context from this one.
    /// </summary>
    /// <returns>An accessor for wherever the stack was found.</returns>
    public GrobPropertyInfo Find()
    {
        if (_properties != null)
        {
            return this;
        }

        Context where = _context?.WhereDefined(_symbol, out object value);
        if (where != null)
        {
            where = _context.WhereDefined(_symbol, out object found);
            GrobProperties properties = found as GrobProperties;
            if (!ReferenceEquals(where, _context))
            {
                return new GrobPropertyInfo(where, _symbol, properties);
            }

            _properties = properties;
        }

        return this;
    }

    /// <summary>Checks whether THIS context defines the stack, without looking up.</summary>
    /// <returns><see langword="true"/> when it does.</returns>
    public bool Check()
    {
        if (_properties != null)
        {
            return true;
        }

        if (_context != null && _context.HereDefined(_symbol, out object value))
        {
            _properties = value as GrobProperties;
        }

        return _properties != null;
    }

    /// <summary>
    /// Ensures THIS context has its own stack, creating one from the global default.
    /// </summary>
    /// <returns><see langword="true"/> when there is one to work with.</returns>
    public bool Create()
    {
        if (Check())
        {
            return true;
        }

        // Check that a grob of this name exists in this output at all, which can only
        // be decided in the global context. The common case is that this context
        // already has a stack, so that is checked first and the global lookup only
        // happens when the context is pristine.
        Context top = _context?.Root;
        if (!(top is GlobalContext))
        {
            // Context is probably dead.
            return false;
        }

        /*
          Don't mess with MIDI.
        */
        if (ReferenceEquals(top, _context))
        {
            return false;
        }

        if (!top.HereDefined(_symbol, out object globalValue))
        {
            return false;
        }

        if (!(globalValue is GrobProperties definition))
        {
            Warn.ProgrammingError("Grob definition expected");
            return false;
        }

        // Built from the default definition, which is what is available right now. It
        // may not be accurate, because overrides in intermediate contexts are not
        // considered here -- Updated folds those in when it is called.
        GrobProperties properties = new GrobProperties(definition.Alist, definition.Alist);
        _context.SetProperty(_symbol, properties);
        _properties = properties;
        return true;
    }

    /*
      Grob descriptions (ie. alists with layout properties) are
      represented as a (ALIST . BASED-ON) pair, where BASED-ON is the
      alist defined in an ancestor context. BASED-ON should always be a
      tail of ALIST.
    */

    /// <summary>
    /// Pushes one override. The returned cell can be given to
    /// <see cref="MatchedPop"/> and will cancel only that same override.
    /// </summary>
    /// <param name="propertyPath">The property path, outermost key first.</param>
    /// <param name="value">The value to push.</param>
    /// <returns>The pushed cell, or the empty list when nothing was pushed.</returns>
    public object Push(object propertyPath, object value)
    {
        /*
          Don't mess with MIDI.
        */
        if (!Create())
        {
            return Nil.Instance;
        }

        if (!(propertyPath is Pair path))
        {
            return Nil.Instance;
        }

        object symbol = path.Car;
        object rest = path.Cdr;

        if (rest is Pair)
        {
            // Poor man's type checking: check an invented value of the right shape.
            if (!(symbol is Symbol name)
                || !SchemeUtilities.TypeCheckAssignment(
                    name,
                    NestedProperty.NestedCreateAlist(rest, value),
                    BackendTypeSymbol))
            {
                return Nil.Instance;
            }

            Pair nestedCell = new Pair(propertyPath, value);
            _properties.Alist = new Pair(nestedCell, _properties.Alist);
            _properties.Nested++;
            return nestedCell;
        }

        /* it's tempting to replace the head of the list if it's the same
           property. However, we have to keep this info around, in case we have to
           \revert back to it.
        */
        if (!(symbol is Symbol plainName)
            || !SchemeUtilities.TypeCheckAssignment(plainName, value, BackendTypeSymbol))
        {
            return Nil.Instance;
        }

        Pair cell = new Pair(plainName, value);
        _properties.Alist = new Pair(cell, _properties.Alist);
        return cell;
    }

    /// <summary>
    /// Pushes a <c>\once \override</c>, which cancels itself at the end of the
    /// timestep.
    /// </summary>
    /// <param name="propertyPath">The property path, outermost key first.</param>
    /// <param name="value">The value to push.</param>
    /// <returns>A token for <see cref="MatchedPop"/>.</returns>
    public object TemporaryOverride(object propertyPath, object value)
    {
        object cell = Push(propertyPath, value);
        if (!(cell is Pair pair))
        {
            return cell;
        }

        if (pair.Car is Symbol)
        {
            _properties.Nested++;
        }

        // Mark the entry as temporary by wrapping it: a true key means "an override
        // that must not reach the expanded list".
        Pair marker = new Pair(true, pair);
        _properties.Alist = new Pair(marker, ((Pair)_properties.Alist).Cdr);
        return marker;
    }

    /// <summary>
    /// Suppresses an override for one timestep, as <c>\once \revert</c> does.
    /// </summary>
    /// <param name="propertyPath">The property path, outermost key first.</param>
    /// <returns>A token for <see cref="MatchedPop"/>.</returns>
    public object TemporaryRevert(object propertyPath)
    {
        if (!Check() || !(propertyPath is Pair path))
        {
            return Nil.Instance;
        }

        object key = path.Cdr is Pair ? propertyPath : path.Car;
        object found = NestedProperty.AssocTail(key, _properties.Alist, _properties.BasedOn);
        if (!(found is Pair where) || !(where.Car is Pair cell))
        {
            return Nil.Instance;
        }

        // A false key means "a revert": the original cell rides along inside so that
        // MatchedPop can put it back.
        Pair marker = new Pair(false, cell);
        if (cell.Car is Symbol)
        {
            _properties.Nested++;
        }

        _properties.Alist = NestedProperty.PartialListCopy(
            _properties.Alist, where, new Pair(marker, where.Cdr));
        return marker;
    }

    /// <summary>Cancels exactly the override a push returned, and nothing else.</summary>
    /// <param name="cell">The token a push returned.</param>
    public void MatchedPop(object cell)
    {
        if (!(cell is Pair) || !Check())
        {
            return;
        }

        object currentAlist = _properties.Alist;
        object daddy = _properties.BasedOn;

        for (object cursor = currentAlist; !ReferenceEquals(cursor, daddy); )
        {
            if (!(cursor is Pair pair))
            {
                return;
            }

            if (ReferenceEquals(pair.Car, cell))
            {
                object key = ((Pair)cell).Car;
                if (key is bool flag && !flag)
                {
                    // A temporary revert: put the original cell back.
                    object original = ((Pair)cell).Cdr;
                    if (original is Pair originalPair && originalPair.Car is Symbol)
                    {
                        _properties.Nested--;
                    }

                    _properties.Alist = NestedProperty.PartialListCopy(
                        currentAlist, pair, new Pair(original, pair.Cdr));
                    return;
                }

                if (!(key is Symbol))
                {
                    _properties.Nested--;
                }

                _properties.Alist = NestedProperty.PartialListCopy(currentAlist, pair, pair.Cdr);
                return;
            }

            cursor = pair.Cdr;
        }
    }

    /// <summary>Reverts the override named by a property path.</summary>
    /// <param name="propertyPath">The property path, outermost key first.</param>
    public void Pop(object propertyPath)
    {
        if (!Check())
        {
            return;
        }

        object currentAlist = _properties.Alist;
        object daddy = _properties.BasedOn;

        if (!(propertyPath is Pair path) || !(path.Car is Symbol))
        {
            Warn.ProgrammingError("Grob property path should be list of symbols.");
            return;
        }

        if (path.Cdr is Pair)
        {
            object before = currentAlist;
            currentAlist = NestedProperty.EvictFromAlist(propertyPath, currentAlist, daddy);
            if (ReferenceEquals(before, currentAlist))
            {
                return;
            }

            _properties.Nested--;
        }
        else
        {
            currentAlist = NestedProperty.EvictFromAlist(path.Car, currentAlist, daddy);
        }

        if (ReferenceEquals(currentAlist, daddy))
        {
            _properties = null;
            _context.UnsetProperty(_symbol);
            return;
        }

        _properties.Alist = currentAlist;
    }

    /// <summary>Pushes a value, or reverts when there is none.</summary>
    /// <param name="propertyPath">The property path, outermost key first.</param>
    /// <param name="value">The value, or null to revert.</param>
    public void PushPop(object propertyPath, object value)
    {
        if (value == null)
        {
            Pop(propertyPath);
            return;
        }

        Push(propertyPath, value);
    }

    /// <summary>
    /// Returns the effective property alist for this grob in this context, folding in
    /// every enclosing context's overrides and expanding nested ones.
    /// <para>
    /// This is what an engraver reads when it creates a grob, and it is why
    /// <c>\override</c> in a Staff reaches a NoteHead made in a Voice.
    /// </para>
    /// </summary>
    /// <returns>The effective alist, or the empty list when this grob is not defined.</returns>
    public object Updated()
    {
        GrobPropertyInfo where = Find();
        if (!where.HasProperties)
        {
            return Nil.Instance;
        }

        Context parent = where._context?.Parent;
        object daddyProperties = parent != null
            ? new GrobPropertyInfo(parent, _symbol).Updated()
            : Nil.Instance;

        GrobProperties properties = where._properties;
        object basedOn = properties.BasedOn;
        object alist = properties.Alist;

        if (!ReferenceEquals(basedOn, daddyProperties))
        {
            properties.BasedOn = daddyProperties;
            alist = NestedProperty.PartialListCopy(alist, basedOn, daddyProperties);
            properties.Alist = alist;
        }

        if (ReferenceEquals(properties.CookedFrom, alist))
        {
            return properties.Cooked;
        }

        properties.CookedFrom = alist;
        properties.Cooked = NestedProperty.NalistToAlist(alist, properties.Nested);
        return properties.Cooked;
    }

    private static readonly Symbol BackendTypeSymbol = Symbol.Intern("backend-type?");

    /*
      Convenience: a push/pop grob property using a single grob_property
      as argument.
    */

    /// <summary>Pushes or reverts a single, non-nested grob property.</summary>
    /// <param name="context">The context to work in.</param>
    /// <param name="grob">The grob name.</param>
    /// <param name="grobProperty">The property name.</param>
    /// <param name="value">The value, or null to revert.</param>
    public static void ExecutePushPopProperty(
        Context context,
        Symbol grob,
        Symbol grobProperty,
        object value)
    {
        new GrobPropertyInfo(context, grob).PushPop(
            new Pair(grobProperty, Nil.Instance), value);
    }

    /// <summary>
    /// Applies a list of property operations to a context — the <c>\with</c> block and
    /// the context definition's own defaults.
    /// </summary>
    /// <param name="context">The context to apply to.</param>
    /// <param name="operations">The operations, in the order specified.</param>
    public static void ApplyPropertyOperations(Context context, object operations)
    {
        object cursor = operations;
        while (cursor is Pair listPair)
        {
            if (listPair.Car is Pair entry && entry.Car is Symbol type)
            {
                object rest = entry.Cdr;
                switch (type.Name)
                {
                    case "push":
                        if (rest is Pair push && push.Cdr is Pair pushValue)
                        {
                            new GrobPropertyInfo(context, push.Car as Symbol)
                                .Push(pushValue.Cdr, pushValue.Car);
                        }

                        break;
                    case "pop":
                        if (rest is Pair pop)
                        {
                            new GrobPropertyInfo(context, pop.Car as Symbol).Pop(pop.Cdr);
                        }

                        break;
                    case "assign":
                        if (rest is Pair assign && assign.Cdr is Pair assignValue
                            && assign.Car is Symbol assignName)
                        {
                            context.SetProperty(assignName, assignValue.Car);
                        }

                        break;
                    case "apply":
                        if (rest is Pair apply)
                        {
                            SchemeUtilities.CallCallback(apply.Car, context);
                        }

                        break;
                    case "unset":
                        if (rest is Pair unset && unset.Car is Symbol unsetName)
                        {
                            context.UnsetProperty(unsetName);
                        }

                        break;
                    default:
                        break;
                }
            }

            cursor = listPair.Cdr;
        }
    }
}
