/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2006--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/figured-bass-engraver.cc, lily/figured-bass-position-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - Figure_group is a CLASS here, not a struct. Upstream's is a value type held in a
//     std::vector, and every use in the file mutates a group IN PLACE through a reference
//     (`Figure_group &group = groups_[i];`). A C# struct in a List<T> would hand back a
//     COPY and silently drop those mutations, which is the one translation error this
//     file makes easy.
//   - derived_mark() is not carried: it exists only to keep the six SCM members alive
//     across a Guile garbage collection, which managed fields do not need.
//   - the two engravers share a file because the position engraver exists solely to place
//     the alignment the figure engraver makes, and reads no other grob of its own.

/// <summary>Makes figured bass numbers.</summary>
public sealed class FiguredBassEngraver : Engraver
{
    private static readonly Symbol FigureSymbol = Symbol.Intern("figure");
    private static readonly Symbol AlterationSymbol = Symbol.Intern("alteration");
    private static readonly Symbol AugmentedSymbol = Symbol.Intern("augmented");
    private static readonly Symbol DiminishedSymbol = Symbol.Intern("diminished");
    private static readonly Symbol AugmentedSlashSymbol = Symbol.Intern("augmented-slash");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol FiguresSymbol = Symbol.Intern("figures");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol TransparentSymbol = Symbol.Intern("transparent");
    private static readonly Symbol ImplicitSymbol = Symbol.Intern("implicit");
    private static readonly Symbol NoContinuationSymbol = Symbol.Intern("no-continuation");
    private static readonly Symbol BracketStartSymbol = Symbol.Intern("bracket-start");
    private static readonly Symbol BracketStopSymbol = Symbol.Intern("bracket-stop");
    private static readonly Symbol UseBassFigureExtendersSymbol
        = Symbol.Intern("useBassFigureExtenders");
    private static readonly Symbol IgnoreFiguredBassRestSymbol
        = Symbol.Intern("ignoreFiguredBassRest");
    private static readonly Symbol FiguredBassCenterContinuationsSymbol
        = Symbol.Intern("figuredBassCenterContinuations");
    private static readonly Symbol FiguredBassFormatterSymbol
        = Symbol.Intern("figuredBassFormatter");
    private static readonly Symbol ImplicitBassFiguresSymbol
        = Symbol.Intern("implicitBassFigures");
    private static readonly Symbol CurrentMusicalColumnSymbol
        = Symbol.Intern("currentMusicalColumn");
    private static readonly Symbol BassFigureEventSymbol = Symbol.Intern("bass-figure-event");
    private static readonly Symbol RestEventSymbol = Symbol.Intern("rest-event");

    private readonly List<FigureGroup> _groups = new List<FigureGroup>();
    private readonly List<StreamEvent> _newEvents = new List<StreamEvent>();
    private readonly BooleanEventListener _restListener = new BooleanEventListener();

    private Spanner _alignment;
    private bool _continuation;
    private bool _newEventFound;
    private Moment _stopMoment = Moment.Zero;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public FiguredBassEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Figured_bass_engraver";

    /// <summary>Starts listening for bass figures and rests.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(RestEventSymbol, _restListener.Listen);
        ListenTo(BassFigureEventSymbol, ListenBassFigure);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Forgets the previous timestep, unless a figure is still sounding.</summary>
    public override void StartTranslationTimestep()
    {
        if (NowMoment.MainPart < _stopMoment.MainPart
            || NowMoment.GracePart < Rational.Zero)
        {
            return;
        }

        _restListener.Reset();
        _newEvents.Clear();
        for (int i = 0; i < _groups.Count; i++)
        {
            _groups[i].CurrentEvent = null;
        }

        _continuation = false;
    }

