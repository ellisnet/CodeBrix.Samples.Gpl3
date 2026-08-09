/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2002--2026 Juergen Reuter <reuter@ipd.uka.de>

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
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/ligature-engraver.cc, lily/include/ligature-engraver.hh;

// Modified by Jeremy Ellis on 2026-08-09 as part of the CodeBrix port:
//   - upstream's two acknowledgers are separate virtuals reached by the ADD_ACKNOWLEDGER
//     dispatcher; the port has ONE AcknowledgeGrob per engraver, so they are branches of
//     it, selected by the same interfaces the macros name (`rest-interface',
//     `ligature-head-interface'). The Grob_info_t<Item> narrowing upstream gets for free
//     from its template is an `as Item' here.
//   - brew_ligature_primitive_proc is looked up BY NAME rather than taken from a static
//     member of the grob class: upstream's MAKE_SCHEME_CALLBACK gives Mensural_ligature a
//     brew_ligature_primitive_proc member holding the registered procedure, and the port's
//     equivalent of "the registered procedure" is what the interpreter has under that
//     name. It is looked up ONCE, in the derived constructor, and a failure to find it is
//     LOUD -- a silently unbound name would install nothing and every head would keep its
//     ordinary note-head stencil, which still draws.
//   - the acknowledgers and the ligature listener are registered HERE rather than in each
//     concrete subclass's boot (). Upstream splits them because a C++ base class cannot
//     run its subclass's ADD_ACKNOWLEDGER; the set is the same either way, because all
//     three concrete subclasses of this class declare exactly `rest' and `ligature_head'
//     and exactly one delegate listener. Ligature_bracket_engraver is NOT a subclass of
//     this class and keeps its own pair.

/*
 * This abstract class provides the general framework for ligatures of
 * any kind.  It cares for handling start/stop ligatures events and
 * collecting all noteheads inbetween, but delegates creation of a
 * ligature spanner for each start/stop pair and typesetting of the
 * ligature spanner to a concrete subclass.
 *
 * A concrete ligature engraver must subclass this class and provide
 * functions create_ligature_spanner () and typeset_ligature
 * (Spanner *, vector<Grob_info>).  Subclasses of this class basically
 * fall into two categories.
 *
 * The first category consists of engravers that engrave ligatures in
 * a way that really deserves the name ligature.  That is, they
 * produce a single connected graphical object of fixed width,
 * consisting of noteheads and other primitives.  Space may be
 * inserted only after each ligature, if necessary, but in no case
 * between the primitives of the ligature. The same approach is
 * used for Kievan notation ligatures, or, rather melismas.
 * Though these are not single connected objects, they behave much
 * in the same way and have a fixed, small amount of space between
 * noteheads. Except in Kievan "ligatures", accidentals have to be put
 * to the left of the ligature, and not to the left of individual
 * noteheads. In Kievan ligatures, the B-flat may be part of the
 * ligature itself. Class Coherent_ligature_engraver is the common
 * superclass for all of these engravers.
 *
 * The second category is for engravers that are relaxed in the sense
 * that they do not require to produce a single connected graphical
 * object.  For example, in contemporary editions, ligatures are often
 * marked, but otherwise use contemporary notation and spacing.  In
 * this category, there is currently only a single class,
 * Ligature_bracket_engraver, which marks each ligature with a
 * horizontal sqare bracket, but otherwise leaves the appearance
 * untouched.
 */

/// <summary>
/// The general framework for ligatures of any kind: it handles the start/stop ligature
/// events and collects the note heads between them, and leaves both the making of the
/// spanner and the typesetting of it to a concrete subclass.
/// </summary>
/// <remarks>
/// <para>
/// TODO (upstream): lyrics/melisma/syllables — there should be at most one syllable of
/// lyrics per ligature (i.e. for the lyrics context, a ligature should count as a single
/// note, regardless of how many heads the ligature consists of).
/// </para>
/// <para>
/// TODO (upstream): currently, you have to add/remove the proper
/// <c>Ligature_engraver</c> to the proper translator to choose between representations.
/// Since adding/removing an engraver to a translator is a global action in the layout
/// block, you cannot mix representations WITHIN the same score.
/// </para>
/// </remarks>
public abstract class LigatureEngraver : Engraver
{
    private static readonly Symbol CurrentMusicalColumnSymbol
        = Symbol.Intern("currentMusicalColumn");

