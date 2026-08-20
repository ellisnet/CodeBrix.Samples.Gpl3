// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Terminal.Engine;
using SilverAssertions;
using Xunit;

namespace Lily.Shell.TerminalView.Tests;

/// <summary>
/// The keyboard-encoding contract Lily.Shell depends on, now provided by the
/// engine's <see cref="TerminalKeyEncoder"/> (the TerminalView add-in routes
/// key events through it).
/// </summary>
public class TerminalKeyEncoderTests
{
    private static string Encode(TerminalKey key, TerminalModifiers modifiers = TerminalModifiers.None) =>
        TerminalKeyEncoder.Encode(key, modifiers);

    [Fact]
    public void letters_are_lowercase_by_default_and_uppercase_with_shift()
    {
        //Assert
        Encode(TerminalKey.A).Should().Be("a");
        Encode(TerminalKey.A, TerminalModifiers.Shift).Should().Be("A");
    }

    [Fact]
    public void caps_lock_inverts_the_shift_case()
    {
        //Assert
        Encode(TerminalKey.A, TerminalModifiers.CapsLock).Should().Be("A");
        Encode(TerminalKey.A, TerminalModifiers.Shift | TerminalModifiers.CapsLock).Should().Be("a");
    }

    [Fact]
    public void ctrl_letters_are_c0_control_codes()
    {
        //Assert
        Encode(TerminalKey.C, TerminalModifiers.Control).Should().Be("\x03");
        Encode(TerminalKey.D, TerminalModifiers.Control).Should().Be("\x04");
        Encode(TerminalKey.L, TerminalModifiers.Control).Should().Be("\x0c");
    }

    [Fact]
    public void digits_shift_to_us_symbols()
    {
        //Assert
        Encode(TerminalKey.D1).Should().Be("1");
        Encode(TerminalKey.D1, TerminalModifiers.Shift).Should().Be("!");
        Encode(TerminalKey.D9, TerminalModifiers.Shift).Should().Be("(");
    }

    [Fact]
    public void editing_keys_encode_vt_sequences()
    {
        //Assert
        Encode(TerminalKey.Enter).Should().Be("\r");
        Encode(TerminalKey.Backspace).Should().Be("\x7f");
        Encode(TerminalKey.Escape).Should().Be("\x1b");
        Encode(TerminalKey.Up).Should().Be("\x1b[A");
        Encode(TerminalKey.Left).Should().Be("\x1b[D");
        Encode(TerminalKey.Home).Should().Be("\x1b[H");
        Encode(TerminalKey.End).Should().Be("\x1b[F");
        Encode(TerminalKey.Delete).Should().Be("\x1b[3~");
        Encode(TerminalKey.PageUp).Should().Be("\x1b[5~");
    }

    [Fact]
    public void application_cursor_mode_switches_arrows_to_ss3()
    {
        //Assert
        TerminalKeyEncoder.Encode(TerminalKey.Up, TerminalModifiers.None,
            applicationCursor: true).Should().Be("\x1bOA");
        TerminalKeyEncoder.Encode(TerminalKey.Left, TerminalModifiers.None,
            applicationCursor: true).Should().Be("\x1bOD");
    }

    [Fact]
    public void shift_tab_is_the_back_tab_sequence()
    {
        //Assert
        Encode(TerminalKey.Tab).Should().Be("\t");
        Encode(TerminalKey.Tab, TerminalModifiers.Shift).Should().Be("\x1b[Z");
    }

    [Fact]
    public void alt_prefixes_escape()
    {
        //Assert
        Encode(TerminalKey.X, TerminalModifiers.Alt).Should().Be("\x1bx");
    }

    [Fact]
    public void us_punctuation_shifts_to_us_symbols()
    {
        //Assert
        Encode(TerminalKey.Semicolon).Should().Be(";");
        Encode(TerminalKey.Semicolon, TerminalModifiers.Shift).Should().Be(":");
        Encode(TerminalKey.Minus, TerminalModifiers.Shift).Should().Be("_");
        Encode(TerminalKey.Backquote).Should().Be("`");
        Encode(TerminalKey.LeftBracket).Should().Be("[");
        Encode(TerminalKey.Quote, TerminalModifiers.Shift).Should().Be("\"");
    }

    [Fact]
    public void unmapped_keys_produce_nothing_and_function_keys_encode()
    {
        //Assert - modifiers are separate state, so None is the "no key" case;
        //  F1-F12 encode now (the old Lily.Shell encoder returned null for them)
        Encode(TerminalKey.None).Should().BeNull();
        Encode(TerminalKey.F1).Should().Be("\x1bOP");
    }

    [Fact]
    public void composed_characters_pass_through_with_ctrl_and_alt_applied()
    {
        //Assert - the path the control uses for layout-composed printables:
        //  shifted digit-row symbols arrive already composed
        TerminalKeyEncoder.EncodeComposed('(', TerminalModifiers.None).Should().Be("(");
        TerminalKeyEncoder.EncodeComposed('c', TerminalModifiers.Control).Should().Be("\x03");
        TerminalKeyEncoder.EncodeComposed('x', TerminalModifiers.Alt).Should().Be("\x1bx");
    }
}
