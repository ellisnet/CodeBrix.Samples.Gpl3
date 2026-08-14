/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1996--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

  keyplacement by Mats Bengtsson

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

using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Translation;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/key-signature-interface.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// A group of accidentals, to be printed as signature sign.
/// </summary>
public static class KeySignatureInterface
{
    private static readonly Symbol C0PositionSymbol = Symbol.Intern("c0-position");
    private static readonly Symbol KeyCancellationInterfaceSymbol
        = Symbol.Intern("key-cancellation-interface");

    private static readonly Symbol PaddingPairsSymbol = Symbol.Intern("padding-pairs");
    private static readonly Symbol PaddingSymbol = Symbol.Intern("padding");
    private static readonly Symbol AlterationGlyphNameAlistSymbol
        = Symbol.Intern("alteration-glyph-name-alist");

    private static readonly Symbol AlterationAlistSymbol = Symbol.Intern("alteration-alist");
    private static readonly Symbol AlterationPositionsProcSymbol
        = Symbol.Intern("key-signature-interface::alteration-positions");

    /*
      TODO
      - space the `natural' signs wider
    */

    /// <summary>
    /// The <c>stencil</c> callback: stacks one accidental glyph column per entry of
    /// <c>alteration-alist</c>, right to left, kerning naturals apart.
    /// </summary>
    /// <param name="grob">The key signature (or cancellation) item.</param>
    /// <returns>The stencil.</returns>
    public static Stencil Print(Grob grob)
    {
        double inter = StaffSymbolReferencer.StaffSpace(grob) / 2.0;

        Stencil mol = default;

        object c0s = grob.GetProperty(C0PositionSymbol);

        bool isCancellation = grob.HasInterface(KeyCancellationInterfaceSymbol);

        /*
          SCM lists are stacks, so we work from right to left, ending with
          the cancellation signature.
        */

        // ht intervals for natural glyph kerning
        Interval htRight = Interval.Empty;
        Interval lastHtLeft = Interval.Empty;
        object lastGlyphName = false;
        object paddingPairs = grob.GetProperty(PaddingPairsSymbol);

        FontMetric fm = FontInterface.GetDefaultFont(grob);
        object alist = grob.GetProperty(AlterationGlyphNameAlistSymbol);
        object cursor = grob.GetProperty(AlterationAlistSymbol);
        for (; cursor is Pair pair; cursor = pair.Cdr)
        {
            if (!(pair.Car is Pair entry))
            {
                continue;
            }

            object alt = isCancellation ? (object)0L : entry.Cdr;

            object glyphNameScm = AssocGet(alt, alist);
            if (!(glyphNameScm is MutableString glyphNameString))
            {
                GrobWarning(
                    grob,
                    "No glyph found for alteration: "
                    + TranslatorSchemeHelpers.ToRational(alt, Rational.Zero));
                continue;
            }

            string glyphName = glyphNameString.ToString();

            Stencil acc = fm != null ? fm.FindByName(glyphName) : default;

            if (acc.IsEmpty)
            {
                GrobWarning(grob, "alteration not found");
            }
            else
            {
                htRight = Interval.Empty;
                Stencil column = default;
                object posList = CallAlterationPositions(entry, c0s, grob);
                for (; posList is Pair posPair; posList = posPair.Cdr)
                {
                    int p = (int)TranslatorSchemeHelpers.ToLong(posPair.Car, 0);
                    htRight.AddPoint(2 * p - 6); /* descender */
                    htRight.AddPoint(2 * p + 3); /* upper right corner */
                    column.AddStencil(acc.Translated(new Offset(0, p * inter)));
                }

                /*
                  The natural sign (unlike flat & sharp)
                  has vertical edges on both sides. A little padding is
                  needed to prevent collisions.
                */
                double padding = TranslatorSchemeHelpers.ToDouble(grob.GetProperty(PaddingSymbol), 0.0);
                Pair handle = TranslatorSchemeHelpers.Assoc(
                    new Pair(glyphNameScm, lastGlyphName), paddingPairs);
                if (handle != null)
                {
                    padding = TranslatorSchemeHelpers.ToDouble(handle.Cdr, 0.0);
                }
                else if (glyphName == "accidentals.natural")
                {
                    Interval overlap = Interval.Intersection(htRight, lastHtLeft);
                    if (!overlap.IsEmpty)
                    {
                        padding += overlap.Length != 0.0
                            ? 0.3    /* edges overlap */
                            : 0.15;  /* just touching at the corners */
                    }
                }

                mol.AddAtEdge(Axis.X, Direction.Negative, column, padding);

                /* shift up (change to left side) */
                lastHtLeft = new Interval(htRight.Left + 3, htRight.Right + 3);
                lastGlyphName = glyphNameScm;
            }
        }

        mol.AlignTo(Axis.X, -1.0);

        return mol;
    }

    private static object CallAlterationPositions(Pair entry, object c0s, Grob grob)
    {
        object procedure = LilyPondScheme.LookupProcedure(AlterationPositionsProcSymbol);
        if (procedure == null)
        {
            Warn.ProgrammingError(
                "key-signature-interface::alteration-positions not found");
            return Nil.Instance;
        }

        return SchemeUtilities.CallCallback(procedure, entry, c0s, grob);
    }

    // ly_assoc_get: assoc with equal?, answering the entry's cdr or #f.
    private static object AssocGet(object key, object alist)
    {
        Pair entry = TranslatorSchemeHelpers.Assoc(key, alist);
        return entry != null ? entry.Cdr : false;
    }

    // Grob::warning reports at the grob's ultimate cause when it has one.
    private static void GrobWarning(Grob grob, string message)
    {
        StreamEvent cause = grob?.UltimateEventCause();
        if (cause != null)
        {
            TranslatorSchemeHelpers.EventWarning(cause, message);
        }
        else
        {
            Warn.Warning(message);
        }
    }
}
