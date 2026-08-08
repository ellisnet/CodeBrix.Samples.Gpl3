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

using System;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Fonts;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/note-head.cc, lily/include/note-head.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <summary>
/// A note head, and the choosing of the glyph that draws it.
/// <para>
/// Note head glyphs are named <c>noteheads.[u/d/s][r?]{log}{style}</c>: <c>u</c> and
/// <c>d</c> mean a head drawn for an up or down stem, <c>s</c> means one symmetric
/// enough to serve either. The search order matters — a symmetric glyph is preferred,
/// and only when the font has none is the grob's direction consulted.
/// </para>
/// </summary>
public static class NoteHead
{
    private static readonly Symbol StyleSymbol = Symbol.Intern("style");
    private static readonly Symbol DefaultSymbol = Symbol.Intern("default");
    private static readonly Symbol DurationLogSymbol = Symbol.Intern("duration-log");
    private static readonly Symbol GlyphInfoSymbol = Symbol.Intern("glyph-info");
    private static readonly Symbol GlyphNameSymbol = Symbol.Intern("glyph-name");
    private static readonly Symbol StaffPositionSymbol = Symbol.Intern("staff-position");
    private static readonly Symbol StemSymbol = Symbol.Intern("stem");
    private static readonly Symbol StemAttachmentSymbol = Symbol.Intern("stem-attachment");
    private static readonly Symbol PositioningDoneSymbol = Symbol.Intern("positioning-done");
    private static readonly Symbol SelectHeadGlyphSymbol = Symbol.Intern("select-head-glyph");
    private static readonly Symbol KievanSymbol = Symbol.Intern("kievan");

    /// <summary>
    /// The <c>ly:note-head::select-glyph</c> callback: picks the glyph, and records the
    /// stencil it found alongside the name.
    /// <para>
    /// It returns the NAME but stores <c>(name . stencil)</c> in <c>glyph-info</c>, so
    /// that <c>ly:note-head::print</c> can reuse the stencil instead of looking the
    /// glyph up a second time — and can tell a user override of <c>glyph-name</c> from
    /// its own answer by comparing the two.
    /// </para>
    /// </summary>
    /// <param name="grob">The note head.</param>
    /// <returns>The chosen glyph name.</returns>
    public static object SelectGlyph(Grob grob)
    {
        object styleValue = grob.GetProperty(StyleSymbol);

        if (!(styleValue is Symbol))
        {
            Warn.ProgrammingError("'style property should be a symbol; try 'default.");
            styleValue = DefaultSymbol;
        }

        string style = ((Symbol)styleValue).Name;

        object logValue = grob.GetProperty(DurationLogSymbol);
        int noteHeadLog = SchemeConvert.IsNumber(logValue)
            ? SchemeConvert.ToInt(logValue, "duration-log")
            : 2;
        int log = Math.Min(noteHeadLog, string.Equals(style, "kievan", StringComparison.Ordinal) ? 3 : 2);

        object suffixValue = CallSelectHeadGlyph(styleValue, log);
        string suffix = suffixValue is MutableString text ? text.ToString() : string.Empty;

        FontMetric font = FontInterface.GetDefaultFont(grob);
        if (font == null)
        {
            return new MutableString(string.Empty);
        }

        const string Prefix = "noteheads.";
        string symmetricIndex;
        string directedIndex = string.Empty;

        // First try a symmetric note head.
        string eitherIndex = symmetricIndex = Prefix + "s";
        Stencil result = font.FindByName(eitherIndex + suffix);

        // If there was no symmetric note head, select a directed one
        // according to grob direction.
        if (result.IsEmpty)
        {
            Direction direction = StrictGrobDirection(grob);
            eitherIndex = directedIndex = Prefix + (direction == Direction.Positive ? "u" : "d");
            result = font.FindByName(eitherIndex + suffix);
        }

