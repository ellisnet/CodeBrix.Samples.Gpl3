/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
  Jan Nieuwenhuizen <janneke@gnu.org>
  Copyright (C) 2011--2026 Mike Solomon <mike@mikesolomon.org>

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

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/bar-engraver.cc, lily/span-bar-engraver.cc, lily/span-bar-stub-engraver.cc, lily/bar-number-engraver.cc;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/// <summary>
/// The bar-type layers <c>Bar_engraver</c> weighs against each other, from low to high
/// priority.
/// </summary>
internal enum BarLayerType
{
    /// <summary>No bar at all.</summary>
    None = 0,

    /// <summary>The underlying repeat bar (<c>underlyingRepeatBarType</c>).</summary>
    UnderlyingRepeat,

    /// <summary>The caesura's underlying bar line.</summary>
    UnderlyingCaesura,

    /// <summary>A submeasure division bar.</summary>
    Submeasure,

    /// <summary>The ordinary measure bar.</summary>
    Measure,

    /// <summary>The caesura's own bar line.</summary>
    Caesura,

    /// <summary>A <c>\section</c> bar.</summary>
    Section,

    /// <summary>The <c>\fine</c> bar.</summary>
    Fine,

    /// <summary>A repeat bar.</summary>
    Repeat,
}

/// <summary>
/// Creates bar lines for various commands, including <c>\bar</c>.
/// <para>
/// If <c>forbidBreakBetweenBarLines</c> is true, allow line breaks at bar lines only.
/// </para>
/// <para>
/// The engraver never decides what a bar LOOKS like: it collects candidate glyph
/// strings by priority and hands them to
/// <c>bar-line::calc-glyph-name-for-direction</c> (scm/bar-line.scm), once per break
/// direction. What comes back is what the <c>BarLine</c> grob carries.
/// </para>
/// </summary>
public class BarEngraver : Engraver
{
    private static readonly Symbol TimingSymbol = Symbol.Intern("Timing");
    private static readonly Symbol ScoreSymbol = Symbol.Intern("Score");
    private static readonly Symbol CurrentBarLineSymbol = Symbol.Intern("currentBarLine");
    private static readonly Symbol WhichBarSymbol = Symbol.Intern("whichBar");
    private static readonly Symbol RepeatCommandsSymbol = Symbol.Intern("repeatCommands");
    private static readonly Symbol EndRepeatSymbol = Symbol.Intern("end-repeat");
    private static readonly Symbol StartRepeatSymbol = Symbol.Intern("start-repeat");
    private static readonly Symbol PrintInitialRepeatBarSymbol
        = Symbol.Intern("printInitialRepeatBar");

    private static readonly Symbol PrintTrivialVoltaRepeatsSymbol
        = Symbol.Intern("printTrivialVoltaRepeats");

    private static readonly Symbol GlyphSymbol = Symbol.Intern("glyph");
    private static readonly Symbol GlyphLeftSymbol = Symbol.Intern("glyph-left");
    private static readonly Symbol GlyphRightSymbol = Symbol.Intern("glyph-right");
    private static readonly Symbol ForbidBreakSymbol = Symbol.Intern("forbidBreak");
    private static readonly Symbol ForbidBreakBetweenBarLinesSymbol
        = Symbol.Intern("forbidBreakBetweenBarLines");

    private static readonly Symbol ToBarlineSymbol = Symbol.Intern("to-barline");
    private static readonly Symbol AllowSpanBarSymbol = Symbol.Intern("allow-span-bar");
    private static readonly Symbol AllowSpanBarAboveSymbol = Symbol.Intern("allow-span-bar-above");
    private static readonly Symbol SpanBarStubInterfaceSymbol
        = Symbol.Intern("span-bar-stub-interface");

    private static readonly Symbol LabelSymbol = Symbol.Intern("label");
    private static readonly Symbol SegnoStyleSymbol = Symbol.Intern("segnoStyle");
    private static readonly Symbol BarLineWordSymbol = Symbol.Intern("bar-line");
    private static readonly Symbol UnderlyingBarLineSymbol = Symbol.Intern("underlying-bar-line");
    private static readonly Symbol CaesuraTypeSymbol = Symbol.Intern("caesuraType");
    private static readonly Symbol CaesuraTypeTransformSymbol
        = Symbol.Intern("caesuraTypeTransform");

    private static readonly Symbol ArticulationsSymbol = Symbol.Intern("articulations");
    private static readonly Symbol ArticulationTypeSymbol = Symbol.Intern("articulation-type");
    private static readonly Symbol SubmeasureStructureSymbol
        = Symbol.Intern("submeasureStructure");

    private static readonly Symbol BeatBaseSymbol = Symbol.Intern("beatBase");
    private static readonly Symbol MeasureStartNowSymbol = Symbol.Intern("measureStartNow");
    private static readonly Symbol SubmeasureBarsEnabledSymbol
        = Symbol.Intern("submeasureBarsEnabled");

    private static readonly Symbol MeasureBarTypeSymbol = Symbol.Intern("measureBarType");
    private static readonly Symbol SubmeasureBarTypeSymbol = Symbol.Intern("submeasureBarType");
    private static readonly Symbol SectionBarTypeSymbol = Symbol.Intern("sectionBarType");
    private static readonly Symbol FineBarTypeSymbol = Symbol.Intern("fineBarType");
    private static readonly Symbol UnderlyingRepeatBarTypeSymbol
        = Symbol.Intern("underlyingRepeatBarType");

    private static readonly Symbol DoubleRepeatBarTypeSymbol
        = Symbol.Intern("doubleRepeatBarType");

    private static readonly Symbol StartRepeatBarTypeSymbol = Symbol.Intern("startRepeatBarType");
    private static readonly Symbol EndRepeatBarTypeSymbol = Symbol.Intern("endRepeatBarType");
    private static readonly Symbol DoubleRepeatSegnoBarTypeSymbol
        = Symbol.Intern("doubleRepeatSegnoBarType");

    private static readonly Symbol FineStartRepeatSegnoBarTypeSymbol
        = Symbol.Intern("fineStartRepeatSegnoBarType");

