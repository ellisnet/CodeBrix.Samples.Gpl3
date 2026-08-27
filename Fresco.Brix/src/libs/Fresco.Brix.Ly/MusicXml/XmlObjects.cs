// === python-ly ly.musicxml.xml_objs module (the score object model) ===
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

namespace Fresco.Brix.Ly.MusicXml; //was previously: ly/musicxml/xml_objs.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A written duration: its base value and whatever <c>*n/m</c> scaled it.
/// </summary>
/// <remarks>
/// Upstream carries this as the bare two-Fraction tuple <c>ly.duration
/// .base_scaling</c> returns, and passes it around under the name
/// <c>base_scaling</c> in one place and <c>duration</c> in another. Naming it
/// costs nothing and says which of the two numbers is which at every call site.
/// </remarks>
public readonly struct BaseScaling : IEquatable<BaseScaling>
{
    /// <summary>Creates a duration.</summary>
    /// <param name="baseValue">The written value, in whole notes.</param>
    /// <param name="scaling">What scaled it.</param>
    public BaseScaling(Fraction baseValue, Fraction scaling)
    {
        Base = baseValue;
        Scaling = scaling;
    }

    /// <summary>Gets the written value.</summary>
    public Fraction Base { get; }

    /// <summary>Gets what scaled it.</summary>
    public Fraction Scaling { get; }

    /// <inheritdoc/>
    public bool Equals(BaseScaling other) => Base == other.Base && Scaling == other.Scaling;

    /// <inheritdoc/>
    public override bool Equals(object obj) => obj is BaseScaling other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Base, Scaling);

    /// <inheritdoc/>
    public override string ToString() => "(" + Base + ", " + Scaling + ")";

    /// <summary>Compares two durations.</summary>
    /// <param name="left">One.</param>
    /// <param name="right">The other.</param>
    /// <returns>True when they are the same.</returns>
    public static bool operator ==(BaseScaling left, BaseScaling right) => left.Equals(right);

    /// <summary>Compares two durations.</summary>
    /// <param name="left">One.</param>
    /// <param name="right">The other.</param>
    /// <returns>True when they differ.</returns>
    public static bool operator !=(BaseScaling left, BaseScaling right) => !left.Equals(right);
}

/// <summary>
/// Anything that can sit in a bar: an attribute, a note or rest, or a backup.
/// </summary>
/// <remarks>
/// There is no such base upstream — a bar's list is walked with
/// <c>isinstance</c> — but a typed list needs one, and it is also where
/// <see cref="HasAttr"/> belongs.
/// <para>
/// ⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14). <c>Bar.is_skip</c> calls
/// <c>has_attr()</c> on EVERY object in a bar, and <c>BarBackup</c> is the one
/// bar object that does not define it — so a bar holding a backup raises
/// <c>AttributeError</c> and kills the export. It looked unreachable and is not:
/// it fires the moment a global section is merged into a part that already has a
/// backup, which is what <c>\new Devnull</c> arranges once the <c>check_voices</c>
/// fix lets that merge happen at all. Both other bar objects answer the
/// question, and the answer for a backup is plainly false — it carries no
/// attributes.
/// </para>
/// </remarks>
public abstract class BarObject
{
    /// <summary>Gets whether this object carries bar attributes.</summary>
    /// <returns>True when it does.</returns>
    public virtual bool HasAttr() => false;
}

/// <summary>A whole score: its parts, its title and who made it.</summary>
public sealed class Score
{
    /// <summary>Gets the parts and part groups, in order.</summary>
    public List<object> PartList { get; } = new List<object>();

    /// <summary>Gets or sets the score's title.</summary>
    public string Title { get; set; }

    /// <summary>Gets the creators, keyed by the kind of creator.</summary>
    /// <remarks>Insertion-ordered, because that is the order the elements come
    /// out in and python 3.7 and later give a dict that order.</remarks>
    public OrderedStringMap Creators { get; } = new OrderedStringMap();

    /// <summary>Gets the other score information, keyed by element name.</summary>
    public OrderedStringMap Info { get; } = new OrderedStringMap();

    /// <summary>Gets the rights statements, each with its type.</summary>
    public List<(string Value, string Type)> Rights { get; }
        = new List<(string Value, string Type)>();

    /// <summary>Gets or sets the section holding what is true of the whole score.</summary>
    /// <remarks>
    /// SETTABLE, because upstream REBINDS it — <c>check_part</c> ends by
    /// replacing it with a fresh attributes-only section extracted from the part
    /// just closed. Mutating the old object in place is not the same thing:
    /// anything still holding the old one would see the new contents.
    /// </remarks>
    public ScoreSection GlobalSection { get; set; } = new ScoreSection("global", true);

    /// <summary>Adds a rights statement.</summary>
    /// <param name="value">The statement.</param>
    /// <param name="type">What kind of rights.</param>
    public void AddRight(string value, string type) => Rights.Add((value, type));

    /// <summary>Gets whether the score has no parts at all.</summary>
    /// <returns>True when it is empty.</returns>
    public bool IsEmpty() => PartList.Count == 0;

    /// <summary>Merges a section into every part.</summary>
    /// <param name="section">The section.</param>
    /// <param name="override">Whether it replaces what is already there.</param>
    public void MergeGlobally(ScoreSection section, bool @override = false)
    {
        foreach (object part in PartList)
        {
            if (part is ScorePart scorePart) { scorePart.MergeVoice(section, @override); }
            else if (part is ScorePartGroup group) { group.MergeVoice(section, @override); }
        }
    }
}

/// <summary>A dictionary of strings that remembers the order keys were added in.</summary>
/// <remarks>
/// python's dict has kept insertion order since 3.7, and the exporter relies on
/// it: the creators and the score information come out of the file in the order
/// the document mentioned them. A plain <c>Dictionary</c> does not promise that.
/// </remarks>
public sealed class OrderedStringMap
{
    private readonly List<string> _keys = new List<string>();
    private readonly Dictionary<string, string> _values
        = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Gets or sets a value, adding the key at the end when it is new.</summary>
    /// <param name="key">The key.</param>
    /// <returns>The value, or null.</returns>
    public string this[string key]
    {
        get => _values.TryGetValue(key, out string value) ? value : null;

        set
        {
            if (!_values.ContainsKey(key)) { _keys.Add(key); }

            _values[key] = value;
        }
    }

    /// <summary>Gets the keys, in the order they were first set.</summary>
    public IReadOnlyList<string> Keys => _keys;

    /// <summary>Gets how many keys there are.</summary>
    public int Count => _keys.Count;

    /// <summary>Gets whether a key has been set.</summary>
    /// <param name="key">The key.</param>
    /// <returns>True when it has.</returns>
    public bool ContainsKey(string key) => _values.ContainsKey(key);
}

