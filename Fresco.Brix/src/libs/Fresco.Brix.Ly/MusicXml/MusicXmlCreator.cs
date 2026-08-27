// === python-ly ly.musicxml.create_musicxml module ===
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

namespace Fresco.Brix.Ly.MusicXml; //was previously: ly/musicxml/create_musicxml.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Builds a MusicXML document, node by node.
/// </summary>
/// <remarks>
/// <para>
/// ⚠⚠ RULING FR15 — THE HARD RULE, AND IT HAS NO EXCEPTIONS. ⚠⚠
/// <b>Fresco.Brix does not write a MusicXML file that fails to conform to the
/// published MusicXML schema.</b> Not "usually", not "unless upstream did it
/// differently": at all times. If a change here would emit something the schema
/// forbids, the change is wrong — find where the specification says that
/// information belongs and put it there instead.
/// </para>
/// <para>
/// WHERE THE SPECIFICATION IS. MusicXML is an open standard and NOT an
/// invention of Frescobaldi, of python-ly or of this project. It was created by
/// Recordare LLC (Michael Good), its copyright passed to MakeMusic in 2011, and
/// since 2017 it has been developed by the <b>W3C Music Notation Community
/// Group</b>. A future developer needs these four addresses and nothing else:
/// <list type="bullet">
/// <item><description>The group:
///   <c>https://www.w3.org/community/music-notation/</c></description></item>
/// <item><description>The reference documentation for the current published
///   version, MusicXML 4.0 (June 2021), a W3C Community Group Final Report:
///   <c>https://www.w3.org/2021/06/musicxml40/</c></description></item>
/// <item><description>The normative schema and DTD sources, by version tag:
///   <c>https://github.com/w3c/musicxml</c></description></item>
/// <item><description>The copy this repository validates against, vendored
///   verbatim with its provenance and licence:
///   <c>tests/libs/Fresco.Brix.Ly.Tests/schema/</c></description></item>
/// </list>
/// </para>
/// <para>
/// HOW THE RULE IS KEPT HONEST. A rule nothing enforces is a wish, so
/// <c>MusicXmlSchemaTests</c> exports every document in the parity corpus and
/// validates the result against that vendored schema. Break conformance and a
/// test says which element and why.
/// </para>
/// <para>
/// AND THE RULE'S COMPANION: <b>lose no information.</b> Everything Frescobaldi
/// and python-ly put into a file is put into ours too — unless it is wrong —
/// but in the place the specification says it goes. The header variables
/// MusicXML has no element for are the worked example: upstream writes
/// <c>&lt;subtitle&gt;</c> and <c>&lt;opus&gt;</c> as bare children of
/// <c>&lt;identification&gt;</c>, which no version of the schema allows; the
/// specification's own answer is <c>&lt;miscellaneous-field name="subtitle"&gt;</c>
/// ("If a program has other metadata not yet supported in the MusicXML format,
/// it can go in the miscellaneous element"), and that is where ours go. The
/// information survives; only its address changes.
/// </para>
/// <para>
/// Upstream's <c>CreateMusicXML</c>. It is the bottom of the export: something
/// else decides WHAT the music is, and this only knows where each element goes
/// in the MusicXML standard's own tree. The three levels upstream separates —
/// the basic elements, the high-level node creation, the low-level node
/// creation — are kept in that order and under those headings.
/// </para>
/// <para>
/// Its state is a handful of "where am I" fields — the current part, bar, note,
/// notations, articulations, ornaments, technical — because MusicXML wants
/// several of those created lazily and at most once per note. That is upstream's
/// design and the reason a note's elements come out in the order they do.
/// </para>
/// </remarks>
public sealed class MusicXmlCreator
{
    private readonly ETreeElement _scoreInfo;
    private readonly ETreeElement _partList;

    //Created only when something needs it, and always LAST inside
    //<identification>, which is where the sequence puts it.
    private ETreeElement _miscellaneous;

    private ETreeElement _currentPart;
    private ETreeElement _currentBar;
    private ETreeElement _currentNote;
    private ETreeElement _currentNotation;
    private ETreeElement _currentArticulation;
    private ETreeElement _currentOrnament;
    private ETreeElement _currentTechnical;
    private ETreeElement _barAttributes;
    private ETreeElement _direction;
    private ETreeElement _duration;
    private Fraction _mult = new Fraction(1);
    private int _partCount = 1;
    private int _barNumber = 1;

    /// <summary>
    /// The version of MusicXML the documents this writes conform to, and
    /// declare (ruling FR15).
    /// </summary>
    /// <remarks>
    /// ⚠ This is not decoration: it is a CLAIM, and
    /// <c>MusicXmlSchemaTests</c> holds it to that claim by validating against
    /// the 4.0 schema. Raising it means fetching the newer schema into
    /// <c>tests/libs/Fresco.Brix.Ly.Tests/schema/</c> and making the corpus
    /// pass against it — not editing this string.
    /// //was previously: "3.0", which is what python-ly declares. The output is
    /// a subset that satisfies both, so the move costs nothing and names the
    /// version the W3C Music Notation Community Group actually publishes.
    /// </remarks>
    public const string MusicXmlVersion = "4.0";

    /// <summary>Creates the basic structure of the document, with no music in it.</summary>
    /// <remarks>
    /// ⚠ THE ORDER OF <c>&lt;identification&gt;</c>'S CHILDREN IS NORMATIVE, and
    /// this constructor is where it is easiest to break. The schema declares
    /// <c>&lt;xs:sequence&gt;</c>, not a choice:
    /// <code>
    /// identification (creator*, rights*, encoding?, source?, relation*, miscellaneous?)
    /// </code>
    /// — identical in the 3.0 XSD, the 4.0 XSD and the DTD, unchanged in fifteen
    /// years. <c>&lt;encoding&gt;</c> is created HERE, before any creator or
    /// rights element can exist, so everything that must precede it is
    /// INSERTED rather than appended; see <see cref="CreateScoreInfo"/>.
    /// //was previously: creators and rights were appended after the encoding,
    /// which made every score with a composer invalid — python-ly does this and
    /// its own test suite never caught it, because none of its test documents
    /// has one.
    /// </remarks>
    public MusicXmlCreator()
    {
        Root = new ETreeElement("score-partwise");
        Root.Set("version", MusicXmlVersion);
        _scoreInfo = Root.SubElement("identification");
        ETreeElement encoding = _scoreInfo.SubElement("encoding");
        ETreeElement software = encoding.SubElement("software");
        software.Text = DefaultSoftware;
        ETreeElement encodingDate = encoding.SubElement("encoding-date");
        encodingDate.Text = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        _partList = Root.SubElement("part-list");
    }

