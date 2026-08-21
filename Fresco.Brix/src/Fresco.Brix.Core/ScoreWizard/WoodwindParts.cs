// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;

namespace Fresco.Brix.ScoreWizard; //was previously: frescobaldi/scorewiz/parts/woodwind.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>A wind instrument that is not brass.</summary>
public abstract class WoodWindPart : SingleVoicePart
{
}

/// <summary>The Flute.</summary>
public sealed class Flute : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Flute");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Flute", "Fl.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "flute";
}

/// <summary>The Piccolo.</summary>
public sealed class Piccolo : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Piccolo");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Piccolo", "Pic.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "piccolo";

    /// <inheritdoc/>
    protected override int Octave => 2;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (1, 0, 0);
}

/// <summary>The Alto flute.</summary>
public sealed class AltoFlute : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Alto flute");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Alto flute", "A.Fl.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "flute";

    /// <inheritdoc/>
    protected override int Octave => 0;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-1, 4, 0);
}

/// <summary>The Bass flute.</summary>
public sealed class BassFlute : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Bass flute");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Bass flute", "B.Fl.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "flute";

    /// <inheritdoc/>
    protected override int Octave => 0;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-1, 0, 0);
}

/// <summary>The Oboe.</summary>
public sealed class Oboe : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Oboe");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Oboe", "Ob.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "oboe";
}

/// <summary>The Oboe d'amore.</summary>
public sealed class OboeDAmore : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Oboe d'amore");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Oboe d'amore", "Ob.D'am.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "oboe";

    /// <inheritdoc/>
    protected override int Octave => 0;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-1, 5, 0);
}

/// <summary>The English horn.</summary>
public sealed class EnglishHorn : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "English horn");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for English horn", "Eng.H.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "english horn";

    /// <inheritdoc/>
    protected override int Octave => 0;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-1, 3, 0);
}

/// <summary>The Bassoon.</summary>
public sealed class Bassoon : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Bassoon");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Bassoon", "Bn.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "bassoon";

    /// <inheritdoc/>
    protected override string Clef => "bass";

    /// <inheritdoc/>
    protected override int Octave => -1;
}

/// <summary>The Contrabassoon.</summary>
public sealed class ContraBassoon : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Contrabassoon");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Contrabassoon", "C.Bn.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "bassoon";

    /// <inheritdoc/>
    protected override string Clef => "bass";

    /// <inheritdoc/>
    protected override int Octave => -2;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-1, 0, 0);
}

/// <summary>The Clarinet.</summary>
public sealed class Clarinet : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Clarinet");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Clarinet", "Cl.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "clarinet";

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-1, 6, -1);
}

/// <summary>The E-flat clarinet.</summary>
public sealed class EflatClarinet : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "E-flat clarinet");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for E-flat clarinet", "Cl. in Eb");

    /// <inheritdoc/>
    protected override string MidiInstrument => "clarinet";

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (0, 2, -1);
}

/// <summary>The A clarinet.</summary>
public sealed class AClarinet : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "A clarinet");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for A clarinet", "Cl. in A");

    /// <inheritdoc/>
    protected override string MidiInstrument => "clarinet";

    /// <inheritdoc/>
    protected override int Octave => 0;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-1, 5, 0);
}

/// <summary>The Bass clarinet.</summary>
public sealed class BassClarinet : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Bass clarinet");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Bass clarinet", "B.Cl.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "clarinet";

    /// <inheritdoc/>
    protected override int Octave => -1;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-2, 6, -1);
}

/// <summary>The C-melody saxophone.</summary>
public sealed class C_MelodySax : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "C-melody saxophone");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for C-melody saxophone", "C-Mel Sax");

    /// <inheritdoc/>
    protected override string MidiInstrument => "soprano sax";
}

/// <summary>The Sopranino saxophone.</summary>
public sealed class SopraninoSax : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Sopranino saxophone");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Sopranino saxophone", "Si.Sax.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "soprano sax";

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (0, 2, -1);
}

/// <summary>The Soprano saxophone.</summary>
public sealed class SopranoSax : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Soprano saxophone");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Soprano saxophone", "So.Sax.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "soprano sax";

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-1, 6, -1);
}

/// <summary>The Alto saxophone.</summary>
public sealed class AltoSax : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Alto saxophone");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Alto saxophone", "A.Sax.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "alto sax";

    /// <inheritdoc/>
    protected override int Octave => 0;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-1, 2, -1);
}

/// <summary>The Tenor saxophone.</summary>
public sealed class TenorSax : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Tenor saxophone");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Tenor saxophone", "T.Sax.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "tenor sax";

    /// <inheritdoc/>
    protected override int Octave => 0;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-2, 6, -1);
}

/// <summary>The Baritone saxophone.</summary>
public sealed class BaritoneSax : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Baritone saxophone");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Baritone saxophone", "B.Sax.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "baritone sax";

    /// <inheritdoc/>
    protected override int Octave => -1;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-2, 2, -1);
}

/// <summary>The Bass saxophone.</summary>
public sealed class BassSax : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Bass saxophone");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Bass saxophone", "Bs.Sax.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "baritone sax";

    /// <inheritdoc/>
    protected override int Octave => -1;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-3, 6, -1);
}

/// <summary>The Sopranino recorder.</summary>
public sealed class SopraninoRecorder : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Sopranino recorder");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Sopranino recorder", "Si.Rec.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "recorder";

    /// <inheritdoc/>
    protected override int Octave => 2;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (1, 0, 0);
}

/// <summary>The Soprano recorder.</summary>
public sealed class SopranoRecorder : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Soprano recorder");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Soprano recorder", "S.Rec.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "recorder";

    /// <inheritdoc/>
    protected override int Octave => 2;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (1, 0, 0);
}

/// <summary>The Alto recorder.</summary>
public sealed class AltoRecorder : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Alto recorder");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Alto recorder", "A.Rec.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "recorder";
}

/// <summary>The Tenor recorder.</summary>
public sealed class TenorRecorder : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Tenor recorder");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Tenor recorder", "T.Rec.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "recorder";
}

/// <summary>The Bass recorder.</summary>
public sealed class BassRecorder : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Bass recorder");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Bass recorder", "B.Rec.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "recorder";

    /// <inheritdoc/>
    protected override string Clef => "bass";

    /// <inheritdoc/>
    protected override int Octave => 0;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (1, 0, 0);
}

/// <summary>The Contrabass recorder.</summary>
public sealed class ContraBassRecorder : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Contrabass recorder");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Contrabass recorder", "Cb.Rec.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "recorder";

    /// <inheritdoc/>
    protected override string Clef => "bass";

    /// <inheritdoc/>
    protected override int Octave => -1;
}

/// <summary>The Subcontrabass recorder.</summary>
public sealed class SubContraBassRecorder : WoodWindPart
{
    /// <inheritdoc/>
    public override string Title(Translator translate) => translate(null, "Subcontrabass recorder");

    /// <inheritdoc/>
    public override string Short(Translator translate)
        => translate("abbreviation for Subcontrabass recorder", "Scb.Rec.");

    /// <inheritdoc/>
    protected override string MidiInstrument => "recorder";

    /// <inheritdoc/>
    protected override string Clef => "bass";

    /// <inheritdoc/>
    protected override int Octave => -2;

    /// <inheritdoc/>
    protected override (int Octave, int Note, int Alter)? Transposition => (-1, 0, 0);
}
