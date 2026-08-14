/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2004--2026 Jan Nieuwenhuizen <janneke@gnu.org>

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
using System.Text;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/prob.cc, lily/include/prob.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/*
  A formatted "system" (A block of titling also is a Property_object)

  To save memory, we don't keep around the System grobs, but put the
  formatted content of the grob is put into a
  Property_object. Page-breaking handles Property_object objects.
*/

/// <summary>
/// A generic property-carrying object: a type tag, an immutable property alist shared
/// with every object of the same kind, and a mutable alist of its own.
/// <para>
/// This is the base of the whole object model. <see cref="MusicObject"/> and
/// <see cref="StreamEvent"/> are Probs; so are paper systems and page objects. The
/// immutable/mutable split is what lets thousands of objects of one type share their
/// defaults while each carries only what it has actually overridden.
/// </para>
/// </summary>
public class Prob : ISchemeEqual
{
    private static readonly Symbol NameSymbol = Symbol.Intern("name");
    private static readonly Symbol UntransposableSymbol = Symbol.Intern("untransposable");
    private static readonly Symbol ElementSymbol = Symbol.Intern("element");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol ArticulationsSymbol = Symbol.Intern("articulations");
    private static readonly Symbol PitchAlistSymbol = Symbol.Intern("pitch-alist");
    private static readonly Symbol TonicSymbol = Symbol.Intern("tonic");

    /// <summary>Initializes a prob.</summary>
    /// <param name="type">The type tag, a symbol such as <c>Music</c>.</param>
    /// <param name="immutableInit">The shared immutable property alist.</param>
    public Prob(object type, object immutableInit)
    {
        MutablePropertyAlist = Nil.Instance;
        ImmutablePropertyAlist = immutableInit ?? Nil.Instance;
        Type = type;
    }

    /// <summary>Initializes a copy of another prob, deep-copying its mutable properties.</summary>
    /// <param name="source">The prob to copy.</param>
    protected Prob(Prob source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        ImmutablePropertyAlist = source.ImmutablePropertyAlist;
        MutablePropertyAlist = Nil.Instance;
        Type = source.Type;
        MutablePropertyAlist = source.CopyMutableProperties();
    }

    /// <summary>Gets the type tag.</summary>
    public object Type { get; }

    /// <summary>Gets or sets the mutable property alist.</summary>
    public object MutablePropertyAlist { get; protected set; }

    /// <summary>Gets the immutable property alist, shared with every object of this type.</summary>
    public object ImmutablePropertyAlist { get; }

    /// <summary>Gets the C++ class name this object corresponds to.</summary>
    public virtual string ClassName => "Prob";

    /// <summary>
    /// Gets the object's name: its <c>name</c> property when it has one, otherwise the
    /// class name.
    /// </summary>
    public virtual string Name
    {
        get
        {
            object name = GetProperty(NameSymbol);
            return name is Symbol symbol ? symbol.Name : ClassName;
        }
    }

    /// <summary>Returns one of the two property alists.</summary>
    /// <param name="mutableAlist">
    /// <see langword="true"/> for the mutable alist, <see langword="false"/> for the
    /// immutable one.
    /// </param>
    /// <returns>The alist.</returns>
    public object GetPropertyAlist(bool mutableAlist)
        => mutableAlist ? MutablePropertyAlist : ImmutablePropertyAlist;

    /// <summary>
    /// Reads a property: the mutable alist first, then the immutable one.
    /// </summary>
    /// <param name="symbol">The property name.</param>
    /// <returns>The value, or the empty list when unset.</returns>
    public virtual object GetProperty(Symbol symbol)
    {
        Pair entry = SchemeUtilities.Assq(symbol, MutablePropertyAlist);
        if (entry != null)
        {
            return entry.Cdr;
        }

        entry = SchemeUtilities.Assq(symbol, ImmutablePropertyAlist);
        return entry == null ? Nil.Instance : entry.Cdr;
    }

    /// <summary>Reads a property by name.</summary>
    /// <param name="name">The property name.</param>
    /// <returns>The value, or the empty list when unset.</returns>
    public object GetProperty(string name) => GetProperty(Symbol.Intern(name));

    /// <summary>Writes a property into the mutable alist, type-checking it first.</summary>
    /// <param name="symbol">The property name.</param>
    /// <param name="value">The value to store.</param>
    public virtual void SetProperty(Symbol symbol, object value)
    {
        TypeCheckAssignment(symbol, value);
        MutablePropertyAlist = SchemeUtilities.AssqSet(MutablePropertyAlist, symbol, value);
    }

    /// <summary>Writes a property by name.</summary>
    /// <param name="name">The property name.</param>
    /// <param name="value">The value to store.</param>
    public void SetProperty(string name, object value) => SetProperty(Symbol.Intern(name), value);

    /// <summary>Removes a property from the mutable alist.</summary>
    /// <param name="symbol">The property name.</param>
    public void UnsetProperty(Symbol symbol)
        => MutablePropertyAlist = SchemeUtilities.AssqRemove(MutablePropertyAlist, symbol);

