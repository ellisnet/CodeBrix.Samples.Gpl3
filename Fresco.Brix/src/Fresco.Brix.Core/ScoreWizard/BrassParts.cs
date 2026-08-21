// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;

namespace Fresco.Brix.ScoreWizard; //was previously: frescobaldi/scorewiz/parts/brass.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>A brass instrument.</summary>
public abstract class BrassPart : SingleVoicePart
{
}

/// <summary>The Horn in F.</summary>
public sealed class HornF : BrassPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Horn in F");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Horn in F", "Hn.F.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "french horn";

    /// <inheritdoc/>
    protected override int Octave => 0;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-1, 3, 0);
}

/// <summary>The Trumpet in C.</summary>
public class TrumpetC : BrassPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Trumpet in C");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Trumpet in C", "Tr.C.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "trumpet";
}

/// <summary>The Trumpet in Bb.</summary>
public class TrumpetBb : TrumpetC
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Trumpet in Bb");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Trumpet in Bb", "Tr.Bb.");

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-1, 6, -1);
}

/// <summary>The Cornet in Bb.</summary>
public sealed class CornetBb : TrumpetBb
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Cornet in Bb");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Cornet in Bb", "Crt.Bb.");
}

/// <summary>The Flugelhorn.</summary>
public sealed class Flugelhorn : BrassPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Flugelhorn");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Flugelhorn", "Fgh.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "trumpet";
}

/// <summary>The Mellophone.</summary>
public sealed class Mellophone : BrassPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Mellophone");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Mellophone", "Mph.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "french horn";

    /// <inheritdoc/>
    protected override int Octave => 0;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-1, 3, 0);
}

/// <summary>The Trombone.</summary>
public class Trombone : BrassPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Trombone");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Trombone", "Trb.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "trombone";

    /// <inheritdoc/>
    protected override string Clef => "bass";

    /// <inheritdoc/>
    protected override int Octave => -1;
}

/// <summary>The Trombone in Bb.</summary>
public sealed class TromboneBb : Trombone
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Trombone in Bb");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Trombone in Bb", "Trb.Bb.");

    /// <inheritdoc/>
    protected override string Clef => null;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-2, 6, -1);
}

/// <summary>The Alto trombone.</summary>
public sealed class AltoTrombone : Trombone
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Alto trombone");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Alto trombone", "A.Trb.");

    /// <inheritdoc/>
    protected override string Clef => "alto";

    /// <inheritdoc/>
    protected override int Octave => 0;
}

/// <summary>The Bass trombone.</summary>
public sealed class BassTrombone : Trombone
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Bass trombone");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Bass trombone", "B.Trb.");
}

/// <summary>The Tenor horn.</summary>
public sealed class TenorHorn : BrassPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Tenor horn");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Tenor horn", "T.Hn.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "french horn";

    /// <inheritdoc/>
    protected override int Octave => -1;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-1, 2, -1);
}

/// <summary>The Baritone.</summary>
public sealed class Baritone : BrassPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Baritone");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Baritone", "Bar.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "trombone";

    /// <inheritdoc/>
    protected override string Clef => "bass";

    /// <inheritdoc/>
    protected override int Octave => -1;
}

/// <summary>The Euphonium.</summary>
public sealed class Euphonium : BrassPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Euphonium");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Euphonium", "Euph.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "trombone";

    /// <inheritdoc/>
    protected override string Clef => "bass";

    /// <inheritdoc/>
    protected override int Octave => -1;
}

/// <summary>The Tuba.</summary>
public class Tuba : BrassPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Tuba");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Tuba", "Tb.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "tuba";

    /// <inheritdoc/>
    protected override string Clef => "bass";

    /// <inheritdoc/>
    protected override int Octave => -1;
}

/// <summary>The Tuba in Eb.</summary>
public sealed class TubaEb : Tuba
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Tuba in Eb");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Tuba in Eb", "Tb.Eb.");

    /// <inheritdoc/>
    protected override string Clef => null;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-1, 2, -1);
}

/// <summary>The Tuba in Bb.</summary>
public sealed class TubaBb : Tuba
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Tuba in Bb");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Tuba in Bb", "Tb.Bb.");

    /// <inheritdoc/>
    protected override string Clef => null;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-2, 6, -1);
}

/// <summary>The Bass tuba.</summary>
public sealed class BassTuba : BrassPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Bass tuba");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Bass tuba", "B.Tb.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "tuba";

    /// <inheritdoc/>
    protected override string Clef => "bass";

    /// <inheritdoc/>
    protected override int Octave => -2;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-1, 0, 0);
}
