// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Windows.System;

namespace Fresco.Brix.Commands; //was previously: PyQt6 QKeySequence, as Frescobaldi uses it

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One keyboard shortcut: a key with its modifiers.
/// <para>
/// Qt's <c>QKeySequence</c> can hold up to four chords; Frescobaldi never
/// uses more than one, and where it wants alternatives it hands the action a
/// LIST of sequences. This type is therefore one chord, and actions carry a
/// list — which is exactly how the shortcuts read in the upstream source.
/// </para>
/// </summary>
public sealed class KeySequence : IEquatable<KeySequence>
{
    private static readonly (VirtualKeyModifiers Modifier, string Name)[] ModifierNames =
    {
        //Order matters: this is the order shortcuts are written in.
        (VirtualKeyModifiers.Control, "Ctrl"),
        (VirtualKeyModifiers.Shift, "Shift"),
        (VirtualKeyModifiers.Menu, "Alt"),
        (VirtualKeyModifiers.Windows, "Meta"),
    };

    private static readonly Dictionary<string, VirtualKey> KeyNames = BuildKeyNames();

    /// <summary>Creates a shortcut.</summary>
    /// <param name="key">The key.</param>
    /// <param name="modifiers">The modifiers held with it.</param>
    public KeySequence(VirtualKey key, VirtualKeyModifiers modifiers = VirtualKeyModifiers.None)
    {
        Key = key;
        Modifiers = modifiers;
    }

    /// <summary>Gets the key.</summary>
    public VirtualKey Key { get; }

    /// <summary>Gets the modifiers.</summary>
    public VirtualKeyModifiers Modifiers { get; }

