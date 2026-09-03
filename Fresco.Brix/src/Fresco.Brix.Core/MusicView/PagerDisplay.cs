// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using System.Globalization;
using System.Text;

namespace Fresco.Brix.MusicView; //was previously: qpageview/viewactions.py (class PagerAction) + frescobaldi/pagedview.py:215

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The Music View toolbar's page box: "{num} of {total}", typed into or
/// stepped through.
/// </summary>
/// <remarks>
/// <para>
/// //was previously: a read-only <c>TextBlock</c> on the Music View panel's own
/// toolbar, showing a page count in this application's own words (audit A
/// C.2.6, folded into GAP-26). Upstream's is a spin box whose PREFIX is
/// whatever the display format puts before <c>{num}</c> and whose SUFFIX is
/// whatever follows it with <c>{total}</c> substituted, so the number itself is
/// the editable part — <c>PagerAction._adjustSpinBox</c>.
/// </para>
/// <para>
/// The format string is Frescobaldi's own msgid, set in
/// <c>pagedview.ViewActions.translateUI</c>: <c>_("{num} of {total}")</c>. It
/// is a msgid rather than a literal because languages order the two differently.
/// </para>
/// <para>
/// When there are no pages, upstream sets the box's range to 0..0 and its
/// special value text to a single space, so it reads as empty and cannot be
/// driven. <see cref="Format"/> answers the empty string for that case and the
/// box is disabled.
/// </para>
/// </remarks>
public static class PagerDisplay
{
    /// <summary>The place in the format string the page number goes.</summary>
    public const string NumberField = "{num}";

    /// <summary>The place in the format string the page count goes.</summary>
    public const string TotalField = "{total}";

    /// <summary>Answers the format string, translated.</summary>
    /// <returns>The format.</returns>
    public static string DisplayFormat() => I18n.Get("{num} of {total}");

    /// <summary>Answers what the box shows.</summary>
    /// <param name="number">The current page, one-based.</param>
    /// <param name="total">How many pages there are.</param>
    /// <returns>The text, or the empty string when there are no pages.</returns>
    public static string Format(int number, int total)
        => Format(DisplayFormat(), number, total);

    /// <summary>Answers what the box shows, under a given format.</summary>
    /// <param name="format">The format string.</param>
    /// <param name="number">The current page, one-based.</param>
    /// <param name="total">How many pages there are.</param>
    /// <returns>The text, or the empty string when there are no pages.</returns>
    public static string Format(string format, int number, int total)
    {
        if (total <= 0 || number <= 0) { return string.Empty; }

        return Substitute(format ?? string.Empty, number, total);
    }

    /// <summary>Answers the page number a typed line asks for.</summary>
    /// <param name="text">What the user left in the box.</param>
    /// <param name="total">How many pages there are.</param>
    /// <returns>The page, one-based and within range, or 0 for none.</returns>
    /// <remarks>
    /// Qt's spin box only ever hands its validator the number, because the
    /// prefix and the suffix are chrome it draws itself. Here the whole line is
    /// a text box, so the first run of digits in it is the answer — which is
    /// what a user typing over "3 of 12" produces either way.
    /// </remarks>
    public static int Parse(string text, int total)
    {
        if (string.IsNullOrEmpty(text) || total <= 0) { return 0; }

        StringBuilder digits = new StringBuilder();
        foreach (char letter in text)
        {
            if (letter >= '0' && letter <= '9')
            {
                digits.Append(letter);
            }
            else if (digits.Length > 0)
            {
                break;
            }
        }

        if (digits.Length == 0
            || !int.TryParse(
                digits.ToString(), NumberStyles.None,
                CultureInfo.InvariantCulture, out int number))
        {
            return 0;
        }

        return number < 1 ? 1 : number > total ? total : number;
    }

    private static string Substitute(string format, int number, int total)
        => format
            .Replace(
                NumberField,
                number.ToString(CultureInfo.CurrentCulture))
            .Replace(
                TotalField,
                total.ToString(CultureInfo.CurrentCulture));
}
