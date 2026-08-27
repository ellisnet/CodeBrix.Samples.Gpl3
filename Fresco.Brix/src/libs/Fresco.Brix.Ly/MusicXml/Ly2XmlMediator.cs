// === python-ly ly.musicxml.ly2xml_mediator module (class Mediator) ===
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
using LyNote = Fresco.Brix.Ly.Music.Note;
using LyPitch = Fresco.Brix.Ly.Pitching.Pitch;

namespace Fresco.Brix.Ly.MusicXml; //was previously: ly/musicxml/ly2xml_mediator.py (class Mediator)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Stands between the LilyPond source parser and the MusicXML object model.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>Mediator</c>. <see cref="ParseSource"/> walks the
/// <c>ly.music</c> tree and calls the methods here; each one puts what it was
/// told into the <see cref="Score"/> being built. Nothing here reads the source
/// and nothing here writes XML — that is the whole point of the class, and it
/// is why a note can be described once and written twice (as sound and as
/// notation) without the parser knowing about either.
/// </para>
/// <para>
/// ⚠ Five of upstream's defects are FIXED across this port under ruling FR14
/// and each is commented at its site. The oracle the parity tests replay was
/// generated with the same five applied to python-ly in memory, so the fixtures
/// answer "what upstream produces with these fixed" — which is exactly what this
/// port is required to produce.
/// </para>
/// </remarks>
public sealed class Ly2XmlMediator
{
    private readonly List<Action<BarNote>> _actionOnNext = new List<Action<BarNote>>();
    private readonly List<ScoreSection> _sections = new List<ScoreSection>();
    private readonly Dictionary<string, string> _staffIdDict
        = new Dictionary<string, string>(StringComparer.Ordinal);
    private readonly Dictionary<string, List<BarMus>> _staffUnsetNotes
        = new Dictionary<string, List<BarMus>>(StringComparer.Ordinal);
    private readonly Dictionary<string, LyricsSection> _lyricSections
        = new Dictionary<string, LyricsSection>(StringComparer.Ordinal);
    private readonly List<BarNote> _currentChord = new List<BarNote>();
    private readonly List<Slur> _slurStack = new List<Slur>();

    private List<BarNote> _qChord = new List<BarNote>();
    private BarAttr _currentAttr;
    private Bar _bar;
    private BarMus _currentNote;
    //The DURABLE being described and the PITCH it is drawn at, kept apart.
    //⚠ Upstream keeps one field, `current_lynote`, and for a bare duration it
    //ASSIGNS A PITCH ATTRIBUTE ONTO THE DURATION OBJECT at runtime
    //(`note.pitch = self.current_lynote.pitch` in new_iso_dura) — a python
    //object grows a field and everything downstream reads it as though it had
    //always been a note. Nothing in C# can do that, and pretending otherwise is
    //how `a1 ~ | 1 |` lost its second bar: the cast failed, the walk swallowed
    //it as "Unpitched not implemented", and a whole note disappeared.
    private Music.Durable _currentLyNote;
    private LyPitch _currentPitch;
    private bool _currentIsRest;
    private Fraction _currentTime = new Fraction(4, 4);
    private Fraction _barDura = new Fraction(0, 4);
    private string _durToken = "4";
    private IReadOnlyList<string> _durTokens = Array.Empty<string>();
    private int _dots;
    private bool _tied;
    private int _voice = 1;
    private string _staff = string.Empty;
    private ScorePart _part;
    private ScorePartGroup _group;
    private int _groupNum;
    private LyPitch _prevPitch;
    private LyPitch _prevChordPitch;
    private int _storeVoiceNr;
    private bool _storeUnsetStaff;
    private LyricEntry _lyric;
    private bool _lyricSyll;
    private int _lyricNr = 1;
    private bool _ongoingWedge;
    private bool _ongoingDashes;
    private int _octDiff;
    private int _prevTremolo = 8;
    private Fraction _tuplDur = Fraction.Zero;
    private Fraction _tuplSum = Fraction.Zero;
    private bool _multipleRest;
    private int _currentMark = 1;
    private bool _barIsPickup;
    private string _stemDir;
    private ClefSignature _clef;

    /// <summary>Gets the score being built.</summary>
    public Score Score { get; } = new Score();

    /// <summary>Gets or sets how many divisions there are to a quarter note.</summary>
    public int Divisions { get; set; } = 1;

    /// <summary>Gets or sets where new bars are appended.</summary>
    public ScoreSection InsertInto { get; set; }

    /// <summary>Gets or sets where warnings go, or null to drop them.</summary>
    public Action<string> Warn { get; set; }

    /// <summary>Gets the note or rest being described.</summary>
    public BarMus CurrentNote => _currentNote;

    /// <summary>Gets the notes of the chord being described.</summary>
    public IReadOnlyList<BarNote> CurrentChord => _currentChord;

    /// <summary>Gets whether a bar has been started.</summary>
    public bool HasBar => _bar != null;

    // ================= sections, parts and groups =================

    /// <summary>Files a header assignment where it belongs.</summary>
    /// <param name="name">The variable's name.</param>
    /// <param name="value">Its value.</param>
    public void NewHeaderAssignment(string name, string value)
    {
        if (name == "title") { Score.Title = value; }
        else if (name == "copyright" || name == "tagline") { Score.AddRight(value, name); }
        else if (Array.IndexOf(CreatorNames, name) >= 0) { Score.Creators[name] = value; }
        else { Score.Info[name] = value; }
    }

    /// <summary>Starts a new section and makes it the one being filled.</summary>
    /// <param name="name">Its name; a free one is found when it is taken.</param>
    /// <param name="global">Whether it holds what is true of the whole score.</param>
    public void NewSection(string name, bool @global = false)
    {
        var section = new ScoreSection(CheckName(name), @global);
        InsertInto = section;
        _sections.Add(section);
        _bar = null;
    }

    /// <summary>Starts a snippet, remembering where it is destined for.</summary>
    /// <param name="name">Its name.</param>
    public void NewSnippet(string name)
    {
        var snippet = new Snippet(CheckName(name), InsertInto?.BarList);
        InsertInto = snippet;
        _sections.Add(snippet);
        _bar = null;
    }

    /// <summary>Starts a lyrics section.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="voiceId">The voice its syllables belong to.</param>
    public void NewLyricSection(string name, string voiceId)
    {
        string checkedName = CheckName(name);
        var lyrics = new LyricsSection(checkedName, voiceId);
        InsertInto = lyrics;
        _lyricSections[checkedName] = lyrics;
    }

    /// <summary>Finds a name nothing is using yet, adding a number if need be.</summary>
    /// <param name="name">The wanted name.</param>
    /// <param name="nr">The number to try first.</param>
    /// <returns>The free name.</returns>
    public string CheckName(string name, int nr = 1)
    {
        if (GetVarByName(name) == null) { return name; }

        return CheckName(name + nr.ToString(CultureInfo.InvariantCulture), nr + 1);
    }

    /// <summary>Finds a section by name.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The section, or null.</returns>
    public ScoreSection GetVarByName(string name)
    {
        foreach (ScoreSection section in _sections)
        {
            if (string.Equals(section.Name, name, StringComparison.Ordinal)) { return section; }
        }

        return null;
    }

