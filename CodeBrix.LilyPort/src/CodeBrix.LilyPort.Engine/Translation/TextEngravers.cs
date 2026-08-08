/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1998--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
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
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/text-engraver.cc, lily/text-spanner-engraver.cc, lily/ottava-engraver.cc;

// Modified by Jeremy Ellis on 2026-08-08 as part of the CodeBrix port:
//   - the three text-producing engravers share a file.
//   - upstream's derived_mark on Ottava_spanner_engraver protects `ottavation_` from the
//     garbage collector; the port holds a managed reference, so there is nothing to mark
//     and the method has no analogue.

/// <summary>
/// Creates text scripts — typesets directions that are plain text.
/// </summary>
public class TextEngraver : Engraver
{
    private static readonly Symbol TextScriptEventSymbol = Symbol.Intern("text-script-event");
    private static readonly Symbol ScriptPrioritySymbol = Symbol.Intern("script-priority");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol NoteHeadsSymbol = Symbol.Intern("note-heads");
    private static readonly Symbol RestSymbol = Symbol.Intern("rest");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");

    private readonly List<StreamEvent> _events = new List<StreamEvent>();
    private readonly List<Grob> _scripts = new List<Grob>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public TextEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Text_engraver";

    /// <summary>Starts listening for text-script events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(TextScriptEventSymbol, ev => _events.Add(ev));
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Makes one text script per event collected this timestep.</summary>
    public override void ProcessMusic()
    {
        for (int i = 0; i < _events.Count; i++)
        {
            StreamEvent ev = _events[i];

            Item script = MakeItem("TextScript", ev);
            _scripts.Add(script);

            /* see script-engraver.cc */
            object priority = script.GetProperty(ScriptPrioritySymbol);
            long value = SchemeConvert.IsNumber(priority)
                ? SchemeConvert.ToLong(priority, "script-priority")
                : 200; // TODO: Explain magic.
            script.SetProperty(ScriptPrioritySymbol, value + i);

            Direction dir = DirectionalElementInterface.FromScheme(
                ev.GetProperty(DirectionSymbol), Direction.Center);
            if (dir != Direction.Center)
            {
                DirectionalElementInterface.SetGrobDirection(script, dir);
            }

            script.SetProperty(TextSymbol, ev.GetProperty(TextSymbol));
        }
    }

    /// <summary>Parents this timestep's scripts onto the note column.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!info.Grob.HasInterface(NoteColumnInterface) || !(info.Grob is Item column))
        {
            return;
        }

        // Make note column (or rest, if there are no heads) the parent of the script.
        IReadOnlyList<Grob> heads
            = PointerGroupInterface.ExtractGrobSet(column, NoteHeadsSymbol);
        Grob xParent = heads.Count > 0 ? column : column.GetObject(RestSymbol) as Grob;

        for (int i = 0; i < _scripts.Count; i++)
        {
            Grob el = _scripts[i];

            if (el != null && el.XParent == null && xParent != null)
            {
                el.XParent = xParent;
            }
        }
    }

    /// <summary>Drops this timestep's events and scripts.</summary>
    public override void StopTranslationTimestep()
    {
        _events.Clear();
        _scripts.Clear();
    }
}

/// <summary>
/// Creates a text spanner from an event.
/// </summary>
public class TextSpannerEngraver : Engraver
{
    private static readonly Symbol TextSpanEventSymbol = Symbol.Intern("text-span-event");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol NoteColumnsSymbol = Symbol.Intern("note-columns");
    private static readonly Symbol CurrentMusicalColumnSymbol
        = Symbol.Intern("currentMusicalColumn");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");

    private readonly UniqueSpanEventListener _textSpanListener = new UniqueSpanEventListener();
    private Spanner _span;
    private Spanner _finished;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public TextSpannerEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Text_spanner_engraver";

