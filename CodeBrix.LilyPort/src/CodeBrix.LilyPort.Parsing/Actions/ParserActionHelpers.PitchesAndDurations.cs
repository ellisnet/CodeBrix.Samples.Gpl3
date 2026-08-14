// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace CodeBrix.LilyPort.Parsing.Actions;

/// <content>
/// The one value-model question the PitchesAndDurations group asks repeatedly. There are no
/// new epilogue helpers in this group: its duration bodies REUSE
/// <see cref="MakeDuration"/> and <see cref="MakeChordElements"/>, which the MusicAssembly group ported
/// whole and whose defaulted C++ parameters were made explicit for exactly these
/// callers.
/// </content>
internal static partial class ParserActionHelpers
{
    /// <summary>
    /// Answers <c>scm_is_eq (SCM_INUM0, value)</c> — is this value the fixnum zero?
    /// <para>
    /// The quotes rules use it as a three-way test, not a numeric one: zero means "no
    /// octave marks were written", and it has to be told apart from both a written
    /// count and a non-number. Every producer of the value is a fixnum
    /// (<c>SCM_INUM0</c>, <c>to_scm (1)</c>, <c>to_scm (-1)</c> and
    /// <c>scm_oneplus</c>/<c>scm_oneminus</c> over them), so identity with the fixnum
    /// zero is exactly "is the exact integer 0" on the port's value model.
    /// </para>
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> for the fixnum zero.</returns>
    internal static bool IsExactZero(object value) => value is long number && number == 0L;
}
