// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Parsing.Session;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort;

/// <summary>
/// Standing up a LilyPond session: the Scheme layer, then the <c>ly/</c> init layer,
/// then the <c>$defaultlayout</c> everything is engraved under.
/// <para>
/// This is the seam between the two halves of the port. The <c>scm/</c> layer defines
/// the grob descriptions, the markup commands and the music types; the <c>ly/</c> layer
/// — read THROUGH THE PARSER — defines the context definitions, the note names and
/// every music function. Neither is optional: a score engraved without the second one
/// has no idea what a Staff is.
/// </para>
/// <para>
/// Both layers are process-global state (plan risk 7), so this class serialises and
/// caches. The cache is keyed on the ambient interpreter, because tests replace it: a
/// layout carried over from a dead interpreter holds context definitions whose grob
/// descriptions belong to nothing, which is silent rather than loud.
/// </para>
/// </summary>
public static class LilyPondInit
{
    private static readonly object Gate = new object();
    private static readonly Symbol DefaultLayoutSymbol = Symbol.Intern("$defaultlayout");
    private static readonly Symbol DefaultPaperSymbol = Symbol.Intern("$defaultpaper");
    private static readonly Symbol DefaultMidiSymbol = Symbol.Intern("$defaultmidi");
    private static readonly Symbol OutputScaleSymbol = Symbol.Intern("output-scale");

    private static Interpreter _loadedFor;
    private static OutputDef _defaultLayout;
    private static OutputDef _defaultPaper;
    private static OutputDef _defaultMidi;
    private static LilyParserSession _session;
    private static IReadOnlyList<string> _diagnostics = Array.Empty<string>();

    private static object _noteNamesSnapshot;
    private static IReadOnlyDictionary<string, object> _optionsSnapshot;

    private static Dictionary<Symbol, object> _layoutSnapshot;
    private static Dictionary<Symbol, object> _paperSnapshot;
    private static Dictionary<Symbol, object> _midiSnapshot;

    /// <summary>
    /// Gets the <c>$defaultpaper</c> the init layer builds, loading both layers on
    /// first use.
    /// </summary>
    /// <returns>The paper, or <see langword="null"/> when the layer defines none.</returns>
    public static OutputDef DefaultPaper()
    {
        lock (Gate)
        {
            DefaultLayoutLocked();
            return _defaultPaper;
        }
    }

