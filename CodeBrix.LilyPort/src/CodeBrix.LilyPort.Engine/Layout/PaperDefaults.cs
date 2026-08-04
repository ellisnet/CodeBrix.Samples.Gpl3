// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Layout;

/// <summary>
/// Builds the default output definition — what a score is laid out under when nothing
/// says otherwise.
/// <para>
/// Upstream this arrives from the PARSER: <c>ly/paper-defaults-init.ly</c> is a
/// <c>\paper</c> block, read at startup into <c>$defaultpaper</c>. Track P has not
/// been built, so this reconstructs the subset the engraving path reads, in C#. The
/// derivations are translated from <c>scm/paper.scm</c>'s
/// <c>layout-set-absolute-staff-size-in-module</c> and <c>calc-line-thickness</c>
/// rather than invented, so the numbers are upstream's.
/// </para>
/// <para>
/// DIVERGENCE, recorded in PORT-COVERAGE: this is a STAND-IN, and it stops being one
/// the moment the parser can read the real <c>\paper</c> block. Only the variables the
/// engrave path actually reads are set — the page-layout, titling and vertical-spacing
/// variables are deliberately absent rather than guessed, so a caller that needs one
/// gets an honest "unset" instead of a plausible wrong number.
/// </para>
/// </summary>
public static class PaperDefaults
{
    /// <summary>LilyPond's default staff size, in points.</summary>
    public const double DefaultStaffSize = 20.0;

    /// <summary>
    /// Builds an output definition for a staff size, in points.
    /// </summary>
    /// <param name="staffSize">The staff size in points; 20 is LilyPond's default.</param>
    /// <returns>The output definition.</returns>
    public static OutputDef Create(double staffSize = DefaultStaffSize)
    {
        OutputDef paper = new OutputDef();

        // The unit variables, as ly/paper-defaults-init.ly sets them.
        paper.SetVariable("mm", 1.0);
        paper.SetVariable("cm", 10.0);
        paper.SetVariable("in", 25.4);
        paper.SetVariable("pt", 25.4 / 72.27);
        paper.SetVariable("bp", 25.4 / 72.0);

        SetAbsoluteStaffSize(paper, staffSize * Dimensions.Point);

        paper.SetVariable("property-defaults", PropertyDefaults());

        return paper;
    }

    /// <summary>
    /// Sets the staff-size-derived variables on an output definition.
    /// </summary>
    /// <param name="definition">The definition to write to.</param>
    /// <param name="staffHeight">The staff height, in internal units.</param>
    public static void SetAbsoluteStaffSize(OutputDef definition, double staffHeight)
    {
        //was previously: scm/paper.scm (layout-set-absolute-staff-size-in-module);
        double pt = Dimensions.Point;
        double ss = staffHeight / 4;
        double factor = staffHeight / (20 * pt);

        // Synchronized with the `text-font-size' binding in add-pango-fonts.
        definition.SetVariable("text-font-size", 11 * factor);

        definition.SetVariable("output-scale", ss);
        definition.SetVariable("staff-height", staffHeight);
        definition.SetVariable("staff-space", ss);

        definition.SetVariable("line-thickness", CalcLineThickness(ss, pt));

        //  sync with feta
        definition.SetVariable("blot-diameter", 0.4 * pt);
    }

    /// <summary>
    /// Returns the line thickness for a staff space, interpolated between the two
    /// values the Metafont sources use.
    /// </summary>
    /// <param name="staffSpace">The staff space, in internal units.</param>
    /// <param name="pt">One point, in internal units.</param>
    /// <returns>The line thickness.</returns>
    public static double CalcLineThickness(double staffSpace, double pt)
    {
        //was previously: scm/paper.scm (calc-line-thickness);
        // !! synchronize with feta-params.mf
        double x1 = 4.125 * pt;
        double x0 = 5 * pt;
        double f1 = 0.47 * pt;
        double f0 = 0.50 * pt;

        return ((f1 * (staffSpace - x0)) + (f0 * (x1 - staffSpace))) / (x1 - x0);
    }

    private static object PropertyDefaults()
    {
        // The SVG names, because the headless backend is the SVG one. Upstream picks
        // these same three when (ly:get-option 'backend) is 'svg.
        object fonts = Alist(
            ("music", new MutableString("emmentaler")),
            ("serif", new MutableString("serif")),
            ("sans", new MutableString("sans")),
            ("typewriter", new MutableString("monospace")));

        return Alist(
            ("fonts", fonts),
            ("baseline-skip", 3L),
            ("replacement-alist", Nil.Instance),
            ("word-space", 0.6));
    }

    private static object Alist(params (string Key, object Value)[] entries)
    {
        List<object> pairs = new List<object>(entries.Length);
        foreach ((string Key, object Value) entry in entries)
        {
            pairs.Add(new Pair(Symbol.Intern(entry.Key), entry.Value));
        }

        return Pair.ListFrom(pairs);
    }
}