        // In ancient styles, off-staff line note heads get reduced hole sizes
        // if there's a special glyph for that
        if (style == "mensural" || style == "neomensural" || style == "petrucci"
            || style == "baroque" || style == "kievan")
        {
            object position = grob.GetProperty(StaffPositionSymbol);
            int staffPosition = SchemeConvert.IsNumber(position)
                ? SchemeConvert.ToInt(position, "staff-position")
                : 0;

            if (!StaffSymbolReferencer.OnLine(grob, staffPosition))
            {
                Stencil test = font.FindByName(eitherIndex + "r" + suffix);
                if (!test.IsEmpty)
                {
                    eitherIndex += "r";
                    result = test;
                }
            }
        }

        // In 'kievan' style, beamed 8th notes (fusae) should look like
        // 4th notes (semiminims), cf. Issue 2492
        if (style == "kievan" && noteHeadLog == 3)
        {
            Grob stem = grob.GetObject(StemSymbol) as Grob;
            if (stem?.GetObject(Symbol.Intern("beam")) is Grob)
            {
                result = font.FindByName(eitherIndex + "2kievan");
            }
        }

        eitherIndex += suffix;
        if (result.IsEmpty)
        {
            Warn.Warning(
                "none of note heads `" + symmetricIndex + "' or `" + directedIndex + "' found");
            result = new Stencil(new Box(new Interval(0, 0), new Interval(0, 0)), Nil.Instance);
            eitherIndex = string.Empty;
        }

        MutableString glyphName = new MutableString(eitherIndex);
        grob.SetProperty(GlyphInfoSymbol, new Pair(glyphName, result));
        return glyphName;
    }

    /// <summary>
    /// The <c>X-offset</c> callback. It answers zero, but reading the stem's
    /// <c>positioning-done</c> on the way is load bearing: that is what makes the stem
    /// place itself before the head asks where it is.
    /// </summary>
    /// <param name="grob">The note head.</param>
    /// <returns>Zero.</returns>
    public static double StemXShift(Grob grob)
    {
        if (grob.GetObject(StemSymbol) is Grob stem)
        {
            _ = stem.GetProperty(PositioningDoneSymbol);
        }

        return 0.0;
    }

    /// <summary>
    /// The <c>extra-spacing-height</c> callback: reserves room for the first ledger
    /// line, so a note off the staff does not collide with its neighbours.
    /// </summary>
    /// <param name="grob">The note head.</param>
    /// <returns>The extra height.</returns>
    public static Interval IncludeLedgerLineHeight(Grob grob)
    {
        Grob staff = StaffSymbolReferencer.GetStaffSymbol(grob);

        if (staff != null)
        {
            double ss = StaffSymbol.StaffSpace(staff);
            Interval lines = StaffSymbol.LineSpan(staff) * (ss / 2.0);
            double myPosition = StaffSymbolReferencer.GetPosition(grob) * ss / 2.0;
            Interval myExtent = grob.Extent(grob, Axis.Y) + myPosition;

            // The +1 and -1 come from the fact that we only want to add
            // the interval between the note and the first ledger line, not
            // the whole interval between the note and the staff.
            return new Interval(
                Math.Min(0.0, lines[Direction.Positive] - myExtent[Direction.Negative] + 1),
                Math.Max(0.0, lines[Direction.Negative] - myExtent[Direction.Positive] - 1));
        }

        return new Interval(0, 0);
    }

    /// <summary>
    /// Returns one coordinate of the point a stem attaches to this head at.
    /// </summary>
    /// <param name="grob">The note head.</param>
    /// <param name="axis">Which coordinate.</param>
    /// <returns>The coordinate.</returns>
    public static double StemAttachmentCoordinate(Grob grob, Axis axis)
    {
        object value = grob.GetProperty(StemAttachmentSymbol);
        Offset offset = value is Pair pair
            ? new Offset(ToDouble(pair.Car), ToDouble(pair.Cdr))
            : Offset.Zero;

        return offset[axis];
    }

