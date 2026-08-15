/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2010--2026 Joe Neeman <joeneeman@gmail.com>

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
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/keep-alive-together-engraver.cc;

/// <summary>
/// Ties a group of removable staves together, so one is removed only if all are.
/// <para>
/// A <c>StaffGroup</c> that uses this engraver keeps every staff in the group visible as
/// long as there is a note in at least one of them — which is what a reader expects of a
/// bracketed group, and what <c>\RemoveEmptyStaves</c> alone would not give.
/// </para>
/// <para>
/// The relationships are written into <c>keep-alive-with</c> and <c>make-dead-when</c>,
/// which <see cref="Objects.HaraKiriGroupSpanner"/> then consults. <c>remove-layer</c>
/// decides how: a symbol names a positional rule (<c>any</c>, <c>above</c>, <c>below</c>),
/// an INTEGER makes the staff part of a priority ladder where only the lowest-numbered
/// non-empty layer survives, and <c>#f</c> opts out of the mechanism entirely.
/// </para>
/// <para>
/// The asymmetry between the two lists is deliberate: an UNSET layer is kept alive by any
/// layer that is not opted out, while an EXPLICIT layer is only affected by other explicit
/// layers.
/// </para>
/// </summary>
public class KeepAliveTogetherEngraver : Engraver
{
    private static readonly Symbol RemoveLayerSymbol = Symbol.Intern("remove-layer");
    private static readonly Symbol KeepAliveWithSymbol = Symbol.Intern("keep-alive-with");
    private static readonly Symbol MakeDeadWhenSymbol = Symbol.Intern("make-dead-when");
    private static readonly Symbol AnySymbol = Symbol.Intern("any");
    private static readonly Symbol AboveSymbol = Symbol.Intern("above");
    private static readonly Symbol BelowSymbol = Symbol.Intern("below");
    private static readonly Symbol HaraKiriGroupSpannerInterfaceSymbol
        = Symbol.Intern("hara-kiri-group-spanner-interface");

    private readonly List<Grob> _groupSpanners = new List<Grob>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public KeepAliveTogetherEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Keep_alive_together_engraver";

    /// <summary>Collects each removable group spanner created at or below this context.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob != null && info.Grob.HasInterface(HaraKiriGroupSpannerInterfaceSymbol))
        {
            _groupSpanners.Add(info.Grob);
        }
    }

    /// <summary>Writes the keep-alive relationships once every group spanner is known.</summary>
    public override void FinalizeTranslation()
    {
        for (int i = 0; i < _groupSpanners.Count; ++i)
        {
            object thisLayer = _groupSpanners[i].GetProperty(RemoveLayerSymbol);

            // Upstream guards with scm_is_false, NOT from_scm<bool>: remove-layer is a
            // key? property, so its real values are integers and the symbols any/above/
            // below, and an unset one reads '(). Exactly-#t is not even a documented
            // value, so `!ToBool` skipped EVERY spanner and this engraver had never
            // written keep-alive-with or make-dead-when at all. Trap 14's second half,
            // the same shape as D1.
            //was previously: if (!SchemeUtilities.ToBool(thisLayer))
            if (!SchemeUtilities.IsSchemeTrue(thisLayer))
            {
                continue;
            }

            GrobArray live = new GrobArray();
            GrobArray dead = new GrobArray();

            for (int j = 0; j < _groupSpanners.Count; ++j)
            {
                if (i == j)
                {
                    continue;
                }

                if (thisLayer is Symbol layerSymbol)
                {
                    if (ReferenceEquals(layerSymbol, AnySymbol))
                    {
                        // layer is kept alive by any other layer
                        live.Add(_groupSpanners[j]);
                        continue;
                    }

                    if (ReferenceEquals(layerSymbol, AboveSymbol))
                    {
                        // layer is kept alive by the layer preceding it
                        if (i == j + 1)
                        {
                            live.Add(_groupSpanners[j]);
                        }

                        continue;
                    }

                    if (ReferenceEquals(layerSymbol, BelowSymbol))
                    {
                        // layer is kept alive by the layer following it
                        if (i == j - 1)
                        {
                            live.Add(_groupSpanners[j]);
                        }

                        continue;
                    }

                    _groupSpanners[i].Warning(
                        "unknown remove-layer value `" + layerSymbol.Name + "'");
                    continue;
                }

                object thatLayer = _groupSpanners[j].GetProperty(RemoveLayerSymbol);

                //was previously: if (!SchemeUtilities.ToBool(thatLayer))
                if (!SchemeUtilities.IsSchemeTrue(thatLayer))
                {
                    continue;
                }

                if (!IsInteger(thisLayer))
                {
                    // unset layers are kept alive by all but ignored layers
                    live.Add(_groupSpanners[j]);
                    continue;
                }

                // an explicit layer is only affected by explicit layers
                if (!IsInteger(thatLayer))
                {
                    continue;
                }

                double thisValue = SchemeConvert.ToDouble(thisLayer, "remove-layer");
                double thatValue = SchemeConvert.ToDouble(thatLayer, "remove-layer");
                if (thatValue == thisValue)
                {
                    live.Add(_groupSpanners[j]);
                }
                else if (thatValue < thisValue)
                {
                    dead.Add(_groupSpanners[j]);
                }
            }

            if (live.Count > 0)
            {
                _groupSpanners[i].SetObject(KeepAliveWithSymbol, live);
            }

            if (dead.Count > 0)
            {
                _groupSpanners[i].SetObject(MakeDeadWhenSymbol, dead);
            }
        }
    }

    private static bool IsInteger(object value) => value is long || value is int;
}
