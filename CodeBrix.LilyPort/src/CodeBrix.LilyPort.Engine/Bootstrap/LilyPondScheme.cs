// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// Access to LilyPond's vendored Scheme layer, and the bootstrap that loads it on top of
/// LilyScheme.
/// <para>
/// The 91 files under <c>Scheme/lily</c> are a byte-identical mirror of LilyPond's
/// <c>scm/</c> directory at v2.27.2. They are loaded in LilyPond's own order, taken from
/// <c>scm/lily.scm</c>, because the files define things for each other and loading them
/// alphabetically produces a cascade of spurious unbound-variable failures.
/// </para>
/// </summary>
public static class LilyPondScheme
{
    private const string LoadOrderResource = "load-order.txt";
    private const string LilyFolderMarker = ".Scheme.lily.";

    private const string LyFolderMarker = ".Scheme.ly.";

    private static readonly string[] LoadOrderCache = ReadLoadOrder();

    /// <summary>
    /// Gets the program-option table the most recently created interpreter uses. It is
    /// also where the <c>ly:warning</c> family's output goes, so a test can read back
    /// what LilyPond's Scheme complained about while loading.
    /// </summary>
    public static ProgramOptions Options { get; private set; } = new ProgramOptions();

    /// <summary>
    /// Gets the registries the most recently created interpreter fills in: grob
    /// interfaces, translators and stencil heads. LilyPond's Scheme populates all three
    /// while it loads, so their contents are a direct measure of how much of the layer
    /// actually ran.
    /// </summary>
    public static EngineRegistries Registries { get; private set; } = new EngineRegistries();

    /// <summary>
    /// Gets the interpreter the engine talks to.
    /// <para>
    /// Process-global on purpose: LilyPond's C++ reaches one Guile through file-scope
    /// state, and the object model needs the same reach for property type checks and
    /// for calling Scheme callbacks. Plan risk 7 records the consequence — tests that
    /// build an interpreter must serialise.
    /// </para>
    /// </summary>
    public static Interpreter Current { get; private set; }

    /// <summary>
    /// Restores a previously captured ambient interpreter — the restore half of a
    /// save/restore around a fixture that publishes a BARE interpreter through
    /// <see cref="CreateInterpreter"/> without loading the Scheme layer. A bare
    /// ambient interpreter has empty property tables, so leaving one published makes
    /// every later <c>Context.SetProperty</c> in the process silently refuse its
    /// type check — the exact defect the publication comment in
    /// <see cref="CreateInterpreter"/> records for the half-built window.
    /// </summary>
    /// <param name="interpreter">The interpreter to publish again, or
    /// <see langword="null"/> when none was ambient.</param>
    internal static void RestoreAmbient(Interpreter interpreter) => Current = interpreter;

    /// <summary>
    /// Looks a name up in the ambient interpreter's current module.
    /// <para>
    /// Upstream reaches the same values through <c>lily-imports.hh</c>, which resolves
    /// each one lazily out of the <c>(lily)</c> module and caches it. The port resolves
    /// per call rather than caching, because the ambient interpreter is REPLACED between
    /// tests and a cached procedure would then belong to a dead one — a class of defect
    /// that is silent, because the stale procedure still runs.
    /// </para>
    /// </summary>
    /// <param name="name">The name to look up.</param>
    /// <returns>The value, or <see langword="null"/> when nothing is bound to it.</returns>
    public static object LookupProcedure(Symbol name)
    {
        Interpreter interpreter = Current;
        if (interpreter == null || name == null)
        {
            return null;
        }

        Variable variable = interpreter.CurrentModule?.Lookup(name);
        return variable != null && variable.IsBound ? variable.GetValue() : null;
    }

