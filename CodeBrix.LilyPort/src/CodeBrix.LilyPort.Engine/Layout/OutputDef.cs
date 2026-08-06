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
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/output-def.cc, lily/include/output-def.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/*
  Output settings for a block of music.

  This devolved into a rather empty class. The distinction between
  various instances is made in the parser, which creates
  midi/layout/paper blocks depending on the keyword read.

  The data structure is set up as recursive: the definitions not
  supplied in layout are looked up in paper. This is done through
  the parent_ field of Output_def.
*/

/// <summary>
/// The variable table a block of music is laid out under: what a <c>\layout</c> or
/// <c>\paper</c> block becomes.
/// <para>
/// Lookups are RECURSIVE. A variable missing from a <c>\layout</c> is looked up in the
/// <c>\paper</c> above it through <see cref="Parent"/>, which is what lets a score
/// override two settings and inherit the other two hundred.
/// </para>
/// <para>
/// THE SCOPE IS A REAL MODULE. An earlier pass held it as a dictionary keyed by symbol,
/// on the reasoning that nothing treated it as a module — which the <c>ly/</c> init layer
/// disproved twice over. <c>output_def_head</c> pushes <see cref="Scope"/> onto the
/// LEXER's scope stack, so every assignment inside a <c>\paper</c> or <c>\layout</c> block
/// lands in the definition rather than in the enclosing file; and
/// <c>declarations-init.ly</c> calls <c>(set-paper-dimension-variables (current-module))</c>
/// from inside a <c>\layout</c> block, which can only mean the layout's own module. With a
/// dictionary the block's variables silently went somewhere else and the scope stack came
/// out one short.
/// </para>
/// </summary>
public class OutputDef
{
    private static readonly Symbol ScaleLayoutSymbol = Symbol.Intern("scale-layout");

    private readonly SchemeModule _scope;

    /// <summary>Initializes an empty output definition.</summary>
    public OutputDef()
    {
        _scope = LilyModules.Make("output-def");
    }

    /// <summary>Initializes a copy of another output definition.</summary>
    /// <param name="source">The definition to copy.</param>
    /// <remarks>
    /// The parent is deliberately NOT copied, matching upstream's copy constructor:
    /// a clone starts unparented and is re-parented by whoever made it.
    /// </remarks>
    public OutputDef(OutputDef source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        // Upstream: scope_ = ly_make_module (); ly_module_copy (scope_, s.scope_);
        // A FRESH module copied into, not a shared one — a cloned \paper that is then
        // edited must not write through to the definition it was cloned from.
        _scope = LilyModules.Make("output-def");
        LilyModules.Copy(_scope, source._scope);

        // input_origin_ = s.input_origin_; — upstream's copy constructor keeps the
        // original's location, so a \paper cloned from the $papers stack still says
        // where its ancestor was written until the parser stamps it afresh.
        InputOrigin = source.InputOrigin;
    }

    /// <summary>Gets or sets the definition variables fall through to.</summary>
    public OutputDef Parent { get; set; }

    /// <summary>
    /// Gets where in the source this definition came from, or <see langword="null"/>
    /// when no location has been recorded.
    /// <para>Upstream: <c>input_origin_</c>, the <c>Input</c> member the parser's
    /// output-definition rules assign. The port has no <c>Input</c> smob, so the
    /// location is carried opaquely — the parser stores its own span here, the same
    /// convention as <see cref="Objects.Book.Origin"/>.</para>
    /// </summary>
    public object InputOrigin { get; private set; }

    /// <summary>
    /// Records where in the source this definition came from.
    /// <para>Upstream: the assignments to <c>input_origin_</c> — both the whole-value
    /// <c>p-&gt;input_origin_ = @$</c> in <c>output_def_head</c> and the
    /// <c>input_origin_.set_spot (@$)</c> calls in <c>output_def_body</c>. The same
    /// shape as <see cref="Objects.Book.SetSpot"/>.</para>
    /// </summary>
    /// <param name="origin">The source location.</param>
    public void SetSpot(object origin) => InputOrigin = origin;

    /// <summary>Gets the C++ class name this definition corresponds to.</summary>
    public virtual string ClassName => "Output_def";

    /// <summary>
    /// Gets the module the definition's variables live in — upstream's <c>scope_</c>.
    /// <para>This is what <c>output_def_head</c> pushes onto the lexer's scope stack, and
    /// what <c>(current-module)</c> answers inside the block.</para>
    /// </summary>
    public SchemeModule Scope => _scope;

    /// <summary>Gets the variables set in THIS definition, without the parent chain.</summary>
    /// <returns>The definition's own variables, by name.</returns>
    public IReadOnlyDictionary<Symbol, object> Variables()
    {
        Dictionary<Symbol, object> variables = new Dictionary<Symbol, object>();
        foreach (KeyValuePair<Symbol, Variable> binding in _scope.Bindings)
        {
            if (binding.Value.IsBound)
            {
                variables[binding.Key] = binding.Value.GetValue();
            }
        }

        return variables;
    }