    /// <summary>
    /// What the <c>&lt;software&gt;</c> element says when nobody says otherwise.
    /// </summary>
    /// <remarks>
    /// ⚠ Upstream writes <c>"python-ly " + ly.pkginfo.version</c> here, and
    /// Frescobaldi then OVERWRITES it with its own name and version on the way
    /// out (<c>file_export/__init__.py</c>). Neither string is a fact about the
    /// music, so the port names itself and leaves the application to say what it
    /// likes; the parity fixtures hold the placeholder <c>SOFTWARE</c> in this
    /// element's place for the same reason.
    /// </remarks>
    public const string DefaultSoftware = "Fresco.Brix.Ly";

    /// <summary>Gets the document's root element.</summary>
    public ETreeElement Root { get; }

    /// <summary>Gets the number of the bar being written.</summary>
    public int BarNumber => _barNumber;

    // ================= Building the basic Elements =================

    /// <summary>Creates the score's title.</summary>
    /// <param name="title">The title.</param>
    public void CreateTitle(string title)
    {
        var movementTitle = new ETreeElement("movement-title") { Text = title };
        Root.Insert(0, movementTitle);
    }

    /// <summary>
    /// Creates one piece of score information that the schema HAS an element
    /// for — a creator or a rights statement — in its proper place.
    /// </summary>
    /// <param name="tag">Its element name; <c>creator</c> or <c>rights</c>.</param>
    /// <param name="info">Its text.</param>
    /// <param name="attributes">Its attributes, in order.</param>
    /// <remarks>
    /// ⚠ INSERTED BEFORE <c>&lt;encoding&gt;</c>, NOT APPENDED (ruling FR15).
    /// Both elements come before it in the sequence, and the encoding already
    /// exists by the time anybody calls this. Anything the schema has no
    /// element for does NOT belong here — it belongs in
    /// <see cref="AddMiscellaneousField"/>.
    /// </remarks>
    public void CreateScoreInfo(
        string tag, string info, params (string Name, string Value)[] attributes)
    {
        var node = new ETreeElement(tag);
        if (attributes != null)
        {
            foreach ((string name, string value) in attributes) { node.Set(name, value); }
        }

        node.Text = info;

        int at = _scoreInfo.IndexOfTag("encoding");
        if (at < 0) { _scoreInfo.Append(node); }
        else { _scoreInfo.Insert(at, node); }
    }

    /// <summary>
    /// Records a piece of metadata the MusicXML format has no element of its
    /// own for.
    /// </summary>
    /// <param name="name">What the metadata is called.</param>
    /// <param name="info">Its text.</param>
    /// <remarks>
    /// <para>
    /// ⚠ RULING FR15's COMPANION: LOSE NO INFORMATION, BUT PUT IT WHERE THE
    /// SPECIFICATION SAYS. LilyPond headers run well past what MusicXML models
    /// — <c>subtitle</c>, <c>subsubtitle</c>, <c>dedication</c>, <c>opus</c>,
    /// <c>piece</c>, <c>meter</c>, <c>instrument</c>, and whatever else a
    /// document invents. The specification anticipated exactly this and says so
    /// in the schema's own words:
    /// </para>
    /// <para>
    /// "If a program has other metadata not yet supported in the MusicXML
    /// format, it can go in the miscellaneous element. The miscellaneous type
    /// puts each separate part of metadata into its own miscellaneous-field
    /// type."
    /// </para>
    /// <para>
    /// //was previously: upstream writes each of these as a bare child of
    /// <c>&lt;identification&gt;</c> — <c>&lt;subtitle&gt;a little
    /// star&lt;/subtitle&gt;</c> — which no version of the schema allows and
    /// which a strict reader rejects outright. The text is identical here; only
    /// its address changed.
    /// </para>
    /// </remarks>
    public void AddMiscellaneousField(string name, string info)
    {
        //<miscellaneous> is LAST in the sequence, so appending to
        //<identification> is right — and creating it lazily keeps it out of
        //documents that have nothing to put in it.
        _miscellaneous ??= _scoreInfo.SubElement("miscellaneous");
        ETreeElement field = _miscellaneous.SubElement("miscellaneous-field", ("name", name));
        field.Text = info;
    }

    /// <summary>Creates a part group.</summary>
    /// <param name="groupType">Whether the group starts or stops.</param>
    /// <param name="number">Its number.</param>
    /// <param name="name">Its name, or null.</param>
    /// <param name="abbreviation">Its short name, or null.</param>
    /// <param name="symbol">Its bracket symbol, or null.</param>
    public void CreatePartGroup(
        string groupType, int number, string name = null,
        string abbreviation = null, string symbol = null)
    {
        ETreeElement group = _partList.SubElement(
            "part-group", ("type", groupType), ("number", Str(number)));
        if (!string.IsNullOrEmpty(name)) { group.SubElement("group-name").Text = name; }

        if (!string.IsNullOrEmpty(abbreviation))
        {
            group.SubElement("group-abbreviation").Text = abbreviation;
        }

        if (!string.IsNullOrEmpty(symbol)) { group.SubElement("group-symbol").Text = symbol; }
    }

