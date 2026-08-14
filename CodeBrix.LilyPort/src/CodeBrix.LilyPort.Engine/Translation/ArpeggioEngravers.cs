/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2000--2026 Jan Nieuwenhuizen <janneke@gnu.org>

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
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/arpeggio-engraver.cc, lily/span-arpeggio-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - the two engravers share a file because they are the two halves of one mechanism:
//     the first makes the per-voice grob and the second spans the ones it made.
//   - upstream's Arpeggio_type enum is a private nested enum here; its ORDER is not
//     load-bearing, but its DEFAULT is — a \non-arpeggiato listener leaves the type at
//     NON_ARPEGGIATED, which is the zero value upstream initialises the field to.
//   - upstream's three separate acknowledgers become interface tests inside the single
//     AcknowledgeGrob the port's Engraver base offers, as every ported engraver does.

/// <summary>Creates arpeggiato and non-arpeggiato symbols.</summary>
public sealed class ArpeggioEngraver : Engraver
{
    private static readonly Symbol StemsSymbol = Symbol.Intern("stems");
    private static readonly Symbol ArpeggioEventSymbol = Symbol.Intern("arpeggio-event");
    private static readonly Symbol ChordSlurEventSymbol = Symbol.Intern("chord-slur-event");
    private static readonly Symbol NonArpeggiatoEventSymbol
        = Symbol.Intern("non-arpeggiato-event");
    private static readonly Symbol StemInterface = Symbol.Intern("stem-interface");
    private static readonly Symbol RhythmicHeadInterface = Symbol.Intern("rhythmic-head-interface");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");

    private Item _arpeggio;
    private StreamEvent _arpeggioEvent;
    private ArpeggioType _arpeggioType = ArpeggioType.NonArpeggiated;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public ArpeggioEngraver(Context context)
        : base(context)
    {
    }

    private enum ArpeggioType
    {
        NonArpeggiated = 0,
        Slurred,
        Arpeggiated,
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Arpeggio_engraver";

    /// <summary>Starts listening for the three arpeggio events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(ArpeggioEventSymbol, ListenArpeggio);
        ListenTo(ChordSlurEventSymbol, ListenChordSlur);
        ListenTo(NonArpeggiatoEventSymbol, ListenNonArpeggiato);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Collects stems, note heads and note columns for the arpeggio.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob.HasInterface(StemInterface))
        {
            AcknowledgeStem(info);
        }

        if (info.Grob.HasInterface(RhythmicHeadInterface))
        {
            AcknowledgeRhythmicHead(info);
        }

        if (info.Grob.HasInterface(NoteColumnInterface))
        {
            AcknowledgeNoteColumn(info);
        }
    }

    /// <summary>Makes the arpeggio grob the heard event asks for.</summary>
    public override void ProcessMusic()
    {
        if (_arpeggioEvent != null)
        {
            string itemName;
            switch (_arpeggioType)
            {
                case ArpeggioType.NonArpeggiated:
                    itemName = "ChordBracket";
                    break;
                case ArpeggioType.Slurred:
                    itemName = "ChordSlur";
                    break;
                default:
                    itemName = "Arpeggio";
                    break;
            }

            _arpeggio = MakeItem(itemName, _arpeggioEvent);
        }
    }

    /// <summary>Forgets this timestep's arpeggio and event.</summary>
    public override void StopTranslationTimestep()
    {
        _arpeggio = null;
        _arpeggioEvent = null;
    }

    private void AcknowledgeStem(GrobInfo info)
    {
        if (_arpeggio != null)
        {
            if (_arpeggio.YParent == null)
            {
                _arpeggio.YParent = info.Grob;
            }

            PointerGroupInterface.AddGrob(_arpeggio, StemsSymbol, info.Grob);
        }
    }

    private void AcknowledgeRhythmicHead(GrobInfo info)
    {
        if (_arpeggio != null)
        {
            /*
              We can't catch local key items (accidentals) from Voice context,
              see Local_key_engraver
            */
            SidePositionInterface.AddSupport(_arpeggio, info.Grob);
        }
    }

    private void AcknowledgeNoteColumn(GrobInfo info)
    {
        // Grob_info_t<Item>: upstream's acknowledger takes an Item, so a spanner
        // announcing this interface is not offered to it at all.
        if (_arpeggio != null && info.Grob is Item item)
        {
            SeparationItem.AddConditionalItem(item, _arpeggio);
        }
    }

    private void ListenArpeggio(StreamEvent ev)
    {
        if (StreamEvent.AssignEventOnce(ref _arpeggioEvent, ev))
        {
            _arpeggioType = ArpeggioType.Arpeggiated;
        }
    }

    private void ListenChordSlur(StreamEvent ev)
    {
        if (StreamEvent.AssignEventOnce(ref _arpeggioEvent, ev))
        {
            _arpeggioType = ArpeggioType.Slurred;
        }
    }

    private void ListenNonArpeggiato(StreamEvent ev)
    {
        if (StreamEvent.AssignEventOnce(ref _arpeggioEvent, ev))
        {
            _arpeggioType = ArpeggioType.NonArpeggiated;
        }
    }
}

