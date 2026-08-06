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
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/translator-ctors.cc, lily/translator-group-ctors.cc, lily/include/translator.hh (Translator_creator);

// Modified by Jeremy Ellis on 2026-08-05 as part of the CodeBrix port.

/// <summary>
/// What a translator's name resolves to: something that can make one translator for one
/// context.
/// <para>
/// Upstream this is a smob wrapping the <c>ADD_TRANSLATOR</c> allocation function, and
/// <c>Translator_creator::call</c> is what <c>ly_call (trans, ctx)</c> reaches. The port
/// carries the same idea as a delegate, and stores instances in the SAME registry that
/// <c>ly:register-translator</c> writes into — that is the whole point: a C++ engraver
/// and a Scheme engraver have to be indistinguishable at the point a context's
/// <c>\consists</c> list is resolved.
/// </para>
/// </summary>
public sealed class TranslatorCreator
{
    private readonly Func<Context, Translator> _allocate;

    /// <summary>Initializes a creator.</summary>
    /// <param name="name">The translator's name, as <c>\consists</c> spells it.</param>
    /// <param name="allocate">Makes one translator for one context.</param>
    public TranslatorCreator(Symbol name, Func<Context, Translator> allocate)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _allocate = allocate ?? throw new ArgumentNullException(nameof(allocate));
    }

    /// <summary>Gets the translator's name.</summary>
    public Symbol Name { get; }

    /// <summary>Makes one translator for one context.</summary>
    /// <param name="context">The context the translator will live in.</param>
    /// <returns>The translator.</returns>
    public Translator Call(Context context) => _allocate(context);

    /// <summary>Returns the external representation.</summary>
    /// <returns>The creator's name.</returns>
    public override string ToString() => "#<Translator_creator " + Name.Name + ">";
}

/// <summary>
/// The name-to-creator table every <c>\consists</c> is resolved through, and the place
/// the port's own C++-side translators announce themselves.
/// <para>
/// Upstream's <c>ADD_TRANSLATOR</c> macro runs at static-initialisation time and calls
/// <c>add_translator_creator</c>; C# has no equivalent hook that is guaranteed to have
/// run before the Scheme layer loads, so the ported translators are listed once, here,
/// and <see cref="RegisterBuiltIn"/> is called when the interpreter's registries are
/// built. The list IS the port's <c>ADD_TRANSLATOR</c> set, and gate G4 measures it
/// against <c>Scheme/translators.tsv</c>.
/// </para>
/// </summary>
public static class TranslatorRegistry
{
    private static readonly Symbol EngraverGroupSymbol = Symbol.Intern("Engraver_group");
    private static readonly Symbol PerformerGroupSymbol = Symbol.Intern("Performer_group");
    private static readonly Symbol ScoreEngraverSymbol = Symbol.Intern("Score_engraver");
    private static readonly Symbol ScorePerformerSymbol = Symbol.Intern("Score_performer");

    /// <summary>
    /// Registers every translator the port carries in C#, into the registry
    /// <c>ly:register-translator</c> shares.
    /// </summary>
    /// <param name="registries">The registries to fill.</param>
    public static void RegisterBuiltIn(EngineRegistries registries)
    {
        if (registries == null)
        {
            throw new ArgumentNullException(nameof(registries));
        }

        Add(registries, "Staff_symbol_engraver", c => new StaffSymbolEngraver(c));
        Add(registries, "Clef_engraver", c => new ClefEngraver(c));
        Add(registries, "Note_heads_engraver", c => new NoteHeadsEngraver(c));
        Add(registries, "Axis_group_engraver", c => new AxisGroupEngraver(c));
        Add(registries, "Paper_column_engraver", c => new PaperColumnEngraver(c));
        Add(registries, "Spacing_engraver", c => new SpacingEngraver(c));
        Add(registries, "Note_spacing_engraver", c => new NoteSpacingEngraver(c));
        Add(registries, "Separating_line_group_engraver",
            c => new SeparatingLineGroupEngraver(c));
    }