    private static readonly Symbol ForbidBreakSymbol = Symbol.Intern("forbidBreak");
    private static readonly Symbol LigatureEventSymbol = Symbol.Intern("ligature-event");
    private static readonly Symbol LigatureHeadInterfaceSymbol
        = Symbol.Intern("ligature-head-interface");

    private static readonly Symbol RestInterfaceSymbol = Symbol.Intern("rest-interface");
    private static readonly Symbol ScoreSymbol = Symbol.Intern("Score");
    private static readonly Symbol StencilSymbol = Symbol.Intern("stencil");

    private readonly List<Item> _primitives = new List<Item>();
    private readonly List<Item> _finishedPrimitives = new List<Item>();

    private Spanner _ligature;
    private Spanner _finishedLigature;
    private Grob _lastBound;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    protected LigatureEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Ligature_engraver";

    /// <summary>
    /// Gets or sets the procedure that draws ONE head of the ligature, installed on each
    /// head as its <c>stencil</c>. <c>'()</c> means the style draws its heads normally,
    /// which is what the bracket engraver wants.
    /// </summary>
    protected object BrewLigaturePrimitiveProc { get; set; } = Nil.Instance;

    /// <summary>The pair of ligature events of this timestep.</summary>
    protected UniqueSpanEventListener LigatureListener { get; } = new UniqueSpanEventListener();

    /// <summary>
    /// Gets the moment the open ligature started at.
    /// <para>
    /// ⚠ DEAD UPSTREAM, and kept dead here: <c>ligature_start_mom_</c> is written by
    /// <c>process_music</c> and read by NOTHING in the whole tree. It is a property here
    /// rather than a field only so that "assigned but never read" is not a build warning.
    /// </para>
    /// </summary>
    protected Moment LigatureStartMoment { get; private set; }

    /// <summary>Starts listening for ligature events.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo(LigatureEventSymbol, LigatureListener.Listen);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Forbids a line break inside an open ligature.</summary>
    public override void PreProcessMusic()
    {
        if (_ligature != null && LigatureListener.Stop == null)
        {
            FindScoreContext()?.SetProperty(ForbidBreakSymbol, true);
        }
    }

    /// <summary>Closes the ligature a stop event ends, and opens the one a start begins.</summary>
    public override void ProcessMusic()
    {
        StreamEvent ender = LigatureListener.Stop;
        if (ender != null)
        {
            if (_ligature == null)
            {
                Epg8Support.EventWarning(ender, "cannot find start of ligature");
                return;
            }

            if (_lastBound == null)
            {
                Epg8Support.EventWarning(ender, "no right bound");
            }
            else
            {
                _ligature.SetBound(Direction.Positive, _lastBound);
            }

            _finishedPrimitives.Clear();
            _finishedPrimitives.AddRange(_primitives);
            _finishedLigature = _ligature;
            _primitives.Clear();
            _ligature = null;
        }

        _lastBound = GetProperty(CurrentMusicalColumnSymbol) as Grob;

        StreamEvent starter = LigatureListener.Start;
        if (starter != null)
        {
            if (_ligature != null)
            {
                Epg8Support.EventWarning(starter, "already have a ligature");
                _ligature.Warning("ligature was started here");
                return;
            }

            _ligature = CreateLigatureSpanner();

            Grob bound = GetProperty(CurrentMusicalColumnSymbol) as Grob;
            if (bound == null)
            {
                Epg8Support.EventWarning(starter, "no left bound");
            }
            else
            {
                _ligature.SetBound(Direction.Negative, bound);
            }

            LigatureStartMoment = NowMoment;

            // TODO (upstream): dump cause into make_item/spanner.
        }
    }

    /// <summary>Typesets the ligature that ended in this timestep.</summary>
    public override void StopTranslationTimestep()
    {
        if (_finishedLigature != null)
        {
            if (_finishedPrimitives.Count == 0)
            {
                _finishedLigature.ProgrammingError(
                    "Ligature_engraver::stop_translation_timestep ():"
                    + " junking empty ligature");
            }
            else
            {
                TypesetLigature(_finishedLigature, _finishedPrimitives);
                _finishedPrimitives.Clear();
            }

            _finishedLigature = null;
        }

        LigatureListener.Reset();
    }