    /// <summary>Starts a part group, nesting it inside the one already open.</summary>
    public void NewGroup()
    {
        ScorePartGroup parent = _group;
        _groupNum++;
        _group = new ScorePartGroup(_groupNum, "bracket");
        if (parent != null)
        {
            _group.Parent = parent;
            parent.PartList.Add(_group);
        }
        else
        {
            Score.PartList.Add(_group);
        }
    }

    /// <summary>Closes the open part group.</summary>
    public void CloseGroup() => _group = _group?.Parent;

    /// <summary>Sets the open group's bracket from a system start delimiter.</summary>
    /// <param name="systemStart">LilyPond's grob name.</param>
    public void ChangeGroupBracket(string systemStart)
        => _group?.SetBracket(Ly2XmlTranslations.GetGroupSymbol(systemStart));

    /// <summary>Starts a new part.</summary>
    /// <param name="partId">Its identifier, or null.</param>
    /// <param name="toPart">The part it is to be merged into, or null.</param>
    /// <param name="piano">Whether it is a two-staff piano part.</param>
    public void NewPart(string partId = null, ScorePart toPart = null, bool piano = false)
    {
        _part = piano
            ? new ScorePart(2, partId, toPart)
            : new ScorePart(partId: partId, toPart: toPart);
        if (toPart == null)
        {
            if (_group != null) { _group.PartList.Add(_part); }
            else { Score.PartList.Add(_part); }
        }

        InsertInto = _part;
        _bar = null;
    }

    /// <summary>Gets whether a part has been started and has anything in it.</summary>
    /// <returns>True when it has.</returns>
    public bool PartNotEmpty() => _part != null && _part.BarList.Count > 0;

    /// <summary>Finds a part by its identifier.</summary>
    /// <param name="partId">The identifier.</param>
    /// <param name="partHolder">Where to look; the score by default.</param>
    /// <returns>The part, or null.</returns>
    /// <remarks>
    /// ⚠ Upstream returns the LAST match rather than the first, because its loop
    /// keeps assigning to <c>ret</c> instead of returning; and a nested group
    /// that finds nothing OVERWRITES a part found earlier with False. Both are
    /// kept — a document with two parts under one identifier is already
    /// ambiguous, and changing which one wins would change output for no stated
    /// reason.
    /// </remarks>
    public ScorePart GetPartById(string partId, object partHolder = null)
    {
        IReadOnlyList<object> list = partHolder switch
        {
            null => Score.PartList,
            ScorePartGroup group => group.PartList,
            Score score => score.PartList,
            _ => Array.Empty<object>(),
        };

        ScorePart result = null;
        foreach (object part in list)
        {
            if (part is ScorePartGroup nested) { result = GetPartById(partId, nested); }
            else if (part is ScorePart scorePart
                && string.Equals(scorePart.PartId, partId, StringComparison.Ordinal))
            {
                result = scorePart;
            }
        }

        return result;
    }

    /// <summary>Sets which voice the notes that follow belong to.</summary>
    /// <param name="command">A <c>voiceOne</c>-style name, or null.</param>
    /// <param name="add">Whether to take the next voice number.</param>
    /// <param name="nr">An explicit number, or zero.</param>
    /// <param name="piano">How many piano staves are open.</param>
    public void SetVoiceNr(string command = null, bool add = false, int nr = 0, int piano = 0)
    {
        if (add)
        {
            if (_storeVoiceNr == 0) { _storeVoiceNr = _voice; }

            _voice++;
        }
        else if (nr != 0)
        {
            _voice = nr;
        }
        else
        {
            _voice = Ly2XmlTranslations.GetVoice(command);
            if (piano > 2) { _voice += piano + 1; }
        }
    }

    /// <summary>Puts the voice number back to what it was.</summary>
    public void RevertVoiceNr()
    {
        _voice = _storeVoiceNr;
        _storeVoiceNr = 0;
    }

    /// <summary>Sets which staff the notes that follow are written on.</summary>
    /// <param name="staffNr">The staff number, or zero.</param>
    /// <param name="staffId">A context identifier, or null.</param>
    public void SetStaffNr(int staffNr, string staffId = null)
    {
        _storeUnsetStaff = false;
        if (staffNr != 0)
        {
            _staff = staffNr.ToString(CultureInfo.InvariantCulture);
        }
        else if (!string.IsNullOrEmpty(staffId) && _staffIdDict.TryGetValue(staffId, out string known))
        {
            _staff = known;
        }
        else if (!string.IsNullOrEmpty(staffId))
        {
            //The identifier has not been seen yet, so the notes go out carrying
            //it and are corrected when it is.
            _storeUnsetStaff = true;
            _staff = staffId;
        }
    }

    /// <summary>Records which staff a context identifier names.</summary>
    /// <param name="staffId">The identifier.</param>
    public void AddStaffId(string staffId)
    {
        _storeUnsetStaff = false;
        if (string.IsNullOrEmpty(staffId)) { return; }

        if (_staffIdDict.TryGetValue(staffId, out string known)) { _staff = known; }
        else { _staffIdDict[staffId] = _staff; }

        if (!_staffUnsetNotes.TryGetValue(staffId, out List<BarMus> waiting)) { return; }

        foreach (BarMus note in waiting) { note.Staff = _staff; }
    }

    /// <summary>Adds a snippet's contents to the bars it was destined for.</summary>
    /// <param name="snippetName">The snippet's name.</param>
    public void AddSnippet(string snippetName)
    {
        if (GetVarByName(snippetName) is not Snippet snippet) { return; }

        ContinueBarList(snippet.MergeBarList);
        foreach (Bar bb in snippet.BarList)
        {
            foreach (BarObject b in bb.ObjList) { _bar.Add(b); }

            if (bb.ListFull) { NewBar(); }
        }
    }

    /// <summary>Merges the two most recent sections, dropping the empty ones.</summary>
    public void CheckVoices()
    {
        if (_sections.Count == 0) { return; }

        if (_sections[_sections.Count - 1].Global)
        {
            Score.MergeGlobally(_sections[_sections.Count - 1]);

            //⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14).
            //Upstream writes `self.section[-1]` here — SINGULAR — and the field
            //is `self.sections`, so this line raises AttributeError for every
            //global section that ever closes. ParseSource swallows it as
            //"Warning: End not implemented!", which means the global section is
            //never merged AND the whole block below never runs. `\new Devnull`
            //is the way to reach it. The plural is plainly what was meant: the
            //line above it reads the same list.
            Score.GlobalSection.MergeVoice(_sections[_sections.Count - 1]);
        }

        if (_sections.Count <= 2) { return; }

        if (_sections[_sections.Count - 2].BarList.Count == 0)
        {
            _sections.RemoveAt(_sections.Count - 2);
            CheckVoices();
        }
        else if (_sections[_sections.Count - 1].BarList.Count == 0)
        {
            _sections.RemoveAt(_sections.Count - 1);
            CheckVoices();
        }
        else
        {
            _sections[_sections.Count - 2].MergeVoice(_sections[_sections.Count - 1]);
            _sections.RemoveAt(_sections.Count - 1);
        }
    }