    /// <summary>
    /// Looks a name up in a NAMED module — upstream's <c>scm_c_public_ref</c>, and what
    /// <c>lily-imports.cc</c>'s <c>Module_variable</c> table exists to provide.
    /// <para>
    /// Page breaking needs this because <c>make-page</c> and <c>calc-printable-height</c>
    /// live in <c>(lily page)</c> and nothing imports that module into the engine's current
    /// one. Resolving the name is also what AUTOLOADS <c>lily/page.scm</c>, so this is the
    /// only thing that has to happen for the file to be there at all.
    /// </para>
    /// <para>
    /// Looked up fresh on every call for the same reason <see cref="LookupProcedure"/> is:
    /// the interpreter is process-global and is replaced between tests, so a cached
    /// procedure would go on running against a dead one without failing.
    /// </para>
    /// </summary>
    /// <param name="moduleName">The module name components, for example <c>lily</c>, <c>page</c>.</param>
    /// <param name="name">The name to look up in it.</param>
    /// <returns>The value, or <see langword="null"/> when the module has no such binding.</returns>
    public static object PublicRef(string[] moduleName, string name)
    {
        Interpreter interpreter = Current;
        if (interpreter == null || moduleName == null || moduleName.Length == 0 || name == null)
        {
            return null;
        }

        object spec = Nil.Instance;
        for (int i = moduleName.Length; i-- > 0;)
        {
            spec = new Pair(Symbol.Intern(moduleName[i]), spec);
        }

        SchemeModule module = interpreter.Modules.Resolve(spec);
        Variable variable = module?.Lookup(Symbol.Intern(name));
        return variable != null && variable.IsBound ? variable.GetValue() : null;
    }

    /// <summary>
    /// Gets LilyPond's own load order, from <c>scm/lily.scm</c>, filtered to the files
    /// that actually exist.
    /// <para>
    /// Upstream's list names <c>font-encodings</c>, which was deleted from <c>scm/</c>
    /// without the list being updated; entries with no matching file are skipped rather
    /// than reported as failures, because they are not ours to fix.
    /// </para>
    /// </summary>
    /// <returns>The ordered file names, without the <c>.scm</c> suffix.</returns>
    public static IReadOnlyList<string> LoadOrder() => LoadOrderCache;

    /// <summary>
    /// Gets every vendored <c>scm/</c> file name, load-order files first and the rest in
    /// alphabetical order. The remainder are the documentation generators, the
    /// output backends and other modules LilyPond loads on demand rather than at startup.
    /// </summary>
    /// <returns>The file names, without the <c>.scm</c> suffix.</returns>
    public static IReadOnlyList<string> AllFiles()
    {
        HashSet<string> ordered = new HashSet<string>(LoadOrderCache, StringComparer.Ordinal);
        List<string> all = new List<string>(LoadOrderCache);
        all.AddRange(VendoredNames().Where(name => !ordered.Contains(name)).OrderBy(name => name, StringComparer.Ordinal));
        return all;
    }

    /// <summary>Gets the names of every vendored <c>scm/</c> file, unordered.</summary>
    /// <returns>The file names, without the <c>.scm</c> suffix.</returns>
    public static IEnumerable<string> VendoredNames()
    {
        Assembly assembly = typeof(LilyPondScheme).Assembly;
        foreach (string resource in assembly.GetManifestResourceNames())
        {
            int marker = resource.IndexOf(LilyFolderMarker, StringComparison.Ordinal);
            if (marker < 0 || !resource.EndsWith(".scm", StringComparison.Ordinal))
            {
                continue;
            }

            string name = resource.Substring(marker + LilyFolderMarker.Length);
            yield return name.Substring(0, name.Length - 4);
        }
    }

