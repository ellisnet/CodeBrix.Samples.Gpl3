/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2000--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/piano-pedal-engraver.cc, lily/piano-pedal-align-engraver.cc, lily/include/piano-pedal.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - the two pedal engravers share a file, and PedalType is declared once for both;
//     upstream declares the enum in piano-pedal.hh and AGAIN, privately and identically,
//     inside Piano_pedal_align_engraver.
//   - upstream's pedal_types_[] is a PROCESS-GLOBAL table built once by an SCM init hook
//     and GC-protected. The port builds the same table as a static readonly array of an
//     immutable record: the strings and symbols are the same for every score, and there
//     is nothing to protect.
//   - upstream terminates its info_list_ with a null `type_` sentinel and walks with a
//     pointer; the port's loops are over the three real entries, which is the same set.

/*
  TODO:
  * Junk hardcoded sustain/sostenuto/una_corda distinction;
    Softcode using (list (sustain-event SustainPedal PianoPedalBracket) ... )
  * Try to use same engraver for dynamics.
*/

/// <summary>Which of the three piano pedals a grob or event belongs to.</summary>
public enum PedalType
{
    /// <summary>The sostenuto pedal.</summary>
    Sostenuto = 0,

    /// <summary>The sustain pedal.</summary>
    Sustain = 1,

    /// <summary>The una corda pedal.</summary>
    UnaCorda = 2,
}

/// <summary>
/// The precalculated symbols and strings for one pedal type — upstream's
/// <c>Pedal_type_info</c>.
/// </summary>
public sealed class PedalTypeInfo
{
    /// <summary>Initializes the record for one pedal type.</summary>
    /// <param name="type">The pedal type.</param>
    public PedalTypeInfo(PedalType type)
    {
        /* FooBar */
        BaseName = TypeName(type);

        /* foo-bar */
        string baseIdent = TypeIdent(type);

        EventClassSymbol = Symbol.Intern(baseIdent + "-event");
        StyleSymbol = Symbol.Intern("pedal" + BaseName + "Style");
        StringsSymbol = Symbol.Intern("pedal" + BaseName + "Strings");
        PedalString = BaseName + "Pedal";
    }

    /// <summary>Gets the pedal's name in <c>FooBar</c> form.</summary>
    public string BaseName { get; }

    /// <summary>Gets the event class this pedal listens for.</summary>
    public Symbol EventClassSymbol { get; }

    /// <summary>Gets the context property naming this pedal's style.</summary>
    public Symbol StyleSymbol { get; }

    /// <summary>Gets the context property holding this pedal's three strings.</summary>
    public Symbol StringsSymbol { get; }

    /// <summary>Gets the grob name this pedal's text item is made from.</summary>
    public string PedalString { get; }

    /// <summary>Returns the pedal's name in <c>FooBar</c> form.</summary>
    /// <param name="t">The pedal type.</param>
    /// <returns>The name.</returns>
    public static string TypeName(PedalType t)
    {
        switch (t)
        {
            case PedalType.Sostenuto:
                return "Sostenuto";
            case PedalType.Sustain:
                return "Sustain";
            case PedalType.UnaCorda:
                return "UnaCorda";
            default:
                Warn.ProgrammingError("Unknown pedal type");
                return null;
        }
    }

    /// <summary>Returns the pedal's name in <c>foo-bar</c> form.</summary>
    /// <param name="t">The pedal type.</param>
    /// <returns>The identifier.</returns>
    public static string TypeIdent(PedalType t)
    {
        switch (t)
        {
            case PedalType.Sostenuto:
                return "sostenuto";
            case PedalType.Sustain:
                return "sustain";
            case PedalType.UnaCorda:
                return "una-corda";
            default:
                Warn.ProgrammingError("Unknown pedal type");
                return null;
        }
    }
}

/// <summary>
/// Engraves piano pedal symbols and brackets.
/// </summary>
public class PianoPedalEngraver : Engraver
{
    internal static readonly PedalTypeInfo[] PedalTypes =
    {
        new PedalTypeInfo(PedalType.Sostenuto),
        new PedalTypeInfo(PedalType.Sustain),
        new PedalTypeInfo(PedalType.UnaCorda),
    };

