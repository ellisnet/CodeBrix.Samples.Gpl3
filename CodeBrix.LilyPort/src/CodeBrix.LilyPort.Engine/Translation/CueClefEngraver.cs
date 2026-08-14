/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
                           Mats Bengtsson <matsb@s3.kth.se>
  Copyright (C) 2010--2026 Reinhold Kainhofer <reinhold@kainhofer.com>

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

using System;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/cue-clef-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Determines and sets the reference point for pitches in cued voices: prints a
/// <c>CueClef</c> where the cue notes start and a cancelling <c>CueEndClef</c> — drawn
/// from the MAIN clef's properties — where they stop.
/// <para>
/// The shape is <see cref="ClefEngraver"/>'s with one addition: a non-zero
/// transposition also creates the <c>ClefModifier</c> digit, formatted by the
/// <c>cueClefTranspositionFormatter</c> procedure and aligned by
/// <c>ly:clef-modifier::calc-parent-alignment</c> (<see cref="ClefModifier"/>).
/// Which of the two clefs to make is decided by <c>cueClefGlyph</c>: a string means a
/// cue is in force, anything else means the cue just ended.
/// </para>
/// </summary>
public class CueClefEngraver : Engraver
{
    private static readonly Symbol GlyphSymbol = Symbol.Intern("glyph");
    private static readonly Symbol CueClefSymbol = Symbol.Intern("CueClef");
    private static readonly Symbol CueEndClefSymbol = Symbol.Intern("CueEndClef");
    private static readonly Symbol CueClefGlyphSymbol = Symbol.Intern("cueClefGlyph");
    private static readonly Symbol ClefGlyphSymbol = Symbol.Intern("clefGlyph");
    private static readonly Symbol CueClefPositionSymbol = Symbol.Intern("cueClefPosition");
    private static readonly Symbol ClefPositionSymbol = Symbol.Intern("clefPosition");
    private static readonly Symbol CueClefTranspositionSymbol
        = Symbol.Intern("cueClefTransposition");

    private static readonly Symbol CueClefTranspositionStyleSymbol
        = Symbol.Intern("cueClefTranspositionStyle");

    private static readonly Symbol CueClefTranspositionFormatterSymbol
        = Symbol.Intern("cueClefTranspositionFormatter");

    private static readonly Symbol ClefTranspositionSymbol = Symbol.Intern("clefTransposition");
    private static readonly Symbol ClefTranspositionStyleSymbol
        = Symbol.Intern("clefTranspositionStyle");

    private static readonly Symbol ClefTranspositionFormatterSymbol
        = Symbol.Intern("clefTranspositionFormatter");

    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");
    private static readonly Symbol NonDefaultSymbol = Symbol.Intern("non-default");
    private static readonly Symbol TextSymbol = Symbol.Intern("text");
    private static readonly Symbol DirectionSymbol = Symbol.Intern("direction");
    private static readonly Symbol BreakVisibilitySymbol = Symbol.Intern("break-visibility");
    private static readonly Symbol ExplicitCueClefVisibilitySymbol
        = Symbol.Intern("explicitCueClefVisibility");

    private Item _clef;
    private Item _modifier;

    private object _prevGlyph = Nil.Instance;
    private object _prevCpos = Nil.Instance;
    private object _prevTransposition = Nil.Instance;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public CueClefEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Cue_clef_engraver";

    /// <summary>Gets the clef created this timestep, for tests.</summary>
    public Item Clef => _clef;

    /// <summary>Gets the clef modifier created this timestep, for tests.</summary>
    public Item Modifier => _modifier;

    private void SetGlyph()
    {
        // A revert then a push, for BOTH clef types: the override has to be replaced,
        // not stacked, exactly as ClefEngraver.SetGlyph does for the main clef.
        Symbol basic = CueClefSymbol;
        GrobPropertyInfo.ExecutePushPopProperty(Context, basic, GlyphSymbol, null);
        GrobPropertyInfo.ExecutePushPopProperty(
            Context, basic, GlyphSymbol, GetProperty(CueClefGlyphSymbol));

        basic = CueEndClefSymbol;
        GrobPropertyInfo.ExecutePushPopProperty(Context, basic, GlyphSymbol, null);
        GrobPropertyInfo.ExecutePushPopProperty(
            Context, basic, GlyphSymbol, GetProperty(ClefGlyphSymbol));
    }