/// <summary>A bracketed group of parts.</summary>
public sealed class ScorePartGroup
{
    /// <summary>Creates a group.</summary>
    /// <param name="number">Its number.</param>
    /// <param name="bracket">What bracket it is drawn with.</param>
    public ScorePartGroup(int number, string bracket)
    {
        Number = number;
        Bracket = bracket;
    }

    /// <summary>Gets or sets the bracket the group is drawn with.</summary>
    public string Bracket { get; set; }

    /// <summary>Gets the parts and nested groups.</summary>
    public List<object> PartList { get; } = new List<object>();

    /// <summary>Gets or sets the group's name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the group's short name.</summary>
    public string Abbreviation { get; set; } = string.Empty;

    /// <summary>Gets or sets the group this one is inside, or null.</summary>
    public ScorePartGroup Parent { get; set; }

    /// <summary>Gets the group's number.</summary>
    public int Number { get; }

    /// <summary>Sets the bracket the group is drawn with.</summary>
    /// <param name="bracket">The bracket.</param>
    public void SetBracket(string bracket) => Bracket = bracket;

    /// <summary>Merges a section into every part of the group.</summary>
    /// <param name="voice">The section.</param>
    /// <param name="override">Whether it replaces what is already there.</param>
    public void MergeVoice(ScoreSection voice, bool @override = false)
    {
        foreach (object part in PartList)
        {
            if (part is ScorePart scorePart) { scorePart.MergeVoice(voice, @override); }
            else if (part is ScorePartGroup group) { group.MergeVoice(voice, @override); }
        }
    }
}

/// <summary>How many slurs a section has open.</summary>
public sealed class SlurCount
{
    /// <summary>Gets or sets the count.</summary>
    public int Count { get; set; }

    /// <summary>Counts one more open slur.</summary>
    public void Increment() => Count++;

    /// <summary>Counts one fewer open slur.</summary>
    public void Decrement() => Count--;
}

/// <summary>A stretch of music, as a list of bars.</summary>
public class ScoreSection
{
    /// <summary>Creates a section.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="global">Whether it holds what is true of the whole score.</param>
    public ScoreSection(string name, bool @global = false)
    {
        Name = name;
        Global = @global;
    }

    /// <summary>Gets or sets the section's name.</summary>
    public string Name { get; set; }

    /// <summary>Gets the bars.</summary>
    public List<Bar> BarList { get; } = new List<Bar>();

    /// <summary>Gets whether the section holds what is true of the whole score.</summary>
    public bool Global { get; }

    /// <summary>Gets how many slurs the section has open.</summary>
    public SlurCount ActiveSlurCount { get; } = new SlurCount();

    /// <inheritdoc/>
    public override string ToString() => "<" + GetType().Name + " " + Name + ">";

    /// <summary>Merges another section's bars into this one, bar by bar.</summary>
    /// <param name="voice">The other section.</param>
    /// <param name="override">Whether its attributes replace this one's.</param>
    public void MergeVoice(ScoreSection voice, bool @override = false)
    {
        int shared = Math.Min(BarList.Count, voice.BarList.Count);
        for (int i = 0; i < shared; i++)
        {
            BarList[i].InjectVoice(voice.BarList[i], @override, ActiveSlurCount);
        }

        for (int i = BarList.Count; i < voice.BarList.Count; i++) { BarList.Add(voice.BarList[i]); }
    }

    /// <summary>Puts a lyrics section's syllables onto this section's notes.</summary>
    /// <param name="lyrics">The lyrics.</param>
    /// <remarks>
    /// One syllable per note, in order, EXCEPT while a melisma is running: a
    /// syllable marked <c>__</c> on a slurred note starts one, and the notes
    /// under the rest of that slur take no syllable at all.
    /// </remarks>
    public void MergeLyrics(LyricsSection lyrics)
    {
        int i = 0;
        bool extending = false;
        foreach (Bar bar in BarList)
        {
            foreach (BarObject obj in bar.ObjList)
            {
                if (obj is not BarNote note) { continue; }

                if (extending)
                {
                    if (note.Slur.Count > 0) { extending = false; }

                    continue;
                }

                if (i >= lyrics.LyricList.Count) { return; }

                LyricEntry entry = lyrics.LyricList[i];
                if (!entry.IsSkip)
                {
                    if (entry.Extend && note.Slur.Count > 0) { extending = true; }

                    note.AddLyric(entry);
                }

                i++;
            }
        }
    }
}

/// <summary>A short section written to be merged into another.</summary>
public sealed class Snippet : ScoreSection
{
    /// <summary>Creates a snippet.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="mergeInto">The bars it is destined for.</param>
    public Snippet(string name, List<Bar> mergeInto)
        : base(name) => MergeBarList = mergeInto;

    /// <summary>Gets the bars this snippet will be merged into.</summary>
    public List<Bar> MergeBarList { get; }
}

/// <summary>One syllable of lyrics, or the marker for a note that gets none.</summary>
/// <remarks>
/// Upstream keeps each of these as a plain list — <c>[text, syllabic, number]</c>
/// with the string <c>"extend"</c> appended when a melisma follows — or the bare
/// string <c>"skip"</c>. Reading it back is then done by index, and by catching
/// the <c>IndexError</c> when there is no fourth element. Naming the four things
/// says what each one is and turns that <c>IndexError</c> into a boolean.
/// </remarks>
public sealed class LyricEntry
{
    /// <summary>Gets or sets the syllable.</summary>
    public string Text { get; set; }

    /// <summary>Gets or sets where in its word the syllable sits.</summary>
    public string Syllabic { get; set; }

    /// <summary>Gets or sets which verse it belongs to.</summary>
    public int Number { get; set; }

    /// <summary>Gets or sets whether a melisma line follows it.</summary>
    public bool Extend { get; set; }

    /// <summary>Gets or sets whether this stands for a note that takes no syllable.</summary>
    public bool IsSkip { get; set; }

    /// <summary>Returns the marker for a note that takes no syllable.</summary>
    /// <returns>The marker.</returns>
    public static LyricEntry Skip() => new LyricEntry { IsSkip = true };
}

/// <summary>The lyrics for one voice, waiting to be put onto its notes.</summary>
/// <remarks>
/// ⚠ Upstream stores these in the section's own <c>barlist</c> — the very list
/// that holds Bar objects in a music section — so a lyrics section's "bars" are
/// lists of strings. That is python duck typing and it does not survive a typed
/// list, so the syllables live in a list of their own here. Nothing else
/// changes: <see cref="ScoreSection.MergeLyrics"/> reads this list exactly where
/// upstream reads that one.
/// </remarks>
public sealed class LyricsSection : ScoreSection
{
    /// <summary>Creates a lyrics section.</summary>
    /// <param name="name">Its name.</param>
    /// <param name="voiceId">Which voice it belongs to.</param>
    public LyricsSection(string name, string voiceId)
        : base(name) => VoiceId = voiceId;