/// <summary>
/// Makes arpeggios, non-arpeggiato brackets, and vertical slurs spanning multiple staves:
/// catches the per-voice grobs and spans one of its own over them when it finds more
/// than one.
/// </summary>
public sealed class SpanArpeggioEngraver : Engraver
{
    private static readonly Symbol StemsSymbol = Symbol.Intern("stems");
    private static readonly Symbol SideSupportElementsSymbol
        = Symbol.Intern("side-support-elements");
    private static readonly Symbol SurrogateSymbol
        = Symbol.Intern("vertically-spanning-surrogate");
    private static readonly Symbol TransparentSymbol = Symbol.Intern("transparent");
    private static readonly Symbol XOffsetSymbol = Symbol.Intern("X-offset");
    private static readonly Symbol ArpeggioInterface = Symbol.Intern("arpeggio-interface");
    private static readonly Symbol ChordBracketInterface = Symbol.Intern("chord-bracket-interface");
    private static readonly Symbol ChordSlurInterface = Symbol.Intern("chord-slur-interface");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");

    private readonly ChordOnsetInfo _arpeggio
        = new ChordOnsetInfo("Arpeggio", "connectArpeggios");
    private readonly ChordOnsetInfo _bracket
        = new ChordOnsetInfo("ChordBracket", "connectChordBrackets");
    private readonly ChordOnsetInfo _slur
        = new ChordOnsetInfo("ChordSlur", "connectChordSlurs");
    private readonly List<Item> _noteColumns = new List<Item>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public SpanArpeggioEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Span_arpeggio_engraver";

    /// <summary>Collects the per-voice arpeggio grobs and the note columns.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        // Every one of upstream's four acknowledgers is Grob_info_t<Item>.
        if (!(info.Grob is Item item))
        {
            return;
        }

        if (item.HasInterface(ArpeggioInterface))
        {
            _arpeggio.Items.Add(item);
        }

        if (item.HasInterface(ChordBracketInterface))
        {
            _bracket.Items.Add(item);
        }

        if (item.HasInterface(ChordSlurInterface))
        {
            _slur.Items.Add(item);
        }

        if (item.HasInterface(NoteColumnInterface))
        {
            _noteColumns.Add(item);
        }
    }

    /// <summary>Makes the spanning grob once more than one child has been caught.</summary>
    public override void ProcessAcknowledged()
    {
        foreach (ChordOnsetInfo info in new[] { _arpeggio, _bracket, _slur })
        {
            /*
              connectArpeggios is slightly brusque; we should really read a grob
              property of the caught non-span arpeggios. That way, we can have

              both non-connected and connected arps in one pianostaff.

            */
            if (info.SpanItem == null && info.Items.Count > 1
                && SchemeUtilities.ToBool(GetProperty(info.ConnectPropertyName)))
            {
                info.SpanItem = MakeItem(info.ItemName, Nil.Instance);
            }

            if (info.SpanItem != null)
            {
                foreach (Item col in _noteColumns)
                {
                    SeparationItem.AddConditionalItem(col, info.SpanItem);
                }

                _noteColumns.Clear();
            }
        }
    }

    /// <summary>Adopts the children's supports and hides them behind the spanning grob.</summary>
    public override void StopTranslationTimestep()
    {
        foreach (ChordOnsetInfo info in new[] { _arpeggio, _bracket, _slur })
        {
            if (info.SpanItem != null)
            {
                /*
                  we do this very late, to make sure we also catch `extra'
                  side-pos support like accidentals.
                */
                foreach (Item item in info.Items)
                {
                    IReadOnlyList<Grob> stems
                        = PointerGroupInterface.ExtractGrobSet(item, StemsSymbol);
                    foreach (Grob stem in stems)
                    {
                        PointerGroupInterface.AddGrob(info.SpanItem, StemsSymbol, stem);
                    }

                    IReadOnlyList<Grob> sses
                        = PointerGroupInterface.ExtractGrobSet(item, SideSupportElementsSymbol);
                    foreach (Grob sse in sses)
                    {
                        PointerGroupInterface.AddGrob(
                            info.SpanItem, SideSupportElementsSymbol, sse);
                    }

                    /*
                      we can't kill the children, since we don't want to the
                      previous note to bump into the span arpeggio; so we make
                      it transparent.
                    */
                    item.SetObject(SurrogateSymbol, info.SpanItem);
                    item.SetProperty(TransparentSymbol, true);

                    /*
                      to avoid collisions due to different horizontal spacings
                      of children make all children align horizontally to the
                      span arpeggio
                    */
                    item.XParent = info.SpanItem;
                    item.SetProperty(XOffsetSymbol, 0.0);
                }

                info.SpanItem.YParent = info.Items[0].YParent;
                info.SpanItem = null;
            }
        }

        _arpeggio.Items.Clear();
        _bracket.Items.Clear();
        _slur.Items.Clear();
        _noteColumns.Clear();
    }

    private sealed class ChordOnsetInfo
    {
        public ChordOnsetInfo(string itemName, string connectPropertyName)
        {
            ItemName = itemName;
            ConnectPropertyName = Symbol.Intern(connectPropertyName);
        }

        public string ItemName { get; }

        public Symbol ConnectPropertyName { get; }

        public List<Item> Items { get; } = new List<Item>();

        public Item SpanItem { get; set; }
    }
}