    /// <summary>Builds this timestep's figures, continuations and brackets.</summary>
    public override void ProcessMusic()
    {
        bool useExtenders = SchemeUtilities.ToBool(GetProperty(UseBassFigureExtendersSymbol));
        if (_alignment != null && !useExtenders
            && !(NowMoment.MainPart < _stopMoment.MainPart
                 || NowMoment.GracePart < Rational.Zero))
        {
            ClearSpanners();
        }

        // If we have a rest, or we have no new or continued events, clear all spanners
        if ((!_continuation && _newEvents.Count == 0)
            || (_restListener.Heard
                && SchemeUtilities.ToBool(GetProperty(IgnoreFiguredBassRestSymbol))))
        {
            ClearSpanners();
            _groups.Clear();
            return;
        }

        if (!_newEventFound)
        {
            return;
        }

        _newEventFound = false;

        /*
          Don't need to sync alignments, if we're not using extenders.
         */
        if (!useExtenders)
        {
            ClearSpanners();
        }

        if (!_continuation)
        {
            ClearSpanners();
            _groups.Clear();
        }

        int k = 0;
        for (int i = 0; i < _newEvents.Count; i++)
        {
            while (k < _groups.Count && _groups[k].CurrentEvent != null)
            {
                k++;
            }

            if (k >= _groups.Count)
            {
                FigureGroup group = new FigureGroup();
                _groups.Add(group);
            }

            _groups[k].ResetFigure();
            _groups[k].CurrentEvent = _newEvents[i];
            _groups[k].FigureItem = null;
            k++;
        }

        for (int i = 0; i < _groups.Count; i++)
        {
            if (!_groups[i].IsContinuation())
            {
                _groups[i].ResetFigure();
            }
        }

        if (useExtenders)
        {
            List<int> junkContinuations = new List<int>();
            for (int i = 0; i < _groups.Count; i++)
            {
                FigureGroup group = _groups[i];

                if (group.IsContinuation())
                {
                    if (group.ContinuationLine == null)
                    {
                        Spanner line = MakeSpanner("BassFigureContinuation", Nil.Instance);
                        Item item = group.FigureItem;
                        group.ContinuationLine = line;
                        line.SetBound(Direction.Negative, item);

                        /*
                          Don't add as child. This will cache the wrong
                          (pre-break) stencil when callbacks are triggered.
                        */
                        line.YParent = group.Group;
                        PointerGroupInterface.AddGrob(line, FiguresSymbol, item);

                        group.FigureItem = null;
                    }
                }
                else if (group.ContinuationLine != null)
                {
                    junkContinuations.Add(i);
                }
            }

            /*
              Ugh, repeated code.
             */
            List<Spanner> consecutive = new List<Spanner>();
            if (SchemeUtilities.ToBool(GetProperty(FiguredBassCenterContinuationsSymbol)))
            {
                for (int i = 0; i <= junkContinuations.Count; i++)
                {
                    if (i < junkContinuations.Count
                        && (i == 0
                            || junkContinuations[i - 1] == junkContinuations[i] - 1))
                    {
                        consecutive.Add(_groups[junkContinuations[i]].ContinuationLine);
                    }
                    else
                    {
                        CenterContinuations(consecutive);
                        consecutive.Clear();
                        if (i < junkContinuations.Count)
                        {
                            consecutive.Add(_groups[junkContinuations[i]].ContinuationLine);
                        }
                    }
                }
            }

            for (int i = 0; i < junkContinuations.Count; i++)
            {
                _groups[junkContinuations[i]].ContinuationLine = null;
            }
        }

        CreateGrobs();
        AddBrackets();
    }

    /// <summary>Closes the spanners off when nothing sounds any more.</summary>
    public override void StopTranslationTimestep()
    {
        if (_groups.Count == 0 || NowMoment.MainPart < _stopMoment.MainPart
            || NowMoment.GracePart < Rational.Zero)
        {
            return;
        }

        bool found = false;
        for (int i = 0; !found && i < _groups.Count; i++)
        {
            found = found || _groups[i].CurrentEvent != null;
        }

        if (!found)
        {
            ClearSpanners();
        }
    }

    private void CenterContinuations(IReadOnlyList<Spanner> consecutiveLines)
    {
        List<Grob> leftFigs = new List<Grob>();
        for (int j = consecutiveLines.Count; j-- > 0;)
        {
            leftFigs.Add(consecutiveLines[j].GetBound(Direction.Negative));
        }

        // One array SHARED by every line in the run — upstream makes a single Grob_array
        // and hands the same one to each. That sharing is the whole point: the lines are
        // centred on a common set of figures.
        GrobArray ga = new GrobArray();
        ga.SetArray(leftFigs);

        for (int j = consecutiveLines.Count; j-- > 0;)
        {
            consecutiveLines[j].SetObject(FiguresSymbol, ga);
        }
    }

