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

using System.Collections.Generic;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/stencil-interpret.cc, lily/include/stencil-interpret.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.
// Moved from CodeBrix.LilyPort.Backends: the interpreter is engine
// machinery (upstream lily/), and the output pipeline's PaperOutputter needs it below the
// backends in the project graph.

/// <summary>
/// Receives the flattened drawing commands a stencil expression unfolds into.
/// </summary>
public interface IStencilSink
{
    /// <summary>Handles one drawing command.</summary>
    /// <param name="expression">
    /// The command, as a Scheme list whose head names a backend procedure.
    /// </param>
    /// <returns>
    /// <see langword="false"/> when the command was not understood. The interpreter
    /// uses that answer for one thing only: a <c>utf-8-string</c> a backend cannot
    /// render falls back to the stencil stored as its fourth element.
    /// </returns>
    object Output(object expression);
}

/// <summary>
/// Walks a stencil expression and flattens it into a stream of drawing commands.
/// <para>
/// The tree carries structure the backends have no interest in — nested translations,
/// combinations, colour and rotation scopes. This turns it into a flat sequence in
/// which every drawing command is preceded by an explicit
/// <c>settranslation</c> and followed by a <c>resettranslation</c>, so a backend never
/// has to track a transform stack of its own.
/// </para>
/// </summary>
public static class StencilInterpreter
{
    private static readonly Symbol DelayStencilEvaluation = Symbol.Intern("delay-stencil-evaluation");
    private static readonly Symbol Footnote = Symbol.Intern("footnote");
    private static readonly Symbol TranslateStencil = Symbol.Intern("translate-stencil");
    private static readonly Symbol CombineStencil = Symbol.Intern("combine-stencil");
    private static readonly Symbol GrobCause = Symbol.Intern("grob-cause");
    private static readonly Symbol NoOrigin = Symbol.Intern("no-origin");
    private static readonly Symbol Color = Symbol.Intern("color");
    private static readonly Symbol SetColor = Symbol.Intern("setcolor");
    private static readonly Symbol ResetColor = Symbol.Intern("resetcolor");
    private static readonly Symbol OutputAttributes = Symbol.Intern("output-attributes");
    private static readonly Symbol StartGroupNode = Symbol.Intern("start-group-node");
    private static readonly Symbol EndGroupNode = Symbol.Intern("end-group-node");
    private static readonly Symbol RotateStencil = Symbol.Intern("rotate-stencil");
    private static readonly Symbol SetRotation = Symbol.Intern("setrotation");
    private static readonly Symbol ResetRotation = Symbol.Intern("resetrotation");
    private static readonly Symbol ScaleStencil = Symbol.Intern("scale-stencil");
    private static readonly Symbol SetScale = Symbol.Intern("setscale");
    private static readonly Symbol ResetScale = Symbol.Intern("resetscale");
    private static readonly Symbol WithOutline = Symbol.Intern("with-outline");
    private static readonly Symbol SetTranslation = Symbol.Intern("settranslation");
    private static readonly Symbol ResetTranslation = Symbol.Intern("resettranslation");
    private static readonly Symbol Utf8String = Symbol.Intern("utf-8-string");

