/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2004--2026 Jan Nieuwenhuizen <janneke@gnu.org>

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

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/paper-system.cc, lily/include/paper-system.hh;

// Modified by Jeremy Ellis on 2026-08-05 as part of the CodeBrix port.

/// <summary>
/// One laid-out line of music, as the page breaker and the backends see it: a
/// <see cref="Prob"/> of type <c>paper-system</c> carrying a stencil and the
/// permissions that decide where a page may end.
/// <para>
/// It is deliberately NOT a grob. Once a system has been broken into lines and its
/// stencils are final, the page breaker needs a value it can move about the page
/// freely; keeping the grob would drag the whole reference-point graph along with it.
/// </para>
/// </summary>
public static class PaperSystem
{
    private static readonly Symbol PaperSystemSymbol = Symbol.Intern("paper-system");
    private static readonly Symbol YExtentSymbol = Symbol.Intern("Y-extent");
    private static readonly Symbol StencilSymbol = Symbol.Intern("stencil");
    private static readonly Symbol DelayStencilEvaluationSymbol
        = Symbol.Intern("delay-stencil-evaluation");

    private static readonly Symbol CombineStencilSymbol = Symbol.Intern("combine-stencil");
    private static readonly Symbol TranslateStencilSymbol = Symbol.Intern("translate-stencil");
    private static readonly Symbol FootnoteSymbol = Symbol.Intern("footnote");

    /// <summary>Makes a paper system with the given immutable properties.</summary>
    /// <param name="immutableInit">The immutable property alist.</param>
    /// <returns>The paper system.</returns>
    public static Prob Make(object immutableInit)
        => new Prob(PaperSystemSymbol, immutableInit ?? Nil.Instance);

    /// <summary>Determines whether a value is a paper system.</summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> when it is.</returns>
    public static bool Is(object value)
        => value is Prob prob && ReferenceEquals(prob.Type, PaperSystemSymbol);

    /// <summary>
    /// Sets a paper system's stencil, keeping an already-declared <c>Y-extent</c>.
    /// <para>
    /// The override matters: a system's vertical extent is decided by the STAVES on it,
    /// not by whatever happens to stick out of the drawn stencil, and letting the
    /// stencil's own box win would make every page-spacing decision follow the tallest
    /// accidental.
    /// </para>
    /// </summary>
    /// <param name="prob">The paper system.</param>
    /// <param name="stencil">The stencil to set.</param>
    public static void SetStencil(Prob prob, Stencil stencil)
    {
        object yext = prob.GetProperty(YExtentSymbol);

        if (yext is Pair pair
            && SchemeConvert.IsNumber(pair.Car)
            && SchemeConvert.IsNumber(pair.Cdr))
        {
            Box box = stencil.ExtentBox;
            box[Axis.Y] = new Interval(
                SchemeConvert.ToDouble(pair.Car, "Y-extent"),
                SchemeConvert.ToDouble(pair.Cdr, "Y-extent"));

            stencil = new Stencil(box, stencil.Expression);
        }

        prob.SetProperty(StencilSymbol, stencil);
    }

    /// <summary>
    /// Reads a paper system's stencil back out.
    /// <para>
    /// It exists so a caller that has already built the paper systems can draw them
    /// without asking the system grob for its stencil a SECOND time — that path runs
    /// <c>PostProcessing</c>, which translates the system, so a second call moves the
    /// music twice. Added by EPG15 (2026-08-08).
    /// </para>
    /// </summary>
    /// <param name="prob">The paper system.</param>
    /// <returns>The stencil, or the empty one when the property holds none.</returns>
    public static Stencil GetStencil(Prob prob)
        => prob?.GetProperty(StencilSymbol) is Stencil stencil ? stencil : Stencil.Empty;

    /// <summary>
    /// Pulls every footnote out of a stencil expression, walking the same three
    /// combining heads a stencil is ever built from.
    /// </summary>
    /// <param name="expression">The stencil expression.</param>
    /// <returns>The footnotes, as a list.</returns>
    public static object GetFootnotes(object expression)
    {
        if (!(expression is Pair pair))
        {
            return Nil.Instance;
        }

        object head = pair.Car;

        if (ReferenceEquals(head, DelayStencilEvaluationSymbol))
        {
            // we likely need to do something here...just don't know what...
            return Nil.Instance;
        }

        if (ReferenceEquals(head, CombineStencilSymbol))
        {
            List<object> all = new List<object>();
            for (object x = pair.Cdr; x is Pair item; x = item.Cdr)
            {
                object footnote = GetFootnotes(item.Car);
                for (object f = footnote; f is Pair entry; f = entry.Cdr)
                {
                    all.Add(entry.Car);
                }
            }

            return Pair.ListFrom(all);
        }

        if (ReferenceEquals(head, TranslateStencilSymbol))
        {
            return pair.Cdr is Pair second && second.Cdr is Pair third
                ? GetFootnotes(third.Car)
                : Nil.Instance;
        }

        if (ReferenceEquals(head, FootnoteSymbol))
        {
            return Pair.List(pair.Cdr);
        }

        return Nil.Instance;
    }
}