    /// <summary>Creates a new part and makes it the one being written.</summary>
    /// <param name="name">The part's name.</param>
    /// <param name="abbreviation">Its short name, or null for none.</param>
    /// <param name="midi">The instrument name, or null for no MIDI information.</param>
    public void CreatePart(string name = "unnamed", string abbreviation = null, string midi = null)
    {
        string number = Str(_partCount);
        ETreeElement part = _partList.SubElement("score-part", ("id", "P" + number));
        part.SubElement("part-name").Text = name;
        if (!string.IsNullOrEmpty(abbreviation))
        {
            part.SubElement("part-abbreviation").Text = abbreviation;
        }

        if (!string.IsNullOrEmpty(midi))
        {
            ETreeElement scoreInstrument = part.SubElement(
                "score-instrument", ("id", "P" + number + "-I" + number));
            scoreInstrument.SubElement("instrument-name").Text = midi;

            if (MidiSoundMap.Sounds.TryGetValue(midi, out string sound)
                && !string.IsNullOrEmpty(sound))
            {
                scoreInstrument.SubElement("instrument-sound").Text = sound;
            }

            ETreeElement midiInstrument = part.SubElement(
                "midi-instrument", ("id", "P" + number + "-I" + number));
            midiInstrument.SubElement("midi-channel").Text = number;
            midiInstrument.SubElement("midi-name").Text = midi;
        }

        _currentPart = Root.SubElement("part", ("id", "P" + number));
        _partCount++;
        _barNumber = 1;
    }

    /// <summary>Creates a new measure and makes it the one being written.</summary>
    /// <param name="pickup">Whether this is an anacrusis, which is numbered zero.</param>
    /// <param name="attributes">The bar attributes to set on it, or null for none.</param>
    public void CreateMeasure(bool pickup = false, BarAttributesRequest attributes = null)
    {
        if (pickup && _barNumber == 1) { _barNumber = 0; }

        _currentBar = _currentPart.SubElement("measure", ("number", Str(_barNumber)));
        _barNumber++;
        if (attributes != null) { NewBarAttribute(attributes); }
    }

    // ================= High-level node creation =================

    /// <summary>Creates every node an ordinary note needs.</summary>
    /// <param name="step">Its note name.</param>
    /// <param name="octave">Its octave.</param>
    /// <param name="durationType">Its written duration, such as <c>quarter</c>.</param>
    /// <param name="divisionDuration">Its duration in divisions.</param>
    /// <param name="alter">Its accidental, in semitones.</param>
    /// <param name="accidentalToken">
    /// <c>!</c> for a forced accidental, <c>?</c> for a cautionary one, or null.
    /// </param>
    /// <param name="voice">Its voice number.</param>
    /// <param name="dot">How many dots it has.</param>
    /// <param name="chord">Whether it belongs to the chord before it.</param>
    /// <param name="grace">Whether it is a grace note, and whether that grace is slashed.</param>
    /// <param name="stemDirection">Its stem direction, or null.</param>
    public void NewNote(
        string step, int octave, string durationType, int divisionDuration,
        double alter = 0, string accidentalToken = null, int voice = 1, int dot = 0,
        bool chord = false, (bool IsGrace, bool Slash) grace = default,
        string stemDirection = null)
    {
        CreateNote();
        if (grace.IsGrace) { AddGrace(grace.Slash); }

        if (chord) { AddChord(); }

        AddPitch(step, alter, octave);
        if (!grace.IsGrace) { AddDivisionDuration(divisionDuration); }

        AddVoice(voice);
        AddDurationType(durationType);
        for (int i = 0; i < dot; i++) { AddDot(); }

        if (alter != 0 || !string.IsNullOrEmpty(accidentalToken))
        {
            if (accidentalToken == "!") { AddAccidental(alter, cautionary: true); }
            else if (accidentalToken == "?") { AddAccidental(alter, parentheses: true); }
            else { AddAccidental(alter); }
        }

        if (!string.IsNullOrEmpty(stemDirection)) { SetStemDirection(stemDirection); }
    }

    /// <summary>Creates every node an unpitched note needs.</summary>
    /// <param name="step">Its display note name.</param>
    /// <param name="octave">Its display octave.</param>
    /// <param name="durationType">Its written duration.</param>
    /// <param name="divisionDuration">Its duration in divisions.</param>
    /// <param name="voice">Its voice number.</param>
    /// <param name="dot">How many dots it has.</param>
    /// <param name="chord">Whether it belongs to the chord before it.</param>
    /// <param name="grace">Whether it is a grace note, and whether that grace is slashed.</param>
    public void NewUnpitchedNote(
        string step, int octave, string durationType, int divisionDuration,
        int voice = 1, int dot = 0, bool chord = false,
        (bool IsGrace, bool Slash) grace = default)
    {
        CreateNote();
        if (grace.IsGrace) { AddGrace(grace.Slash); }

        if (chord) { AddChord(); }

        AddUnpitched(step, octave);
        if (!grace.IsGrace) { AddDivisionDuration(divisionDuration); }

        //⚠ VOICE BEFORE TYPE (ruling FR15). The note's sequence is
        //  ... duration, tie?, instrument*, [footnote?, level?, voice?], type?, dot* ...
        //so `voice` — which the schema reaches through the editorial-voice
        //group — precedes `type`.
        ////was previously: AddDurationType then AddVoice, which is the reverse
        //and invalid. NewNote above has always had it the right way round, so
        //this was an oversight in upstream rather than a decision — and it made
        //every drum note in a document invalid.
        AddVoice(voice);
        AddDurationType(durationType);
        for (int i = 0; i < dot; i++) { AddDot(); }
    }

    /// <summary>Turns the note being written into part of a tuplet.</summary>
    /// <param name="fraction">The tuplet's ratio, actual over normal.</param>
    /// <param name="baseScaling">The note's own base and scaling.</param>
    /// <param name="tupletType">Whether the tuplet starts, stops, or neither.</param>
    /// <param name="number">The tuplet's number.</param>
    /// <param name="divisions">Divisions per quarter note.</param>
    /// <param name="actualType">The actual note type shown, or null.</param>
    /// <param name="normalType">The normal note type shown, or null.</param>
    public void TupletNote(
        (int Actual, int Normal) fraction, (Fraction Base, Fraction Scaling) baseScaling,
        string tupletType, int number, int divisions,
        string actualType = "", string normalType = "")
    {
        Fraction baseValue = _mult * baseScaling.Base;
        Fraction scaling = baseScaling.Scaling;
        Fraction a = new Fraction(divisions * 4L * fraction.Normal);
        Fraction b = new Fraction(1) / baseValue * new Fraction(fraction.Actual);
        Fraction duration = a / b * scaling;
        ChangeDivisionDuration(duration);
        _mult = new Fraction(fraction.Normal, fraction.Actual);
        ETreeElement timeModify = GetTimeModify();
        if (timeModify != null) { AdjustTimeModify(timeModify, fraction); }
        else { AddTimeModify(fraction); }

        if (string.IsNullOrEmpty(tupletType)) { return; }

        AddNotations();
        if (!string.IsNullOrEmpty(actualType) && tupletType != "stop")
        {
            AddTupletType(number, tupletType, fraction.Actual, actualType, fraction.Normal, normalType);
        }
        else
        {
            AddTupletType(number, tupletType);
        }
    }

