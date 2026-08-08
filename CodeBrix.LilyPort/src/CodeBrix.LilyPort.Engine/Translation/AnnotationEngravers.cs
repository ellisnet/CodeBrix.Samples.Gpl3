/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/font-size-engraver.cc, lily/tweak-engraver.cc, lily/balloon-engraver.cc, lily/parenthesis-engraver.cc, lily/instrument-name-engraver.cc, lily/instrument-switch-engraver.cc, lily/horizontal-bracket-engraver.cc, lily/ledger-line-engraver.cc;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - eight small engravers that annotate or decorate what other engravers made share a
//     file; none is larger than a screen.
//   - the derived_mark overrides on Parenthesis_engraver and Instrument_switch_engraver
//     protect SCM fields from the garbage collector; the port holds managed references and
//     has nothing to mark, so those methods have no analogue.

/// <summary>
/// Puts <c>fontSize</c> into the <c>font-size</c> grob property.
/// </summary>
public class FontSizeEngraver : Engraver
{
    private static readonly Symbol FontSizeContextSymbol = Symbol.Intern("fontSize");
    private static readonly Symbol FontSizeSymbol = Symbol.Intern("font-size");
    private static readonly Symbol FontInterfaceSymbol = Symbol.Intern("font-interface");

    private double _size;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public FontSizeEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Font_size_engraver";

    /// <summary>Reads this timestep's font size.</summary>
    public override void ProcessMusic()
        => _size = ToDouble(GetProperty(FontSizeContextSymbol), 0.0);

    /// <summary>Adds the context's font size to every grob made in this context.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!info.Grob.HasInterface(FontInterfaceSymbol))
        {
            return;
        }

        /*
          We only want to process a grob once.
        */
        if (_size == 0.0)
        {
            return;
        }

        if (!ReferenceEquals(info.OriginEngraver?.Context, Context))
        {
            return;
        }

        double fontSize = _size + ToDouble(info.Grob.GetProperty(FontSizeSymbol), 0);
        info.Grob.SetProperty(FontSizeSymbol, fontSize);
    }

    private static double ToDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "font-size")
            : fallback;
}

/// <summary>
/// Reads the <c>tweaks</c> property from the originating event, and sets properties.
/// </summary>
public class TweakEngraver : Engraver
{
    private static readonly Symbol TweaksSymbol = Symbol.Intern("tweaks");

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public TweakEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Tweak_engraver";

    /// <summary>Applies the tweaks the causing event carries.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        StreamEvent ev = info.EventCause;
        bool direct = ev != null;
        Symbol grobname = null;
        if (!direct)
        {
            ev = info.UltimateEventCause;
        }

        if (ev == null)
        {
            return;
        }

        // Each tweak conses an address and a value.
        // The address has one of the following forms:
        // symbol -> direct tweak
        // (grob . symbol) -> targeted tweak
        // (#t . symbol-path) -> direct nested tweak
        // (grob . symbol-path) -> targeted nested tweak
        for (object s = ev.GetProperty(TweaksSymbol); s is Pair sPair; s = sPair.Cdr)
        {
            if (!(sPair.Car is Pair entry))
            {
                continue;
            }

            if (entry.Car is Pair address)
            {
                if (address.Car is Symbol target)
                {
                    if (grobname == null)
                    {
                        grobname = Symbol.Intern(info.Grob.Name);
                    }

                    if (ReferenceEquals(target, grobname))
                    {
                        if (address.Cdr is Symbol property)
                        {
                            info.Grob.SetProperty(property, entry.Cdr);
                        }
                        else
                        {
                            NestedProperty.SetNestedProperty(
                                info.Grob, address.Cdr, entry.Cdr);
                        }
                    }
                }
                else if (direct)
                {
                    NestedProperty.SetNestedProperty(info.Grob, address.Cdr, entry.Cdr);
                }
            }
            else if (direct && entry.Car is Symbol directProperty)
            {
                info.Grob.SetProperty(directProperty, entry.Cdr);
            }
        }
    }
}

