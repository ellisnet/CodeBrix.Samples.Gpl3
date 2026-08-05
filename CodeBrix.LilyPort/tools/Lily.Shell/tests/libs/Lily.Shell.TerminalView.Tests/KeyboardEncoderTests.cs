// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Lily.Shell.TerminalView.Input;
using SilverAssertions;
using Windows.System;
using Xunit;

namespace Lily.Shell.TerminalView.Tests;

public class KeyboardEncoderTests
{
    private static string Encode(VirtualKey key, bool shift = false, bool control = false,
        bool alt = false, bool capsLock = false) =>
        KeyboardEncoder.Encode(key, shift, control, alt, capsLock);

    [Fact]
    public void letters_are_lowercase_by_default_and_uppercase_with_shift()
    {
        //Assert
        Encode(VirtualKey.A).Should().Be("a");
        Encode(VirtualKey.A, shift: true).Should().Be("A");
    }

    [Fact]
    public void caps_lock_inverts_the_shift_case()
    {
        //Assert
        Encode(VirtualKey.A, capsLock: true).Should().Be("A");
        Encode(VirtualKey.A, shift: true, capsLock: true).Should().Be("a");
    }

    [Fact]
    public void ctrl_letters_are_c0_control_codes()
    {
        //Assert
        Encode(VirtualKey.C, control: true).Should().Be("\x03");
        Encode(VirtualKey.D, control: true).Should().Be("\x04");
        Encode(VirtualKey.L, control: true).Should().Be("\x0c");
    }

    [Fact]
    public void digits_shift_to_us_symbols()
    {
        //Assert
        Encode(VirtualKey.Number1).Should().Be("1");
        Encode(VirtualKey.Number1, shift: true).Should().Be("!");
        Encode(VirtualKey.Number9, shift: true).Should().Be("(");
    }

    [Fact]
    public void editing_keys_encode_vt_sequences()
    {
        //Assert
        Encode(VirtualKey.Enter).Should().Be("\r");
        Encode(VirtualKey.Back).Should().Be("\x7f");
        Encode(VirtualKey.Escape).Should().Be("\x1b");
        Encode(VirtualKey.Up).Should().Be("\x1b[A");
        Encode(VirtualKey.Left).Should().Be("\x1b[D");
        Encode(VirtualKey.Home).Should().Be("\x1b[H");
        Encode(VirtualKey.End).Should().Be("\x1b[F");
        Encode(VirtualKey.Delete).Should().Be("\x1b[3~");
        Encode(VirtualKey.PageUp).Should().Be("\x1b[5~");
    }

    [Fact]
    public void alt_prefixes_escape()
    {
        //Assert
        Encode(VirtualKey.X, alt: true).Should().Be("\x1bx");
    }

    [Fact]
    public void us_oem_punctuation_maps_by_raw_code()
    {
        //Assert
        Encode((VirtualKey)186).Should().Be(";");
        Encode((VirtualKey)186, shift: true).Should().Be(":");
        Encode((VirtualKey)189, shift: true).Should().Be("_");
        Encode((VirtualKey)192).Should().Be("`");
        Encode((VirtualKey)219).Should().Be("[");
        Encode((VirtualKey)222, shift: true).Should().Be("\"");
    }

    [Fact]
    public void bare_modifiers_produce_nothing()
    {
        //Assert
        Encode(VirtualKey.Shift).Should().BeNull();
        Encode(VirtualKey.Control).Should().BeNull();
        Encode(VirtualKey.F1).Should().BeNull();
    }
}