    private static readonly Symbol StartRepeatSegnoBarTypeSymbol
        = Symbol.Intern("startRepeatSegnoBarType");

    private static readonly Symbol EndRepeatSegnoBarTypeSymbol
        = Symbol.Intern("endRepeatSegnoBarType");

    private static readonly Symbol FineSegnoBarTypeSymbol = Symbol.Intern("fineSegnoBarType");
    private static readonly Symbol SegnoBarTypeSymbol = Symbol.Intern("segnoBarType");
    private static readonly Symbol CalcGlyphNameForDirectionSymbol
        = Symbol.Intern("bar-line::calc-glyph-name-for-direction");

    private StreamEvent _caesuraEvent;
    private readonly BooleanEventListener _fineListener = new BooleanEventListener();
    private readonly BooleanEventListener _sectionListener = new BooleanEventListener();
    private readonly BooleanEventListener _underlyingRepeatListener = new BooleanEventListener();

    private bool _repeatEndObserved;
    private bool _repeatStartObserved;
    private bool _segnoObserved;

    private object _glyph = Nil.Instance;
    private object _glyphLeft = Nil.Instance;
    private object _glyphRight = Nil.Instance;

    // #f forces initial calculation
    private object _submeasureStructure = false;
    private readonly List<Rational> _submeasurePositions = new List<Rational>();

    // This period is the length of the submeasure structure in units of whole
    // notes.  It normally equals the measure length, but in music with irregular
    // measures, the structure may be longer than the current measure (and be used
    // only partly) or shorter than the current measure (and be repeated to fill
    // the measure).
    private Rational _submeasurePeriod = Rational.Zero;

    private Item _bar;
    private readonly List<Spanner> _spanners = new List<Spanner>();
    private bool _firstTime = true;
    private bool _hasAnyGlyph;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public BarEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Bar_engraver";

    /// <summary>Gets the bar line made this timestep, for tests.</summary>
    public Item Bar => _bar;

    /// <summary>Starts listening for the events bar decisions are made from.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo("caesura-event", ListenCaesura);
        ListenTo("segno-mark-event", ListenSegnoMark);
        ListenTo("fine-event", _fineListener.Listen);
        ListenTo("section-event", _sectionListener.Listen);
        ListenTo("ad-hoc-jump-event", _underlyingRepeatListener.Listen);
        ListenTo("coda-mark-event", _underlyingRepeatListener.Listen);
        ListenTo("dal-segno-event", _underlyingRepeatListener.Listen);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    // DEFER: Consider refactoring this.  Other engravers will probably want to do
    // some of this too.  For example, we might want to add an option that makes
    // Accidental_engraver reset at submeasure divisions.
    private bool IsAtSubmeasureDivision()
    {
        // Recompute submeasure positions after changes in submeasureStructure.
        object structureScm = GetProperty(SubmeasureStructureSymbol);
        if (!ReferenceEquals(_submeasureStructure, structureScm))
        {
            // eq? considers object identity; therefore, someone must have set the
            // property since the last time we checked, though its value is not
            // necessarily different.
            _submeasureStructure = structureScm;

            _submeasurePositions.Clear();
            _submeasurePositions.Add(Rational.Zero);
            Rational running = Rational.Zero;
            object cursor = structureScm;
            while (cursor is Pair pair)
            {
                running += Epg8Support.ToRational(pair.Car, Rational.Zero);
                _submeasurePositions.Add(running);
                cursor = pair.Cdr;
            }

            // The last element of the result is the period of the structure in units
            // of beatBase.  If the submeasureStructure is empty, the period is zero,
            // and we will have to handle it as a special case later.
            _submeasurePeriod = _submeasurePositions[_submeasurePositions.Count - 1];
            _submeasurePositions.RemoveAt(_submeasurePositions.Count - 1);

            // Convert units from beats to whole notes.
            Rational beatBase = Epg8Support.ToRational(
                GetProperty(BeatBaseSymbol), new Rational(1, 4));
            _submeasurePeriod *= beatBase;
            for (int i = 0; i < _submeasurePositions.Count; i++)
            {
                _submeasurePositions[i] *= beatBase;
            }
        }

        // A submeasure can only begin when the main part of the current time
        // advances.  Any later timestep with the same main part isn't a candidate for
        // a submeasure bar line.
        GlobalContext global = Context?.GlobalContext; // should always exist
        if (global != null && global.NowMoment.MainPart == global.PreviousMoment.MainPart)
        {
            return false;
        }

        if (!_submeasurePeriod.IsNonZero) // no submeasure structure is defined
        {
            // If we're at a measure boundary, say that we're also at a submeasure
            // boundary.
            return Epg8Support.ToBool(GetProperty(MeasureStartNowSymbol));
        }

        Moment pos = MeasureTiming.ScaledMeasurePosition(Context, _submeasurePeriod);
        return _submeasurePositions.Contains(pos.MainPart);
    }

    // Reads caesuraType, adds the event's articulations, and passes the result
    // through caesuraTypeTransform.
    // TODO: Caesura_engraver and Divisio_engraver also do this stuff.
    // Refactor to reduce repetition and ensure consistency.
    private object GetCaesuraType(Context ctx, StreamEvent ev)
    {
        object caesuraType = ctx.GetProperty(CaesuraTypeSymbol);

        // Form a symbol list describing the user-provided articulations.
        List<object> userArticTypes = new List<object>();
        object arts = ev.GetProperty(ArticulationsSymbol);
        object cursor = arts;
        while (cursor is Pair pair)
        {
            if (pair.Car is StreamEvent art
                && art.GetProperty(ArticulationTypeSymbol) is Symbol articType)
            {
                userArticTypes.Add(articType);
            }

            cursor = pair.Cdr;
        }

        // Add the user's articulations to the caesuraType value.
        caesuraType = new Pair(
            new Pair(ArticulationsSymbol, Pair.ListFrom(userArticTypes)), caesuraType);