/// <summary>
/// Creates balloon texts.
/// </summary>
public class BalloonEngraver : Engraver
{
    private static readonly Symbol AnnotateOutputEventSymbol
        = Symbol.Intern("annotate-output-event");
    private static readonly Symbol ArticulationsSymbol = Symbol.Intern("articulations");
    private static readonly Symbol SymbolSymbol = Symbol.Intern("symbol");
    private static readonly Symbol BalloonTextSymbol = Symbol.Intern("BalloonText");

    private readonly List<StreamEvent> _events = new List<StreamEvent>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public BalloonEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Balloon_engraver";

    /// <summary>Starts listening for annotation events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(AnnotateOutputEventSymbol, ev => _events.Add(ev));
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Sticks a balloon onto every grob an annotation names.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        StreamEvent cause = info.EventCause;
        Engraver eng = info.OriginEngraver;
        object arts = cause != null ? cause.GetProperty(ArticulationsSymbol) : Nil.Instance;
        for (object s = arts; s is Pair pair; s = pair.Cdr)
        {
            if (pair.Car is StreamEvent e && e.IsInEventClass(AnnotateOutputEventSymbol))
            {
                eng.MakeSticky(BalloonTextSymbol, info.Grob, e);
            }
        }

        foreach (StreamEvent ev in _events)
        {
            if (ev.GetProperty(SymbolSymbol) is Symbol name
                && info.Grob.Name == name.Name)
            {
                eng.MakeSticky(BalloonTextSymbol, info.Grob, ev);
            }
        }
    }

    /// <summary>Drops this timestep's annotation events.</summary>
    public override void StopTranslationTimestep() => _events.Clear();
}

/// <summary>
/// Parenthesizes objects whose <c>parenthesize</c> property is <c>#t</c>.
/// </summary>
public class ParenthesisEngraver : Engraver
{
    private static readonly Symbol ParenthesizedSymbol = Symbol.Intern("parenthesized");
    private static readonly Symbol ParenthesisIdSymbol = Symbol.Intern("parenthesis-id");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol FontSizeSymbol = Symbol.Intern("font-size");
    private static readonly Symbol ParenthesesSymbol = Symbol.Intern("Parentheses");
    private static readonly Symbol AccidentalInterfaceSymbol
        = Symbol.Intern("accidental-interface");
    private static readonly Symbol TabNoteHeadInterface
        = Symbol.Intern("tab-note-head-interface");

    // When we see parenthesis-id set, we make a single Parentheses grob
    // for all grobs having the same value.  This alist maps IDs (symbols)
    // to Parentheses grobs.  It is reset after each time step.
    private object _idAlist = Nil.Instance;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public ParenthesisEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Parenthesis_engraver";

    /// <summary>Puts parentheses around every grob that asks for them.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        Grob g = info.Grob;
        if (SchemeUtilities.ToBool(g.GetProperty(ParenthesizedSymbol))

            // AccidentalCautionary has its own implementation
            // of parentheses.  It changes the stencil, which
            // is important for accidental placement, but won't
            // work with parenthesis friends.  TODO: find a nice
            // way to merge the two.
            && !g.HasInterface(AccidentalInterfaceSymbol)

            // Similar for TabNoteHead
            && !g.HasInterface(TabNoteHeadInterface))
        {
            object id = g.GetProperty(ParenthesisIdSymbol);
            bool mustAddToAlist = false;
            Grob paren = null;
            if (id is Symbol)
            {
                Pair maybeParen = SchemeUtilities.Assq(id, _idAlist);
                if (maybeParen != null)
                {
                    paren = maybeParen.Cdr as Grob;
                }
                else
                {
                    mustAddToAlist = true;
                }
            }

            if (paren == null)
            {
                Engraver eng = info.OriginEngraver;
                paren = eng.MakeSticky(ParenthesesSymbol, g, g);
            }

            if (mustAddToAlist)
            {
                // No need for scm_assq_set_x: we already know that the
                // id is not a key in the alist.
                _idAlist = new Pair(new Pair(id, paren), _idAlist);
            }

            PointerGroupInterface.AddGrob(paren, ElementsSymbol, g);
            double size = ToDouble(paren.GetProperty(FontSizeSymbol), 0.0)
                          + ToDouble(g.GetProperty(FontSizeSymbol), 0.0);
            paren.SetProperty(FontSizeSymbol, size);

            /*
              TODO?
              enlarge victim to allow for parentheses space?
            */
        }
    }

    /// <summary>Forgets this timestep's parenthesis identities.</summary>
    public override void StopTranslationTimestep() => _idAlist = Nil.Instance;

    private static double ToDouble(object value, double fallback)
        => SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, "font-size")
            : fallback;
}