    private static readonly Symbol BracketFlareSymbol = Symbol.Intern("bracket-flare");
    private static readonly Symbol PedalTextSymbol = Symbol.Intern("pedal-text");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol SpanDirectionSymbol = Symbol.Intern("span-direction");
    private static readonly Symbol MixedSymbol = Symbol.Intern("mixed");
    private static readonly Symbol BracketSymbol = Symbol.Intern("bracket");
    private static readonly Symbol CurrentMusicalColumnSymbol
        = Symbol.Intern("currentMusicalColumn");
    private static readonly Symbol CurrentCommandColumnSymbol
        = Symbol.Intern("currentCommandColumn");

    // upstream's Pedal_info, one per type.
    private sealed class PedalInfo
    {
        internal PedalTypeInfo Type;

        /* Event for currently running pedal. */
        internal StreamEvent CurrentBracketEv;

        /*
          Event for currently starting pedal, (necessary?
          distinct from current_bracket_ev_, since current_bracket_ev_ only
          necessary for brackets, not for text style.
        */
        internal StreamEvent StartEv;

        /* Events that were found in this timestep. */
        internal DrulArray<StreamEvent> EventDrul = new DrulArray<StreamEvent>(null, null);

        internal Item Item;
        internal Spanner Bracket;               // A single portion of a pedal bracket
        internal Spanner FinishedBracket;
    }

    private readonly PedalInfo[] _infoList =
    {
        new PedalInfo(),
        new PedalInfo(),
        new PedalInfo(),
    };

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public PianoPedalEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Piano_pedal_engraver";

    /// <summary>Wires each pedal slot to its type.</summary>
    public override void Initialize()
    {
        base.Initialize();
        for (int i = 0; i < PedalTypes.Length; i++)
        {
            PedalInfo info = _infoList[i];
            info.Type = PedalTypes[i];
            info.Item = null;
            info.Bracket = null;
            info.FinishedBracket = null;
            info.CurrentBracketEv = null;
            info.EventDrul[Direction.Negative] = null;
            info.EventDrul[Direction.Positive] = null;
            info.StartEv = null;
        }
    }

