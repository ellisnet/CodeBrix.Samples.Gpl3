/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2003--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

  LilyPond is free software: you can redistribute it and/or modify
  it under the terms of the GNU General Public License as published by
  the Free Software Foundation, either version 3 of the License, or
  (at your option) any later version.

  LilyPond is distributed in the hope that it will be useful,
  but WITHOUT ANY WARRANTY; without even the implied warranty of
  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
  GNU General Public License for more details.

  You should have received a copy of the GNU General Public License
  along with LilyPond.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.Text;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/gregorian-ligature.cc, lily/include/gregorian-ligature.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - upstream's bit masks are preprocessor #defines in the header, which C# has no
//     equivalent for; they are public consts here, renamed from MACRO_CASE to PascalCase.
//     The VALUES are upstream's exactly, and they are load-bearing: `prefix-set` and
//     `context-info` are grob PROPERTIES, so a wrong bit is visible to Scheme.
//   - upstream's free function check_prefix has no header declaration and no caller
//     outside this file, so it is a private static here.

/// <summary>
/// The style-independent half of a Gregorian ligature: the head prefixes a user writes
/// (<c>\virga</c>, <c>\inclinatum</c>, …) and the context information the engraver
/// derives from them.
/// </summary>
/// <remarks>
/// <para>
/// Upstream keeps this as a nearly-empty class whose real content is the two families of
/// bit masks in <c>gregorian-ligature.hh</c>. The first family is written straight from
/// user input; the second is DERIVED by
/// <see cref="Translation.GregorianLigatureEngraver"/> from a head and its two
/// neighbours, and a concrete style — currently only Vaticana — reads both to choose
/// glyphs.
/// </para>
/// <para>
/// <c>prefix-set</c> and <c>context-info</c> are ordinary grob properties holding these
/// masks as integers, which is why the values may not be renumbered.
/// </para>
/// </remarks>
public static class GregorianLigature
{
    private static readonly Symbol PrefixSetSymbol = Symbol.Intern("prefix-set");

    // ----- head prefixes -----
    //
    // Attributes immediately derived from user input (e.g. by the user setting a
    // gregorian ligature grob property or using the "\~" keyword). If the according bit
    // of the head prefix value is set, the attribute applies for this head. The binary
    // operator "\~" is treated like a prefix for the head that follows the operator, but
    // does not affect the head that precedes the operator, if any.

    /// <summary>Attribute <c>\virga</c>.</summary>
    public const int Virga = 0x0001;

    /// <summary>Attribute <c>\stropha</c>.</summary>
    public const int Stropha = 0x0002;

    /// <summary>Attribute <c>\inclinatum</c>.</summary>
    public const int Inclinatum = 0x0004;

    /// <summary>Attribute <c>\auctum</c>.</summary>
    public const int Auctum = 0x0008;

    /// <summary>Attribute <c>\descendens</c>.</summary>
    public const int Descendens = 0x0010;

    /// <summary>Attribute <c>\ascendens</c>.</summary>
    public const int Ascendens = 0x0020;

    /// <summary>Attribute <c>\oriscus</c>.</summary>
    public const int Oriscus = 0x0040;

    /// <summary>Attribute <c>\quilisma</c>.</summary>
    public const int Quilisma = 0x0080;

    /// <summary>Attribute <c>\deminutum</c>.</summary>
    public const int Deminutum = 0x0100;

    /// <summary>Attribute <c>\cavum</c>.</summary>
    public const int Cavum = 0x0200;

    /// <summary>Attribute <c>\linea</c>.</summary>
    public const int Linea = 0x0400;

    /// <summary>Operator <c>\~</c>.</summary>
    public const int PesOrFlexa = 0x0800;

    // ----- ligature context info -----
    //
    // These attributes are derived from the head prefixes by considering the current and
    // the two neighbouring heads. The definitions may be extended by more specific
    // Gregorian ligatures; VaticanaLigature adds STACKED_HEAD.

    /// <summary>This is a head before <c>\~</c> in an ascending melody.</summary>
    public const int PesLower = 0x0001;

    /// <summary>This is a head after <c>\~</c> in an ascending melody.</summary>
    public const int PesUpper = 0x0002;

    /// <summary>This is a head before <c>\~</c> in a descending melody.</summary>
    public const int FlexaLeft = 0x0004;

    /// <summary>This is a head after <c>\~</c> in a descending melody.</summary>
    public const int FlexaRight = 0x0008;

    /// <summary>The previous head was a deminutum.</summary>
    public const int AfterDeminutum = 0x0020;

    /// <summary>
    /// Names the prefixes a head carries, for the warning a style issues when it cannot
    /// honour them.
    /// </summary>
    /// <param name="primitive">The ligature head.</param>
    /// <returns>The prefix names, comma-separated, or the empty string.</returns>
    public static string PrefixesToStr(Grob primitive)
    {
        StringBuilder str = new StringBuilder();
        int prefixSet = SchemeConvert.ToInt(primitive.GetProperty(PrefixSetSymbol), 0);
        CheckPrefix("virga", Virga, prefixSet, str);
        CheckPrefix("stropha", Stropha, prefixSet, str);
        CheckPrefix("inclinatum", Inclinatum, prefixSet, str);
        CheckPrefix("auctum", Auctum, prefixSet, str);
        CheckPrefix("descendens", Descendens, prefixSet, str);
        CheckPrefix("ascendens", Ascendens, prefixSet, str);
        CheckPrefix("oriscus", Oriscus, prefixSet, str);
        CheckPrefix("quilisma", Quilisma, prefixSet, str);
        CheckPrefix("deminutum", Deminutum, prefixSet, str);
        CheckPrefix("cavum", Cavum, prefixSet, str);
        CheckPrefix("linea", Linea, prefixSet, str);
        return str.ToString();
    }

    private static void CheckPrefix(string name, int mask, int prefixSet, StringBuilder str)
    {
        if ((prefixSet & mask) != 0)
        {
            if (str.Length != 0)
            {
                str.Append(", ");
            }

            str.Append(name);
        }
    }
}