/// <summary>
/// Creates a system start text for instrument or vocal names.
/// </summary>
public class InstrumentNameEngraver : Engraver
{
    private static readonly Symbol InstrumentNameSymbol = Symbol.Intern("instrumentName");
    private static readonly Symbol ShortInstrumentNameSymbol
        = Symbol.Intern("shortInstrumentName");
    private static readonly Symbol VocalNameSymbol = Symbol.Intern("vocalName");
    private static readonly Symbol ShortVocalNameSymbol = Symbol.Intern("shortVocalName");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol LongTextSymbol = Symbol.Intern("long-text");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol RootSystemSymbol = Symbol.Intern("rootSystem");
    private static readonly Symbol CurrentCommandColumnSymbol
        = Symbol.Intern("currentCommandColumn");
    private static readonly Symbol HaraKiriGroupSpannerInterface
        = Symbol.Intern("hara-kiri-group-spanner-interface");

    private Spanner _textSpanner;
    private object _longText = Nil.Instance;
    private object _shortText = Nil.Instance;
    private List<Grob> _axisGroups = new List<Grob>();
    private readonly List<Grob> _backupAxisGroups = new List<Grob>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public InstrumentNameEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Instrument_name_engraver";

    /// <summary>Starts a name spanner when the name changes.</summary>
    public override void ProcessMusic() => ConsiderStartSpanner();

    /// <summary>Collects the staves the name will be centred against.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!info.Grob.HasInterface(HaraKiriGroupSpannerInterface))
        {
            return;
        }

        if (PageLayoutSpacing.IsSpaceable(info.Grob))
        {
            _axisGroups.Add(info.Grob);
        }
        else
        {
            // By default, don't include non-spaceable staves in the
            // support of an instrument name.  However, if the only staves
            // are non-spaceable, we'll fall back to using them.
            _backupAxisGroups.Add(info.Grob);
        }
    }

    /// <summary>Closes the name spanner.</summary>
    public override void FinalizeTranslation()
    {
        if (_textSpanner != null)
        {
            StopSpanner();
        }
    }

    private void ConsiderStartSpanner()
    {
        object longText = GetProperty(InstrumentNameSymbol);
        object shortText = GetProperty(ShortInstrumentNameSymbol);

        if (!(TextInterface.IsMarkup(longText) || TextInterface.IsMarkup(shortText)))
        {
            longText = GetProperty(VocalNameSymbol);
            shortText = GetProperty(ShortVocalNameSymbol);
        }

        if ((TextInterface.IsMarkup(longText) || TextInterface.IsMarkup(shortText))
            && (_textSpanner == null
                || !ReferenceEquals(_shortText, shortText)
                || !ReferenceEquals(_longText, longText)))
        {
            if (_textSpanner != null)
            {
                StopSpanner();
            }

            _shortText = shortText;
            _longText = longText;
            StartSpanner();
        }
    }

    private void StartSpanner()
    {
        _textSpanner = MakeSpanner("InstrumentName", Nil.Instance);
        _textSpanner.SetBound(
            Direction.Negative, GetProperty(CurrentCommandColumnSymbol) as Grob);
        _textSpanner.SetProperty(TextSymbol, _shortText);
        _textSpanner.SetProperty(LongTextSymbol, _longText);

        /*
          UGH, should handle this in Score_engraver.
        */
        if (GetProperty(RootSystemSymbol) is Grob system)
        {
            AxisGroupInterface.AddElement(system, _textSpanner);
        }
        else
        {
            _textSpanner.ProgrammingError("cannot find root system");
        }
    }

    private void StopSpanner()
    {
        if (_axisGroups.Count == 0)
        {
            _axisGroups = new List<Grob>(_backupAxisGroups);
        }

        for (int i = 0; i < _axisGroups.Count; i++)
        {
            PointerGroupInterface.AddGrob(_textSpanner, ElementsSymbol, _axisGroups[i]);
        }

        _textSpanner.SetBound(
            Direction.Positive, GetProperty(CurrentCommandColumnSymbol) as Grob);
        PointerGroupInterface.SetOrdered(_textSpanner, ElementsSymbol, false);
        _textSpanner = null;
    }
}