    /// <summary>
    /// Transposes every pitch this object carries, and recursively everything it holds.
    /// <para>
    /// This MUTATES the mutable alist in place, so the alist must not be shared —
    /// upstream says so outright, and the copy constructor's deep copy is what
    /// guarantees it.
    /// </para>
    /// </summary>
    /// <param name="delta">The interval to transpose by, from central C.</param>
    public void Transpose(Pitch delta)
    {
        if (SchemeUtilities.ToBool(GetProperty(UntransposableSymbol)))
        {
            return;
        }

        object cursor = MutablePropertyAlist;
        while (cursor is Pair listPair)
        {
            if (listPair.Car is Pair entry)
            {
                object property = entry.Car;
                object value = entry.Cdr;
                object newValue = value;

                if (value is Pitch pitch)
                {
                    Pitch transposed = pitch.Transposed(delta);

                    if (ReferenceEquals(property, TonicSymbol))
                    {
                        transposed = new Pitch(-1, transposed.NoteName, transposed.Alteration);
                    }

                    newValue = transposed;
                }
                else if (ReferenceEquals(property, ElementSymbol))
                {
                    if (value is Prob element)
                    {
                        element.Transpose(delta);
                    }
                }
                else if (ReferenceEquals(property, ElementsSymbol)
                         || ReferenceEquals(property, ArticulationsSymbol))
                {
                    TransposeMusicList(value, delta);
                }
                else if (ReferenceEquals(property, PitchAlistSymbol) && value is Pair)
                {
                    // Upstream reaches ly_transpose_key_alist here. A private identity
                    // stand-in sat in its place from before the real algorithm existed
                    // — landed with the MIDI group in MusicSequence — and it made every
                    // `\key PITCH \SCALE' carry the UNTRANSPOSED scale pattern: d minor
                    // announced c minor's three flats to the MIDI key signature. The
                    // stale-stand-in pattern again: a recorded absence stops being true
                    // the moment its owner lands, and nothing re-checks it.
                    newValue = Music.MusicSequence.TransposeKeyAlist(value, delta);
                }

                if (!ReferenceEquals(value, newValue))
                {
                    entry.Cdr = newValue;
                }
            }

            cursor = listPair.Cdr;
        }
    }

    /// <summary>Returns the external representation, in upstream's debug wording.</summary>
    /// <returns>The type, class and both alists.</returns>
    public override string ToString()
    {
        StringBuilder builder = new StringBuilder();
        builder.Append("#<Prob: ");
        builder.Append(Type);
        builder.Append(" C++: ");
        builder.Append(ClassName);
        builder.Append(' ');
        builder.Append(MutablePropertyAlist);
        builder.Append(' ');
        builder.Append(ImmutablePropertyAlist);
        builder.Append(" >");
        return builder.ToString();
    }

    /// <summary>
    /// Determines whether two probs carry the same properties.
    /// <para>
    /// Upstream's comparison exists only to make the copy constructor preserve
    /// equality, so it compares the two alists positionally rather than as sets, and
    /// skips <c>origin</c>.
    /// </para>
    /// </summary>
    /// <param name="a">The first prob.</param>
    /// <param name="b">The second prob.</param>
    /// <returns><see langword="true"/> when the two are equal.</returns>
    public static bool AreEqual(Prob a, Prob b)
    {
        if (a == null || b == null)
        {
            return ReferenceEquals(a, b);
        }

        if (!string.Equals(a.ClassName, b.ClassName, StringComparison.Ordinal))
        {
            return false;
        }

        return SchemeUtilities.PropertyAlistsEqual(a.ImmutablePropertyAlist, b.ImmutablePropertyAlist)
               && SchemeUtilities.PropertyAlistsEqual(a.MutablePropertyAlist, b.MutablePropertyAlist);
    }

    /// <summary>
    /// Compares this prob with another for <c>equal?</c> — upstream's
    /// <c>Prob::equal_p</c>, reached through the <see cref="ISchemeEqual"/> dispatch.
    /// <para>
    /// The eighth and last handler on the <see cref="ISchemeEqual"/> roster read out of
    /// the pinned source; it was left OWED at first because nothing demanded it, and it
    /// closed together with the MIDI group's carry-forwards. The
    /// algorithm itself was already here as <see cref="AreEqual"/>.
    /// </para>
    /// </summary>
    /// <param name="other">The value to compare against.</param>
    /// <returns><see langword="true"/> when the two are equal by value.</returns>
    public bool SchemeEquals(object other) => other is Prob prob && AreEqual(this, prob);

    /// <summary>Copies the mutable property alist for a new instance.</summary>
    /// <returns>The copied alist.</returns>
    protected virtual object CopyMutableProperties() => SchemeUtilities.DeepCopy(MutablePropertyAlist);

    /// <summary>
    /// Checks an assignment before it is stored. The base prob checks nothing;
    /// <see cref="MusicObject"/> and the grob layer override this.
    /// </summary>
    /// <param name="symbol">The property being set.</param>
    /// <param name="value">The value being assigned.</param>
    protected virtual void TypeCheckAssignment(Symbol symbol, object value)
    {
    }

    /// <summary>Transposes every music object in a Scheme list, in place.</summary>
    /// <param name="list">The list of music objects.</param>
    /// <param name="delta">The interval to transpose by.</param>
    protected static void TransposeMusicList(object list, Pitch delta)
    {
        object cursor = list;
        while (cursor is Pair pair)
        {
            if (pair.Car is Prob prob)
            {
                prob.Transpose(delta);
            }

            cursor = pair.Cdr;
        }
    }

}
