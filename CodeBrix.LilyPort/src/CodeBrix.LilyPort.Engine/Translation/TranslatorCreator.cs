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

        // EPG5 — columns, rests, dots, collisions.
        Add(registries, "Rest_engraver", c => new RestEngraver(c));
        Add(registries, "Rest_collision_engraver", c => new RestCollisionEngraver(c));
        Add(registries, "Rhythmic_column_engraver", c => new RhythmicColumnEngraver(c));
        Add(registries, "Collision_engraver", c => new CollisionEngraver(c));
        Add(registries, "Dot_column_engraver", c => new DotColumnEngraver(c));
        Add(registries, "Dots_engraver", c => new DotsEngraver(c));
        Add(registries, "Completion_heads_engraver", c => new CompletionHeadsEngraver(c));
        Add(registries, "Completion_rest_engraver", c => new CompletionRestEngraver(c));
        Add(registries, "Multi_measure_rest_engraver",
            c => new MultiMeasureRestEngraver(c));

        // EPG6 — stems and flags.
        Add(registries, "Stem_engraver", c => new StemEngraver(c));

        // EPG7 — vertical organization.
        Add(registries, "Vertical_align_engraver", c => new VerticalAlignEngraver(c));
        Add(registries, "System_start_delimiter_engraver",
            c => new SystemStartDelimiterEngraver(c));
        Add(registries, "Staff_collecting_engraver", c => new StaffCollectingEngraver(c));

        // EPG8 — bars, meter, keys, marks.
        Add(registries, "Bar_engraver", c => new BarEngraver(c));
        Add(registries, "Span_bar_engraver", c => new SpanBarEngraver(c));
        Add(registries, "Span_bar_stub_engraver", c => new SpanBarStubEngraver(c));
        Add(registries, "Bar_number_engraver", c => new BarNumberEngraver(c));
        Add(registries, "Key_engraver", c => new KeyEngraver(c));
        Add(registries, "Time_signature_engraver", c => new TimeSignatureEngraver(c));
        Add(registries, "Timing_translator", c => new TimingTranslator(c));
        Add(registries, "Metronome_mark_engraver", c => new MetronomeMarkEngraver(c));
        Add(registries, "Mark_engraver", c => new MarkEngraver(c));
        Add(registries, "Mark_tracking_translator", c => new MarkTrackingTranslator(c));
        Add(registries, "Jump_engraver", c => new JumpEngraver(c));
        Add(registries, "Caesura_engraver", c => new CaesuraEngraver(c));
        Add(registries, "Grid_line_span_engraver", c => new GridLineSpanEngraver(c));
        Add(registries, "Grid_point_engraver", c => new GridPointEngraver(c));

        // EPG9 — accidentals and pitch machinery.
        Add(registries, "Accidental_engraver", c => new AccidentalEngraver(c));
        Add(registries, "Ambitus_engraver", c => new AmbitusEngraver(c));
        Add(registries, "Pitched_trill_engraver", c => new PitchedTrillEngraver(c));
        Add(registries, "Pitch_squash_engraver", c => new PitchSquashEngraver(c));
        Add(registries, "Note_name_engraver", c => new NoteNameEngraver(c));
        Add(registries, "Cue_clef_engraver", c => new CueClefEngraver(c));

        // EPG10 — beams.
        Add(registries, "Beam_engraver", c => new BeamEngraver(c));
        Add(registries, "Grace_beam_engraver", c => new GraceBeamEngraver(c));
        Add(registries, "Auto_beam_engraver", c => new AutoBeamEngraver(c));
        Add(registries, "Grace_auto_beam_engraver", c => new GraceAutoBeamEngraver(c));
        Add(registries, "Beam_collision_engraver", c => new BeamCollisionEngraver(c));
        Add(registries, "Chord_tremolo_engraver", c => new ChordTremoloEngraver(c));

        // EPG22 — iterators and music plumbing.
        Add(registries, "Part_combine_engraver", c => new PartCombineEngraver(c));

        // EPG17 — repeats, voltas, percent, tuplets, grace.
        Add(registries, "Volta_engraver", c => new VoltaEngraver(c));
        Add(registries, "Repeat_acknowledge_engraver",
            c => new RepeatAcknowledgeEngraver(c));
        Add(registries, "Tuplet_engraver", c => new TupletEngraver(c));
        Add(registries, "Percent_repeat_engraver", c => new PercentRepeatEngraver(c));
        Add(registries, "Double_percent_repeat_engraver",
            c => new DoublePercentRepeatEngraver(c));
        Add(registries, "Slash_repeat_engraver", c => new SlashRepeatEngraver(c));
        Add(registries, "Grace_engraver", c => new GraceEngraver(c));
        Add(registries, "Grace_spacing_engraver", c => new GraceSpacingEngraver(c));

        // EPG10's grob-pq-engraver.cc, PULLED FORWARD by EPG18: busyGrobs has no other
        // writer, and lyric extenders cannot find their note heads without it.
        Add(registries, "Grob_pq_engraver", c => new GrobPqEngraver(c));

        // EPG18 — lyrics and melody.
        Add(registries, "Lyric_engraver", c => new LyricEngraver(c));
        Add(registries, "Extender_engraver", c => new ExtenderEngraver(c));
        Add(registries, "Hyphen_engraver", c => new HyphenEngraver(c));
        Add(registries, "Melody_engraver", c => new MelodyEngraver(c));

        // EPG11 (2026-08-08): ties.
        Add(registries, "Tie_engraver", c => new TieEngraver(c));
        Add(registries, "Laissez_vibrer_engraver", c => new LaissezVibrerEngraver(c));
        Add(registries, "Repeat_tie_engraver", c => new RepeatTieEngraver(c));

        // EPG12 (2026-08-08): slurs.
        Add(registries, "Slur_engraver", c => new SlurEngraver(c));
        Add(registries, "Phrasing_slur_engraver", c => new PhrasingSlurEngraver(c));

        // EPG14 (2026-08-08): scripts, dynamics, brackets, pedals, fingering.
        Add(registries, "Script_engraver", c => new ScriptEngraver(c));
        Add(registries, "Script_column_engraver", c => new ScriptColumnEngraver(c));
        Add(registries, "Script_row_engraver", c => new ScriptRowEngraver(c));
        Add(registries, "Non_musical_script_column_engraver",
            c => new NonMusicalScriptColumnEngraver(c));
        Add(registries, "Text_engraver", c => new TextEngraver(c));
        Add(registries, "Text_spanner_engraver", c => new TextSpannerEngraver(c));
        Add(registries, "Ottava_spanner_engraver", c => new OttavaSpannerEngraver(c));
        Add(registries, "Dynamic_engraver", c => new DynamicEngraver(c));
        Add(registries, "Dynamic_align_engraver", c => new DynamicAlignEngraver(c));
        Add(registries, "Concurrent_hairpin_engraver",
            c => new ConcurrentHairpinEngraver(c));
        Add(registries, "Piano_pedal_engraver", c => new PianoPedalEngraver(c));
        Add(registries, "Piano_pedal_align_engraver",
            c => new PianoPedalAlignEngraver(c));
        Add(registries, "Fingering_engraver", c => new FingeringEngraver(c));
        Add(registries, "New_fingering_engraver", c => new NewFingeringEngraver(c));
        Add(registries, "Fingering_column_engraver", c => new FingeringColumnEngraver(c));
        Add(registries, "Font_size_engraver", c => new FontSizeEngraver(c));
        Add(registries, "Tweak_engraver", c => new TweakEngraver(c));
        Add(registries, "Balloon_engraver", c => new BalloonEngraver(c));
        Add(registries, "Parenthesis_engraver", c => new ParenthesisEngraver(c));
        Add(registries, "Instrument_name_engraver", c => new InstrumentNameEngraver(c));
        Add(registries, "Instrument_switch_engraver",
            c => new InstrumentSwitchEngraver(c));
        Add(registries, "Horizontal_bracket_engraver",
            c => new HorizontalBracketEngraver(c));
        Add(registries, "Ledger_line_engraver", c => new LedgerLineEngraver(c));
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