    /// <summary>Ties the note being written, for sound and for notation.</summary>
    /// <param name="tieType">Whether the tie starts or stops.</param>
    public void TieNote(string tieType)
    {
        AddTie(tieType);
        AddNotations();
        AddTied(tieType);
    }

    /// <summary>Creates every node a rest needs.</summary>
    /// <param name="duration">Its duration in divisions.</param>
    /// <param name="durationType">Its written duration, or null.</param>
    /// <param name="position">Where on the staff it sits, or null.</param>
    /// <param name="dot">How many dots it has.</param>
    /// <param name="voice">Its voice number.</param>
    public void NewRest(
        int duration, string durationType, (string Step, int Octave)? position, int dot, int voice)
    {
        CreateNote();
        if (position != null) { AddRestWithPosition(position.Value.Step, position.Value.Octave); }
        else { AddRest(); }

        AddDivisionDuration(duration);
        AddVoice(voice);
        if (!string.IsNullOrEmpty(durationType)) { AddDurationType(durationType); }

        for (int i = 0; i < dot; i++) { AddDot(); }
    }

    /// <summary>Adds an articulation to the note being written.</summary>
    /// <param name="articulation">Its element name.</param>
    public void NewArticulation(string articulation)
    {
        AddNotations();
        AddArticulations();
        AddNamedArticulation(articulation);
    }

    /// <summary>Adds a simple ornament to the note being written.</summary>
    /// <param name="ornament">Which one: <c>trill</c>, <c>turn</c>, <c>mordent</c>, <c>prall</c>.</param>
    public void NewSimpleOrnament(string ornament)
    {
        AddNotations();
        AddOrnaments();

        //Upstream dispatches with getattr(self, 'add_'+ornament), so the set is
        //exactly the four add_ methods that name an ornament.
        switch (ornament)
        {
            case "trill": AddTrill(); break;
            case "turn": AddTurn(); break;
            case "mordent": AddMordent(); break;
            case "prall": AddPrall(); break;
            default:
                throw new ArgumentException(
                    "There is no ornament called '" + ornament + "'.", nameof(ornament));
        }
    }

    /// <summary>Adds an ornament that carries arguments.</summary>
    /// <param name="ornament">Which one; only <c>wavy-line</c> is understood.</param>
    /// <param name="type">Whether it starts or stops.</param>
    public void NewAdvancedOrnament(string ornament, string type)
    {
        AddNotations();
        AddOrnaments();
        if (ornament == "wavy-line") { AddWavyLine(type); }
    }

    /// <summary>Creates the bar's attributes element and fills it in.</summary>
    /// <param name="request">What the bar sets.</param>
    public void NewBarAttribute(BarAttributesRequest request)
    {
        CreateBarAttribute();
        if (request == null) { return; }

        if (request.Divisions != 0) { AddDivisions(request.Divisions); }

        if (request.Key != null) { AddKey(request.Key.Value, request.Mode); }

        if (request.Time != null) { AddTime(request.Time); }

        if (request.Clef != null)
        {
            AddClef(request.Clef.Sign, request.Clef.Line, octaveChange: request.Clef.OctaveChange);
        }

        if (request.MultiRest != 0) { AddBarStyle(request.MultiRest); }
    }

    /// <summary>Creates a tempo direction.</summary>
    /// <param name="words">The words shown, or null.</param>
    /// <param name="metronome">The beat unit and the beats per minute, or null.</param>
    /// <param name="sound">The MIDI tempo.</param>
    /// <param name="dots">How many dots the beat unit carries.</param>
    public void CreateTempo(
        string words, (string Unit, string Beats)? metronome, double sound, int dots)
    {
        AddDirection();
        if (!string.IsNullOrEmpty(words)) { AddDirectionWords(words); }

        if (metronome == null) { return; }

        AddMetronomeDirection(metronome.Value.Unit, metronome.Value.Beats, dots);
        AddSoundDirection(sound);
    }

    /// <summary>Creates an element the rest of this class does not cover.</summary>
    /// <param name="parent">Where to put it.</param>
    /// <param name="name">Its element name.</param>
    /// <param name="text">Its text.</param>
    public void CreateNewNode(ETreeElement parent, string name, string text)
        => parent.SubElement(name).Text = text;

    // ================= Low-level node creation =================

    /// <summary>Adds a creator to the score information.</summary>
    /// <param name="creator">What kind of creator.</param>
    /// <param name="name">Their name.</param>
    public void AddCreator(string creator, string name)
        => CreateScoreInfo("creator", name, ("type", creator));

    /// <summary>Adds a rights statement to the score information.</summary>
    /// <param name="rights">The statement.</param>
    /// <param name="type">What kind of rights, or null.</param>
    public void AddRights(string rights, string type = null)
    {
        if (string.IsNullOrEmpty(type)) { CreateScoreInfo("rights", rights); }
        else { CreateScoreInfo("rights", rights, ("type", type)); }
    }

    /// <summary>Starts a new note and forgets the last one's lazy children.</summary>
    public void CreateNote()
    {
        _currentNote = _currentBar.SubElement("note");
        _currentNotation = null;
        _currentArticulation = null;
        _currentOrnament = null;
        _currentTechnical = null;
    }