    /// <summary>
    /// Puts <c>$defaultpaper</c> and <c>$defaultlayout</c> back the way the init layer
    /// left them.
    /// <para>
    /// Upstream never needs this: LilyPond engraves ONE input file per process, so the
    /// defaults a file mutates die with it. A batch runner shares one session across
    /// two thousand files, and the mutations are not hypothetical — a toplevel
    /// <c>\paper</c> block, and above all <c>#(set-global-staff-size 30)</c>, rewrite
    /// the shared definition and every LATER file inherits it. That is a whole-suite
    /// measurement error rather than one file's: it silently rescales everything
    /// downstream of the offender.
    /// </para>
    /// <para>
    /// BOTH halves are needed, and the second is the one that bites. A
    /// <c>\paper</c> block mutates the definition IN PLACE, so the variables have to go
    /// back; but <c>set-global-staff-size</c> does something else entirely — it CLONES
    /// the paper, scales the clone, and rebinds the toplevel <c>$defaultpaper</c>
    /// identifier to it, deliberately leaving the original object untouched ("maybe
    /// someone still refers to the old one"). Restoring only the object therefore fixes
    /// nothing at all: the next book still picks up the clone by name.
    /// </para>
    /// <para>
    /// The old LIMIT here — "a variable a file INVENTS is left in place, because the
    /// scope has no unbind" — RETIRED:
    /// <see cref="Parsing.Session.LilyParserSession.RestoreToplevelScope"/> now removes
    /// invented bindings and reverts overwritten ones, which is upstream's
    /// one-parser-per-file semantics. The ssaattbb templates were the measured victim.
    /// </para>
    /// </summary>
    public static void RestoreDefaults()
    {
        lock (Gate)
        {
            Restore(_defaultPaper, _paperSnapshot);
            Restore(_defaultLayout, _layoutSnapshot);
            Restore(_defaultMidi, _midiSnapshot);

            // THE NINTH LEAK: the base scope itself — every toplevel assignment and
            // #(define ...) a file made. Removed/reverted FIRST, so the identifier
            // re-pointing below lands on the restored scope. See the snapshot note
            // in Load() and LilyParserSession.RestoreToplevelScope.
            _session?.RestoreToplevelScope();

            if (_session != null)
            {
                if (_defaultPaper != null)
                {
                    _session.SetIdentifier(DefaultPaperSymbol, _defaultPaper);
                }

                if (_defaultLayout != null)
                {
                    _session.SetIdentifier(DefaultLayoutSymbol, _defaultLayout);
                }

                // THE SEVENTH LEAK, same shape as $defaultpaper and $defaultlayout
                // (found through the MIDI comparator): a TOPLEVEL \midi
                // block REBINDS the $defaultmidi identifier to its clone, and nothing
                // put the original back. midi2ly-generated regression files set
                // midiChannelMapping = #'instrument in exactly such a block, so every
                // file swept after the first of them performed under 'instrument
                // mapping instead of performer-init.ly's 'staff — visible as every
                // staff landing on MIDI channel 0. Run alone, the same file is
                // correct: the full-sweep-only trap, MIDI edition.
                if (_defaultMidi != null)
                {
                    _session.SetIdentifier(DefaultMidiSymbol, _defaultMidi);
                }

                // THE THIRD LEAK, and it is the same shape as the other two: \language
                // rebinds (lily)'s `pitchnames' through ly:parser-set-note-names, and
                // nothing put it back. One regression file includes arabic.ly, which
                // opens with \language "italiano" -- so every file swept after it was
                // parsed with ITALIAN note names, and the whole \partCombine family
                // read as "not a note name: g". Upstream never needs this: it engraves
                // one file per process.
                if (_noteNamesSnapshot != null)
                {
                    _session.SetNoteNames(_noteNamesSnapshot);
                }
            }

            // THE TENTH LEAK: the PROGRAM OPTIONS. `ly:set-option' writes a
            // process-global store, and upstream engraves one file per process, so an
            // option a file sets cannot outlive it there. Here it did:
            // skyline-debug.ly opens with (ly:set-option 'debug-skylines #t), and
            // System and VerticalAxisGroup default show-vertical-skylines to
            // grob::show-skylines-if-debug-skylines-set, which reads that option AT
            // STENCIL TIME — so every one of the 376 files swept after it drew the
            // debug skyline outlines over its own music.
            //
            // INVISIBLE UNTIL THE DRAWING EXISTED. The option leaked for the life of
            // the port and cost nothing, because Grob::get_print_stencil's
            // add_skylines block had never been ported and there was nothing for the
            // flag to switch on. Porting the block is what turned a dormant leak into
            // 376 regressed pages in one sweep — trap 28 exactly, and the reason the
            // ratchet is read before the floor is advanced.
            LilyPondScheme.Options?.RestoreValues(_optionsSnapshot);

            // THE FIFTH LEAK, AND IT IS THE OLDEST AND WIDEST OF THEM (found through the
            // MIDI comparator). Lily_parser::default_duration_ is what a note with NO written
            // duration inherits, and upstream makes ONE PARSER PER FILE, so it starts
            // every file at a quarter note. The port has one session for the whole sweep,
            // so file N's last written duration became file N+1's default -- and since a
            // file that writes no durations at all writes nothing to correct it, the
            // error persisted for as long as the run did.
            //
            // FOUND THROUGH MIDI, WHICH IS WHY IT SURVIVED SO LONG: the layout comparator
            // grades glyph inventories, and a page whose notes are all half length holds
            // the SAME glyphs in the same order. The MIDI comparator grades TICKS, and a
            // note-off at 192 where the oracle says 384 is not a near miss. Measured both
            // ways: crescendo-return-crescendo.ly run ALONE is correct, and inside the
            // sweep it is halved -- the full-sweep-only trap in reverse.
            //
            // DefaultTremoloType is restored beside it: same member class, same lifetime,
            // and nothing had put it back either.
            if (_session != null)
            {
                _session.DefaultDuration = new Engine.Music.Duration(2, 0);
                _session.DefaultTremoloType = 8;
            }

            // THE SIXTH LEAK, and the MIDI layer added the state that could leak.
            // Staff_performer keeps its instrument-to-channel map in STATIC members,
            // because upstream gets one process per file and clears them when the last
            // Staff_performer finalizes. A score that dies before finalize would hand its
            // channel assignments to the next file in the sweep, which is exactly the
            // shape of the three leaks above. Upstream's own mechanism is reproduced in
            // StaffPerformer; this is the belt to its braces.
            Engine.Translation.StaffPerformer.ResetStaticChannelState();

            // THE EIGHTH LEAK, and unlike the others it points at a MECHANISM rather
            // than a variable (found through the MIDI comparator). Upstream
            // re-initializes every `define-session' variable per file — scm/lily.scm's
            // session machinery exists precisely for multi-file invocations.
            //
            // ⚠ THE PORT NOW DRIVES THAT MACHINERY, AND THE NOTE THAT USED TO STAND HERE
            // SAID IT DID NOT. It hand-restored the ONE session variable then measured to
            // leak — `unique-counter', which names the voices \addlyrics generates. That
            // was one variable out of every variable `define-session' declares, and the
            // TWELFTH leak was a second one: `bar-line.scm''s
            // volta-bracket-allow-volta-hook-list. `#(allow-volta-hook "|")' APPENDS to it,
            // volta-bracket-add-volta-hook.ly does exactly that, and the file swept
            // immediately after it — volta-bracket-nest.ly — then drew volta hooks on bar
            // lines that should not carry them, 2.0 staff spaces of edge-height each. Run
            // alone that file MATCHES; in the sweep it does not. The full-sweep-only trap
            // (trap 2), and trap 16's rule — suspect this class FIRST — named it in eleven
            // bisect runs.
            //
            // WHAT UPSTREAM DOES, and what this is: `session-terminate' has three steps —
            // module restore, DECLARATION restore, and the after-session hook — and the
            // port had the first and the third. This is the second, and it covers every
            // define-session variable at once rather than the two now known to leak.
            // `#(session-save)' is already called for us, by ly/declarations-init.ly's
            // last line, which is what makes the recorded values the POST-LOAD ones — for
            // this list that matters, because bar-line.scm's own twelve
            // `(allow-volta-hook ...)' calls run at load time and must survive the reset.
            CodeBrix.LilyScheme.Interpreter interpreter = Engine.Bootstrap.LilyPondScheme.Current;
            CodeBrix.LilyScheme.Runtime.SchemeModule lilyModule = interpreter?.Modules?.Resolve(
                CodeBrix.LilyScheme.Values.Pair.List(Symbol.Intern("lily")));
            RestoreSessionDeclarations(lilyModule);

            // THE ELEVENTH LEAK, and it is the OTHER half of the eighth: upstream's
            // `session-terminate' ends with `(run-hook after-session-hook)', and the
            // port ran nothing. `call-after-session' is how the vendored layer registers
            // its own per-file resets, and five of them were registered and never fired:
            // toc-init.ly's THREE tables (which is why a table of contents accumulated
            // every earlier file's entries — page-label-loose-column listed a "Second
            // part" and a "Third part" that belong to another file entirely),
            // music-functions.scm's `reset-tag-groups', define-event-classes.scm's
            // `ancestor-lookup-initialize', and declarations-init.ly's fret, chord-shape
            // and music-quote hash tables.
            //
            // Registering a reset and never calling it is trap 17a's shape once more:
            // the WRITE side is faithful, complete and visible to a grep, and the thing
            // that was supposed to call it is missing.
            RunAfterSessionHook(interpreter, lilyModule);
        }
    }

