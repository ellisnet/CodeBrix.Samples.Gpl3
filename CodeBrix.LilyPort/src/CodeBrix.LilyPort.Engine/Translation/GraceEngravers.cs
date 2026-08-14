/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2004--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>       (grace-engraver.cc)
  Copyright (C) 2006--2026 Han-Wen <hanwen@lilypond.org>              (grace-spacing-engraver.cc)

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
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/grace-engraver.cc, lily/grace-spacing-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - Grace_engraver registers its GraceChange listener on the context's EVENT SOURCE,
//     by hand in Initialize and removed in FinalizeTranslation, exactly as upstream
//     does. It deliberately does NOT use Translator.ListenTo, which registers on
//     EventsBelow: that would additionally hear GraceChange announced by a DESCENDANT
//     context's grace iterator, and switching this context into grace mode on a
//     descendant's grace is not what upstream does.

/// <summary>
/// Sets font size and other properties for grace notes, by pushing the
/// <c>graceSettings</c> overrides on entering grace time and popping exactly those on
/// leaving it.
/// </summary>
public class GraceEngraver : Engraver
{
    private static readonly Symbol GraceChangeSymbol = Symbol.Intern("GraceChange");
    private static readonly Symbol GraceSettingsSymbol = Symbol.Intern("graceSettings");

    // Each entry records where a push happened so the matching pop can be found again:
    // the context, the grob name, and the token push returned.
    private readonly List<GracePush> _graceSettings = new List<GracePush>();

    private Moment _lastMoment = -Moment.Infinity;
    private Listener _graceChangeListener;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public GraceEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Grace_engraver";

    /// <summary>Enters grace mode if already in grace time, then starts listening.</summary>
    public override void Initialize()
    {
        base.Initialize();

        // If we are in grace time already on initialization, it is unlikely that we'll
        // receive a GraceChange event from the grace iterator yet, so we want to start
        // into grace mode anyway. The downside is that this will get us confused when
        // given something like
        //
        //   \new Voice { \oneVoice \grace { c''8 8 } g'1 }
        //
        // where \grace executes its actions already before \oneVoice, causing different
        // stem directions.
        ConsiderChangeGraceSettings();

        if (Context != null)
        {
            _graceChangeListener = Context.EventSource.AddListener(
                this, GraceChange, GraceChangeSymbol);
        }
    }

    /// <summary>Stops listening.</summary>
    public override void FinalizeTranslation()
    {
        if (Context != null && _graceChangeListener != null)
        {
            Context.EventSource.RemoveListener(_graceChangeListener, GraceChangeSymbol);
            _graceChangeListener = null;
        }

        base.FinalizeTranslation();
    }

    /// <summary>Catches up when the grace iterator has moved to another context.</summary>
    public override void ProcessMusic()
    {
        // If the grace iterator has moved off to some other context, we might not get to
        // see the ChangeContext event. In that case, we still want to change into or out
        // of grace mode settings as appropriate — in particular in order to get out of
        // grace mode again.
        if (_lastMoment != NowMoment)
        {
            ConsiderChangeGraceSettings();
        }
    }

    // The iterator should usually come before process_music.
    private void GraceChange(StreamEvent streamEvent) => ConsiderChangeGraceSettings();

    private void ConsiderChangeGraceSettings()
    {
        Moment now = NowMoment;

        if (!now.GracePart.IsNonZero)
        {
            foreach (GracePush push in _graceSettings)
            {
                new GrobPropertyInfo(push.Context, push.GrobName).MatchedPop(push.Cell);
            }

            _graceSettings.Clear();
        }
        else if (!_lastMoment.GracePart.IsNonZero)
        {
            object settings = GetProperty(GraceSettingsSymbol);

            _graceSettings.Clear();
            for (object cursor = settings; cursor is Pair pair; cursor = pair.Cdr)
            {
                if (!(pair.Car is Pair entry))
                {
                    continue;
                }

                // (context-name grob symbol value)
                object contextName = entry.Car;
                if (!(entry.Cdr is Pair rest1)
                    || !(rest1.Cdr is Pair rest2)
                    || !(rest2.Cdr is Pair rest3))
                {
                    continue;
                }

                object grob = rest1.Car;
                object sym = rest2.Car;
                object val = rest3.Car;

                if (!(sym is Pair))
                {
                    sym = new Pair(sym, Nil.Instance);
                }

                Context c = contextName is Symbol name
                    ? Context?.FindContextAbove(name)
                    : null;

                if (c != null && grob is Symbol grobName)
                {
                    object cell = new GrobPropertyInfo(c, grobName).Push(sym, val);
                    _graceSettings.Insert(0, new GracePush(c, grobName, cell));
                }
                else
                {
                    Warn.ProgrammingError(
                        "cannot find context from graceSettings: "
                        + SchemeUtilities.RobustSymbolToString(contextName, "?"));
                }
            }
        }

        _lastMoment = now;
    }

    private readonly struct GracePush
    {
        public GracePush(Context context, Symbol grobName, object cell)
        {
            Context = context;
            GrobName = grobName;
            Cell = cell;
        }

        public Context Context { get; }

        public Symbol GrobName { get; }

        public object Cell { get; }
    }
}

/// <summary>
/// Bookkeeping of shortest starting and playing notes in grace note runs: it makes one
/// <c>GraceSpacing</c> spanner per run and hands it every musical column the run covers.
/// </summary>
public class GraceSpacingEngraver : Engraver
{
    private static readonly Symbol ColumnsSymbol = Symbol.Intern("columns");
    private static readonly Symbol CurrentMusicalColumnSymbol
        = Symbol.Intern("currentMusicalColumn");

    private static readonly Symbol GraceSpacingSymbol = Symbol.Intern("grace-spacing");

    private Moment _lastMoment;
    private Spanner _graceSpacing;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public GraceSpacingEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Grace_spacing_engraver";

    /// <summary>Opens a spacing spanner at the start of a grace run and feeds it columns.</summary>
    public override void ProcessMusic()
    {
        Moment now = NowMoment;
        if (!_lastMoment.GracePart.IsNonZero && now.GracePart.IsNonZero)
        {
            _graceSpacing = MakeSpanner("GraceSpacing", Nil.Instance);
        }

        if (_graceSpacing != null && (now.GracePart.IsNonZero || _lastMoment.GracePart.IsNonZero))
        {
            if (GetProperty(CurrentMusicalColumnSymbol) is Item column)
            {
                PointerGroupInterface.AddGrob(_graceSpacing, ColumnsSymbol, column);

                column.SetObject(GraceSpacingSymbol, _graceSpacing);

                if (_graceSpacing.GetBound(Direction.Negative) == null)
                {
                    _graceSpacing.SetBound(Direction.Negative, column);
                }
                else
                {
                    _graceSpacing.SetBound(Direction.Positive, column);
                }
            }
        }
    }

    /// <summary>Closes the spanner once grace time is over.</summary>
    public override void StopTranslationTimestep()
    {
        _lastMoment = NowMoment;

        if (!_lastMoment.GracePart.IsNonZero)
        {
            _graceSpacing = null;
        }
    }
}
