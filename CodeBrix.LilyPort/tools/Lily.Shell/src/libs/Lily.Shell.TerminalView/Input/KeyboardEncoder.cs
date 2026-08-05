// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Windows.System;

namespace Lily.Shell.TerminalView.Input;

/// <summary>
/// Translates a platform key event into the VT byte sequence a terminal
/// application expects. This is a US-QWERTY mapping maintained by hand
/// because the Skia heads expose no composed-text event (CharacterReceived
/// is unimplemented and KeyRoutedEventArgs.UnicodeKey is internal), and the
/// modifier state must be tracked by the caller from KeyDown/KeyUp for the
/// same reason.
/// </summary>
public static class KeyboardEncoder
{
    /// <summary>
    /// Encodes a key with the current modifier state. Returns null when the
    /// key produces no terminal input (a bare modifier, an unmapped key).
    /// </summary>
    public static string Encode(VirtualKey key, bool shift, bool control, bool alt, bool capsLock)
    {
        var encoded = EncodeCore(key, shift, control, capsLock);
        if (encoded == null) { return null; }

        //Alt prefixes ESC, the classic meta convention
        return alt ? "\x1b" + encoded : encoded;
    }

    /// <summary>
    /// Encodes only the non-printable special keys (Enter, Backspace, Tab,
    /// Escape, arrows, Home/End, Insert/Delete, paging). Returns null for
    /// everything else — printable input should prefer the platform's
    /// composed character (see <see cref="UnicodeKeyReader"/>).
    /// </summary>
    public static string EncodeSpecial(VirtualKey key) => key switch
    {
        VirtualKey.Enter => "\r",
        VirtualKey.Back => "\x7f",
        VirtualKey.Tab => "\t",
        VirtualKey.Escape => "\x1b",
        VirtualKey.Up => "\x1b[A",
        VirtualKey.Down => "\x1b[B",
        VirtualKey.Right => "\x1b[C",
        VirtualKey.Left => "\x1b[D",
        VirtualKey.Home => "\x1b[H",
        VirtualKey.End => "\x1b[F",
        VirtualKey.Insert => "\x1b[2~",
        VirtualKey.Delete => "\x1b[3~",
        VirtualKey.PageUp => "\x1b[5~",
        VirtualKey.PageDown => "\x1b[6~",
        _ => null
    };

    private static string EncodeCore(VirtualKey key, bool shift, bool control, bool capsLock)
    {
        var special = EncodeSpecial(key);
        if (special != null) { return special; }

        //Letters
        if (key >= VirtualKey.A && key <= VirtualKey.Z)
        {
            if (control)
            {
                //Ctrl+A..Ctrl+Z are C0 control codes 1..26
                return ((char)(key - VirtualKey.A + 1)).ToString();
            }

            var upper = shift ^ capsLock;
            var c = (char)('a' + (key - VirtualKey.A));
            return (upper ? char.ToUpperInvariant(c) : c).ToString();
        }

        //Digit row (shifted forms are the US symbols)
        if (key >= VirtualKey.Number0 && key <= VirtualKey.Number9)
        {
            var digit = key - VirtualKey.Number0;
            return shift
                ? ")!@#$%^&*("[digit].ToString()
                : ((char)('0' + digit)).ToString();
        }

        //Numeric keypad
        if (key >= VirtualKey.NumberPad0 && key <= VirtualKey.NumberPad9)
        {
            return ((char)('0' + (key - VirtualKey.NumberPad0))).ToString();
        }

        switch (key)
        {
            case VirtualKey.Space: return control ? "\x00" : " ";
            case VirtualKey.Multiply: return "*";
            case VirtualKey.Add: return "+";
            case VirtualKey.Subtract: return "-";
            case VirtualKey.Decimal: return ".";
            case VirtualKey.Divide: return "/";
        }

        //US OEM punctuation keys arrive as raw VK codes with no VirtualKey names
        return (int)key switch
        {
            186 => shift ? ":" : ";",
            187 => shift ? "+" : "=",
            188 => shift ? "<" : ",",
            189 => shift ? "_" : "-",
            190 => shift ? ">" : ".",
            191 => shift ? "?" : "/",
            192 => shift ? "~" : "`",
            219 => shift ? "{" : "[",
            220 => shift ? "|" : "\\",
            221 => shift ? "}" : "]",
            222 => shift ? "\"" : "'",
            _ => null
        };
    }
}