    /// <summary>Merges every snippet made since the voice number was stored.</summary>
    public void CheckVoicesByNr()
    {
        if (_sections.Count <= 2 || _voice <= 1) { return; }

        for (int n = _storeVoiceNr; n < _voice; n++) { CheckVoices(); }

        if (_sections.Count > 0 && _sections[_sections.Count - 1] is Snippet snippet)
        {
            AddSnippet(snippet.Name);
            _sections.RemoveAt(_sections.Count - 1);
        }
        else
        {
            Warn?.Invoke("WARNING: problem adding snippet!");
        }
    }

    /// <summary>Puts a finished lyrics section onto the voice it names.</summary>
    /// <param name="voiceId">The voice's identifier.</param>
    public void CheckLyrics(string voiceId)
    {
        if (_lyric != null && _lyric.Syllabic == "middle") { _lyric.Syllabic = "end"; }

        if (!_lyricSections.TryGetValue("lyricsto" + voiceId, out LyricsSection lyricsSection))
        {
            Warn?.Invoke("Warning can't merge in lyrics! (no such section)");
            return;
        }

        ScoreSection voiceSection = GetVarByName(lyricsSection.VoiceId);
        if (voiceSection != null) { voiceSection.MergeLyrics(lyricsSection); }
        else { Warn?.Invoke("Warning can't merge in lyrics!"); }
    }

    /// <summary>Adds the most recent section to the part and closes the part.</summary>
    public void CheckPart()
    {
        if (_sections.Count > 1)
        {
            if (Score.IsEmpty()) { NewPart(); }

            if (_sections[_sections.Count - 1].Global)
            {
                _part.MergeVoice(_sections[_sections.Count - 1]);
            }
            else
            {
                _part.BarList.AddRange(_sections[_sections.Count - 1].BarList);
                _sections.RemoveAt(_sections.Count - 1);
            }
        }

        if (_part == null) { return; }

        if (_part.ToPart != null) { _part.MergePartToPart(); }

        _part.MergeVoice(Score.GlobalSection);
        string name = CheckName("glob");
        Score.GlobalSection = _part.ExtractGlobalToSection(name);
        _part = null;
    }

    /// <summary>Merges the most recent section after a simultaneous block.</summary>
    public void CheckSimultan()
    {
        if (_sections.Count == 0) { return; }

        if (_part != null) { _part.MergeVoice(_sections[_sections.Count - 1]); }
        else if (_sections.Count > 1)
        {
            _sections[_sections.Count - 2].MergeVoice(_sections[_sections.Count - 1]);
        }

        _sections.RemoveAt(_sections.Count - 1);
    }

    /// <summary>
    /// Finishes the score: makes a part out of the first variable when nothing
    /// else made one, and applies the global section over everything.
    /// </summary>
    public void CheckScore()
    {
        if (Score.IsEmpty())
        {
            NewPart();
            List<Bar> first = GetFirstVar();
            if (first != null) { _part.BarList.AddRange(first); }
        }

        Score.MergeGlobally(Score.GlobalSection, @override: true);
    }

    /// <summary>Returns the bars of the first section made.</summary>
    /// <returns>The bars, or null when there are no sections.</returns>
    public List<Bar> GetFirstVar() => _sections.Count > 0 ? _sections[0].BarList : null;

    // ================= bars =================

    /// <summary>Says the next bar is an anacrusis.</summary>
    public void SetPickup() => _barIsPickup = true;

    /// <summary>Starts a new bar.</summary>
    /// <param name="fillPrevious">Whether the bar before it is now as full as it gets.</param>
    public void NewBar(bool fillPrevious = true)
    {
        if (_bar != null && fillPrevious) { _bar.ListFull = true; }

        _currentAttr = new BarAttr();
        _bar = new Bar();
        if (_barIsPickup)
        {
            _bar.Pickup = true;
            _barIsPickup = false;
        }

        _bar.ObjList.Add(_currentAttr);
        InsertInto?.BarList.Add(_bar);
    }

    /// <summary>Adds something to the bar, starting one when there is none.</summary>
    /// <param name="obj">The thing.</param>
    public void AddToBar(BarObject obj)
    {
        if (_bar == null) { NewBar(); }

        _bar.Add(obj);
    }

    /// <summary>Ends the bar with a bar line and starts the next.</summary>
    /// <param name="barline">LilyPond's bar line string.</param>
    public void CreateBarline(string barline)
    {
        var attr = new BarAttr();
        attr.SetBarline(barline);
        if (_bar == null) { NewBar(); }

        _bar.Add(attr);
        NewBar();
    }

    /// <summary>Adds a repeat bar line.</summary>
    /// <param name="rep">Which way it faces.</param>
    public void NewRepeat(string rep)
    {
        var attr = new BarAttr();
        attr.SetBarline(rep);
        attr.Repeat = rep;
        if (_bar == null) { NewBar(); }

        _bar.Add(attr);
    }

    /// <summary>Sets the key.</summary>
    /// <param name="keyName">The key's note name.</param>
    /// <param name="mode">The mode.</param>
    public void NewKey(string keyName, string mode)
    {
        if (_bar == null) { NewBar(); }

        if (_bar.HasMusic())
        {
            var attr = new BarAttr();
            attr.SetKey(Ly2XmlTranslations.GetFifths(keyName, mode), mode);
            AddToBar(attr);
        }
        else
        {
            _currentAttr.SetKey(Ly2XmlTranslations.GetFifths(keyName, mode), mode);
        }
    }

    /// <summary>Adds a rehearsal mark.</summary>
    /// <param name="numMark">Which mark, or null for the next one in sequence.</param>
    public void NewMark(int? numMark = null)
    {
        if (numMark == null)
        {
            if (_bar == null) { NewBar(); }

            if (_bar.HasAttr())
            {
                _currentAttr.SetMark(Ly2XmlTranslations.Bijective(_currentMark));
            }
            else
            {
                var attr = new BarAttr();
                attr.SetMark(Ly2XmlTranslations.Bijective(_currentMark));
                AddToBar(attr);
            }
        }
        else if (numMark <= 0)
        {
            Warn?.Invoke("Mark value out of range");
        }
        else
        {
            _currentMark = numMark.Value;
            _currentAttr.SetMark(Ly2XmlTranslations.Bijective(_currentMark));
        }

        _currentMark++;
    }

    /// <summary>Adds words over the bar.</summary>
    /// <param name="word">The words.</param>
    public void NewWord(string word)
    {
        if (_bar == null) { NewBar(); }

        if (_bar.HasAttr()) { _currentAttr.SetWord(word); }
        else
        {
            var attr = new BarAttr();
            attr.SetWord(word);
            AddToBar(attr);
        }
    }