    /// <summary>Returns an independent copy of this definition.</summary>
    /// <returns>The clone.</returns>
    public virtual OutputDef Clone() => new OutputDef(this);

    /// <summary>
    /// Returns a clone with every DIMENSION variable divided by a factor — upstream's
    /// <c>scale_output_def</c>, which hands the work to <c>scm/paper.scm</c>'s
    /// <c>scale-layout</c>.
    /// <para>
    /// This is what puts engraving into output units. The factor is the paper's
    /// <c>output-scale</c>, one staff space in millimetres, and both the book's paper
    /// and each score's layout go through it before a single grob is made — so a
    /// coordinate that reaches a stencil is already in the units the backend writes.
    /// Skipping it does not fail: everything is laid out in millimetres instead and
    /// the whole page comes out scaled by about 1.76 with no diagnostic anywhere.
    /// </para>
    /// <para>
    /// The list of what counts as a dimension is the definition's own
    /// <c>dimension-variables</c>, and it deliberately excludes <c>output-scale</c> and
    /// <c>staff-height</c>: the first has to survive so the framework can convert back
    /// to millimetres for the page, and the second is read in POINTS to size fonts.
    /// </para>
    /// </summary>
    /// <param name="amount">The factor to divide dimensions by.</param>
    /// <returns>The scaled clone, or this definition when the Scheme layer is absent.</returns>
    public OutputDef ScaledClone(double amount)
    {
        object procedure = Bootstrap.LilyPondScheme.LookupProcedure(ScaleLayoutSymbol);
        CodeBrix.LilyScheme.Interpreter interpreter = Bootstrap.LilyPondScheme.Current;
        if (procedure == null || interpreter == null)
        {
            return this;
        }

        return interpreter.Evaluator.Apply(procedure, new object[] { this, amount })
            as OutputDef ?? this;
    }

    /// <summary>
    /// Reads a variable, walking up the parent chain.
    /// </summary>
    /// <param name="symbol">The variable name.</param>
    /// <returns>
    /// The value, or <see langword="null"/> when it is set nowhere. Null stands for
    /// upstream's <c>SCM_UNDEFINED</c>, which is a DIFFERENT answer from the empty list
    /// — several callers distinguish "never set" from "set to nothing".
    /// </returns>
    public object LookupVariable(Symbol symbol)
    {
        // Upstream: scm_module_variable (scope_, sym), which searches the module AND what
        // it imports, then falls through to the parent definition. An UNBOUND variable is
        // not an answer — psyntax reserves a slot before a definition runs — so a bound
        // check stands where upstream's !SCM_UNBNDP does.
        for (OutputDef definition = this; definition != null; definition = definition.Parent)
        {
            Variable variable = definition._scope.Lookup(symbol);
            if (variable != null && variable.IsBound)
            {
                return variable.GetValue();
            }
        }

        return null;
    }

    /// <summary>Reads a variable by name.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The value, or <see langword="null"/> when unset.</returns>
    public object CVariable(string name) => LookupVariable(Symbol.Intern(name));

    /// <summary>Sets a variable in this definition.</summary>
    /// <param name="symbol">The variable name.</param>
    /// <param name="value">The value.</param>
    public void SetVariable(Symbol symbol, object value)
    {
        if (symbol == null)
        {
            throw new ArgumentNullException(nameof(symbol));
        }

        _scope.Define(symbol, value);
    }

    /// <summary>Sets a variable by name.</summary>
    /// <param name="name">The variable name.</param>
    /// <param name="value">The value.</param>
    public void SetVariable(string name, object value) => SetVariable(Symbol.Intern(name), value);

    /// <summary>
    /// Reads a variable as a dimension.
    /// </summary>
    /// <param name="symbol">The variable name.</param>
    /// <returns>The value, or zero when it is unset or not a number.</returns>
    public double GetDimension(Symbol symbol)
    {
        object value = LookupVariable(symbol);
        return SchemeConvert.IsNumber(value)
            ? SchemeConvert.ToDouble(value, symbol.Name)
            : 0.0;
    }

    /// <summary>Reads a variable as a dimension, by name.</summary>
    /// <param name="name">The variable name.</param>
    /// <returns>The dimension.</returns>
    public double GetDimension(string name) => GetDimension(Symbol.Intern(name));