        // Pass caesuraType through the transform function, if it is set.
        object transform = ctx.GetProperty(CaesuraTypeTransformSymbol);
        if (SchemeUtilities.IsProcedure(transform))
        {
            caesuraType = SchemeUtilities.CallCallback(
                transform, ctx, caesuraType, Nil.Instance /*observations*/);
        }

        return caesuraType;
    }

    /// <summary>
    /// Returns zero or more <c>BarLine.glyph</c> values from highest to lowest
    /// priority.
    /// </summary>
    private object CalcBarType()
    {
        object caesuraType = _caesuraEvent != null
            ? GetCaesuraType(Context, _caesuraEvent)
            : Nil.Instance;

        bool segno = _segnoObserved
            && ReferenceEquals(GetProperty(SegnoStyleSymbol), BarLineWordSymbol);

        List<object> glyphs = new List<object>();

        // This order could be user-configurable, but most of the permutations are
        // probably not useful enough to be worth explaining, testing, and
        // maintaining.  Varying the position of a caesura/phrase bar might be a good
        // reason to do it, but that is easy enough to do with two layers (as seen).
        BarLayerType[] typesByPriority =
        {
            BarLayerType.Repeat,
            BarLayerType.Fine,
            BarLayerType.Section,
            BarLayerType.Caesura,
            BarLayerType.Measure,
            BarLayerType.Submeasure,
            BarLayerType.UnderlyingCaesura,
            BarLayerType.UnderlyingRepeat,
        };

        foreach (BarLayerType layer in typesByPriority)
        {
            bool hasUnderlyingBar = false;
            string ub = null;

            // Read the named bar-type context property into `ub`.
            void ReadBar(Symbol contextPropSym)
            {
                if (GetProperty(contextPropSym) is MutableString s)
                {
                    ub = s.ToString();
                    hasUnderlyingBar = true;
                }
            }

            // Get the requested bar subproperty ('bar-line or 'underlying-bar-line)
            // from the caesura properties.
            void ReadCaesuraBar(Symbol subpropSym)
            {
                Pair entry = SchemeUtilities.Assq(subpropSym, caesuraType);
                if (entry != null && entry.Cdr is MutableString s)
                {
                    ub = s.ToString();
                    hasUnderlyingBar = true;
                }
            }

            switch (layer)
            {
                case BarLayerType.Repeat:
                    // TODO: Move this jenga tower into a Scheme callback if further
                    // customizability is desired.  The number of dimensions makes it a
                    // hassle to maintain a built-in context property for every
                    // combination.  Don't pass the state as parameters: set context
                    // properties before calling.  (Well, some of these already came from
                    // repeatCommands, for what that's worth.)
                    if (segno)
                    {
                        if (_repeatStartObserved)
                        {
                            if (_repeatEndObserved)
                            {
                                ReadBar(DoubleRepeatSegnoBarTypeSymbol);
                            }
                            else if (_fineListener.Heard)
                            {
                                ReadBar(FineStartRepeatSegnoBarTypeSymbol);
                            }
                            else
                            {
                                ReadBar(StartRepeatSegnoBarTypeSymbol);
                            }
                        }
                        else if (_repeatEndObserved)
                        {
                            ReadBar(EndRepeatSegnoBarTypeSymbol);
                        }
                        else // no repeat
                        {
                            if (_fineListener.Heard)
                            {
                                ReadBar(FineSegnoBarTypeSymbol);
                            }
                            else
                            {
                                ReadBar(SegnoBarTypeSymbol);
                            }
                        }
                    }
                    else // no segno
                    {
                        if (_repeatStartObserved)
                        {
                            if (_repeatEndObserved)
                            {
                                ReadBar(DoubleRepeatBarTypeSymbol);
                            }
                            else
                            {
                                ReadBar(StartRepeatBarTypeSymbol);
                            }
                        }
                        else if (_repeatEndObserved)
                        {
                            ReadBar(EndRepeatBarTypeSymbol);
                        }
                    }

                    break;

                case BarLayerType.Fine:
                    if (_fineListener.Heard)
                    {
                        ReadBar(FineBarTypeSymbol);
                    }

                    break;

                case BarLayerType.Section:
                    // Gould writes that "[a] thin double barline ... marks the written
                    // end of the music when this is not the end of the piece" (Behind
                    // Bars, p.240).  Although it would be fairly easy to implement that
                    // as a default, we avoid it on the grounds that the input is
                    // possibly not a finished work, and it is easy for the user to add a
                    // \section command at the end when it is.
                    if (_sectionListener.Heard)
                    {
                        ReadBar(SectionBarTypeSymbol);
                    }

                    break;

                case BarLayerType.Caesura:
                    if (_caesuraEvent != null)
                    {
                        ReadCaesuraBar(BarLineWordSymbol);
                    }

                    break;

                case BarLayerType.Measure:
                    if (!_firstTime && Epg8Support.ToBool(GetProperty(MeasureStartNowSymbol)))
                    {
                        ReadBar(MeasureBarTypeSymbol);
                    }

                    break;

                case BarLayerType.Submeasure:
                    if (!_firstTime
                        && Epg8Support.ToBool(GetProperty(SubmeasureBarsEnabledSymbol))
                        && IsAtSubmeasureDivision())
                    {
                        ReadBar(SubmeasureBarTypeSymbol);
                    }

                    break;

                case BarLayerType.UnderlyingCaesura:
                    if (_caesuraEvent != null)
                    {
                        ReadCaesuraBar(UnderlyingBarLineSymbol);
                    }

                    break;

                case BarLayerType.UnderlyingRepeat:
                    if (_underlyingRepeatListener.Heard)
                    {
                        ReadBar(UnderlyingRepeatBarTypeSymbol);
                    }

                    break;

                default:
                    break;
            }

            if (hasUnderlyingBar)
            {
                glyphs.Add(new MutableString(ub));
            }
        }

        return Pair.ListFrom(glyphs);
    }

    private void ListenCaesura(StreamEvent ev) => StreamEvent.AssignEventOnce(ref _caesuraEvent, ev);

