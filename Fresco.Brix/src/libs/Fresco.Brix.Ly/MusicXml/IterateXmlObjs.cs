// === python-ly ly.musicxml.xml_objs module (class IterateXmlObjs) ===
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

namespace Fresco.Brix.Ly.MusicXml; //was previously: ly/musicxml/xml_objs.py (class IterateXmlObjs)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Walks a <see cref="Score"/> and builds the MusicXML tree from it.
/// </summary>
/// <remarks>
/// Upstream's <c>IterateXmlObjs</c>, whose whole job happens in its constructor.
/// A constructor that does the work reads oddly in C#, so the walk is
/// <see cref="Run"/> and the constructor only takes the three things it needs;
/// the ORDER of everything it does is unchanged, because that order is the
/// order the elements appear in the file.
/// </remarks>
public sealed class IterateXmlObjs
{
    private readonly MusicXmlCreator _musxml;
    private readonly int _divisions;
    private readonly Action<string> _warn;

    /// <summary>Creates a walk over a score.</summary>
    /// <param name="musxml">The builder to write into.</param>
    /// <param name="divisions">The divisions per quarter note.</param>
    /// <param name="warn">Where to say what could not be written, or null.</param>
    public IterateXmlObjs(MusicXmlCreator musxml, int divisions, Action<string> warn = null)
    {
        _musxml = musxml ?? throw new ArgumentNullException(nameof(musxml));
        _divisions = divisions;
        _warn = warn;
    }

    /// <summary>Writes the score's information, then every part in it.</summary>
    /// <param name="score">The score.</param>
    public void Run(Score score)
    {
        if (score == null) { throw new ArgumentNullException(nameof(score)); }

        if (!string.IsNullOrEmpty(score.Title)) { _musxml.CreateTitle(score.Title); }

        foreach (string tag in score.Creators.Keys) { _musxml.AddCreator(tag, score.Creators[tag]); }

        //⚠ RULING FR15. These are the header variables MusicXML has NO element
        //for — subtitle, subsubtitle, dedication, opus, piece, meter,
        //instrument, and whatever else a document invents — and the
        //specification's own answer for them is <miscellaneous-field>. Not one
        //of them may be written as a bare child of <identification>: the
        //content model is a closed sequence and a strict reader rejects the
        //file outright.
        ////was previously: CreateScoreInfo(tag, ...), which emitted
        //<subtitle>…</subtitle> inside <identification> — invalid in every
        //version of the schema. The information is unchanged; its address is
        //now the one the specification names.
        foreach (string tag in score.Info.Keys)
        {
            _musxml.AddMiscellaneousField(tag, score.Info[tag]);
        }

        if (score.Rights.Count > 1)
        {
            foreach ((string value, string type) in score.Rights) { _musxml.AddRights(value, type); }
        }
        else if (score.Rights.Count == 1)
        {
            //Upstream drops the TYPE when there is only one statement, which is
            //what makes a single \copyright come out as a bare <rights>.
            _musxml.AddRights(score.Rights[0].Value);
        }

        foreach (object part in score.PartList)
        {
            if (part is ScorePart scorePart) { IteratePart(scorePart); }
            else if (part is ScorePartGroup group) { IteratePartGroup(group); }
        }
    }

    /// <summary>Writes a group of parts, and any group inside it.</summary>
    /// <param name="group">The group.</param>
    public void IteratePartGroup(ScorePartGroup group)
    {
        _musxml.CreatePartGroup(
            "start", group.Number, group.Name, group.Abbreviation, group.Bracket);
        foreach (object part in group.PartList)
        {
            if (part is ScorePart scorePart) { IteratePart(scorePart); }
            else if (part is ScorePartGroup nested) { IteratePartGroup(nested); }
        }

        _musxml.CreatePartGroup("stop", group.Number);
    }