    /// <summary>Gets which voice the lyrics belong to.</summary>
    public string VoiceId { get; }

    /// <summary>Gets the syllables, in order.</summary>
    public List<LyricEntry> LyricList { get; } = new List<LyricEntry>();
}

/// <summary>One part of the score.</summary>
public sealed class ScorePart : ScoreSection
{
    /// <summary>Creates a part.</summary>
    /// <param name="staves">How many staves it has, or zero for one.</param>
    /// <param name="partId">Its identifier, or null.</param>
    /// <param name="toPart">The part it is to be merged into, or null.</param>
    /// <param name="name">Its name.</param>
    public ScorePart(int staves = 0, string partId = null, ScorePart toPart = null, string name = "")
        : base(name)
    {
        PartId = partId;
        ToPart = toPart;
        Staves = staves;
    }

    /// <summary>Gets or sets the part's identifier.</summary>
    public string PartId { get; set; }

    /// <summary>Gets or sets the part this one is merged into, or null.</summary>
    public ScorePart ToPart { get; set; }

    /// <summary>Gets or sets the part's short name.</summary>
    public string Abbreviation { get; set; } = string.Empty;

    /// <summary>Gets or sets the part's MIDI instrument name.</summary>
    public string Midi { get; set; } = string.Empty;

    /// <summary>Gets or sets how many staves the part has.</summary>
    public int Staves { get; set; }

    /// <inheritdoc/>
    public override string ToString() => "<" + GetType().Name + " " + Name + " " + PartId + ">";

    /// <summary>
    /// Makes sure the first bar states a time signature, a clef and the
    /// divisions, so a reader has something to start from.
    /// </summary>
    /// <param name="divisions">The divisions per quarter note.</param>
    /// <param name="warn">Where to say what could not be set, or null.</param>
    public void SetFirstBar(int divisions, Action<string> warn = null)
    {
        var initialTime = new TimeSignature("4", "4");
        var initialClef = new ClefSignature("G", 2, 0);

        if (BarList.Count == 0 || BarList[0].ObjList.Count == 0) { return; }

        BarObject first = BarList[0].ObjList[0];

        if (!CheckTime(BarList[0]))
        {
            if (first is BarAttr timeAttr) { timeAttr.SetTime(initialTime, numeric: false); }
            else { warn?.Invoke("Warning can't set initial time sign!"); }
        }

        if (!CheckClef(BarList[0]))
        {
            if (first is BarAttr clefAttr) { clefAttr.SetClef(initialClef); }
            else { warn?.Invoke("Warning can't set initial clef sign!"); }
        }

        //Upstream sets these two straight onto the object without checking what
        //it is, so a first object that is not an attribute raises here — after
        //the two guarded settings above have already reported themselves. Kept
        //as a guard rather than a throw: the two warnings have already said the
        //bar is not shaped the way the exporter needs.
        if (first is not BarAttr attr) { return; }

        attr.Divisions = divisions;
        if (Staves != 0) { attr.StavesCount = Staves; }
    }

    /// <summary>Merges this part into the one it names.</summary>
    public void MergePartToPart()
    {
        if (ToPart == null) { return; }

        if (ToPart.BarList.Count > 0) { ToPart.MergeVoice(this); }
        else { ToPart.BarList.AddRange(BarList); }
    }

    /// <summary>
    /// Copies out only what is true of the whole score — key, time, mode,
    /// bar lines, repeats and tempo — into a section of its own.
    /// </summary>
    /// <param name="name">What to call it.</param>
    /// <returns>The section.</returns>
    public ScoreSection ExtractGlobalToSection(string name)
    {
        var section = new ScoreSection(name, true);
        foreach (Bar bar in BarList)
        {
            var sectionBar = new Bar();
            foreach (BarObject obj in bar.ObjList)
            {
                if (obj is not BarAttr attr) { continue; }

                var global = new BarAttr
                {
                    Key = attr.Key,
                    Time = attr.Time,
                    Mode = attr.Mode,
                    Barline = attr.Barline,
                    Repeat = attr.Repeat,
                    Tempo = attr.Tempo,
                };
                sectionBar.ObjList.Add(global);
            }

            section.BarList.Add(sectionBar);
        }

        return section;
    }

    /// <summary>Returns the part's last bar, or null when it has none.</summary>
    /// <returns>The bar, or null.</returns>
    /// <remarks>
    /// Upstream walks the list backwards looking for the first thing that IS a
    /// Bar, because its list is untyped; here everything in it is one.
    /// </remarks>
    public Bar LastBar() => BarList.Count == 0 ? null : BarList[BarList.Count - 1];

    private static bool CheckTime(Bar bar)
    {
        foreach (BarObject obj in bar.ObjList)
        {
            if (obj is BarAttr attr && attr.Time != null) { return true; }

            if (obj is BarMus) { return false; }
        }

        return false;
    }

    private static bool CheckClef(Bar bar)
    {
        foreach (BarObject obj in bar.ObjList)
        {
            if (obj is BarAttr attr && (attr.Clef != null || attr.MultiClef.Count > 0))
            {
                return true;
            }

            if (obj is BarMus) { return false; }
        }

        return false;
    }
}

/// <summary>One bar, and what is in it.</summary>
public sealed class Bar
{
    /// <summary>Gets what is in the bar, in order.</summary>
    public List<BarObject> ObjList { get; } = new List<BarObject>();

    /// <summary>Gets or sets whether the bar is an anacrusis.</summary>
    public bool Pickup { get; set; }

    /// <summary>Gets or sets whether the bar is as full as it is going to get.</summary>
    public bool ListFull { get; set; }

    /// <inheritdoc/>
    public override string ToString() => "<Bar " + ObjList.Count + " objects>";

    /// <summary>Adds something to the bar.</summary>
    /// <param name="obj">The thing.</param>
    public void Add(BarObject obj) => ObjList.Add(obj);

    /// <summary>Gets whether the bar has any notes or rests in it.</summary>
    /// <returns>True when it has.</returns>
    public bool HasMusic()
    {
        foreach (BarObject obj in ObjList)
        {
            if (obj is BarMus) { return true; }
        }

        return false;
    }

    /// <summary>Gets whether the bar carries any attributes.</summary>
    /// <returns>True when it does.</returns>
    public bool HasAttr()
    {
        foreach (BarObject obj in ObjList)
        {
            if (obj is BarAttr) { return true; }
        }

        return false;
    }