    /// <summary>Reads one vendored <c>scm/</c> file.</summary>
    /// <param name="name">The file name, with or without the <c>.scm</c> suffix.</param>
    /// <returns>The Scheme source text, or <see langword="null"/> when there is no such file.</returns>
    public static string ReadSource(string name)
    {
        if (name == null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        string fileName = name.EndsWith(".scm", StringComparison.Ordinal) ? name : name + ".scm";
        return ReadResource(LilyFolderMarker + fileName);
    }

    /// <summary>
    /// Makes <c>(lily <em>name</em>)</c> autoload from the vendored <c>scm/</c> mirror
    /// the first time it is named.
    /// <para>
    /// LilyScheme's own autoloader (Milestone 3, finding 3) covers the Guile layer it
    /// vendors — <c>(srfi srfi-1)</c>, <c>(ice-9 match)</c> and the rest. LilyPond's
    /// SUBMODULES are not its to know about: <c>(lily ly-syntax-constructors)</c>,
    /// <c>(lily curried-definitions)</c>, <c>(lily clip-region)</c> and
    /// <c>(lily display-lily)</c> live in LilyPort's mirror, and upstream reaches them
    /// the same lazy way — <c>lily/lily-imports.cc</c> declares
    /// <c>Scm_module module ("lily ly-syntax-constructors")</c> and lets Guile find
    /// the file.
    /// </para>
    /// <para>
    /// <c>(lily)</c> ITSELF is excluded. It is not autoloadable: <c>lily.scm</c> DRIVES
    /// the startup load rather than being one of the files loaded (Milestone 3,
    /// finding 1), so autoloading it would re-enter the loader from inside itself.
    /// </para>
    /// </summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void EnableLilyModuleAutoload(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        Func<object, SchemeModule, bool> previous = interpreter.Modules.ModuleLoader;
        interpreter.Modules.ModuleLoader = (name, module) =>
            AutoloadLilySubmodule(interpreter, name, module)
            || (previous != null && previous(name, module));
    }

    private static bool AutoloadLilySubmodule(Interpreter interpreter, object name, SchemeModule module)
    {
        // Only (lily <one-more-component>), and only when the mirror has the file.
        if (!(name is Pair head)
            || !(head.Car is Symbol first)
            || !string.Equals(first.Name, "lily", StringComparison.Ordinal)
            || !(head.Cdr is Pair rest)
            || !(rest.Car is Symbol second)
            || !(rest.Cdr is Nil))
        {
            return false;
        }

        string source = ReadSource(second.Name);
        if (source == null)
        {
            return false;
        }

        // LoadExpanded, not EvalString: the file OPENS with `(define-module (lily ...)
        // #:use-module (lily) ...)`, and only the expander-driven load path treats that
        // as the module declaration it is. Evaluating the forms directly reads
        // `(lily)` as a procedure call and dies on an unbound variable — which is
        // exactly what the first attempt did.
        //
        // SAVE AND RESTORE THE CURRENT MODULE — Guile's autoloader wraps the load in
        // save-module-excursion, and the reason is this exact file. `define-module`
        // makes its module current and never puts the old one back, so an autoload
        // triggered from a `use-modules` line REDIRECTS EVERY LATER DEFINITION in the
        // file that triggered it. lily.scm's header is
        //
        //     (use-modules ... (lily clip-region) (lily curried-definitions) ...)
        //
        // so without this, everything lily.scm defines after its own header — and every
        // one of the 55 startup files it then loads — landed in
        // `(lily curried-definitions)` instead of `(lily)`: 668 bindings in the wrong
        // module. It went unnoticed for as long as it did because `(lily)` USES that
        // module, so ordinary lookups still found everything.
        //
        // What it broke is SHADOWING, where the difference is the whole point.
        // scm/operators.scm specialises `+`, `-`, `*` and `<` on <Moment>, <Pitch> and
        // <Duration> with GOOPS methods, which have to REPLACE the arithmetic bound in
        // the root module. Defined one module further out, they were found only after
        // `(guile)`'s own `-` had already answered, so `(- pitch pitch)` — how
        // chord-name.scm normalises every chord — raised wrong-type-arg instead.
        SchemeModule saved = interpreter.CurrentModule;
        try
        {
            SchemeBootstrap.LoadExpanded(interpreter, source, second.Name + ".scm");
        }
        finally
        {
            interpreter.CurrentModule = saved;
        }

        return true;
    }