    private void ListenSegnoMark(StreamEvent ev)
    {
        // Ignore a default segno at the beginning of a piece, just like
        // Mark_tracking_translator.
        if (_firstTime)
        {
            object label = ev.GetProperty(LabelSymbol);
            if (!(label is long || label is int || label is System.Numerics.BigInteger))
            {
                return; // \segnoMark \default
            }
        }

        _segnoObserved = true;
    }

    /// <summary>Decides whether an initial bar line is possible at all.</summary>
    public override void Initialize()
    {
        base.Initialize();
        Context.SetProperty(CurrentBarLineSymbol, Nil.Instance);

        // For decisions about initial bar lines, what matters is the lifetime of the
        // Timing context.  The Bar_engraver's local context could be an ossia staff
        // or other late-created staff, in which cases we allow initial bar lines.
        //
        // TODO: Gould says that "most editions do not include an initial barline
        // through the cue stave" (Behind Bars, p. 497), so we may want to make this
        // configurable, check the usual editions, and possibly change the default.
        Context timing = Context?.FindContextAbove(TimingSymbol);
        if (timing != null)
        {
            _firstTime = !(timing.InitMoment < timing.NowMoment);
        }
    }

    /// <summary>Releases the bar line made in the previous timestep.</summary>
    public override void StartTranslationTimestep()
    {
        // We reset currentBarLine here rather than in stop_translation_timestep ()
        // so that other engravers can use it during stop_translation_timestep ().
        if (_bar != null)
        {
            _bar = null;
            Context.SetProperty(CurrentBarLineSymbol, Nil.Instance);
        }
    }

    /// <summary>
    /// Works out this timestep's glyphs. At the start of a piece, we don't print any
    /// repeat bars.
    /// </summary>
    public override void PreProcessMusic()
    {
        object glyphs = Nil.Instance;

        // If whichBar is set, use it.  It was probably set with \bar, but it might
        // have been set with the deprecated \set Timing.whichBar or a Scheme
        // equivalent.
        object wb = GetProperty(WhichBarSymbol);
        if (wb is MutableString)
        {
            glyphs = Pair.List(wb);
        }
        else // consider automatic bars
        {
            if (!_firstTime || Epg8Support.ToBool(GetProperty(PrintInitialRepeatBarSymbol)))
            {
                object repeatCommands = GetProperty(RepeatCommandsSymbol);
                object cursor = repeatCommands;
                while (cursor is Pair commandPair)
                {
                    object command = commandPair.Car;
                    object options = Nil.Instance;
                    if (command is Pair inner) // (command option...)
                    {
                        options = inner.Cdr;
                        command = inner.Car;
                    }

                    if (ReferenceEquals(command, EndRepeatSymbol))
                    {
                        const long Default = 1L;
                        long retCount = options is Pair optionPair
                            ? Epg8Support.ToLong(optionPair.Car, Default)
                            : Default;
                        if (retCount >= 1
                            || Epg8Support.ToBool(GetProperty(PrintTrivialVoltaRepeatsSymbol)))
                        {
                            _repeatEndObserved = true;
                        }
                    }
                    else if (ReferenceEquals(command, StartRepeatSymbol))
                    {
                        const long Default = 2L;
                        long repCount = options is Pair optionPair
                            ? Epg8Support.ToLong(optionPair.Car, Default)
                            : Default;
                        if (repCount >= 2
                            || Epg8Support.ToBool(GetProperty(PrintTrivialVoltaRepeatsSymbol)))
                        {
                            _repeatStartObserved = true;
                        }
                    }

                    cursor = commandPair.Cdr;
                }
            }
            else
            {
                _repeatEndObserved = false;
                _repeatStartObserved = false;
                _underlyingRepeatListener.Reset();
            }

            if (_repeatStartObserved || _repeatEndObserved || _segnoObserved)
            {
                _underlyingRepeatListener.SetHeard();
            }

            glyphs = CalcBarType();
        }

        object calcName = LilyPondScheme.LookupProcedure(CalcGlyphNameForDirectionSymbol);
        _glyph = CallGlyphName(calcName, glyphs, 0L);
        _glyphLeft = CallGlyphName(calcName, glyphs, -1L);
        _glyphRight = CallGlyphName(calcName, glyphs, 1L);
        _hasAnyGlyph = _glyph is MutableString
            || _glyphLeft is MutableString
            || _glyphRight is MutableString;

        // This needs to be in pre-process-music so other engravers can notice a break
        // won't be allowed (unless forced) at process-music stage.  That allows some
        // of them to efficiently skip processing that is only needed at potential
        // break points.
        if (!_hasAnyGlyph
            && Epg8Support.ToBool(GetProperty(ForbidBreakBetweenBarLinesSymbol)))
        {
            FindScoreContext()?.SetProperty(ForbidBreakSymbol, true);
        }
    }

    /// <summary>Makes the timestep's <c>BarLine</c>, when there is any glyph to draw.</summary>
    public override void ProcessMusic()
    {
        if (!_hasAnyGlyph)
        {
            return;
        }

        // TODO: We have events in most cases (not for manual repeats), so it
        // would be nice to provide a cause here.
        _bar = MakeItem("BarLine", Nil.Instance);

        if (!SchemeIsEqual(_glyph, _bar.GetProperty(GlyphSymbol)))
        {
            _bar.SetProperty(GlyphSymbol, _glyph);
        }

        if (!SchemeIsEqual(_glyphLeft, _bar.GetProperty(GlyphLeftSymbol)))
        {
            _bar.SetProperty(GlyphLeftSymbol, _glyphLeft);
        }

        if (!SchemeIsEqual(_glyphRight, _bar.GetProperty(GlyphRightSymbol)))
        {
            _bar.SetProperty(GlyphRightSymbol, _glyphRight);
        }

        Context?.SetProperty(CurrentBarLineSymbol, _bar);
    }

    /// <summary>Ends every <c>to-barline</c> spanner at the bar.</summary>
    public override void ProcessAcknowledged()
    {
        if (_bar != null)
        {
            foreach (Spanner sp in _spanners)
            {
                sp.SetBound(Direction.Positive, _bar);
            }
        }

        _spanners.Clear();
    }