    /// <summary>Sets the time signature.</summary>
    /// <param name="num">The beats.</param>
    /// <param name="den">The beat type, as a fraction whose denominator it is.</param>
    /// <param name="numeric">Whether it is written as numbers.</param>
    public void NewTime(int num, Fraction den, bool numeric = false)
    {
        _currentTime = new Fraction(num, den.Denominator);
        if (_bar == null) { NewBar(); }

        _currentAttr.SetTime(
            new TimeSignature(
                num.ToString(CultureInfo.InvariantCulture),
                den.Denominator.ToString(CultureInfo.InvariantCulture)),
            numeric);
    }

    /// <summary>Sets the clef.</summary>
    /// <param name="clefName">LilyPond's clef name.</param>
    public void NewClef(string clefName)
    {
        _clef = Ly2XmlTranslations.ClefNameToClef(clefName);
        if (_bar == null) { NewBar(); }

        if (_bar.HasMusic())
        {
            var attr = new BarAttr();
            attr.SetClef(_clef);
            AddToBar(attr);
        }
        else if (!string.IsNullOrEmpty(_staff))
        {
            _currentAttr.MultiClef.Add((_clef, StaffNumber()));
        }
        else
        {
            _currentAttr.SetClef(_clef);
        }
    }

    /// <summary>Asks for a system break at this bar.</summary>
    public void AddBreak()
    {
        if (_bar == null) { NewBar(); }

        _currentAttr.AddBreak("yes");
    }

    // ================= notes and rests =================

    /// <summary>Remembers a note as the one relative pitches are measured from.</summary>
    /// <param name="note">The note.</param>
    public void SetRelative(LyNote note)
    {
        _prevPitch = note.Pitch;
        _currentPitch ??= note.Pitch;
    }

    /// <summary>Counts a duration into the bar, starting a new bar when it is full.</summary>
    /// <param name="duration">The duration.</param>
    public void IncreaseBarDura(BaseScaling duration)
    {
        _barDura += duration.Base * duration.Scaling;
        if (_barDura < _currentTime) { return; }

        _barDura = Fraction.Zero;
        NewBar();
    }

    /// <summary>Describes a new note.</summary>
    /// <param name="note">The note from the source.</param>
    /// <param name="rel">Whether pitches are relative.</param>
    /// <param name="isUnpitched">Whether it has no pitch of its own.</param>
    /// <param name="pitchOverride">
    /// The pitch to draw a bare duration at; ignored for a real note.
    /// </param>
    public void NewNote(
        Music.Durable note, bool rel = false, bool isUnpitched = false,
        LyPitch pitchOverride = null)
    {
        _currentIsRest = false;
        ClearChord();
        if (isUnpitched)
        {
            _currentNote = CreateUnpitched(note);
            _currentLyNote = note;
            CheckCurrentNote(isUnpitched: true);
        }
        else
        {
            //A Note brings its own pitch; a bare duration is drawn at whatever
            //pitch the caller carried over (see the field's remarks).
            LyPitch pitch = (note as LyNote)?.Pitch ?? pitchOverride;
            _currentNote = CreateBarNoteFrom(note, pitch);
            _currentLyNote = note;
            _currentPitch = pitch;
            CheckCurrentNote(rel);
        }

        if (!string.IsNullOrEmpty(_stemDir) && _currentNote is BarNote stemmed)
        {
            stemmed.SetStemDirection(_stemDir);
        }

        DoActionOnNext(_currentNote as BarNote);
        _actionOnNext.Clear();
        IncreaseBarDura(ToBaseScaling(note.Duration));
    }

    /// <summary>
    /// Describes a bare duration in a music sequence, which takes its pitch
    /// from the note before it.
    /// </summary>
    /// <param name="note">The duration from the source.</param>
    /// <param name="rel">Whether pitches are relative.</param>
    /// <param name="isUnpitched">Whether it has no pitch of its own.</param>
    public void NewIsoDura(Music.Durable note, bool rel = false, bool isUnpitched = false)
    {
        if (_currentChord.Count > 0)
        {
            CopyPrevChord(ToBaseScaling(note.Duration));
            return;
        }

        //Upstream writes the previous pitch ONTO the duration object here; the
        //port hands it to NewNote instead, which is the same fact travelling by
        //a route C# has.
        NewNote(note, rel, isUnpitched, _currentPitch);
    }

    /// <summary>Makes an unpitched note.</summary>
    /// <param name="unpitched">The item from the source.</param>
    /// <returns>The note.</returns>
    public Unpitched CreateUnpitched(Music.Durable unpitched)
        => new Unpitched(ToBaseScaling(unpitched.Duration));

    /// <summary>Makes a note from a source note.</summary>
    /// <param name="note">The item from the source.</param>
    /// <returns>The note.</returns>
    public BarNote CreateBarNoteFromNote(LyNote note)
        => CreateBarNoteFrom(note, note.Pitch);

    /// <summary>Makes a note from a durable and the pitch it is drawn at.</summary>
    /// <param name="item">The item from the source.</param>
    /// <param name="pitch">The pitch, which a bare duration does not carry.</param>
    /// <returns>The note.</returns>
    /// <remarks>
    /// Upstream reads the accidental token in a try/except AttributeError,
    /// because a bare duration has none; asking the type is the same question.
    /// </remarks>
    public BarNote CreateBarNoteFrom(Music.Durable item, LyPitch pitch)
    {
        string p = Ly2XmlTranslations.GetNoteName(pitch.Note);
        double alt = GetXmlAlter(pitch.Alter);
        string acc = (item as LyNote)?.AccidentalToken?.Text ?? string.Empty;
        return new BarNote(p, alt, acc, ToBaseScaling(item.Duration), _voice);
    }

    /// <summary>Copies the parts of a note that a chord repeat needs.</summary>
    /// <param name="barNote">The note to copy.</param>
    /// <returns>The copy.</returns>
    public BarNote CopyBarNoteBasics(BarNote barNote)
    {
        var copy = new BarNote(
            barNote.BaseNote, barNote.Alter, barNote.AccidentalToken,
            barNote.Duration, barNote.Voice)
        {
            Octave = barNote.Octave,
            Chord = barNote.Chord,
        };
        return copy;
    }

    /// <summary>Takes in a written duration.</summary>
    /// <param name="token">The duration value.</param>
    /// <param name="tokens">The dots and scaling that follow it.</param>
    public void NewDurationToken(string token, IReadOnlyList<string> tokens)
    {
        _durToken = token;
        _durTokens = tokens ?? Array.Empty<string>();
        CheckDuration(_currentIsRest);
    }

    /// <summary>Runs the checks every new note and rest goes through.</summary>
    /// <param name="rel">Whether pitches are relative.</param>
    /// <param name="rest">Whether it is a rest.</param>
    /// <param name="isUnpitched">Whether it has no pitch of its own.</param>
    public void CheckCurrentNote(bool rel = false, bool rest = false, bool isUnpitched = false)
    {
        if (!rest && !isUnpitched) { SetOctave(rel); }

        if (!rest && _tied && _currentNote is BarNote tiedNote)
        {
            tiedNote.SetTie("stop");
            _tied = false;
        }

        CheckDuration(rest);
        CheckDivs();
        if (!string.IsNullOrEmpty(_staff))
        {
            _currentNote.Staff = _staff;
            if (_storeUnsetStaff)
            {
                if (!_staffUnsetNotes.TryGetValue(_staff, out List<BarMus> waiting))
                {
                    waiting = new List<BarMus>();
                    _staffUnsetNotes[_staff] = waiting;
                }

                waiting.Add(_currentNote);
            }
        }

        AddToBar(_currentNote);
    }

