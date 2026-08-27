// === python-ly ly.dom module (the context types and music elements) ===
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

using System.Globalization;
using System.Linq;
using PitchTable = Fresco.Brix.Ly.Pitching.Pitches;

namespace Fresco.Brix.Ly.Dom; //was previously: ly/dom.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.
//
// Every context type below takes the same (contextId, isNew, parent) arguments
// upstream's ContextType does; the class NAME is what LilyPond is told, so the
// names are kept exactly as upstream spells them.

/// <summary>A <c>\new ChoirStaff</c> context.</summary>
public class ChoirStaff : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public ChoirStaff(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new ChordNames</c> context.</summary>
public class ChordNames : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public ChordNames(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new CueVoice</c> context.</summary>
public class CueVoice : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public CueVoice(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new Devnull</c> context.</summary>
public class Devnull : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public Devnull(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new DrumStaff</c> context.</summary>
public class DrumStaff : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public DrumStaff(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new DrumVoice</c> context.</summary>
public class DrumVoice : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public DrumVoice(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new Dynamics</c> context.</summary>
public class Dynamics : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public Dynamics(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new FiguredBass</c> context.</summary>
public class FiguredBass : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public FiguredBass(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new FretBoards</c> context.</summary>
public class FretBoards : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public FretBoards(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new Global</c> context.</summary>
public class Global : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public Global(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new GrandStaff</c> context.</summary>
public class GrandStaff : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public GrandStaff(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new GregorianTranscriptionStaff</c> context.</summary>
public class GregorianTranscriptionStaff : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public GregorianTranscriptionStaff(
        object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new GregorianTranscriptionVoice</c> context.</summary>
public class GregorianTranscriptionVoice : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public GregorianTranscriptionVoice(
        object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new InnerChoirStaff</c> context.</summary>
public class InnerChoirStaff : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public InnerChoirStaff(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new InnerStaffGroup</c> context.</summary>
public class InnerStaffGroup : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public InnerStaffGroup(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new Lyrics</c> context.</summary>
public class Lyrics : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public Lyrics(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new MensuralStaff</c> context.</summary>
public class MensuralStaff : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public MensuralStaff(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new MensuralVoice</c> context.</summary>
public class MensuralVoice : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public MensuralVoice(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new NoteNames</c> context.</summary>
public class NoteNames : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public NoteNames(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new PianoStaff</c> context.</summary>
public class PianoStaff : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public PianoStaff(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new RhythmicStaff</c> context.</summary>
public class RhythmicStaff : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public RhythmicStaff(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>
/// The Score CONTEXT — the name <c>Score</c> is taken by the
/// <c>\score { }</c> section, which is used more often, so this class carries
/// the type name instead.
/// </summary>
public class ScoreContext : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public ScoreContext(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }

    /// <inheritdoc/>
    public override string ContextTypeName => "Score";
}

/// <summary>A <c>\new Staff</c> context.</summary>
public class Staff : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public Staff(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new StaffGroup</c> context.</summary>
public class StaffGroup : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public StaffGroup(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new TabStaff</c> context.</summary>
public class TabStaff : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public TabStaff(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new TabVoice</c> context.</summary>
public class TabVoice : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public TabVoice(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new VaticanaStaff</c> context.</summary>
public class VaticanaStaff : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public VaticanaStaff(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new VaticanaVoice</c> context.</summary>
public class VaticanaVoice : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public VaticanaVoice(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A <c>\new Voice</c> context.</summary>
public class Voice : ContextType
{
    /// <summary>Initializes the context.</summary>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public Voice(object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
    {
    }
}

/// <summary>A context the user creates, e.g. <c>\new MyStaff = cid</c>.</summary>
public class UserContext : ContextType
{
    private readonly string _contextTypeName;

    /// <summary>Initializes the context.</summary>
    /// <param name="contextTypeName">The context type written.</param>
    /// <param name="contextId">The context id, if any.</param>
    /// <param name="isNew">Whether it is a <c>\new</c> context.</param>
    /// <param name="parent">The parent to attach to.</param>
    public UserContext(
        string contextTypeName, object contextId = null, bool isNew = true, Node parent = null)
        : base(contextId, isNew, parent)
        => _contextTypeName = contextTypeName;

    /// <inheritdoc/>
    public override string ContextTypeName => _contextTypeName;
}

/// <summary>
/// A <c>Context.property</c> or <c>Context.layoutObject</c> construct, e.g.
/// <c>Staff.aDueText</c>.
/// </summary>
public class ContextProperty : Leaf
{
    /// <summary>Initializes the property.</summary>
    /// <param name="property">The property name.</param>
    /// <param name="context">The context name, if any.</param>
    public ContextProperty(string property, string context = null)
    {
        //⚠ DELIBERATE DIVERGENCE FROM UPSTREAM (ruling FR14). Upstream's
        //signature takes a parent and then never attaches the leaf to it, so a
        //caller who passes one gets a node that silently is not in the tree.
        //The argument is simply not offered here; a caller appends the leaf
        //itself, as it would any other.
        Property = property;
        Context = context;
    }

    /// <summary>Gets or sets the property name.</summary>
    public string Property { get; set; }

    /// <summary>Gets or sets the context name.</summary>
    public string Context { get; set; }

    /// <inheritdoc/>
    public override string Ly(Printer printer)
    {
        if (string.IsNullOrEmpty(Context)) { return Property; }

        //In \lyrics or \lyricmode, put spaces around the dot.
        InputMode mode = FindParent<InputMode>();
        return mode is LyricMode
            ? Context + " . " + Property
            : Context + "." + Property;
    }
}

/// <summary>The base class for the input modes, e.g. lyricmode or chordmode.</summary>
public class InputMode : StatementEnclosed
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public InputMode(Node parent = null)
        : base(parent)
    {
    }
}

/// <summary>A <c>\chordmode { }</c> expression.</summary>
public class ChordMode : InputMode
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public ChordMode(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "chordmode";
}

/// <summary>A <c>\chords { }</c> expression.</summary>
public class InputChords : ChordMode
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public InputChords(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "chords";
}

/// <summary>A <c>\lyricmode { }</c> expression.</summary>
public class LyricMode : InputMode
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public LyricMode(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "lyricmode";
}

/// <summary>A <c>\lyrics { }</c> expression.</summary>
public class InputLyrics : LyricMode
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public InputLyrics(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "lyrics";
}

/// <summary>A <c>\notemode { }</c> expression.</summary>
public class NoteMode : InputMode
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public NoteMode(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "notemode";
}

/// <summary>A <c>\notes { }</c> expression.</summary>
public class InputNotes : NoteMode
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public InputNotes(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "notes";
}

/// <summary>A <c>\figuremode { }</c> expression.</summary>
public class FigureMode : InputMode
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public FigureMode(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "figuremode";
}

/// <summary>A <c>\figures { }</c> expression.</summary>
public class InputFigures : FigureMode
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public InputFigures(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "figures";
}

/// <summary>A <c>\drummode { }</c> expression.</summary>
public class DrumMode : InputMode
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public DrumMode(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "drummode";
}

/// <summary>A <c>\drums { }</c> expression.</summary>
public class InputDrums : DrumMode
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public InputDrums(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "drums";
}

/// <summary>An <c>\addlyrics { }</c> expression.</summary>
public class AddLyrics : InputLyrics
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public AddLyrics(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "addlyrics";

    /// <inheritdoc/>
    protected override bool DefaultMayRemoveBrackets => false;

    /// <inheritdoc/>
    protected override int DefaultBefore => 1;

    /// <inheritdoc/>
    protected override int DefaultAfter => 1;
}

/// <summary>A <c>\lyricsto</c> expression.</summary>
public class LyricsTo : LyricMode
{
    /// <summary>Initializes the expression.</summary>
    /// <param name="contextId">The context the lyrics belong to.</param>
    /// <param name="parent">The parent to attach to.</param>
    public LyricsTo(object contextId, Node parent = null)
        : base(parent)
        => ContextId = contextId;

    /// <inheritdoc/>
    public override object Name { get; set; } = "lyricsto";

    /// <summary>Gets or sets the context the lyrics belong to.</summary>
    public object ContextId { get; set; } //was previously: cid

    /// <inheritdoc/>
    public override string Ly(Printer printer)
        => string.Join(
            " ",
            "\\" + Format(Name),
            printer.QuoteString(Format(ContextId)),
            EnclosedLy(printer));
}

/// <summary>
/// A pitch: the octave is an integer, zero for the octave holding middle C;
/// the note runs 0 (C) to 6 (B); the alteration is in whole tones.
/// </summary>
public class Pitch : Leaf
{
    /// <summary>Initializes the pitch.</summary>
    /// <param name="octave">The octave.</param>
    /// <param name="note">The note, 0 to 6.</param>
    /// <param name="alter">The alteration, in whole tones.</param>
    /// <param name="parent">The parent to attach to.</param>
    public Pitch(int octave = 0, int note = 0, Fraction alter = default, Node parent = null)
    {
        Octave = octave;
        Note = note;
        Alter = alter;
        parent?.Append(this);
    }

    /// <summary>Gets or sets the octave.</summary>
    public int Octave { get; set; }

    /// <summary>Gets or sets the note, 0 to 6.</summary>
    public int Note { get; set; }

    /// <summary>Gets or sets the alteration, in whole tones.</summary>
    public Fraction Alter { get; set; }

    /// <inheritdoc/>
    public override string Ly(Printer printer)
    {
        string p = PitchTable.PitchWriterFor(printer.Language).Write(Note, Alter);
        if (Octave < -1) { return p + new string(',', -Octave - 1); }

        if (Octave > -1) { return p + new string('\'', Octave + 1); }

        return p;
    }
}

/// <summary>
/// A duration in logarithmic form (-2 for <c>\longa</c> through 8), with a
/// number of dots and a scaling factor.
/// </summary>
public class Duration : Leaf
{
    /// <summary>Initializes the duration.</summary>
    /// <param name="duration">The logarithmic duration.</param>
    /// <param name="dots">The number of dots.</param>
    /// <param name="factor">The scaling factor.</param>
    /// <param name="parent">The parent to attach to.</param>
    public Duration(int duration, int dots = 0, Fraction? factor = null, Node parent = null)
    {
        Dur = duration;
        Dots = dots;
        Factor = factor ?? Fraction.One;
        parent?.Append(this);
    }

    /// <summary>Gets or sets the logarithmic duration.</summary>
    public int Dur { get; set; }

    /// <summary>Gets or sets the number of dots.</summary>
    public int Dots { get; set; }

    /// <summary>Gets or sets the scaling factor.</summary>
    public Fraction Factor { get; set; }

    /// <inheritdoc/>
    public override string Ly(Printer printer) => Durations.ToString(Dur, Dots, Factor);
}

/// <summary>
/// A chord of one or more pitches and optionally one duration — a stand-in
/// until real music objects arrive, as upstream puts it.
/// </summary>
public class Chord : Container
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Chord(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override string Ly(Printer printer)
    {
        Pitch[] pitches = FindChildren<Pitch>(1).ToArray();
        string s = pitches.Length == 1
            ? pitches[0].Ly(printer)
            : "<" + string.Join(" ", pitches.Select(p => p.Ly(printer))) + ">";

        Duration duration = FindChild<Duration>(1);
        return duration != null ? s + duration.Ly(printer) : s;
    }
}

/// <summary>A <c>\relative &lt;pitch&gt; music</c> expression.</summary>
public class Relative : Statement
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Relative(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "relative";
}

/// <summary>A <c>\transposition &lt;pitch&gt;</c> expression.</summary>
public class Transposition : Statement
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Transposition(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "transposition";
}

/// <summary>A key signature, e.g. <c>\key c \major</c>.</summary>
public class KeySignature : Leaf
{
    /// <summary>Initializes the signature.</summary>
    /// <param name="note">The note, 0 to 6.</param>
    /// <param name="alter">The alteration, in whole tones.</param>
    /// <param name="mode">The mode, e.g. major or minor.</param>
    /// <param name="parent">The parent to attach to.</param>
    public KeySignature(
        int note = 0, Fraction alter = default, string mode = "major", Node parent = null)
    {
        Note = note;
        Alter = alter;
        Mode = mode;
        parent?.Append(this);
    }

    /// <summary>Gets or sets the note, 0 to 6.</summary>
    public int Note { get; set; }

    /// <summary>Gets or sets the alteration, in whole tones.</summary>
    public Fraction Alter { get; set; }

    /// <summary>Gets or sets the mode.</summary>
    public string Mode { get; set; }

    /// <inheritdoc/>
    public override string Ly(Printer printer)
        => "\\key "
            + PitchTable.PitchWriterFor(printer.Language).Write(Note, Alter)
            + " \\" + Mode;
}

/// <summary>A time signature, e.g. <c>\time 4/4</c>.</summary>
public class TimeSignature : Leaf
{
    /// <summary>Initializes the signature.</summary>
    /// <param name="numerator">The upper number.</param>
    /// <param name="beat">The lower number.</param>
    /// <param name="parent">The parent to attach to.</param>
    public TimeSignature(int numerator, int beat, Node parent = null)
    {
        Numerator = numerator;
        Beat = beat;
        parent?.Append(this);
    }

    /// <summary>Gets or sets the upper number.</summary>
    public int Numerator { get; set; } //was previously: num

    /// <summary>Gets or sets the lower number.</summary>
    public int Beat { get; set; }

    /// <inheritdoc/>
    public override string Ly(Printer printer)
        => string.Format(CultureInfo.InvariantCulture, "\\time {0}/{1}", Numerator, Beat);
}

/// <summary>A <c>\partial &lt;duration&gt;</c> expression.</summary>
public class Partial : Duration
{
    /// <summary>Initializes the expression.</summary>
    /// <param name="duration">The logarithmic duration.</param>
    /// <param name="dots">The number of dots.</param>
    /// <param name="factor">The scaling factor.</param>
    /// <param name="parent">The parent to attach to.</param>
    public Partial(int duration, int dots = 0, Fraction? factor = null, Node parent = null)
        : base(duration, dots, factor, parent)
    {
    }

    /// <inheritdoc/>
    protected override int DefaultBefore => 1;

    /// <inheritdoc/>
    protected override int DefaultAfter => 1;

    /// <inheritdoc/>
    public override string Ly(Printer printer) => "\\partial " + base.Ly(printer);
}

/// <summary>A tempo setting, e.g. <c>\tempo 4 = 100</c>; may carry a markup or
/// a quoted string as a child.</summary>
public class Tempo : Container
{
    /// <summary>Initializes the setting.</summary>
    /// <param name="duration">The note value the tempo is given in.</param>
    /// <param name="value">The number of those per minute.</param>
    /// <param name="parent">The parent to attach to.</param>
    public Tempo(object duration, object value, Node parent = null)
    {
        DurationValue = duration;
        Value = value;
        parent?.Append(this);
    }

    /// <summary>Gets or sets the note value the tempo is given in.</summary>
    public object DurationValue { get; set; } //was previously: duration

    /// <summary>Gets or sets the number of those per minute.</summary>
    public object Value { get; set; }

    /// <inheritdoc/>
    protected override int DefaultBefore => 1;

    /// <inheritdoc/>
    protected override int DefaultAfter => 1;

    /// <inheritdoc/>
    public override string Ly(Printer printer)
    {
        var result = new System.Collections.Generic.List<string> { "\\tempo" };
        if (Count > 0) { result.Add(ContainerLy(printer)); }

        if (Value != null && Format(Value).Length > 0)
        {
            result.Add(Format(DurationValue) + "=" + Format(Value));
        }

        return string.Join(" ", result);
    }
}

/// <summary>A clef.</summary>
public class Clef : Leaf
{
    /// <summary>Initializes the clef.</summary>
    /// <param name="clef">The clef name.</param>
    /// <param name="parent">The parent to attach to.</param>
    public Clef(string clef, Node parent = null)
    {
        ClefName = clef;
        parent?.Append(this);
    }

    /// <summary>Gets or sets the clef name.</summary>
    public string ClefName { get; set; } //was previously: clef

    /// <inheritdoc/>
    public override string Ly(Printer printer)
        => "\\clef "
            + (ClefName.Length > 0 && ClefName.All(char.IsLetter)
                ? ClefName
                : "\"" + ClefName + "\"");
}

/// <summary>A voice separator.</summary>
public class VoiceSeparator : Leaf
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public VoiceSeparator(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override string Ly(Printer printer) => "\\\\";
}

/// <summary>The <c>\mark</c> command.</summary>
public class Mark : Statement
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Mark(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "mark";
}

/// <summary>
/// The <c>\markup</c> command; with several children it prints <c>{</c> and
/// <c>}</c> around them itself.
/// </summary>
public class Markup : StatementEnclosed
{
    /// <summary>Initializes the node, optionally attaching it to a parent.</summary>
    /// <param name="parent">The parent to attach to.</param>
    public Markup(Node parent = null)
        : base(parent)
    {
    }

    /// <inheritdoc/>
    public override object Name { get; set; } = "markup";
}

/// <summary>
/// A markup command that auto-encloses its arguments, such as
/// <c>italic</c> or <c>bold</c>.
/// </summary>
public class MarkupEnclosed : CommandEnclosed
{
    /// <summary>Initializes the command.</summary>
    /// <param name="name">The command name.</param>
    /// <param name="parent">The parent to attach to.</param>
    public MarkupEnclosed(object name, Node parent = null)
        : base(name, parent)
    {
    }
}

/// <summary>
/// A markup command that does NOT auto-enclose its arguments, useful for
/// commands such as <c>note-by-number</c> or <c>hspace</c>; its arguments are
/// its children.
/// </summary>
public class MarkupCommand : Command
{
    /// <summary>Initializes the command.</summary>
    /// <param name="name">The command name.</param>
    /// <param name="parent">The parent to attach to.</param>
    public MarkupCommand(object name, Node parent = null)
        : base(name, parent)
    {
    }
}