    /// <summary>Writes one part.</summary>
    /// <param name="part">The part.</param>
    /// <remarks>
    /// The last bar is written only when it holds more than one thing, or when
    /// the one thing it holds has attributes: a part almost always ends with an
    /// empty bar the reader started and never filled.
    /// </remarks>
    public void IteratePart(ScorePart part)
    {
        Bar lastBar = part.LastBar();
        if (lastBar == null)
        {
            _warn?.Invoke("Warning: empty part: " + part.Name);
            return;
        }

        List<BarObject> lastBarObjs = lastBar.ObjList;
        part.SetFirstBar(_divisions, _warn);
        _musxml.CreatePart(part.Name, part.Abbreviation, part.Midi);
        for (int i = 0; i < part.BarList.Count - 1; i++) { IterateBar(part.BarList[i]); }

        if (lastBarObjs.Count > 1 || (lastBarObjs.Count > 0 && lastBarObjs[0].HasAttr()))
        {
            IterateBar(lastBar);
        }
    }

    /// <summary>Writes one bar.</summary>
    /// <param name="bar">The bar.</param>
    public void IterateBar(Bar bar)
    {
        _musxml.CreateMeasure(bar.Pickup);
        foreach (BarObject obj in bar.ObjList)
        {
            if (obj is BarAttr attr)
            {
                NewXmlBarAttr(attr);
            }
            else if (obj is BarMus mus)
            {
                BeforeNote(mus);
                if (mus is BarNote note) { NewXmlNote(note); }
                else if (mus is BarRest rest) { NewXmlRest(rest); }

                GenerateXmlMus(mus);
                AfterNote(mus);
            }
            else if (obj is BarBackup backup)
            {
                _musxml.AddBackup(CountDuration(backup.Duration, _divisions));
            }
        }
    }

    /// <summary>Writes a bar's attributes.</summary>
    /// <param name="obj">The attributes.</param>
    public void NewXmlBarAttr(BarAttr obj)
    {
        if (obj.HasAttr())
        {
            _musxml.NewBarAttribute(new BarAttributesRequest
            {
                Clef = obj.Clef,
                Time = obj.Time,
                Key = obj.Key,
                Mode = obj.Mode,
                Divisions = obj.Divisions,
                MultiRest = obj.MultiRest ?? 0,
            });
        }

        if (!string.IsNullOrEmpty(obj.NewSystem)) { _musxml.NewSystem(obj.NewSystem); }

        if (!string.IsNullOrEmpty(obj.Repeat)) { _musxml.AddBarline(obj.Barline, obj.Repeat); }
        else if (!string.IsNullOrEmpty(obj.Barline)) { _musxml.AddBarline(obj.Barline); }

        if (obj.StavesCount != 0) { _musxml.AddStaves(obj.StavesCount); }

        foreach ((ClefSignature clef, string number) in obj.MultiClef)
        {
            _musxml.AddClef(clef.Sign, clef.Line, number, clef.OctaveChange);
        }

        if (obj.Tempo != null)
        {
            _musxml.CreateTempo(
                obj.Tempo.Text, obj.Tempo.Metronome, obj.Tempo.Midi, obj.Tempo.Dots);
        }

        if (!string.IsNullOrEmpty(obj.Mark)) { _musxml.AddMark(obj.Mark); }

        if (!string.IsNullOrEmpty(obj.Word)) { _musxml.AddDirectionWords(obj.Word); }
    }

    /// <summary>Writes what goes before a note.</summary>
    /// <param name="obj">The note or rest.</param>
    public void BeforeNote(BarMus obj)
    {
        AddDynamics(obj.Dynamic, before: true);
        if (obj.OctShift != null && obj.OctShift.OctaveDirection != "stop")
        {
            _musxml.AddOctaveShift(
                obj.OctShift.Placement, obj.OctShift.OctaveDirection, obj.OctShift.Size);
        }
    }

    /// <summary>Writes what goes after a note.</summary>
    /// <param name="obj">The note or rest.</param>
    public void AfterNote(BarMus obj)
    {
        AddDynamics(obj.Dynamic, before: false);
        if (obj.OctShift != null && obj.OctShift.OctaveDirection == "stop")
        {
            _musxml.AddOctaveShift(
                obj.OctShift.Placement, obj.OctShift.OctaveDirection, obj.OctShift.Size);
        }
    }

    /// <summary>Writes what a note and a rest have in common.</summary>
    /// <param name="obj">The note or rest.</param>
    public void GenerateXmlMus(BarMus obj)
    {
        foreach (Tuplet t in obj.Tuplet)
        {
            _musxml.TupletNote(
                t.Fraction, (obj.Duration.Base, obj.Duration.Scaling), t.TupletType, t.Number,
                _divisions, t.ActualType, t.NormalType);
        }

        if (!string.IsNullOrEmpty(obj.Staff) && !obj.Skip) { _musxml.AddStaff(obj.Staff); }

        if (!string.IsNullOrEmpty(obj.OtherNotation))
        {
            _musxml.AddNamedNotation(obj.OtherNotation);
        }
    }