    /// <summary>Takes in a stem-direction command.</summary>
    /// <param name="direction">The command.</param>
    public void StemDirection(string direction) => _stemDir = direction switch
    {
        "\\stemUp" => "up",
        "\\stemDown" => "down",
        "\\stemNeutral" => null,
        _ => _stemDir,
    };

    /// <summary>Works out the note's octave, making it absolute if it is relative.</summary>
    /// <param name="relative">Whether pitches are relative.</param>
    public void SetOctave(bool relative)
    {
        if (_currentPitch == null) { return; }

        LyPitch p = _currentPitch.Copy();
        if (relative) { p.MakeAbsolute(_prevPitch); }

        _prevPitch = p;
        (_currentNote as BarNote)?.SetOctave(p.Octave + 3);
    }

    /// <summary>Runs whatever was waiting for the next note.</summary>
    /// <param name="note">The note.</param>
    public void DoActionOnNext(BarNote note)
    {
        if (note == null) { return; }

        foreach (Action<BarNote> action in _actionOnNext) { action(note); }
    }

    /// <summary>Applies the current written duration to the note being described.</summary>
    /// <param name="rest">Whether it is a rest.</param>
    public void CheckDuration(bool rest)
    {
        if (_currentNote == null) { return; }

        (int dots, int rs) = DurationFromTokens(_durTokens);
        if (rest && rs != 0 && _currentNote is BarRest multiRest)
        {
            //A multi-bar rest: R1*20 is twenty bars of one rest, not one bar of
            //a twentyfold duration.
            if (!multiRest.ShowType || multiRest.Skip)
            {
                BaseScaling bs = multiRest.Duration;
                if (new Fraction(rs) == bs.Scaling)
                {
                    multiRest.Duration = new BaseScaling(bs.Base, new Fraction(1));
                    multiRest.Dot = 0;
                    ScaleRest(bs);
                    return;
                }
            }
        }

        _currentNote.Dot = dots;
        _dots = dots;
        string type = Ly2XmlTranslations.DurationValueToType(_durToken);
        switch (_currentNote)
        {
            case BarRest r: r.SetDurType(type); break;
            case BarNote n: n.SetDurType(type); break;
            default: break;
        }

        foreach (BarNote c in _currentChord) { c.SetDurType(type); }
    }

    /// <summary>Describes a note of a chord.</summary>
    /// <param name="note">The note from the source.</param>
    /// <param name="duration">The chord's duration, when this is its first note.</param>
    /// <param name="rel">Whether pitches are relative.</param>
    /// <param name="chordBase">Whether this is the chord's first note.</param>
    public void NewChord(
        LyNote note, BaseScaling? duration = null, bool rel = false, bool chordBase = true)
    {
        if (chordBase)
        {
            NewChordBase(note, duration ?? default, rel);
            _currentChord.Add((BarNote)_currentNote);
        }
        else
        {
            _currentChord.Add(NewChordNote(note, rel));
        }

        DoActionOnNext(_currentChord[_currentChord.Count - 1]);
    }

    /// <summary>Describes a chord's first note.</summary>
    /// <param name="note">The note from the source.</param>
    /// <param name="duration">The chord's duration.</param>
    /// <param name="rel">Whether pitches are relative.</param>
    public void NewChordBase(LyNote note, BaseScaling duration, bool rel = false)
    {
        BarNote created = CreateBarNoteFromNote(note);
        created.SetDuration(duration);
        _currentNote = created;
        _currentLyNote = note;
        _currentPitch = note.Pitch;
        CheckCurrentNote(rel);
        IncreaseBarDura(duration);
    }

    /// <summary>Describes one of a chord's other notes.</summary>
    /// <param name="note">The note from the source.</param>
    /// <param name="rel">Whether pitches are relative.</param>
    /// <returns>The note.</returns>
    public BarNote NewChordNote(LyNote note, bool rel)
    {
        BarNote chordNote = CreateBarNoteFromNote(note);
        chordNote.SetDuration(_currentNote.Duration);
        chordNote.SetDurType(Ly2XmlTranslations.DurationValueToType(_durToken));
        chordNote.Dot = _dots;
        if (_currentNote is BarNote head)
        {
            chordNote.Tie.AddRange(head.Tie);
            chordNote.Tuplet.AddRange(head.Tuplet);
        }

        _prevChordPitch ??= _prevPitch;
        LyPitch p = note.Pitch.Copy();
        if (rel) { p.MakeAbsolute(_prevChordPitch); }

        chordNote.SetOctave(p.Octave + 3);
        _prevChordPitch = p;
        chordNote.Chord = true;
        _bar.Add(chordNote);
        return chordNote;
    }

    /// <summary>Repeats the chord before this one at a new duration.</summary>
    /// <param name="duration">The new duration.</param>
    public void CopyPrevChord(BaseScaling duration)
    {
        List<BarNote> prevChord;
        if (_currentChord.Count > 0)
        {
            prevChord = new List<BarNote>(_currentChord);
            ClearChord();
        }
        else
        {
            prevChord = _qChord;
        }

        for (int i = 0; i < prevChord.Count; i++)
        {
            BarNote cn = CopyBarNoteBasics(prevChord[i]);
            cn.SetDuration(duration);
            cn.SetDurType(Ly2XmlTranslations.DurationValueToType(_durToken));
            if (i == 0) { _currentNote = cn; }

            _currentChord.Add(cn);
            if (_tied) { cn.SetTie("stop"); }

            _bar.Add(cn);
        }

        _tied = false;
        IncreaseBarDura(duration);
    }

    /// <summary>Puts the chord aside so a repeat can find it.</summary>
    public void ClearChord()
    {
        _qChord = new List<BarNote>(_currentChord);
        _currentChord.Clear();
        _prevChordPitch = null;
    }

    /// <summary>Forgets whatever was waiting for the next note.</summary>
    public void ChordEnd() => _actionOnNext.Clear();

    /// <summary>Describes a rest or a skip.</summary>
    /// <param name="rest">The item from the source.</param>
    public void NewRest(Music.Durable rest)
    {
        _currentIsRest = true;
        ClearChord();
        string rtype = rest.Token?.Text;
        BaseScaling dur = ToBaseScaling(rest.Duration);
        if (rtype == "r") { _currentNote = new BarRest(dur, _voice); }
        else if (rtype == "R")
        {
            _currentNote = new BarRest(dur, _voice, showType: false);
            if (_multipleRest) { SetMultRestBar(dur); }
        }
        else if (rtype == "s" || rtype == "\\skip")
        {
            _currentNote = new BarRest(dur, _voice, skip: true);
        }

        CheckCurrentNote(rest: true);
        IncreaseBarDura(dur);
    }

