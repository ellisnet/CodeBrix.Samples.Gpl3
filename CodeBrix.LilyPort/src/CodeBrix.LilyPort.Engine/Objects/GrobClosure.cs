/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2005--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/grob-closure.cc, lily/grob.cc (x_parent_positioning / y_parent_positioning only);

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// The offset-callback chaining helpers of <c>lily/grob-closure.cc</c>, pulled forward
/// ahead of line breaking because the alignment interfaces are their first port-side callers:
/// <c>Side_position_interface::set_axis</c> chains an offset callback and
/// <c>Align_interface::add_element</c> plants a parent-positioning procedure.
/// <para>
/// The composition itself is NOT done here. Upstream reaches
/// <c>grob::compose-function</c> and <c>grob::offset-function</c> in
/// <c>scm/output-lib.scm</c> through <c>lily-imports</c>, and the port looks the same
/// two procedures up by name at the moment of use — the same late-binding rule
/// <c>MusicFunctionSupport</c> records: caching at install time captures whatever was
/// bound before the Scheme layer loaded, which is nothing.
/// </para>
/// </summary>
public static class GrobClosure
{
    private static readonly Symbol XOffsetSymbol = Symbol.Intern("X-offset");
    private static readonly Symbol YOffsetSymbol = Symbol.Intern("Y-offset");
    private static readonly Symbol PositioningDoneSymbol = Symbol.Intern("positioning-done");
    private static readonly Symbol XParentPositioningSymbol
        = Symbol.Intern("ly:grob::x-parent-positioning");

    private static readonly Symbol YParentPositioningSymbol
        = Symbol.Intern("ly:grob::y-parent-positioning");

    private static readonly Symbol ComposeFunctionSymbol
        = Symbol.Intern("grob::compose-function");

    private static readonly Symbol OffsetFunctionSymbol = Symbol.Intern("grob::offset-function");

    /// <summary>Returns the offset property symbol for an axis.</summary>
    /// <param name="axis">The axis.</param>
    /// <returns><c>X-offset</c> or <c>Y-offset</c>.</returns>
    public static Symbol AxisOffsetSymbol(Axis axis)
        => axis == Axis.X ? XOffsetSymbol : YOffsetSymbol;

    /// <summary>
    /// Returns the parent-positioning procedure for an axis: the callback that, read as
    /// an offset, triggers the PARENT's <c>positioning-done</c> and answers zero. It is
    /// what a vertical alignment plants on each of its elements so that reading any
    /// element's offset first runs the whole alignment.
    /// </summary>
    /// <param name="axis">The axis.</param>
    /// <returns>The procedure, or <see langword="null"/> when it is not yet bound.</returns>
    public static object AxisParentPositioning(Axis axis)
        => LilyPondScheme.LookupProcedure(
            axis == Axis.X ? XParentPositioningSymbol : YParentPositioningSymbol);

    /*
      Replace

      (orig-proc GROB)

      by

      (+ (PROC GROB) (orig-proc GROB))
    */

    /// <summary>Adds an offset callback whose answer is ADDED to the existing one's.</summary>
    /// <param name="grob">The grob to modify.</param>
    /// <param name="procedure">The callback to add.</param>
    /// <param name="axis">The axis whose offset is chained.</param>
    public static void AddOffsetCallback(Grob grob, object procedure, Axis axis)
    {
        Symbol symbol = AxisOffsetSymbol(axis);
        object data = grob.GetPropertyData(symbol);
        grob.SetProperty(symbol, Compose(OffsetFunctionSymbol, procedure, data));
    }

    /*
      replace

      (orig-proc GROB)

      by

      (PROC GROB (orig-proc GROB))
    */

    /// <summary>Chains a callback in FRONT of a property's existing one.</summary>
    /// <param name="grob">The grob to modify.</param>
    /// <param name="procedure">The callback to chain, called with the old value.</param>
    /// <param name="symbol">The property to chain on.</param>
    public static void ChainCallback(Grob grob, object procedure, Symbol symbol)
    {
        object data = grob.GetPropertyData(symbol);
        grob.SetProperty(symbol, Compose(ComposeFunctionSymbol, procedure, data));
    }

    /// <summary>Chains a callback in front of an axis's offset callback.</summary>
    /// <param name="grob">The grob to modify.</param>
    /// <param name="procedure">The callback to chain.</param>
    /// <param name="axis">The axis whose offset is chained.</param>
    public static void ChainOffsetCallback(Grob grob, object procedure, Axis axis)
        => ChainCallback(grob, procedure, AxisOffsetSymbol(axis));

    /// <summary>
    /// The <c>ly:grob::x-parent-positioning</c> callback: trigger the X parent's
    /// <c>positioning-done</c>, then contribute nothing to the offset — the
    /// positioning has already moved this grob through
    /// <see cref="Grob.TranslateAxis"/>.
    /// </summary>
    /// <param name="grob">The grob whose parent positions it.</param>
    /// <returns>Zero.</returns>
    public static double XParentPositioning(Grob grob)
    {
        Grob parent = grob.GetParent(Axis.X);
        parent?.GetProperty(PositioningDoneSymbol);
        return 0.0;
    }

    /// <summary>
    /// The <c>ly:grob::y-parent-positioning</c> callback: trigger the Y parent's
    /// <c>positioning-done</c>, then contribute nothing to the offset.
    /// </summary>
    /// <param name="grob">The grob whose parent positions it.</param>
    /// <returns>Zero.</returns>
    public static double YParentPositioning(Grob grob)
    {
        Grob parent = grob.GetParent(Axis.Y);
        parent?.GetProperty(PositioningDoneSymbol);
        return 0.0;
    }

    private static object Compose(Symbol composer, object procedure, object data)
    {
        object composeProcedure = LilyPondScheme.LookupProcedure(composer);
        CodeBrix.LilyScheme.Interpreter interpreter = LilyPondScheme.Current;
        if (composeProcedure == null || interpreter == null)
        {
            // Without the Scheme layer there is nothing to compose WITH; the newest
            // callback simply replaces the old datum, which is what a fixture that
            // never loaded output-lib.scm can meaningfully get.
            Warn.ProgrammingError(
                "grob callback composition requested before scm/output-lib.scm loaded");
            return procedure;
        }

        return interpreter.Evaluator.Apply(composeProcedure, new object[] { procedure, data });
    }
}