    /// <summary>
    /// Puts every <c>define-session</c> variable back to the value <c>session-save</c>
    /// recorded — the middle step of <c>scm/lily.scm</c>'s <c>session-terminate</c>.
    /// </summary>
    /// <param name="lilyModule">The <c>(lily)</c> module, or <see langword="null"/>.</param>
    /// <remarks>
    /// <c>lilypond-declarations</c> is a list of <c>(cons* SYMBOL IS-PARSER? VAR VALUE)</c>
    /// tuples, so each element is an improper list whose fourth position IS the value
    /// rather than holding it. Upstream's whole step is
    /// <c>(for-each (lambda (p) (variable-set! (caddr p) (cdddr p))) lilypond-declarations)</c>.
    /// <para>
    /// The list is private to <c>(lily)</c> — it is not exported — which is why it is
    /// walked here rather than through a binding, exactly as <c>unique-counter</c> was.
    /// </para>
    /// </remarks>
    private static void RestoreSessionDeclarations(
        CodeBrix.LilyScheme.Runtime.SchemeModule lilyModule)
    {
        CodeBrix.LilyScheme.Values.Variable declarations
            = lilyModule?.Lookup(Symbol.Intern("lilypond-declarations"));
        if (declarations == null || !declarations.IsBound)
        {
            return;
        }

        object node = declarations.GetValue();
        while (node is CodeBrix.LilyScheme.Values.Pair cell)
        {
            if (cell.Car is CodeBrix.LilyScheme.Values.Pair entry
                && entry.Cdr is CodeBrix.LilyScheme.Values.Pair afterSymbol
                && afterSymbol.Cdr is CodeBrix.LilyScheme.Values.Pair afterFlag
                && afterFlag.Car is CodeBrix.LilyScheme.Values.Variable variable)
            {
                variable.SetValue(afterFlag.Cdr);
            }

            node = cell.Cdr;
        }
    }

