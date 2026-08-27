// === python-ly ly.music.items module (the item types) ===
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

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using LilyPondMode = Fresco.Brix.Ly.Lex.LilyPondMode;
using LyToken = Fresco.Brix.Ly.Slexing.Token;
using SchemeMode = Fresco.Brix.Ly.Lex.SchemeMode;

namespace Fresco.Brix.Ly.Music; //was previously: ly/music/items.py (the item types);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Any token that is not otherwise recognized.</summary>
public class TokenItem : Item //was previously: items.Token
{
}

/// <summary>An item having a list of child items.</summary>
public class Container : Item
{
}

/// <summary>A written duration.</summary>
public class DurationItem : Item //was previously: items.Duration
{
}

/// <summary>An item that has a musical duration.</summary>
public class Durable : Item
{
    /// <summary>Gets or sets the duration, as its base and its scaling.</summary>
    public (Fraction Base, Fraction Scaling) Duration { get; set; }
        = (Fraction.Zero, Fraction.One);

    /// <inheritdoc/>
    public override Fraction Length() => Duration.Base * Duration.Scaling;

    /// <inheritdoc/>
    public override Fraction Events(Events events, Fraction time, Fraction scaling)
        => time + (Duration.Base * Duration.Scaling * scaling);
}

/// <summary>A chord: a durable that contains its notes.</summary>
public class Chord : Durable
{
}

/// <summary>A "note" without pitch, just a standalone duration.</summary>
public class Unpitched : Durable
{
}

/// <summary>A note that has a pitch.</summary>
public class Note : Durable
{
    /// <summary>Gets or sets the pitch.</summary>
    public Pitching.Pitch Pitch { get; set; }

    /// <summary>Gets or sets the octave token.</summary>
    public LyToken OctaveToken { get; set; }

    /// <summary>Gets or sets the accidental token.</summary>
    public LyToken AccidentalToken { get; set; }

    /// <summary>Gets or sets the octave-check token.</summary>
    public LyToken OctavecheckToken { get; set; }
}

/// <summary>A skip.</summary>
public class Skip : Durable
{
}

/// <summary>A rest.</summary>
public class Rest : Durable
{
}

/// <summary>A chord repetition (<c>q</c>).</summary>
public class Q : Durable
{
}

/// <summary>A note in drum mode.</summary>
public class DrumNote : Durable
{
}

/// <summary>Any music expression.</summary>
public class Music : Container
{
    /// <inheritdoc/>
    public override Fraction Events(Events events, Fraction time, Fraction scaling)
    {
        foreach (Node node in this)
        {
            time = events.Traverse((Item)node, time, scaling);
        }

        return time;
    }

    /// <inheritdoc/>
    public override Fraction Length() => new Events().Read(this);

    /// <summary>
    /// Answers the nodes that come before a child in time, and the scaling
    /// this node applies. When the child is <see langword="null"/>, every node
    /// that would precede a fictive node at the end is answered.
    /// </summary>
    /// <param name="node">The child, or <see langword="null"/>.</param>
    /// <returns>The preceding nodes and the scaling.</returns>
    public virtual (IReadOnlyList<Item> Nodes, Fraction Scaling) Preceding(Item node = null)
        => (ChildrenBefore(node), Fraction.One);

    /// <summary>Answers the children before a child, or all of them.</summary>
    /// <param name="node">The child, or <see langword="null"/>.</param>
    /// <returns>The children.</returns>
    protected IReadOnlyList<Item> ChildrenBefore(Item node)
    {
        int end = node == null ? Count : Index(node);
        if (end < 0) { end = Count; }

        var result = new List<Item>(end);
        for (int i = 0; i < end; i++) { result.Add((Item)this[i]); }

        return result;
    }
}

/// <summary>A music expression, either <c>&lt;&lt; &gt;&gt;</c> or <c>{ }</c>.</summary>
public class MusicList : Music
{
    /// <summary>Gets or sets whether the expression is simultaneous.</summary>
    public bool Simultaneous { get; set; }

    /// <inheritdoc/>
    public override Fraction Events(Events events, Fraction time, Fraction scaling)
    {
        if (!Simultaneous) { return base.Events(events, time, scaling); }

        if (Count > 0)
        {
            Fraction longest = time;
            foreach (Node node in this)
            {
                Fraction end = events.Traverse((Item)node, time, scaling);
                if (end > longest) { longest = end; }
            }

            time = longest;
        }

        return time;
    }