    /// <summary>Starts listening for the three pedal events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(PedalTypes[(int)PedalType.Sostenuto].EventClassSymbol,
            ev => ListenPedal(PedalType.Sostenuto, ev));
        ListenTo(PedalTypes[(int)PedalType.Sustain].EventClassSymbol,
            ev => ListenPedal(PedalType.Sustain, ev));
        ListenTo(PedalTypes[(int)PedalType.UnaCorda].EventClassSymbol,
            ev => ListenPedal(PedalType.UnaCorda, ev));
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Makes the text and bracket grobs each pedal's style asks for.</summary>
    public override void ProcessMusic()
    {
        foreach (PedalInfo p in _infoList)
        {
            if (p.EventDrul[Direction.Positive] != null
                || p.EventDrul[Direction.Negative] != null)
            {
                /* Choose the appropriate grobs to add to the line spanner
                   These can be text items or text-spanners

                   ugh, code dup, should read grob to create from other
                   property.

                   bracket: |_________/\____|
                   text:    Ped.     *Ped.  *
                   mixed:   Ped. _____/\____|
                */
                object style = GetProperty(p.Type.StyleSymbol);
                bool mixed = ReferenceEquals(style, MixedSymbol);
                bool bracket = mixed || ReferenceEquals(style, BracketSymbol);
                bool text = mixed || ReferenceEquals(style, TextSymbol);

                if (text && p.Item == null)
                {
                    CreateTextGrobs(p, mixed);
                }

                if (bracket)
                {
                    CreateBracketGrobs(p, mixed);
                }
            }
        }
    }

    /// <summary>Binds the open brackets and closes the finished ones.</summary>
    public override void StopTranslationTimestep()
    {
        foreach (PedalInfo p in _infoList)
        {
            TypesetAll(p);
            if (p.Bracket != null && p.Bracket.GetBound(Direction.Negative) == null)
            {
                p.Bracket.SetBound(
                    Direction.Negative, GetProperty(CurrentMusicalColumnSymbol) as Grob);
            }
        }

        foreach (PedalInfo p in _infoList)
        {
            p.EventDrul[Direction.Positive] = null;
            p.EventDrul[Direction.Negative] = null;
        }
    }

    /// <summary>Closes any bracket still running at the end of the piece.</summary>
    public override void FinalizeTranslation()
    {
        foreach (PedalInfo p in _infoList)
        {
            if (p.Bracket != null && !p.Bracket.IsLive)
            {
                p.Bracket = null;
            }

            if (p.Bracket != null)
            {
                Item c = GetProperty(CurrentCommandColumnSymbol) as Item;
                p.Bracket.SetBound(Direction.Positive, c);
                p.FinishedBracket = p.Bracket;
                p.Bracket = null;
                TypesetAll(p);
            }
        }
    }

    private void ListenPedal(PedalType type, StreamEvent ev)
    {
        Direction d = DirectionalElementInterface.FromScheme(
            ev.GetProperty(SpanDirectionSymbol), Direction.Center);
        PedalInfo info = _infoList[(int)type];
        StreamEvent existing = info.EventDrul[d];
        StreamEvent.AssignEventOnce(ref existing, ev);
        info.EventDrul[d] = existing;
    }

    private void CreateTextGrobs(PedalInfo p, bool mixed)
    {
        object s = Nil.Instance;
        object strings = GetProperty(p.Type.StringsSymbol);
        List<object> stringList = Pair.ToList(strings);
        if (stringList.Count < 3)
        {
            StreamEvent m = p.EventDrul[Direction.Negative] ?? p.EventDrul[Direction.Positive];
            string msg = "expect 3 strings for piano pedals, found: " + stringList.Count;
            if (m != null)
            {
                TranslatorSchemeHelpers.EventWarning(m, msg);
            }
            else
            {
                Warn.Warning(msg);
            }

            return;
        }

        if (p.EventDrul[Direction.Positive] != null && p.EventDrul[Direction.Negative] != null)
        {
            if (!mixed)
            {
                if (p.StartEv == null)
                {
                    TranslatorSchemeHelpers.EventWarning(
                        p.EventDrul[Direction.Positive],
                        "cannot find start of piano pedal: `" + p.Type.BaseName + "'");
                }
                else
                {
                    s = stringList[1];
                }

                p.StartEv = p.EventDrul[Direction.Negative];
            }
        }
        else if (p.EventDrul[Direction.Positive] != null)
        {
            if (!mixed)
            {
                if (p.StartEv == null)
                {
                    TranslatorSchemeHelpers.EventWarning(
                        p.EventDrul[Direction.Positive],
                        "cannot find start of piano pedal: `" + p.Type.BaseName + "'");
                }
                else
                {
                    s = stringList[2];
                }

                p.StartEv = null;
            }
        }
        else if (p.EventDrul[Direction.Negative] != null)
        {
            p.StartEv = p.EventDrul[Direction.Negative];
            s = stringList[0];
        }

        //was previously: `s is string`. The strings come out of pedalSustainStrings and
        // friends as MutableStrings, so this never matched and no pedal item was made at
        // all; the property still receives the ORIGINAL Scheme value, as upstream does.
        if (SchemeUtilities.IsString(s))
        {
            p.Item = MakeItem(
                p.Type.PedalString,
                p.EventDrul[Direction.Negative] ?? p.EventDrul[Direction.Positive]);
            p.Item.SetProperty(TextSymbol, s);
        }

        if (!mixed)
        {
            p.EventDrul[Direction.Negative] = null;
            p.EventDrul[Direction.Positive] = null;
        }
    }

    private void CreateBracketGrobs(PedalInfo p, bool mixed)
    {
        if (p.Bracket == null && p.EventDrul[Direction.Positive] != null)
        {
            TranslatorSchemeHelpers.EventWarning(
                p.EventDrul[Direction.Positive],
                "cannot find start of piano pedal bracket: `" + p.Type.BaseName + "'");
            p.EventDrul[Direction.Positive] = null;
        }

        if (p.EventDrul[Direction.Positive] != null)
        {
            Grob cmc = GetProperty(CurrentMusicalColumnSymbol) as Grob;
            p.Bracket.SetBound(Direction.Positive, cmc);

            /*
              Set properties so that the stencil-creating function will
              know whether the right edge should be flared ___/
            */
            if (p.EventDrul[Direction.Negative] == null)
            {
                object flare = p.Bracket.GetProperty(BracketFlareSymbol);
                if (flare is Pair flarePair)
                {
                    p.Bracket.SetProperty(
                        BracketFlareSymbol, new Pair(flarePair.Car, 0L));
                }
            }

            p.FinishedBracket = p.Bracket;
            p.Bracket = null;
            AnnounceEndGrob(p.FinishedBracket, p.EventDrul[Direction.Positive]);
            p.CurrentBracketEv = null;
        }

        if (p.EventDrul[Direction.Negative] != null)
        {
            p.StartEv = p.EventDrul[Direction.Negative];
            p.CurrentBracketEv = p.EventDrul[Direction.Negative];
            p.Bracket = MakeSpanner("PianoPedalBracket", p.EventDrul[Direction.Negative]);

            /*
              Set properties so that the stencil-creating function will
              know whether the left edge should be flared \___
            */
            if (p.FinishedBracket == null)
            {
                object flare = p.Bracket.GetProperty(BracketFlareSymbol);
                p.Bracket.SetProperty(
                    BracketFlareSymbol,
                    new Pair(0L, flare is Pair flarePair ? flarePair.Cdr : Nil.Instance));
            }

            /* Set this property for 'mixed style' pedals,    Ped._______/\ ,
               so the stencil function will shorten the ____ line by the length of the
               Ped. text.
            */
            if (mixed)
            {
                /*
                  Mixed style: Store a pointer to the preceding text for use in
                  calculating the length of the line
                  TODO:
                  WTF is pedal-text not the bound of the object? --hwn
                */
                if (p.Item != null)
                {
                    p.Bracket.SetObject(PedalTextSymbol, p.Item);
                }
            }
        }

        p.EventDrul[Direction.Negative] = null;
        p.EventDrul[Direction.Positive] = null;
    }

    private void TypesetAll(PedalInfo p)
    {
        /*
          Handle suicide.
        */
        if (p.FinishedBracket != null && !p.FinishedBracket.IsLive)
        {
            p.FinishedBracket = null;
        }

        if (p.Item != null)
        {
            p.Item = null;
        }

        if (p.FinishedBracket != null)
        {
            if (p.FinishedBracket.GetBound(Direction.Positive) == null)
            {
                p.FinishedBracket.SetBound(
                    Direction.Positive, GetProperty(CurrentMusicalColumnSymbol) as Grob);
            }

            p.FinishedBracket = null;
        }
    }
}