    /// <summary>Adds the note's pitch.</summary>
    /// <param name="step">Its note name.</param>
    /// <param name="alter">Its accidental, in semitones.</param>
    /// <param name="octave">Its octave.</param>
    public void AddPitch(string step, double alter, int octave)
    {
        ETreeElement pitch = _currentNote.SubElement("pitch");
        pitch.SubElement("step").Text = step;
        if (alter != 0) { pitch.SubElement("alter").Text = Str(alter); }

        pitch.SubElement("octave").Text = Str(octave);
    }

    /// <summary>Adds an unpitched note's display position.</summary>
    /// <param name="step">Its display note name.</param>
    /// <param name="octave">Its display octave.</param>
    public void AddUnpitched(string step, int octave)
    {
        ETreeElement unpitched = _currentNote.SubElement("unpitched");
        unpitched.SubElement("display-step").Text = step;
        unpitched.SubElement("display-octave").Text = Str(octave);
    }

    /// <summary>Adds the note's accidental.</summary>
    /// <param name="alter">The accidental, in semitones.</param>
    /// <param name="cautionary">Whether it is cautionary.</param>
    /// <param name="parentheses">Whether it is bracketed.</param>
    public void AddAccidental(double alter, bool cautionary = false, bool parentheses = false)
    {
        var attributes = new List<(string, string)>();
        if (cautionary) { attributes.Add(("cautionary", "yes")); }

        if (parentheses) { attributes.Add(("parentheses", "yes")); }

        ETreeElement accidental = _currentNote.SubElement("accidental", attributes.ToArray());
        accidental.Text = AccidentalNames[alter];
    }

    /// <summary>Sets the note's stem direction.</summary>
    /// <param name="direction">Which way.</param>
    public void SetStemDirection(string direction)
        => _currentNote.SubElement("stem").Text = direction;

    /// <summary>Makes the note a rest.</summary>
    public void AddRest() => _currentNote.SubElement("rest");

    /// <summary>Makes the note a rest at a staff position.</summary>
    /// <param name="step">Its display note name.</param>
    /// <param name="octave">Its display octave.</param>
    public void AddRestWithPosition(string step, int octave)
    {
        ETreeElement rest = _currentNote.SubElement("rest");
        ETreeElement stepNode = rest.SubElement("display-step");
        ETreeElement octaveNode = rest.SubElement("display-octave");
        stepNode.Text = step;
        octaveNode.Text = Str(octave);
    }

    /// <summary>Moves the writing position without writing a note.</summary>
    /// <param name="duration">How far, in divisions.</param>
    /// <param name="forward">Forward when true, backward when false.</param>
    public void AddSkip(int duration, bool forward = true)
    {
        ETreeElement skip = _currentBar.SubElement(forward ? "forward" : "backward");
        skip.SubElement("duration").Text = Str(duration);
    }

    /// <summary>Adds the note's duration in divisions.</summary>
    /// <param name="divisionDuration">The duration.</param>
    public void AddDivisionDuration(int divisionDuration)
    {
        _duration = _currentNote.SubElement("duration");
        _duration.Text = Str(divisionDuration);
        _mult = new Fraction(1);
    }

    /// <summary>Rewrites the duration already added, for a tuplet.</summary>
    /// <param name="duration">The new duration.</param>
    public void ChangeDivisionDuration(Fraction duration) => _duration.Text = duration.ToString();

    /// <summary>Adds the note's written duration.</summary>
    /// <param name="durationType">The type, such as <c>quarter</c>.</param>
    public void AddDurationType(string durationType)
        => _currentNote.SubElement("type").Text = durationType;

    /// <summary>Adds one augmentation dot.</summary>
    public void AddDot() => _currentNote.SubElement("dot");

    /// <summary>Adds a beam to the note's notations.</summary>
    /// <param name="number">Which beam.</param>
    /// <param name="beamType">What it does.</param>
    public void AddBeam(int number, string beamType)
        => _currentNotation.SubElement("beam", ("number", Str(number))).Text = beamType;

    /// <summary>Adds a tie, which is the SOUND of a tie.</summary>
    /// <param name="tieType">Whether it starts or stops.</param>
    /// <remarks>A tie must come directly after the duration, so it is inserted.</remarks>
    public void AddTie(string tieType)
    {
        int insertAt = _currentNote.IndexOfTag("duration") + 1;
        var tie = new ETreeElement("tie");
        tie.Set("type", tieType);
        _currentNote.Insert(insertAt, tie);
    }

    /// <summary>Makes the note a grace note.</summary>
    /// <param name="slash">Whether the grace is slashed.</param>
    public void AddGrace(bool slash)
    {
        if (slash) { _currentNote.SubElement("grace", ("slash", "yes")); }
        else { _currentNote.SubElement("grace"); }
    }

    /// <summary>Creates the note's notations element, at most once.</summary>
    public void AddNotations()
        => _currentNotation ??= _currentNote.SubElement("notations");

    /// <summary>Adds a tied, which is the NOTATION of a tie.</summary>
    /// <param name="tieType">Whether it starts or stops.</param>
    public void AddTied(string tieType)
        => _currentNotation.SubElement("tied", ("type", tieType));

    /// <summary>Adds a time modification for a tuplet.</summary>
    /// <param name="fraction">The ratio, actual over normal.</param>
    /// <remarks>
    /// The position matters and upstream computes it by looking for the last of
    /// the elements that must come before it — the accidental, else a dot, else
    /// the type — and inserting after that.
    /// </remarks>
    public void AddTimeModify((int Actual, int Normal) fraction)
    {
        int index = _currentNote.IndexOfTag("accidental");
        if (index == -1) { index = _currentNote.IndexOfTag("dot"); }

        if (index == -1) { index = _currentNote.IndexOfTag("type"); }

        var node = new ETreeElement("time-modification");
        node.SubElement("actual-notes").Text = Str(fraction.Actual);
        node.SubElement("normal-notes").Text = Str(fraction.Normal);
        _currentNote.Insert(index + 1, node);
    }

    /// <summary>Returns the note's time modification, or null when it has none.</summary>
    /// <returns>The element, or null.</returns>
    public ETreeElement GetTimeModify() => _currentNote.FindDescendant("time-modification");

