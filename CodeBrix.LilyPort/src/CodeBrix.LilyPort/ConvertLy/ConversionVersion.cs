// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Globalization;

namespace CodeBrix.LilyPort.ConvertLy; //was previously: convert-ly.py's version tuples;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// A LilyPond version as the conversion rules compare them: a three-part tuple, ordered
/// the way python orders tuples.
/// </summary>
/// <remarks>
/// <c>convert-ly.py</c> keeps these as plain python tuples and relies on tuple
/// comparison for the whole rule-selection rule
/// (<c>from_version &lt; conv[0] &lt;= to_version</c>), so the ordering here is the
/// feature, not a convenience.
/// </remarks>
public readonly struct ConversionVersion : IComparable<ConversionVersion>,
    IEquatable<ConversionVersion>
{
    /// <summary>Initializes a version.</summary>
    /// <param name="major">The major number.</param>
    /// <param name="minor">The minor number.</param>
    /// <param name="patch">The patch number.</param>
    public ConversionVersion(int major, int minor, int patch)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
    }

    /// <summary>Gets the major number.</summary>
    public int Major { get; }

    /// <summary>Gets the minor number.</summary>
    public int Minor { get; }

    /// <summary>Gets the patch number.</summary>
    public int Patch { get; }

    /// <summary>Gets whether the minor number is odd — an unstable release series.</summary>
    public bool IsUnstable => (Minor % 2) != 0;

    /// <summary>
    /// Reads a dotted version, accepting the two- and three-part spellings a
    /// <c>\version</c> line may carry.
    /// </summary>
    /// <param name="text">The text, e.g. <c>2.14.2</c>.</param>
    /// <param name="version">The version read.</param>
    /// <returns>Whether the text was a version.</returns>
    public static bool TryParse(string text, out ConversionVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Trim().Split('.');
        if (parts.Length < 1 || parts.Length > 3)
        {
            return false;
        }

        int[] numbers = new int[3];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(
                parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out numbers[i]))
            {
                return false;
            }
        }

        version = new ConversionVersion(numbers[0], numbers[1], numbers[2]);
        return true;
    }

    /// <summary>Compares two versions part by part.</summary>
    /// <param name="other">The version to compare with.</param>
    /// <returns>The comparison.</returns>
    public int CompareTo(ConversionVersion other)
    {
        int result = Major.CompareTo(other.Major);
        if (result != 0) { return result; }

        result = Minor.CompareTo(other.Minor);
        return result != 0 ? result : Patch.CompareTo(other.Patch);
    }

    /// <summary>Answers whether two versions are the same.</summary>
    /// <param name="other">The version to compare with.</param>
    /// <returns>Whether they are equal.</returns>
    public bool Equals(ConversionVersion other) => CompareTo(other) == 0;

    /// <inheritdoc/>
    public override bool Equals(object obj)
        => obj is ConversionVersion other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => (Major, Minor, Patch).GetHashCode();

    /// <summary>The dotted spelling, which is what a <c>\version</c> line carries.</summary>
    /// <returns>The text.</returns>
    public override string ToString()
        => string.Format(
            CultureInfo.InvariantCulture, "{0}.{1}.{2}", Major, Minor, Patch);

    /// <summary>Answers whether one version is before another.</summary>
    /// <param name="left">The left version.</param>
    /// <param name="right">The right version.</param>
    /// <returns>Whether left is before right.</returns>
    public static bool operator <(ConversionVersion left, ConversionVersion right)
        => left.CompareTo(right) < 0;

    /// <summary>Answers whether one version is after another.</summary>
    /// <param name="left">The left version.</param>
    /// <param name="right">The right version.</param>
    /// <returns>Whether left is after right.</returns>
    public static bool operator >(ConversionVersion left, ConversionVersion right)
        => left.CompareTo(right) > 0;

    /// <summary>Answers whether one version is at or before another.</summary>
    /// <param name="left">The left version.</param>
    /// <param name="right">The right version.</param>
    /// <returns>Whether left is at or before right.</returns>
    public static bool operator <=(ConversionVersion left, ConversionVersion right)
        => left.CompareTo(right) <= 0;

    /// <summary>Answers whether one version is at or after another.</summary>
    /// <param name="left">The left version.</param>
    /// <param name="right">The right version.</param>
    /// <returns>Whether left is at or after right.</returns>
    public static bool operator >=(ConversionVersion left, ConversionVersion right)
        => left.CompareTo(right) >= 0;

    /// <summary>Answers whether two versions are the same.</summary>
    /// <param name="left">The left version.</param>
    /// <param name="right">The right version.</param>
    /// <returns>Whether they are equal.</returns>
    public static bool operator ==(ConversionVersion left, ConversionVersion right)
        => left.Equals(right);

    /// <summary>Answers whether two versions differ.</summary>
    /// <param name="left">The left version.</param>
    /// <param name="right">The right version.</param>
    /// <returns>Whether they differ.</returns>
    public static bool operator !=(ConversionVersion left, ConversionVersion right)
        => !left.Equals(right);
}

/// <summary>
/// One conversion rule: the version it brings a document UP TO, the one-line
/// description <c>convert-ly --show-rules</c> prints, and the transformation itself.
/// </summary>
public sealed class ConversionRule
{
    /// <summary>Initializes a rule.</summary>
    /// <param name="version">The version the rule converts to.</param>
    /// <param name="message">The description of what it does.</param>
    /// <param name="convert">The transformation.</param>
    public ConversionRule(
        ConversionVersion version, string message, Func<string, string> convert)
    {
        Version = version;
        Message = message;
        Convert = convert;
    }

    /// <summary>Gets the version this rule converts to.</summary>
    public ConversionVersion Version { get; }

    /// <summary>Gets the description of what the rule does.</summary>
    public string Message { get; }

    /// <summary>Gets the transformation.</summary>
    public Func<string, string> Convert { get; }
}

/// <summary>
/// Thrown by a rule that has found something it cannot convert and must not guess at.
/// The driver stops at the last rule that succeeded, exactly as upstream does.
/// </summary>
public sealed class FatalConversionError : Exception
{
    /// <summary>Initializes the error.</summary>
    public FatalConversionError()
        : base("The document could not be converted any further.")
    {
    }

    /// <summary>Initializes the error with a message.</summary>
    /// <param name="message">The message.</param>
    public FatalConversionError(string message)
        : base(message)
    {
    }

    /// <summary>Initializes the error with a message and a cause.</summary>
    /// <param name="message">The message.</param>
    /// <param name="innerException">The cause.</param>
    public FatalConversionError(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
