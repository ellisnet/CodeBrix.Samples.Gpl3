// === python-ly ly.musicxml.lymus2musxml module ===
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
using Fresco.Brix.Ly.Music;
using LyDocument = Fresco.Brix.Ly.Document;
//`Document` is BOTH ly.document's and ly.music's; the walk needs the second,
//and ParseDocument takes the first (board trap 19's family again).
using MusicTree = Fresco.Brix.Ly.Music.Document;
//Four more names that exist on BOTH sides of the port: the source's Slur, its
//TimeSignature, its Score and its Music base all share a name with something in
//this namespace or with the namespace itself.
using MusicNode = Fresco.Brix.Ly.Music.Music;
using MusicScore = Fresco.Brix.Ly.Music.Score;
using MusicSlur = Fresco.Brix.Ly.Music.Slur;
using MusicTimeSignature = Fresco.Brix.Ly.Music.TimeSignature;

namespace Fresco.Brix.Ly.MusicXml; //was previously: ly/musicxml/lymus2musxml.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Marks where a container node's children end.</summary>
/// <remarks>
/// Upstream's <c>End</c>. The walk is otherwise a flat stream of nodes, and a
/// great deal of the exporter's work happens when something CLOSES — a context,
/// a tuplet, a slur, a chord — so the stream carries an end marker for every
/// container it went into.
/// </remarks>
public sealed class EndOfNode : Item
{
    /// <summary>Creates an end marker.</summary>
    /// <param name="node">The container that ends.</param>
    public EndOfNode(Item node) => Node = node;

    /// <summary>Gets the container that ends.</summary>
    public Item Node { get; }

    /// <inheritdoc/>
    public override string ToString() => "<End " + Node + ">";
}

/// <summary>
/// Walks a <c>ly.music</c> tree and drives the MusicXML export from it.
/// </summary>
/// <remarks>
/// <para>
/// Upstream's <c>ParseSource</c>. There the walk dispatches on the node's class
/// NAME — <c>getattr(self, m.__class__.__name__)</c> — which is why every
/// handler in the file is named after a <c>ly.music.items</c> class. That is
/// kept: the switch below is keyed by the same names, so a reader comparing the
/// two files finds the same list in the same order.
/// </para>
/// <para>
/// ⚠ Upstream reports a node it has no handler for as
/// <c>Warning: X not implemented!</c> and carries on — and because it catches
/// <c>AttributeError</c> around the CALL, it reports a handler that FAILED the
/// same way. That is how four of the five defects ruling FR14 covers stayed
/// invisible. The port keeps the resilience (a wild document must not kill the
/// export) and keeps the message, but the five defects are fixed, so nothing
/// legitimate hides behind it any more.
/// </para>
/// </remarks>
public sealed class ParseSource
{
    private static readonly string[] ExclList = { "Version", "Midi", "Layout" };

    private static readonly string[] GroupContexts = { "StaffGroup", "ChoirStaff" };

    private static readonly string[] PnoContexts = { "PianoStaff", "GrandStaff" };

    private static readonly string[] StaffContexts =
    {
        "Staff", "RhythmicStaff", "TabStaff", "DrumStaff", "VaticanaStaff", "MensuralStaff",
    };

    private readonly List<TupletLevel> _tuplet = new List<TupletLevel>();
    private readonly List<string> _simsAndSeqs = new List<string>();
    private readonly Dictionary<string, string> _overrideDict
        = new Dictionary<string, string>(StringComparer.Ordinal);

    private bool _relative;
    private bool _graceSeq;
    private int _tremRep;
    private int _pianoStaff;
    private bool _numericTime;
    private bool _voiceSep;
    private bool _ottava;
    private string _withContext;
    private string _schmAssignm;
    private (IReadOnlyList<int> Tempo, Item Text)? _tempo;
    private bool _tremolo;
    private bool _tuplSpan;
    private bool _unsetTuplSpan;
    private string _altMode;
    private bool _relPitchIsSet;
    private int _slurCount;
    private int _slurNr;
    private int _phrSlurNr;
    private bool _mark;
    private bool _pickup;
    private string _overrideKey = string.Empty;
    private MusicTree _document;

    /// <summary>Gets the builder the document is written into.</summary>
    public MusicXmlCreator Musxml { get; } = new MusicXmlCreator();

    /// <summary>Gets the mediator that holds the score being built.</summary>
    public Ly2XmlMediator Mediator { get; } = new Ly2XmlMediator();

    /// <summary>Gets or sets where warnings go, or null to drop them.</summary>
    public Action<string> Warn
    {
        get => Mediator.Warn;
        set => Mediator.Warn = value;
    }

