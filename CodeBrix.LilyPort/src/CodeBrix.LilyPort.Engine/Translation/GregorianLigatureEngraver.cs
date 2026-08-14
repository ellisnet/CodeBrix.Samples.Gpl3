/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2003--2026 Juergen Reuter <reuter@ipd.uka.de>

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

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/gregorian-ligature-engraver.cc, lily/include/gregorian-ligature-engraver.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - upstream's free functions fix_prefix, fix_prefix_set, check_and_fix_all_prefixes and
//     provide_context_info have no header declarations and no callers outside this file,
//     so they are private statics.
//   - ⚠ fix_prefix_set's LAST line passes LINEA where it names "pes_or_flexa". That is
//     upstream's own text, and it is REPRODUCED, not corrected: the port's job here is
//     parity, and "fixing" it would change which warnings a Gregorian score emits.
//   - upstream's stop_translation_timestep override calls its base and does nothing else,
//     which is what NOT overriding it already does; it is not carried over.

/*
 * This abstract class is the common superclass for all ligature
 * engravers for Gregorian chant notation.  It cares for the musical
 * handling of the neumes, such as checking for valid combinations of
 * neumes and providing context information.  Notational aspects such
 * as the glyphs to use or calculating the total width of a ligature,
 * are left to the concrete subclass.  Currently, there is only a
 * single subclass, Vaticana_ligature_engraver.
 */

/// <summary>
/// The common superclass of the Gregorian-chant ligature engravers: it handles the
/// MUSICAL side of the neumes — checking that a head's prefixes are a combination the
/// notation allows, and deriving each head's context information from its neighbours —
/// and leaves the notational side to a concrete style.
/// </summary>
public abstract class GregorianLigatureEngraver : CoherentLigatureEngraver
{
    private static readonly Symbol AscendensSymbol = Symbol.Intern("ascendens");
    private static readonly Symbol AuctumSymbol = Symbol.Intern("auctum");
    private static readonly Symbol CavumSymbol = Symbol.Intern("cavum");
    private static readonly Symbol ContextInfoSymbol = Symbol.Intern("context-info");
    private static readonly Symbol DeminutumSymbol = Symbol.Intern("deminutum");
    private static readonly Symbol DescendensSymbol = Symbol.Intern("descendens");
    private static readonly Symbol InclinatumSymbol = Symbol.Intern("inclinatum");
    private static readonly Symbol LineaSymbol = Symbol.Intern("linea");
    private static readonly Symbol OriscusSymbol = Symbol.Intern("oriscus");
    private static readonly Symbol PesOrFlexaSymbol = Symbol.Intern("pes-or-flexa");
    private static readonly Symbol PitchSymbol = Symbol.Intern("pitch");
    private static readonly Symbol PrefixSetSymbol = Symbol.Intern("prefix-set");
    private static readonly Symbol QuilismaSymbol = Symbol.Intern("quilisma");
    private static readonly Symbol StrophaSymbol = Symbol.Intern("stropha");
    private static readonly Symbol VirgaSymbol = Symbol.Intern("virga");

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    protected GregorianLigatureEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Gregorian_ligature_engraver";

    /// <summary>
    /// Applies the style-INDEPENDENT checking and transformation, then hands over to the
    /// concrete style's line-up.
    /// </summary>
    /// <param name="ligature">The ligature spanner.</param>
    /// <param name="primitives">The heads, in time order.</param>
    protected override void BuildLigature(Spanner ligature, IReadOnlyList<Item> primitives)
    {
        // apply style-independent checking and transformation
        CheckAndFixAllPrefixes(primitives);
        ProvideContextInfo(primitives);

        // apply style-specific transformation (including line-up); to be
        // implemented by subclass
        TransformHeads(ligature, primitives);
    }

    /// <summary>Chooses the glyphs and lines the heads up. A concrete style provides it.</summary>
    /// <param name="ligature">The ligature spanner.</param>
    /// <param name="primitives">The heads, in time order.</param>
    protected abstract void TransformHeads(Spanner ligature, IReadOnlyList<Item> primitives);

    private static void FixPrefix(
        string name, int mask, ref int currentSet, int minSet, int maxSet, Grob primitive)
    {
        bool current = (currentSet & mask) != 0;
        bool min = (minSet & mask) != 0;
        bool max = (maxSet & mask) != 0;
        if (!max && min)
        {
            Warn.ProgrammingError("min_set > max_set");
            return;
        }

        if (min && !current)
        {
            primitive.Warning("\\" + name + " ignored");
            currentSet &= ~mask;
        }

        if (!max && current)
        {
            primitive.Warning("implied \\" + name + " added");
            currentSet |= mask;
        }
    }

