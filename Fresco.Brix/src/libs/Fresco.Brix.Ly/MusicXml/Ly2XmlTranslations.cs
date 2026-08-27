// === python-ly ly.musicxml.ly2xml_mediator module (the translation functions) ===
//
// Copyright (c) 2008 - 2015 by Wilbert Berendsen
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation; either version 3
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
// See http://www.gnu.org/licenses/ for more information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Fresco.Brix.Ly.MusicXml; //was previously: ly/musicxml/ly2xml_mediator.py (translation functions)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The lookup tables that turn LilyPond's words into MusicXML's.
/// </summary>
/// <remarks>
/// Upstream's module-level translation functions, kept together because they
/// are what a reader adding a clef or an articulation has to edit — and each
/// one carries upstream's own instructions for doing that.
/// </remarks>
public static class Ly2XmlTranslations
{
    /// <summary>Returns the note name for a note's index.</summary>
    /// <param name="index">The index, zero for C.</param>
    /// <returns>The name.</returns>
    public static string GetNoteName(int index) => NoteNames[index];

    /// <summary>
    /// Turns an alteration into the number MusicXML wants: twice LilyPond's,
    /// and a whole number wherever it comes out whole.
    /// </summary>
    /// <param name="alter">LilyPond's alteration.</param>
    /// <returns>MusicXML's.</returns>
    public static double GetXmlAlter(double alter) => alter * 2;

    /// <summary>Turns a LilyPond duration value into MusicXML's name for it.</summary>
    /// <param name="durationValue">The value, such as <c>4</c>.</param>
    /// <returns>The name, such as <c>quarter</c>.</returns>
    /// <remarks>
    /// ⚠ A value the list does not know falls to index 5 — <c>quarter</c> —
    /// which is upstream's own <c>except ValueError</c> arm. And 2048th is in
    /// the list although MusicXML has no such type; upstream says so in a
    /// comment and it is kept.
    /// </remarks>
    public static string DurationValueToType(string durationValue)
    {
        int index = Array.IndexOf(DurationValues, durationValue ?? string.Empty);
        return XmlTypes[index < 0 ? 5 : index];
    }

    /// <summary>Returns how many sharps or flats a key has.</summary>
    /// <param name="key">The key's note name, in Dutch.</param>
    /// <param name="mode">The mode.</param>
    /// <returns>The count, negative for flats, or zero when the mode is unknown.</returns>
    /// <remarks>
    /// ⚠ Upstream returns <c>None</c> for a mode that is not major, minor or
    /// dorian, and the caller passes that straight into <c>set_key</c>. The port
    /// answers zero instead — the difference is invisible in the file, because
    /// the exporter only ever asks about a key it has already decided to write,
    /// and a null there would be an exception rather than an element.
    /// </remarks>
    public static int GetFifths(string key, string mode)
    {
        int fifths = 0;
        int sharp = Array.IndexOf(SharpKeys, key);
        int flat = Array.IndexOf(FlatKeys, key);
        if (sharp >= 0) { fifths = sharp; }
        else if (flat >= 0) { fifths = -flat; }

        return mode switch
        {
            "minor" => fifths - 3,
            "major" => fifths,
            "dorian" => fifths - 2,
            _ => 0,
        };
    }

    /// <summary>Returns the clef a LilyPond clef name means.</summary>
    /// <param name="clefName">LilyPond's name.</param>
    /// <returns>The clef, or null when the table does not know the name.</returns>
    /// <remarks>
    /// To add a clef, look the name up in LilyPond and its definition in
    /// MusicXML, and add it to the table below — upstream's own instruction.
    /// The last six entries are the ones upstream marks "from here on the clefs
    /// will end up with wrong symbols"; they are kept as they are.
    /// </remarks>
    public static ClefSignature ClefNameToClef(string clefName)
        => Clefs.TryGetValue(clefName ?? string.Empty, out ClefSignature clef) ? clef : null;

    /// <summary>Returns the multiplier that makes a division count whole.</summary>
    /// <param name="numerator">The numerator.</param>
    /// <param name="denominator">The denominator.</param>
    /// <returns>The denominator of the two in lowest terms.</returns>
    public static long GetMult(Fraction numerator, Fraction denominator)
        => (numerator / denominator).Denominator;