    /// <summary>Typesets a ligature left open at the end, and kills an unterminated one.</summary>
    public override void FinalizeTranslation()
    {
        if (_finishedLigature != null)
        {
            TypesetLigature(_finishedLigature, _finishedPrimitives);
            _finishedPrimitives.Clear();
            _finishedLigature = null;
        }

        if (_ligature != null)
        {
            _ligature.Warning("unterminated ligature");
            _ligature.Suicide();
        }
    }

    /// <summary>Collects the ligature's heads, and refuses its rests.</summary>
    /// <param name="info">The announced grob.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        Grob grob = info.Grob;
        if (grob.HasInterface(RestInterfaceSymbol))
        {
            AcknowledgeRest(info);
        }

        // The `is Item' is upstream's Grob_info_t<Item> narrowing, which its dispatcher
        // applies BEFORE the hook runs: a ligature head that is not an item never reaches
        // acknowledge_ligature_head at all, so it is never collected either.
        if (grob is Item && grob.HasInterface(LigatureHeadInterfaceSymbol))
        {
            AcknowledgeLigatureHead(info);
        }
    }

    /// <summary>
    /// Gets the ligature currently open, or <see langword="null"/>.
    /// <para>
    /// ⚠ DEAD UPSTREAM, and kept dead here: <c>current_ligature ()</c> is declared
    /// virtual, defined, and called by NOTHING in the whole tree. It is ported because it
    /// is part of the class's declared shape, not because anything reaches it.
    /// </para>
    /// </summary>
    /// <returns>The ligature spanner.</returns>
    protected virtual Spanner CurrentLigature() => _ligature;

    /// <summary>Makes the spanner one ligature is drawn as. A concrete style provides it.</summary>
    /// <returns>The ligature spanner.</returns>
    protected abstract Spanner CreateLigatureSpanner();

    /// <summary>Draws one finished ligature. A concrete style provides it.</summary>
    /// <param name="ligature">The ligature spanner.</param>
    /// <param name="primitives">The heads it was collected from, in time order.</param>
    protected abstract void TypesetLigature(Spanner ligature, IReadOnlyList<Item> primitives);

    /// <summary>Collects one head into the open ligature, and gives it the style's stencil.</summary>
    /// <param name="info">The announced head.</param>
    protected virtual void AcknowledgeLigatureHead(GrobInfo info)
    {
        if (_ligature != null && info.Grob is Item item)
        {
            _primitives.Add(item);
            if (!(BrewLigaturePrimitiveProc is Nil))
            {
                item.SetProperty(StencilSymbol, BrewLigaturePrimitiveProc);
            }
        }
    }

    /// <summary>Warns that a rest cannot be part of a ligature.</summary>
    /// <param name="info">The announced rest.</param>
    protected virtual void AcknowledgeRest(GrobInfo info)
    {
        if (_ligature != null)
        {
            Epg8Support.EventWarning(
                info.EventCause, "ignoring rest: ligature may not contain rest");
            _ligature.Warning("ligature was started here");

            // TODO (upstream): maybe better should stop ligature here rather than
            // ignoring the rest?
        }
    }

    /// <summary>
    /// Looks the style's primitive-drawing procedure up by name, loudly.
    /// </summary>
    /// <param name="name">The registered procedure name.</param>
    /// <returns>The procedure, or <c>'()</c> when the name is unbound.</returns>
    protected static object LookupBrewProc(string name)
    {
        object procedure = LilyPondScheme.LookupProcedure(Symbol.Intern(name));
        if (procedure == null)
        {
            Warn.ProgrammingError(
                "ligature engraver: `" + name + "' is not registered;"
                + " ligature heads will keep their ordinary stencils");
            return Nil.Instance;
        }

        return procedure;
    }

    private Context FindScoreContext()
    {
        Context score = Context?.FindContextAbove(ScoreSymbol);
        if (score == null)
        {
            Warn.ProgrammingError("no score context");
        }

        return score;
    }
}