    /// <summary>
    /// Works out how far back the writing position has to go for another voice,
    /// and adds the backup that does it.
    /// </summary>
    public void CreateBackup()
    {
        Fraction b = new Fraction(0);
        Fraction s = new Fraction(1);
        foreach (BarObject obj in ObjList)
        {
            if (obj is BarMus mus)
            {
                if (mus.Chord) { continue; }

                b += mus.Duration.Base;
                s *= mus.Duration.Scaling;
            }
            else if (obj is BarBackup)
            {
                break;
            }
        }

        Add(new BarBackup(new BaseScaling(b, s)));
    }

    /// <summary>Gets whether a list of bar objects is nothing but skips.</summary>
    /// <param name="objList">The list, or null for this bar's own.</param>
    /// <returns>True when there is nothing in it that would be written.</returns>
    public bool IsSkip(IReadOnlyList<BarObject> objList = null)
    {
        //Upstream's `if not obj_list` is falsy for an EMPTY list as well as for
        //None, so an empty list means "use my own" there too.
        IReadOnlyList<BarObject> list = objList == null || objList.Count == 0 ? ObjList : objList;
        foreach (BarObject obj in list)
        {
            if (obj.HasAttr()) { return false; }

            if (obj is BarNote) { return false; }

            if (obj is BarRest rest && !rest.Skip) { return false; }
        }

        return true;
    }

    /// <summary>Adds another voice's bar into this one.</summary>
    /// <param name="newVoice">The other bar.</param>
    /// <param name="override">Whether its attributes replace this bar's.</param>
    /// <param name="activeSlurCount">The section's open-slur count, or null.</param>
    /// <remarks>
    /// Double or conflicting bar attributes are dropped unless
    /// <paramref name="override"/> says otherwise, and a voice that is nothing
    /// but skips is added without a backup — there is nothing to back up for.
    /// The slur numbering is the delicate part: a slur opened in the added
    /// voice takes the next free number in the SECTION, and the slur that
    /// closes it takes the number its opener got.
    /// </remarks>
    public void InjectVoice(Bar newVoice, bool @override = false, SlurCount activeSlurCount = null)
    {
        if (newVoice == null || newVoice.ObjList.Count == 0) { return; }

        List<BarObject> backupList;
        if (newVoice.ObjList[0].HasAttr())
        {
            if (ObjList.Count > 0 && ObjList[0].HasAttr())
            {
                ((BarAttr)ObjList[0]).MergeAttr((BarAttr)newVoice.ObjList[0], @override);
            }
            else
            {
                ObjList.Insert(0, newVoice.ObjList[0]);
            }

            backupList = newVoice.ObjList.GetRange(1, newVoice.ObjList.Count - 1);
        }
        else
        {
            backupList = new List<BarObject>(newVoice.ObjList);
        }

        //Upstream reads `.barline` off whatever is last in each list and catches
        //the AttributeError when it is not an attribute object; asking the type
        //is the same question.
        if (ObjList.Count > 0 && newVoice.ObjList.Count > 0
            && ObjList[ObjList.Count - 1] is BarAttr mine
            && newVoice.ObjList[newVoice.ObjList.Count - 1] is BarAttr theirs
            && !string.IsNullOrEmpty(mine.Barline) && !string.IsNullOrEmpty(theirs.Barline))
        {
            ObjList.RemoveAt(ObjList.Count - 1);
        }

        if (IsSkip(backupList)) { return; }

        CreateBackup();

        if (activeSlurCount != null)
        {
            //Count what is already open in this bar before the other voice's
            //notes arrive, or the numbers the new slurs take will collide.
            foreach (BarObject obj in ObjList)
            {
                if (obj is not BarNote note) { continue; }

                foreach (Slur slur in note.Slur)
                {
                    if (slur.SlurType == "start") { activeSlurCount.Increment(); }
                    else if (slur.SlurType == "stop") { activeSlurCount.Decrement(); }
                }
            }
        }

        foreach (BarObject obj in backupList)
        {
            Add(obj);

            if (activeSlurCount == null || obj is not BarNote note) { continue; }

            foreach (Slur slur in note.Slur)
            {
                if (slur.SlurType == "start")
                {
                    activeSlurCount.Increment();
                    slur.Number = activeSlurCount.Count;
                }
                else if (slur.SlurType == "stop")
                {
                    activeSlurCount.Decrement();
                    if (slur.StartNode != null) { slur.Number = slur.StartNode.Number; }
                }
            }
        }
    }
}

/// <summary>What notes and rests have in common.</summary>
public abstract class BarMus : BarObject
{
    /// <summary>Creates a note or rest.</summary>
    /// <param name="duration">How long it is.</param>
    /// <param name="voice">Which voice it belongs to.</param>
    protected BarMus(BaseScaling duration, int voice = 1)
    {
        Duration = duration;
        Voice = voice;
    }

    /// <summary>Gets or sets how long it is.</summary>
    public BaseScaling Duration { get; set; }

    /// <summary>Gets or sets its written duration, such as <c>quarter</c>.</summary>
    public string Type { get; set; }

    /// <summary>Gets the tuplets it is part of.</summary>
    public List<Tuplet> Tuplet { get; } = new List<Tuplet>();

    /// <summary>Gets or sets how many augmentation dots it has.</summary>
    public int Dot { get; set; }

    /// <summary>Gets or sets which voice it belongs to.</summary>
    public int Voice { get; set; }

    /// <summary>Gets or sets which staff it is written on, or null for none.</summary>
    /// <remarks>
    /// ⚠ A STRING, not a number, and upstream's field is the same thing wearing
    /// python's type-freedom. It normally holds a staff number; but a
    /// <c>\change Staff = "x"</c> naming a context that has not been seen yet
    /// puts the CONTEXT IDENTIFIER here instead, and the note keeps carrying it
    /// until <c>AddStaffId</c> resolves it. Either way what reaches the file is
    /// <c>str(staff)</c>, so a string is what it is.
    /// </remarks>
    public string Staff { get; set; }

    /// <summary>Gets or sets whether it belongs to the chord before it.</summary>
    public bool Chord { get; set; }

    /// <summary>Gets or sets a notation that is nothing but its name, or null.</summary>
    public string OtherNotation { get; set; }

    /// <summary>Gets the dynamics attached to it.</summary>
    public List<Dynamics> Dynamic { get; } = new List<Dynamics>();

    /// <summary>Gets or sets the octave shift it starts or ends, or null.</summary>
    public OctaveShift OctShift { get; set; }

    /// <summary>Gets whether it is a skip rather than something written.</summary>
    public virtual bool Skip => false;

    /// <inheritdoc/>
    public override string ToString() => "<" + GetType().Name + " " + Duration + ">";