/*
  TODO:
  * Detach from pedal specifics,
  * Also use this engraver for dynamics.
*/

/// <summary>
/// Aligns piano pedal symbols and brackets.
/// </summary>
public class PianoPedalAlignEngraver : Engraver
{
    private static readonly Symbol CurrentCommandColumnSymbol
        = Symbol.Intern("currentCommandColumn");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");
    private static readonly Symbol PianoPedalBracketInterface
        = Symbol.Intern("piano-pedal-bracket-interface");
    private static readonly Symbol PianoPedalScriptInterface
        = Symbol.Intern("piano-pedal-script-interface");
    private static readonly Symbol SostenutoEventSymbol = Symbol.Intern("sostenuto-event");
    private static readonly Symbol SustainEventSymbol = Symbol.Intern("sustain-event");
    private static readonly Symbol UnaCordaEventSymbol = Symbol.Intern("una-corda-event");

    // upstream's Pedal_align_info.
    private sealed class PedalAlignInfo
    {
        internal Spanner LineSpanner;
        internal Grob CarryingItem;
        internal Spanner CarryingSpanner;
        internal Spanner FinishedCarryingSpanner;

        internal void Clear()
        {
            LineSpanner = null;
            CarryingSpanner = null;
            CarryingItem = null;
            FinishedCarryingSpanner = null;
        }

        internal bool IsFinished()
        {
            bool doContinue = CarryingItem != null;
            doContinue |= CarryingSpanner != null && FinishedCarryingSpanner == null;
            doContinue |= CarryingSpanner != null
                          && !ReferenceEquals(FinishedCarryingSpanner, CarryingSpanner);
            return !doContinue;
        }
    }

    private readonly PedalAlignInfo[] _pedalInfo =
    {
        new PedalAlignInfo(),
        new PedalAlignInfo(),
        new PedalAlignInfo(),
    };

    private readonly List<Item> _supports = new List<Item>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public PianoPedalAlignEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Piano_pedal_align_engraver";

    /// <summary>Drops last timestep's support points.</summary>
    public override void StartTranslationTimestep() => _supports.Clear();

    /// <summary>Collects the note columns, brackets and scripts of this timestep.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        // Upstream's registration order: note_column, piano_pedal_bracket,
        // piano_pedal_script.
        if (info.Grob.HasInterface(NoteColumnInterface) && info.Grob is Item column)
        {
            _supports.Add(column);
        }

        if (info.Grob.HasInterface(PianoPedalBracketInterface) && info.Grob is Spanner bracket)
        {
            PedalType type = GetGrobPedalType(info.EventCause);
            Grob sp = MakeLineSpanner(type, bracket);
            AxisGroupInterface.AddElement(sp, bracket);
            _pedalInfo[(int)type].CarryingSpanner = bracket;
        }