    /// <summary>
    /// Parses a shortcut written the way Qt writes it, e.g. <c>Ctrl+Shift+F</c>
    /// or <c>F11</c>. Case-insensitive; <c>Control</c>/<c>Ctrl</c> and
    /// <c>Alt</c>/<c>Meta</c> are accepted.
    /// </summary>
    /// <param name="text">The shortcut text.</param>
    /// <returns>The shortcut, or null when the text names no known key.</returns>
    public static KeySequence Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) { return null; }

        VirtualKeyModifiers modifiers = VirtualKeyModifiers.None;
        string[] parts = text.Split('+');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].Trim().ToLowerInvariant())
            {
                case "ctrl":
                case "control": modifiers |= VirtualKeyModifiers.Control; break;
                case "shift": modifiers |= VirtualKeyModifiers.Shift; break;
                case "alt": modifiers |= VirtualKeyModifiers.Menu; break;
                case "meta":
                case "win": modifiers |= VirtualKeyModifiers.Windows; break;
                default: return null;
            }
        }

        string keyText = parts[parts.Length - 1].Trim();
        if (KeyNames.TryGetValue(keyText.ToLowerInvariant(), out var key))
        {
            return new KeySequence(key, modifiers);
        }

        //A shifted character stands for its unshifted key plus Shift.
        return ShiftedKeyNames.TryGetValue(keyText, out var shifted)
            ? new KeySequence(shifted, modifiers | VirtualKeyModifiers.Shift)
            : null;
    }

    /// <summary>Writes the shortcut the way Qt writes it.</summary>
    /// <returns>The shortcut text.</returns>
    public override string ToString()
    {
        StringBuilder text = new StringBuilder();
        foreach (var (modifier, name) in ModifierNames)
        {
            if ((Modifiers & modifier) == modifier)
            {
                text.Append(name).Append('+');
            }
        }

        return text.Append(KeyName(Key)).ToString();
    }

    /// <inheritdoc/>
    public bool Equals(KeySequence other)
        => other != null && other.Key == Key && other.Modifiers == Modifiers;

    /// <inheritdoc/>
    public override bool Equals(object obj) => Equals(obj as KeySequence);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Key, Modifiers);

    /// <summary>The display name for a key.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The name.</returns>
    private static string KeyName(VirtualKey key)
    {
        //Letters and digits print as themselves; the rest use the enum name,
        //with the punctuation keys spelled the way Qt spells them.
        if (key >= VirtualKey.A && key <= VirtualKey.Z)
        {
            return ((char)key).ToString();
        }

        if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
        {
            return ((char)('0' + (key - VirtualKey.Number0)))
                .ToString(CultureInfo.InvariantCulture);
        }

        return key switch
        {
            (VirtualKey)188 => ",",
            (VirtualKey)190 => ".",
            (VirtualKey)191 => "/",
            (VirtualKey)186 => ";",
            (VirtualKey)187 => "=",
            (VirtualKey)189 => "-",
            (VirtualKey)219 => "[",
            (VirtualKey)221 => "]",
            (VirtualKey)220 => "\\",
            (VirtualKey)192 => "`",
            (VirtualKey)222 => "'",
            _ => key.ToString(),
        };
    }

    private static Dictionary<string, VirtualKey> BuildKeyNames()
    {
        Dictionary<string, VirtualKey> names
            = new Dictionary<string, VirtualKey>(StringComparer.Ordinal);

        foreach (VirtualKey key in Enum.GetValues<VirtualKey>())
        {
            names[key.ToString().ToLowerInvariant()] = key;
        }

        for (char c = 'a'; c <= 'z'; c++)
        {
            names[c.ToString()] = (VirtualKey)char.ToUpperInvariant(c);
        }

        for (char c = '0'; c <= '9'; c++)
        {
            names[c.ToString()] = VirtualKey.Number0 + (c - '0');
        }

        names[","] = (VirtualKey)188;
        names["."] = (VirtualKey)190;
        names["/"] = (VirtualKey)191;
        names[";"] = (VirtualKey)186;
        names["="] = (VirtualKey)187;
        names["-"] = (VirtualKey)189;
        names["["] = (VirtualKey)219;
        names["]"] = (VirtualKey)221;
        names["\\"] = (VirtualKey)220;
        names["`"] = (VirtualKey)192;
        names["'"] = (VirtualKey)222;

        //Qt's own spellings for the keys the platform's enum names
        //differently. The shortcut strings in this port are Frescobaldi's
        //verbatim, so they are Qt's spellings; without these a shortcut such
        //as Alt+Backspace parses to NOTHING and the command silently loses
        //its key.
        //was previously: only the platform's enum names, which meant
        //Alt+Backspace, Alt+Return and Ctrl+Shift+Return never bound.
        //Written out here rather than held in a static field, because this
        //method IS a static field's initializer and a field declared below it
        //would still be null when it ran.
        names["backspace"] = VirtualKey.Back;
        names["return"] = VirtualKey.Enter;
        names["esc"] = VirtualKey.Escape;
        names["del"] = VirtualKey.Delete;
        names["ins"] = VirtualKey.Insert;
        names["pgup"] = VirtualKey.PageUp;
        names["pgdown"] = VirtualKey.PageDown;
        names["pgdn"] = VirtualKey.PageDown;
        return names;
    }

    /// <summary>
    /// The shifted characters Qt writes literally, and the key plus Shift
    /// that produces them.
    /// </summary>
    /// <remarks>Which character a shifted key produces is a KEYBOARD LAYOUT
    /// question, and Qt's own shortcut strings ignore that too; these are the
    /// US layout, which is what upstream's defaults assume.</remarks>
    private static readonly Dictionary<string, VirtualKey> ShiftedKeyNames
        = new Dictionary<string, VirtualKey>(StringComparer.Ordinal)
        {
            ["("] = VirtualKey.Number9,
            [")"] = VirtualKey.Number0,
            ["\""] = (VirtualKey)222,
            ["<"] = (VirtualKey)188,
            [">"] = (VirtualKey)190,
            ["?"] = (VirtualKey)191,
            [":"] = (VirtualKey)186,
            ["+"] = (VirtualKey)187,
            ["_"] = (VirtualKey)189,
            ["{"] = (VirtualKey)219,
            ["}"] = (VirtualKey)221,
            ["|"] = (VirtualKey)220,
            ["~"] = (VirtualKey)192,
            ["!"] = VirtualKey.Number1,
            ["@"] = VirtualKey.Number2,
            ["#"] = VirtualKey.Number3,
            ["$"] = VirtualKey.Number4,
            ["%"] = VirtualKey.Number5,
            ["^"] = VirtualKey.Number6,
            ["&"] = VirtualKey.Number7,
            ["*"] = VirtualKey.Number8,
        };
}