    /// <summary>Makes it part of a tuplet.</summary>
    /// <param name="fraction">The ratio, actual over normal.</param>
    /// <param name="tupletType">What the bracket does.</param>
    /// <param name="number">Which tuplet.</param>
    /// <param name="actualType">The actual note type shown.</param>
    /// <param name="normalType">The normal note type shown.</param>
    public void SetTuplet(
        (int Actual, int Normal) fraction, string tupletType, int number,
        string actualType = "", string normalType = "")
        => Tuplet.Add(new Tuplet(fraction, tupletType, number, actualType, normalType));

    /// <summary>Puts it on a staff.</summary>
    /// <param name="staff">The staff.</param>
    public void SetStaff(string staff) => Staff = staff;

    /// <summary>Gives it one more augmentation dot.</summary>
    public void AddDot() => Dot++;

    /// <summary>Attaches a notation that is nothing but its name.</summary>
    /// <param name="other">The notation.</param>
    public void AddOtherNotation(string other) => OtherNotation = other;

    /// <summary>Attaches a dynamic mark.</summary>
    /// <param name="sign">Its element name.</param>
    /// <param name="before">Whether it is written before the note.</param>
    public void SetDynamicsMark(string sign, bool before = true)
        => Dynamic.Add(new DynamicsMark(sign, before));

    /// <summary>Attaches a hairpin.</summary>
    /// <param name="sign">What it does.</param>
    /// <param name="before">Whether it is written before the note.</param>
    public void SetDynamicsWedge(string sign, bool before = true)
        => Dynamic.Add(new DynamicsWedge(sign, before));

    /// <summary>Attaches a dynamic written as words.</summary>
    /// <param name="sign">The words.</param>
    /// <param name="before">Whether it is written before the note.</param>
    public void SetDynamicsText(string sign, bool before = true)
        => Dynamic.Add(new DynamicsText(sign, before));

    /// <summary>Attaches a dashed line under a dynamic instruction.</summary>
    /// <param name="sign">What it does.</param>
    /// <param name="before">Whether it is written before the note.</param>
    public void SetDynamicsDashes(string sign, bool before = true)
        => Dynamic.Add(new DynamicsDashes(sign, before));

    /// <summary>Attaches an octave shift.</summary>
    /// <param name="placement">Where it is drawn.</param>
    /// <param name="octaveDirection">What it does.</param>
    /// <param name="size">How many notes it spans.</param>
    public void SetOctShift(string placement, string octaveDirection, int size)
        => OctShift = new OctaveShift(placement, octaveDirection, size);
}

/// <summary>An octave shift, and how it is drawn.</summary>
public sealed class OctaveShift
{
    /// <summary>Creates an octave shift.</summary>
    /// <param name="placement">Where it is drawn.</param>
    /// <param name="octaveDirection">What it does.</param>
    /// <param name="size">How many notes it spans.</param>
    public OctaveShift(string placement, string octaveDirection, int size)
    {
        Placement = placement;
        OctaveDirection = octaveDirection;
        Size = size;
    }

    /// <summary>Gets where it is drawn.</summary>
    public string Placement { get; }

    /// <summary>Gets what it does.</summary>
    public string OctaveDirection { get; }

    /// <summary>Gets how many notes it spans.</summary>
    public int Size { get; }
}

/// <summary>A dynamic attached to a note.</summary>
public abstract class Dynamics
{
    /// <summary>Creates a dynamic.</summary>
    /// <param name="sign">What it says.</param>
    /// <param name="before">Whether it is written before the note.</param>
    protected Dynamics(string sign, bool before = true)
    {
        Before = before;
        Sign = sign;
    }

    /// <summary>Gets whether it is written before the note.</summary>
    public bool Before { get; }

    /// <summary>Gets what it says.</summary>
    public string Sign { get; }
}

/// <summary>A dynamic mark, such as a forte.</summary>
public sealed class DynamicsMark : Dynamics
{
    /// <summary>Creates a dynamic mark.</summary>
    /// <param name="sign">Its element name.</param>
    /// <param name="before">Whether it is written before the note.</param>
    public DynamicsMark(string sign, bool before = true)
        : base(sign, before)
    {
    }
}

/// <summary>A hairpin.</summary>
public sealed class DynamicsWedge : Dynamics
{
    /// <summary>Creates a hairpin.</summary>
    /// <param name="sign">What it does.</param>
    /// <param name="before">Whether it is written before the note.</param>
    public DynamicsWedge(string sign, bool before = true)
        : base(sign, before)
    {
    }
}

/// <summary>A dynamic written as words.</summary>
public sealed class DynamicsText : Dynamics
{
    /// <summary>Creates a dynamic written as words.</summary>
    /// <param name="sign">The words.</param>
    /// <param name="before">Whether it is written before the note.</param>
    public DynamicsText(string sign, bool before = true)
        : base(sign, before)
    {
    }
}

/// <summary>A dashed line under a dynamic instruction.</summary>
public sealed class DynamicsDashes : Dynamics
{
    /// <summary>Creates a dashed line.</summary>
    /// <param name="sign">What it does.</param>
    /// <param name="before">Whether it is written before the note.</param>
    public DynamicsDashes(string sign, bool before = true)
        : base(sign, before)
    {
    }
}

/// <summary>One note's share of a tuplet.</summary>
public sealed class Tuplet
{
    /// <summary>Creates a tuplet.</summary>
    /// <param name="fraction">The ratio, actual over normal.</param>
    /// <param name="tupletType">What the bracket does.</param>
    /// <param name="number">Which tuplet.</param>
    /// <param name="actualType">The actual note type shown.</param>
    /// <param name="normalType">The normal note type shown.</param>
    public Tuplet(
        (int Actual, int Normal) fraction, string tupletType, int number,
        string actualType, string normalType)
    {
        Fraction = fraction;
        TupletType = tupletType;
        Number = number;
        ActualType = actualType;
        NormalType = normalType;
    }

    /// <summary>Gets the ratio, actual over normal.</summary>
    public (int Actual, int Normal) Fraction { get; }

    /// <summary>Gets what the bracket does.</summary>
    public string TupletType { get; }

    /// <summary>Gets which tuplet this is.</summary>
    public int Number { get; }

    /// <summary>Gets the actual note type shown.</summary>
    public string ActualType { get; }

    /// <summary>Gets the normal note type shown.</summary>
    public string NormalType { get; }
}

/// <summary>One end of a slur.</summary>
public sealed class Slur
{
    /// <summary>Creates a slur end.</summary>
    /// <param name="number">Which slur.</param>
    /// <param name="slurType">Whether it starts or stops.</param>
    /// <param name="phrasing">Whether it is a phrasing slur.</param>
    /// <param name="startNode">The slur it closes, when it stops.</param>
    public Slur(int number, string slurType, bool phrasing = false, Slur startNode = null)
    {
        Number = number;
        SlurType = slurType;
        Phrasing = phrasing;
        StartNode = startNode;
    }

    /// <summary>Gets or sets which slur this is.</summary>
    public int Number { get; set; }