    /// <summary>
    /// Runs the vendored layer's <c>after-session-hook</c>, which is the last thing
    /// <c>scm/lily.scm</c>'s <c>session-terminate</c> does.
    /// </summary>
    /// <param name="interpreter">The interpreter, or <see langword="null"/>.</param>
    /// <param name="lilyModule">The <c>(lily)</c> module, or <see langword="null"/>.</param>
    private static void RunAfterSessionHook(
        CodeBrix.LilyScheme.Interpreter interpreter,
        CodeBrix.LilyScheme.Runtime.SchemeModule lilyModule)
    {
        if (interpreter == null || lilyModule == null)
        {
            return;
        }

        CodeBrix.LilyScheme.Values.Variable hook
            = lilyModule.Lookup(Symbol.Intern("after-session-hook"));
        if (hook == null || !hook.IsBound)
        {
            return;
        }

        CodeBrix.LilyScheme.Values.Variable runHook
            = lilyModule.Lookup(Symbol.Intern("run-hook"));
        if (runHook == null || !runHook.IsBound)
        {
            return;
        }

        interpreter.Evaluator.Apply(runHook.GetValue(), new object[] { hook.GetValue() });
    }

    private static void Restore(OutputDef definition, Dictionary<Symbol, object> snapshot)
    {
        if (definition == null || snapshot == null)
        {
            return;
        }

        foreach (KeyValuePair<Symbol, object> entry in snapshot)
        {
            definition.SetVariable(entry.Key, entry.Value);
        }
    }

    private static Dictionary<Symbol, object> Snapshot(OutputDef definition)
    {
        if (definition == null)
        {
            return null;
        }

        Dictionary<Symbol, object> snapshot = new Dictionary<Symbol, object>();
        foreach (KeyValuePair<Symbol, object> entry in definition.Variables())
        {
            snapshot[entry.Key] = entry.Value;
        }

        return snapshot;
    }

    /// <summary>
    /// Gets the <c>$defaultlayout</c> the init layer builds, loading both layers on
    /// first use.
    /// </summary>
    /// <returns>The layout, carrying every context definition.</returns>
    public static OutputDef DefaultLayout()
    {
        lock (Gate)
        {
            return DefaultLayoutLocked();
        }
    }

    private static OutputDef DefaultLayoutLocked()
    {
        Interpreter ambient = LilyPondScheme.Current;
        if (_defaultLayout != null && ReferenceEquals(_loadedFor, ambient) && ambient != null)
        {
            return _defaultLayout;
        }

        return Load();
    }

    /// <summary>
    /// Gets what the init layer reported when it was last loaded. Empty is the expected
    /// state and the ratchet <c>InitLayerProbeTests</c> fences it.
    /// </summary>
    public static IReadOnlyList<string> Diagnostics
    {
        get
        {
            lock (Gate)
            {
                return _diagnostics;
            }
        }
    }

    /// <summary>
    /// Gets THE parser session the init layer was read into, loading on first use.
    /// <para>
    /// There is deliberately one: the interpreter's session-lifecycle guards
    /// (<c>call-after-session</c>) fire if a second session replays the init layer,
    /// exactly as upstream's would if <c>init.ly</c> ran twice without
    /// <c>session-replay</c>. The batch runner parses every file through this
    /// session, resetting the toplevel collection state per file with its prologue.
    /// </para>
    /// </summary>
    /// <returns>The session, with both layers loaded.</returns>
    public static LilyParserSession Session()
    {
        lock (Gate)
        {
            DefaultLayoutLocked();
            return _session;
        }
    }

