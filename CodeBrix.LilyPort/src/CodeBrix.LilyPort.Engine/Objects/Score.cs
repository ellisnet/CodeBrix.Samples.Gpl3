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

using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Layout;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/score.cc, lily/include/score.hh;

// Modified by Jeremy Ellis on 2026-08-03 as part of the CodeBrix port.

/// <summary>
/// What a <c>\score</c> block becomes: one music expression, its output definitions
/// (<c>\layout</c>, <c>\midi</c>), and an optional header.
/// <para>
/// PORTED MINIMALLY, on the demand of the parser's score rule actions plus honest
/// storage of the type's own state. The rendering half — <c>book_rendering</c> and
/// <c>ly_run_translator</c> — and the copy constructor and <c>clone</c> (which need
/// Guile module copying inside the engine) are NOT ported; see the Engine
/// PORT-COVERAGE entry. The type's smob predicate <c>ly:score?</c>
/// (<c>type_p_name_</c> upstream) is registered in
/// <see cref="Bootstrap.TypePredicates"/>.
/// </para>
/// </summary>
public class Score
{
    private static readonly Symbol ErrorFoundSymbol = Symbol.Intern("error-found");

    private readonly List<OutputDef> _defs = new List<OutputDef>();
    private object _music;
    private object _header;

    /// <summary>
    /// Initializes an empty score: no music, no header, no error — exactly
    /// upstream's default constructor. Upstream also stamps a fresh, empty
    /// <c>Input</c> as the origin; the port has no input location type yet, so
    /// <see cref="Origin"/> starts unset instead.
    /// </summary>
    public Score()
    {
        _header = Nil.Instance;
        _music = Nil.Instance;
        ErrorFound = false;
    }

    /// <summary>
    /// Gets where in the source this score came from, or <see langword="null"/> when
    /// no location has been recorded.
    /// <para>Upstream: <c>origin ()</c> over the <c>input_location_</c> smob.</para>
    /// </summary>
    public object Origin { get; private set; }

    /// <summary>
    /// Gets or sets a value indicating whether errors were found in the score — set
    /// by the parser's error-recovery rule and by <see cref="SetMusic"/> when the
    /// music itself carries <c>error-found</c>.
    /// <para>Upstream: the public <c>error_found_</c> member.</para>
    /// </summary>
    public bool ErrorFound { get; set; }

    /// <summary>
    /// Gets the score's output definitions, in the order they were added.
    /// <para>Upstream: the public <c>defs_</c> vector.</para>
    /// </summary>
    public IReadOnlyList<OutputDef> Defs => _defs;

    /// <summary>
    /// Records where in the source this score came from.
    /// <para>Upstream: <c>origin ()-&gt;set_spot (...)</c>, which the
    /// <c>score_block</c> rule action calls on the finished block. The same shape as
    /// <see cref="MusicObject.SetSpot"/>.</para>
    /// </summary>
    /// <param name="origin">The source location.</param>
    public void SetSpot(object origin) => Origin = origin;

    /// <summary>
    /// Gets the score's music expression, or the empty list when it has none.
    /// <para>Upstream: <c>Score::get_music</c>.</para>
    /// </summary>
    /// <returns>The music.</returns>
    public object GetMusic() => _music;

    /// <summary>
    /// Sets the score's music expression, complaining (without throwing) when music
    /// is already present, and DROPPING the music entirely — with
    /// <see cref="ErrorFound"/> raised — when the music arrives already marked with
    /// <c>error-found</c>. Both diagnostics go through
    /// <see cref="Warn.NonFatalError(string, string)"/>; upstream attaches them to
    /// the music's input location, which the port does not carry yet.
    /// <para>Upstream: <c>Score::set_music</c>, reached from the Scheme layer's
    /// <c>ly:make-score</c> (and so from <c>scorify-music</c>).</para>
    /// </summary>
    /// <param name="music">The music expression.</param>
    public void SetMusic(object music)
    {
        if (_music is MusicObject)
        {
            Warn.NonFatalError("already have music in score");
            Warn.NonFatalError("this is the previous music");
        }

        MusicObject incoming = music as MusicObject;
        if (incoming != null
            && incoming.GetProperty(ErrorFoundSymbol) is bool errorFound
            && errorFound)
        {
            // from_scm<bool> upstream is true only for exactly #t, so an unset
            // property (the empty list) correctly reads as no-error here.
            Warn.NonFatalError("errors found, ignoring music expression");
            ErrorFound = true;
        }

        if (ErrorFound)
        {
            _music = Nil.Instance;
        }
        else
        {
            _music = music;
        }
    }