    private void CreateClefModifier(object transp, object style, object formatter)
    {
        if (SchemeConvert.IsNumber(transp)
            && SchemeConvert.ToInt(transp, "cueClefTransposition") != 0)
        {
            Item g = MakeItem("ClefModifier", Nil.Instance);

            int absTransp = SchemeConvert.ToInt(transp, "cueClefTransposition");
            int dir = Math.Sign(absTransp);
            absTransp = Math.Abs(absTransp) + 1;

            object txt = new MutableString(
                absTransp.ToString(System.Globalization.CultureInfo.InvariantCulture));

            if (SchemeUtilities.IsProcedure(formatter))
            {
                g.SetProperty(TextSymbol, SchemeUtilities.CallCallback(formatter, txt, style));
            }

            SidePositionInterface.AddSupport(g, _clef);

            g.YParent = _clef;
            g.XParent = _clef;
            g.SetProperty(DirectionSymbol, (long)dir);
            _modifier = g;
        }
    }

    private void CreateClef()
    {
        if (_clef == null)
        {
            Item c = MakeItem("CueClef", Nil.Instance);

            _clef = c;
            object cpos = GetProperty(CueClefPositionSymbol);
            if (SchemeConvert.IsNumber(cpos))
            {
                _clef.SetProperty(StaffPositionSymbol, cpos);
            }

            CreateClefModifier(
                GetProperty(CueClefTranspositionSymbol),
                GetProperty(CueClefTranspositionStyleSymbol),
                GetProperty(CueClefTranspositionFormatterSymbol));
        }
    }

    private void CreateEndClef()
    {
        if (_clef == null)
        {
            _clef = MakeItem("CueEndClef", Nil.Instance);
            object cpos = GetProperty(ClefPositionSymbol);
            if (SchemeConvert.IsNumber(cpos))
            {
                _clef.SetProperty(StaffPositionSymbol, cpos);
            }

            CreateClefModifier(
                GetProperty(ClefTranspositionSymbol),
                GetProperty(ClefTranspositionStyleSymbol),
                GetProperty(ClefTranspositionFormatterSymbol));
        }
    }

    /// <summary>Creates a cue clef when the cue clef properties have changed.</summary>
    public override void ProcessMusic()
    {
        InspectClefProperties();

        // Efficiency: don't create a default clef if it's not going to be
        // visible.  A default clef can only be visible at the start of the
        // line.
        if (GetProperty(CueClefGlyphSymbol) is MutableString && Context.BreakAllowed(Context))
        {
            CreateClef();
        }
    }

    private void InspectClefProperties()
    {
        object glyph = GetProperty(CueClefGlyphSymbol);
        object clefpos = GetProperty(CueClefPositionSymbol);
        object transposition = GetProperty(CueClefTranspositionSymbol);

        if (!SchemeUtilities.IsEqual(glyph, _prevGlyph)
            || !SchemeUtilities.IsEqual(clefpos, _prevCpos)
            || !SchemeUtilities.IsEqual(transposition, _prevTransposition))
        {
            SetGlyph();
            if (glyph is MutableString)
            {
                CreateClef();
                if (_clef != null)
                {
                    _clef.SetProperty(NonDefaultSymbol, true);
                }
            }
            else
            {
                CreateEndClef();
            }

            _prevCpos = clefpos;
            _prevGlyph = glyph;
            _prevTransposition = transposition;
        }
    }

    /// <summary>Applies the explicit visibility and releases the timestep's grobs.</summary>
    public override void StopTranslationTimestep()
    {
        if (_clef != null)
        {
            if (SchemeUtilities.ToBool(_clef.GetProperty(NonDefaultSymbol)))
            {
                object vis = GetProperty(ExplicitCueClefVisibilitySymbol);

                if (vis is object[] visibility)
                {
                    _clef.SetProperty(BreakVisibilitySymbol, visibility);
                }
            }

            _clef = null;
            _modifier = null;
        }
    }
}