    /// <summary>Forgets the cached layout, so the next call reloads both layers.</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _loadedFor = null;
            _defaultLayout = null;
            _defaultPaper = null;
            _defaultMidi = null;
            _session = null;
            _diagnostics = Array.Empty<string>();
            _layoutSnapshot = null;
            _paperSnapshot = null;
            _midiSnapshot = null;
        }
    }

    private static OutputDef Load()
    {
        OutputDef layout = null;
        IReadOnlyList<string> diagnostics = Array.Empty<string>();

        // psyntax recurses hard enough to overflow the default stack, and the init
        // layer expands a great deal of it.
        Interpreter.RunWithLargeStack(() =>
        {
            Interpreter interpreter = LilyPondScheme.Current;
            if (interpreter == null)
            {
                interpreter = LilyPondScheme.CreateInterpreter();
                LilyPondScheme.LoadViaLilyScm(interpreter);
            }

            LilyParserSession session = new LilyParserSession(interpreter);
            ParseOutcome outcome = session.LoadInitLayer();
            diagnostics = outcome.AllDiagnostics();

            layout = session.LookupIdentifier(DefaultLayoutSymbol.Name) as OutputDef;
            _defaultPaper = session.LookupIdentifier(DefaultPaperSymbol.Name) as OutputDef;
            _defaultMidi = session.LookupIdentifier(DefaultMidiSymbol.Name) as OutputDef;
            _session = session;
        });

        if (layout == null)
        {
            throw new InvalidOperationException(
                "the ly/ init layer produced no $defaultlayout"
                + (diagnostics.Count == 0
                    ? " and reported nothing"
                    : "; it reported: " + string.Join(" || ", diagnostics)));
        }

        // ly/paper-defaults-init.ly is what normally supplies the staff size and the
        // units, through $defaultpaper. When the layout cannot resolve output-scale the
        // paper half did not arrive, and every dimension below would silently come out
        // zero — so the reconstructed defaults are parented in rather than guessed at
        // each use. Recorded in PORT-COVERAGE.
        if (layout.LookupVariable(OutputScaleSymbol) == null)
        {
            OutputDef fallback = PaperDefaults.Create();
            OutputDef root = layout;
            while (root.Parent != null)
            {
                root = root.Parent;
            }

            root.Parent = fallback;
        }

        _loadedFor = LilyPondScheme.Current;
        _defaultLayout = layout;
        _diagnostics = diagnostics;

        // Taken AFTER the fallback parenting above, so a restore puts back the state
        // the first file would have seen rather than a half-built one.
        _layoutSnapshot = Snapshot(_defaultLayout);
        _paperSnapshot = Snapshot(_defaultPaper);
        _midiSnapshot = Snapshot(_defaultMidi);

        // The note-name table the init layer settled on, so a file that says
        // \language (or includes one that does) cannot rename the notes for the rest
        // of the suite. See RestoreDefaults.
        _noteNamesSnapshot = _session.NoteNames();

        // THE TENTH LEAK: the PROGRAM OPTIONS. See RestoreDefaults.
        _optionsSnapshot = LilyPondScheme.Options?.SnapshotValues();

        // THE NINTH LEAK (found by bisecting
        // the sweep against ssaattbb-template-with-all-staves): the parser's BASE
        // SCOPE. Upstream makes one parser per file, so a file's toplevel
        // assignments die with it; this shared session kept every one of them, and
        // the built-in vocal templates read OPTIONAL variables — one template file's
        // leftover `Time = { s1 \break s1 }' forced a line break inside every later
        // template. The snapshot is names, variable identities AND values;
        // RestoreDefaults plays it back per file. See LilyParserSession.
        _session.SnapshotToplevelScope();

        // ly:parse-file and ly:parse-init were early bindings deferred to the
        // batch runner by decision; the runner exists, so every real session gets
        // them the moment the layers are up.
        BatchRunner.InstallSessionBindings(LilyPondScheme.Current);
        return layout;
    }
}
