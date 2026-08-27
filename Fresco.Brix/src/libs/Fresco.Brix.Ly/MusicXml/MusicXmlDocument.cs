// === python-ly ly.musicxml.create_musicxml module (class MusicXML) ===
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
using System.IO;
using System.Text;

namespace Fresco.Brix.Ly.MusicXml; //was previously: ly/musicxml/create_musicxml.py (class MusicXML)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A finished MusicXML tree, and how it is written out.
/// </summary>
/// <remarks>
/// Upstream's <c>MusicXML</c> class. The declaration and the DOCTYPE are
/// written here as literal text — they are upstream's two module-level strings,
/// character for character, down to the thirty-two spaces that indent the
/// second line of the DOCTYPE — and only then is the tree serialised, with no
/// declaration of its own.
/// </remarks>
public sealed class MusicXmlDocument
{
    /// <summary>The XML declaration, with a place for the encoding's name.</summary>
    public const string XmlDeclaration = "<?xml version=\"1.0\" encoding=\"{0}\"?>";

    /// <summary>The MusicXML DOCTYPE for the version this writes.</summary>
    /// <remarks>
    /// <para>
    /// ⚠ THE PUBLIC IDENTIFIER MUST NAME THE SAME VERSION THE ROOT ELEMENT
    /// DOES (ruling FR15). //was previously: upstream writes
    /// <c>-//Recordare//DTD MusicXML <b>2.0</b> Partwise//EN</c> on a root that
    /// declares <c>version="3.0"</c> — a document announcing two different
    /// versions of itself, which is a defect however tolerant readers are about
    /// it. This one names 4.0 in both places.
    /// </para>
    /// <para>
    /// The identifier is the one the specification's own <c>catalog.xml</c>
    /// lists (<c>-//Recordare//DTD MusicXML 4.0 Partwise//EN</c> resolving to
    /// <c>partwise.dtd</c>). ⚠ The DTD form of MusicXML is DEPRECATED as of
    /// version 4.0 — its own header says "Use the musicxml.xsd W3C XML Schema
    /// definition instead" — so the SCHEMA is what conformance is measured
    /// against here, and this declaration is kept only because it is
    /// information upstream provides and a corrected version of it costs
    /// nothing. If a future reader ever objects to it, deleting it is
    /// conformant too.
    /// </para>
    /// </remarks>
    public const string DocTypeDeclaration =
        "<!DOCTYPE score-partwise PUBLIC \"-//Recordare//DTD MusicXML "
        + MusicXmlCreator.MusicXmlVersion + " Partwise//EN\"\n"
        + "                                \"http://www.musicxml.org/dtds/partwise.dtd\">";

    /// <summary>Creates a document over a root element.</summary>
    /// <param name="root">The root.</param>
    public MusicXmlDocument(ETreeElement root)
        => Root = root ?? throw new ArgumentNullException(nameof(root));

    /// <summary>Gets the document's root element.</summary>
    public ETreeElement Root { get; }

    /// <summary>
    /// Gets whether the document contains any music at all — which is the one
    /// precondition of conformance that a USER can walk into.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ⚠ RULING FR15. <c>&lt;part-list&gt;</c> must hold at least one
    /// <c>&lt;score-part&gt;</c> and <c>&lt;score-partwise&gt;</c> at least one
    /// <c>&lt;part&gt;</c>; a document with neither is not MusicXML, whatever
    /// its extension says. It arises when the converter cannot find anything in
    /// the source it can turn into a part — a file that is all header, all
    /// markup, or shaped in a way the reader does not follow — and upstream
    /// writes the empty skeleton anyway. Thirty of the eighty-one documents in
    /// the parity corpus are like this, so it is not a corner case.
    /// </para>
    /// <para>
    /// The DOCUMENT still gets built, because that is what the parity tests
    /// compare against python-ly. What must not happen is WRITING it, and
    /// <c>MusicXmlExport</c> is where that is refused.
    /// </para>
    /// </remarks>
    public bool HasParts => Root.IndexOfTag("part") >= 0;

    /// <summary>Adds newlines and indenting to the tree, in place.</summary>
    /// <param name="indent">One level of indent.</param>
    public void Indent(string indent = "  ") => ETreeUtil.Indent(Root, indent);

    /// <summary>Returns the tree as XML text, with no declaration.</summary>
    /// <returns>The text.</returns>
    public string ToXmlString()
    {
        var builder = new StringBuilder();
        Root.Serialize(builder);
        return builder.ToString();
    }

    /// <inheritdoc/>
    public override string ToString() => ToXmlString();