    /// <summary>Walks a stencil expression, feeding drawing commands to a sink.</summary>
    /// <param name="expression">The stencil expression.</param>
    /// <param name="sink">The sink to feed.</param>
    /// <param name="offset">The accumulated translation.</param>
    public static void Interpret(object expression, IStencilSink sink, Offset offset)
    {
        object expr = expression;
        Offset o = offset;

        while (true)
        {
            if (!(expr is Pair pair))
            {
                return;
            }

            object head = pair.Car;
            List<object> parts = Pair.ToList(expr);

            if (ReferenceEquals(head, DelayStencilEvaluation))
            {
                // FORCED, as upstream's `scm_force (scm_cadr (expr))' does. The comment
                // that used to stand here said the port "carries the value directly" —
                // it does not, and could not: `delay-stencil-evaluation' is built by
                // VENDORED Scheme (page-ref, tocItem, on-the-fly page predicates), and
                // vendored Scheme writes a real `(delay ...)'. Interpreting the promise
                // OBJECT as an expression matches no head and draws nothing, which is
                // why \page-ref printed its trailing text and no page number at all.
                Interpret(Objects.SchemeUtilities.Force(Second(parts)), sink, o);
                return;
            }

            if (ReferenceEquals(head, Footnote))
            {
                return;
            }

            if (ReferenceEquals(head, TranslateStencil))
            {
                o += ToOffset(Second(parts));
                expr = Third(parts);
            }
            else if (ReferenceEquals(head, CombineStencil))
            {
                for (int i = 1; i < parts.Count; i++)
                {
                    Interpret(parts[i], sink, o);
                }

                return;
            }
            else if (ReferenceEquals(head, GrobCause))
            {
                object grob = Second(parts);
                sink.Output(Pair.List(head, new Pair(o.X, o.Y), grob));
                Interpret(Third(parts), sink, o);
                sink.Output(Pair.List(NoOrigin));
                return;
            }
            else if (ReferenceEquals(head, Color))
            {
                List<object> color = Pair.ToList(Second(parts));
                sink.Output(Pair.List(
                    SetColor,
                    At(color, 0),
                    At(color, 1),
                    At(color, 2),
                    At(color, 3)));
                Interpret(Third(parts), sink, o);
                sink.Output(Pair.List(ResetColor));
                return;
            }
            else if (ReferenceEquals(head, OutputAttributes))
            {
                sink.Output(Pair.List(StartGroupNode, Second(parts)));
                Interpret(Third(parts), sink, o);
                sink.Output(Pair.List(EndGroupNode));
                return;
            }
            else if (ReferenceEquals(head, RotateStencil))
            {
                List<object> args = Pair.ToList(Second(parts));
                object angle = At(args, 0);
                Offset centre = o + ToOffset(At(args, 1));

                sink.Output(Pair.List(SetRotation, angle, centre.X, centre.Y));
                Interpret(Third(parts), sink, o);
                sink.Output(Pair.List(ResetRotation, angle, centre.X, centre.Y));
                return;
            }
            else if (ReferenceEquals(head, ScaleStencil))
            {
                List<object> args = Pair.ToList(Second(parts));
                object xScale = At(args, 0);
                object yScale = At(args, 1);
                Offset unscaled = new Offset(o.X / ToDouble(xScale), o.Y / ToDouble(yScale));

                sink.Output(Pair.List(SetScale, xScale, yScale));
                Interpret(Third(parts), sink, unscaled);
                sink.Output(Pair.List(ResetScale));
                return;
            }
            else if (ReferenceEquals(head, WithOutline))
            {
                // The outline is for collision only; draw the real contents.
                expr = Third(parts);
            }
            else
            {
                sink.Output(Pair.List(SetTranslation, o.X, o.Y));
                object result = sink.Output(expr);
                sink.Output(Pair.List(ResetTranslation));

                if (IsFalse(result) && ReferenceEquals(head, Utf8String) && parts.Count > 3)
                {
                    expr = parts[3];
                    continue;
                }

                return;
            }
        }
    }

    private static object Second(List<object> parts) => At(parts, 1);

    private static object Third(List<object> parts) => At(parts, 2);

    private static object At(List<object> parts, int index)
        => index >= 0 && index < parts.Count ? parts[index] : Nil.Instance;

    private static bool IsFalse(object value) => value is bool flag && !flag;

    private static Offset ToOffset(object value)
        => value is Pair pair ? new Offset(ToDouble(pair.Car), ToDouble(pair.Cdr)) : Offset.Zero;

    private static double ToDouble(object value)
    {
        switch (value)
        {
            case double d:
                return d;
            case long l:
                return l;
            case int i:
                return i;
            case System.Numerics.BigInteger big:
                return (double)big;
            case CodeBrix.LilyScheme.Numeric.Ratio ratio:
                return (double)ratio.Numerator / (double)ratio.Denominator;
            default:
                return 0.0;
        }
    }
}
