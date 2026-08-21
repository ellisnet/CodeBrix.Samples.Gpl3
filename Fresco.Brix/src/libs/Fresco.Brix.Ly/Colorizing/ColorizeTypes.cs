// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using Token = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.Ly.Colorizing; //was previously: ly/colorize.py (the mapping types)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One highlighting style: a name, the optional base (default) style it
/// inherits from, and the token classes it applies to.
/// </summary>
/// <remarks>Upstream this is the <c>style</c> named tuple.</remarks>
public sealed class Style
{
    /// <summary>Creates a style.</summary>
    /// <param name="name">The style name, unique within its mode group.</param>
    /// <param name="baseName">The default style inherited from, or null.</param>
    /// <param name="classes">The token classes this style applies to.</param>
    public Style(string name, string baseName, params Type[] classes)
    {
        Name = name;
        Base = baseName;
        Classes = classes ?? Array.Empty<Type>();
    }

    /// <summary>Gets the style name.</summary>
    public string Name { get; }

    /// <summary>Gets the inherited default style name, or null.</summary>
    public string Base { get; }

    /// <summary>Gets the token classes this style applies to.</summary>
    public IReadOnlyList<Type> Classes { get; }
}

/// <summary>
/// A group of styles for one lexer mode (<c>lilypond</c>, <c>scheme</c>,
/// <c>html</c>, <c>texinfo</c>, <c>mup</c>).
/// </summary>
/// <remarks>Upstream this is a (mode, styles) tuple in the mapping.</remarks>
public sealed class StyleGroup
{
    /// <summary>Creates a group.</summary>
    /// <param name="mode">The mode name.</param>
    /// <param name="styles">The styles in the group, in upstream order.</param>
    public StyleGroup(string mode, params Style[] styles)
    {
        Mode = mode;
        Styles = styles ?? Array.Empty<Style>();
    }

    /// <summary>Gets the mode name.</summary>
    public string Mode { get; }

    /// <summary>Gets the styles, in upstream order.</summary>
    public IReadOnlyList<Style> Styles { get; }
}

/// <summary>
/// The CSS class a token maps to: its mode, its style name and the base
/// (default) style it inherits from.
/// </summary>
/// <remarks>Upstream this is the <c>css_class</c> named tuple.</remarks>
public sealed class CssClass : IEquatable<CssClass>
{
    /// <summary>Creates a CSS class triple.</summary>
    /// <param name="mode">The mode name.</param>
    /// <param name="name">The style name.</param>
    /// <param name="baseName">The inherited default style, or null.</param>
    public CssClass(string mode, string name, string baseName)
    {
        Mode = mode;
        Name = name;
        Base = baseName;
    }

    /// <summary>Gets the mode name.</summary>
    public string Mode { get; }

    /// <summary>Gets the style name.</summary>
    public string Name { get; }

    /// <summary>Gets the inherited default style name, or null.</summary>
    public string Base { get; }

    /// <inheritdoc/>
    public bool Equals(CssClass other)
        => other != null
            && Mode == other.Mode && Name == other.Name && Base == other.Base;

    /// <inheritdoc/>
    public override bool Equals(object obj) => Equals(obj as CssClass);

    /// <inheritdoc/>
    public override int GetHashCode()
        => HashCode.Combine(Mode, Name, Base);

    /// <inheritdoc/>
    public override string ToString()
        => Base == null ? $"{Mode}-{Name}" : $"{Mode}-{Name} {Base}";
}

/// <summary>
/// Maps token classes to arbitrary values (normally highlighting styles).
/// <para>
/// Looking a token up walks its base-class chain — stopping before
/// <see cref="Fresco.Brix.Ly.Lex.Token"/>, exactly the range upstream's
/// <c>_token_mro_slice</c> covers — and caches the answer for the exact
/// class, so repeat lookups are a dictionary hit.
/// </para>
/// </summary>
/// <typeparam name="TValue">The mapped value type.</typeparam>
/// <remarks>Upstream this is the <c>Mapper</c> dict subclass.</remarks>
public sealed class TokenMapper<TValue>
    where TValue : class
{
    private readonly Dictionary<Type, TValue> _map = new Dictionary<Type, TValue>();
    private readonly Func<Type, IEnumerable<Type>> _bases;

    /// <summary>Creates an empty mapper over the declared base chain.</summary>
    public TokenMapper()
        : this(Array.Empty<KeyValuePair<Type, TValue>>(), null)
    {
    }

    /// <summary>Creates a mapper over the given class/value pairs.</summary>
    /// <param name="items">The pairs; later entries win, as in a dict literal.</param>
    /// <param name="bases">The base-class walk to resolve unregistered classes
    /// with, or null for the declared C# chain. Ports whose python original
    /// used multiple inheritance pass a walk that splices the dropped bases
    /// back in (see <c>Colorize.PythonBases</c>).</param>
    public TokenMapper(
        IEnumerable<KeyValuePair<Type, TValue>> items,
        Func<Type, IEnumerable<Type>> bases = null)
    {
        _bases = bases ?? DeclaredBases;
        foreach (var item in items)
        {
            _map[item.Key] = item.Value;
        }
    }

    /// <summary>
    /// The default walk: the declared base classes above a token class, up to
    /// but not including <see cref="Fresco.Brix.Ly.Lex.Token"/>.
    /// </summary>
    /// <param name="tokenClass">The token class.</param>
    /// <returns>The bases, nearest first.</returns>
    public static IEnumerable<Type> DeclaredBases(Type tokenClass)
    {
        for (var t = tokenClass?.BaseType;
             t != null && t != typeof(Lex.Token);
             t = t.BaseType)
        {
            yield return t;
        }
    }

    /// <summary>Gets or sets the value registered for an exact token class.</summary>
    /// <param name="tokenClass">The token class.</param>
    /// <returns>The registered value, or null.</returns>
    public TValue this[Type tokenClass]
    {
        get => _map.TryGetValue(tokenClass, out var value) ? value : null;
        set => _map[tokenClass] = value;
    }

    /// <summary>Resolves the value for a token, walking its base classes.</summary>
    /// <param name="token">The token.</param>
    /// <returns>The mapped value, or null when nothing matches.</returns>
    public TValue ValueFor(Token token)
        => token == null ? null : ValueForClass(token.GetType());

    /// <summary>Resolves the value for a token class, walking its bases.</summary>
    /// <param name="tokenClass">The token class.</param>
    /// <returns>The mapped value, or null when nothing matches.</returns>
    public TValue ValueForClass(Type tokenClass)
    {
        if (tokenClass == null) { return null; }

        if (_map.TryGetValue(tokenClass, out var cached))
        {
            return cached;
        }

        TValue value = null;
        //Upstream walks __mro__[1:-len(Token.__mro__)] — the bases above the
        //exact class, stopping before ly.lex.Token itself.
        foreach (var t in _bases(tokenClass))
        {
            if (_map.TryGetValue(t, out value))
            {
                break;
            }

            value = null;
        }

        //Upstream caches the resolved value under the exact class, so the
        //second lookup of the same class is a plain dict hit.
        _map[tokenClass] = value;
        return value;
    }
}