        if (info.Grob.HasInterface(PianoPedalScriptInterface))
        {
            PedalType type = GetGrobPedalType(info.EventCause);
            Grob sp = MakeLineSpanner(type, info.Grob);
            AxisGroupInterface.AddElement(sp, info.Grob);
            _pedalInfo[(int)type].CarryingItem = info.Grob;
        }
    }

    /// <summary>Notes a bracket ending.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeEndGrob(GrobInfo info)
    {
        if (info.Grob.HasInterface(PianoPedalBracketInterface) && info.Grob is Spanner bracket)
        {
            PedalType type = GetGrobPedalType(info.EventCause);
            _pedalInfo[(int)type].FinishedCarryingSpanner = bracket;
        }
    }

    /// <summary>Bounds each alignment spanner and hangs its supports off the notes.</summary>
    public override void StopTranslationTimestep()
    {
        foreach (PedalAlignInfo pi in _pedalInfo)
        {
            if (pi.LineSpanner != null)
            {
                if (pi.CarryingItem != null)
                {
                    if (pi.LineSpanner.GetBound(Direction.Negative) == null)
                    {
                        pi.LineSpanner.SetBound(Direction.Negative, pi.CarryingItem);
                    }

                    pi.LineSpanner.SetBound(Direction.Positive, pi.CarryingItem);
                }
                else if (pi.CarryingSpanner != null || pi.FinishedCarryingSpanner != null)
                {
                    if (pi.LineSpanner.GetBound(Direction.Negative) == null
                        && pi.CarryingSpanner != null)
                    {
                        if (pi.CarryingSpanner.GetBound(Direction.Negative) is Item bound)
                        {
                            pi.LineSpanner.SetBound(Direction.Negative, bound);
                        }
                    }

                    if (pi.FinishedCarryingSpanner != null)
                    {
                        Item bound = pi.FinishedCarryingSpanner.GetBound(Direction.Positive);
                        pi.LineSpanner.SetBound(Direction.Positive, bound);
                    }
                }

                for (int i = 0; i < _supports.Count; i++)
                {
                    SidePositionInterface.AddSupport(pi.LineSpanner, _supports[i]);
                }

                if (pi.IsFinished())
                {
                    AnnounceEndGrob(pi.LineSpanner, Nil.Instance);
                    pi.Clear();
                }
            }

            pi.CarryingItem = null;
        }
    }

    /// <summary>Bounds any alignment spanner still open at the end of the piece.</summary>
    public override void FinalizeTranslation()
    {
        for (int i = 0; i < _pedalInfo.Length; i++)
        {
            if (_pedalInfo[i].LineSpanner != null)
            {
                Item c = GetProperty(CurrentCommandColumnSymbol) as Item;
                _pedalInfo[i].LineSpanner.SetBound(Direction.Positive, c);
                _pedalInfo[i].Clear();
            }
        }
    }

    private PedalType GetGrobPedalType(StreamEvent cause)
    {
        if (cause != null)
        {
            if (cause.IsInEventClass(SostenutoEventSymbol))
            {
                return PedalType.Sostenuto;
            }

            if (cause.IsInEventClass(SustainEventSymbol))
            {
                return PedalType.Sustain;
            }

            if (cause.IsInEventClass(UnaCordaEventSymbol))
            {
                return PedalType.UnaCorda;
            }
        }

        Warn.ProgrammingError("Unknown piano pedal type.  Defaulting to sustain");
        return PedalType.Sustain;
    }

    private Spanner MakeLineSpanner(PedalType t, Grob cause)
    {
        Spanner sp = _pedalInfo[(int)t].LineSpanner;
        if (sp == null)
        {
            switch (t)
            {
                case PedalType.Sostenuto:
                    sp = MakeSpanner("SostenutoPedalLineSpanner", cause);
                    break;
                case PedalType.Sustain:
                    sp = MakeSpanner("SustainPedalLineSpanner", cause);
                    break;
                case PedalType.UnaCorda:
                    sp = MakeSpanner("UnaCordaPedalLineSpanner", cause);
                    break;
                default:
                    Warn.ProgrammingError("No pedal type fonud!");
                    return sp;
            }

            _pedalInfo[(int)t].LineSpanner = sp;
        }

        return sp;
    }
}
