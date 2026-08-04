// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyScheme.Numeric;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Parsing.Tests;

/// <content>
/// The RAG16 additions. <c>DefaultTremoloType</c> is real state (the actions both read
/// and write it, and the sticky behaviour is the point), and <c>IsScale</c> /
/// <c>ScaleToFactor</c> reproduce the vendored <c>scm/c++.scm</c> definitions over the
/// value model — a non-negative exact rational or a <c>(num . den)</c> pair. The
/// <c>ly:moment?</c> arm of both is deliberately absent and is recorded as such: the
/// scripted host has no moments, and the REAL host answers from the vendored Scheme.
/// </content>
internal sealed partial class ScriptedParserHost
{
    /// <inheritdoc/>
    public int DefaultTremoloType { get; set; } = 8;

    /// <inheritdoc/>
    public bool IsScale(object value)
    {
        if (value is Pair fraction)
        {
            return IsNonNegativeExact(fraction.Car) && IsNonNegativeExact(fraction.Cdr);
        }

        return IsNonNegativeExact(value);
    }

    /// <inheritdoc/>
    public object ScaleToFactor(object value)
        => value is Pair fraction
            ? SchemeNumber.Divide(fraction.Car, fraction.Cdr)
            : value;

    private static bool IsNonNegativeExact(object value)
        => SchemeNumber.IsNumber(value)
           && SchemeNumber.IsExact(value)
           && SchemeNumber.Compare(value, 0L) >= 0;
}
