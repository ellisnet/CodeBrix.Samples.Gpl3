// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// The text <c>ly:usage</c> prints, and the same text Lily.Shell's <c>usage</c> command
/// prints — ONE string, so the two can never drift apart.
/// <para>
/// Upstream's <c>ly:usage</c> (<c>lily/main.cc</c>) prints the <c>lilypond</c> binary's
/// command-line help: a usage line, what the program does, the project URL, the option
/// table, and where to report bugs. The SHAPE is reproduced section for section.
/// </para>
/// <para>
/// ⚠ The CONTENT names Lily.Shell rather than <c>lilypond</c>, and that is a deliberate
/// divergence recorded in PORT-COVERAGE. D14 makes Lily.Shell the port's public command
/// line and the batch runner internal-only, so there is no <c>lilypond</c> binary here
/// whose options could be listed. Printing upstream's table verbatim would document
/// flags that do not exist — a usage message is not an algorithm to be reproduced
/// faithfully, it is a statement about THIS program, and a false one is worse than none.
/// </para>
/// </summary>
public static class UsageText
{
    /// <summary>Gets the usage message.</summary>
    public static string Text =>
        "Usage: lily-shell [COMMAND]...\n"
        + "\n"
        + "Typeset music and/or produce MIDI from a LilyPond source file.\n"
        + "\n"
        + "LilyPond produces beautiful music notation.\n"
        + "For more information, see https://lilypond.org\n"
        + "\n"
        + "Commands:\n"
        + "  engrave <file.ly>    Engrave a file, writing SVG and MIDI beside it.\n"
        + "  help [<command>]     List the commands, or describe one of them.\n"
        + "  usage                Print this message.\n"
        + "\n"
        + "Type 'help <command>' for the full option list of a single command.\n"
        + "\n"
        + "CodeBrix.LilyPort is a port of LilyPond to .NET; it is not the lilypond\n"
        + "program and does not take lilypond's command-line options.\n";
}