    /// <summary>Returns the voice number a <c>\voiceOne</c>-style command means.</summary>
    /// <param name="command">The command's name, without its backslash.</param>
    /// <returns>The number, one-based.</returns>
    public static int GetVoice(string command) => Array.IndexOf(Voices, command) + 1;

    /// <summary>
    /// Sorts an articulation token into an articulation, an ornament, or
    /// something else.
    /// </summary>
    /// <param name="articulationToken">The token.</param>
    /// <returns>
    /// MusicXML's element name, or the marker <c>ornament</c> or <c>other</c>,
    /// or null when nothing is known about it.
    /// </returns>
    /// <remarks>
    /// To add an articulation, look up its name or abbreviation in LilyPond and
    /// the matching node name in MusicXML, and add it to the table — upstream's
    /// own instruction.
    /// </remarks>
    public static string ArticulationTokenToXmlName(string articulationToken)
    {
        if (articulationToken == null) { return null; }

        if (Articulations.TryGetValue(articulationToken, out string name)) { return name; }

        if (Array.IndexOf(Ornaments, articulationToken) >= 0) { return "ornament"; }

        return Array.IndexOf(Others, articulationToken) >= 0 ? "other" : null;
    }

    /// <summary>
    /// Works out what one repeat of a counted tremolo is worth, and what it is
    /// drawn as.
    /// </summary>
    /// <param name="repeats">How many repeats.</param>
    /// <param name="baseScaling">The written duration of one repeat.</param>
    /// <param name="duration">The note value the tremolo is written with.</param>
    /// <returns>The whole tremolo's duration, and its written type.</returns>
    public static (BaseScaling Duration, string Type) CalcTremDuration(
        int repeats, BaseScaling baseScaling, int duration)
    {
        Fraction newBase = baseScaling.Base * new Fraction(repeats);
        //Upstream's own arithmetic, integer division and all: past the point
        //where one repeat is shorter than the written value, the tremolo is
        //named by a LOG duration rather than by a value.
        string tremLength = repeats > duration
            ? Fresco.Brix.Ly.Durations.ToString((int)((repeats / duration) * -0.5))
            : (duration / repeats).ToString(CultureInfo.InvariantCulture);
        return (new BaseScaling(newBase, baseScaling.Scaling), DurationValueToType(tremLength));
    }

    /// <summary>Returns MusicXML's name for a LilyPond line style.</summary>
    /// <param name="style">LilyPond's name.</param>
    /// <returns>MusicXML's, or null when it is not one of the four.</returns>
    public static string GetLineStyle(string style)
        => LineStyles.TryGetValue(style ?? string.Empty, out string name) ? name : null;

    /// <summary>Returns the bracket a system start delimiter means.</summary>
    /// <param name="lilySystemStart">LilyPond's grob name.</param>
    /// <returns>The bracket, or null when it is neither of the two.</returns>
    public static string GetGroupSymbol(string lilySystemStart)
        => GroupSymbols.TryGetValue(lilySystemStart ?? string.Empty, out string name) ? name : null;

    /// <summary>Encodes a number as a sequence of letters, skipping I.</summary>
    /// <param name="n">The number, one-based.</param>
    /// <returns>The letters.</returns>
    /// <remarks>
    /// Upstream's <c>Mediator.bijective</c>, used for rehearsal marks: 1 is A,
    /// 8 is H, 9 is J — the letter I is left out, as engraving convention has
    /// it, because it reads as a bar line.
    /// </remarks>
    public static string Bijective(int n)
    {
        const string digits = "ABCDEFGHJKLMNOPQRSTUVWXYZ";
        var result = new StringBuilder();
        while (n > 0)
        {
            int mod = (n - 1) % digits.Length;
            n = (n - 1) / digits.Length;
            result.Append(digits[mod]);
        }

        var reversed = new StringBuilder(result.Length);
        for (int i = result.Length - 1; i >= 0; i--) { reversed.Append(result[i]); }

        return reversed.ToString();
    }

    private static readonly string[] NoteNames = { "C", "D", "E", "F", "G", "A", "B" };

