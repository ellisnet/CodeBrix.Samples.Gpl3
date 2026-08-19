// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyScheme.Values;
using SilverAssertions;
using Xunit;

namespace CodeBrix.LilyPort.Engine.Tests;

/// <summary>
/// The <c>include-settings</c> host-side path sanitizing (GO Jeremy 2026-08-18).
/// <para>
/// <c>ly/init.ly</c> splices each <c>include-settings</c> value raw between double
/// quotes into an <c>\include</c> line, so on Windows a native path is misread by the
/// lexer's escape rules — <c>C:\Users</c> dies on <c>\U</c> and <c>C:\temp</c>
/// silently names a different file through <c>\t</c>. The store therefore normalizes
/// that ONE option's separators on Windows, and only there. The expectations here come
/// from the ruling that defines the behaviour, not from the oracle — upstream has the
/// same Windows exposure and no counterpart to read (the R18 precedent for rule 33's
/// "read it off the authority").
/// </para>
/// </summary>
public class ProgramOptionsTests
{
    [Fact]
    public void a_windows_include_settings_path_is_rewritten_to_forward_slashes()
    {
        //Arrange
        // The literal that started the class in CodeBrix.LilyScheme: \U opens a
        // six-hex-digit escape, so a raw splice dies on the 's' of Users.
        MutableString path = new MutableString(@"C:\Users\jerem\settings.ly");

        //Act
        object normalized = ProgramOptions.NormalizeDirectorySeparators(
            path, treatAsWindows: true);

        //Assert
        normalized.Should().BeOfType<MutableString>();
        normalized.ToString().Should().Be("C:/Users/jerem/settings.ly");
    }

    [Fact]
    public void a_silently_misreading_path_is_rewritten_too()
    {
        //Arrange
        // The SILENT half, and the reason this cannot wait for an error report:
        // \t is a valid escape, so C:\temp reads clean as a tab plus "emp".
        //Act
        object normalized = ProgramOptions.NormalizeDirectorySeparators(
            @"C:\temp\alarm\settings.ly", treatAsWindows: true);

        //Assert
        normalized.Should().Be("C:/temp/alarm/settings.ly");
    }

    [Fact]
    public void a_posix_path_passes_through_untouched_on_windows()
    {
        //Arrange
        // The no-op arm answers the SAME instance, which is the cheapest proof that
        // nothing was copied or rewritten.
        MutableString path = new MutableString("/tmp/lilyport/settings.ly");

        //Act
        object normalized = ProgramOptions.NormalizeDirectorySeparators(
            path, treatAsWindows: true);

        //Assert
        ReferenceEquals(normalized, path).Should().BeTrue();
    }

    [Fact]
    public void a_backslash_path_is_left_alone_off_windows()
    {
        //Arrange
        // The CONTROL that must come out differently: on POSIX a backslash is a legal
        // file-name character, so rewriting it would make the option name a DIFFERENT
        // file. This is also why the sanitize is keyed to the host OS.
        MutableString path = new MutableString(@"C:\Users\jerem\settings.ly");

        //Act
        object untouched = ProgramOptions.NormalizeDirectorySeparators(
            path, treatAsWindows: false);

        //Assert
        ReferenceEquals(untouched, path).Should().BeTrue();
    }

    [Fact]
    public void a_non_string_value_passes_through_untouched()
    {
        //Arrange
        // include-settings holds strings, but the store must not corrupt whatever a
        // caller actually passed; only strings are candidates for the rewrite.
        Pair value = new Pair(new MutableString(@"a\b"), Nil.Instance);

        //Act
        object untouched = ProgramOptions.NormalizeDirectorySeparators(
            value, treatAsWindows: true);

        //Assert
        ReferenceEquals(untouched, value).Should().BeTrue();
    }

    [Fact]
    public void only_include_settings_is_sanitized_by_the_store()
    {
        //Arrange
        // The store-level control: another option's string value keeps its backslashes
        // on EVERY host — the sanitize is scoped to the one option whose consumer
        // splices its value into re-lexed source, not to string options in general.
        ProgramOptions options = new ProgramOptions();
        options.Add("paper-size", new MutableString("a4"), "test option");

        //Act
        options.Set("paper-size", new MutableString(@"weird\value"));

        //Assert
        options.Get("paper-size").ToString().Should().Be(@"weird\value");
    }
}