    /*
      lines may only be broken if there is a barline in all staves
    */

    /// <summary>Forgets the timestep's state.</summary>
    public override void StopTranslationTimestep()
    {
        _glyph = Nil.Instance;
        _glyphLeft = Nil.Instance;
        _glyphRight = Nil.Instance;
        _firstTime = false;
        _hasAnyGlyph = false;
        _repeatEndObserved = false;
        _repeatStartObserved = false;
        _segnoObserved = false;

        _caesuraEvent = null;
        _fineListener.Reset();
        _sectionListener.Reset();
        _underlyingRepeatListener.Reset();
    }

    /// <summary>Marks span-bar stubs unwanted when this context drew no bar.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob is Item stub && stub.HasInterface(SpanBarStubInterfaceSymbol))
        {
            if (_bar == null)
            {
                // Tell Span_bar_engraver that drawing a bar to this context is
                // unwanted.
                stub.SetProperty(AllowSpanBarSymbol, false);
                stub.SetProperty(AllowSpanBarAboveSymbol, false);
            }
        }
    }

    /// <summary>Collects ending spanners that want to reach the bar line.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeEndGrob(GrobInfo info)
    {
        if (_bar != null) // otherwise avoid a little work
        {
            if (info.Grob is Spanner sp && Epg8Support.ToBool(sp.GetProperty(ToBarlineSymbol)))
            {
                _spanners.Add(sp);
            }
        }
    }

    private Context FindScoreContext()
    {
        Context score = Context?.FindContextAbove(ScoreSymbol);
        if (score == null)
        {
            Warn.ProgrammingError("no score context");
        }

        return score;
    }

    private static object CallGlyphName(object calcName, object glyphs, long direction)
    {
        if (calcName == null)
        {
            Warn.ProgrammingError(
                "Bar_engraver: bar-line::calc-glyph-name-for-direction not found");
            return false;
        }

        return SchemeUtilities.CallCallback(calcName, glyphs, direction);
    }

    private static bool SchemeIsEqual(object a, object b)
        => CodeBrix.LilyScheme.Primitives.CorePrimitives.SchemeEqual(a, b);
}

/// <summary>
/// Makes cross-staff bar lines: it catches all normal bar lines and draws a single
/// span bar across them.
/// <para>
/// Vertical alignment of staves changes the appearance of spanbars. It is up to the
/// aligner (<c>Vertical_align_engraver</c>, in this case) to add extra dependencies to
/// the spanbars.
/// </para>
/// </summary>
public class SpanBarEngraver : Engraver
{
    private static readonly Symbol BarLineInterfaceSymbol = Symbol.Intern("bar-line-interface");
    private static readonly Symbol SpanBarInterfaceSymbol = Symbol.Intern("span-bar-interface");
    private static readonly Symbol SpanBarStubInterfaceSymbol
        = Symbol.Intern("span-bar-stub-interface");

    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol AllowSpanBarSymbol = Symbol.Intern("allow-span-bar");
    private static readonly Symbol AllowSpanBarAboveSymbol = Symbol.Intern("allow-span-bar-above");
    private static readonly Symbol HasSpanBarSymbol = Symbol.Intern("has-span-bar");

    private Item _spanbar;
    private bool _makeSpanbar;
    private readonly List<Item> _bars = new List<Item>();
    private readonly List<Item> _stubs = new List<Item>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public SpanBarEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Span_bar_engraver";

    /// <summary>Collects bar lines and span-bar stubs.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!(info.Grob is Item it))
        {
            return;
        }

        if (it.HasInterface(BarLineInterfaceSymbol))
        {
            if (!it.HasInterface(SpanBarInterfaceSymbol))
            {
                _bars.Add(it);

                if (_bars.Count >= 2 && _spanbar == null)
                {
                    _makeSpanbar = true;
                }
            }
        }
        else if (it.HasInterface(SpanBarStubInterfaceSymbol))
        {
            _stubs.Add(it);
        }
    }

    /// <summary>Makes the span bar once two or more bar lines were seen.</summary>
    public override void ProcessAcknowledged()
    {
        if (_makeSpanbar)
        {
            _spanbar = MakeItem("SpanBar", Nil.Instance);
            foreach (Item bar in _bars)
            {
                PointerGroupInterface.AddGrob(_spanbar, ElementsSymbol, bar);
            }

            // TODO: More bar lines could be acknowledged, but they won't be added to
            // the group.  That might not happen currently, but it could conceivably
            // happen after enhancements to bar-line engraving.
            _makeSpanbar = false;
        }
    }

    /// <summary>Wires the finished span bar into its bar lines.</summary>
    public override void StopTranslationTimestep()
    {
        if (_spanbar != null)
        {
            // Span_bar_stub_engraver creates stubs in contexts where no bar line was
            // created.  Usually, this is because there is no Bar_engraver operating
            // in these contexts; however, if Bar_engraver is operating in one of
            // those contexts and has been configured not to create a BarLine at this
            // point, it sets SpanBarStub.allow-span-bar and .allow-span-bar-above to
            // #f to signal that.  In that case, we include this stub among the
            // SpanBar's elements so that the SpanBar avoids drawing through the staff
            // (or whatever context it happens to be).
            foreach (Item stub in _stubs)
            {
                if (!Epg8Support.ToBool(stub.GetProperty(AllowSpanBarSymbol))
                    || !Epg8Support.ToBool(stub.GetProperty(AllowSpanBarAboveSymbol)))
                {
                    _bars.Add(stub);
                    PointerGroupInterface.AddGrob(_spanbar, ElementsSymbol, stub);
                }
            }

            // Because of alignAboveContext and alignBelowContext, grobs are not
            // necessarily announced in the order that they should be laid out, so
            // they need to be sorted.
            List<(Item Bar, int Index)> keyed = new List<(Item, int)>();
            foreach (Item bar in _bars)
            {
                keyed.Add((bar, SpanBarVerticalOrder.GetVerticalAxisGroupIndex(bar)));
            }

            // List<T>.Sort is unstable; an index tiebreak makes it upstream's
            // std::stable_sort.
            List<int> order = new List<int>();
            for (int i = 0; i < keyed.Count; i++)
            {
                order.Add(i);
            }

            order.Sort((a, b) => keyed[a].Index != keyed[b].Index
                ? keyed[a].Index.CompareTo(keyed[b].Index)
                : a.CompareTo(b));

            _bars.Clear();
            foreach (int i in order)
            {
                _bars.Add(keyed[i].Bar);
            }

            int numBars = _bars.Count;
            bool allowAbove = false;
            for (int i = 0; i < numBars; i++)
            {
                Item bar = _bars[i];
                bool isBottom = (i + 1) == numBars;
                bool allowBelow = !isBottom
                    && Epg8Support.ToBool(bar.GetProperty(AllowSpanBarSymbol))
                    && Epg8Support.ToBool(_bars[i + 1].GetProperty(AllowSpanBarAboveSymbol));
                bar.SetObject(
                    HasSpanBarSymbol,
                    new Pair(
                        allowBelow ? (object)_spanbar : false,
                        allowAbove ? (object)_spanbar : false));
                allowAbove = allowBelow;
            }

            _spanbar = null;
        }

        _bars.Clear();
        _stubs.Clear();
    }
}