    private void CenterRepeatedContinuations()
    {
        List<Spanner> consecutiveLines = new List<Spanner>();
        foreach (FigureGroup group in _groups)
        {
            Spanner contLine = group.ContinuationLine;
            if (contLine != null
                && (consecutiveLines.Count == 0
                    || (consecutiveLines[0].GetBound(Direction.Negative).GetColumn()
                            == contLine.GetBound(Direction.Negative).GetColumn()
                        && consecutiveLines[0].GetBound(Direction.Positive).GetColumn()
                            == contLine.GetBound(Direction.Positive).GetColumn())))
            {
                consecutiveLines.Add(contLine);
            }
            else
            {
                CenterContinuations(consecutiveLines);
                consecutiveLines.Clear();
            }
        }

        CenterContinuations(consecutiveLines);
    }

    private void ClearSpanners()
    {
        if (_alignment == null)
        {
            return;
        }

        AnnounceEndGrob(_alignment, Nil.Instance);
        _alignment = null;

        if (SchemeUtilities.ToBool(GetProperty(FiguredBassCenterContinuationsSymbol)))
        {
            CenterRepeatedContinuations();
        }

        for (int i = 0; i < _groups.Count; i++)
        {
            if (_groups[i].Group != null)
            {
                AnnounceEndGrob(_groups[i].Group, Nil.Instance);
                _groups[i].Group = null;
            }

            if (_groups[i].ContinuationLine != null)
            {
                AnnounceEndGrob(_groups[i].ContinuationLine, Nil.Instance);
                _groups[i].ContinuationLine = null;
            }
        }

        /* Check me, groups_.clear () ? */
    }

    private void CreateGrobs()
    {
        Grob muscol = GetProperty(CurrentMusicalColumnSymbol) as Item;
        if (_alignment == null)
        {
            _alignment = MakeSpanner("BassFigureAlignment", Nil.Instance);
            _alignment.SetBound(Direction.Negative, muscol);
        }

        _alignment.SetBound(Direction.Positive, muscol);

        object proc = GetProperty(FiguredBassFormatterSymbol);
        Interpreter interpreter = LilyPondScheme.Current;
        for (int i = 0; i < _groups.Count; i++)
        {
            FigureGroup group = _groups[i];

            if (group.CurrentEvent != null)
            {
                Item item = MakeItem("BassFigure", group.CurrentEvent);
                group.AssignFromEvent(group.CurrentEvent, item);

                if (group.Group == null)
                {
                    group.Group = MakeSpanner("BassFigureLine", Nil.Instance);
                    group.Group.SetBound(Direction.Negative, muscol);
                    AlignInterface.AddElement(_alignment, group.Group);
                }

                if (SchemeUtilities.Memq(
                        group.Number, GetProperty(ImplicitBassFiguresSymbol)))
                {
                    item.SetProperty(TransparentSymbol, true);
                    item.SetProperty(ImplicitSymbol, true);
                }

                object number = group.Number;
                if (group.Number is Nil && group.Text is Nil
                    && group.Alteration is Nil
                    && group.Augmented is Nil
                    && group.Diminished is Nil
                    && group.AugmentedSlash is Nil)
                {
                    // Convert `<_>` to an invisible digit.
                    number = 0L;
                    item.SetProperty(TransparentSymbol, true);
                    item.SetProperty(ImplicitSymbol, true);
                }

                object text = group.Text;
                if (!TextInterface.IsMarkup(text) && SchemeUtilities.IsProcedure(proc)
                    && interpreter != null)
                {
                    text = interpreter.Evaluator.Apply(
                        proc, new object[] { number, group.CurrentEvent, Context });
                }

                item.SetProperty(TextSymbol, text);

                AxisGroupInterface.AddElement(group.Group, item);
            }

            if (group.ContinuationLine != null)
            {
                /*
                  UGH should connect to the bass staff, and get the note heads.
                  For now, simply set the hidden figure to a default value to
                  ensure the extenders of different figures always end at the same
                  position, e.g. in <12 5> <12 5>
                */
                object text;
                if (SchemeUtilities.IsProcedure(proc) && interpreter != null)
                {
                    StreamEvent ev = group.CurrentEvent;
                    ev = ev.Clone();
                    ev.SetProperty(AlterationSymbol, false);
                    ev.SetProperty(AugmentedSymbol, false);
                    ev.SetProperty(DiminishedSymbol, false);
                    ev.SetProperty(AugmentedSlashSymbol, false);

                    text = interpreter.Evaluator.Apply(
                        proc, new object[] { 0L, ev, Context });
                }
                else
                {
                    text = new MutableString("0");
                }

                group.FigureItem.SetProperty(TransparentSymbol, true);
                group.FigureItem.SetProperty(TextSymbol, text);
                group.ContinuationLine.SetBound(Direction.Positive, group.FigureItem);
            }

            if (_groups[i].Group != null)
            {
                _groups[i].Group.SetBound(Direction.Positive, muscol);
            }
        }
    }

