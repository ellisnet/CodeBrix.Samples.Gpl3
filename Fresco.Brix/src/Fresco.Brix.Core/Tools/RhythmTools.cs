// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Ly;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Tools; //was previously: frescobaldi/rhythm/rhythm.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The commands that change the durations of the selected music: double and
/// halve, dot and undot, the three kinds of removal, implicit and explicit,
/// and the three that write a rhythm the user supplies.
/// </summary>
/// <remarks>
/// <para>
/// Every one of these needs a SELECTION — a rhythm command over a whole
/// document is not a thing anybody wants — which is why upstream turns them
/// all off when there is none, and why they are listed in
/// <see cref="RhythmActions.SelectionActionNames"/>.
/// </para>
/// <para>
/// The clipboard and the history are upstream's two module-level variables,
/// and they are per-application rather than per-document for the same reason:
/// a rhythm copied in one document is meant to be pasted in another.
/// </para>
/// </remarks>
public static class RhythmTools
{
    /// <summary>What the Apply Rhythm dialog accepts.</summary>
    /// <remarks>Upstream's regexp, verbatim: durations, dots, scalings, and
    /// the three named long notes.</remarks>
    public const string ApplyPattern = @"([0-9./* ]|\\breve|\\longa|\\maxima)+";

    private static readonly List<string> Clipboard = new List<string>();
    private static readonly SortedSet<string> History
        = new SortedSet<string>(StringComparer.Ordinal);

    /// <summary>Gets the rhythm on the clipboard, if any.</summary>
    public static IReadOnlyList<string> CopiedRhythm => Clipboard;

    /// <summary>Gets the rhythms the user has typed before, sorted.</summary>
    public static IReadOnlyList<string> TypedRhythms => History.ToArray();

    /// <summary>Doubles every duration in the selection.</summary>
    /// <param name="cursor">The selection.</param>
    public static void Double(Cursor cursor) => Rhythm.Double(cursor);

    /// <summary>Halves every duration in the selection.</summary>
    /// <param name="cursor">The selection.</param>
    public static void Halve(Cursor cursor) => Rhythm.Halve(cursor);

    /// <summary>Adds a dot to every duration in the selection.</summary>
    /// <param name="cursor">The selection.</param>
    public static void Dot(Cursor cursor) => Rhythm.Dot(cursor);

    /// <summary>Takes a dot off every duration in the selection.</summary>
    /// <param name="cursor">The selection.</param>
    public static void Undot(Cursor cursor) => Rhythm.Undot(cursor);

    /// <summary>Removes every scaling from the selection's durations.</summary>
    /// <param name="cursor">The selection.</param>
    public static void RemoveScaling(Cursor cursor) => Rhythm.RemoveScaling(cursor);

    /// <summary>Removes only the fractional scalings.</summary>
    /// <param name="cursor">The selection.</param>
    public static void RemoveFractionScaling(Cursor cursor)
        => Rhythm.RemoveFractionScaling(cursor);

    /// <summary>Removes every duration from the selection.</summary>
    /// <param name="cursor">The selection.</param>
    public static void Remove(Cursor cursor) => Rhythm.Remove(cursor);

    /// <summary>Removes the durations that repeat the one before them.</summary>
    /// <param name="cursor">The selection.</param>
    public static void Implicit(Cursor cursor) => Rhythm.Implicit(cursor);

    /// <summary>Removes repeated durations, but keeps one on each line.</summary>
    /// <param name="cursor">The selection.</param>
    public static void ImplicitPerLine(Cursor cursor) => Rhythm.ImplicitPerLine(cursor);

    /// <summary>Writes a duration on every note, repeated or not.</summary>
    /// <param name="cursor">The selection.</param>
    public static void Explicit(Cursor cursor) => Rhythm.Explicit(cursor);

    /// <summary>Writes a rhythm the user typed over the selection.</summary>
    /// <param name="cursor">The selection.</param>
    /// <param name="text">The rhythm, as a line of durations.</param>
    /// <remarks>The rhythm is remembered, so the dialog offers it next time.</remarks>
    public static void Apply(Cursor cursor, string text)
    {
        if (cursor == null || string.IsNullOrWhiteSpace(text)) { return; }

        string[] durations = text.Split(
            (char[])null, StringSplitOptions.RemoveEmptyEntries);
        if (durations.Length == 0) { return; }

        lock (History) { History.Add(text.Trim()); }

        Rhythm.Overwrite(cursor, durations);
    }