    /// <summary>Multiplies an existing time modification by another ratio.</summary>
    /// <param name="node">The element.</param>
    /// <param name="fraction">The ratio to apply.</param>
    public void AdjustTimeModify(ETreeElement node, (int Actual, int Normal) fraction)
    {
        ETreeElement actual = node.FindDescendant("actual-notes");
        actual.Text = Str(int.Parse(actual.Text, CultureInfo.InvariantCulture) * fraction.Actual);
        ETreeElement normal = node.FindDescendant("normal-notes");
        normal.Text = Str(int.Parse(normal.Text, CultureInfo.InvariantCulture) * fraction.Normal);
    }

    /// <summary>Adds a tuplet bracket to the note's notations.</summary>
    /// <param name="number">Which tuplet.</param>
    /// <param name="tupletType">What it does.</param>
    /// <param name="actualNumber">The actual count shown, or zero for none.</param>
    /// <param name="actualType">The actual note type shown; the note's own when empty.</param>
    /// <param name="normalNumber">The normal count shown, or zero for none.</param>
    /// <param name="normalType">The normal note type shown; the note's own when empty.</param>
    public void AddTupletType(
        int number, string tupletType, int actualNumber = 0, string actualType = "",
        int normalNumber = 0, string normalType = "")
    {
        ETreeElement tuplet = _currentNotation.SubElement(
            "tuplet", ("number", Str(number)), ("type", tupletType));
        if (actualNumber != 0)
        {
            ETreeElement actual = tuplet.SubElement("tuplet-actual");
            actual.SubElement("tuplet-number").Text = Str(actualNumber);
            actual.SubElement("tuplet-type").Text = string.IsNullOrEmpty(actualType)
                ? _currentNote.FindDescendant("type").Text
                : actualType;
        }

        if (normalNumber == 0) { return; }

        ETreeElement normal = tuplet.SubElement("tuplet-normal");
        normal.SubElement("tuplet-number").Text = Str(normalNumber);
        normal.SubElement("tuplet-type").Text = string.IsNullOrEmpty(normalType)
            ? _currentNote.FindDescendant("type").Text
            : normalType;
    }

    /// <summary>Adds a slur to the note's notations.</summary>
    /// <param name="number">Which slur.</param>
    /// <param name="slurType">What it does.</param>
    public void AddSlur(int number, string slurType)
    {
        AddNotations();
        _currentNotation.SubElement("slur", ("number", Str(number)), ("type", slurType));
    }

    /// <summary>Adds a notation that is nothing but its name — a fermata, say.</summary>
    /// <param name="notation">Its element name.</param>
    public void AddNamedNotation(string notation)
    {
        AddNotations();
        _currentNotation.SubElement(notation);
    }

    /// <summary>Creates the notations' articulations element, at most once.</summary>
    public void AddArticulations()
        => _currentArticulation ??= _currentNotation.SubElement("articulations");

    /// <summary>Adds an articulation that is nothing but its name.</summary>
    /// <param name="articulation">Its element name.</param>
    public void AddNamedArticulation(string articulation)
        => _currentArticulation.SubElement(articulation);

    /// <summary>Creates the notations' ornaments element, at most once.</summary>
    public void AddOrnaments()
    {
        if (_currentOrnament != null) { return; }

        AddNotations();
        _currentOrnament = _currentNotation.SubElement("ornaments");
    }

    /// <summary>Adds a tremolo.</summary>
    /// <param name="tremoloType">What it does.</param>
    /// <param name="lines">How many beams it is drawn with.</param>
    public void AddTremolo(string tremoloType, int lines)
    {
        AddOrnaments();
        _currentOrnament.SubElement("tremolo", ("type", tremoloType)).Text = Str(lines);
    }

    /// <summary>Adds a trill mark.</summary>
    public void AddTrill() => _currentOrnament.SubElement("trill-mark");

    /// <summary>Adds a turn.</summary>
    public void AddTurn() => _currentOrnament.SubElement("turn");

    /// <summary>Adds a mordent.</summary>
    public void AddMordent() => _currentOrnament.SubElement("mordent");

    /// <summary>Adds an inverted mordent, which LilyPond calls a prall.</summary>
    public void AddPrall() => _currentOrnament.SubElement("inverted-mordent");

    /// <summary>Adds a wavy line.</summary>
    /// <param name="endType">What it does.</param>
    /// <remarks>
    /// ⚠ Upstream's first line here is <c>self.add_ornaments</c> — the METHOD,
    /// not a call to it — so it does nothing at all. It is harmless because
    /// every caller reaches this through <c>new_adv_ornament</c>, which has
    /// already created the ornaments element; the port leaves the ornaments
    /// element to the caller in the same way rather than quietly adding a call
    /// upstream does not make.
    /// </remarks>
    public void AddWavyLine(string endType)
        => _currentOrnament.SubElement("wavy-line", ("type", endType));

    /// <summary>Adds a glissando to the note's notations.</summary>
    /// <param name="lineType">How the line is drawn.</param>
    /// <param name="endType">What it does.</param>
    /// <param name="number">Which glissando.</param>
    public void AddGlissando(string lineType, string endType, int number)
    {
        AddNotations();
        _currentNotation.SubElement(
            "glissando", ("line-type", lineType), ("number", Str(number)), ("type", endType));
    }

    /// <summary>Creates the notations' technical element, at most once.</summary>
    public void AddTechnical()
    {
        if (_currentTechnical != null) { return; }

        AddNotations();
        _currentTechnical = _currentNotation.SubElement("technical");
    }

    /// <summary>Adds a fingering.</summary>
    /// <param name="finger">Which finger.</param>
    public void AddFingering(int finger)
    {
        AddTechnical();
        _currentTechnical.SubElement("fingering").Text = Str(finger);
    }

    /// <summary>Creates the bar's attributes element.</summary>
    public void CreateBarAttribute() => _barAttributes = _currentBar.SubElement("attributes");

    /// <summary>Adds the divisions per quarter note.</summary>
    /// <param name="divisions">The divisions.</param>
    public void AddDivisions(int divisions)
        => _barAttributes.SubElement("divisions").Text = Str(divisions);