    private void AddBrackets()
    {
        List<Grob> encompass = new List<Grob>();
        bool inside = false;
        for (int i = 0; i < _groups.Count; i++)
        {
            if (_groups[i].CurrentEvent == null)
            {
                continue;
            }

            if (SchemeUtilities.ToBool(
                    _groups[i].CurrentEvent.GetProperty(BracketStartSymbol)))
            {
                inside = true;
            }

            if (inside && _groups[i].FigureItem != null)
            {
                encompass.Add(_groups[i].FigureItem);
            }

            if (SchemeUtilities.ToBool(
                    _groups[i].CurrentEvent.GetProperty(BracketStopSymbol)))
            {
                inside = false;

                Item brack = MakeItem("BassFigureBracket", _groups[i].CurrentEvent);
                for (int j = 0; j < encompass.Count; j++)
                {
                    PointerGroupInterface.AddGrob(brack, ElementsSymbol, encompass[j]);
                }

                encompass.Clear();
            }
        }
    }

    private void ListenBassFigure(StreamEvent ev)
    {
        _newEventFound = true;
        Moment stop = NowMoment + GetEventLength(ev, NowMoment);
        if (_stopMoment < stop)
        {
            _stopMoment = stop;
        }

        // Handle no-continuation here, don't even add it to the already existing
        // spanner... This fixes some layout issues (figure will be placed separately)
        if (SchemeUtilities.ToBool(GetProperty(UseBassFigureExtendersSymbol))
            && !SchemeUtilities.ToBool(ev.GetProperty(NoContinuationSymbol)))
        {
            for (int i = 0; i < _groups.Count; i++)
            {
                if (_groups[i].CurrentEvent == null && _groups[i].GroupIsEqualTo(ev))
                {
                    _groups[i].CurrentEvent = ev;
                    _continuation = true;
                    return;
                }
            }
        }

        _newEvents.Add(ev);
    }

    // A CLASS, not a struct: every caller mutates a group in place through upstream's
    // `Figure_group &`, and a struct in a List<T> would hand back a copy.
    private sealed class FigureGroup
    {
        private static readonly Symbol FigurePropertySymbol = Symbol.Intern("figure");
        private static readonly Symbol AlterationPropertySymbol = Symbol.Intern("alteration");
        private static readonly Symbol AugmentedPropertySymbol = Symbol.Intern("augmented");
        private static readonly Symbol DiminishedPropertySymbol = Symbol.Intern("diminished");
        private static readonly Symbol AugmentedSlashPropertySymbol
            = Symbol.Intern("augmented-slash");
        private static readonly Symbol TextPropertySymbol = Symbol.Intern("text");

        public FigureGroup() => ResetFigure();

        public Spanner Group { get; set; }

        public Spanner ContinuationLine { get; set; }

        public object Number { get; private set; }

        public object Alteration { get; private set; }

        public object Augmented { get; private set; }

        public object Diminished { get; private set; }

        public object AugmentedSlash { get; private set; }

        public object Text { get; private set; }

        public Item FigureItem { get; set; }

        public StreamEvent CurrentEvent { get; set; }

        /* Reset (or init) all figure information to FALSE */
        public void ResetFigure()
        {
            Number = false;
            Alteration = false;
            Augmented = false;
            Diminished = false;
            AugmentedSlash = false;
            Text = false;
        }

        public bool GroupIsEqualTo(StreamEvent evt)
        {
            return SchemeUtilities.IsEqual(Number, evt.GetProperty(FigurePropertySymbol))
                   && SchemeUtilities.IsEqual(
                       Alteration, evt.GetProperty(AlterationPropertySymbol))
                   && SchemeUtilities.IsEqual(
                       Augmented, evt.GetProperty(AugmentedPropertySymbol))
                   && SchemeUtilities.IsEqual(
                       Diminished, evt.GetProperty(DiminishedPropertySymbol))
                   && SchemeUtilities.IsEqual(
                       AugmentedSlash, evt.GetProperty(AugmentedSlashPropertySymbol))
                   && SchemeUtilities.IsEqual(Text, evt.GetProperty(TextPropertySymbol));
        }