    /// <summary>Copies the rhythm of the selected music.</summary>
    /// <param name="cursor">The selection.</param>
    public static void Copy(Cursor cursor)
    {
        IReadOnlyList<string> durations = Rhythm.Extract(cursor);
        Clipboard.Clear();
        Clipboard.AddRange(durations);
    }

    /// <summary>Writes the copied rhythm over the selected music.</summary>
    /// <param name="cursor">The selection.</param>
    public static void Paste(Cursor cursor) => Rhythm.Overwrite(cursor, Clipboard);

    /// <summary>Forgets the clipboard and the typed rhythms, for tests.</summary>
    internal static void Reset()
    {
        Clipboard.Clear();
        lock (History) { History.Clear(); }
    }
}

/// <summary>The Rhythm menu's commands.</summary>
public sealed class RhythmActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "rhythm";

    /// <summary>Creates the collection.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public RhythmActions(SettingsStore settings = null)
        : base(CollectionName, settings) => Initialize();

    /// <summary>The commands that need a selection to act on.</summary>
    /// <remarks>Which is all of them — upstream turns the whole collection on
    /// and off together.</remarks>
    public static readonly IReadOnlyList<string> SelectionActionNames = new[]
    {
        "rhythm_double", "rhythm_halve", "rhythm_dot", "rhythm_undot",
        "rhythm_remove_scaling", "rhythm_remove_fraction_scaling", "rhythm_remove",
        "rhythm_implicit", "rhythm_implicit_per_line", "rhythm_explicit",
        "rhythm_apply", "rhythm_copy", "rhythm_paste",
    };

    /// <summary>Gets the commands, by the operation each performs.</summary>
    public IReadOnlyDictionary<string, AppAction> Operations => _operations;

    private readonly Dictionary<string, AppAction> _operations
        = new Dictionary<string, AppAction>(StringComparer.Ordinal);

    /// <inheritdoc/>
    public override string Title => I18n.Get("Rhythm");

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        foreach (var name in SelectionActionNames)
        {
            _operations[name.Substring("rhythm_".Length)] = Add(name);
        }
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        Set("double", I18n.Get("&Double durations"),
            I18n.Get("Double all the durations in the selection."));
        Set("halve", I18n.Get("&Halve durations"),
            I18n.Get("Halve all the durations in the selection."));
        Set("dot", I18n.Get("Do&t durations"),
            I18n.Get("Add a dot to all the durations in the selection."));
        Set("undot", I18n.Get("&Undot durations"),
            I18n.Get("Remove one dot from all the durations in the selection."));
        Set("remove_scaling", I18n.Get("Remove &scaling"),
            I18n.Get("Remove all scaling (*n, *n/m) from the durations in the selection."));
        Set("remove_fraction_scaling", I18n.Get("Remove scaling with &fractions"),
            I18n.Get("Remove only scaling containing fractions (*n/m) "
                + "from the durations in the selection."));
        Set("remove", I18n.Get("&Remove durations"),
            I18n.Get("Remove all durations from the selection."));
        Set("implicit", I18n.Get("Make &implicit"),
            I18n.Get("Make durations implicit (remove repeated durations)."));
        Set("implicit_per_line", I18n.Get("Make implicit (per &line)"),
            I18n.Get("Make durations implicit (remove repeated durations), "
                + "except for the first duration in a line."));
        Set("explicit", I18n.Get("Make &explicit"),
            I18n.Get("Make durations explicit (add duration to every note, "
                + "even if it is the same as the preceding note)."));
        Set("apply", I18n.Get("&Apply rhythm..."),
            I18n.Get("Apply an entered rhythm to the selected music."));
        Set("copy", I18n.Get("&Copy rhythm"),
            I18n.Get("Copy the rhythm of the selected music."));
        Set("paste", I18n.Get("&Paste rhythm"),
            I18n.Get("Paste a rhythm to the selected music."));
    }

    private void Set(string operation, string text, string toolTip)
    {
        AppAction action = _operations[operation];
        action.Text = text;
        action.ToolTip = toolTip;
    }
}
