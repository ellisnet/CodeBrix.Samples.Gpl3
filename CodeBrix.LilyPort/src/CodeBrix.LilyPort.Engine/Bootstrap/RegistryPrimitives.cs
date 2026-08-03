// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The registries LilyPond's Scheme fills in as it loads: grob interfaces, translators,
/// stencil heads -- plus the two small value types the same files construct, the
/// unpure-pure container and the compiled regular expression.
/// <para>
/// These are pure bookkeeping in the C++ as well: <c>ly:add-interface</c> writes to a
/// hash table and nothing more. Porting them removes the last placeholder values from
/// the startup path, which matters because a placeholder is truthy and would let a later
/// lookup silently succeed with nonsense.
/// </para>
/// </summary>
public static class RegistryPrimitives
{
    /// <summary>Installs the registry primitives, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    /// <returns>The registries the interpreter will populate.</returns>
    public static EngineRegistries Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        EngineRegistries registries = new EngineRegistries();
        InstallInterfaces(interpreter, registries);
        InstallTranslators(interpreter, registries);
        InstallStencils(interpreter, registries);
        InstallUnpurePure(interpreter);
        InstallRegex(interpreter);
        return registries;
    }

    private static void InstallInterfaces(Interpreter interpreter, EngineRegistries registries)
    {
        interpreter.DefinePrimitive("ly:add-interface", 3, 3, a =>
        {
            Symbol name = AsSymbol(a[0], "ly:add-interface");
            registries.GrobInterfaces[name] = Pair.List(a[0], a[1], a[2]);
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:all-grob-interfaces", 0, 0, a =>
        {
            List<object> entries = new List<object>(registries.GrobInterfaces.Count);
            foreach (KeyValuePair<Symbol, object> entry in registries.GrobInterfaces)
            {
                entries.Add(new Pair(entry.Key, entry.Value));
            }

            return Pair.ListFrom(entries);
        });
    }

    private static void InstallTranslators(Interpreter interpreter, EngineRegistries registries)
    {
        interpreter.DefinePrimitive("ly:register-translator", 2, 3, a =>
        {
            Symbol name = AsSymbol(a[1], "ly:register-translator");
            object description = a.Length > 2 && !(a[2] is DefaultArgument) ? a[2] : Nil.Instance;
            registries.Translators[name] = a[0];
            registries.TranslatorDescriptions[name] = description;
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:get-all-translators", 0, 0, a =>
        {
            List<object> creators = new List<object>(registries.Translators.Count);
            foreach (KeyValuePair<Symbol, object> entry in registries.Translators)
            {
                creators.Add(entry.Value);
            }

            return Pair.ListFrom(creators);
        });

        interpreter.DefinePrimitive("ly:translator-name", 1, 1, a =>
        {
            foreach (KeyValuePair<Symbol, object> entry in registries.Translators)
            {
                if (ReferenceEquals(entry.Value, a[0]))
                {
                    return entry.Key;
                }
            }

            return false;
        });

        interpreter.DefinePrimitive("ly:translator-description", 1, 1, a =>
        {
            foreach (KeyValuePair<Symbol, object> entry in registries.Translators)
            {
                if (ReferenceEquals(entry.Value, a[0]))
                {
                    return registries.TranslatorDescriptions[entry.Key];
                }
            }

            return Nil.Instance;
        });
    }

    private static void InstallStencils(Interpreter interpreter, EngineRegistries registries)
    {
        interpreter.DefinePrimitive("ly:register-stencil-expression", 1, 1, a =>
        {
            registries.StencilHeads.Add(AsSymbol(a[0], "ly:register-stencil-expression"));
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:all-stencil-expressions", 0, 0, a =>
        {
            List<object> heads = new List<object>(registries.StencilHeads.Count);
            foreach (Symbol head in registries.StencilHeads)
            {
                heads.Add(head);
            }

            return Pair.ListFrom(heads);
        });

        interpreter.DefinePrimitive("ly:make-stencil", 1, 3, a =>
        {
            // Upstream rejects an expression whose head was never registered, which is
            // what catches a backend procedure name typo at construction rather than at
            // render time. Keeping the check keeps that early failure.
            if (a[0] is Pair expression
                && expression.Car is Symbol head
                && !registries.StencilHeads.Contains(head))
            {
                throw SchemeErrors.WrongType("ly:make-stencil", "registered stencil expression", a[0]);
            }

            return new Stencil(
                a[0],
                ToInterval(a, 1, "ly:make-stencil"),
                ToInterval(a, 2, "ly:make-stencil"));
        });

        interpreter.DefinePrimitive("ly:stencil-expr", 1, 1, a =>
            a[0] is Stencil stencil ? stencil.Expression ?? Nil.Instance : Nil.Instance);

        interpreter.DefinePrimitive("ly:stencil-extent", 2, 2, a =>
        {
            if (!(a[0] is Stencil stencil))
            {
                throw SchemeErrors.WrongType("ly:stencil-extent", "stencil", a[0]);
            }

            Interval extent = stencil.Extent(
                SchemeConvert.ToLong(a[1], "ly:stencil-extent") == 0 ? Axis.X : Axis.Y);
            return new Pair(extent.Left, extent.Right);
        });

        interpreter.DefinePrimitive("ly:stencil-empty?", 1, 2, a =>
            !(a[0] is Stencil stencil) || stencil.IsEmpty);
    }

    private static void InstallUnpurePure(Interpreter interpreter)
    {
        interpreter.DefinePrimitive("ly:make-unpure-pure-container", 1, 2, a =>
            new UnpurePureContainer(
                a[0],
                a.Length > 1 && !(a[1] is DefaultArgument) ? a[1] : null));

        interpreter.DefinePrimitive("ly:unpure-pure-container-unpure-part", 1, 1, a =>
            a[0] is UnpurePureContainer container ? container.Unpure : false);

        interpreter.DefinePrimitive("ly:unpure-pure-container-pure-part", 1, 1, a =>
            a[0] is UnpurePureContainer container ? container.Pure : false);

        interpreter.DefinePrimitive("ly:unpure-pure-container?", 1, 1, a =>
            a[0] is UnpurePureContainer);
    }

    private static void InstallRegex(Interpreter interpreter)
    {
        // LilyPond compiles these with GLib; the patterns it uses are Perl-compatible,
        // which is also what System.Text.RegularExpressions implements.
        interpreter.DefinePrimitive("ly:make-regex", 1, 1, a =>
            new Regex(StringPrimitives.Text(a[0], "ly:make-regex"), RegexOptions.None));

        interpreter.DefinePrimitive("ly:regex?", 1, 1, a => a[0] is Regex);

        interpreter.DefinePrimitive("ly:regex-exec", 2, 3, a =>
        {
            Match match = AsRegex(a[0], "ly:regex-exec")
                .Match(StringPrimitives.Text(a[1], "ly:regex-exec"));
            return match.Success ? (object)match : false;
        });

        interpreter.DefinePrimitive("ly:regex-match?", 1, 1, a => a[0] is Match match && match.Success);

        interpreter.DefinePrimitive("ly:regex-match-substring", 1, 2, a =>
        {
            if (!(a[0] is Match match))
            {
                throw SchemeErrors.WrongType("ly:regex-match-substring", "regex match", a[0]);
            }

            int group = a.Length > 1 && !(a[1] is DefaultArgument)
                ? SchemeConvert.ToInt(a[1], "ly:regex-match-substring")
                : 0;
            return group < match.Groups.Count
                ? (object)new MutableString(match.Groups[group].Value)
                : false;
        });

        interpreter.DefinePrimitive("ly:regex-replace", 3, -1, a =>
            new MutableString(AsRegex(a[0], "ly:regex-replace").Replace(
                StringPrimitives.Text(a[1], "ly:regex-replace"),
                StringPrimitives.Text(a[2], "ly:regex-replace"))));

        interpreter.DefinePrimitive("ly:regex-split", 2, 2, a =>
        {
            string[] parts = AsRegex(a[0], "ly:regex-split")
                .Split(StringPrimitives.Text(a[1], "ly:regex-split"));
            List<object> pieces = new List<object>(parts.Length);
            foreach (string part in parts)
            {
                pieces.Add(new MutableString(part));
            }

            return Pair.ListFrom(pieces);
        });
    }

    private static Interval ToInterval(object[] arguments, int index, string procedureName)
    {
        if (arguments.Length <= index || arguments[index] is DefaultArgument)
        {
            return Interval.Empty;
        }

        if (!(arguments[index] is Pair pair))
        {
            throw SchemeErrors.WrongType(procedureName, "number pair", arguments[index]);
        }

        return new Interval(
            SchemeConvert.ToDouble(pair.Car, procedureName),
            SchemeConvert.ToDouble(pair.Cdr, procedureName));
    }

    private static Regex AsRegex(object value, string procedureName)
        => value as Regex ?? throw SchemeErrors.WrongType(procedureName, "regex", value);

    private static Symbol AsSymbol(object value, string procedureName)
        => value as Symbol ?? throw SchemeErrors.WrongType(procedureName, "symbol", value);
}

/// <summary>
/// The tables LilyPond's Scheme layer populates while it loads.
/// <para>
/// These are the engine's global state in upstream too -- file-scope smobs in
/// <c>grob-interface-scheme.cc</c>, <c>translator-ctors.cc</c> and
/// <c>stencil-scheme.cc</c>. They are gathered into one object here so an interpreter
/// owns its own set rather than sharing process-wide statics, which is what lets tests
/// run independently.
/// </para>
/// </summary>
public sealed class EngineRegistries
{
    /// <summary>Gets the grob interfaces, keyed by interface name.</summary>
    public Dictionary<Symbol, object> GrobInterfaces { get; } = new Dictionary<Symbol, object>();

    /// <summary>Gets the translator creators, keyed by translator name.</summary>
    public Dictionary<Symbol, object> Translators { get; } = new Dictionary<Symbol, object>();

    /// <summary>Gets the translator descriptions, keyed by translator name.</summary>
    public Dictionary<Symbol, object> TranslatorDescriptions { get; } = new Dictionary<Symbol, object>();

    /// <summary>Gets the registered stencil expression heads.</summary>
    public HashSet<Symbol> StencilHeads { get; } = new HashSet<Symbol>();
}

/// <summary>
/// A pair of callbacks: one for the real value, one for a cheaper estimate used during
/// pure calculations.
/// <para>
/// LilyPond's layout needs a height before line breaking is decided, but the real height
/// may depend on the break. The pure callback answers without triggering that dependency.
/// </para>
/// </summary>
public sealed class UnpurePureContainer
{
    /// <summary>Initializes a container.</summary>
    /// <param name="unpure">The unpure expression.</param>
    /// <param name="pure">The pure expression, or null to reuse the unpure one.</param>
    public UnpurePureContainer(object unpure, object pure)
    {
        Unpure = unpure;

        // Upstream: "If pure is omitted, the value of unpure will be used twice".
        Pure = pure ?? unpure;
        IsPureOmitted = pure == null;
    }

    /// <summary>Gets the unpure expression.</summary>
    public object Unpure { get; }

    /// <summary>Gets the pure expression.</summary>
    public object Pure { get; }

    /// <summary>
    /// Gets a value indicating whether the pure part was omitted. When it was, a callback
    /// is given two extra arguments that are ignored for the sake of pure calculations.
    /// </summary>
    public bool IsPureOmitted { get; }

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description of the container.</returns>
    public override string ToString() => "#<unpure-pure-container>";
}