    /// <inheritdoc/>
    public override (IReadOnlyList<Item> Nodes, Fraction Scaling) Preceding(Item node = null)
        => Simultaneous
            ? ((IReadOnlyList<Item>)new List<Item>(), Fraction.One)
            : base.Preceding(node);
}

/// <summary>A <c>\tag</c>, <c>\keepWithTag</c> or <c>\removeWithTag</c> command.</summary>
public class Tag : Music
{
    /// <inheritdoc/>
    public override Fraction Events(Events events, Fraction time, Fraction scaling)
    {
        if (Count > 0)
        {
            time = events.Traverse((Item)this[Count - 1], time, scaling);
        }

        return time;
    }

    /// <inheritdoc/>
    public override (IReadOnlyList<Item> Nodes, Fraction Scaling) Preceding(Item node = null)
        => (new List<Item>(), Fraction.One);
}

/// <summary>
/// A music construct that scales the duration of its contents. The numerator
/// and denominator hold the values as LilyPond spells them — note that
/// <c>\tuplet</c> and <c>\times</c> reverse their meaning — while
/// <see cref="Scaling"/> holds the algebraic scaling.
/// </summary>
public class Scaler : Music
{
    /// <summary>Gets or sets the algebraic scaling.</summary>
    public Fraction Scaling { get; set; } = Fraction.One;

    /// <summary>Gets or sets the numerator as written.</summary>
    public int Numerator { get; set; }

    /// <summary>Gets or sets the denominator as written.</summary>
    public int Denominator { get; set; }

    /// <inheritdoc/>
    public override Fraction Events(Events events, Fraction time, Fraction scaling)
        => base.Events(events, time, scaling * Scaling);

    /// <inheritdoc/>
    public override (IReadOnlyList<Item> Nodes, Fraction Scaling) Preceding(Item node = null)
        => (ChildrenBefore(node), Scaling);
}

/// <summary>Music with grace timing, i.e. zero as far as computation goes.</summary>
public class Grace : Music
{
    /// <inheritdoc/>
    public override Fraction Events(Events events, Fraction time, Fraction scaling)
        => base.Events(events, time, Fraction.Zero);

    /// <inheritdoc/>
    public override (IReadOnlyList<Item> Nodes, Fraction Scaling) Preceding(Item node = null)
        => (ChildrenBefore(node), Fraction.Zero);
}

/// <summary>The <c>\afterGrace</c> function with its two arguments; only the
/// duration of the first counts.</summary>
public class AfterGrace : Music
{
}

/// <summary>The <c>\partcombine</c> command with its two music arguments.</summary>
public class PartCombine : Music
{
    /// <inheritdoc/>
    public override Fraction Events(Events events, Fraction time, Fraction scaling)
    {
        if (Count > 0)
        {
            Fraction longest = time;
            foreach (Node node in this)
            {
                Fraction end = events.Traverse((Item)node, time, scaling);
                if (end > longest) { longest = end; }
            }

            time = longest;
        }

        return time;
    }

    /// <inheritdoc/>
    public override (IReadOnlyList<Item> Nodes, Fraction Scaling) Preceding(Item node = null)
        => (new List<Item>(), Fraction.One);
}

/// <summary>A <c>\relative</c> music expression.</summary>
public class Relative : Music
{
}

/// <summary>An <c>\absolute</c> music expression.</summary>
public class Absolute : Music
{
}

/// <summary>A <c>\transpose</c> music expression.</summary>
public class Transpose : Music
{
}

/// <summary>A <c>\repeat</c> expression.</summary>
public class Repeat : Music
{
    /// <summary>Gets or sets the specifier, as an item or a token.</summary>
    public object SpecifierValue { get; set; } //was previously: _specifier

    /// <summary>Gets or sets the repeat count, as an item or a token.</summary>
    public object RepeatCountValue { get; set; } //was previously: _repeat_count

    /// <summary>Answers the repeat kind, e.g. <c>volta</c>.</summary>
    /// <returns>The kind.</returns>
    public string Specifier()
        => SpecifierValue switch
        {
            Scheme scheme => scheme.GetString(),
            StringItem text => text.Value(),
            LyToken token => token.Text,
            string text => text,
            _ => null,
        };

    /// <summary>Answers how often the expression repeats.</summary>
    /// <returns>The count, never below one.</returns>
    public int RepeatCount()
    {
        if (RepeatCountValue is Scheme scheme)
        {
            int? value = scheme.GetInt();
            return value == null || value.Value == 0 ? 1 : value.Value;
        }

        string text = RepeatCountValue switch
        {
            LyToken token => token.Text,
            string s => s,
            _ => null,
        };

        if (string.IsNullOrEmpty(text)) { text = "1"; }

        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
            && count != 0
            ? count
            : 1;
    }