/*
  The Span_bar_stub_engraver creates SpanBarStub grobs in the contexts
  that a grouping context contains.  For example, if a PianoStaff contains
  two Staffs, a Dynamics, and a Lyrics, SpanBarStubs will be created in
  all contexts that do not have bar lines (Dynamics and Lyrics).

  We only want to create these SpanBarStubs in contexts that the SpanBar
  traverses.  However, Contexts do not contain layout information and it
  is thus difficult to know if they will eventually be above or below other
  Contexts.  To determine this we use the VerticalAxisGroup created in the
  Context.  We relate VerticalAxisGroups to Contexts in the variable
  axis_groups_ and weed out unused contexts after each translation timestep.

  Note that SpanBarStubs exist for pure height calculations ONLY.
  They should never be visually present on the page and should never
  be engraved in contexts where BarLines are engraved.
*/

/// <summary>
/// Makes stubs for span bars in all contexts that the span bars cross.
/// </summary>
public class SpanBarStubEngraver : Engraver
{
    private static readonly Symbol SpanBarInterfaceSymbol = Symbol.Intern("span-bar-interface");
    private static readonly Symbol HaraKiriGroupSpannerInterfaceSymbol
        = Symbol.Intern("hara-kiri-group-spanner-interface");

    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol AllowSpanBarSymbol = Symbol.Intern("allow-span-bar");
    private static readonly Symbol SpanBarStubSymbol = Symbol.Intern("SpanBarStub");

    private readonly List<Grob> _spanbars = new List<Grob>();
    private readonly List<(Grob AxisGroup, Context Owner)> _axisGroups
        = new List<(Grob, Context)>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public SpanBarStubEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Span_bar_stub_engraver";

    /// <summary>Collects span bars and per-context vertical axis groups.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob.HasInterface(SpanBarInterfaceSymbol))
        {
            _spanbars.Add(info.Grob);
        }

        if (info.Grob.HasInterface(HaraKiriGroupSpannerInterfaceSymbol))
        {
            _axisGroups.Insert(0, (info.Grob, info.OriginEngraver.Context));
        }
    }

    /// <summary>Creates the stubs for the contexts each span bar crosses.</summary>
    public override void ProcessAcknowledged()
    {
        if (_spanbars.Count == 0)
        {
            return;
        }

        if (_axisGroups.Count == 0)
        {
            Warn.ProgrammingError(
                "At least one vertical axis group needs to be created "
                + "in the first time step.");
            return;
        }

        Grob verticalAlignment
            = SpanBarVerticalOrder.GetRootVerticalAlignment(_axisGroups[0].AxisGroup);
        if (verticalAlignment == null)
        {
            // we are at the beginning of a score, so no need for stubs
            return;
        }

        foreach (Grob spanbar in _spanbars)
        {
            IReadOnlyList<Grob> bars = PointerGroupInterface.ExtractGrobSet(
                spanbar, ElementsSymbol);
            List<int> barAxisIndices = new List<int>();
            foreach (Grob bar in bars)
            {
                int i = SpanBarVerticalOrder.GetVerticalAxisGroupIndex(bar);
                if (i >= 0)
                {
                    barAxisIndices.Add(i);
                }
            }

            List<Context> affectedContexts = new List<Context>();
            List<Grob> yParents = new List<Grob>();
            List<bool> keepExtent = new List<bool>();
            foreach ((Grob AxisGroup, Context Owner) entry in _axisGroups)
            {
                Context c = entry.Owner;
                if (c == null || c.IsRemovable)
                {
                    continue;
                }

                Grob g = entry.AxisGroup;
                if (g == null)
                {
                    continue;
                }

                int j = SpanBarVerticalOrder.GetVerticalAxisGroupIndex(g);
                if (barAxisIndices.Count > 0
                    && j > barAxisIndices[0]
                    && j < barAxisIndices[barAxisIndices.Count - 1]
                    && !barAxisIndices.Contains(j))
                {
                    int k = 0;
                    for (; k < barAxisIndices.Count; k++)
                    {
                        if (barAxisIndices[k] > j)
                        {
                            break;
                        }
                    }

                    k--;

                    if (c.Parent != null)
                    {
                        keepExtent.Add(
                            Epg8Support.ToBool(bars[k].GetProperty(AllowSpanBarSymbol)));
                        yParents.Add(g);
                        affectedContexts.Add(c);
                    }
                }
            }

            for (int j = 0; j < affectedContexts.Count; j++)
            {
                Context ctx = affectedContexts[j];
                Item it = new Item(new GrobPropertyInfo(ctx, SpanBarStubSymbol).Updated());
                it.XParent = spanbar;
                GrobInfo gi = MakeGrobInfo(it, spanbar);

                // Upstream announces INTO the crossed context rather than its own —
                // Engraver::announce_grob (Grob_info, Context *).
                (ctx.Implementation as EngraverGroup)
                    ?.AddGrobToAnnounce(gi, Direction.Positive);

                if (!keepExtent[j])
                {
                    it.Suicide();
                }
            }
        }

        _spanbars.Clear();
    }

    /// <summary>Weeds out the contexts that have gone away.</summary>
    public override void StopTranslationTimestep()
    {
        // remove unused contexts
        _axisGroups.RemoveAll(entry =>
            entry.Owner == null || entry.Owner.IsRemovable || entry.AxisGroup == null);
    }
}

