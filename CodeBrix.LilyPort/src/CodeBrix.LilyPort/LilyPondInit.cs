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
    private static readonly Symbol OutputScaleSymbol = Symbol.Intern("output-scale");

    private static Interpreter _loadedFor;
    private static OutputDef _defaultLayout;
    private static OutputDef _defaultPaper;
    private static LilyParserSession _session;
    private static IReadOnlyList<string> _diagnostics = Array.Empty<string>();

    private static object _noteNamesSnapshot;

    private static Dictionary<Symbol, object> _layoutSnapshot;
    private static Dictionary<Symbol, object> _paperSnapshot;

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
    /// LIMIT, recorded in PORT-COVERAGE: this restores the value of every variable the
    /// definitions had after initialisation, which covers the numeric leaks. A variable
    /// a file INVENTS is left in place, because the scope has no unbind.
    /// </para>
    /// </summary>
    public static void RestoreDefaults()
    {
        lock (Gate)
        {
            Restore(_defaultPaper, _paperSnapshot);
            Restore(_defaultLayout, _layoutSnapshot);

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
        }
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
            _session = null;
            _diagnostics = Array.Empty<string>();
            _layoutSnapshot = null;
            _paperSnapshot = null;
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

        // The note-name table the init layer settled on, so a file that says
        // \language (or includes one that does) cannot rename the notes for the rest
        // of the suite. See RestoreDefaults.
        _noteNamesSnapshot = _session.NoteNames();

        // ly:parse-file and ly:parse-init were EPG1 bindings deferred "to EPG3's
        // batch runner by decision"; the runner exists, so every real session gets
        // them the moment the layers are up.
        BatchRunner.InstallSessionBindings(LilyPondScheme.Current);
        return layout;
    }
}