    /// <inheritdoc/>
    public override Fraction Events(Events events, Fraction time, Fraction scaling)
    {
        Alternative alternative = Count > 0 ? this[Count - 1] as Alternative : null;
        var children = new List<Item>();
        for (int i = 0; i < (alternative != null ? Count - 1 : Count); i++)
        {
            children.Add((Item)this[i]);
        }

        if (events.UnfoldRepeats || Specifier() != "volta")
        {
            int count = RepeatCount();
            if (alternative != null && alternative.Count > 0 && alternative[0].Count > 0)
            {
                Node group = alternative[0];
                var alternatives = new List<Item>();
                for (int i = 0; i < group.Count && i < count + 1; i++)
                {
                    alternatives.Add((Item)group[i]);
                }

                //Upstream pads the front with copies of the first alternative
                //when there are fewer of them than repeats.
                for (int i = 0, missing = count - alternatives.Count; i < missing; i++)
                {
                    alternatives.Insert(0, alternatives[0]);
                }

                foreach (Item a in alternatives)
                {
                    foreach (Item n in children) { time = events.Traverse(n, time, scaling); }

                    time = events.Traverse(a, time, scaling);
                }
            }
            else
            {
                for (int i = 0; i < count; i++)
                {
                    foreach (Item n in children) { time = events.Traverse(n, time, scaling); }
                }
            }
        }
        else
        {
            foreach (Item n in children) { time = events.Traverse(n, time, scaling); }

            if (alternative != null) { time = events.Traverse(alternative, time, scaling); }
        }

        return time;
    }
}

/// <summary>An <c>\alternative</c> expression.</summary>
public class Alternative : Music
{
}

/// <summary>Base class for the input-mode-changing commands.</summary>
public class InputMode : Music
{
}

/// <summary>A <c>\notemode</c> or <c>\notes</c> expression.</summary>
public class NoteMode : InputMode
{
}

/// <summary>A <c>\chordmode</c> or <c>\chords</c> expression.</summary>
public class ChordMode : InputMode
{
}

/// <summary>A <c>\drummode</c> or <c>\drums</c> expression.</summary>
public class DrumMode : InputMode
{
}

/// <summary>A <c>\figuremode</c> or <c>\figures</c> expression.</summary>
public class FigureMode : InputMode
{
}

/// <summary>A <c>\lyricmode</c>, <c>\lyrics</c> or <c>\addlyrics</c> expression.</summary>
public class LyricMode : InputMode
{
}

/// <summary>A <c>\lyricsto</c> expression.</summary>
public class LyricsTo : InputMode
{
    /// <summary>Gets or sets the context id, as an item or a token.</summary>
    public object ContextIdValue { get; set; } //was previously: _context_id

    /// <summary>Answers the context id.</summary>
    /// <returns>The id.</returns>
    public string ContextId()
        => ContextIdValue switch
        {
            StringItem text => text.Value(),
            Scheme scheme => scheme.GetString(),
            LyToken token => token.Text,
            string text => text,
            _ => null,
        };
}

/// <summary>A lyric text (word, markup or string), with a duration.</summary>
public class LyricText : Durable
{
}

/// <summary>Another lyric item (skip, extender, hyphen or tie).</summary>
public class LyricItem : Item
{
}

/// <summary>Chord specifications after a note in chord mode.</summary>
public class ChordSpecifier : Item
{
}

/// <summary>An item inside a <see cref="ChordSpecifier"/>.</summary>
public class ChordItem : Item
{
}

/// <summary>A tremolo item <c>:</c>.</summary>
public class Tremolo : Item
{
    /// <summary>Gets or sets the duration, as its base and its scaling.</summary>
    public (Fraction Base, Fraction Scaling) Duration { get; set; }
        = (Fraction.Zero, Fraction.One);
}

/// <summary>
/// What a <c>\change</c>, <c>\new</c> or <c>\context</c> music expression
/// answers. Upstream's <c>Context</c> inherits from BOTH <c>Translator</c> and
/// <c>Music</c>; C# has no multiple inheritance, so the translator half is
/// this interface, which <see cref="Translator"/> and <see cref="ContextItem"/>
/// implement, and <see cref="ContextItem"/> keeps the music half by
/// inheritance — the half every music walk tests for.
/// </summary>
public interface ITranslator
{
    /// <summary>Answers the context, when specified.</summary>
    /// <returns>The context token.</returns>
    LyToken Context();