/*
  TODO: detect the top staff (stavesFound), and acknowledge staff-group
  system-start-delims. If we find these, and the top staff is in the
  staff-group, add padding to the bar number.
*/

/// <summary>
/// Creates bar numbers.
/// <para>
/// A bar number may be created at any bar line, subject to the
/// <c>barNumberVisibility</c> callback. By default, it is put on top of all staves and
/// appears only at the left side of the staff. The staves are taken from
/// <c>stavesFound</c>, which is maintained by <c>Staff_collecting_engraver</c>. This
/// engraver usually creates <c>BarNumber</c> grobs, but when <c>centerBarNumbers</c>
/// is true, it makes <c>CenteredBarNumber</c> grobs instead.
/// </para>
/// </summary>
public class BarNumberEngraver : Engraver
{
    private static readonly Symbol CenterBarNumbersSymbol = Symbol.Intern("centerBarNumbers");
    private static readonly Symbol CurrentCommandColumnSymbol
        = Symbol.Intern("currentCommandColumn");

    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol BarNumberVisibilitySymbol
        = Symbol.Intern("barNumberVisibility");

    private static readonly Symbol CurrentBarNumberSymbol = Symbol.Intern("currentBarNumber");
    private static readonly Symbol MeasurePositionSymbol = Symbol.Intern("measurePosition");
    private static readonly Symbol BarNumberFormatterSymbol = Symbol.Intern("barNumberFormatter");
    private static readonly Symbol BarLineInterfaceSymbol = Symbol.Intern("bar-line-interface");
    private static readonly Symbol StavesFoundSymbol = Symbol.Intern("stavesFound");
    private static readonly Symbol SideSupportElementsSymbol
        = Symbol.Intern("side-support-elements");

    private static readonly Symbol BreakVisibilitySymbol = Symbol.Intern("break-visibility");
    private static readonly Symbol AlternativeNumberSymbol = Symbol.Intern("alternativeNumber");
    private static readonly Symbol AlternativeNumberingStyleSymbol
        = Symbol.Intern("alternativeNumberingStyle");

    private static readonly Symbol NumbersWithLettersSymbol
        = Symbol.Intern("numbers-with-letters");

    // A regular bar number.
    private Item _text;

    // A centered bar number.
    private Spanner _span;
    private bool _consideredNumbering;

    // There is no bar line at the beginning of the piece, and a break
    // isn't allowed either, but we want to allow a bar number
    // nevertheless, so initialize to true.
    private bool _sawBarLine = true;

    // Store the value read in process-music in a local member so that
    // we can reuse it in stop-translation-timestep.  This avoids a
    // dependency on engraver order, since the Paper_column_engraver
    // unsets forbidBreak in stop-translation-timestep.
    private bool _breakAllowedNow;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public BarNumberEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Bar_number_engraver";

    /* Allow a bar number if any of these conditions is met:

       - there is a bar line,
       - there is a break point,
       - we are at the start of the piece.

       We must allow bar numbers at breaks without bar lines (created
       with explicit \break or \allowBreak) so that there can be a
       parenthesized bar number at the start of the line.  We won't know
       breaks until way later, so we need to create a bar number now.
       We must also allow bar numbers at bar lines without a break point
       since \noBreak should not influence bar numbers.  However, if we
       did nothing more, \allowBreak wouln't play well with

         \set Score.barNumberVisibility = #all-bar-numbers-visible
         \override Score.BarNumber.break-visibility = #all-visible

       as that will create bar numbers at all allowed break points.  The
       strategy here is that if a bar number was created only because a
       break is allowed and not because of a bar line, its only reason to
       exist is the break, so it shouldn't be printed if the break point
       eventually doesn't end up as a break.  Thus, we set the middle
       component of the resulting bar number's break-visibility to false
       in this specific case.

       In centerBarNumbers mode, the spanner will be automatically broken,
       so there is no need to restart a spanner at a break point without a
       bar line.  Nor should we do it, as it can't be suppressed with
       break-visibility. */

    private void CreateBarNumber(object text)
    {
        if (SchemeUtilities.IsSchemeTrue(GetProperty(CenterBarNumbersSymbol)))
        {
            Grob column = GetProperty(CurrentCommandColumnSymbol) as Grob;
            _span = MakeSpanner("CenteredBarNumber", Nil.Instance);
            _span.SetBound(Direction.Negative, column);
            _span.SetProperty(TextSymbol, text);
        }
        else
        {
            _text = MakeItem("BarNumber", Nil.Instance);
            _text.SetProperty(TextSymbol, text);
        }
    }

    private void ConsiderCreatingBarNumber()
    {
        _consideredNumbering = true;

        // Time to terminate the previous spanner if applicable.
        if (_span != null)
        {
            _span.SetBound(
                Direction.Positive, GetProperty(CurrentCommandColumnSymbol) as Grob);
            AnnounceEndGrob(_span, Nil.Instance);
            _span = null;
        }

        object visibility = GetProperty(BarNumberVisibilitySymbol);
        if (SchemeUtilities.IsProcedure(visibility))
        {
            object bn = GetProperty(CurrentBarNumberSymbol);
            if (SchemeConvert.IsNumber(bn))
            {
                Moment mp = Epg8Support.ToMoment(
                    GetProperty(MeasurePositionSymbol), Moment.Zero);

                if (Epg8Support.ToBool(SchemeUtilities.CallCallback(visibility, bn, mp)))
                {
                    object formatter = GetProperty(BarNumberFormatterSymbol);
                    object formattedText = Nil.Instance;
                    if (SchemeUtilities.IsProcedure(formatter))
                    {
                        formattedText = SchemeUtilities.CallCallback(
                            formatter, bn, mp, GetAltNumber() - 1, Context);
                    }

                    CreateBarNumber(formattedText);
                }
            }
        }
    }