    /// <summary>Gets whether it starts or stops.</summary>
    public string SlurType { get; }

    /// <summary>Gets whether it is a phrasing slur.</summary>
    public bool Phrasing { get; }

    /// <summary>Gets the slur this one closes, or null.</summary>
    public Slur StartNode { get; }
}

/// <summary>One note.</summary>
public class BarNote : BarMus
{
    /// <summary>Creates a note.</summary>
    /// <param name="pitchNote">Its note name.</param>
    /// <param name="alter">Its accidental, in semitones.</param>
    /// <param name="accidental">The accidental token, or null.</param>
    /// <param name="duration">How long it is.</param>
    /// <param name="voice">Which voice it belongs to.</param>
    public BarNote(
        string pitchNote, double alter, string accidental, BaseScaling duration, int voice = 1)
        : base(duration, voice)
    {
        BaseNote = pitchNote?.ToUpperInvariant();
        Alter = alter;
        AccidentalToken = accidental;
    }

    /// <summary>Gets or sets the note's name, in capitals.</summary>
    public string BaseNote { get; set; }

    /// <summary>Gets or sets the note's accidental, in semitones.</summary>
    public double Alter { get; set; }

    /// <summary>Gets or sets the note's octave.</summary>
    public int Octave { get; set; }

    /// <summary>Gets or sets the accidental token, or null.</summary>
    public string AccidentalToken { get; set; }

    /// <summary>Gets the ties on the note.</summary>
    public List<string> Tie { get; } = new List<string>();

    /// <summary>Gets or sets whether the note is a grace, and whether it is slashed.</summary>
    public (bool IsGrace, bool Slash) Grace { get; set; }

    /// <summary>Gets or sets the glissando on the note, or null.</summary>
    public (string Line, string EndType, int Number)? Gliss { get; set; }

    /// <summary>Gets or sets the tremolo on the note.</summary>
    public (string Type, int Lines) Tremolo { get; set; } = (string.Empty, 0);

    /// <summary>Gets the slurs starting or stopping on the note.</summary>
    public List<Slur> Slur { get; } = new List<Slur>();

    /// <summary>Gets the note's articulations.</summary>
    public List<string> Artic { get; } = new List<string>();

    /// <summary>Gets or sets a simple ornament, or null.</summary>
    public string Ornament { get; set; }

    /// <summary>Gets or sets an ornament with arguments, or null.</summary>
    public (string Name, string Type)? AdvOrnament { get; set; }

    /// <summary>Gets or sets the fingering, or null.</summary>
    public int? Fingering { get; set; }

    /// <summary>Gets or sets the syllables under the note, or null.</summary>
    public List<LyricEntry> Lyric { get; set; }

    /// <summary>Gets or sets the stem direction, or null.</summary>
    public string StemDirection { get; set; }

    /// <summary>Sets the note's duration.</summary>
    /// <param name="duration">How long it is.</param>
    /// <param name="durationType">Its written duration, or empty to keep it.</param>
    /// <remarks>⚠ Setting a duration CLEARS the dots, which is upstream's own
    /// behaviour and the difference between this and <see cref="BarRest"/>'s.</remarks>
    public virtual void SetDuration(BaseScaling duration, string durationType = "")
    {
        Duration = duration;
        Dot = 0;
        if (!string.IsNullOrEmpty(durationType)) { Type = durationType; }
    }

    /// <summary>Sets the note's written duration.</summary>
    /// <param name="durationType">The type.</param>
    public virtual void SetDurType(string durationType) => Type = durationType;

    /// <summary>Sets the note's octave.</summary>
    /// <param name="octave">The octave.</param>
    public void SetOctave(int octave) => Octave = octave;

    /// <summary>Adds a tie.</summary>
    /// <param name="tieType">Whether it starts or stops.</param>
    public void SetTie(string tieType) => Tie.Add(tieType);

    /// <summary>Adds one end of a slur.</summary>
    /// <param name="number">Which slur.</param>
    /// <param name="slurType">Whether it starts or stops.</param>
    /// <param name="phrasing">Whether it is a phrasing slur.</param>
    /// <param name="slurStartNode">The slur it closes, when it stops.</param>
    public void SetSlur(
        int number, string slurType, bool phrasing = false, Slur slurStartNode = null)
        => Slur.Add(new Slur(number, slurType, phrasing, slurStartNode));

    /// <summary>Adds an articulation.</summary>
    /// <param name="articulationName">Its element name.</param>
    public void AddArticulation(string articulationName) => Artic.Add(articulationName);

    /// <summary>Sets a simple ornament.</summary>
    /// <param name="ornament">Which one.</param>
    public void AddOrnament(string ornament) => Ornament = ornament;

    /// <summary>Sets an ornament with arguments.</summary>
    /// <param name="ornament">Which one.</param>
    /// <param name="endType">What it does.</param>
    public void AddAdvOrnament(string ornament, string endType = "start")
        => AdvOrnament = (ornament, endType);

    /// <summary>Makes the note a grace note.</summary>
    /// <param name="slash">Whether the grace is slashed.</param>
    public void SetGrace(bool slash) => Grace = (true, slash);

    /// <summary>Adds a glissando.</summary>
    /// <param name="line">How the line is drawn; solid when not said.</param>
    /// <param name="endType">What it does.</param>
    /// <param name="number">Which glissando.</param>
    public void SetGliss(string line, string endType = "start", int number = 1)
        => Gliss = (string.IsNullOrEmpty(line) ? "solid" : line, endType, number);

    /// <summary>Adds a tremolo.</summary>
    /// <param name="tremoloType">What it does.</param>
    /// <param name="duration">
    /// The tremolo's own note value, or zero to keep the lines already counted.
    /// </param>
    public void SetTremolo(string tremoloType, int duration = 0)
        => Tremolo = duration != 0
            ? (tremoloType, DurationToLines(duration))
            : (tremoloType, Tremolo.Lines);

    /// <summary>Sets the stem direction.</summary>
    /// <param name="direction">Which way.</param>
    public void SetStemDirection(string direction) => StemDirection = direction;

    /// <summary>Adds a fingering.</summary>
    /// <param name="fingerNumber">Which finger.</param>
    public void AddFingering(int fingerNumber) => Fingering = fingerNumber;

    /// <summary>Adds a syllable under the note.</summary>
    /// <param name="entry">The syllable.</param>
    public void AddLyric(LyricEntry entry)
    {
        Lyric ??= new List<LyricEntry>();
        Lyric.Add(entry);
    }

    /// <summary>Changes where in its word a syllable sits.</summary>
    /// <param name="index">Which syllable.</param>
    /// <param name="syllabic">The new value.</param>
    public void ChangeLyricSyll(int index, string syllabic) => Lyric[index].Syllabic = syllabic;