    /// <summary>Answers the context id, when one was given after an equal sign.</summary>
    /// <returns>The id.</returns>
    string ContextId();
}

/// <summary>Base class for a <c>\change</c> music expression.</summary>
public class Translator : Item, ITranslator
{
    /// <summary>Gets or sets the context, as a token.</summary>
    public LyToken ContextValue { get; set; } //was previously: _context

    /// <summary>Gets or sets the context id, as an item or a token.</summary>
    public object ContextIdValue { get; set; } //was previously: _context_id

    /// <inheritdoc/>
    public LyToken Context() => ContextValue;

    /// <inheritdoc/>
    public string ContextId() => TranslatorHelpers.ContextId(ContextIdValue);
}

/// <summary>A <c>\new</c> or <c>\context</c> music expression.</summary>
public class ContextItem : Music, ITranslator //was previously: items.Context
{
    /// <summary>Gets or sets the context, as a token.</summary>
    public LyToken ContextValue { get; set; } //was previously: _context

    /// <summary>Gets or sets the context id, as an item or a token.</summary>
    public object ContextIdValue { get; set; } //was previously: _context_id

    /// <inheritdoc/>
    public LyToken Context() => ContextValue;

    /// <inheritdoc/>
    public string ContextId() => TranslatorHelpers.ContextId(ContextIdValue);
}

/// <summary>A <c>\change</c> music expression.</summary>
public class Change : Translator
{
}

/// <summary>The context-id reading both translator kinds share.</summary>
internal static class TranslatorHelpers
{
    /// <summary>Reads a context id from an item or a token.</summary>
    /// <param name="value">The stored value.</param>
    /// <returns>The id.</returns>
    internal static string ContextId(object value)
        => value switch
        {
            StringItem text => text.Value(),
            LyToken token => token.Text,
            string text => text,
            _ => null,
        };
}

/// <summary>A <c>\tempo</c> command.</summary>
public class Tempo : Item
{
    /// <summary>Gets or sets the note value, as its base and its scaling.</summary>
    public (Fraction Base, Fraction Scaling) Duration { get; set; }
        = (Fraction.Zero, Fraction.One);

    /// <summary>Answers the note value given before the equal sign.</summary>
    /// <returns>The value.</returns>
    public Fraction FractionValue() => Duration.Base * Duration.Scaling;

    /// <summary>Answers the tempo text, when set — markup, scheme or a string.</summary>
    /// <returns>The item, or <see langword="null"/>.</returns>
    public Item Text()
    {
        foreach (Node node in this)
        {
            return node is Markup || node is Scheme || node is StringItem ? (Item)node : null;
        }

        return null;
    }

    /// <summary>Answers the integer values describing the tempo or its range.</summary>
    /// <returns>The values.</returns>
    public IReadOnlyList<int> TempoValues()
    {
        var result = new List<int>();
        bool afterDuration = false;
        foreach (Node node in this)
        {
            if (!afterDuration)
            {
                if (node is DurationItem) { afterDuration = true; }

                continue;
            }

            if (node is Scheme scheme)
            {
                int? value = scheme.GetInt();
                if (value != null) { result.Add(value.Value); }
            }
            else if (node is Number number)
            {
                object value = number.Value();
                if (value is int integer) { result.Add(integer); }
            }
        }

        return result;
    }
}

/// <summary>A <c>\time</c> command.</summary>
public class TimeSignature : Item
{
    /// <summary>Gets or sets the upper number.</summary>
    public int NumeratorValue { get; set; } = 4; //was previously: _num

    /// <summary>Gets or sets the lower number, as a fraction.</summary>
    public Fraction FractionValue { get; set; } = new Fraction(1, 4); //was previously: _fraction

    /// <summary>Gets or sets the beat-structure scheme expression, if given.</summary>
    public Item BeatStructureValue { get; set; } //was previously: _beatstructure

    /// <summary>Answers the length of one measure in this time signature.</summary>
    /// <returns>The length.</returns>
    public Fraction MeasureLength() => NumeratorValue * FractionValue;

    /// <summary>Answers the upper number (3 for 3/2).</summary>
    /// <returns>The number.</returns>
    public int Numerator() => NumeratorValue;

    /// <summary>Answers the lower number as a fraction (1/2 for 3/2).</summary>
    /// <returns>The fraction.</returns>
    public Fraction Fraction() => FractionValue;