    /// <summary>
    /// Reads one vendored <c>ly/</c> initialisation file.
    /// <para>
    /// These are LilyPond source, not Scheme: <c>declarations-init.ly</c>,
    /// <c>engraver-init.ly</c>, <c>music-functions-init.ly</c> and the rest of the
    /// 62-file layer that <c>ly/init.ly</c> pulls in. They are vendored verbatim
    /// beside the <c>scm/</c> layer and read by the PARSER, which is why they arrived
    /// only once Track P was finished.
    /// </para>
    /// </summary>
    /// <param name="name">The file name, with or without the <c>.ly</c> suffix.</param>
    /// <returns>The source text, or <see langword="null"/> when there is no such file.</returns>
    public static string ReadInitFile(string name)
    {
        if (name == null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        string fileName = name.EndsWith(".ly", StringComparison.Ordinal) ? name : name + ".ly";
        return ReadResource(LyFolderMarker + fileName);
    }

    /// <summary>Lists the vendored <c>ly/</c> initialisation files.</summary>
    /// <returns>Their names, with the <c>.ly</c> suffix.</returns>
    public static IEnumerable<string> InitFileNames()
    {
        Assembly assembly = typeof(LilyPondScheme).Assembly;
        foreach (string resource in assembly.GetManifestResourceNames())
        {
            int marker = resource.IndexOf(LyFolderMarker, StringComparison.Ordinal);
            if (marker >= 0 && resource.EndsWith(".ly", StringComparison.Ordinal))
            {
                yield return resource.Substring(marker + LyFolderMarker.Length);
            }
        }
    }

    /// <summary>
    /// Creates an interpreter with LilyScheme bootstrapped, the LilyPond Scheme-side
    /// support layer installed, and every unported C++ entry point stubbed.
    /// </summary>
    /// <returns>An interpreter ready to load LilyPond's <c>scm/</c> layer.</returns>
    public static Interpreter CreateInterpreter()
    {
        Interpreter interpreter = new Interpreter();

        // Attached before LoadCore so the prelude's expansion caches too. Replaying
        // still EVALUATES everything live — only read-and-macroexpand is substituted —
        // so a cached boot and a live boot build identical interpreter state.
        interpreter.ExpansionCache = BootExpansionCache.Acquire();

        SchemeBootstrap.LoadCore(interpreter);

        // Order matters. The stubs go in first so every entry point is REACHABLE and
        // recorded; the ported primitives then replace the ones that exist, and the
        // difference between the two sets is the porting worklist.
        EnginePrimitives.InstallStubs(interpreter);
        EngineClasses.Install();
        EmbeddedLilyReader.Install();
        Options = GeneralPrimitives.Install(interpreter);
        MusicPrimitives.Install(interpreter);
        TypePredicates.Install(interpreter);
        ProbPrimitives.Install(interpreter);
        IteratorPrimitives.Install(interpreter);
        Registries = RegistryPrimitives.Install(interpreter);

        // The C#-side translators go in BEFORE the Scheme layer loads, exactly as
        // upstream's ADD_TRANSLATOR static initialisers run before Guile starts: a
        // context definition's \consists list must resolve the same way whether the
        // translator was written in C# or in Scheme.
        Translation.TranslatorRegistry.RegisterBuiltIn(Registries);
        GrobPrimitives.Install(interpreter);
        TranslationPrimitives.Install(interpreter);
        OutputPrimitives.Install(interpreter);
        TransformPrimitives.Install(interpreter);
        FontPrimitives.Install(interpreter);
        GrobCallbacks.Install(interpreter);

        // The Wave A group installers (2026-08-07). Epg8Callbacks carries a few
        // demand-pulled bindings for files outside its group; the overlapping
        // EPG7 stand-ins it shipped were removed at integration, so the order of
        // these five is not load-bearing.
        Epg5Callbacks.Install(interpreter);
        Epg6Callbacks.Install(interpreter);
        Epg7Callbacks.Install(interpreter);
        Epg8Callbacks.Install(interpreter);
        Epg9Callbacks.Install(interpreter);
        Epg17Callbacks.Install(interpreter);
        Epg18Callbacks.Install(interpreter);

        // EPG10 (2026-08-07): the beam callbacks. Order-independent of the above —
        // every ly:beam::* name is its own, and Beam reads Stem through C#, not Scheme.
        Epg10Callbacks.Install(interpreter);

        // EPG11/EPG12 (2026-08-08): the tie and slur callbacks. Order-independent of each
        // other and of everything above — no name is shared — but both must be installed,
        // because the slur's outside-slur trio is looked up BY NAME from C# when a dodging
        // grob is chained onto the slur, and an unregistered name would chain a stub.
        Epg11Callbacks.Install(interpreter);
        Epg12Callbacks.Install(interpreter);

        // EPG14 (2026-08-08): scripts, dynamics, brackets, pedals, fingering, ledger
        // lines and the line spanner. Order-independent of everything above — every name
        // is its own — and it must be installed for the same reason EPG12's trio must:
        // ly:script-column::row-before-line-breaking compares a grob's Y-offset AGAINST
        // ly:side-position-interface::y-aligned-side by identity, so both have to be the
        // registered procedure and not a stub.
        Epg14Callbacks.Install(interpreter);

        // EPG20 (2026-08-08): the arpeggio/chord-bracket/chord-slur callbacks and the
        // chord-name binding. Order-independent of everything above — every name is its
        // own — but it must be installed, because ly/property-init.ly's \arpeggioBracket
        // and \arpeggioParenthesis OVERRIDE Arpeggio.stencil with ly:chord-bracket::print
        // and ly:chord-slur::print BY NAME, so an unregistered name would install a stub
        // on a grob that is otherwise fully working.
        Epg20Callbacks.Install(interpreter);
        Epg15Callbacks.Install(interpreter);

        // EPG16 (2026-08-08): the six page-breaking strategies, Paper_book's accessors and
        // ly:book-process. These go in AFTER Epg15Callbacks because a page breaker calls
        // straight into the line breaker EPG15 landed.
        Epg16Callbacks.Install(interpreter);

        // EPG21 (2026-08-09): the four ancient-notation ligature grobs. Order-independent
        // of everything above -- every name is its own -- but it must be installed BEFORE
        // any score runs, because the mensural and vaticana engravers look their
        // brew-ligature-primitive callbacks up BY NAME at construction time and install
        // them as the stencil of every head they collect.
        Epg21Callbacks.Install(interpreter);

        // EPG22 (2026-08-07): dispatcher-scheme.cc, pulled forward from EPG23 because
        // \addQuote cannot run without it. It must go in AFTER Epg8Callbacks, which is
        // where ly:broadcast used to live.
        DispatcherPrimitives.Install(interpreter);
        OriginPrimitives.Install(interpreter);
        ParserPrimitives.Install(interpreter);
        EngineSupport.Install(interpreter);

        // EPG23 (2026-08-12): the leaf binding files the ledger still owed —
        // simple-spacer-scheme.cc and spring-smob.cc here, lily-random.cc next. Both
        // groups' TYPES landed long ago; only the LY_DEFINE surface was missing.
        SpacingPrimitives.Install(interpreter);
        RandomPrimitives.Install(interpreter);
        Epg23Callbacks.Install(interpreter);

        // D25's N/A half, LAST among EPG23's installers so that anything above may still
        // claim a name for a real implementation instead.
        NotApplicableEntryPoints.Install(interpreter);

        // LAST, and it must stay last: it looks both halves of each getter/setter pair
        // up by name, so every primitive involved has to exist first.
        SetterBindings.Install(interpreter);

        EnableLilyModuleAutoload(interpreter);

        // Published only now. A half-built interpreter must never become the ambient
        // one: anything that consults it for a property type check in that window
        // sees empty tables and silently refuses every assignment.
        Current = interpreter;
        return interpreter;
    }

    /// <summary>
    /// Loads LilyPond's Scheme layer the way LilyPond itself does: by loading
    /// <c>lily.scm</c> and letting its own <c>(for-each ly:load init-scheme-files)</c>
    /// pull in the rest.
    /// <para>
    /// This ordering is not a stylistic preference. <c>lily.scm</c> builds its
    /// type-predicate tables AFTER the load list, from predicates that <c>c++.scm</c> and
    /// <c>lily-library.scm</c> define -- so loading the list without <c>lily.scm</c>
    /// around it leaves the session machinery, the SRFI imports and <c>G_</c> undefined,
    /// while loading <c>lily.scm</c> alone leaves its tables referring to nothing.
    /// </para>
    /// </summary>
    /// <param name="interpreter">A bootstrapped interpreter.</param>
    /// <returns>A report of which files loaded and which failed.</returns>
    public static LoadReport LoadViaLilyScm(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        LoadReport report = new LoadReport();

        // The hook is NOT ly:load. lily.scm defines its own ly:load in Scheme, which
        // resolves the name with %search-load-path and then hands off to
        // primitive-load-path -- so overriding ly:load has no effect at all, because
        // lily.scm replaces it before the load list runs. These two are the real seam,
        // and the vendored resources are what stands in for Guile's load path.
        // Resolution always succeeds for a name under lily/, and primitive-load-path
        // below decides whether there is anything to load. That split is deliberate:
        // upstream's own load list still names font-encodings, which was deleted from
        // scm/ without the list being updated, and lily.scm turns an unresolved name into
        // a fatal ly:error. Answering honestly there would abort the whole load over a
        // defect that is upstream's, not ours.
        interpreter.DefinePrimitive("%search-load-path", 1, 1, arguments =>
            new MutableString(StringPrimitives.Text(arguments[0], "%search-load-path") + ".scm"));

        // A failing file must NOT abort the run: LilyPond's own loader would stop, but
        // the point of this pass is to see how far every file gets, not just the first.
        interpreter.DefinePrimitive("primitive-load-path", 1, 2, arguments =>
        {
            string name = NameOf(arguments[0]);
            string source = ReadSource(name);
            if (source == null)
            {
                // Upstream's list still names font-encodings, deleted from scm/ without
                // the list being updated. Not ours to fix, and not a failure.
                return Unspecified.Instance;
            }

            if (report.Loaded.Contains(name) || report.Failed.ContainsKey(name))
            {
                return Unspecified.Instance;
            }

            try
            {
                SchemeBootstrap.LoadExpanded(interpreter, source, name);
                report.Loaded.Add(name);
            }
            catch (Exception ex)
            {
                report.Failed[name] = Describe(ex);
            }

            return Unspecified.Instance;
        });

        try
        {
            SchemeBootstrap.LoadExpanded(interpreter, ReadSource("lily"), "lily.scm");
            report.Loaded.Add("lily");
        }
        catch (Exception ex)
        {
            report.Failed["lily"] = Describe(ex);
        }

        // The port HAS one backend, and it is SVG (decision D15: no PostScript, no
        // Cairo). Upstream's option defaults to `ps` because upstream ships all three,
        // and scm/lily.scm's own
        //
        //     (if (memq (ly:get-option 'backend) music-string-to-path-backends)
        //         (ly:set-option 'music-strings-to-paths #t))
        //
        // has already run by now with that default, so both have to be set here.
        //
        // This is not cosmetic. ly/paper-defaults-init.ly branches on the same option
        // to choose the text font family names, and under `ps` it picks the FontConfig
        // aliases -- "LilyPond Serif", "LilyPond Sans Serif", "LilyPond Monospace" --
        // where the SVG backend wants the generic CSS families "serif", "sans" and
        // "monospace". Those names are written verbatim into every text element, so
        // getting the option wrong makes every piece of text in the suite differ from
        // the reference while looking entirely reasonable on its own.
        Options.Set("backend", Symbol.Intern("svg"));
        Options.Set("music-strings-to-paths", true);

        // Suite mode arms HERE and not a line earlier. Loading is the one phase that
        // legitimately calls unported primitives -- LilyPond's Scheme builds its tables
        // by calling into C++, and a throwing stub would abort the file and hide every
        // later call in it. Once the layer is loaded, any further placeholder is
        // something the port RELIED on, which is the whole point of the mode.
        if (EnginePrimitives.SuiteModeRequested)
        {
            EnginePrimitives.ThrowOnUnported = true;
        }

        // First live boot in a fresh world: persist the recording so every later boot
        // — this process's next interpreter, the next test process, the next sweep —
        // replays it. A boot that replayed has nothing new and skips this.
        BootExpansionCache.SaveIfDirty(interpreter);

        return report;
    }

    /// <summary>
    /// Loads the <c>scm/</c> files LilyPond does NOT load at startup, retrying until no
    /// further progress is made.
    /// <para>
    /// These are the documentation generators, the output backends and the other modules
    /// LilyPond pulls in on demand. Unlike the startup list, nothing records the order
    /// they depend on each other in -- so rather than guess, this repeats the pass while
    /// it keeps succeeding. A file that fails only because a library it needs has not
    /// loaded yet succeeds on the next round; a file that fails for its own reasons fails
    /// every round, and the last failure is what gets reported.
    /// </para>
    /// </summary>
    /// <param name="interpreter">An interpreter with the startup layer already loaded.</param>
    /// <param name="names">The files to attempt.</param>
    /// <returns>A report of which files loaded and which failed.</returns>
    public static LoadReport LoadToFixpoint(Interpreter interpreter, IEnumerable<string> names)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        List<string> pending = new List<string>(names ?? Array.Empty<string>());
        LoadReport report = new LoadReport();

        while (pending.Count > 0)
        {
            LoadReport round = Load(interpreter, pending);
            report.Loaded.AddRange(round.Loaded);

            if (round.Loaded.Count == 0)
            {
                // No progress this round, so nothing left will ever succeed.
                foreach (KeyValuePair<string, string> failure in round.Failed)
                {
                    report.Failed[failure.Key] = failure.Value;
                }

                break;
            }

            pending = new List<string>(round.Failed.Keys);
        }

        return report;
    }

