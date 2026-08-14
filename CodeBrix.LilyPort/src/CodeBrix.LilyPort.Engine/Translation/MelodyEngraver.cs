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
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/melody-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - upstream declares acknowledge_stem and acknowledge_slur separately and lets the
//     macro layer dispatch by interface; the port's single AcknowledgeGrob writes both
//     interface tests out.

/// <summary>
/// Collects the stems of a melodic phrase into <c>MelodyItem</c> spans, which is what lets
/// a run of stems that have no direction of their own agree on one.
/// <para>
/// A span ends at a slur or at a bar line, because those are the places a melodic line is
/// allowed to change direction without looking wrong. The bar line is found by reading
/// <c>currentBarLine</c> rather than by acknowledging one, since the Bar_engraver lives in
/// Staff context and this engraver — which must see exactly one stem at a time — lives in
/// Voice.
/// </para>
/// </summary>
public class MelodyEngraver : Engraver
{
    private static readonly Symbol CurrentBarLineSymbol = Symbol.Intern("currentBarLine");
    private static readonly Symbol RestsSymbol = Symbol.Intern("rests");
    private static readonly Symbol SlurInterfaceSymbol = Symbol.Intern("slur-interface");
    private static readonly Symbol StemInterfaceSymbol = Symbol.Intern("stem-interface");
    private static readonly Symbol SuspendMelodyDecisionsSymbol
        = Symbol.Intern("suspendMelodyDecisions");

    private Grob _melodyItem;
    private Grob _nextMelodyItem;

    // This engraver is designed to operate in Voice context, so we expect only
    // one stem.
    private Grob _stem;
    private bool _breakMelody = true;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public MelodyEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Melody_engraver";

    /// <summary>Notes a stem to span, or a slur that ends the current span.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        Grob grob = info.Grob;

        if (grob.HasInterface(SlurInterfaceSymbol))
        {
            _breakMelody = true;
            return;
        }

        if (!grob.HasInterface(StemInterfaceSymbol))
        {
            return;
        }

        if (!SchemeUtilities.IsSchemeTrue(GetProperty(SuspendMelodyDecisionsSymbol)))
        {
            IReadOnlyList<Grob> rests = PointerGroupInterface.ExtractGrobSet(grob, RestsSymbol);
            if (rests.Count == 0)
            {
                _stem = grob;

                // We don't necessarily know yet whether we will need to place this
                // stem in a new melody span.  Create a next MelodyItem now because
                // creating grobs in stop_translation_timestep () isn't allowed.
                if (_nextMelodyItem == null)
                {
                    _nextMelodyItem = MakeItem("MelodyItem", grob);
                }
            }
            else
            {
                _breakMelody = true;
            }
        }
    }

    /// <summary>Opens a new span when one is due, then adds the stem to the current one.</summary>
    public override void StopTranslationTimestep()
    {
        if (_stem != null)
        {
            // If we don't already know a reason to start a new melody span, check
            // whether there is a bar line.  We can't use acknowledge_bar_line () for
            // this because the Bar_engraver operates in Staff context, so this
            // engraver can't observe its grobs.
            if (!_breakMelody)
            {
                _breakMelody = GetProperty(CurrentBarLineSymbol) is Grob;
            }

            if (_breakMelody)
            {
                _breakMelody = false;

                _melodyItem = _nextMelodyItem;
                _nextMelodyItem = null;
            }

            MelodySpanner.AddStem(_melodyItem, _stem);
            _stem = null;
        }
    }
}