    private static void FixPrefixSet(ref int currentSet, int minSet, int maxSet, Grob primitive)
    {
        FixPrefix("virga", GregorianLigature.Virga, ref currentSet, minSet, maxSet, primitive);
        FixPrefix("stropha", GregorianLigature.Stropha, ref currentSet, minSet, maxSet, primitive);
        FixPrefix(
            "inclinatum", GregorianLigature.Inclinatum, ref currentSet, minSet, maxSet, primitive);
        FixPrefix("auctum", GregorianLigature.Auctum, ref currentSet, minSet, maxSet, primitive);
        FixPrefix(
            "descendens", GregorianLigature.Descendens, ref currentSet, minSet, maxSet, primitive);
        FixPrefix(
            "ascendens", GregorianLigature.Ascendens, ref currentSet, minSet, maxSet, primitive);
        FixPrefix("oriscus", GregorianLigature.Oriscus, ref currentSet, minSet, maxSet, primitive);
        FixPrefix(
            "quilisma", GregorianLigature.Quilisma, ref currentSet, minSet, maxSet, primitive);
        FixPrefix(
            "deminutum", GregorianLigature.Deminutum, ref currentSet, minSet, maxSet, primitive);
        FixPrefix("cavum", GregorianLigature.Cavum, ref currentSet, minSet, maxSet, primitive);
        FixPrefix("linea", GregorianLigature.Linea, ref currentSet, minSet, maxSet, primitive);

        // ⚠ upstream passes LINEA under the name "pes_or_flexa". Reproduced verbatim; see
        // the note at the top of this file.
        FixPrefix(
            "pes_or_flexa", GregorianLigature.Linea, ref currentSet, minSet, maxSet, primitive);
    }

    private static void CheckAndFixAllPrefixes(IReadOnlyList<Item> primitives)
    {
        /* Check for invalid head modifier combinations */
        foreach (Item primitive in primitives)
        {
            /* compute head prefix set by inspecting primitive grob properties */
            int prefixSet
                = (GregorianLigature.Virga * Flag(primitive, VirgaSymbol))
                | (GregorianLigature.Stropha * Flag(primitive, StrophaSymbol))
                | (GregorianLigature.Inclinatum * Flag(primitive, InclinatumSymbol))
                | (GregorianLigature.Auctum * Flag(primitive, AuctumSymbol))
                | (GregorianLigature.Descendens * Flag(primitive, DescendensSymbol))
                | (GregorianLigature.Ascendens * Flag(primitive, AscendensSymbol))
                | (GregorianLigature.Oriscus * Flag(primitive, OriscusSymbol))
                | (GregorianLigature.Quilisma * Flag(primitive, QuilismaSymbol))
                | (GregorianLigature.Deminutum * Flag(primitive, DeminutumSymbol))
                | (GregorianLigature.Cavum * Flag(primitive, CavumSymbol))
                | (GregorianLigature.Linea * Flag(primitive, LineaSymbol))
                | (GregorianLigature.PesOrFlexa * Flag(primitive, PesOrFlexaSymbol));

            /* check: ascendens and descendens exclude each other; same with
               auctum and deminutum */
            if ((prefixSet & GregorianLigature.Descendens) != 0)
            {
                FixPrefixSet(
                    ref prefixSet,
                    prefixSet & ~GregorianLigature.Ascendens,
                    prefixSet & ~GregorianLigature.Ascendens,
                    primitive);
            }

            if ((prefixSet & GregorianLigature.Auctum) != 0)
            {
                FixPrefixSet(
                    ref prefixSet,
                    prefixSet & ~GregorianLigature.Deminutum,
                    prefixSet & ~GregorianLigature.Deminutum,
                    primitive);
            }

            /* check: virga, quilisma and oriscus cannot be combined with any
               other prefix, but may be part of a pes or flexa */
            if ((prefixSet & GregorianLigature.Virga) != 0)
            {
                FixPrefixSet(
                    ref prefixSet,
                    GregorianLigature.Virga,
                    GregorianLigature.Virga | GregorianLigature.PesOrFlexa,
                    primitive);
            }

            if ((prefixSet & GregorianLigature.Quilisma) != 0)
            {
                FixPrefixSet(
                    ref prefixSet,
                    GregorianLigature.Quilisma,
                    GregorianLigature.Quilisma | GregorianLigature.PesOrFlexa,
                    primitive);
            }

            if ((prefixSet & GregorianLigature.Oriscus) != 0)
            {
                FixPrefixSet(
                    ref prefixSet,
                    GregorianLigature.Oriscus,
                    GregorianLigature.Oriscus | GregorianLigature.PesOrFlexa,
                    primitive);
            }

            /* check: auctum is the only valid optional prefix for stropha */
            if ((prefixSet & GregorianLigature.Stropha) != 0)
            {
                FixPrefixSet(
                    ref prefixSet,
                    GregorianLigature.Stropha,
                    GregorianLigature.Stropha | GregorianLigature.Auctum,
                    primitive);
            }

            /* check: inclinatum may be prefixed with auctum or deminutum only */
            if ((prefixSet & GregorianLigature.Inclinatum) != 0)
            {
                FixPrefixSet(
                    ref prefixSet,
                    GregorianLigature.Inclinatum,
                    GregorianLigature.Inclinatum | GregorianLigature.Auctum
                        | GregorianLigature.Deminutum,
                    primitive);
            }

            /* check: semivocalis (deminutum but not inclinatum) must occur in
               combination with and only with pes or flexa */
            else if ((prefixSet & GregorianLigature.Deminutum) != 0)
            {
                FixPrefixSet(
                    ref prefixSet,
                    GregorianLigature.Deminutum | GregorianLigature.PesOrFlexa,
                    GregorianLigature.Deminutum | GregorianLigature.PesOrFlexa,
                    primitive);
            }

            /* check: cavum and linea (either or both) may be applied only
               upon core punctum */
            if ((prefixSet & (GregorianLigature.Cavum | GregorianLigature.Linea)) != 0)
            {
                FixPrefixSet(
                    ref prefixSet,
                    0,
                    GregorianLigature.Cavum | GregorianLigature.Linea,
                    primitive);
            }

            /* all other combinations should be valid (unless I made a
               mistake) */

            primitive.SetProperty(PrefixSetSymbol, SchemeConvert.FromInt(prefixSet));
        }
    }