    /// <summary>Answers the beat structure, when specified.</summary>
    /// <returns>The scheme expression.</returns>
    public Item BeatStructure() => BeatStructureValue;
}

/// <summary>A <c>\partial</c> command.</summary>
public class Partial : Item
{
    /// <summary>Gets or sets the duration, as its base and its scaling.</summary>
    public (Fraction Base, Fraction Scaling) Duration { get; set; }
        = (Fraction.Zero, Fraction.One);

    /// <summary>Answers the duration given as the argument.</summary>
    /// <returns>The length.</returns>
    public Fraction PartialLength() => Duration.Base * Duration.Scaling;
}

/// <summary>A <c>\clef</c> item.</summary>
public class Clef : Item
{
    /// <summary>Gets or sets the specifier, as an item or a token.</summary>
    public object SpecifierValue { get; set; } //was previously: _specifier

    /// <summary>Answers the clef name.</summary>
    /// <returns>The name.</returns>
    public string Specifier()
        => SpecifierValue switch
        {
            StringItem text => text.Value(),
            LyToken token => token.Text,
            string text => text,
            _ => null,
        };
}

/// <summary>A <c>\key pitch \mode</c> command.</summary>
public class KeySignature : Item
{
    /// <summary>Answers the pitch the key is on.</summary>
    /// <returns>The pitch.</returns>
    public Pitching.Pitch Pitch()
    {
        foreach (Note note in Find<Note>()) { return note.Pitch; }

        return null;
    }

    /// <summary>Answers the mode, e.g. <c>major</c> or <c>minor</c>.</summary>
    /// <returns>The mode.</returns>
    public string Mode()
    {
        foreach (Command command in Find<Command>())
        {
            return command.Token?.Text.Substring(1);
        }

        return null;
    }
}

/// <summary>A pipe symbol: <c>|</c>.</summary>
public class PipeSymbol : Item
{
}

/// <summary>A voice separator.</summary>
public class VoiceSeparator : Item
{
}

/// <summary>Any item prefixed with a <c>_</c>, <c>-</c> or <c>^</c> direction.</summary>
public class Postfix : Item
{
    /// <summary>Gets or sets the direction: -1 down, 0 neutral, 1 up.</summary>
    public int Direction { get; set; }
}

/// <summary>A tie.</summary>
public class Tie : Item
{
}

/// <summary>A slur, <c>(</c> or <c>)</c>.</summary>
public class Slur : Item
{
    /// <summary>Gets or sets whether the slur starts or stops.</summary>
    public string Event { get; set; }
}

/// <summary>A phrasing slur, <c>\(</c> or <c>\)</c>.</summary>
public class PhrasingSlur : Item
{
    /// <summary>Gets or sets whether the slur starts or stops.</summary>
    public string Event { get; set; }
}

/// <summary>A beam, <c>[</c> or <c>]</c>.</summary>
public class Beam : Item
{
    /// <summary>Gets or sets whether the beam starts or stops.</summary>
    public string Event { get; set; }
}

/// <summary>Any dynamic symbol.</summary>
public class Dynamic : Item
{
}

/// <summary>An articulation, fingering, string number or other symbol.</summary>
public class Articulation : Item
{
}

/// <summary>A <c>\stringTuning</c> command, with a chord as its argument.</summary>
public class StringTuning : Item
{
}

/// <summary>A LilyPond keyword.</summary>
public class Keyword : Item
{
}

/// <summary>A LilyPond command.</summary>
public class Command : Item
{
}

/// <summary>A user command, most probably referring to music.</summary>
public class UserCommand : Music
{
    /// <summary>Answers the command's name, without the leading backslash.</summary>
    /// <returns>The name.</returns>
    public string Name() => Token?.Text.Substring(1);