    /// <summary>Returns the whole document as text.</summary>
    /// <param name="encodingName">
    /// The name to write into the declaration. It is a NAME, not an encoding:
    /// the bytes are always UTF-8, as upstream's are for every encoding it is
    /// ever given.
    /// </param>
    /// <param name="docType">Whether to write the declaration and the DOCTYPE.</param>
    /// <returns>The document.</returns>
    public string ToDocumentString(string encodingName = "UTF-8", bool docType = true)
    {
        var builder = new StringBuilder();
        if (docType)
        {
            builder.Append(string.Format(
                System.Globalization.CultureInfo.InvariantCulture, XmlDeclaration, encodingName));
            builder.Append('\n');
            builder.Append(DocTypeDeclaration);
            builder.Append('\n');
        }
        else
        {
            builder.Append(string.Format(
                System.Globalization.CultureInfo.InvariantCulture, XmlDeclaration, encodingName));
            builder.Append('\n');
        }

        Root.Serialize(builder);
        return builder.ToString();
    }

    /// <summary>Writes the document to a file.</summary>
    /// <param name="fileName">The file.</param>
    /// <param name="encodingName">The name to write into the declaration.</param>
    /// <param name="docType">Whether to write the DOCTYPE.</param>
    public void Write(string fileName, string encodingName = "UTF-8", bool docType = true)
    {
        //No byte order mark: python writes the bytes and nothing else, and a
        //mark at the front of a MusicXML file trips some readers.
        File.WriteAllText(
            fileName, ToDocumentString(encodingName, docType), new UTF8Encoding(false));
    }

    /// <summary>Writes the document to a stream.</summary>
    /// <param name="stream">Where to write.</param>
    /// <param name="encodingName">The name to write into the declaration.</param>
    /// <param name="docType">Whether to write the DOCTYPE.</param>
    public void Write(Stream stream, string encodingName = "UTF-8", bool docType = true)
    {
        if (stream == null) { throw new ArgumentNullException(nameof(stream)); }

        byte[] bytes = new UTF8Encoding(false).GetBytes(ToDocumentString(encodingName, docType));
        stream.Write(bytes, 0, bytes.Length);
    }
}

/// <summary>A time signature, as the bar attributes carry it.</summary>
/// <remarks>
/// Upstream passes a list of two or three strings; a record makes the third one
/// — the symbol, which turns 4/4 into a C — visibly optional.
/// </remarks>
public sealed class TimeSignature
{
    /// <summary>Creates a time signature.</summary>
    /// <param name="beats">How many beats.</param>
    /// <param name="beatType">What kind of beat.</param>
    /// <param name="symbol">The symbol to draw instead of the numbers, or null.</param>
    public TimeSignature(string beats, string beatType, string symbol = null)
    {
        Beats = beats;
        BeatType = beatType;
        Symbol = symbol;
    }

    /// <summary>Gets how many beats there are to the bar.</summary>
    public string Beats { get; }

    /// <summary>Gets what kind of beat they are.</summary>
    public string BeatType { get; }

    /// <summary>Gets the symbol drawn instead of the numbers, or null.</summary>
    public string Symbol { get; }
}

/// <summary>A clef, as the bar attributes carry it.</summary>
public sealed class ClefSignature
{
    /// <summary>Creates a clef.</summary>
    /// <param name="sign">Its letter.</param>
    /// <param name="line">Which staff line it sits on.</param>
    /// <param name="octaveChange">How many octaves it transposes.</param>
    public ClefSignature(string sign, int line, int octaveChange = 0)
    {
        Sign = sign;
        Line = line;
        OctaveChange = octaveChange;
    }

    /// <summary>Gets the clef's letter.</summary>
    public string Sign { get; }

    /// <summary>Gets which staff line it sits on.</summary>
    public int Line { get; }

    /// <summary>Gets how many octaves it transposes.</summary>
    public int OctaveChange { get; }
}

/// <summary>Everything a bar's attributes element may be asked to say.</summary>
/// <remarks>
/// Upstream's <c>new_bar_attr</c> takes six keyword arguments, and every caller
/// passes some subset by name. A parameter object says the same thing without a
/// call site having to spell out the ones it does not care about — and keeps
/// upstream's order, because that is the order the elements come out in.
/// </remarks>
public sealed class BarAttributesRequest
{
    /// <summary>Gets or sets the clef, or null for none.</summary>
    public ClefSignature Clef { get; set; }

    /// <summary>Gets or sets the time signature, or null for none.</summary>
    public TimeSignature Time { get; set; }

    /// <summary>Gets or sets the key, in fifths, or null for none.</summary>
    public int? Key { get; set; }

    /// <summary>Gets or sets the mode's name.</summary>
    public string Mode { get; set; }

    /// <summary>Gets or sets the divisions per quarter note, or zero for none.</summary>
    public int Divisions { get; set; }

    /// <summary>Gets or sets how many bars a multiple rest lasts, or zero for none.</summary>
    public int MultiRest { get; set; }
}