    /// <summary>Considers a bar number wherever a break is currently allowed.</summary>
    public override void ProcessMusic()
    {
        _breakAllowedNow = Context.BreakAllowed(Context);
        if (_breakAllowedNow && IsFalse(GetProperty(CenterBarNumbersSymbol)))
        {
            ConsiderCreatingBarNumber();
        }
    }

    /// <summary>Records that a bar line exists this timestep.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob is Item item && item.HasInterface(BarLineInterfaceSymbol))
        {
            _sawBarLine = true;
        }
    }

    /// <summary>Considers a bar number at a bar line seen after process-music.</summary>
    public override void ProcessAcknowledged()
    {
        if (!_consideredNumbering && _sawBarLine)
        {
            ConsiderCreatingBarNumber();
        }
    }

    /// <summary>Finishes the timestep's bar number.</summary>
    public override void StopTranslationTimestep()
    {
        if (_text != null)
        {
            _text.SetObject(
                SideSupportElementsSymbol,
                Epg8Support.GrobListToGrobArray(GetProperty(StavesFoundSymbol)));

            if (_breakAllowedNow && !_sawBarLine
                && IsFalse(GetProperty(CenterBarNumbersSymbol)))
            {
                object bkVis = _text.GetProperty(BreakVisibilitySymbol);
                object[] vector;
                if (!(bkVis is object[] existing))
                {
                    // The general default for break-visibility is all-visible.
                    vector = new object[] { true, true, true };
                }
                else
                {
                    vector = (object[])existing.Clone();
                }

                vector[1] = false;
                _text.SetProperty(BreakVisibilitySymbol, vector);
            }

            _text = null;
        }

        _consideredNumbering = false;
        _sawBarLine = false;
    }

    private long GetAltNumber()
    {
        long altNum = Epg8Support.ToLong(GetProperty(AlternativeNumberSymbol), 0);

        // TODO: Here we have things baked into C++ that should probably be done in
        // Scheme.  Why not just pass the alternative number to the formatter and let
        // the formatter decide whether to use it?  The formatter could look up the
        // style itself, if necessary; well, it could look up the alternative number
        // too.  The impact on users who might have created their own formatters
        // should be considered before changing this.
        if (altNum > 0
            && !ReferenceEquals(
                GetProperty(AlternativeNumberingStyleSymbol), NumbersWithLettersSymbol))
        {
            altNum = 0;
        }

        return altNum;
    }

    private static bool IsFalse(object value) => value is bool flag && !flag;
}

/// <summary>
/// The vertical-ordering readers <c>Span_bar_engraver</c> and
/// <c>Span_bar_stub_engraver</c> need — upstream these are static members of
/// <c>Grob</c> in lily/grob.cc (<c>get_root_vertical_alignment</c>,
/// <c>get_vertical_axis_group</c>, <c>get_vertical_axis_group_index</c>). They live
/// here because <c>Objects/Grob.cs</c> predates them and this session may not edit
/// it; the divergence is recorded in PORT-COVERAGE. They answer by INTERFACE symbol,
/// so they need no type from EPG7 — until a <c>VerticalAlignment</c> grob exists they
/// simply find none, which is upstream's start-of-score answer too.
/// </summary>
internal static class SpanBarVerticalOrder
{
    private static readonly Symbol AlignInterfaceSymbol = Symbol.Intern("align-interface");
    private static readonly Symbol AxisGroupInterfaceSymbol
        = Symbol.Intern("axis-group-interface");

    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");

    private static Grob GetMaybeRootVerticalAlignment(Grob grob, Grob maybe)
    {
        if (grob == null)
        {
            return maybe;
        }

        if (grob.HasInterface(AlignInterfaceSymbol))
        {
            return GetMaybeRootVerticalAlignment(grob.YParent, grob);
        }

        return GetMaybeRootVerticalAlignment(grob.YParent, maybe);
    }

    /// <summary>Finds the outermost vertical alignment above a grob.</summary>
    /// <param name="grob">The grob to start from.</param>
    /// <returns>The alignment, or <see langword="null"/> when there is none yet.</returns>
    internal static Grob GetRootVerticalAlignment(Grob grob)
        => GetMaybeRootVerticalAlignment(grob, null);

    /// <summary>Finds the vertical axis group a grob belongs to.</summary>
    /// <param name="grob">The grob to start from.</param>
    /// <returns>The axis group, or <see langword="null"/>.</returns>
    internal static Grob GetVerticalAxisGroup(Grob grob)
    {
        if (grob == null)
        {
            return null;
        }

        if (grob.YParent == null)
        {
            return null;
        }

        if (grob.HasInterface(AxisGroupInterfaceSymbol)
            && grob.YParent.HasInterface(AlignInterfaceSymbol))
        {
            return grob;
        }

        return GetVerticalAxisGroup(grob.YParent);
    }

    /// <summary>Finds a grob's position among the alignment's axis groups.</summary>
    /// <param name="grob">The grob to place.</param>
    /// <returns>The index, or -1 when there is no alignment.</returns>
    internal static int GetVerticalAxisGroupIndex(Grob grob)
    {
        Grob alignment = GetRootVerticalAlignment(grob);
        if (alignment == null)
        {
            return -1;
        }

        Grob axisGroup = GetVerticalAxisGroup(grob);
        IReadOnlyList<Grob> elements
            = PointerGroupInterface.ExtractGrobSet(alignment, ElementsSymbol);
        for (int i = 0; i < elements.Count; i++)
        {
            if (ReferenceEquals(elements[i], axisGroup))
            {
                return i;
            }
        }

        Warn.ProgrammingError(
            "could not find this grob's vertical axis group in the vertical alignment");
        return -1;
    }
}