/*
  TODO: should use an event.
 */

/// <summary>
/// Creates a cue text for taking instrument. This engraver is deprecated.
/// </summary>
public class InstrumentSwitchEngraver : Engraver
{
    private static readonly Symbol InstrumentCueNameSymbol
        = Symbol.Intern("instrumentCueName");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");

    private Grob _text;
    private object _cueName = Nil.Instance;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public InstrumentSwitchEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Instrument_switch_engraver";

    /// <summary>Makes the cue text when the cue name changes.</summary>
    public override void ProcessMusic()
    {
        object cueText = GetProperty(InstrumentCueNameSymbol);
        if (!ReferenceEquals(_cueName, cueText))
        {
            if (TextInterface.IsMarkup(cueText))
            {
                _text = MakeItem("InstrumentSwitch", Nil.Instance);
                _text.SetProperty(TextSymbol, cueText);
            }

            _cueName = cueText;
        }
    }

    // UPSTREAM DEAD CODE, REPRODUCED DEAD. Upstream declares and defines
    // `stop_translation_time_step` — with an extra underscore — where every other one of
    // the 91 engravers spells it `stop_translation_timestep`. That is not the name the
    // translator framework dispatches, so upstream NEVER calls this and `text_` is never
    // cleared. Overriding the real hook here would be a silent divergence, so the method
    // stays unreachable exactly as upstream's is. It cannot matter either way: `_text` is
    // only ever written, never read. Recorded in PORT-COVERAGE.
    private void StopTranslationTimeStep() => _text = null;
}

/// <summary>
/// Creates horizontal brackets over notes for musical analysis purposes.
/// </summary>
public class HorizontalBracketEngraver : Engraver
{
    private static readonly Symbol NoteGroupingEventSymbol
        = Symbol.Intern("note-grouping-event");
    private static readonly Symbol SpanDirectionSymbol = Symbol.Intern("span-direction");
    private static readonly Symbol ColumnsSymbol = Symbol.Intern("columns");
    private static readonly Symbol BracketTextSymbol = Symbol.Intern("bracket-text");
    private static readonly Symbol BracketSymbol = Symbol.Intern("bracket");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");

    private readonly List<Spanner> _bracketStack = new List<Spanner>();
    private readonly List<Spanner> _textStack = new List<Spanner>();
    private readonly List<StreamEvent> _events = new List<StreamEvent>();
    private int _popCount;
    private int _pushCount;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public HorizontalBracketEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Horizontal_bracket_engraver";

    /// <summary>Starts listening for note-grouping events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(NoteGroupingEventSymbol, ListenNoteGrouping);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Opens a bracket for each group starting this timestep.</summary>
    public override void ProcessMusic()
    {
        for (int k = 0; k < _pushCount; k++)
        {
            Spanner sp = MakeSpanner("HorizontalBracket", _events[k]);
            Spanner hbt = MakeSpanner("HorizontalBracketText", sp);
            sp.SetObject(BracketTextSymbol, hbt);
            SidePositionInterface.AddSupport(hbt, sp);
            hbt.XParent = sp;
            hbt.YParent = sp;
            hbt.SetObject(BracketSymbol, sp);

            for (int i = 0; i < _bracketStack.Count; i++)
            {
                /* sp is the smallest, it should be added to the bigger brackets.  */
                SidePositionInterface.AddSupport(_bracketStack[i], sp);
                SidePositionInterface.AddSupport(_bracketStack[i], hbt);
            }

            _bracketStack.Add(sp);
            _textStack.Add(hbt);
        }
    }

    /// <summary>Collects the note columns every open bracket encompasses.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!info.Grob.HasInterface(NoteColumnInterface) || !(info.Grob is Item column))
        {
            return;
        }