    /// <summary>Turns the note just described into a rest at its own position.</summary>
    public void Note2Rest()
    {
        if (_currentNote is not BarNote note) { return; }

        BaseScaling dur = note.Duration;
        int voice = note.Voice;
        var pos = (note.BaseNote, note.Octave);
        _currentNote = new BarRest(dur, voice, position: pos);
        CheckDuration(rest: true);
        _bar.ObjList.RemoveAt(_bar.ObjList.Count - 1);
        _bar.Add(_currentNote);
    }

    /// <summary>Says the rests that follow are multi-bar rests.</summary>
    public void SetMultRest() => _multipleRest = true;

    /// <summary>Adds the multiple-rest attribute to the bar.</summary>
    /// <param name="dur">The rest's duration.</param>
    public void SetMultRestBar(BaseScaling dur)
    {
        if (_bar == null) { NewBar(); }

        Fraction size = dur.Scaling * (dur.Base / _currentTime);
        var attr = new BarAttr();
        attr.SetMultipleRest((int)(size.Numerator / size.Denominator));
        _bar.Add(attr);
    }

    /// <summary>Writes out the extra bars a multi-bar rest stands for.</summary>
    /// <param name="bs">The rest's original duration.</param>
    public void ScaleRest(BaseScaling bs)
    {
        if (_currentNote is not BarRest rest) { return; }

        BaseScaling dur = rest.Duration;
        int voice = rest.Voice;
        bool showType = rest.ShowType;
        bool skip = rest.Skip;
        Fraction multiple = bs.Scaling * (bs.Base / _currentTime);
        long count = multiple.Numerator / multiple.Denominator;
        for (long i = 1; i < count; i++)
        {
            NewBar();
            AddToBar(new BarRest(dur, voice, showType, skip));
        }
    }

    // ================= tuplets, ties, slurs =================

    /// <summary>Makes the note being described part of a tuplet.</summary>
    /// <param name="tfraction">The ratio, actual over normal.</param>
    /// <param name="ttype">What the bracket does.</param>
    /// <param name="nr">Which tuplet.</param>
    /// <param name="length">The tuplet's own note length, or null.</param>
    public void ChangeToTuplet(
        (int Actual, int Normal) tfraction, string ttype, int nr, Fraction? length = null)
    {
        var tuplScaling = new Fraction(tfraction.Actual, tfraction.Normal);
        if (_tuplDur != Fraction.Zero)
        {
            if (_tuplSum == Fraction.Zero) { ttype = "start"; }

            (Fraction Base, Fraction Scaling) duration = _currentLyNote?.Duration
                ?? (Fraction.Zero, Fraction.One);
            _tuplSum += new Fraction(1) / tuplScaling * duration.Base * duration.Scaling;
            if (_tuplSum == _tuplDur)
            {
                ttype = "stop";
                _tuplSum = Fraction.Zero;
            }
        }

        if (length != null)
        {
            string type = Ly2XmlTranslations.DurationValueToType(
                CalcTuplDen(tfraction, length.Value));
            _currentNote.SetTuplet(tfraction, ttype, nr, type, type);
        }
        else
        {
            _currentNote.SetTuplet(tfraction, ttype, nr);
        }
    }

    /// <summary>Changes what one of the note's tuplet brackets does.</summary>
    /// <param name="index">Which bracket.</param>
    /// <param name="newType">What it should do.</param>
    public void ChangeTupletType(int index, string newType)
    {
        Tuplet old = _currentNote.Tuplet[index];
        _currentNote.Tuplet[index] = new Tuplet(
            old.Fraction, newType, old.Number, old.ActualType, old.NormalType);
    }

    /// <summary>Takes in the duration set by the tuplet spanner property.</summary>
    /// <param name="token">The duration value, or null.</param>
    /// <param name="tokens">The dots and scaling, or null.</param>
    /// <param name="fraction">The duration itself, or null.</param>
    public void SetTuplSpanDur(
        string token = null, IReadOnlyList<string> tokens = null, Fraction? fraction = null)
    {
        if (fraction != null)
        {
            _tuplDur = fraction.Value;
            return;
        }

        var all = new List<string> { token };
        if (tokens != null) { all.AddRange(tokens); }

        (Fraction b, Fraction s) = Durations.BaseScalingTexts(all);
        _tuplDur = b * s;
    }

    /// <summary>Forgets the tuplet spanner duration and what has been counted.</summary>
    public void UnsetTuplSpanDur()
    {
        _tuplSum = Fraction.Zero;
        _tuplDur = Fraction.Zero;
    }

    /// <summary>Works out the tuplet denominator from its ratio and its length.</summary>
    /// <param name="tfraction">The ratio.</param>
    /// <param name="length">The length.</param>
    /// <returns>The denominator, as a duration value.</returns>
    public static string CalcTuplDen((int Actual, int Normal) tfraction, Fraction length)
    {
        Fraction result = new Fraction(tfraction.Normal) / length;
        return Durations.FormatFraction(result);
    }

    /// <summary>Ties the note being described to the next one.</summary>
    public void TieToNext()
    {
        _tied = true;
        (_currentNote as BarNote)?.SetTie("start");
    }

    /// <summary>Starts or stops a slur on the note being described.</summary>
    /// <param name="nr">Which slur.</param>
    /// <param name="slurType">Whether it starts or stops.</param>
    /// <param name="phrasing">Whether it is a phrasing slur.</param>
    public void SetSlur(int nr, string slurType, bool phrasing = false)
    {
        if (_currentNote is not BarNote note) { return; }

        Slur slurStart = null;
        if (slurType == "stop" && _slurStack.Count > 0)
        {
            slurStart = _slurStack[_slurStack.Count - 1];
            _slurStack.RemoveAt(_slurStack.Count - 1);
        }

        note.SetSlur(nr, slurType, phrasing, slurStart);

        if (slurType == "start") { _slurStack.Add(note.Slur[note.Slur.Count - 1]); }
    }

    // ================= marks on notes =================

    /// <summary>Adds an articulation, a fingering or another symbol.</summary>
    /// <param name="articulationToken">The token.</param>
    /// <param name="isFingering">Whether the token is a fingering number.</param>
    public void NewArticulation(string articulationToken, bool isFingering = false)
    {
        if (_currentNote is not BarNote note) { return; }

        if (isFingering)
        {
            note.AddFingering(int.Parse(articulationToken, CultureInfo.InvariantCulture));
            return;
        }

        string ret = Ly2XmlTranslations.ArticulationTokenToXmlName(articulationToken);
        if (ret == "ornament") { note.AddOrnament(articulationToken.Substring(1)); }
        else if (ret == "other") { note.AddOtherNotation(articulationToken.Substring(1)); }
        else if (!string.IsNullOrEmpty(ret)) { note.AddArticulation(ret); }
    }