    /// <summary>Starts listening for text-span events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(TextSpanEventSymbol, _textSpanListener.Listen);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Ends the spanner this timestep stops, then starts the one it starts.</summary>
    public override void ProcessMusic()
    {
        if (_textSpanListener.Stop is StreamEvent ender)
        {
            if (_span == null)
            {
                Epg8Support.EventWarning(ender, "cannot find start of text spanner");
            }
            else
            {
                _finished = _span;
                AnnounceEndGrob(_finished, Nil.Instance);
                _span = null;
            }
        }

        if (_textSpanListener.Start is StreamEvent starter)
        {
            if (_span != null)
            {
                Epg8Support.EventWarning(starter, "already have a text spanner");
                _span.Warning("text spanner was started here");
            }
            else
            {
                _span = MakeSpanner("TextSpanner", starter);

                object dScm = starter.GetProperty(DirectionSymbol);
                if (DirectionalElementInterface.FromScheme(dScm, Direction.Center)
                    != Direction.Center)
                {
                    _span.SetProperty(DirectionSymbol, dScm);
                }

                SidePositionInterface.SetAxis(_span, Axis.Y);
            }
        }
    }

    /// <summary>Collects the note columns the spanner runs over.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!info.Grob.HasInterface(NoteColumnInterface) || !(info.Grob is Item column))
        {
            return;
        }

        if (_span != null)
        {
            PointerGroupInterface.AddGrob(_span, NoteColumnsSymbol, column);
            if (_span.GetBound(Direction.Negative) == null)
            {
                Spanner.AddBoundItem(_span, column);
            }
        }
        else if (_finished != null)
        {
            PointerGroupInterface.AddGrob(_finished, NoteColumnsSymbol, column);
            if (_finished.GetBound(Direction.Positive) == null)
            {
                Spanner.AddBoundItem(_finished, column);
            }
        }
    }

    /// <summary>Binds the open spanner's left end and closes the finished one.</summary>
    public override void StopTranslationTimestep()
    {
        if (_span != null && _span.GetBound(Direction.Negative) == null)
        {
            _span.SetBound(Direction.Negative, GetProperty(CurrentMusicalColumnSymbol) as Grob);
        }

        TypesetAll();
        _textSpanListener.Reset();
    }

    /// <summary>Warns about a spanner that never ended, and kills it.</summary>
    public override void FinalizeTranslation()
    {
        TypesetAll();
        if (_span != null)
        {
            _span.Warning("unterminated text spanner");
            _span.Suicide();
            _span = null;
        }
    }

    private void TypesetAll()
    {
        if (_finished != null)
        {
            if (_finished.GetBound(Direction.Positive) == null)
            {
                _finished.SetBound(
                    Direction.Positive, GetProperty(CurrentMusicalColumnSymbol) as Grob);
            }

            _finished = null;
        }
    }
}

/// <summary>
/// Creates a text spanner when the ottavation property changes.
/// </summary>
public class OttavaSpannerEngraver : Engraver
{
    private static readonly Symbol OttavaEventSymbol = Symbol.Intern("ottava-event");
    private static readonly Symbol OttavaNumberSymbol = Symbol.Intern("ottava-number");
    private static readonly Symbol MiddleCOffsetSymbol = Symbol.Intern("middleCOffset");
    private static readonly Symbol OttavaStartNowSymbol = Symbol.Intern("ottavaStartNow");
    private static readonly Symbol OttavationSymbol = Symbol.Intern("ottavation");
    private static readonly Symbol OttavationMarkupsSymbol = Symbol.Intern("ottavationMarkups");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol CurrentMusicalColumnSymbol
        = Symbol.Intern("currentMusicalColumn");
    private static readonly Symbol CurrentCommandColumnSymbol
        = Symbol.Intern("currentCommandColumn");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");
    private static readonly Symbol PercentRepeatInterface
        = Symbol.Intern("percent-repeat-interface");
    private static readonly Symbol TrillSpannerInterface = Symbol.Intern("trill-spanner-interface");

    private StreamEvent _ottavaEv;
    private object _ottavation = Nil.Instance;
    private Item _noteCol;
    private Item _lastNoteCol;
    private bool _ackedTrill;
    private Spanner _span;
    private Spanner _finished;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public OttavaSpannerEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Ottava_spanner_engraver";

    /// <summary>Starts listening for ottava events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(OttavaEventSymbol, ListenOttava);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Clears the start flag at the top of each timestep.</summary>
    public override void StartTranslationTimestep()
        => Context.SetProperty(OttavaStartNowSymbol, Nil.Instance);