    /*
     * Marks those heads that participate in a pes or flexa.
     */
    private static void ProvideContextInfo(IReadOnlyList<Item> primitives)
    {
        Grob prevPrimitive = null;
        int prevPrefixSet = 0;
        int prevContextInfo = 0;
        int prevPitch = 0;
        for (int i = 0; i < primitives.Count; i++)
        {
            Item primitive = primitives[i];
            StreamEvent eventCause = primitive.EventCause();
            int contextInfo = 0;
            int pitch = eventCause?.GetProperty(PitchSymbol) is Pitch p ? p.Steps() : 0;
            int prefixSet = SchemeConvert.ToInt(primitive.GetProperty(PrefixSetSymbol), 0);

            if ((prefixSet & GregorianLigature.PesOrFlexa) != 0)
            {
                if (i == 0)
                {
                    // ligature may not start with 2nd head of pes or flexa
                    primitive.Warning("cannot apply `\\~' on first head of ligature");
                }
                else if (pitch > prevPitch) // pes
                {
                    prevContextInfo |= GregorianLigature.PesLower;
                    contextInfo |= GregorianLigature.PesUpper;
                }
                else if (pitch < prevPitch) // flexa
                {
                    prevContextInfo |= GregorianLigature.FlexaLeft;
                    contextInfo |= GregorianLigature.FlexaRight;
                }
                else // (pitch == prev_pitch)
                {
                    primitive.Warning("cannot apply `\\~' on heads with identical pitch");
                }
            }

            if ((prevPrefixSet & GregorianLigature.Deminutum) != 0)
            {
                contextInfo |= GregorianLigature.AfterDeminutum;
            }

            if (prevPrimitive != null)
            {
                prevPrimitive.SetProperty(
                    ContextInfoSymbol, SchemeConvert.FromInt(prevContextInfo));
            }

            prevPrimitive = primitive;
            prevPrefixSet = prefixSet;
            prevContextInfo = contextInfo;
            prevPitch = pitch;
        }

        if (prevPrimitive != null)
        {
            prevPrimitive.SetProperty(ContextInfoSymbol, SchemeConvert.FromInt(prevContextInfo));
        }
    }

    // from_scm<bool> is EXACTLY-#t, not Scheme truth -- see lily-guile.hh's
    // scm_conversions<bool>. The multiplication upstream writes needs it as 0 or 1.
    private static int Flag(Grob primitive, Symbol property)
        => SchemeUtilities.ToBool(primitive.GetProperty(property)) ? 1 : 0;
}