    private static readonly string[] XmlTypes =
    {
        "maxima", "long", "breve", "whole",
        "half", "quarter", "eighth",
        "16th", "32nd", "64th",
        "128th", "256th", "512th", "1024th", "2048th",
    };

    private static readonly string[] SharpKeys =
    {
        "c", "g", "d", "a", "e", "b", "fis", "cis", "gis", "dis", "ais", "eis",
        "bis", "fisis", "cisis",
    };

    private static readonly string[] FlatKeys =
    {
        "c", "f", "bes", "es", "as", "des", "ges", "ces", "fes", "beses", "eses", "ases",
    };

    private static readonly string[] Voices = { "voiceOne", "voiceTwo", "voiceThree", "voiceFour" };

    private static readonly string[] Ornaments = { "\\trill", "\\prall", "\\mordent", "\\turn" };

    private static readonly string[] Others = { "\\fermata" };

    private static readonly Dictionary<string, string> Articulations
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["."] = "staccato",
            ["-"] = "tenuto",
            [">"] = "accent",
            ["_"] = "detached-legato",
            ["!"] = "staccatissimo",
            ["\\staccatissimo"] = "staccatissimo",
            ["\\breathe"] = "breath-mark",
        };

    private static readonly Dictionary<string, string> LineStyles
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["dashed-line"] = "dashed",
            ["dotted-line"] = "dotted",
            ["trill"] = "wavy",
            ["zigzag"] = "wavy",
        };

    private static readonly Dictionary<string, string> GroupSymbols
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["SystemStartBrace"] = "brace",
            ["SystemStartSquare"] = "square",
        };

    private static readonly Dictionary<string, ClefSignature> Clefs
        = new Dictionary<string, ClefSignature>(StringComparer.Ordinal)
        {
            ["treble"] = new ClefSignature("G", 2, 0),
            ["violin"] = new ClefSignature("G", 2, 0),
            ["G"] = new ClefSignature("G", 2, 0),
            ["bass"] = new ClefSignature("F", 4, 0),
            ["F"] = new ClefSignature("F", 4, 0),
            ["alto"] = new ClefSignature("C", 3, 0),
            ["C"] = new ClefSignature("C", 3, 0),
            ["tenor"] = new ClefSignature("C", 4, 0),
            ["treble_8"] = new ClefSignature("G", 2, -1),
            ["treble_15"] = new ClefSignature("G", 2, -2),
            ["bass_8"] = new ClefSignature("F", 4, -1),
            ["bass_15"] = new ClefSignature("F", 4, -2),
            ["treble^8"] = new ClefSignature("G", 2, 1),
            ["treble^15"] = new ClefSignature("G", 2, 2),
            ["bass^8"] = new ClefSignature("F", 4, 1),
            ["bass^15"] = new ClefSignature("F", 4, 2),
            ["percussion"] = new ClefSignature("percussion", 0, 0),
            ["tab"] = new ClefSignature("TAB", 5, 0),
            ["soprano"] = new ClefSignature("C", 1, 0),
            ["mezzosoprano"] = new ClefSignature("C", 2, 0),
            ["baritone"] = new ClefSignature("C", 5, 0),
            ["varbaritone"] = new ClefSignature("F", 3, 0),
            ["baritonevarF"] = new ClefSignature("F", 3, 0),
            ["french"] = new ClefSignature("G", 1, 0),
            ["subbass"] = new ClefSignature("F", 5, 0),

            //From here on the clefs will end up with wrong symbols -- upstream's
            //own note, kept with the entries it is about.
            ["GG"] = new ClefSignature("G", 2, -1),
            ["tenorG"] = new ClefSignature("G", 2, -1),
            ["varC"] = new ClefSignature("C", 3, 0),
            ["altovarC"] = new ClefSignature("C", 3, 0),
            ["tenorvarC"] = new ClefSignature("C", 4, 0),
            ["baritonevarC"] = new ClefSignature("C", 5, 0),
        };

    //ly.duration's own list, which is what durval2type indexes into. Read
    //from the ported module rather than written out again, so the two cannot
    //drift.
    private static string[] DurationValues => Fresco.Brix.Ly.Durations.Names;
}