        public bool IsContinuation() => CurrentEvent != null && GroupIsEqualTo(CurrentEvent);

        public void AssignFromEvent(StreamEvent currevt, Item item)
        {
            // NOTE: upstream reads the FIGURE off current_event_ and everything else off
            // currevt. The one call site passes the same event for both, so the two are
            // indistinguishable in practice; kept as written rather than tidied, because
            // tidying it would be a guess about which upstream meant.
            Number = CurrentEvent.GetProperty(FigurePropertySymbol);
            Alteration = currevt.GetProperty(AlterationPropertySymbol);
            Augmented = currevt.GetProperty(AugmentedPropertySymbol);
            Diminished = currevt.GetProperty(DiminishedPropertySymbol);
            AugmentedSlash = currevt.GetProperty(AugmentedSlashPropertySymbol);
            Text = currevt.GetProperty(TextPropertySymbol);
            FigureItem = item;
        }
    }
}

/// <summary>Positions figured bass alignments over notes.</summary>
public sealed class FiguredBassPositionEngraver : Engraver
{
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");
    private static readonly Symbol SlurInterface = Symbol.Intern("slur-interface");
    private static readonly Symbol StemInterface = Symbol.Intern("stem-interface");
    private static readonly Symbol TieInterface = Symbol.Intern("tie-interface");
    private static readonly Symbol BassFigureAlignmentInterface
        = Symbol.Intern("bass-figure-alignment-interface");

    private readonly List<Grob> _support = new List<Grob>();
    private readonly List<Grob> _spanSupport = new List<Grob>();

    private Spanner _bassFigureAlignment;
    private Spanner _positioner;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public FiguredBassPositionEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Figured_bass_position_engraver";

    /// <summary>Collects the grobs the alignment has to clear, and starts the spanner.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob is Item && info.Grob.HasInterface(NoteColumnInterface))
        {
            _support.Add(info.Grob);
        }

        if (info.Grob.HasInterface(SlurInterface))
        {
            _spanSupport.Add(info.Grob);
        }

        if (info.Grob.HasInterface(StemInterface))
        {
            _support.Add(info.Grob);
        }

        if (info.Grob is Spanner alignment && info.Grob.HasInterface(BassFigureAlignmentInterface))
        {
            _bassFigureAlignment = alignment;
            StartSpanner();
        }
    }

    /// <summary>Releases a slur that ended, and closes the spanner with its alignment.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeEndGrob(GrobInfo info)
    {
        if (info.Grob.HasInterface(SlurInterface))
        {
            int i = _spanSupport.IndexOf(info.Grob);
            if (i >= 0)
            {
                _spanSupport.RemoveAt(i);
            }
        }

        if (info.Grob.HasInterface(TieInterface))
        {
            _support.Add(info.Grob);
        }

        if (info.Grob is Spanner && info.Grob.HasInterface(BassFigureAlignmentInterface))
        {
            StopSpanner();
        }
    }

    /// <summary>Closes the spanner off at the end of the context's life.</summary>
    public override void FinalizeTranslation() => StopSpanner();

    /// <summary>Hangs this timestep's supports off the positioner.</summary>
    public override void StopTranslationTimestep()
    {
        if (_positioner != null)
        {
            for (int i = 0; i < _spanSupport.Count; i++)
            {
                SidePositionInterface.AddSupport(_positioner, _spanSupport[i]);
            }

            for (int i = 0; i < _support.Count; i++)
            {
                SidePositionInterface.AddSupport(_positioner, _support[i]);
            }
        }

        _support.Clear();
    }

    private void StartSpanner()
    {
        // upstream asserts (!positioner_) here; the port answers the same question with a
        // programming error rather than aborting the whole run.
        if (_positioner != null)
        {
            Warn.ProgrammingError(
                "figured bass positioner started while one was already open");
            return;
        }

        _positioner = MakeSpanner("BassFigureAlignmentPositioning", _bassFigureAlignment);
        _positioner.SetBound(
            Direction.Negative, _bassFigureAlignment.GetBound(Direction.Negative));
        AxisGroupInterface.AddElement(_positioner, _bassFigureAlignment);
    }

    private void StopSpanner()
    {
        if (_positioner != null && _positioner.GetBound(Direction.Positive) == null)
        {
            _positioner.SetBound(
                Direction.Positive, _bassFigureAlignment.GetBound(Direction.Positive));
        }

        _positioner = null;
        _bassFigureAlignment = null;
    }
}
