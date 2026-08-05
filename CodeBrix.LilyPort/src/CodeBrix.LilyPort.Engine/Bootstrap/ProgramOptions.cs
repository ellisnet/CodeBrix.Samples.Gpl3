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

// Modified by Jeremy Ellis on 2026-08-02 as part of the CodeBrix port.

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
/// (<c>lily/warn-scheme.cc</c>) are still owed, and the ledger routes them to EPG23.
/// </para>
/// </summary>
public sealed class ProgramOptions
{
    private readonly Dictionary<string, object> _values = new Dictionary<string, object>(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _documentation = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly List<string> _order = new List<string>();

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
        => _values.TryGetValue(name, out object value) ? value : false;

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