    /// <summary>Changes which verse a syllable belongs to.</summary>
    /// <param name="index">Which syllable.</param>
    /// <param name="number">The new verse.</param>
    public void ChangeLyricNr(int index, int number) => Lyric[index].Number = number;

    /// <summary>How many beams a tremolo of a note value is drawn with.</summary>
    /// <param name="duration">The note value.</param>
    /// <returns>The number of beams.</returns>
    public static int DurationToLines(int duration) => duration switch
    {
        8 => 1,
        16 => 2,
        32 => 3,
        _ => 0,
    };
}

/// <summary>A note with no pitch — a drum, say.</summary>
public sealed class Unpitched : BarNote
{
    /// <summary>Creates an unpitched note.</summary>
    /// <param name="duration">How long it is.</param>
    /// <param name="step">Where on the staff it is drawn, or null for B.</param>
    /// <param name="voice">Which voice it belongs to.</param>
    /// <remarks>
    /// ⚠ Upstream passes <c>voice=1</c> to its base constructor LITERALLY,
    /// ignoring the <c>voice</c> parameter it was given, so an unpitched note is
    /// always in voice 1. It is kept: the only call site
    /// (<c>ly2xml_mediator.new_unpitched_note</c>) does not pass a voice either,
    /// so no document can tell the difference, and "the parameter is unused" is
    /// not the same thing as "the code does not do what it says" (ruling FR14's
    /// bar). Raised in the wave's STATUS file.
    /// </remarks>
    public Unpitched(BaseScaling duration, string step = null, int voice = 1)
        : base("B", 0, string.Empty, duration, 1)
    {
        Octave = 4;
        if (!string.IsNullOrEmpty(step)) { BaseNote = step.ToUpperInvariant(); }
    }
}

/// <summary>A rest, or a skip.</summary>
public sealed class BarRest : BarMus
{
    private readonly bool _skip;

    /// <summary>Creates a rest.</summary>
    /// <param name="duration">How long it is.</param>
    /// <param name="voice">Which voice it belongs to.</param>
    /// <param name="showType">Whether its written duration is shown.</param>
    /// <param name="skip">Whether it is a skip rather than a rest.</param>
    /// <param name="position">Where on the staff it sits, or null.</param>
    public BarRest(
        BaseScaling duration, int voice = 1, bool showType = true, bool skip = false,
        (string Step, int Octave)? position = null)
        : base(duration, voice)
    {
        ShowType = showType;
        _skip = skip;
        Pos = position;
    }

    /// <summary>Gets whether the rest's written duration is shown.</summary>
    public bool ShowType { get; }

    /// <inheritdoc/>
    public override bool Skip => _skip;

    /// <summary>Gets or sets where on the staff the rest sits, or null.</summary>
    public (string Step, int Octave)? Pos { get; set; }

    /// <summary>Sets the rest's duration.</summary>
    /// <param name="duration">How long it is.</param>
    /// <param name="durationType">Its written duration, or empty to leave it.</param>
    /// <remarks>Unlike a note's, this does NOT clear the dots — upstream's own
    /// difference between the two.</remarks>
    public void SetDuration(BaseScaling duration, string durationType = "")
    {
        Duration = duration;
        if (string.IsNullOrEmpty(durationType)) { return; }

        Type = ShowType ? durationType : null;
    }

    /// <summary>Sets the rest's written duration, if it is shown at all.</summary>
    /// <param name="durationType">The type.</param>
    public void SetDurType(string durationType)
    {
        if (ShowType) { Type = durationType; }
    }
}

/// <summary>Everything a bar can state about itself.</summary>
public sealed class BarAttr : BarObject
{
    /// <summary>Gets or sets the key, in fifths, or null for none.</summary>
    public int? Key { get; set; }

    /// <summary>Gets or sets the time signature, or null for none.</summary>
    public TimeSignature Time { get; set; }

    /// <summary>Gets or sets the clef, or null for none.</summary>
    public ClefSignature Clef { get; set; }

    /// <summary>Gets or sets the mode's name.</summary>
    public string Mode { get; set; } = string.Empty;

    /// <summary>Gets or sets the divisions per quarter note, or zero for none.</summary>
    public int Divisions { get; set; }

    /// <summary>Gets or sets the bar line style, or null.</summary>
    public string Barline { get; set; }

    /// <summary>Gets or sets which way a repeat faces, or null.</summary>
    public string Repeat { get; set; }

    /// <summary>Gets or sets how many staves the part has, or zero for none.</summary>
    public int StavesCount { get; set; }

    /// <summary>Gets the clefs for each staff of a multi-staff part.</summary>
    public List<(ClefSignature Clef, string Number)> MultiClef { get; }
        = new List<(ClefSignature, string)>();

    /// <summary>Gets or sets the tempo direction, or null.</summary>
    public TempoDir Tempo { get; set; }

    /// <summary>Gets or sets how many bars a multiple rest lasts, or null for none.</summary>
    public int? MultiRest { get; set; }

    /// <summary>Gets or sets the rehearsal mark, or null.</summary>
    public string Mark { get; set; }

    /// <summary>Gets or sets the words written over the bar, or null.</summary>
    public string Word { get; set; }

    /// <summary>Gets or sets whether a new system starts here, or null.</summary>
    public string NewSystem { get; set; }

    /// <inheritdoc/>
    public override string ToString() => "<BarAttr " + (Time?.Beats ?? "0") + ">";

    /// <summary>Asks for a system break at this bar.</summary>
    /// <param name="forceBreak">Whether to break.</param>
    public void AddBreak(string forceBreak) => NewSystem = forceBreak;

    /// <summary>Sets the key.</summary>
    /// <param name="musicKey">How many sharps, or flats when negative.</param>
    /// <param name="mode">The mode's name.</param>
    public void SetKey(int musicKey, string mode)
    {
        Key = musicKey;
        Mode = mode;
    }

    /// <summary>Sets the time signature.</summary>
    /// <param name="signature">The beats and the beat type.</param>
    /// <param name="numeric">Whether it is written as numbers.</param>
    /// <remarks>
    /// ⚠ A non-numeric 2/2 comes out as <c>common</c>, not <c>cut</c>, because
    /// upstream tests for either 2/2 or 4/4 and appends the one word. It is a
    /// design decision rather than a defect — the symbol is the caller's to name
    /// — and it is ported as it stands.
    /// </remarks>
    public void SetTime(TimeSignature signature, bool numeric = true)
    {
        Time = signature;
        if (numeric || signature == null) { return; }

        bool common = (signature.Beats == "2" && signature.BeatType == "2")
            || (signature.Beats == "4" && signature.BeatType == "4");
        if (common) { Time = new TimeSignature(signature.Beats, signature.BeatType, "common"); }
    }

