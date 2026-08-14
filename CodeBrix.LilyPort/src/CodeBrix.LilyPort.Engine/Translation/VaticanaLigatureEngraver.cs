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
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/vaticana-ligature-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port:
//   - align_heads returns the ligature width, which upstream computes and then DISCARDS at
//     its single call site. The return is kept because the value is the function's whole
//     result and dropping it would hide that; nothing reads it, exactly as upstream.
//   - glyph names are MutableStrings on the Scheme side, so the C# side keeps them as
//     plain strings and converts once at each set_property.

/*
 * This class implements the notation specific aspects of Vaticana
 * style ligatures for Gregorian chant notation.
 */

/*
 * TODO (upstream): Maybe move handling of dots/mora to
 * Gregorian_ligature_engraver?  It's probably common for all types of
 * Gregorian chant notation that have dotted notes.
 *
 * FIXME (upstream): The horizontal alignment of the mora column is bad (too far
 * to the left), if the last dotted note is not the last primitive in
 * the ligature.
 */

/// <summary>
/// Glues special ligature heads together in the Vaticana style: it chooses each head's
/// GLYPH from the head's prefixes and its neighbours, lines the heads up into one column,
/// and collects the dotted heads' morae into a single dot column behind the ligature.
/// </summary>
public sealed class VaticanaLigatureEngraver : GregorianLigatureEngraver
{
    private static readonly Symbol AddCaudaSymbol = Symbol.Intern("add-cauda");
    private static readonly Symbol AddJoinSymbol = Symbol.Intern("add-join");
    private static readonly Symbol AddStemSymbol = Symbol.Intern("add-stem");
    private static readonly Symbol ContextInfoSymbol = Symbol.Intern("context-info");
    private static readonly Symbol DeltaPositionSymbol = Symbol.Intern("delta-position");
    private static readonly Symbol DotCountSymbol = Symbol.Intern("dot-count");
    private static readonly Symbol DotSymbol = Symbol.Intern("dot");
    private static readonly Symbol FlexaHeightSymbol = Symbol.Intern("flexa-height");
    private static readonly Symbol FlexaWidthSymbol = Symbol.Intern("flexa-width");
    private static readonly Symbol GlyphNameSymbol = Symbol.Intern("glyph-name");
    private static readonly Symbol HeadXOffsetSymbol = Symbol.Intern("head-x-offset");
    private static readonly Symbol LineThicknessSymbol = Symbol.Intern("line-thickness");
    private static readonly Symbol PitchSymbol = Symbol.Intern("pitch");
    private static readonly Symbol PrefixSetSymbol = Symbol.Intern("prefix-set");
    private static readonly Symbol ThicknessSymbol = Symbol.Intern("thickness");

    private const string VaticanaPunctum = "vaticana.punctum";