    /// <summary>Reads LilyPond source given as text.</summary>
    /// <param name="lyText">The source.</param>
    /// <param name="fileName">Its file name, for resolving includes, or null.</param>
    public void ParseText(string lyText, string fileName = null)
    {
        var doc = new LyDocument(lyText) { Filename = fileName };
        ParseDocument(doc);
    }

    /// <summary>Reads LilyPond source given as a document.</summary>
    /// <param name="lyDoc">The document.</param>
    /// <param name="relativeFirstPitchAbsolute">
    /// Whether the first pitch of a <c>\relative</c> with no start pitch is
    /// absolute, which is LilyPond 2.18's behaviour.
    /// </param>
    /// <remarks>
    /// The document is COPIED and the copy is turned absolute before anything
    /// is read; the caller's document is not touched. That is upstream's own
    /// arrangement and it is what lets everything downstream assume absolute
    /// pitches.
    /// </remarks>
    public void ParseDocument(LyDocument lyDoc, bool relativeFirstPitchAbsolute = false)
    {
        LyDocument doc = lyDoc.Copy();
        var cursor = new Cursor(doc);
        Pitching.Rel2Abs.Convert(cursor, firstPitchAbsolute: relativeFirstPitchAbsolute);
        MusicTree mustree = MusicReader.ReadDocument(doc);
        ParseTree(mustree);
    }

    /// <summary>Reads a music tree that has already been built.</summary>
    /// <param name="mustree">The tree.</param>
    public void ParseTree(MusicTree mustree)
    {
        _document = mustree;
        IEnumerable<Item> headerNodes = IterHeader(mustree);
        if (headerNodes != null) { ParseNodes(headerNodes); }

        Item score = GetScore(mustree);
        IEnumerable<Item> musNodes = score != null
            ? IterScore(score, mustree)
            : FindScoreSub(mustree);

        //The fallback section: a document with no \score at all still has music,
        //and this is where it goes.
        Mediator.NewSection("fallback");
        ParseNodes(musNodes);
    }