    /// <summary>Adds the key signature.</summary>
    /// <param name="key">How many sharps, or flats when negative.</param>
    /// <param name="mode">The mode's name.</param>
    public void AddKey(int key, string mode)
    {
        ETreeElement node = _barAttributes.SubElement("key");
        node.SubElement("fifths").Text = Str(key);
        node.SubElement("mode").Text = mode;
    }

    /// <summary>Adds the time signature.</summary>
    /// <param name="time">The beats, the beat type, and a symbol when there is one.</param>
    public void AddTime(TimeSignature time)
    {
        ETreeElement node = string.IsNullOrEmpty(time.Symbol)
            ? _barAttributes.SubElement("time")
            : _barAttributes.SubElement("time", ("symbol", time.Symbol));
        node.SubElement("beats").Text = time.Beats;
        node.SubElement("beat-type").Text = time.BeatType;
    }

    /// <summary>Adds a clef.</summary>
    /// <param name="sign">Its letter.</param>
    /// <param name="line">Which staff line it sits on, or zero for none.</param>
    /// <param name="number">Which staff it is for, or null for none.</param>
    /// <param name="octaveChange">How many octaves it transposes, or zero.</param>
    public void AddClef(string sign, int line, string number = null, int octaveChange = 0)
    {
        //⚠ `number` is a staff-number: a POSITIVE INTEGER (ruling FR15), and the
        //same unresolved-context-identifier that reaches AddStaff reaches here.
        //`\new Staff = down` gives the string "down", and `<clef number="down">`
        //is a document no reader can parse. Omitting the attribute says the clef
        //belongs to the part rather than to a numbered staff, which is
        //imprecise but readable; writing the identifier is neither.
        ////was previously: the string was written verbatim whenever it was not
        //empty.
        bool numbered = int.TryParse(
                number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int staffNumber)
            && staffNumber >= 1;

        ETreeElement clef = numbered
            ? _barAttributes.SubElement("clef", ("number", Str(staffNumber)))
            : _barAttributes.SubElement("clef");
        clef.SubElement("sign").Text = sign;
        if (line != 0) { clef.SubElement("line").Text = Str(line); }

        if (octaveChange != 0) { clef.SubElement("clef-octave-change").Text = Str(octaveChange); }
    }

    /// <summary>Adds a measure style, which here means a multiple rest.</summary>
    /// <param name="multiRest">How many bars it lasts, or zero for none.</param>
    public void AddBarStyle(int multiRest)
    {
        ETreeElement style = _barAttributes.SubElement("measure-style");
        if (multiRest != 0) { style.SubElement("multiple-rest").Text = Str(multiRest); }
    }

    /// <summary>Asks for a system break at this bar.</summary>
    /// <param name="forceBreak">Whether to break.</param>
    public void NewSystem(string forceBreak)
        => _currentBar.SubElement("print", ("new-system", forceBreak));

    /// <summary>Adds a bar line at the end of the bar.</summary>
    /// <param name="barlineType">Its style.</param>
    /// <param name="repeat">Which way a repeat faces, or null for no repeat.</param>
    public void AddBarline(string barlineType, string repeat = null)
    {
        ETreeElement node = _currentBar.SubElement("barline", ("location", "right"));
        node.SubElement("bar-style").Text = barlineType;
        if (!string.IsNullOrEmpty(repeat)) { node.SubElement("repeat", ("direction", repeat)); }
    }

    /// <summary>Moves the writing position back, so another voice can be written.</summary>
    /// <param name="duration">How far, in divisions. Nothing happens when it is not positive.</param>
    public void AddBackup(int duration)
    {
        if (duration <= 0) { return; }

        ETreeElement node = _currentBar.SubElement("backup");
        node.SubElement("duration").Text = Str(duration);
    }

    /// <summary>Adds the note's voice number.</summary>
    /// <param name="voice">The voice.</param>
    public void AddVoice(int voice) => _currentNote.SubElement("voice").Text = Str(voice);

    /// <summary>Adds the note's staff number, if it has a usable one.</summary>
    /// <param name="staff">The staff.</param>
    /// <remarks>
    /// <para>
    /// ⚠ TWO THINGS THE SCHEMA REQUIRES HERE, and upstream honours neither
    /// (ruling FR15).
    /// </para>
    /// <para>
    /// FIRST, WHERE IT GOES. The note's sequence puts <c>staff</c> BEFORE
    /// <c>beam</c>, <c>notations</c> and <c>lyric</c> — and by the time anything
    /// calls this, the notations and the lyrics are already on the note, because
    /// <c>IterateXmlObjs</c> writes the note and only then asks for the staff.
    /// So it is INSERTED before the first of them.
    /// //was previously: appended, which put it last and made every note in a
    /// multi-staff part invalid.
    /// </para>
    /// <para>
    /// SECOND, WHAT IT MAY SAY. <c>staff</c> is a positive integer. It normally
    /// holds one — but a <c>\change Staff = "x"</c> naming a context that has
    /// not been seen yet leaves the CONTEXT IDENTIFIER here instead, and if
    /// nothing ever resolves it, that string reaches the file and no reader can
    /// parse the document. An unresolved staff is not knowledge we have, so the
    /// element is OMITTED: a note with no staff element is on staff 1, which is
    /// the standard's own default and the honest answer.
    /// //was previously: str(staff), which wrote the identifier verbatim.
    /// </para>
    /// </remarks>
    public void AddStaff(string staff)
    {
        if (!int.TryParse(staff, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)
            || number < 1)
        {
            return;
        }

        var node = new ETreeElement("staff") { Text = Str(number) };
        int at = -1;
        foreach (string tag in AfterStaff)
        {
            int i = _currentNote.IndexOfTag(tag);
            if (i >= 0 && (at < 0 || i < at)) { at = i; }
        }

        if (at < 0) { _currentNote.Append(node); }
        else { _currentNote.Insert(at, node); }
    }

    //What the note's sequence puts AFTER <staff>; the staff goes before the
    //earliest of these that is already on the note.
    private static readonly string[] AfterStaff =
    {
        "beam", "notations", "lyric", "play", "listen",
    };