    /// <summary>Adds a dynamic to the note being described.</summary>
    /// <param name="dynamics">The dynamic's name or sign.</param>
    public void NewDynamics(string dynamics)
    {
        if (_currentNote == null) { return; }

        if (dynamics == "!")
        {
            if (_ongoingWedge)
            {
                _currentNote.SetDynamicsWedge("stop");
                _ongoingWedge = false;
            }

            if (_ongoingDashes)
            {
                _currentNote.SetDynamicsDashes("stop");
                _ongoingDashes = false;
            }
        }
        else if (Hairpins.TryGetValue(dynamics, out string hairpin))
        {
            _currentNote.SetDynamicsWedge(hairpin);
            _ongoingWedge = true;
        }
        else if (TextDynamics.TryGetValue(dynamics, out string text))
        {
            _currentNote.SetDynamicsText(text);
            _currentNote.SetDynamicsDashes("start", before: false);
            _ongoingDashes = true;
        }
        else if (_ongoingWedge)
        {
            _currentNote.SetDynamicsWedge("stop");
            _currentNote.SetDynamicsMark(dynamics);
            _ongoingWedge = false;
        }
        else if (_ongoingDashes)
        {
            _currentNote.SetDynamicsDashes("stop");
            _currentNote.SetDynamicsMark(dynamics);
            _ongoingDashes = false;
        }
        else
        {
            _currentNote.SetDynamicsMark(dynamics);
        }
    }

    /// <summary>Makes the note being described a grace note.</summary>
    /// <param name="slash">Whether the grace is slashed.</param>
    public void NewGrace(bool slash = false) => (_currentNote as BarNote)?.SetGrace(slash);

    /// <summary>Makes the chord's last note a grace note.</summary>
    /// <param name="slash">Whether the grace is slashed.</param>
    public void NewChordGrace(bool slash = false)
    {
        if (_currentChord.Count > 0) { _currentChord[_currentChord.Count - 1].SetGrace(slash); }
    }

    /// <summary>Starts a glissando, and arranges for the next note to end it.</summary>
    /// <param name="line">The line style, or null.</param>
    public void NewGliss(string line = null)
    {
        string style = string.IsNullOrEmpty(line) ? null : Ly2XmlTranslations.GetLineStyle(line);
        if (_currentChord.Count > 0)
        {
            for (int n = 0; n < _currentChord.Count; n++)
            {
                _currentChord[n].SetGliss(style, number: n + 1);
            }
        }
        else
        {
            (_currentNote as BarNote)?.SetGliss(style);
        }

        _actionOnNext.Add(note => EndGliss(note, style));
    }

    /// <summary>Ends a glissando on a note.</summary>
    /// <param name="note">The note.</param>
    /// <param name="line">The line style.</param>
    public void EndGliss(BarNote note, string line)
    {
        int n = _currentChord.Count > 0 ? _currentChord.Count : 1;
        note.SetGliss(line, endType: "stop", number: n);
    }

    /// <summary>Adds a tremolo to the note being described.</summary>
    /// <param name="tremType">What it does.</param>
    /// <param name="duration">The tremolo's note value, or zero.</param>
    /// <param name="repeats">How many repeats a counted tremolo has, or zero.</param>
    public void SetTremolo(string tremType = "single", int duration = 0, int repeats = 0)
    {
        if (_currentNote is not BarNote note) { return; }

        if (note.Tremolo.Lines != 0)
        {
            note.SetTremolo(tremType);
            return;
        }

        if (repeats != 0)
        {
            duration = int.Parse(_durToken, CultureInfo.InvariantCulture);

            //⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14).
            //Upstream's calc_trem_dur ends with `xml_objs.durval2type(...)`, and
            //xml_objs has no such member — durval2type lives in this very
            //module. So every counted tremolo raised AttributeError, which
            //ParseSource swallowed as "Warning: Note not implemented!": the
            //tremolo simply disappeared and nothing said why. Calling the
            //function that exists gives `\repeat tremolo 8 { c32 e }` two
            //quarter notes carrying three tremolo beams, which is the music.
            (BaseScaling bs, string durType) = Ly2XmlTranslations.CalcTremDuration(
                repeats, note.Duration, duration);
            note.Duration = bs;
            note.Type = durType;
        }
        else if (duration == 0)
        {
            duration = _prevTremolo;
        }
        else
        {
            _prevTremolo = duration;
        }

        note.SetTremolo(tremType, duration);
    }

    /// <summary>Starts or ends a trill spanner.</summary>
    /// <param name="end">What it does, or null to start one.</param>
    public void NewTrillSpanner(string end = null)
    {
        if (_currentNote is not BarNote note) { return; }

        if (string.IsNullOrEmpty(end))
        {
            note.AddOrnament("trill");
            end = "start";
        }

        note.AddAdvOrnament("wavy-line", end);
    }

    /// <summary>Starts or ends an octave shift.</summary>
    /// <param name="octdiff">How many octaves, zero to end one.</param>
    public void NewOttava(int octdiff)
    {
        if (_octDiff == octdiff) { return; }

        if (_octDiff != 0)
        {
            string placement = _octDiff < 0 ? "below" : "above";
            int size = (Math.Abs(_octDiff) * 7) + 1;
            _currentNote?.SetOctShift(placement, "stop", size);
        }

        if (octdiff != 0)
        {
            string placement = octdiff < 0 ? "below" : "above";
            string direction = octdiff < 0 ? "up" : "down";
            int size = (Math.Abs(octdiff) * 7) + 1;
            _actionOnNext.Add(note => note.SetOctShift(placement, direction, size));
        }

        _octDiff = octdiff;
    }

    /// <summary>Adds a tempo direction.</summary>
    /// <param name="unit">The beat unit's value, or null for no metronome mark.</param>
    /// <param name="durTokens">The beat unit's dots and scaling.</param>
    /// <param name="tempo">
    /// The beats per minute, as the source's own numbers; the first is used.
    /// </param>
    /// <param name="text">The words shown, or null.</param>
    public void NewTempo(
        string unit, IReadOnlyList<string> durTokens, IReadOnlyList<int> tempo, string text)
    {
        (int dots, int _) = DurationFromTokens(durTokens);
        string beats = tempo != null && tempo.Count > 0
            ? tempo[0].ToString(CultureInfo.InvariantCulture)
            : "0";
        var attr = new BarAttr();
        string unitType = string.IsNullOrEmpty(unit)
            ? string.Empty
            : Ly2XmlTranslations.DurationValueToType(unit);
        attr.SetTempo(unit, unitType, beats, dots, text);
        if (_bar == null) { NewBar(); }

        _bar.Add(attr);
    }

    // ================= properties =================

    /// <summary>Takes in a context property the exporter cares about.</summary>
    /// <param name="property">The property's name.</param>
    /// <param name="value">Its value.</param>
    /// <param name="group">Whether it was set on a group rather than a staff.</param>
    public void SetByProperty(string property, string value, bool group = false)
    {
        switch (property)
        {
            case "instrumentName":
                if (group) { SetGroupName(value); } else { SetPartName(value); }

                break;
            case "shortInstrumentName":
                if (group) { SetGroupAbbr(value); } else { SetPartAbbr(value); }

                break;
            case "midiInstrument": SetPartMidi(value); break;
            case "stanza": NewLyricNr(value); break;
            case "systemStartDelimiter": ChangeGroupBracket(value); break;
            default: break;
        }
    }

