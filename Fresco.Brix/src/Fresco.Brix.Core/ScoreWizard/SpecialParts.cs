// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using Dom = Fresco.Brix.Ly.Dom;

namespace Fresco.Brix.ScoreWizard; //was previously: frescobaldi/scorewiz/parts/special.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// A part for laying a piece's structure out — breaks, bar lines and the like
/// — apart from its musical content.
/// </summary>
/// <remarks>The LilyPond notation reference shows this done with an extra
/// VOICE (section 4.3.1, "Using an extra voice for breaks"); a separate part
/// is the same idea one level up.</remarks>
public sealed class Structure : PartType
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Structure");

    /// <inheritdoc/>
    public override void Build(PartData data, ScoreBuilder builder)
    {
        Dom.Devnull devnull = new Dom.Devnull();
        Dom.Assignment assignment = data.Assign("structure");
        new Dom.Identifier(assignment.Name, devnull);
        Dom.Seq stub = new Dom.Seq(assignment);
        new Dom.Identifier(data.GlobalName, stub) { After = 1 };
        new Dom.LineComment(I18n.Get("Structure follows here."), stub);
        new Dom.BlankLine(stub);
        data.Nodes.Add(devnull);
    }
}

/// <summary>A staff of chord names.</summary>
public sealed class Chords : PartType
{
    /// <summary>Initializes the part and its settings.</summary>
    public Chords()
    {
        ChordNames = new ChordNamesSupport();
        Add(ChordNames.ChordStyle);
        Add(ChordNames.GuitarFrets);
    }

    /// <summary>Gets the chord-name settings.</summary>
    public ChordNamesSupport ChordNames { get; }

    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Chord names");

    /// <inheritdoc/>
    public override void Build(PartData data, ScoreBuilder builder)
        => ChordNames.Build(data, builder);
}

/// <summary>A figured-bass staff.</summary>
public sealed class BassFigures : PartType
{
    /// <summary>Initializes the part and its settings.</summary>
    public BassFigures()
        => ExtenderLines = Add(new BoolSetting("extenderLines")
        {
            Label = () => I18n.Get("Use extender lines"),
        });

    /// <summary>Gets whether figures are joined by extender lines.</summary>
    public BoolSetting ExtenderLines { get; }

    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Figured bass");

    /// <inheritdoc/>
    public override void Build(PartData data, ScoreBuilder builder)
    {
        Dom.Assignment assignment = data.Assign("figBass");
        Dom.FigureMode mode = new Dom.FigureMode(assignment);
        Dom.FiguredBass figuredBass = new Dom.FiguredBass();
        new Dom.Identifier(assignment.Name, figuredBass);
        new Dom.Identifier(data.GlobalName, mode);
        new Dom.LineComment(I18n.Get("Figures follow here."), mode);
        new Dom.BlankLine(mode);
        if (ExtenderLines.Value)
        {
            figuredBass.GetWith()["useBassFigureExtenders"] = new Dom.Scheme("#t");
        }

        data.Nodes.Add(figuredBass);
    }
}
