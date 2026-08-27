// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.ScoreWizard; //was previously: frescobaldi/scorewiz/parts/__init__.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>One part type as the Available parts list offers it.</summary>
public sealed class PartEntry
{
    /// <summary>Initializes the entry.</summary>
    /// <param name="name">The part's type name.</param>
    /// <param name="create">Makes a fresh one.</param>
    public PartEntry(string name, Func<PartBase> create)
    {
        Name = name;
        Create = create;
    }

    /// <summary>Gets the part's type name, which its variables are named from.</summary>
    public string Name { get; }

    /// <summary>Gets what makes a fresh part of this type.</summary>
    public Func<PartBase> Create { get; }

    /// <summary>Answers the part's name for the list.</summary>
    /// <returns>The name.</returns>
    public string Title() => Create().Title();
}

/// <summary>A group of related part types.</summary>
public sealed class PartCategory
{
    /// <summary>Initializes the category.</summary>
    /// <param name="title">What names the category, translated on demand.</param>
    /// <param name="items">The part types in it.</param>
    public PartCategory(Func<string> title, IReadOnlyList<PartEntry> items)
    {
        TitleText = title;
        Items = items;
    }

    /// <summary>Gets what names the category.</summary>
    public Func<string> TitleText { get; }

    /// <summary>Gets the part types in it, in order.</summary>
    public IReadOnlyList<PartEntry> Items { get; }

    /// <summary>Answers the category's name.</summary>
    /// <returns>The name.</returns>
    public string Title() => TitleText();
}

/// <summary>Every part type the Score Wizard offers, in upstream's order.</summary>
public static class PartRegistry
{
    /// <summary>Gets the categories, in the order they are listed.</summary>
    public static IReadOnlyList<PartCategory> Categories { get; } = Build();

    /// <summary>Makes a part by its type name.</summary>
    /// <param name="name">The type name.</param>
    /// <returns>The part, or null when nothing has that name.</returns>
    public static PartBase Create(string name)
    {
        foreach (PartCategory category in Categories)
        {
            foreach (PartEntry entry in category.Items)
            {
                if (string.Equals(entry.Name, name, StringComparison.Ordinal))
                {
                    return entry.Create();
                }
            }
        }

        return null;
    }

    /// <summary>Gets every part type, whatever category it is in.</summary>
    /// <returns>The entries.</returns>
    public static IEnumerable<PartEntry> AllParts()
        => Categories.SelectMany(category => category.Items);

    /// <summary>Builds the catalogue.</summary>
    /// <returns>The categories.</returns>
    private static IReadOnlyList<PartCategory> Build() => new[]
    {
        new PartCategory(() => I18n.Get("Strings"), new[]
        {
            Entry(() => new Violin()),
            Entry(() => new Viola()),
            Entry(() => new Cello()),
            Entry(() => new Contrabass()),
            Entry(() => new BassoContinuo()),
        }),
        new PartCategory(() => I18n.Get("Plucked strings"), new[]
        {
            Entry(() => new Mandolin()),
            Entry(() => new Banjo()),
            Entry(() => new Ukulele()),
            Entry(() => new Guitar()),
            Entry(() => new AcousticGuitar()),
            Entry(() => new ElectricGuitar()),
            Entry(() => new AcousticBass()),
            Entry(() => new ElectricBass()),
            Entry(() => new Harp()),
        }),
        new PartCategory(() => I18n.Get("Woodwinds"), new[]
        {
            Entry(() => new Flute()),
            Entry(() => new Piccolo()),
            Entry(() => new AltoFlute()),
            Entry(() => new BassFlute()),
            Entry(() => new Oboe()),
            Entry(() => new OboeDAmore()),
            Entry(() => new EnglishHorn()),
            Entry(() => new Bassoon()),
            Entry(() => new ContraBassoon()),
            Entry(() => new Clarinet()),
            Entry(() => new EflatClarinet()),
            Entry(() => new AClarinet()),
            Entry(() => new BassClarinet()),
            Entry(() => new SopraninoSax()),
            Entry(() => new SopranoSax()),
            Entry(() => new AltoSax()),
            Entry(() => new TenorSax()),
            Entry(() => new BaritoneSax()),
            Entry(() => new BassSax()),
            Entry(() => new C_MelodySax()),
            Entry(() => new SopraninoRecorder()),
            Entry(() => new SopranoRecorder()),
            Entry(() => new AltoRecorder()),
            Entry(() => new TenorRecorder()),
            Entry(() => new BassRecorder()),
            Entry(() => new ContraBassRecorder()),
            Entry(() => new SubContraBassRecorder()),
        }),
        new PartCategory(() => I18n.Get("Brass"), new[]
        {
            Entry(() => new HornF()),
            Entry(() => new TrumpetC()),
            Entry(() => new TrumpetBb()),
            Entry(() => new CornetBb()),
            Entry(() => new Flugelhorn()),
            Entry(() => new Mellophone()),
            Entry(() => new Trombone()),
            Entry(() => new TromboneBb()),
            Entry(() => new AltoTrombone()),
            Entry(() => new BassTrombone()),
            Entry(() => new TenorHorn()),
            Entry(() => new Baritone()),
            Entry(() => new Euphonium()),
            Entry(() => new Tuba()),
            Entry(() => new TubaEb()),
            Entry(() => new TubaBb()),
            Entry(() => new BassTuba()),
        }),
        new PartCategory(() => I18n.Get("Vocal"), new[]
        {
            Entry(() => new LeadSheet()),
            Entry(() => new SopranoVoice()),
            Entry(() => new MezzoSopranoVoice()),
            Entry(() => new AltoVoice()),
            Entry(() => new TenorVoice()),
            Entry(() => new BassVoice()),
            Entry(() => new Choir()),
        }),
        new PartCategory(() => I18n.Get("Keyboard instruments"), new[]
        {
            Entry(() => new Piano()),
            Entry(() => new ElectricPiano()),
            Entry(() => new Harpsichord()),
            Entry(() => new Clavichord()),
            Entry(() => new Organ()),
            Entry(() => new Celesta()),
            Entry(() => new SynthLead()),
            Entry(() => new SynthPad()),
            Entry(() => new SynthBass()),
            Entry(() => new SynthStrings()),
            Entry(() => new SynthBrass()),
            Entry(() => new SynthFx()),
        }),
        new PartCategory(() => I18n.Get("Percussion"), new[]
        {
            Entry(() => new Timpani()),
            Entry(() => new Xylophone()),
            Entry(() => new Marimba()),
            Entry(() => new Vibraphone()),
            Entry(() => new TubularBells()),
            Entry(() => new Steelpan()),
            Entry(() => new Dulcimer()),
            Entry(() => new Glockenspiel()),
            Entry(() => new Carillon()),
            Entry(() => new Drums()),
        }),
        new PartCategory(() => I18n.Get("Special"), new[]
        {
            Entry(() => new Structure()),
            Entry(() => new Chords()),
            Entry(() => new BassFigures()),
        }),
        new PartCategory(() => I18n.Get("Containers"), new[]
        {
            Entry(() => new StaffGroup()),
            Entry(() => new Score()),
            Entry(() => new BookPart()),
            Entry(() => new Book()),
        }),
    };

    /// <summary>Makes an entry, naming it after the type it creates.</summary>
    /// <typeparam name="T">The part type.</typeparam>
    /// <param name="create">Makes a fresh one.</param>
    /// <returns>The entry.</returns>
    private static PartEntry Entry<T>(Func<T> create)
        where T : PartBase
        => new PartEntry(typeof(T).Name, () => create());
}