    /// <summary>Sets the clef.</summary>
    /// <param name="clef">The clef.</param>
    public void SetClef(ClefSignature clef) => Clef = clef;

    /// <summary>Sets the bar line, translating LilyPond's spelling of it.</summary>
    /// <param name="barline">LilyPond's bar line string.</param>
    public void SetBarline(string barline) => Barline = ConvertBarline(barline);

    /// <summary>Sets the tempo direction.</summary>
    /// <param name="unit">The beat unit's note value.</param>
    /// <param name="unitType">The beat unit's name, or empty for no metronome mark.</param>
    /// <param name="beats">Beats per minute.</param>
    /// <param name="dots">How many dots the beat unit carries.</param>
    /// <param name="text">The words shown.</param>
    public void SetTempo(
        string unit = null, string unitType = "", string beats = null, int dots = 0, string text = "")
        => Tempo = new TempoDir(unit, unitType, beats, dots, text);

    /// <summary>Sets a multiple rest.</summary>
    /// <param name="size">How many bars it lasts.</param>
    public void SetMultipleRest(int size = 0) => MultiRest = size;

    /// <summary>Sets the rehearsal mark.</summary>
    /// <param name="mark">The mark.</param>
    public void SetMark(string mark) => Mark = mark;

    /// <summary>Adds words over the bar, each followed by a space.</summary>
    /// <param name="words">The words.</param>
    public void SetWord(string words)
    {
        Word ??= string.Empty;
        Word += words + " ";
    }

    /// <inheritdoc/>
    public override bool HasAttr()
        => Key != null || Time != null || Clef != null || MultiClef.Count > 0
            || Divisions != 0 || MultiRest != null || !string.IsNullOrEmpty(Mark);

    /// <summary>Takes in another bar's attributes.</summary>
    /// <param name="barattr">The other bar's.</param>
    /// <param name="override">Whether they replace what is already set.</param>
    public void MergeAttr(BarAttr barattr, bool @override = false)
    {
        if (barattr == null) { return; }

        if (barattr.Key != null && (@override || Key == null))
        {
            Key = barattr.Key;
            Mode = barattr.Mode;
        }

        if (barattr.Time != null && (@override || Time == null)) { Time = barattr.Time; }

        if (barattr.Clef != null && (@override || Clef == null)) { Clef = barattr.Clef; }

        if (barattr.MultiClef.Count > 0) { MultiClef.AddRange(barattr.MultiClef); }

        if (barattr.Tempo != null && (@override || Tempo == null)) { Tempo = barattr.Tempo; }
    }

    /// <summary>Turns LilyPond's spelling of a bar line into MusicXML's.</summary>
    /// <param name="barline">LilyPond's string.</param>
    /// <returns>MusicXML's name, or null when there is no translation.</returns>
    public static string ConvertBarline(string barline) => barline switch
    {
        "|" => "regular",
        ":" => "dotted",
        "dashed" => "dashed",
        "." => "heavy",
        "||" => "light-light",
        ".|" or "forward" => "heavy-light",
        ".|." => "heavy-heavy",
        "|." or "backward" => "light-heavy",
        "'" => "tick",
        _ => null,
    };
}

/// <summary>How far the writing position goes back for another voice.</summary>
public sealed class BarBackup : BarObject
{
    /// <summary>Creates a backup.</summary>
    /// <param name="duration">How far back.</param>
    public BarBackup(BaseScaling duration) => Duration = duration;

    /// <summary>Gets how far back.</summary>
    public BaseScaling Duration { get; }
}

/// <summary>A tempo direction: what it says, and what it sounds like.</summary>
public sealed class TempoDir
{
    /// <summary>Creates a tempo direction.</summary>
    /// <param name="unit">The beat unit's note value.</param>
    /// <param name="unitType">The beat unit's name, or empty for no metronome mark.</param>
    /// <param name="beats">Beats per minute.</param>
    /// <param name="dots">How many dots the beat unit carries.</param>
    /// <param name="text">The words shown.</param>
    public TempoDir(string unit, string unitType, string beats, int dots, string text)
    {
        if (!string.IsNullOrEmpty(unitType))
        {
            Metronome = (unitType, beats);
            Midi = ComputeMidiTempo(unit, beats, dots);
        }
        else
        {
            Metronome = null;
            Midi = 0.0;
        }

        Dots = dots;
        Text = text;
    }

    /// <summary>Gets the beat unit and the beats per minute, or null for no mark.</summary>
    public (string Unit, string Beats)? Metronome { get; }

    /// <summary>Gets the tempo in quarter notes per minute, for playback.</summary>
    public double Midi { get; }

    /// <summary>Gets how many dots the beat unit carries.</summary>
    public int Dots { get; }

    /// <summary>Gets the words shown.</summary>
    public string Text { get; }

    /// <summary>Works out the playback tempo from the written one.</summary>
    /// <param name="unit">The beat unit's note value.</param>
    /// <param name="beats">Beats per minute.</param>
    /// <param name="dots">How many dots the beat unit carries.</param>
    /// <returns>The tempo in quarter notes per minute.</returns>
    public static double ComputeMidiTempo(string unit, string beats, int dots)
    {
        int unitValue = int.Parse(unit, CultureInfo.InvariantCulture);
        Fraction u = new Fraction(1, unitValue);
        if (dots > 0)
        {
            long den = (long)Math.Pow(2, dots);
            long num = (long)Math.Pow(2, dots + 1) - 1;
            u *= new Fraction(num, den);
        }

        Fraction mult = new Fraction(4) * u;
        Fraction result = ParseFraction(beats) * mult;
        return (double)result.Numerator / result.Denominator;
    }

    /// <summary>Reads a number the way python's <c>Fraction()</c> reads one.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The number.</returns>
    /// <remarks>
    /// The beats per minute arrive as the token the document wrote, so they may
    /// be a whole number, a fraction or a decimal — all three of which python's
    /// Fraction constructor accepts from a string.
    /// </remarks>
    internal static Fraction ParseFraction(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) { return new Fraction(0); }

        string value = text.Trim();
        int slash = value.IndexOf('/');
        if (slash >= 0)
        {
            return new Fraction(
                long.Parse(value.Substring(0, slash), CultureInfo.InvariantCulture),
                long.Parse(value.Substring(slash + 1), CultureInfo.InvariantCulture));
        }

        int point = value.IndexOf('.');
        if (point < 0) { return new Fraction(long.Parse(value, CultureInfo.InvariantCulture)); }

        string whole = value.Substring(0, point);
        string decimals = value.Substring(point + 1);
        long denominator = (long)Math.Pow(10, decimals.Length);
        long numerator = long.Parse(
            (whole.Length == 0 ? "0" : whole) + decimals, CultureInfo.InvariantCulture);
        return new Fraction(numerator, denominator);
    }
}