    private readonly List<Item> _augmentedPrimitives = new List<Item>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public VaticanaLigatureEngraver(Context context)
        : base(context)
    {
        BrewLigaturePrimitiveProc
            = LookupBrewProc("ly:vaticana-ligature::brew-ligature-primitive");
        _augmentedPrimitives.Clear();
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Vaticana_ligature_engraver";

    /// <summary>Makes the <c>VaticanaLigature</c> spanner.</summary>
    /// <returns>The ligature spanner.</returns>
    protected override Spanner CreateLigatureSpanner()
        => MakeSpanner("VaticanaLigature", Nil.Instance);

    /// <summary>
    /// Chooses each head's glyph, lines the heads up, and appends the ligature's morae.
    /// </summary>
    /// <param name="ligature">The ligature spanner.</param>
    /// <param name="primitives">The heads, in time order.</param>
    protected override void TransformHeads(Spanner ligature, IReadOnlyList<Item> primitives)
    {
        double flexaWidth = SchemeConvert.ToDouble(ligature.GetProperty(FlexaWidthSymbol), 2);

        double thickness = SchemeConvert.ToDouble(ligature.GetProperty(ThicknessSymbol), 1);

        Item prevPrimitive = null;
        int prevPrefixSet = 0;
        int prevContextInfo = 0;
        int prevDeltaPitch = 0;
        string prevGlyphName = string.Empty;
        _augmentedPrimitives.Clear();
        for (int i = 0; i < primitives.Count; i++)
        {
            Item primitive = primitives[i];

            object deltaPitchScm = primitive.GetProperty(DeltaPositionSymbol);
            if (deltaPitchScm is Nil)
            {
                primitive.ProgrammingError(
                    "Vaticana_ligature: delta-position undefined -> ignoring grob");
                continue;
            }

            int deltaPitch = SchemeConvert.ToInt(deltaPitchScm, 0);

            /* retrieve & complete prefix_set and context_info */
            int prefixSet = SchemeConvert.ToInt(primitive.GetProperty(PrefixSetSymbol), 0);
            int contextInfo = SchemeConvert.ToInt(primitive.GetProperty(ContextInfoSymbol), 0);

            if (RhythmicHead.DotCount(primitive) > 0)
            {
                // remove dots from primitive and add remember primitive for
                // creating a dot column
                RhythmicHead.GetDots(primitive)?.SetProperty(
                    DotCountSymbol, SchemeConvert.FromInt(0));

                // TODO (upstream): Maybe completely remove grob "Dots" rather than
                // setting property "dot-count" to 0.
                CheckForAmbiguousDotPitch(primitive);
                _augmentedPrimitives.Add(primitive);
            }
            else if (_augmentedPrimitives.Count > 0)
            {
                primitive.Warning(
                    "This ligature has a dotted head followed by a non-dotted head."
                    + "  The ligature should be split after the last dotted head before"
                    + " this head.");
            }

            if (IsStackedHead(prefixSet, contextInfo))
            {
                contextInfo |= VaticanaLigature.StackedHead;
                primitive.SetProperty(ContextInfoSymbol, SchemeConvert.FromInt(contextInfo));
            }

            /*
             * Now determine which head to typeset (this is context sensitive
             * information, since it depends on neighbouring heads; therefore,
             * this decision must be made here in the engraver rather than in
             * the backend).
             */
            string glyphName;
            if ((prefixSet & GregorianLigature.Virga) != 0)
            {
                glyphName = VaticanaPunctum;
                primitive.SetProperty(AddStemSymbol, true);
            }
            else if ((prefixSet & GregorianLigature.Quilisma) != 0)
            {
                glyphName = "vaticana.quilisma";
            }
            else if ((prefixSet & GregorianLigature.Oriscus) != 0)
            {
                glyphName = "solesmes.oriscus";
            }
            else if ((prefixSet & GregorianLigature.Stropha) != 0)
            {
                glyphName = (prefixSet & GregorianLigature.Auctum) != 0
                    ? "solesmes.stropha.aucta"
                    : "solesmes.stropha";
            }
            else if ((prefixSet & GregorianLigature.Inclinatum) != 0)
            {
                if ((prefixSet & GregorianLigature.Auctum) != 0)
                {
                    glyphName = "solesmes.incl.auctum";
                }
                else if ((prefixSet & GregorianLigature.Deminutum) != 0)
                {
                    glyphName = "solesmes.incl.parvum";
                }
                else
                {
                    glyphName = "vaticana.inclinatum";
                }
            }
            else if ((prefixSet & GregorianLigature.Deminutum) != 0)
            {
                glyphName = string.Empty;
                if (i == 0)
                {
                    // initio debilis
                    glyphName = "vaticana.reverse.plica";
                }
                else if (prevDeltaPitch > 0)
                {
                    // epiphonus
                    if ((prevContextInfo & GregorianLigature.FlexaRight) == 0)
                    {
                        /* correct head of previous primitive */
                        prevGlyphName = prevDeltaPitch > 1
                            ? "vaticana.epiphonus"
                            : "vaticana.vepiphonus";
                    }

                    glyphName = prevDeltaPitch > 1 ? "vaticana.plica" : "vaticana.vplica";
                }
                else if (prevDeltaPitch < 0)
                {
                    // cephalicus
                    if ((prevContextInfo & GregorianLigature.FlexaRight) == 0)
                    {
                        /* correct head of previous primitive */
                        prevGlyphName = i > 1
                            /* cephalicus head with fixed size cauda */
                            ? "vaticana.inner.cephalicus"

                            /* cephalicus head without cauda */
                            : "vaticana.cephalicus";

                        /*
                         * Flexa has no variable size cauda if its left head is
                         * stacked on the right head.  This is true for
                         * cephalicus.  Hence, remove the cauda.
                         *
                         * Urgh: for the current implementation, this rule only
                         * applies for cephalicus; but it is a fundamental rule.
                         * Therefore, the following line of code should be
                         * placed somewhere else.
                         */
                        prevPrimitive.SetProperty(AddCaudaSymbol, false);
                    }

                    glyphName = prevDeltaPitch < -1
                        ? "vaticana.reverse.plica"
                        : "vaticana.reverse.vplica";
                }
                else // (prev_delta_pitch == 0)
                {
                    primitive.ProgrammingError(
                        "Vaticana_ligature: deminutum head must have different"
                        + " pitch -> ignoring grob");
                }
            }
            else if ((prefixSet & (GregorianLigature.Cavum | GregorianLigature.Linea)) != 0)
            {
                if ((prefixSet & GregorianLigature.Cavum) != 0
                    && (prefixSet & GregorianLigature.Linea) != 0)
                {
                    glyphName = "vaticana.linea.punctum.cavum";
                }
                else if ((prefixSet & GregorianLigature.Cavum) != 0)
                {
                    glyphName = "vaticana.punctum.cavum";
                }
                else
                {
                    glyphName = "vaticana.linea.punctum";
                }
            }
            else if ((prefixSet & GregorianLigature.Auctum) != 0)
            {
                glyphName = (prefixSet & GregorianLigature.Ascendens) != 0
                    ? "solesmes.auct.asc"
                    : "solesmes.auct.desc";
            }
            else if ((contextInfo & VaticanaLigature.StackedHead) != 0
                && (contextInfo & GregorianLigature.PesUpper) != 0)
            {
                glyphName = prevDeltaPitch > 1 ? "vaticana.upes" : "vaticana.vupes";
            }
            else
            {
                glyphName = VaticanaPunctum;
            }

            /*
             * This head needs a cauda, if it starts a flexa, is not the upper
             * head of a pes, and if it is a punctum.
             */
            if ((contextInfo & GregorianLigature.FlexaLeft) != 0
                && (contextInfo & GregorianLigature.PesUpper) == 0
                && glyphName == VaticanaPunctum)
            {
                primitive.SetProperty(AddCaudaSymbol, true);
            }

            /*
             * Execptional rule for porrectus:
             *
             * If the current head is preceded by a \flexa and succeded by a
             * \pes (e.g. "a \flexa g \pes a"), then join the current head and
             * the previous head into a single curved flexa shape.
             */
            if ((contextInfo & GregorianLigature.FlexaRight) != 0
                && (contextInfo & GregorianLigature.PesLower) != 0)
            {
                CheckForPrefixLoss(prevPrimitive);
                prevGlyphName = "flexa";
                prevPrimitive.SetProperty(
                    FlexaHeightSymbol, SchemeConvert.FromInt(prevDeltaPitch));
                prevPrimitive.SetProperty(FlexaWidthSymbol, flexaWidth);
                bool addCauda = (prevPrefixSet & GregorianLigature.PesOrFlexa) == 0;
                prevPrimitive.SetProperty(AddCaudaSymbol, addCauda);
                CheckForPrefixLoss(primitive);
                glyphName = string.Empty;
                primitive.SetProperty(FlexaWidthSymbol, flexaWidth);
            }

            /*
             * Exceptional rule for pes:
             *
             * If this head is stacked on the previous one due to a \pes, then
             * set the glyph of the previous head to that for this special
             * case, thereby avoiding potential vertical collision with the
             * current head.
             */
            if ((prefixSet & GregorianLigature.PesOrFlexa) != 0
                && (contextInfo & GregorianLigature.PesUpper) != 0
                && (contextInfo & VaticanaLigature.StackedHead) != 0
                && prevGlyphName == VaticanaPunctum)
            {
                prevGlyphName = prevDeltaPitch > 1 ? "vaticana.lpes" : "vaticana.vlpes";
            }

            if (prevPrimitive != null)
            {
                prevPrimitive.SetProperty(GlyphNameSymbol, new MutableString(prevGlyphName));
            }

            /*
             * In the backend, flexa shapes and joins need to know about line
             * thickness.  Hence, for simplicity, let's distribute the
             * ligature grob's value for thickness to each ligature head (even
             * if not all of them need to know).
             */
            primitive.SetProperty(ThicknessSymbol, thickness);

            prevPrimitive = primitive;
            prevPrefixSet = prefixSet;
            prevContextInfo = contextInfo;
            prevDeltaPitch = deltaPitch;
            prevGlyphName = glyphName;
        }

        prevPrimitive.SetProperty(GlyphNameSymbol, new MutableString(prevGlyphName));

        AlignHeads(primitives, flexaWidth, thickness);

        // append all dots to paper column of ligature's last head
        AddMoraColumn(prevPrimitive.GetColumn());
    }

    private static bool IsStackedHead(int prefixSet, int contextInfo)
    {
        // upper head of pes is stacked upon lower head of pes ...
        bool isStacked = (contextInfo & GregorianLigature.PesUpper) != 0;

        // ... unless this note starts a flexa
        if ((contextInfo & GregorianLigature.FlexaLeft) != 0)
        {
            isStacked = false;
        }

        // ... or another pes
        if ((contextInfo & GregorianLigature.PesLower) != 0)
        {
            isStacked = false;
        }

        // ... or the previous note is a semivocalis or inclinatum
        if ((contextInfo & GregorianLigature.AfterDeminutum) != 0)
        {
            isStacked = false;
        }

        // auctum head is never stacked upon preceding note
        if ((prefixSet & GregorianLigature.Auctum) != 0)
        {
            isStacked = false;
        }

        // virga is never stacked upon preceding note
        if ((prefixSet & GregorianLigature.Virga) != 0)
        {
            isStacked = false;
        }

        // oriscus is never stacked upon preceding note
        if ((prefixSet & GregorianLigature.Oriscus) != 0)
        {
            isStacked = false;
        }

        if ((prefixSet & GregorianLigature.Deminutum) != 0
            && (prefixSet & GregorianLigature.Inclinatum) == 0
            && (contextInfo & GregorianLigature.FlexaRight) != 0)
        {
            isStacked = true; // semivocalis head of deminutus form
        }

        return isStacked;
    }

    /*
     * When aligning the heads, sometimes extra space is needed, e.g. to
     * avoid clashing with the appendix of an adjacent notehead or with an
     * adjacent notehead itself if it has the same pitch.  Extra space is
     * added at most once between to heads.
     */
    private static bool NeedExtraHorizontalSpace(
        int prevPrefixSet, int prefixSet, int contextInfo, int deltaPitch)
    {
        if ((prevPrefixSet & GregorianLigature.Virga) != 0)
        {
            /*
             * After a virga, make an additional small space such that the
             * appendix on the right side of the head does not touch the
             * following head.
             */
            return true;
        }

        if ((prefixSet & GregorianLigature.Inclinatum) != 0
            && (prevPrefixSet & GregorianLigature.Inclinatum) == 0)
        {
            /*
             * Always start a series of inclinatum heads with an extra space.
             */
            return true;
        }

        if ((contextInfo & GregorianLigature.FlexaLeft) != 0
            && (contextInfo & GregorianLigature.PesUpper) == 0)
        {
            /*
             * Before a flexa (but not within a torculus), make an
             * additional small space such that the appendix on the left side
             * of the flexa does not touch the this head.
             */
            return true;
        }

        if (deltaPitch == 0)
        {
            /*
             * If there are two adjacent noteheads with the same pitch, add
             * additional small space between them, such that they do not
             * touch each other.
             */
            return true;
        }

        return false;
    }

    private static double AlignHeads(
        IReadOnlyList<Item> primitives, double flexaWidth, double thickness)
    {
        if (primitives.Count == 0)
        {
            Warn.ProgrammingError("Vaticana_ligature: empty ligature [ignored]");
            return 0.0;
        }

        /*
         * The paper column where we put the whole ligature into.
         */
        PaperColumn column = primitives[0].GetColumn();

        double joinThickness = thickness * column.Layout.GetDimension(LineThicknessSymbol);

        /*
         * Amount of extra space two put between some particular
         * configurations of adjacent heads.
         *
         * TODO (upstream): make this a property of primtive grobs.
         */
        double extraSpace = 4.0 * joinThickness;

        /*
         * Keep track of the total width of the ligature.
         */
        double ligatureWidth = 0.0;

        Item prevPrimitive = null;
        int prevPrefixSet = 0;
        foreach (Item primitive in primitives)
        {
            int prefixSet = SchemeConvert.ToInt(primitive.GetProperty(PrefixSetSymbol), 0);
            int contextInfo = SchemeConvert.ToInt(primitive.GetProperty(ContextInfoSymbol), 0);

            /*
             * Get glyph_name, delta_pitch and context_info for this head.
             */
            object glyphNameScm = primitive.GetProperty(GlyphNameSymbol);
            if (glyphNameScm is Nil)
            {
                primitive.ProgrammingError(
                    "Vaticana_ligature: undefined glyph-name -> ignoring grob");
                continue;
            }

            string glyphName
                = glyphNameScm is MutableString text ? text.ToString() : string.Empty;

            int deltaPitch = 0;
            if (prevPrimitive != null) /* urgh, need prev_primitive only here */
            {
                object deltaPitchScm = prevPrimitive.GetProperty(DeltaPositionSymbol);
                if (!(deltaPitchScm is Nil))
                {
                    deltaPitch = SchemeConvert.ToInt(deltaPitchScm, 0);
                }
                else
                {
                    primitive.ProgrammingError(
                        "Vaticana_ligature: delta-position undefined -> ignoring grob");
                    continue;
                }
            }

            /*
             * Now determine width and x-offset of head.
             */
            double headWidth;
            double headXOffset;

            if ((contextInfo & VaticanaLigature.StackedHead) != 0)
            {
                /*
                 * This head is stacked upon the previous one; hence, it
                 * does not contribute to the total width of the ligature,
                 * and its width is assumed to be 0.0.  Moreover, it is
                 * shifted to the left by its width such that the right side
                 * of this and the other head are horizontally aligned.
                 */
                headWidth = 0.0;
                headXOffset = joinThickness
                    - FontInterface.GetDefaultFont(primitive)
                        .FindByName("noteheads.s" + glyphName)
                        .Extent(Axis.X)
                        .Length;
            }
            else if (glyphName == "flexa" || glyphName.Length == 0)
            {
                /*
                 * This head represents either half of a flexa shape.
                 * Hence, it is assigned half the width of this shape.
                 */
                headWidth = 0.5 * flexaWidth;
                headXOffset = 0.0;
            }
            else
            {
                /*
                 * This is a regular head, placed right to the previous one.
                 * Retrieve its width from corresponding font.
                 */
                headWidth = FontInterface.GetDefaultFont(primitive)
                    .FindByName("noteheads.s" + glyphName)
                    .Extent(Axis.X)
                    .Length;

                headXOffset = 0.0;
            }

            /*
             * Save the head's final x-offset.
             */
            primitive.SetProperty(HeadXOffsetSymbol, headXOffset);

            /*
             * If the head is the 2nd head of a pes or flexa (but not a
             * flexa shape), mark this head to be joined with the left-side
             * neighbour head (i.e. the previous head) by a vertical beam.
             */
            if ((contextInfo & GregorianLigature.PesUpper) != 0
                || ((contextInfo & GregorianLigature.FlexaRight) != 0
                    && (contextInfo & GregorianLigature.PesLower) == 0))
            {
                if (prevPrimitive == null)
                {
                    primitive.ProgrammingError(
                        "Vaticana ligature: add-join: missing previous primitive");
                }
                else
                {
                    prevPrimitive.SetProperty(AddJoinSymbol, true);

                    /*
                     * Create a small overlap of adjacent heads so that the join
                     * can be drawn perfectly between them.
                     */
                    ligatureWidth -= joinThickness;
                }
            }

            // Upstream's `else if (glyph_name == "")' branch here is EMPTY -- it exists
            // only to carry the comment that the 2nd (virtual) head of a flexa shape is
            // deliberately joined tightly, with no additional space, so that the next head
            // is not off from the flexa shape. There is nothing to port.
            if (NeedExtraHorizontalSpace(prevPrefixSet, prefixSet, contextInfo, deltaPitch))
            {
                ligatureWidth += extraSpace;
            }

            /*
             * Horizontally line-up this head to form a ligature.
             */
            MoveRelatedItemsToColumn(primitive, column, ligatureWidth);
            ligatureWidth += headWidth;

            prevPrimitive = primitive;
            prevPrefixSet = prefixSet;
        }

        /*
         * Add extra horizontal padding space after ligature, such that
         * neighbouring ligatures do not touch each other.
         */
        ligatureWidth += extraSpace;

        return ligatureWidth;
    }

    /*
     * Depending on the typographical features of a particular ligature
     * style, some prefixes may be ignored.  In particular, if a curved
     * flexa shape is produced, any prefixes to either of the two
     * contributing heads that would select a head other than punctum, is
     * by definition ignored.
     */
    private static void CheckForPrefixLoss(Item primitive)
    {
        int prefixSet = SchemeConvert.ToInt(primitive.GetProperty(PrefixSetSymbol), 0);
        if ((prefixSet & ~GregorianLigature.PesOrFlexa) != 0)
        {
            string prefs = GregorianLigature.PrefixesToStr(primitive);
            primitive.Warning(
                "ignored prefix(es) `" + prefs + "' of this head"
                + " according to restrictions of the selected ligature style");
        }
    }

    private void AddMoraColumn(PaperColumn column)
    {
        if (_augmentedPrimitives.Count == 0) // no dot for column
        {
            return;
        }

        if (column == null) // empty ligature???
        {
            _augmentedPrimitives[0].ProgrammingError("no paper column to add dot");
            return;
        }

        Item dotcol = MakeItem("DotColumn", Nil.Instance);
        dotcol.XParent = column;
        foreach (Item primitive in _augmentedPrimitives)
        {
            Item dot = MakeItem("Dots", primitive);
            dot.SetProperty(DotCountSymbol, SchemeConvert.FromInt(1));
            dot.YParent = primitive;
            primitive.SetObject(DotSymbol, dot);
            DotColumn.AddHead(dotcol, primitive);

            // FIXME (upstream): why isn't the dot picked up by Paper_column_engraver?
            SeparationItem.AddItem(column, dot);
        }
    }

    /*
     * This function prints a warning, if the given primitive has the same
     * pitch as at least one of the primitives already stored in the
     * augmented_primitives_ array.
     *
     * The rationale of this check is, that, if there are two dotted
     * primitives with the same pitch, then collecting all dots in a dot
     * column behind the ligature leads to a notational ambiguity of to
     * which head the corresponding dot refers.
     */
    private void CheckForAmbiguousDotPitch(Item primitive)
    {
        // TODO (upstream): Fix performance, which is currently O (n^2) but could be
        // O (n); n is typically small (n<10), though.
        int newPitch = PitchStepsOf(primitive);
        for (int i = 0; i < _augmentedPrimitives.Count; i++)
        {
            int pitch = PitchStepsOf(_augmentedPrimitives[i]);
            if (pitch == newPitch)
            {
                primitive.Warning(
                    "Ambiguous use of dots in ligature: there are"
                    + " multiple dotted notes with the same pitch."
                    + "  The ligature should be split.");
                return; // supress multiple identical warnings
            }
        }
    }

    private static int PitchStepsOf(Item primitive)
    {
        StreamEvent cause = primitive.EventCause();
        return cause?.GetProperty(PitchSymbol) is Pitch pitch ? pitch.Steps() : 0;
    }
}
