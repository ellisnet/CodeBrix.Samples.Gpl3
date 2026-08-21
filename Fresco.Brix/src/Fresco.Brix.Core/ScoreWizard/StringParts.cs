// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using Dom = Fresco.Brix.Ly.Dom;

namespace Fresco.Brix.ScoreWizard; //was previously: frescobaldi/scorewiz/parts/strings.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>A bowed string instrument.</summary>
public abstract class StringPart : SingleVoicePart
{
}

/// <summary>The violin.</summary>
public sealed class Violin : StringPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Violin");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Violin", "Vl.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "violin";
}

/// <summary>The viola.</summary>
public sealed class Viola : StringPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Viola");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Viola", "Vla.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "viola";

    /// <inheritdoc/>
    protected override string Clef => "alto";

    /// <inheritdoc/>
    protected override int Octave => 0;
}

/// <summary>The cello.</summary>
public class Cello : StringPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Cello");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Cello", "Cl.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "cello";

    /// <inheritdoc/>
    protected override string Clef => "bass";

    /// <inheritdoc/>
    protected override int Octave => -1;
}

/// <summary>The contrabass.</summary>
public sealed class Contrabass : StringPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Contrabass");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Contrabass", "Cb.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "contrabass";

    /// <inheritdoc/>
    protected override string Clef => "bass";

    /// <inheritdoc/>
    protected override int Octave => -2;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-1, 0, 0);
}

/// <summary>A cello that also carries the figured bass.</summary>
public sealed class BassoContinuo : Cello
{
    /// <inheritdoc/>
    public override string Title(Translator translate)
        => translate(null, "Basso continuo");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Basso continuo", "B.C.");

    /// <inheritdoc/>
    public override void Build(PartData data, ScoreBuilder builder)
    {
        base.Build(data, builder);
        if (data.Assignments[0].Name is Dom.Reference reference)
        {
            reference.Name = "bcMusic";
        }

        Dom.Assignment figures = data.Assign("bcFigures");
        Dom.FigureMode mode = new Dom.FigureMode(figures);
        new Dom.Identifier(data.GlobalName, mode);
        new Dom.Line(
            "\\override Staff.BassFigureAlignmentPositioning.direction = #DOWN", mode);
        new Dom.LineComment(I18n.Get("Figures follow here."), mode);
        new Dom.BlankLine(mode);

        Dom.FiguredBass figuredBass = new Dom.FiguredBass();
        new Dom.Identifier(figures.Name, figuredBass);
        data.Nodes.Add(figuredBass);
    }
}