        for (int i = 0; i < _bracketStack.Count; i++)
        {
            SidePositionInterface.AddSupport(_bracketStack[i], column);
            PointerGroupInterface.AddGrob(_bracketStack[i], ColumnsSymbol, column);
            Spanner.AddBoundItem(_bracketStack[i], column);
            Spanner.AddBoundItem(_textStack[i], column);
        }
    }

    /// <summary>Closes the brackets this timestep ends.</summary>
    public override void StopTranslationTimestep()
    {
        for (int i = _popCount; i-- > 0;)
        {
            if (_bracketStack.Count > 0)
            {
                _bracketStack.RemoveAt(_bracketStack.Count - 1);
            }

            if (_textStack.Count > 0)
            {
                _textStack.RemoveAt(_textStack.Count - 1);
            }
        }

        _popCount = 0;
        _pushCount = 0;
        _events.Clear();
    }

    private void ListenNoteGrouping(StreamEvent ev)
    {
        Direction d = DirectionalElementInterface.FromScheme(
            ev.GetProperty(SpanDirectionSymbol), Direction.Center);
        if (d == Direction.Positive)
        {
            _popCount++;
            if (_popCount > _bracketStack.Count)
            {
                Epg8Support.EventWarning(ev, "do not have that many brackets");
            }
        }
        else
        {
            _pushCount++;
            _events.Add(ev);
        }

        if (_popCount != 0 && _pushCount != 0)
        {
            Epg8Support.EventWarning(ev, "conflicting note group events");
        }
    }
}

/// <summary>
/// Creates the spanner to draw ledger lines, and notices objects that need ledger lines.
/// </summary>
public class LedgerLineEngraver : Engraver
{
    private static readonly Symbol NoteHeadsSymbol = Symbol.Intern("note-heads");
    private static readonly Symbol NoLedgersSymbol = Symbol.Intern("no-ledgers");
    private static readonly Symbol CurrentCommandColumnSymbol
        = Symbol.Intern("currentCommandColumn");
    private static readonly Symbol LedgeredInterface = Symbol.Intern("ledgered-interface");
    private static readonly Symbol StaffSymbolInterface = Symbol.Intern("staff-symbol-interface");

    private Spanner _span;
    private readonly List<Grob> _ledgeredGrobs = new List<Grob>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public LedgerLineEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Ledger_line_engraver";

    /// <summary>Opens the ledger spanner before the first note can miss it.</summary>
    public override void ProcessMusic()
    {
        /*
          Need to do this, otherwise the first note might miss ledgers.
        */
        if (_span == null)
        {
            StartSpanner();
        }
    }

    /// <summary>Collects the grobs that may need ledgers, and follows the staff.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        // Upstream's registration order: ledgered, staff_symbol.
        if (info.Grob.HasInterface(LedgeredInterface))
        {
            _ledgeredGrobs.Add(info.Grob);
        }

        if (info.Grob.HasInterface(StaffSymbolInterface) && info.Grob is Spanner staff)
        {
            if (_span == null
                || !ReferenceEquals(
                    _span.GetBound(Direction.Negative), staff.GetBound(Direction.Negative)))
            {
                StopSpanner();
                StartSpanner();
            }
        }
    }

    /// <summary>Hands this timestep's ledgered grobs to the spanner.</summary>
    public override void StopTranslationTimestep()
    {
        if (_span != null)
        {
            for (int i = 0; i < _ledgeredGrobs.Count; i++)
            {
                if (!SchemeUtilities.ToBool(_ledgeredGrobs[i].GetProperty(NoLedgersSymbol)))
                {
                    PointerGroupInterface.AddGrob(
                        _span, NoteHeadsSymbol, _ledgeredGrobs[i]);
                }
            }
        }

        _ledgeredGrobs.Clear();
    }

    /// <summary>Closes the ledger spanner.</summary>
    public override void FinalizeTranslation() => StopSpanner();

    private void StartSpanner()
    {
        _span = MakeSpanner("LedgerLineSpanner", Nil.Instance);
        _span.SetBound(Direction.Negative, GetProperty(CurrentCommandColumnSymbol) as Grob);
    }

    private void StopSpanner()
    {
        if (_span != null)
        {
            _span.SetBound(
                Direction.Positive, GetProperty(CurrentCommandColumnSymbol) as Grob);
            _span = null;
        }
    }
}