    /// <summary>Says how many staves the part has.</summary>
    /// <param name="staves">The count.</param>
    public void AddStaves(int staves)
    {
        int index = _barAttributes.IndexOfTag("time");
        var node = new ETreeElement("staves") { Text = Str(staves) };
        _barAttributes.Insert(index + 1, node);
    }

    /// <summary>Marks the note as belonging to the chord before it.</summary>
    public void AddChord() => _currentNote.SubElement("chord");

    /// <summary>Starts a direction and makes it the one being written.</summary>
    /// <param name="placement">Where it goes.</param>
    public void AddDirection(string placement = "above")
        => _direction = _currentBar.SubElement("direction", ("placement", placement));

    /// <summary>Adds a dynamic mark below the staff.</summary>
    /// <param name="dynamic">Its element name.</param>
    public void AddDynamicMark(string dynamic)
    {
        ETreeElement direction = _currentBar.SubElement("direction", ("placement", "below"));
        ETreeElement type = direction.SubElement("direction-type");
        type.SubElement("dynamics").SubElement(dynamic);
    }

    /// <summary>Adds a hairpin below the staff.</summary>
    /// <param name="wedgeType">What it does.</param>
    public void AddDynamicWedge(string wedgeType)
    {
        ETreeElement direction = _currentBar.SubElement("direction", ("placement", "below"));
        direction.SubElement("direction-type").SubElement("wedge", ("type", wedgeType));
    }

    /// <summary>Adds a dynamic written as words, below the staff.</summary>
    /// <param name="text">The words.</param>
    public void AddDynamicText(string text)
    {
        ETreeElement direction = _currentBar.SubElement("direction", ("placement", "below"));
        ETreeElement type = direction.SubElement("direction-type");
        ETreeElement words = type.SubElement("words");
        words.Set("font-style", "italic");
        words.Text = text;
    }

    /// <summary>Adds a dashed line under a dynamic instruction.</summary>
    /// <param name="text">What it does.</param>
    public void AddDynamicDashes(string text)
    {
        ETreeElement direction = _currentBar.SubElement("direction", ("placement", "below"));
        direction.SubElement("direction-type").SubElement("dashes", ("type", text));
    }

    /// <summary>Adds an octave shift.</summary>
    /// <param name="placement">Where it goes.</param>
    /// <param name="octaveDirection">What it does.</param>
    /// <param name="size">How many notes it spans.</param>
    public void AddOctaveShift(string placement, string octaveDirection, int size)
    {
        ETreeElement direction = _currentBar.SubElement("direction", ("placement", placement));
        direction.SubElement("direction-type")
            .SubElement("octave-shift", ("type", octaveDirection), ("size", Str(size)));
    }

    /// <summary>Adds words to the direction being written — a tempo mark, say.</summary>
    /// <param name="words">The words.</param>
    public void AddDirectionWords(string words)
    {
        if (_currentBar.FindDescendant("direction") == null) { AddDirection(); }

        _direction.SubElement("direction-type").SubElement("words").Text = words;
    }

    /// <summary>Adds a rehearsal mark to the direction being written.</summary>
    /// <param name="mark">The mark.</param>
    public void AddMark(string mark)
    {
        if (_currentBar.FindDescendant("direction") == null) { AddDirection(); }

        _direction.SubElement("direction-type").SubElement("rehearsal").Text = mark;
    }

    /// <summary>Adds a metronome mark to the direction being written.</summary>
    /// <param name="unit">The beat unit.</param>
    /// <param name="beats">
    /// Beats per minute, as TEXT: it is written out verbatim, so the token the
    /// document typed is what a reader sees.
    /// </param>
    /// <param name="dots">How many dots the beat unit carries.</param>
    public void AddMetronomeDirection(string unit, string beats, int dots)
    {
        ETreeElement type = _direction.SubElement("direction-type");
        ETreeElement metronome = type.SubElement("metronome");
        metronome.SubElement("beat-unit").Text = unit;
        for (int i = 0; i < dots; i++) { metronome.SubElement("beat-unit-dot"); }

        metronome.SubElement("per-minute").Text = beats;
    }

    /// <summary>Adds the playback tempo to the direction being written.</summary>
    /// <param name="midiTempo">The tempo.</param>
    /// <remarks>
    /// Upstream truncates to a whole number here, with the comment "remove the
    /// int conversion once LilyPond accepts decimal tempo". Kept: it is a
    /// deliberate accommodation and not a defect.
    /// </remarks>
    public void AddSoundDirection(double midiTempo)
        => _direction.SubElement("sound", ("tempo", Str((int)midiTempo)));

    /// <summary>Adds a syllable of lyrics to the note being written.</summary>
    /// <param name="text">The syllable.</param>
    /// <param name="syllabic">Where in its word it sits.</param>
    /// <param name="number">Which verse.</param>
    /// <param name="extend">Whether a melisma line follows it.</param>
    public void AddLyric(string text, string syllabic, int number, bool extend = false)
    {
        ETreeElement lyric = _currentNote.SubElement("lyric", ("number", Str(number)));
        lyric.SubElement("syllabic").Text = syllabic;
        lyric.SubElement("text").Text = text;
        if (extend) { lyric.SubElement("extend"); }
    }

    // ================= Create the XML document =================

    /// <summary>Finishes the document.</summary>
    /// <param name="prettyPrint">Whether to indent it.</param>
    /// <returns>The document.</returns>
    public MusicXmlDocument MusicXml(bool prettyPrint = true)
    {
        var document = new MusicXmlDocument(Root);
        if (prettyPrint) { document.Indent("  "); }

        return document;
    }

    //Upstream's acc_dict, keyed by the alteration in semitones.
    private static readonly Dictionary<double, string> AccidentalNames
        = new Dictionary<double, string>
        {
            [0] = "natural",
            [1] = "sharp",
            [-1] = "flat",
            [2] = "sharp-sharp",
            [-2] = "flat-flat",
            [0.5] = "natural-up",
            [-0.5] = "natural-down",
            [1.5] = "sharp-up",
            [-1.5] = "flat-down",
        };

    private static string Str(int value) => ETreeUtil.Str(value);

    private static string Str(double value) => ETreeUtil.Str(value);
}