    /// <summary>Names the part.</summary>
    /// <param name="name">The name.</param>
    public void SetPartName(string name)
    {
        if (Score.IsEmpty()) { NewPart(); }

        if (_part != null) { _part.Name = name; }
    }

    /// <summary>Gives the part a short name.</summary>
    /// <param name="abbr">The short name.</param>
    public void SetPartAbbr(string abbr)
    {
        if (Score.IsEmpty()) { NewPart(); }

        if (_part != null) { _part.Abbreviation = abbr; }
    }

    /// <summary>Names the open group.</summary>
    /// <param name="name">The name.</param>
    public void SetGroupName(string name)
    {
        if (_group != null) { _group.Name = name; }
    }

    /// <summary>Gives the open group a short name.</summary>
    /// <param name="abbr">The short name.</param>
    public void SetGroupAbbr(string abbr)
    {
        if (_group != null) { _group.Abbreviation = abbr; }
    }

    /// <summary>Gives the part a MIDI instrument.</summary>
    /// <param name="midi">The instrument's name.</param>
    public void SetPartMidi(string midi)
    {
        if (Score.IsEmpty()) { NewPart(); }

        if (_part != null) { _part.Midi = midi; }
    }

    // ================= lyrics =================

    /// <summary>Sets which verse the syllables that follow belong to.</summary>
    /// <param name="num">The verse.</param>
    public void NewLyricNr(string num)
    {
        //A stanza is written as text — "1." — so only its digits are the number.
        var digits = new System.Text.StringBuilder();
        foreach (char c in num ?? string.Empty)
        {
            if (char.IsDigit(c)) { digits.Append(c); }
        }

        _lyricNr = digits.Length > 0
            ? int.Parse(digits.ToString(), CultureInfo.InvariantCulture)
            : _lyricNr;
    }

    /// <summary>Takes in one syllable of lyrics.</summary>
    /// <param name="txt">The syllable.</param>
    public void NewLyricsText(string txt)
    {
        if (_lyric != null)
        {
            if (_lyricSyll)
            {
                if (_lyric.Syllabic == "begin" || _lyric.Syllabic == "middle")
                {
                    _lyric = new LyricEntry { Text = txt, Syllabic = "middle", Number = _lyricNr };
                }
            }
            else
            {
                if (_lyric.Syllabic == "begin" || _lyric.Syllabic == "middle")
                {
                    _lyric.Syllabic = "end";
                }

                _lyric = new LyricEntry { Text = txt, Syllabic = "single", Number = _lyricNr };
            }
        }
        else
        {
            _lyric = new LyricEntry { Text = txt, Syllabic = "single", Number = _lyricNr };
        }

        if (InsertInto is LyricsSection lyricsSection) { lyricsSection.LyricList.Add(_lyric); }

        _lyricSyll = false;
    }

    /// <summary>Takes in something in a lyrics list that is not a syllable.</summary>
    /// <param name="item">The item.</param>
    public void NewLyricsItem(string item)
    {
        if (item == "--")
        {
            if (_lyric == null) { return; }

            if (_lyric.Syllabic == "single") { _lyric.Syllabic = "begin"; }

            _lyricSyll = true;
        }
        else if (item == "__")
        {
            if (_lyric != null) { _lyric.Extend = true; }
        }
        else if (item == "\\skip")
        {
            if (InsertInto is LyricsSection lyricsSection)
            {
                lyricsSection.LyricList.Add(LyricEntry.Skip());
            }
        }
    }

    // ================= duration arithmetic =================

    /// <summary>Counts the dots and the multi-bar scaling in a set of tokens.</summary>
    /// <param name="tokens">The tokens.</param>
    /// <returns>How many dots, and the multi-bar count.</returns>
    public static (int Dots, int Scaling) DurationFromTokens(IReadOnlyList<string> tokens)
    {
        int dots = 0;
        int rs = 0;
        if (tokens == null) { return (dots, rs); }

        foreach (string t in tokens)
        {
            if (t == ".") { dots++; }
            else if (t != null && t.Contains('*') && !t.Contains('/'))
            {
                rs = int.Parse(t.Substring(1), CultureInfo.InvariantCulture);
            }
        }

        return (dots, rs);
    }

    /// <summary>
    /// Makes sure the divisions are fine enough to say the note's duration as
    /// a whole number, raising them when they are not.
    /// </summary>
    public void CheckDivs()
    {
        if (_currentNote == null) { return; }

        Fraction @base = _currentNote.Duration.Base;
        Fraction scaling = _currentNote.Duration.Scaling;
        int divs = Divisions;
        IReadOnlyList<Tuplet> tupl = _currentNote.Tuplet;

        Fraction a;
        Fraction b;
        if (tupl.Count == 0)
        {
            a = new Fraction(4);
            if (@base != Fraction.Zero) { b = new Fraction(1) / @base; }
            else
            {
                b = new Fraction(1);
                Warn?.Invoke("Warning problem checking duration!");
            }
        }
        else
        {
            long num = 1;
            long den = 1;
            foreach (Tuplet t in tupl)
            {
                num *= t.Fraction.Actual;
                den *= t.Fraction.Normal;
            }

            a = new Fraction(4 * den);
            b = new Fraction(1) / @base * new Fraction(num);
        }

        Fraction c = a * new Fraction(divs) * scaling;
        Fraction quotient = c / b;
        if (quotient.Denominator == 1) { return; }

        long mult = Ly2XmlTranslations.GetMult(a, b);
        Divisions = (int)(divs * mult);
    }

    /// <summary>Turns a ported duration tuple into the exporter's own.</summary>
    /// <param name="duration">The tuple.</param>
    /// <returns>The duration.</returns>
    public static BaseScaling ToBaseScaling((Fraction Base, Fraction Scaling) duration)
        => new BaseScaling(duration.Base, duration.Scaling);

    /// <summary>
    /// Turns an alteration into the number MusicXML wants, which is twice
    /// LilyPond's.
    /// </summary>
    /// <param name="alter">LilyPond's alteration.</param>
    /// <returns>MusicXML's.</returns>
    public static double GetXmlAlter(Fraction alter)
    {
        Fraction doubled = alter * new Fraction(2);
        return (double)doubled.Numerator / doubled.Denominator;
    }

    private string StaffNumber() => _staff;

    private void ContinueBarList(List<Bar> barList)
    {
        foreach (ScoreSection section in _sections)
        {
            if (!ReferenceEquals(section.BarList, barList)) { continue; }

            InsertInto = section;
            break;
        }

        if (barList != null && barList.Count > 0) { _bar = barList[barList.Count - 1]; }
        else { NewBar(false); }
    }

    private static readonly string[] CreatorNames = { "composer", "arranger", "poet", "lyricist" };

    private static readonly Dictionary<string, string> Hairpins
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["<"] = "crescendo",
            [">"] = "diminuendo",
        };

    //⚠ "descresc." is upstream's spelling, misprint and all. It is DATA a
    //reader may already have in files python-ly wrote, so it stays; raised in
    //the wave's STATUS file.
    private static readonly Dictionary<string, string> TextDynamics
        = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["cresc"] = "cresc.",
            ["decresc"] = "descresc.",
            ["dim"] = "dim.",
        };
}