    /// <summary>
    /// Adds an output definition to the score.
    /// <para>Upstream: <c>Score::add_output_def</c>. The argument may be
    /// <see langword="null"/>, exactly as upstream's pointer may be — the parser
    /// pushes whatever <c>unsmob&lt;Output_def&gt;</c> answered.</para>
    /// </summary>
    /// <param name="def">The output definition.</param>
    public void AddOutputDef(OutputDef def) => _defs.Add(def);

    /// <summary>
    /// Gets the score's header, or the empty list when it has none.
    /// <para>Upstream: <c>Score::get_header</c>.</para>
    /// </summary>
    /// <returns>The header module, or the empty list.</returns>
    public object GetHeader() => _header;

    /// <summary>
    /// Sets the score's header. Upstream asserts the value is a Guile module;
    /// module-ness lives in the Scheme/host layer the engine does not own, so the
    /// port stores what it is given — recorded in PORT-COVERAGE.
    /// <para>Upstream: <c>Score::set_header</c>.</para>
    /// </summary>
    /// <param name="module">The header module.</param>
    public void SetHeader(object module) => _header = module;

    /// <summary>
    /// Renders the score under a book's paper: runs the translators once per output
    /// definition the score carries, and answers the resulting outputs.
    /// <para>
    /// A score with NO output definitions of its own still renders ONCE, under the book's
    /// default — that is what upstream's <c>for (i = 0; !i || i &lt; count; i++)</c> loop
    /// says, and it is the ordinary case: a plain <c>\score { ... }</c> carries no
    /// <c>\layout</c> block and must still produce a page.
    /// </para>
    /// <para>
    /// Only a <c>layout</c> definition is RESCALED. A <c>\midi</c> block has no dimensions
    /// to scale, and the scale itself is 1 unless the book's definition is a real
    /// <c>paper</c> block.
    /// </para>
    /// <para>
    /// Landed with EPG16 (2026-08-08), and its absence is why this file's ledger row read
    /// <c>ported</c> while the rendering half was hollow — the same shape EPG15 found in
    /// five files at once.
    /// </para>
    /// </summary>
    /// <param name="layoutBook">The book's paper definition.</param>
    /// <param name="defaultDef">The layout to use when the score carries none.</param>
    /// <returns>The music outputs, as a Scheme list, in definition order.</returns>
    public object BookRendering(OutputDef layoutBook, OutputDef defaultDef)
    {
        if (ErrorFound)
        {
            return Nil.Instance;
        }

        double scale = 1.0;

        if (layoutBook != null
            && ReferenceEquals(layoutBook.CVariable("output-def-kind"), PaperSymbol))
        {
            scale = Bootstrap.SchemeConvert.ToDouble(layoutBook.CVariable("output-scale"), 1.0);
        }

        List<object> outputs = new List<object>();

        int outdefCount = _defs.Count;

        for (int i = 0; i == 0 || i < outdefCount; i++)
        {
            OutputDef def = outdefCount != 0 ? _defs[i] : defaultDef;
            if (def == null)
            {
                continue;
            }

            OutputDef scaled = def;
            if (ReferenceEquals(def.CVariable("output-def-kind"), LayoutSymbol))
            {
                scaled = Bootstrap.OutputPrimitives.ScaleOutputDef(def, scale);
                scaled.Parent = layoutBook;
            }

            Translation.ContextDef globalDef
                = Translation.ContextDef.FindContextDef(scaled, GlobalSymbol);
            if (globalDef == null)
            {
                continue;
            }

            Translation.GlobalContext global
                = new Translation.GlobalContext(scaled, globalDef);
            global.MakeGlobalTranslator();
            global.Iterate(_music as Music.MusicObject);

            object output = Bootstrap.OutputPrimitives.FormatOutput(global);
            if (output != null && !(output is Nil))
            {
                outputs.Add(output);
            }
        }

        return Pair.ListFrom(outputs);
    }

    private static readonly Symbol PaperSymbol = Symbol.Intern("paper");
    private static readonly Symbol LayoutSymbol = Symbol.Intern("layout");
    private static readonly Symbol GlobalSymbol = Symbol.Intern("Global");
}