    /// <summary>Writes a note.</summary>
    /// <param name="obj">The note.</param>
    public void NewXmlNote(BarNote obj)
    {
        int divdur = CountDuration(obj.Duration, _divisions);
        if (obj is Unpitched)
        {
            _musxml.NewUnpitchedNote(
                obj.BaseNote, obj.Octave, obj.Type, divdur, obj.Voice, obj.Dot, obj.Chord, obj.Grace);
        }
        else
        {
            _musxml.NewNote(
                obj.BaseNote, obj.Octave, obj.Type, divdur, obj.Alter, obj.AccidentalToken,
                obj.Voice, obj.Dot, obj.Chord, obj.Grace, obj.StemDirection);
        }

        foreach (string t in obj.Tie) { _musxml.TieNote(t); }

        foreach (Slur s in obj.Slur) { _musxml.AddSlur(s.Number, s.SlurType); }

        foreach (string a in obj.Artic) { _musxml.NewArticulation(a); }

        if (!string.IsNullOrEmpty(obj.Ornament)) { _musxml.NewSimpleOrnament(obj.Ornament); }

        if (obj.AdvOrnament != null)
        {
            _musxml.NewAdvancedOrnament(obj.AdvOrnament.Value.Name, obj.AdvOrnament.Value.Type);
        }

        if (obj.Tremolo.Lines != 0) { _musxml.AddTremolo(obj.Tremolo.Type, obj.Tremolo.Lines); }

        if (obj.Gliss != null)
        {
            _musxml.AddGlissando(
                obj.Gliss.Value.Line, obj.Gliss.Value.EndType, obj.Gliss.Value.Number);
        }

        if (obj.Fingering != null && obj.Fingering.Value != 0)
        {
            _musxml.AddFingering(obj.Fingering.Value);
        }

        if (obj.Lyric == null) { return; }

        foreach (LyricEntry l in obj.Lyric)
        {
            //Upstream reads a fourth element and catches the IndexError when it
            //is not there; "there is no fourth element" and "there is no
            //melisma" are the same fact.
            _musxml.AddLyric(l.Text, l.Syllabic, l.Number, l.Extend);
        }
    }

    /// <summary>Writes a rest, or the gap a skip leaves.</summary>
    /// <param name="obj">The rest.</param>
    public void NewXmlRest(BarRest obj)
    {
        int divdur = CountDuration(obj.Duration, _divisions);
        if (obj.Skip) { _musxml.AddSkip(divdur); }
        else { _musxml.NewRest(divdur, obj.Type, obj.Pos, obj.Dot, obj.Voice); }
    }

    /// <summary>Turns a written duration into a whole number of divisions.</summary>
    /// <param name="baseScaling">The written duration.</param>
    /// <param name="divisions">The divisions per quarter note.</param>
    /// <returns>The duration in divisions, truncated as python's <c>int()</c> does.</returns>
    public static int CountDuration(BaseScaling baseScaling, int divisions)
    {
        Fraction duration = new Fraction(divisions * 4L) * baseScaling.Base * baseScaling.Scaling;
        return (int)Truncate(duration);
    }

    private static long Truncate(Fraction value)
    {
        //python's int() truncates towards zero; a Fraction of a negative
        //duration cannot arise here, but the rule is the rule.
        long quotient = value.Numerator / value.Denominator;
        return quotient;
    }

    private void AddDynamics(IEnumerable<Dynamics> dynamics, bool before)
    {
        foreach (Dynamics d in dynamics)
        {
            if (d.Before != before) { continue; }

            switch (d)
            {
                case DynamicsMark mark: _musxml.AddDynamicMark(mark.Sign); break;
                case DynamicsWedge wedge: _musxml.AddDynamicWedge(wedge.Sign); break;
                case DynamicsText text: _musxml.AddDynamicText(text.Sign); break;
                case DynamicsDashes dashes: _musxml.AddDynamicDashes(dashes.Sign); break;
                default: break;
            }
        }
    }
}