    /// <summary>
    /// Returns the creator a translator name resolves to, warning when there is none.
    /// <para>
    /// The warning is the demand loop's signal, not noise: every unknown name is a
    /// translator this port has not reached yet, and <c>ly/engraver-init.ly</c> names
    /// them all. Silence here would turn a missing engraver into missing OUTPUT with
    /// nothing to explain it.
    /// </para>
    /// </summary>
    /// <param name="name">The translator name.</param>
    /// <returns>The creator, or <see langword="null"/> when the name is unknown.</returns>
    public static object GetTranslatorCreator(Symbol name)
    {
        EngineRegistries registries = LilyPondScheme.Registries;
        if (registries != null
            && name != null
            && registries.Translators.TryGetValue(name, out object creator))
        {
            return creator;
        }

        Warn.Warning("unknown translator: `" + (name == null ? "()" : name.Name) + "'");
        return null;
    }

    /// <summary>
    /// Returns the translator names <c>Scheme/translators.tsv</c> declares that this
    /// registry cannot answer for — gate G4's measurement, COMPUTED rather than
    /// remembered.
    /// </summary>
    /// <param name="registries">The registries to measure.</param>
    /// <param name="declared">The declared names, from the manifest.</param>
    /// <returns>The missing names, in the manifest's order.</returns>
    public static IReadOnlyList<string> MissingTranslators(
        EngineRegistries registries,
        IEnumerable<string> declared)
    {
        List<string> missing = new List<string>();
        if (registries == null || declared == null)
        {
            return missing;
        }

        foreach (string name in declared)
        {
            if (!registries.Translators.ContainsKey(Symbol.Intern(name)))
            {
                missing.Add(name);
            }
        }

        return missing;
    }

    /// <summary>
    /// Makes the translator GROUP a context definition's <c>\type</c> names.
    /// <para>Upstream: <c>get_translator_group</c> in
    /// <c>lily/translator-group-ctors.cc</c>, comment and all.</para>
    /// </summary>
    /// <param name="symbol">The group type name.</param>
    /// <returns>The group, or <see langword="null"/> when the name is not a group type.</returns>
    public static TranslatorGroup GetTranslatorGroup(object symbol)
    {
        /*
          Quick & dirty.
        */
        if (ReferenceEquals(symbol, EngraverGroupSymbol))
        {
            return new EngraverGroup();
        }
        else if (ReferenceEquals(symbol, PerformerGroupSymbol))
        {
            return new PerformerGroupPlaceholder();
        }
        else if (ReferenceEquals(symbol, ScoreEngraverSymbol))
        {
            return new ScoreEngraver();
        }
        else if (ReferenceEquals(symbol, ScorePerformerSymbol))
        {
            return new PerformerGroupPlaceholder();
        }

        Warn.Error(
            "Couldn't find translator type " + symbol
            + " (should be Engraver_group, Performer_group, "
            + "Score_engraver or Score_performer)");
        return null;
    }

    private static void Add(
        EngineRegistries registries,
        string name,
        Func<Context, Translator> allocate)
    {
        Symbol symbol = Symbol.Intern(name);
        registries.Translators[symbol] = new TranslatorCreator(symbol, allocate);
        registries.TranslatorDescriptions[symbol] = Nil.Instance;
    }
}

/// <summary>
/// The stand-in for <c>Performer_group</c> and <c>Score_performer</c> until EPG19 ports
/// the MIDI subsystem.
/// <para>
/// It is a real, empty group rather than a null return, and the difference matters: a
/// null group would make <c>\midi</c> blocks fail at context creation with a
/// programming error, whereas an empty one builds the same context tree and simply
/// produces no performance. That keeps the MIDI half of <c>ly/performer-init.ly</c>
/// loadable and the LAYOUT half — the whole of EPG2's exit criterion — unaffected by
/// work that has not been done yet. Recorded in PORT-COVERAGE.
/// </para>
/// </summary>
public sealed class PerformerGroupPlaceholder : TranslatorGroup
{
    /// <summary>Gets the C++ class name this group corresponds to.</summary>
    public override string ClassName => "Performer_group";
}
