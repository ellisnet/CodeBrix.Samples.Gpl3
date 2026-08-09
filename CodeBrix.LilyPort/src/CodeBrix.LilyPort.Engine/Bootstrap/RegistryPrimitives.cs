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
using CodeBrix.LilyPort.Engine.Objects;
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
    private static readonly Symbol RegexMatchClassSymbol = Symbol.Intern("<regex-match>");
    private static readonly Symbol MakeSymbol = Symbol.Intern("make");

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

        // Upstream's C++ ADD_INTERFACE macros register from static initialisers, so their
        // 86 interfaces are in the table before any Scheme runs. The port has no static
        // initialisers to run -- most of those grob classes are not ported yet -- so it
        // reads the same declarations from a vendored extraction and registers them HERE,
        // at the equivalent point: after the primitive exists, before the Scheme layer
        // loads. scm/define-grob-interfaces.scm then adds its own 88 and overwrites the
        // two that both halves declare, which is what upstream's ordering produces.
        GrobInterfaceTable.Register(registries);

        InstallGrobProperties(interpreter);

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

        // Upstream returns the interface HASH TABLE — document-backend hash-folds and
        // hashq-refs it, so an alist is not an acceptable stand-in. The table is
        // rebuilt per call from the registry (upstream hands out its live table); the
        // interfaces are all registered at startup, so the difference is unobservable.
        interpreter.DefinePrimitive("ly:all-grob-interfaces", 0, 0, a =>
        {
            SchemeHashTable table = new SchemeHashTable(null);
            foreach (KeyValuePair<Symbol, object> entry in registries.GrobInterfaces)
            {
                table.CreateHandle(entry.Key, entry.Value);
            }

            return table;
        });
    }

    private static void InstallGrobProperties(Interpreter interpreter)
    {
        // Packages a property alist as the override-stack container a context property
        // named after a grob holds. Upstream's Grob_properties is a smob with a type
        // predicate, so both are registered.
        interpreter.DefinePrimitive("ly:make-grob-properties", 1, 1, a =>
            new GrobProperties(a[0], Nil.Instance));

        interpreter.DefinePrimitive("ly:grob-properties?", 1, 1, a => a[0] is GrobProperties);
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

        // A match is a GOOPS <regex-match>, not the underlying match object, and that is
        // not decoration: scm/lily-library.scm defines ly:regex-match-positions,
        // ly:regex-match-substring, ly:regex-match-prefix and ly:regex-match-suffix in
        // SCHEME, over the two slots this fills in — and those definitions replace the
        // port's own primitives of the same names when lily-library.scm loads. Handing
        // back anything else makes every one of them slot-ref a non-object, which is
        // what stopped hyphenate-internal-words.scm loading.
        interpreter.DefinePrimitive("ly:regex-exec", 2, 3, a =>
        {
            string subject = StringPrimitives.Text(a[1], "ly:regex-exec");
            Match match = AsRegex(a[0], "ly:regex-exec").Match(subject);
            return match.Success ? MakeRegexMatch(subject, match) : false;
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

        // The REPLACEMENTS are a rest list, and each element is one of three things: a
        // string emitted as-is, a non-negative integer naming a capture group, or a
        // PROCEDURE called on the match object. An earlier pass read the third argument
        // as a single .NET replacement pattern, which is wrong twice over — it made a
        // procedure a type error (that is what stopped hyphenate-internal-words.scm
        // loading) and it let `$1' in an ordinary replacement string expand, where
        // upstream emits it literally.
        interpreter.DefinePrimitive("ly:regex-replace", 2, -1, a =>
        {
            Regex regex = AsRegex(a[0], "ly:regex-replace");
            string subject = StringPrimitives.Text(a[1], "ly:regex-replace");

            List<object> replacements = new List<object>();
            for (int i = 2; i < a.Length; i++)
            {
                if (a[i] is DefaultArgument)
                {
                    continue;
                }

                if (!(a[i] is MutableString || a[i] is string
                      || SchemeConvert.IsNumber(a[i])
                      || SchemeUtilities.IsProcedure(a[i])))
                {
                    throw SchemeErrors.WrongType(
                        "ly:regex-replace",
                        "string, non-negative integer or procedure",
                        a[i]);
                }

                replacements.Add(a[i]);
            }

            return new MutableString(regex.Replace(
                subject, match => BuildReplacement(subject, match, replacements)));
        });

        interpreter.DefinePrimitive("ly:regex-quote", 1, 1, a =>
            new MutableString(Regex.Escape(StringPrimitives.Text(a[0], "ly:regex-quote"))));

        interpreter.DefinePrimitive("ly:regex-exec->list", 2, 2, a =>
        {
            string subject = StringPrimitives.Text(a[1], "ly:regex-exec->list");
            List<object> matches = new List<object>();
            foreach (Match match in AsRegex(a[0], "ly:regex-exec->list").Matches(subject))
            {
                matches.Add(MakeRegexMatch(subject, match));
            }

            return Pair.ListFrom(matches);
        });

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

    /// <summary>Builds one match's replacement text from the replacement list.</summary>
    /// <param name="subject">The string being replaced in.</param>
    /// <param name="match">The match being replaced.</param>
    /// <param name="replacements">The replacement specifiers, in order.</param>
    /// <returns>The text to substitute.</returns>
    private static string BuildReplacement(
        string subject,
        Match match,
        List<object> replacements)
    {
        System.Text.StringBuilder result = new System.Text.StringBuilder();
        foreach (object replacement in replacements)
        {
            if (replacement is MutableString || replacement is string)
            {
                result.Append(replacement.ToString());
            }
            else if (SchemeConvert.IsNumber(replacement))
            {
                int group = SchemeConvert.ToInt(replacement, "ly:regex-replace");
                if (group >= 0 && group < match.Groups.Count && match.Groups[group].Success)
                {
                    result.Append(match.Groups[group].Value);
                }
            }
            else
            {
                object produced = SchemeUtilities.CallCallback(
                    replacement, MakeRegexMatch(subject, match));
                if (produced is MutableString || produced is string)
                {
                    result.Append(produced.ToString());
                }
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// Wraps a match in the GOOPS <c>&lt;regex-match&gt;</c> object the Scheme accessors
    /// read, carrying the original string and a vector of
    /// <c>(start . end)</c> character positions — one per capturing group, plus group
    /// zero for the whole match, and <see langword="false"/> for a group the match did
    /// not use.
    /// <para>
    /// Upstream converts those positions from BYTES to characters because GLib counts in
    /// bytes; .NET already counts in characters, so the conversion has no analogue here.
    /// </para>
    /// </summary>
    /// <param name="subject">The string that was matched against.</param>
    /// <param name="match">The match.</param>
    /// <returns>The match object, or the raw match when GOOPS is not yet loaded.</returns>
    private static object MakeRegexMatch(string subject, Match match)
    {
        object regexMatchClass = LilyPondScheme.LookupProcedure(RegexMatchClassSymbol);
        object make = LilyPondScheme.LookupProcedure(MakeSymbol);
        if (regexMatchClass == null || !SchemeUtilities.IsProcedure(make))
        {
            // lily-library.scm has not been loaded yet, so nothing can read the slots
            // anyway. Answering the raw match keeps the port's own primitives usable.
            return match;
        }

        object[] positions = new object[match.Groups.Count];
        for (int i = 0; i < match.Groups.Count; i++)
        {
            Group group = match.Groups[i];
            positions[i] = group.Success
                ? new Pair((long)group.Index, (long)(group.Index + group.Length))
                : (object)false;
        }

        return SchemeUtilities.CallCallback(
            make,
            regexMatchClass,
            Keyword.Get("original-string"),
            new MutableString(subject),
            Keyword.Get("substring-positions"),
            positions);
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

    /// <summary>
    /// Returns the container's pure part as something CALLABLE, which is what
    /// <c>ly:unpure-pure-container-pure-part</c> must answer.
    /// <para>
    /// When the pure part was omitted, upstream does not answer the unpure part directly —
    /// it wraps it in an <c>Unpure_pure_call</c>, a procedure that DROPS the second and
    /// third arguments and applies the unpure part to the rest. That wrapper is the whole
    /// reason the type exists: a pure caller passes <c>(grob start end . rest)</c>, and an
    /// unpure procedure invoked with those extra two arguments would be called with the
    /// wrong arity. Answering the unpure part bare would put that error one call further
    /// away from its cause.
    /// </para>
    /// <para>Added by EPG15 (2026-08-08) with the rest of unpure-pure-container.cc.</para>
    /// </summary>
    /// <returns>The pure part, or a call wrapper around the unpure one.</returns>
    public object PurePart() => IsPureOmitted ? new UnpurePureCall(Unpure) : Pure;

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description of the container.</returns>
    public override string ToString() => "#<unpure-pure-container>";
}

/// <summary>
/// Applies an UNPURE procedure in a PURE context by dropping the two column arguments a
/// pure call carries — upstream's <c>Unpure_pure_call</c>.
/// </summary>
/// <remarks>
/// Upstream notes that a smob procedure can take at most three SCM arguments, which is why
/// it checks the argument count itself rather than declaring a <c>3, 0, 1</c> signature.
/// The port has no such limit, so the check is simply that a pure call carries its grob
/// plus the start and end columns.
/// </remarks>
public sealed class UnpurePureCall : IApplicable
{
    private readonly object _unpure;

    /// <summary>Initializes the wrapper around an unpure procedure.</summary>
    /// <param name="unpure">The procedure to call.</param>
    public UnpurePureCall(object unpure) => _unpure = unpure;

    /// <summary>Drops the start and end columns and applies the unpure procedure.</summary>
    /// <param name="arguments">The pure call's arguments.</param>
    /// <returns>The unpure procedure's answer.</returns>
    public object Apply(object[] arguments)
    {
        if (arguments == null || arguments.Length < 3)
        {
            throw SchemeErrors.MiscError(
                "unpure-pure-call",
                "a pure call needs a grob plus its start and end columns; got "
                + (arguments?.Length ?? 0));
        }

        object[] forwarded = new object[arguments.Length - 2];
        forwarded[0] = arguments[0];
        for (int i = 3; i < arguments.Length; i++)
        {
            forwarded[i - 2] = arguments[i];
        }

        return Objects.SchemeUtilities.CallCallback(_unpure, forwarded);
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description of the wrapper.</returns>
    public override string ToString() => "#<unpure-pure-call>";
}
