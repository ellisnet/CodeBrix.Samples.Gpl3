// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap; //was previously: lily/program-option.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>How severe a diagnostic message is.</summary>
public enum MessageSeverity
{
    /// <summary>Detail only wanted when debugging.</summary>
    Debug,

    /// <summary>Progress through a long operation.</summary>
    Progress,

    /// <summary>Ordinary informational output.</summary>
    Message,

    /// <summary>Something is wrong but processing continues.</summary>
    Warning,

    /// <summary>Something is wrong and processing should stop.</summary>
    Error,
}

/// <summary>
/// LilyPond's program-option table, and the diagnostic sink the <c>ly:warning</c> family
/// writes to.
/// <para>
/// The two belong together because the options decide what the sink prints: <c>verbose</c>
/// turns debug output on, and LilyPond's Scheme sets that option while it is loading.
/// </para>
/// <para>
/// The sink here is engine-local plumbing, NOT a second port of <c>flower/warn.cc</c> —
/// that file is carried in full by <c>CodeBrix.LilyPort.Flower</c>'s <c>Warn</c>. This one
/// exists because the <c>ly:message</c> family is bound over program options rather than
/// over Flower's static writer. The upstream bindings themselves
/// (<c>lily/warn-scheme.cc</c>) are registered with the general primitives.
/// </para>
/// </summary>
public sealed class ProgramOptions
{
    private readonly Dictionary<string, object> _values = new Dictionary<string, object>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _documentation = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly List<string> _order = new List<string>();
    private readonly HashSet<string> _accumulative = new HashSet<string>(StringComparer.Ordinal);

    /// <summary>Gets or sets where diagnostics are written. Defaults to discarding them.</summary>
    public TextWriter Output { get; set; } = TextWriter.Null;

    /// <summary>Gets the messages recorded, most recent last.</summary>
    public List<string> Messages { get; } = new List<string>();

    /// <summary>Gets the number of warnings and errors reported.</summary>
    public int WarningCount { get; private set; }

    /// <summary>Declares an option with a default value and documentation.</summary>
    /// <param name="name">The option name.</param>
    /// <param name="value">The default value.</param>
    /// <param name="documentation">The documentation string.</param>
    public void Add(string name, object value, string documentation)
    {
        if (!_values.ContainsKey(name))
        {
            _order.Add(name);
        }

        _values[name] = value;
        _documentation[name] = documentation;
    }

    /// <summary>Sets an option's value.</summary>
    /// <param name="name">The option name.</param>
    /// <param name="value">The new value.</param>
    public void Set(string name, object value)
    {
        if (!_values.ContainsKey(name))
        {
            _order.Add(name);
        }

        _values[name] = value;
    }

    /// <summary>
    /// Marks an option ACCUMULATIVE — one that gathers repeated <c>-d</c> values into a
    /// list rather than letting the last one overwrite the others.
    /// <para>
    /// Upstream records this as a Guile object property on the option's symbol
    /// (<c>program-option-accumulative?</c>), set from <c>ly:add-option</c>'s
    /// <c>#:accumulative?</c> keyword. The port keeps that object property too — the
    /// vendored <c>lily.scm</c> reads it directly to choose between
    /// <c>ly:append-to-option</c> and <c>ly:set-option</c> — and mirrors it here so the
    /// option store itself can answer without a Scheme lookup.
    /// </para>
    /// </summary>
    /// <param name="name">The option name.</param>
    public void MarkAccumulative(string name) => _accumulative.Add(name);

    /// <summary>Returns whether an option accumulates its values.</summary>
    /// <param name="name">The option name.</param>
    /// <returns><see langword="true"/> when the option is accumulative.</returns>
    public bool IsAccumulative(string name) => _accumulative.Contains(name);

    /// <summary>
    /// Prepends a value to an accumulative option — <c>ly:append-to-option</c>.
    /// </summary>
    /// <param name="name">The option name.</param>
    /// <param name="value">The value to add.</param>
    /// <returns><see langword="false"/> when no such option is declared.</returns>
    /// <remarks>
    /// PREPENDS, and that is not a bug in the name: upstream stores an accumulative
    /// option's values in REVERSE for efficiency and reverses them again in
    /// <c>ly:get-option</c>. Appending here instead would read back reversed.
    /// </remarks>
    public bool AppendTo(string name, object value)
    {
        if (!_values.TryGetValue(name, out object current))
        {
            return false;
        }

        // Upstream conses onto whatever the handle holds, without checking that it is a
        // list — so a mis-declared accumulative option builds an improper list there and
        // here alike. Reproduced rather than corrected.
        _values[name] = new Pair(value, current);
        return true;
    }

    /// <summary>
    /// Gets an option's value, or <see langword="false"/> when it was never declared.
    /// <para>
    /// Returning <see langword="false"/> rather than raising matters: LilyPond's Scheme
    /// reads options it has not declared yet and expects a false, and an exception there
    /// would abort a file load for no reason.
    /// </para>
    /// </summary>
    /// <param name="name">The option name.</param>
    /// <returns>The value, or <see langword="false"/>.</returns>
    public object Get(string name)
    {
        if (!_values.TryGetValue(name, out object value))
        {
            return false;
        }

        // An accumulative option is STORED reversed (see AppendTo) and read back in
        // order, which is upstream's own arrangement in ly:get-option.
        return _accumulative.Contains(name) ? Reverse(value) : value;
    }

    /// <summary>Returns a list reversed, leaving a non-list value alone.</summary>
    /// <param name="value">The value to reverse.</param>
    /// <returns>The reversed list.</returns>
    private static object Reverse(object value)
    {
        object result = Nil.Instance;
        object cursor = value;
        while (cursor is Pair pair)
        {
            result = new Pair(pair.Car, result);
            cursor = pair.Cdr;
        }

        // A proper list reverses; anything else (an improper tail, or a non-list value a
        // mis-declared option is holding) is handed back untouched.
        return cursor is Nil ? result : value;
    }

    /// <summary>Returns the options as a Scheme alist of name and value.</summary>
    /// <returns>The alist.</returns>
    public object ToAlist()
    {
        List<object> entries = new List<object>(_order.Count);
        foreach (string name in _order)
        {
            entries.Add(new Pair(Symbol.Intern(name), _values[name]));
        }

        return Pair.ListFrom(entries);
    }

    /// <summary>Gets an option's documentation, or the empty string.</summary>
    /// <param name="name">The option name.</param>
    /// <returns>The documentation string.</returns>
    public string Documentation(string name)
        => _documentation.TryGetValue(name, out string text) ? text : string.Empty;

    /// <summary>Reports a diagnostic.</summary>
    /// <param name="severity">How severe the message is.</param>
    /// <param name="message">The message text.</param>
    public void Report(MessageSeverity severity, string message)
    {
        if (severity >= MessageSeverity.Warning)
        {
            WarningCount++;
        }

        Messages.Add(severity + ": " + message);

        if (severity == MessageSeverity.Debug && !IsTrue(Get("verbose")))
        {
            return;
        }

        Output.WriteLine(message);
    }

    private static bool IsTrue(object value) => !(value is bool boolean) || boolean;
}