    /// <summary>Sends every node to the handler named after its class.</summary>
    /// <param name="nodes">The nodes.</param>
    public void ParseNodes(IEnumerable<Item> nodes)
    {
        if (nodes == null)
        {
            Warn?.Invoke("Warning! Couldn't parse source!");
            return;
        }

        bool any = false;
        foreach (Item m in nodes)
        {
            any = true;
            string funcName = PythonClassName(m);
            if (Array.IndexOf(ExclList, funcName) >= 0) { continue; }

            try
            {
                if (!Dispatch(funcName, m))
                {
                    Warn?.Invoke("Warning: " + funcName + " not implemented!");
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException
                and not StackOverflowException)
            {
                //Upstream's own resilience: a node it cannot deal with must not
                //stop the export. See the class remarks.
                Warn?.Invoke("Warning: " + funcName + " not implemented!");
                Warn?.Invoke(exception.Message);
            }
        }

        if (!any) { Warn?.Invoke("Warning! Couldn't parse source!"); }
    }

    /// <summary>Finishes the score and returns the document.</summary>
    /// <param name="prettyPrint">Whether to indent it.</param>
    /// <returns>The document.</returns>
    public MusicXmlDocument MusicXml(bool prettyPrint = true)
    {
        Mediator.CheckScore();
        new IterateXmlObjs(Musxml, Mediator.Divisions, Warn).Run(Mediator.Score);
        return Musxml.MusicXml(prettyPrint);
    }

    // ================= the handlers, in upstream's order =================

    private bool Dispatch(string name, Item m)
    {
        switch (name)
        {
            case "Assignment": Assignment((Assignment)m); return true;
            case "MusicList": MusicList((MusicList)m); return true;
            case "Chord": Mediator.ClearChord(); return true;
            case "Q": Q((Q)m); return true;
            case "Context": Context((ContextItem)m); return true;
            case "VoiceSeparator": VoiceSeparator(); return true;
            case "Change": Change((Change)m); return true;
            case "PipeSymbol": return true;
            case "Clef": Mediator.NewClef(((Clef)m).Specifier()); return true;
            case "KeySignature": KeySignature((KeySignature)m); return true;
            case "Relative": _relative = true; return true;
            case "Partial": _pickup = true; return true;
            case "Note": Note((Note)m); return true;
            case "Unpitched": UnpitchedNode((Music.Unpitched)m); return true;
            case "DrumNote": DrumNote((DrumNote)m); return true;
            case "Duration": DurationNode((DurationItem)m); return true;
            case "Tempo": TempoNode((Tempo)m); return true;
            case "Tie": Mediator.TieToNext(); return true;
            case "Rest": Rest((Rest)m); return true;
            case "Skip": Skip((Skip)m); return true;
            case "Scaler": Scaler((Scaler)m); return true;
            case "Number": return true;
            case "Articulation": Articulation(((Articulation)m).Token?.Text, m); return true;
            case "Postfix": return true;
            case "Beam": return true;
            case "Slur": Slur((MusicSlur)m); return true;
            case "PhrasingSlur": PhrasingSlur((PhrasingSlur)m); return true;
            case "Dynamic": Dynamic((Dynamic)m); return true;
            case "Grace": _graceSeq = true; return true;
            case "TimeSignature": TimeSignatureNode((MusicTimeSignature)m); return true;
            case "Repeat": Repeat((Repeat)m); return true;
            case "Tremolo": TremoloNode((Tremolo)m); return true;
            case "With": With((With)m); return true;
            case "Set": Set((Set)m); return true;
            case "Command": Command((Command)m); return true;
            case "UserCommand": UserCommand((UserCommand)m); return true;
            case "Markup": return true;
            case "MarkupWord": Mediator.NewWord(m.Token?.Text); return true;
            case "MarkupList": return true;
            case "String": StringNode((StringItem)m); return true;
            case "LyricsTo": LyricsTo((LyricsTo)m); return true;
            case "LyricText": Mediator.NewLyricsText(m.Token?.Text); return true;
            case "LyricItem": Mediator.NewLyricsItem(m.Token?.Text); return true;
            case "NoteMode": _altMode = "note"; return true;
            case "ChordMode": _altMode = "chord"; return true;
            case "DrumMode": DrumMode((DrumMode)m); return true;
            case "FigureMode": _altMode = "figure"; return true;
            case "LyricMode": _altMode = "lyric"; return true;
            case "Override": _overrideKey = string.Empty; return true;
            case "PathItem": _overrideKey += m.Token?.Text; return true;
            case "Scheme": return true;
            case "SchemeItem": SchemeItem(m); return true;
            case "SchemeQuote": return true;
            case "End": End((EndOfNode)m); return true;
            default: return false;
        }
    }

    /// <summary>An assignment outside a header, or one inside a <c>\with</c>.</summary>
    /// <param name="a">The assignment.</param>
    private void Assignment(Assignment a)
    {
        Item value = a.Value();
        string val;
        switch (value)
        {
            case Markup markup: val = markup.PlainText(); break;
            case StringItem str: val = str.Value(); break;
            case Scheme scheme:
                val = scheme.GetString();
                if (string.IsNullOrEmpty(val)) { _schmAssignm = a.Name(); }

                break;
            default:
                //⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14).
                //Upstream's chain has four arms and no else, and then reads
                //`val` unconditionally two lines below — so an assignment whose
                //value is any other kind of node raises UnboundLocalError and
                //kills the whole export. `left-margin = 20\mm` is one, and it is
                //in LilyPond's own regression corpus. What upstream MEANS to do
                //with a value it does not understand is written into the arm it
                //gave UserCommand: "Don't know what to do with this: return".
                return;
        }

        if (!string.IsNullOrEmpty(_withContext))
        {
            Mediator.SetByProperty(
                a.Name(), val, Array.IndexOf(GroupContexts, _withContext) >= 0);
        }
        else
        {
            Mediator.NewHeaderAssignment(a.Name(), val);
        }
    }

    private void MusicList(MusicList musicList)
    {
        string token = musicList.Token?.Text;
        if (token == "<<")
        {
            if (LookAhead<VoiceSeparator>(musicList))
            {
                Mediator.NewSnippet("sim-snip");
                _voiceSep = true;
            }
            else
            {
                Mediator.NewSection("simultan");
                _simsAndSeqs.Add("sim");
            }
        }
        else if (token == "{")
        {
            _simsAndSeqs.Add("seq");
        }
    }

    private void Q(Q q) => Mediator.CopyPrevChord(Ly2XmlMediator.ToBaseScaling(q.Duration));

    private void Context(ContextItem context)
        => CheckContext(context.Context()?.Text, context.ContextId(), context.Token?.Text ?? string.Empty);

    /// <summary>Acts on a context: makes a part, a group, a staff or a voice.</summary>
    /// <param name="context">The context's name.</param>
    /// <param name="contextId">Its identifier, or null.</param>
    /// <param name="token">The token that introduced it.</param>
    public void CheckContext(string context, string contextId = null, string token = "")
    {
        if (!string.IsNullOrEmpty(contextId))
        {
            ScorePart match = Mediator.GetPartById(contextId);
            if (match != null)
            {
                Mediator.NewPart(toPart: match);
                return;
            }
        }

        if (Array.IndexOf(PnoContexts, context) >= 0)
        {
            Mediator.NewPart(contextId, piano: true);
            _pianoStaff = 1;
        }
        else if (Array.IndexOf(GroupContexts, context) >= 0)
        {
            Mediator.NewGroup();
        }
        else if (Array.IndexOf(StaffContexts, context) >= 0)
        {
            if (_pianoStaff != 0)
            {
                if (_pianoStaff > 1) { Mediator.SetVoiceNr(nr: _pianoStaff + 3); }

                Mediator.NewSection(
                    "piano-staff" + _pianoStaff.ToString(CultureInfo.InvariantCulture));
                Mediator.SetStaffNr(_pianoStaff);
                _pianoStaff++;
            }
            else if (token != "\\context" || Mediator.PartNotEmpty())
            {
                Mediator.NewPart(contextId);
            }

            Mediator.AddStaffId(contextId);
        }
        else if (context == "Voice")
        {
            _simsAndSeqs.Add("voice");
            Mediator.NewSection(string.IsNullOrEmpty(contextId) ? "voice" : contextId);
        }
        else if (context == "Devnull")
        {
            Mediator.NewSection("devnull", true);
        }
        else
        {
            Warn?.Invoke("Context not implemented: " + context);
        }
    }

    private void VoiceSeparator()
    {
        Mediator.NewSnippet("sim");
        Mediator.SetVoiceNr(add: true);
    }

    private void Change(Change change)
    {
        if (change.Context()?.Text == "Staff") { Mediator.SetStaffNr(0, change.ContextId()); }
    }

    private void KeySignature(KeySignature key)
        => Mediator.NewKey(key.Pitch()?.Output(), key.Mode());

    private void Note(Note note)
    {
        if (note.Length() != Fraction.Zero)
        {
            if (_relative && !_relPitchIsSet)
            {
                Mediator.NewNote(note, false);
                Mediator.SetRelative(note);
                _relPitchIsSet = true;
            }
            else
            {
                Mediator.NewNote(note, _relative);
            }

            CheckNote(note);
            return;
        }

        Node parent = note.Parent();
        if (parent is Relative)
        {
            Mediator.SetRelative(note);
            _relPitchIsSet = true;
        }
        else if (parent is Chord chord)
        {
            if (Mediator.CurrentChord.Count > 0)
            {
                Mediator.NewChord(note, chordBase: false);
            }
            else
            {
                Mediator.NewChord(
                    note, Ly2XmlMediator.ToBaseScaling(chord.Duration), _relative);
                CheckTuplet();
            }

            if (_graceSeq) { Mediator.NewChordGrace(); }
        }
    }

    private void UnpitchedNode(Music.Unpitched unpitched)
    {
        if (unpitched.Length() == Fraction.Zero) { return; }

        Mediator.NewIsoDura(unpitched, _relative, _altMode == "drum");
        CheckNote(unpitched);
    }

    private void DrumNote(DrumNote drumnote)
    {
        if (drumnote.Length() == Fraction.Zero) { return; }

        Mediator.NewNote(drumnote, isUnpitched: true);
        CheckNote(drumnote);
    }

    /// <summary>The checks every note goes through, pitched or not.</summary>
    /// <param name="note">The note.</param>
    public void CheckNote(Item note)
    {
        CheckTuplet();
        if (_graceSeq) { Mediator.NewGrace(); }

        if (_tremRep != 0 && !LookAhead<DurationItem>(note))
        {
            Mediator.SetTremolo(tremType: "start", repeats: _tremRep);
        }
    }

    /// <summary>Applies whatever tuplets are open to the note just described.</summary>
    public void CheckTuplet()
    {
        if (_tuplet.Count == 0) { return; }

        bool nested = _tuplet.Count > 1;
        foreach (TupletLevel td in _tuplet)
        {
            if (nested)
            {
                Mediator.ChangeToTuplet(td.Fraction, td.TupletType, td.Number, td.Length);
            }
            else
            {
                Mediator.ChangeToTuplet(td.Fraction, td.TupletType, td.Number);
            }

            td.TupletType = string.Empty;
        }

        Mediator.CheckDivs();
    }

    private void DurationNode(DurationItem duration)
    {
        if (_tempo != null)
        {
            Mediator.NewTempo(
                duration.Token?.Text, TokenTexts(duration), _tempo.Value.Tempo,
                TextOf(_tempo.Value.Text));
            _tempo = null;
        }
        else if (_tremolo)
        {
            Mediator.SetTremolo(
                duration: int.Parse(duration.Token?.Text ?? "0", CultureInfo.InvariantCulture));
            _tremolo = false;
        }
        else if (_tuplSpan)
        {
            Mediator.SetTuplSpanDur(duration.Token?.Text, TokenTexts(duration));
            _tuplSpan = false;
        }
        else if (_pickup)
        {
            Mediator.SetPickup();
            _pickup = false;
        }
        else
        {
            Mediator.NewDurationToken(duration.Token?.Text, TokenTexts(duration));
            if (_tremRep != 0)
            {
                Mediator.SetTremolo(tremType: "start", repeats: _tremRep);
            }
        }
    }

    private void TempoNode(Tempo tempo)
    {
        if (LookAhead<DurationItem>(tempo)) { _tempo = (tempo.TempoValues(), tempo.Text()); }
        else
        {
            Mediator.NewTempo(
                null, Array.Empty<string>(), tempo.TempoValues(), TextOf(tempo.Text()));
        }
    }

    private void Rest(Rest rest) => Mediator.NewRest(rest);

    private void Skip(Skip skip)
    {
        if (_simsAndSeqs.Contains("lyrics")) { Mediator.NewLyricsItem(skip.Token?.Text); }
        else { Mediator.NewRest(skip); }
    }

    private void Scaler(Scaler scaler)
    {
        string token = scaler.Token?.Text;
        string ttype;
        (int Actual, int Normal) fraction;
        if (token == "\\scaleDurations")
        {
            ttype = string.Empty;
            fraction = (scaler.Denominator, scaler.Numerator);
        }
        else if (token == "\\times")
        {
            ttype = "start";
            fraction = (scaler.Denominator, scaler.Numerator);
        }
        else
        {
            ttype = "start";
            fraction = (scaler.Numerator, scaler.Denominator);
        }

        _tuplet.Add(new TupletLevel
        {
            Fraction = fraction,
            TupletType = ttype,
            Length = scaler.Length(),
            Number = _tuplet.Count + 1,
        });

        if (!LookAhead<DurationItem>(scaler)) { return; }

        _tuplSpan = true;
        _unsetTuplSpan = true;
    }

    private void Articulation(string token, Item node)
    {
        bool isFingering = node is Articulation
            && !string.IsNullOrEmpty(token) && char.IsDigit(token[0]);
        Mediator.NewArticulation(token, isFingering);
    }

    private void Slur(MusicSlur slur)
    {
        if (slur.Token?.Text == "(")
        {
            _slurCount++;
            _slurNr = _slurCount;
            Mediator.SetSlur(_slurNr, "start");
        }
        else if (slur.Token?.Text == ")")
        {
            Mediator.SetSlur(_slurNr, "stop");
            _slurCount--;
        }
    }

    private void PhrasingSlur(PhrasingSlur phrslur)
    {
        if (phrslur.Token?.Text == "\\(")
        {
            _slurCount++;
            _phrSlurNr = _slurCount;
            Mediator.SetSlur(_phrSlurNr, "start", phrasing: true);
        }
        else if (phrslur.Token?.Text == "\\)")
        {
            Mediator.SetSlur(_phrSlurNr, "stop", phrasing: true);
            _slurCount--;
        }
    }

    private void Dynamic(Dynamic dynamic)
        => Mediator.NewDynamics(dynamic.Token?.Text?.Substring(1));

    private void TimeSignatureNode(MusicTimeSignature timeSign)
        => Mediator.NewTime(timeSign.Numerator(), timeSign.Fraction(), _numericTime);

    private void Repeat(Repeat repeat)
    {
        if (repeat.Specifier() == "volta") { Mediator.NewRepeat("forward"); }
        else if (repeat.Specifier() == "tremolo") { _tremRep = repeat.RepeatCount(); }
    }

    private void TremoloNode(Tremolo tremolo)
    {
        if (LookAhead<DurationItem>(tremolo)) { _tremolo = true; }
        else { Mediator.SetTremolo(); }
    }

    private void With(With contWith)
        => _withContext = (contWith.Parent() as ITranslator)?.Context()?.Text
            ?? (contWith.Parent() as ContextItem)?.Context()?.Text;

    private void Set(Set contSet)
    {
        string val;
        if (contSet.Value() is Scheme scheme)
        {
            if (contSet.Property()?.Text == "tupletSpannerDuration")
            {
                Fraction? moment = scheme.GetLyMakeMoment();
                if (moment != null) { Mediator.SetTuplSpanDur(fraction: moment); }
                else { Mediator.UnsetTuplSpanDur(); }

                return;
            }

            val = scheme.GetString();
        }
        else
        {
            val = (contSet.Value() as StringItem)?.Value() ?? contSet.Value()?.PlainText();
        }

        string context = contSet.Context()?.Text;
        if (Array.IndexOf(PnoContexts, context) >= 0 || Array.IndexOf(StaffContexts, context) >= 0)
        {
            Mediator.SetByProperty(contSet.Property()?.Text, val);
        }
        else if (Array.IndexOf(GroupContexts, context) >= 0)
        {
            Mediator.SetByProperty(contSet.Property()?.Text, val, group: true);
        }
    }

    private void Command(Command command)
    {
        string token = command.Token?.Text ?? string.Empty;
        switch (token)
        {
            case "\\rest": Mediator.Note2Rest(); return;
            case "\\numericTimeSignature": _numericTime = true; return;
            case "\\defaultTimeSignature": _numericTime = false; return;
            case "\\glissando":
                Mediator.NewGliss(
                    _overrideDict.TryGetValue("Glissando.style", out string style) ? style : null);
                return;
            case "\\startTrillSpan": Mediator.NewTrillSpanner(); return;
            case "\\stopTrillSpan": Mediator.NewTrillSpanner("stop"); return;
            case "\\ottava": _ottava = true; return;
            case "\\mark":
                _mark = true;
                Mediator.NewMark();
                return;
            case "\\breathe": Mediator.NewArticulation("\\breathe"); return;
            case "\\stemUp":
            case "\\stemDown":
            case "\\stemNeutral":
                Mediator.StemDirection(token);
                return;
            case "\\default":
                if (_tuplSpan)
                {
                    Mediator.UnsetTuplSpanDur();
                    _tuplSpan = false;
                }
                else if (_mark)
                {
                    _mark = false;
                }

                return;
            case "\\compressFullBarRests": Mediator.SetMultRest(); return;
            case "\\break": Mediator.AddBreak(); return;
            default: break;
        }

        //Upstream's `command.token.find('voice') == 1` — the word `voice` at
        //index 1, which is to say right after the backslash.
        if (token.Length > 1 && token.IndexOf("voice", StringComparison.Ordinal) == 1)
        {
            Mediator.SetVoiceNr(token.Substring(1), piano: _pianoStaff);
            return;
        }

        if (Array.IndexOf(CommandExclusions, token) < 0)
        {
            Warn?.Invoke("Unknown command: " + token);
        }
    }

    private void UserCommand(UserCommand usercommand)
    {
        if (usercommand.Name() == "tupletSpan") { _tuplSpan = true; }
    }

    private void StringNode(StringItem str)
    {
        Item prev = GetPreviousNode(str);
        if (prev != null && prev.Token?.Text == "\\bar") { Mediator.CreateBarline(str.Value()); }
    }

    private void LyricsTo(LyricsTo lyricsTo)
    {
        Mediator.NewLyricSection("lyricsto" + lyricsTo.ContextId(), lyricsTo.ContextId());
        _simsAndSeqs.Add("lyrics");
    }

    private void DrumMode(DrumMode drummode)
    {
        if (drummode.Token?.Text == "\\drums") { CheckContext("DrumStaff"); }

        _altMode = "drum";
    }

    private void SchemeItem(Item item)
    {
        string token = item.Token?.Text;
        if (_ottava)
        {
            Mediator.NewOttava(int.Parse(token, CultureInfo.InvariantCulture));
            _ottava = false;
        }
        else if (LookBehind<Override>(item))
        {
            _overrideDict[_overrideKey] = token;
        }
        else if (!string.IsNullOrEmpty(_schmAssignm))
        {
            Mediator.SetByProperty(_schmAssignm, token);
        }
        else if (_mark)
        {
            Mediator.NewMark(int.Parse(token, CultureInfo.InvariantCulture));
        }
        else
        {
            Warn?.Invoke("SchemeItem not implemented: " + token);
        }
    }

    private void End(EndOfNode end)
    {
        Item node = end.Node;
        string token = node.Token?.Text;

        if (node is Scaler scaler)
        {
            if (_unsetTuplSpan)
            {
                Mediator.UnsetTuplSpanDur();
                _unsetTuplSpan = false;
            }

            if (token != "\\scaleDurations")
            {
                Mediator.ChangeTupletType(_tuplet.Count - 1, "stop");
            }

            if (_tuplet.Count > 0) { _tuplet.RemoveAt(_tuplet.Count - 1); }

            return;
        }

        if (node is Grace)
        {
            _graceSeq = false;
            return;
        }

        if (token == "\\repeat" && node is Repeat repeat)
        {
            if (repeat.Specifier() == "volta") { Mediator.NewRepeat("backward"); }
            else if (repeat.Specifier() == "tremolo")
            {
                Mediator.SetTremolo(
                    tremType: LookAhead<MusicList>(repeat) ? "stop" : "single");
                _tremRep = 0;
            }

            return;
        }

        if (node is ContextItem context)
        {
            string name = context.Context()?.Text;
            if (name == "Voice")
            {
                Mediator.CheckVoices();
                PopSimsAndSeqs();
            }
            else if (Array.IndexOf(GroupContexts, name) >= 0)
            {
                Mediator.CloseGroup();
            }
            else if (Array.IndexOf(StaffContexts, name) >= 0)
            {
                if (_pianoStaff == 0) { Mediator.CheckPart(); }
            }
            else if (Array.IndexOf(PnoContexts, name) >= 0)
            {
                Mediator.CheckVoices();
                Mediator.CheckPart();
                _pianoStaff = 0;
                Mediator.SetVoiceNr(nr: 1);
            }
            else if (name == "Devnull")
            {
                Mediator.CheckVoices();
            }

            return;
        }

        switch (token)
        {
            case "<<":
                if (_voiceSep)
                {
                    Mediator.CheckVoicesByNr();
                    Mediator.RevertVoiceNr();
                    _voiceSep = false;
                }
                else if (_pianoStaff == 0)
                {
                    Mediator.CheckSimultan();
                    PopSimsAndSeqs();
                }

                return;
            case "{": PopSimsAndSeqs(); return;
            case "<": Mediator.ChordEnd(); return;
            case "\\lyricsto":
                Mediator.CheckLyrics((node as LyricsTo)?.ContextId());
                PopSimsAndSeqs();
                return;
            case "\\with": _withContext = null; return;
            case "\\drums": Mediator.CheckPart(); return;
            default: break;
        }

        if (node is Relative)
        {
            _relative = false;
            _relPitchIsSet = false;
        }
    }

    // ================= walking the tree =================

    /// <summary>Returns the node before this one, or null when it is first.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The node, or null.</returns>
    public static Item GetPreviousNode(Item node)
    {
        Node parent = node.Parent();
        if (parent == null) { return null; }

        int i = parent.Index(node);
        return i > 0 ? parent[i - 1] as Item : null;
    }

    /// <summary>Yields every node under one, without substitution.</summary>
    /// <param name="node">The node.</param>
    /// <returns>The nodes.</returns>
    public static IEnumerable<Item> SimpleNodeGen(Item node)
    {
        foreach (Node n in node)
        {
            if (n is not Item item) { continue; }

            yield return item;
            foreach (Item s in SimpleNodeGen(item)) { yield return s; }
        }
    }

    /// <summary>Yields the nodes of the document's header, if it has one.</summary>
    /// <param name="tree">The tree.</param>
    /// <returns>The nodes, or null when there is no header.</returns>
    public static IEnumerable<Item> IterHeader(Item tree)
    {
        foreach (Node t in tree)
        {
            if (t is Header header) { return SimpleNodeGen(header); }
        }

        return null;
    }

    /// <summary>Returns the first score or book node, or null.</summary>
    /// <param name="node">The tree.</param>
    /// <returns>The node, or null.</returns>
    public static Item GetScore(Item node)
    {
        foreach (Node n in node)
        {
            if (n is MusicScore or Book) { return (Item)n; }
        }

        return null;
    }

    /// <summary>
    /// Yields the nodes of a score, substituting music variables and unfolding
    /// <c>\repeat unfold</c> as it goes, with an end marker after every
    /// container.
    /// </summary>
    /// <param name="scoreNode">The score.</param>
    /// <param name="doc">The document, which answers the substitutions.</param>
    /// <returns>The nodes.</returns>
    public IEnumerable<Item> IterScore(Item scoreNode, MusicTree doc)
    {
        foreach (Node s in scoreNode)
        {
            if (s is not Item item) { continue; }

            if (item is Repeat repeat && repeat.Specifier() == "unfold")
            {
                foreach (Item u in UnfoldRepeat(repeat, repeat.RepeatCount(), doc))
                {
                    yield return u;
                }

                continue;
            }

            Item n = doc.SubstituteForNode(item) ?? item;
            yield return n;
            foreach (Item c in IterScore(n, doc)) { yield return c; }

            //⚠ BOARD TRAP 2, AGAIN, AND IT REACHES THE WALK. python-ly declares
            //`class Chord(Durable, Container)` — MULTIPLE inheritance — and the
            //port flattened it to `Chord : Durable`, so `is Container` answers
            //false for a chord and no end marker is emitted for one. The
            //consequence is silent and specific: `chord_end` never runs, so
            //whatever was waiting for the next note (a glissando's stop, an
            //ottava) is still waiting when the chord AFTER next arrives, and it
            //fires on that one too. Naming Chord here restores exactly what
            //python's MRO answers. (`Context(Translator, Music)` is the other
            //two-base class; it is a Music, which is a Container, so the port's
            //`ContextItem : Music` already answers true.)
            if (item is Container or Chord) { yield return new EndOfNode(item); }
        }
    }

    /// <summary>Yields a <c>\repeat unfold</c>'s body, once per repeat.</summary>
    /// <param name="repeatNode">The repeat.</param>
    /// <param name="repeatCount">How many times.</param>
    /// <param name="doc">The document.</param>
    /// <returns>The nodes.</returns>
    public IEnumerable<Item> UnfoldRepeat(Item repeatNode, int repeatCount, MusicTree doc)
    {
        for (int r = 0; r < repeatCount; r++)
        {
            foreach (Node n in repeatNode)
            {
                if (n is not Item item) { continue; }

                foreach (Item c in IterScore(item, doc)) { yield return c; }
            }
        }
    }

    /// <summary>
    /// Finds something to stand in for a score: the first music node that is
    /// not an assignment.
    /// </summary>
    /// <param name="doc">The document.</param>
    /// <returns>The nodes, or null.</returns>
    public IEnumerable<Item> FindScoreSub(MusicTree doc)
    {
        foreach (Node n in doc)
        {
            if (n is Assignment) { continue; }

            if (n is MusicNode music) { return IterScore(music, doc); }
        }

        return null;
    }

    /// <summary>Gets whether a node has a child of a kind.</summary>
    /// <typeparam name="T">The kind.</typeparam>
    /// <param name="node">The node.</param>
    /// <returns>True when it has.</returns>
    public static bool LookAhead<T>(Item node)
        where T : Node
    {
        foreach (Node n in node)
        {
            if (n is T) { return true; }
        }

        return false;
    }

    /// <summary>Gets whether a node has an ancestor of a kind.</summary>
    /// <typeparam name="T">The kind.</typeparam>
    /// <param name="node">The node.</param>
    /// <returns>True when it has.</returns>
    public static bool LookBehind<T>(Item node)
        where T : Node
    {
        Node parent = node.Parent();
        if (parent == null) { return false; }

        return parent is T || (parent is Item item && LookBehind<T>(item));
    }

    /// <summary>Returns the class name upstream would dispatch on.</summary>
    /// <param name="item">The node.</param>
    /// <returns>The name.</returns>
    /// <remarks>
    /// Four of the ported types had to be renamed because their python names
    /// collide with something in scope here — <c>Duration</c>, <c>Context</c>,
    /// <c>String</c> and <c>Token</c> (board trap 19's family) — so the walk
    /// asks for the name upstream used rather than the name the type has.
    /// </remarks>
    public static string PythonClassName(Item item) => item switch
    {
        DurationItem => "Duration",
        ContextItem => "Context",
        StringItem => "String",
        TokenItem => "Token",
        EndOfNode => "End",
        _ => item.GetType().Name,
    };

    private void PopSimsAndSeqs()
    {
        if (_simsAndSeqs.Count > 0) { _simsAndSeqs.RemoveAt(_simsAndSeqs.Count - 1); }
    }

    private static IReadOnlyList<string> TokenTexts(Item item)
    {
        var texts = new List<string>();
        if (item?.Tokens == null) { return texts; }

        foreach (Slexing.Token t in item.Tokens) { texts.Add(t.Text); }

        return texts;
    }

    private static string TextOf(Item text) => (text as StringItem)?.Value();

    private static readonly string[] CommandExclusions =
    {
        "\\major", "\\minor", "\\dorian", "\\bar",
    };

    /// <summary>One level of tuplet nesting, while it is open.</summary>
    private sealed class TupletLevel
    {
        internal (int Actual, int Normal) Fraction { get; set; }

        internal string TupletType { get; set; }

        internal Fraction Length { get; set; }

        internal int Number { get; set; }
    }
}