    /// <summary>Loads LilyPond's Scheme layer into an interpreter, in LilyPond's order.</summary>
    /// <param name="interpreter">A bootstrapped interpreter.</param>
    /// <param name="names">The file names to load, or <see langword="null"/> for the load order.</param>
    /// <returns>A report of which files loaded and which failed.</returns>
    public static LoadReport Load(Interpreter interpreter, IEnumerable<string> names)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        IEnumerable<string> targets = names ?? LoadOrder();
        List<KeyValuePair<string, string>> sources = new List<KeyValuePair<string, string>>();
        foreach (string name in targets)
        {
            string source = ReadSource(name);
            if (source != null)
            {
                sources.Add(new KeyValuePair<string, string>(name, source));
            }
        }

        return EnginePrimitives.LoadLilyPondScheme(interpreter, sources);
    }

    /// <summary>Reads a file from the LilyPort-authored <c>Scheme/lilyport</c> folder.</summary>
    /// <param name="fileName">The file name, for example <c>support.scm</c>.</param>
    /// <returns>The Scheme source text, or <see langword="null"/> when there is no such file.</returns>
    public static string ReadSupportResource(string fileName)
    {
        if (fileName == null)
        {
            throw new ArgumentNullException(nameof(fileName));
        }

        return ReadResource(".Scheme.lilyport." + fileName);
    }

    private static string NameOf(object value)
    {
        // ly:load is called with a Scheme string; MutableString.ToString() is the
        // external representation, complete with quotes, so go through the text accessor.
        string text = value == null
            ? string.Empty
            : StringPrimitives.Text(value, "ly:load");
        int slash = text.LastIndexOf('/');
        if (slash >= 0)
        {
            text = text.Substring(slash + 1);
        }

        return text.EndsWith(".scm", StringComparison.Ordinal)
            ? text.Substring(0, text.Length - 4)
            : text;
    }

    private static string Describe(Exception exception)
    {
        System.Text.StringBuilder builder = new System.Text.StringBuilder();
        while (exception != null)
        {
            if (builder.Length > 0)
            {
                builder.Append(" <- ");
            }

            builder.Append(exception.Message);
            exception = exception.InnerException;
        }

        return builder.ToString();
    }

    private static Exception Deepest(Exception exception)
    {
        while (exception.InnerException != null)
        {
            exception = exception.InnerException;
        }

        return exception;
    }

    private static string[] ReadLoadOrder()
    {
        string text = ReadResource("." + LoadOrderResource);
        if (text == null)
        {
            throw new InvalidOperationException("Embedded resource 'load-order.txt' is missing from the assembly.");
        }

        HashSet<string> present = new HashSet<string>(VendoredNames(), StringComparer.Ordinal);
        List<string> order = new List<string>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#' || !present.Contains(line))
            {
                continue;
            }

            order.Add(line);
        }

        return order.ToArray();
    }

    private static string ReadResource(string suffix)
    {
        Assembly assembly = typeof(LilyPondScheme).Assembly;
        string match = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(suffix, StringComparison.Ordinal));
        if (match == null)
        {
            return null;
        }

        using (Stream stream = assembly.GetManifestResourceStream(match))
        using (StreamReader reader = new StreamReader(stream))
        {
            return reader.ReadToEnd();
        }
    }
}