    /// <summary>
    /// Resolves the horizontal page geometry: works out whichever of
    /// <c>line-width</c>, <c>left-margin</c> and <c>right-margin</c> was not stated, and
    /// writes all three back.
    /// <para>
    /// This is where <c>line-width</c> COMES FROM. <c>scm/paper.scm</c>'s
    /// <c>set-paper-dimensions</c> deliberately REMOVES it — a stale one from
    /// lilypond-book would be worse than none — so between paper setup and this call
    /// there is genuinely no line width anywhere, and every spacing solve would fall
    /// back to a guess.
    /// </para>
    /// <para>
    /// The two unstated-margin cases are not symmetric, and the asymmetry is deliberate:
    /// with only a line width given, the systems are CENTRED; with a line width and one
    /// margin, the other margin absorbs the remainder.
    /// </para>
    /// </summary>
    public void Normalize()
    {
        object scmPaperWidth = CVariable("paper-width");

        bool twosided = SchemeUtilities.ToBool(CVariable("two-sided"));

        // We don't distinguish between outer-margin / left-margin and so on
        // until page-stencil positioning in page.scm
        object scmLeftMarginDefault = twosided
            ? CVariable("outer-margin-default-scaled")
            : CVariable("left-margin-default-scaled");
        object scmLeftMargin = twosided ? CVariable("outer-margin") : CVariable("left-margin");

        object scmRightMarginDefault = twosided
            ? CVariable("inner-margin-default-scaled")
            : CVariable("right-margin-default-scaled");
        object scmRightMargin = twosided ? CVariable("inner-margin") : CVariable("right-margin");

        if (!SchemeConvert.IsNumber(scmPaperWidth)
            || !SchemeConvert.IsNumber(scmLeftMarginDefault)
            || !SchemeConvert.IsNumber(scmRightMarginDefault))
        {
            Warn.ProgrammingError("called normalize () on paper with missing settings");
            return;
        }

        double paperWidth = SchemeConvert.ToDouble(scmPaperWidth, "paper-width");
        double leftMarginDefault = SchemeConvert.ToDouble(scmLeftMarginDefault, "left-margin");
        double rightMarginDefault = SchemeConvert.ToDouble(scmRightMarginDefault, "right-margin");

        double lineWidth;
        double leftMargin;
        double rightMargin;
        double lineWidthDefault = paperWidth - leftMarginDefault - rightMarginDefault;
        object scmLineWidth = CVariable("line-width");

        double bindingOffset = 0;
        if (twosided && SchemeConvert.IsNumber(CVariable("binding-offset")))
        {
            bindingOffset = SchemeConvert.ToDouble(CVariable("binding-offset"), "binding-offset");
        }

        if (!SchemeConvert.IsNumber(scmLineWidth))
        {
            leftMargin = SchemeConvert.IsNumber(scmLeftMargin)
                ? SchemeConvert.ToDouble(scmLeftMargin, "left-margin")
                : leftMarginDefault;
            rightMargin = (SchemeConvert.IsNumber(scmRightMargin)
                ? SchemeConvert.ToDouble(scmRightMargin, "right-margin")
                : rightMarginDefault) + bindingOffset;
            lineWidth = paperWidth - leftMargin - rightMargin;
        }
        else
        {
            lineWidth = SchemeConvert.ToDouble(scmLineWidth, "line-width");
            if (!SchemeConvert.IsNumber(scmLeftMargin))
            {
                // Vertically center systems if only line-width is given
                if (!SchemeConvert.IsNumber(scmRightMargin))
                {
                    leftMargin = (paperWidth - lineWidth) / 2;
                    rightMargin = leftMargin;
                }
                else
                {
                    rightMargin = SchemeConvert.ToDouble(scmRightMargin, "right-margin")
                        + bindingOffset;
                    leftMargin = paperWidth - lineWidth - rightMargin;
                }
            }
            else
            {
                leftMargin = SchemeConvert.ToDouble(scmLeftMargin, "left-margin");
                rightMargin = (SchemeConvert.IsNumber(scmRightMargin)
                    ? SchemeConvert.ToDouble(scmRightMargin, "right-margin")
                    : paperWidth - lineWidth - leftMargin) + bindingOffset;
            }
        }

        if (SchemeUtilities.ToBool(CVariable("check-consistency")))
        {
            // Consistency checks. If values don't match, set defaults.
            if (Math.Abs(paperWidth - lineWidth - leftMargin - rightMargin) > 1e-6)
            {
                lineWidth = lineWidthDefault;
                leftMargin = leftMarginDefault;
                rightMargin = rightMarginDefault;
                Warn.Warning("margins do not fit with line-width, setting default values");
            }
            else if (leftMargin < 0 || rightMargin < 0)
            {
                lineWidth = lineWidthDefault;
                leftMargin = leftMarginDefault;
                rightMargin = rightMarginDefault;
                Warn.Warning(
                    "systems run off the page due to improper paper settings, "
                    + "setting default values");
            }
        }

        SetVariable("left-margin", leftMargin);
        SetVariable("right-margin", rightMargin);
        SetVariable("line-width", lineWidth);
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The definition's class name.</returns>
    public override string ToString() => "#< " + ClassName + ">";
}