/// <summary>
/// The shortcut lists Qt's <c>QKeySequence.StandardKey</c> resolves to, as
/// Frescobaldi's actions ask for them.
/// </summary>
/// <remarks>
/// The values are Qt's X11/GNOME bindings — X11 is the interactively verified
/// head (FR10). Two deliberate omissions: Qt's X11 <c>NextChild</c> and
/// <c>PreviousChild</c> also carry <c>Ctrl+,</c> and <c>Ctrl+.</c>, and
/// upstream immediately strips both with <c>qutil.removeShortcut</c> (they
/// collide with Preferences), so the port simply never adds them.
/// </remarks>
public static class StandardKeys
{
    /// <summary>File &gt; New.</summary>
    public static IReadOnlyList<KeySequence> New { get; } = Keys("Ctrl+N");

    /// <summary>File &gt; Open.</summary>
    public static IReadOnlyList<KeySequence> Open { get; } = Keys("Ctrl+O");

    /// <summary>File &gt; Save.</summary>
    public static IReadOnlyList<KeySequence> Save { get; } = Keys("Ctrl+S");

    /// <summary>File &gt; Save As.</summary>
    public static IReadOnlyList<KeySequence> SaveAs { get; } = Keys("Ctrl+Shift+S");

    /// <summary>File &gt; Close.</summary>
    public static IReadOnlyList<KeySequence> Close { get; } = Keys("Ctrl+W");

    /// <summary>File &gt; Quit.</summary>
    public static IReadOnlyList<KeySequence> Quit { get; } = Keys("Ctrl+Q");

    /// <summary>Edit &gt; Undo.</summary>
    public static IReadOnlyList<KeySequence> Undo { get; } = Keys("Ctrl+Z");

    /// <summary>Edit &gt; Redo.</summary>
    public static IReadOnlyList<KeySequence> Redo { get; } = Keys("Ctrl+Shift+Z", "Ctrl+Y");

    /// <summary>Edit &gt; Cut.</summary>
    public static IReadOnlyList<KeySequence> Cut { get; } = Keys("Ctrl+X");

    /// <summary>Edit &gt; Copy.</summary>
    public static IReadOnlyList<KeySequence> Copy { get; } = Keys("Ctrl+C");

    /// <summary>Edit &gt; Paste.</summary>
    public static IReadOnlyList<KeySequence> Paste { get; } = Keys("Ctrl+V");

    /// <summary>Edit &gt; Select All.</summary>
    public static IReadOnlyList<KeySequence> SelectAll { get; } = Keys("Ctrl+A");

    /// <summary>Edit &gt; Find.</summary>
    public static IReadOnlyList<KeySequence> Find { get; } = Keys("Ctrl+F");

    /// <summary>Edit &gt; Find Next.</summary>
    public static IReadOnlyList<KeySequence> FindNext { get; } = Keys("F3", "Ctrl+G");

    /// <summary>Edit &gt; Find Previous.</summary>
    public static IReadOnlyList<KeySequence> FindPrevious { get; }
        = Keys("Shift+F3", "Ctrl+Shift+G");

    /// <summary>Edit &gt; Replace.</summary>
    public static IReadOnlyList<KeySequence> Replace { get; } = Keys("Ctrl+R");

    /// <summary>View &gt; Next Document.</summary>
    public static IReadOnlyList<KeySequence> Forward { get; } = Keys("Alt+Right");

    /// <summary>View &gt; Previous Document.</summary>
    public static IReadOnlyList<KeySequence> Back { get; } = Keys("Alt+Left");

    /// <summary>Help &gt; User Guide.</summary>
    public static IReadOnlyList<KeySequence> HelpContents { get; } = Keys("F1");

    /// <summary>Window &gt; Next View.</summary>
    public static IReadOnlyList<KeySequence> NextChild { get; } = Keys("Ctrl+Tab");

    /// <summary>Window &gt; Previous View.</summary>
    public static IReadOnlyList<KeySequence> PreviousChild { get; }
        = Keys("Ctrl+Shift+Tab");

    private static IReadOnlyList<KeySequence> Keys(params string[] shortcuts)
        => shortcuts.Select(KeySequence.Parse).Where(k => k != null).ToArray();
}