    /*
      Stem attachment position for a given stem direction. Each component
      is measured in a -1 to 1 scale, so that -1 is the left/bottom edge of
      the note's bounding box and 1 is the right/top edge.
    */

    /// <summary>
    /// Returns where a stem attaches, in a -1 to 1 scale over the head's own bounding
    /// box.
    /// </summary>
    /// <param name="font">The font the head is drawn from.</param>
    /// <param name="key">The glyph name.</param>
    /// <param name="direction">Which side the stem is on.</param>
    /// <returns>The attachment point.</returns>
    public static Offset GetStemAttachment(FontMetric font, string key, Direction direction)
    {
        // Offset is an immutable value type in the port, so the two coordinates are
        // computed and then combined, rather than assigned through an indexer.
        double attachmentX = 0.0;
        double attachmentY = 0.0;

        // TODO: This is a bandage on an inconsistent Font_metric interface.
        // Font_metric::find_by_name() does this automatically but other methods do
        // not.  This affects names of breve, longa, and maxima heads.
        string mangledKey = (key ?? string.Empty).Replace('-', 'M');

        int index = font.NameToIndex(mangledKey);
        if (index == FontMetric.GlyphIndexInvalid)
        {
            return Offset.Zero;
        }

        Box box = font.GetIndexedCharDimensions(index);
        Offset point = font.AttachmentPoint(mangledKey, direction, out bool rotate);

        Interval x = box[Axis.X];
        if (!x.IsEmpty)
        {
            attachmentX = 2 * (point.X - x.Center) / x.Length;
        }

        Interval y = box[Axis.Y];
        if (!y.IsEmpty)
        {
            attachmentY = 2 * (point.Y - y.Center) / y.Length;
        }

        Offset attachment = new Offset(attachmentX, attachmentY);
        return rotate ? -attachment : attachment;
    }

    /// <summary>The <c>stem-attachment</c> callback.</summary>
    /// <param name="grob">The note head.</param>
    /// <returns>The attachment point, as a Scheme pair.</returns>
    public static object CalcStemAttachment(Grob grob)
    {
        Grob stem = grob.GetObject(StemSymbol) as Grob;
        FontMetric font = FontInterface.GetDefaultFont(grob);
        if (font == null)
        {
            return new Pair(0.0, 0.0);
        }

        object key = grob.GetProperty(GlyphNameSymbol);
        string glyphName = key is MutableString text ? text.ToString() : string.Empty;

        Direction direction = GrobDirection(stem);
        if (!direction.IsNonZero)
        {
            direction = Direction.Positive;
        }

        Offset attachment = GetStemAttachment(font, glyphName, direction);
        return new Pair(attachment.X, attachment.Y);
    }

    private static object CallSelectHeadGlyph(object style, int log)
    {
        Variable variable = LilyPondScheme.Current?.CurrentModule?.Lookup(SelectHeadGlyphSymbol);
        if (variable == null || !variable.IsBound)
        {
            Warn.ProgrammingError("select-head-glyph is not defined");
            return new MutableString(string.Empty);
        }

        return SchemeUtilities.CallCallback(variable.GetValue(), style, (long)log);
    }

    // These predate the ported directional-element-interface.cc and now delegate to
    // it: the private strict copy warned with its own text and did NOT store the
    // direction, where upstream's get_strict_grob_direction warns "direction of
    // grob %s must be UP or DOWN; using UP" and SETS the property. Found
    // independently by EPG5 and EPG6, reconciled at Wave A integration 2026-08-07.
    private static Direction GrobDirection(Grob grob)
        => grob == null ? Direction.Center : DirectionalElementInterface.GetGrobDirection(grob);

    private static Direction StrictGrobDirection(Grob grob)
        => DirectionalElementInterface.GetStrictGrobDirection(grob);

    private static double ToDouble(object value)
        => SchemeConvert.IsNumber(value) ? SchemeConvert.ToDouble(value, "stem-attachment") : 0.0;
}