    /// <summary>Answers the value assigned to this variable.</summary>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public Item Value()
    {
        foreach (Item item in IterToplevelItemsInclude())
        {
            if (item is Assignment assignment && assignment.Name() == Name())
            {
                return assignment.Value();
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public override Fraction Events(Events events, Fraction time, Fraction scaling)
    {
        Item value = Value();
        return value != null ? events.Traverse(value, time, scaling) : time;
    }
}

/// <summary>A <c>\version</c> command.</summary>
public class Version : Item
{
    /// <summary>Answers the version as a string.</summary>
    /// <returns>The version.</returns>
    public string VersionString()
    {
        foreach (Node node in this)
        {
            if (node is StringItem text) { return text.Value(); }

            if (node is Scheme scheme) { return scheme.GetString(); }
        }

        return string.Empty;
    }

    /// <summary>Answers the version as its integer parts.</summary>
    /// <returns>The parts.</returns>
    public int[] VersionParts()
        => Regex.Matches(VersionString() ?? string.Empty, @"\d+")
            .Select(m => int.Parse(m.Value, CultureInfo.InvariantCulture))
            .ToArray();
}

/// <summary>An <c>\include</c> command that does not change the language.</summary>
public class Include : Item
{
    /// <summary>Gets or sets the document this include resolved to.</summary>
    internal Document IncludedDocument { get; set; }

    /// <summary>Gets or sets whether the include has already been resolved.</summary>
    internal bool IncludeResolved { get; set; }

    /// <summary>Answers the file name included.</summary>
    /// <returns>The name, or <see langword="null"/>.</returns>
    public string Filename()
    {
        foreach (Node node in this)
        {
            if (node is StringItem text) { return text.Value(); }

            if (node is Scheme scheme) { return scheme.GetString(); }
        }

        return null;
    }
}

/// <summary>A command that changes the pitch language — <c>\language</c>, or
/// an <c>\include</c> of a language file.</summary>
public class Language : Item
{
    /// <summary>Gets or sets the language selected.</summary>
    public string LanguageName { get; set; }
}

/// <summary>A command starting markup (<c>\markup</c> and its relatives).</summary>
public class Markup : Item
{
    /// <inheritdoc/>
    public override string PlainText()
        => string.Join(" ", this.Select(n => ((Item)n).PlainText()));
}

/// <summary>A markup command, such as <c>\italic</c>.</summary>
public class MarkupCommand : Item
{
    /// <inheritdoc/>
    public override string PlainText()
    {
        string joiner = Token != null && Token.Text == "\\concat" ? string.Empty : " ";
        Node node = Count == 1 && this[0] is MarkupList ? this[0] : this;
        return string.Join(joiner, node.Select(n => ((Item)n).PlainText()));
    }
}

/// <summary>A user-defined markup command.</summary>
public class MarkupUserCommand : Item
{
    /// <summary>Answers the command's name, without the leading backslash.</summary>
    /// <returns>The name.</returns>
    public string Name() => Token?.Text.Substring(1);

    /// <summary>Answers the value assigned to this markup command.</summary>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public Item Value()
    {
        foreach (Item item in IterToplevelItemsInclude())
        {
            if (item is Assignment assignment && assignment.Name() == Name())
            {
                return assignment.Value();
            }

            //#(define-markup-command (name …) …): upstream only ever looks at
            //the FIRST child at each level, so this walk does the same.
            if (item is Scheme scheme && DefinesMarkupCommand(scheme, Name()))
            {
                return scheme;
            }
        }

        return null;
    }

    private static bool DefinesMarkupCommand(Scheme scheme, string name)
    {
        foreach (Node j in scheme)
        {
            if (!(j is SchemeList list)) { break; }

            foreach (Node k in list)
            {
                if (!(k is SchemeItem item)
                    || item.Token == null
                    || item.Token.Text != "define-markup-command")
                {
                    break;
                }

                //Upstream loops over j[1:] but breaks after the first entry,
                //so only the second element of the list is ever looked at.
                if (list.Count > 1 && list[1] is SchemeList inner)
                {
                    foreach (Node m in inner)
                    {
                        if (m is SchemeItem candidate
                            && candidate.Token != null
                            && candidate.Token.Text == name)
                        {
                            return true;
                        }

                        break;
                    }
                }

                break;
            }

            break;
        }

        return false;
    }
}

/// <summary>A <c>\score</c> inside markup.</summary>
public class MarkupScore : Item
{
}

/// <summary>The group of markup items inside <c>{</c> and <c>}</c> — NOT a
/// <c>\markuplist</c>.</summary>
public class MarkupList : Item
{
    /// <inheritdoc/>
    public override string PlainText()
        => string.Join(" ", this.Select(n => ((Item)n).PlainText()));
}

/// <summary>A markup word.</summary>
public class MarkupWord : Item
{
    /// <inheritdoc/>
    public override string PlainText() => Token?.Text ?? string.Empty;
}

/// <summary>A <c>variable = value</c> construct.</summary>
public class Assignment : Item
{
    /// <summary>Answers the variable's name.</summary>
    /// <returns>The name.</returns>
    public string Name() => Token?.Text;

    /// <summary>Answers the assigned value.</summary>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public Item Value() => Count > 0 ? (Item)this[Count - 1] : null;
}

/// <summary>A <c>\book { … }</c> construct.</summary>
public class Book : Container
{
}

/// <summary>A <c>\bookpart { … }</c> construct.</summary>
public class BookPart : Container
{
}

/// <summary>A <c>\score { … }</c> construct.</summary>
public class Score : Container
{
}

/// <summary>A <c>\header { … }</c> construct.</summary>
public class Header : Container
{
}

/// <summary>A <c>\paper { … }</c> construct.</summary>
public class Paper : Container
{
}

/// <summary>A <c>\layout { … }</c> construct.</summary>
public class Layout : Container
{
}

/// <summary>A <c>\midi { … }</c> construct.</summary>
public class Midi : Container
{
}

/// <summary>A <c>\context { … }</c> construct within a layout or midi block.</summary>
public class LayoutContext : Container
{
}

/// <summary>A <c>\with …</c> construct.</summary>
public class With : Container
{
}

/// <summary>A <c>\set</c> command.</summary>
public class Set : Item
{
    /// <summary>Answers the context, when specified.</summary>
    /// <returns>The context token.</returns>
    public LyToken Context() => ContextOf(Tokens);

    /// <summary>Answers the property being set.</summary>
    /// <returns>The property token.</returns>
    public LyToken Property() => PropertyOf(Tokens);

    /// <summary>Answers the value given as the argument.</summary>
    /// <returns>The value item.</returns>
    public Item Value()
    {
        foreach (Node node in this) { return (Item)node; }

        return null;
    }

    /// <summary>Answers the first context-name token of a token list.</summary>
    /// <param name="tokens">The tokens.</param>
    /// <returns>The token, or <see langword="null"/>.</returns>
    internal static LyToken ContextOf(IReadOnlyList<LyToken> tokens)
        => tokens.FirstOrDefault(t => t is LilyPondMode.ContextName);

    /// <summary>Answers the property token of a token list.</summary>
    /// <param name="tokens">The tokens.</param>
    /// <returns>The token, or <see langword="null"/>.</returns>
    internal static LyToken PropertyOf(IReadOnlyList<LyToken> tokens)
    {
        LyToken property = tokens.FirstOrDefault(t => t is LilyPondMode.ContextProperty);
        if (property != null) { return property; }

        return Enumerable.Reverse(tokens).FirstOrDefault(t => t is LilyPondMode.Name);
    }
}

/// <summary>An <c>\unset</c> command.</summary>
public class Unset : Item
{
    /// <summary>Answers the context, when specified.</summary>
    /// <returns>The context token.</returns>
    public LyToken Context() => Set.ContextOf(Tokens);

    /// <summary>Answers the property being unset.</summary>
    /// <returns>The property token.</returns>
    public LyToken Property() => Set.PropertyOf(Tokens);
}

/// <summary>An <c>\override</c> command.</summary>
public class Override : Item
{
    /// <summary>Answers the context, when specified.</summary>
    /// <returns>The context token.</returns>
    public LyToken Context() => ChildToken<LilyPondMode.ContextName>(this);

    /// <summary>Answers the grob being overridden.</summary>
    /// <returns>The grob token.</returns>
    public LyToken Grob() => ChildToken<LilyPondMode.GrobName>(this);

    /// <summary>Answers the first child whose token is of a type.</summary>
    /// <typeparam name="T">The token type.</typeparam>
    /// <param name="node">The node whose children to search.</param>
    /// <returns>The token, or <see langword="null"/>.</returns>
    internal static LyToken ChildToken<T>(Node node)
        where T : LyToken
    {
        foreach (Node child in node)
        {
            if (((Item)child).Token is T token) { return token; }
        }

        return null;
    }
}

/// <summary>A <c>\revert</c> command.</summary>
public class Revert : Item
{
    /// <summary>Answers the context, when specified.</summary>
    /// <returns>The context token.</returns>
    public LyToken Context() => Override.ChildToken<LilyPondMode.ContextName>(this);

    /// <summary>Answers the grob being reverted.</summary>
    /// <returns>The grob token.</returns>
    public LyToken Grob() => Override.ChildToken<LilyPondMode.GrobName>(this);
}

/// <summary>A <c>\tweak</c> command.</summary>
public class Tweak : Item
{
}

/// <summary>An item in the path of an <c>\override</c> or <c>\revert</c>.</summary>
public class PathItem : Item
{
}

/// <summary>A double-quoted string.</summary>
public class StringItem : Item //was previously: items.String
{
    /// <inheritdoc/>
    public override string PlainText() => Value();

    /// <summary>Answers the string's value, without its escapes and quotes.</summary>
    /// <returns>The value.</returns>
    public string Value()
        => string.Concat(
            Tokens.Take(System.Math.Max(Tokens.Count - 1, 0))
                .Select(t => t is Lex.Character && t.Text.StartsWith("\\", System.StringComparison.Ordinal)
                    ? t.Text.Substring(1)
                    : t.Text));
}

/// <summary>A numerical value, entered directly.</summary>
public class Number : Item
{
    /// <summary>Answers the value: an int, a double or a fraction.</summary>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public object Value()
    {
        if (Token == null) { return null; }

        if (Token is LilyPondMode.IntegerValue)
        {
            return int.Parse(Token.Text, CultureInfo.InvariantCulture);
        }

        if (Token is LilyPondMode.DecimalValue)
        {
            return double.Parse(Token.Text, CultureInfo.InvariantCulture);
        }

        if (Token is LilyPondMode.Fraction)
        {
            return Fraction.Parse(Token.Text);
        }

        return Token.Text.Length > 0 && Token.Text.All(char.IsDigit)
            ? (object)int.Parse(Token.Text, CultureInfo.InvariantCulture)
            : null;
    }
}

/// <summary>A scheme expression inside LilyPond.</summary>
public class Scheme : Item
{
    /// <inheritdoc/>
    public override string PlainText() => GetString();

    /// <summary>Answers two integers specified as a pair.</summary>
    /// <returns>The pair, or <see langword="null"/>.</returns>
    public (int First, int Second)? GetPairInts()
    {
        List<int> values = GetListInts().ToList();
        return values.Count >= 2 ? (values[0], values[1]) : ((int, int)?)null;
    }

    /// <summary>Answers the integer values in this expression.</summary>
    /// <returns>The values.</returns>
    public IReadOnlyList<int> GetListInts()
        => Find<SchemeItem>()
            .Where(i => IsDigits(i.Token))
            .Select(i => int.Parse(i.Token.Text, CultureInfo.InvariantCulture))
            .ToList();

    /// <summary>Answers the first integer value in this expression.</summary>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public int? GetInt()
    {
        foreach (SchemeItem item in Find<SchemeItem>())
        {
            if (IsDigits(item.Token))
            {
                return int.Parse(item.Token.Text, CultureInfo.InvariantCulture);
            }
        }

        return null;
    }

    /// <summary>Answers the first numerical value, which may be a fraction.</summary>
    /// <returns>The value, or <see langword="null"/>.</returns>
    public Fraction? GetFraction()
    {
        foreach (SchemeItem item in Find<SchemeItem>())
        {
            if (IsDigits(item.Token))
            {
                return new Fraction(int.Parse(item.Token.Text, CultureInfo.InvariantCulture));
            }

            if (item.Token is SchemeMode.Fraction)
            {
                return Fraction.Parse(item.Token.Text);
            }
        }

        return null;
    }

    /// <summary>Answers the quoted string value, without its quotes.</summary>
    /// <returns>The value.</returns>
    public string GetString()
        => string.Concat(Find<StringItem>().Select(i => i.Value()));

    /// <summary>Answers a <c>ly:make-moment</c> fraction.</summary>
    /// <returns>The fraction, or <see langword="null"/>.</returns>
    public Fraction? GetLyMakeMoment()
    {
        List<LyToken> tokens = Find<SchemeItem>().Select(i => i.Token).ToList();
        if (tokens.Count != 3 || tokens[0]?.Text != "ly:make-moment") { return null; }

        if (!IsDigits(tokens[1]) || !IsDigits(tokens[2])) { return null; }

        return new Fraction(
            int.Parse(tokens[1].Text, CultureInfo.InvariantCulture),
            int.Parse(tokens[2].Text, CultureInfo.InvariantCulture));
    }

    private static bool IsDigits(LyToken token)
        => token != null && token.Text.Length > 0 && token.Text.All(char.IsDigit);
}

/// <summary>Any scheme token.</summary>
public class SchemeItem : Item
{
}

/// <summary>A <c>( … )</c> expression.</summary>
public class SchemeList : Container
{
}

/// <summary>A <c>'</c> in scheme.</summary>
public class SchemeQuote : Item
{
}

/// <summary>A music expression inside <c>#{</c> and <c>#}</c>.</summary>
public class SchemeLily : Container
{
}