    /// <summary>Ends the running bracket and starts a new one when the octave changes.</summary>
    public override void ProcessMusic()
    {
        if (_ottavaEv != null)
        {
            _finished = _span;
            _span = null;
            if (!IsZero(_ottavation))
            {
                Context.SetProperty(OttavaStartNowSymbol, true);
                CreateSpanner();
            }
        }
    }

    /// <summary>Collects the note columns, percent repeats and trills that bear on the bracket.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        // Upstream's registration order: note_column, percent_repeat, trill_spanner.
        if (info.Grob.HasInterface(NoteColumnInterface) && info.Grob is Item column)
        {
            if (_span != null)
            {
                SidePositionInterface.AddSupport(_span, column);
                if (_span.GetBound(Direction.Negative) == null)
                {
                    _span.SetBound(Direction.Negative, column);
                }

                _noteCol = column;
            }
        }

        if (info.Grob.HasInterface(PercentRepeatInterface))
        {
            // A percent repeat disqualifies a preceding note from use as the right bound
            // of the ottava bracket.
            _lastNoteCol = null;
        }

        if (info.Grob.HasInterface(TrillSpannerInterface) && info.Grob is Spanner)
        {
            _ackedTrill = true;
        }
    }

    /// <summary>Binds the bracket's ends and closes the finished one.</summary>
    public override void StopTranslationTimestep()
    {
        // The head of a trilled note that is notated with a wavy line is not eligible
        // for use as the right bound of an ottava bracket.
        if (_ackedTrill)
        {
            _noteCol = null;
        }

        if (_span != null && _span.GetBound(Direction.Negative) == null)
        {
            _span.SetBound(Direction.Negative, GetProperty(CurrentMusicalColumnSymbol) as Grob);
        }

        TypesetAll();
        _ackedTrill = false;
        _ottavaEv = null;
        if (_noteCol != null)
        {
            _lastNoteCol = _noteCol;
            _noteCol = null;
        }
    }

    /// <summary>Closes whatever is still open.</summary>
    public override void FinalizeTranslation()
    {
        TypesetAll();
        if (_span != null)
        {
            _finished = _span;
        }

        TypesetAll();
    }

    private void ListenOttava(StreamEvent ev)
    {
        _ottavation = ev.GetProperty(OttavaNumberSymbol);
        long offset = -7 * ToLong(_ottavation);
        Context.SetProperty(MiddleCOffsetSymbol, offset);
        Pitch.SetMiddleC(Context);
        _ottavaEv = ev;
    }

    private void CreateSpanner()
    {
        _span = MakeSpanner("OttavaBracket", _ottavaEv);

        // Respect user tweaks.
        if (_span.GetPropertyData(TextSymbol) is Nil)
        {
            object ott = GetProperty(OttavationSymbol);
            if (ott is Nil)
            {
                object markups = GetProperty(OttavationMarkupsSymbol);
                ott = SchemeUtilities.LyAssocGet(_ottavation, markups, Nil.Instance);
                if (ott is Nil)
                {
                    Warn.Warning(
                        "Could not find ottavation markup for " + ToLong(_ottavation)
                        + " octaves up.");
                    ott = string.Empty;
                }
            }

            _span.SetProperty(TextSymbol, ott);
        }

        if (_span.GetPropertyData(DirectionSymbol) is Nil)
        {
            long offset = ToLong(GetProperty(MiddleCOffsetSymbol));
            Direction d = offset > 0 ? Direction.Negative : Direction.Positive;
            _span.SetProperty(DirectionSymbol, (long)(int)d);
        }
    }

    private void TypesetAll()
    {
        if (_finished != null)
        {
            if (_finished.GetBound(Direction.Negative) != null)
            {
                if (_finished.GetBound(Direction.Positive) == null)
                {
                    // Usually, end the bracket just after the last note head.
                    Grob col = _lastNoteCol ?? GetProperty(CurrentCommandColumnSymbol) as Item;
                    _finished.SetBound(Direction.Positive, col);
                }
            }
            else
            {
                _finished.Suicide();
            }

            _finished = null;
            _lastNoteCol = null;
        }
    }

    private static bool IsZero(object value) => ToLong(value) == 0;

    private static long ToLong(object value)
        => SchemeConvert.IsNumber(value) ? SchemeConvert.ToLong(value, "ottava-number") : 0;
}
