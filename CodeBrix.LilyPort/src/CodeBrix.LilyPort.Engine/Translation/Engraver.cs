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
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/engraver.cc, lily/include/engraver.hh, lily/include/grob-info.hh, lily/engraver-group.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/*
  Data container for broadcasts.
*/

/// <summary>
/// A grob together with the engraver that made it: what gets broadcast when a grob is
/// announced.
/// </summary>
public readonly struct GrobInfo
{
    /// <summary>Initializes the record. Both the engraver and the grob are required.</summary>
    /// <param name="originEngraver">The engraver that made the grob.</param>
    /// <param name="grob">The grob.</param>
    public GrobInfo(Engraver originEngraver, Grob grob)
    {
        OriginEngraver = originEngraver ?? throw new ArgumentNullException(nameof(originEngraver));
        Grob = grob ?? throw new ArgumentNullException(nameof(grob));
    }

    /// <summary>Gets the engraver that made the grob.</summary>
    public Engraver OriginEngraver { get; }

    /// <summary>Gets the grob.</summary>
    public Grob Grob { get; }

    /// <summary>Gets the event that caused the grob, if any.</summary>
    public StreamEvent EventCause => Grob.EventCause();

    /// <summary>Gets the event that ultimately caused the grob, if any.</summary>
    public StreamEvent UltimateEventCause => Grob.UltimateEventCause();
}

/**
   a struct which processes events, and creates the Grobs.
   It may use derived classes.
*/

/// <summary>
/// A translator that makes grobs.
/// <para>
/// The engraving protocol has two halves. An engraver MAKES grobs during
/// <see cref="Translator.ProcessMusic"/> and ANNOUNCES them; every other engraver in
/// the context and its ancestors then gets to
/// <see cref="AcknowledgeGrob"/> what was made. That is how a Stem_engraver finds the
/// note heads it must attach to without either engraver knowing about the other.
/// </para>
/// </summary>
public abstract class Engraver : Translator
{
    private static readonly Symbol MetaSymbol = Symbol.Intern("meta");
    private static readonly Symbol ClassesSymbol = Symbol.Intern("classes");
    private static readonly Symbol CauseSymbol = Symbol.Intern("cause");
    private static readonly Symbol ItemSymbol = Symbol.Intern("Item");
    private static readonly Symbol SpannerSymbol = Symbol.Intern("Spanner");
    private static readonly Symbol PaperColumnSymbol = Symbol.Intern("Paper_column");
    private static readonly Symbol AllGrobDescriptionsSymbol = Symbol.Intern("all-grob-descriptions");
    private static readonly Symbol StickyHostSymbol = Symbol.Intern("sticky-host");
    private static readonly Symbol StickyGrobInterfaceSymbol = Symbol.Intern("sticky-grob-interface");

    /// <summary>Initializes an engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    protected Engraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Engraver";

    /// <summary>Gets a value indicating whether this translator contributes to MIDI.</summary>
    public override bool IsMidi => false;

    /// <summary>Gets the engraver group this engraver belongs to.</summary>
    public EngraverGroup EngraverGroup => Context?.Implementation as EngraverGroup;

    /// <summary>Makes an item and announces it.</summary>
    /// <param name="grobName">The grob type name, such as <c>NoteHead</c>.</param>
    /// <param name="cause">What caused it, usually a stream event.</param>
    /// <returns>The item.</returns>
    public Item MakeItem(string grobName, object cause)
        => (Item)MakeGrob(Symbol.Intern(grobName), cause, ItemSymbol);

    /// <summary>Makes a spanner and announces it.</summary>
    /// <param name="grobName">The grob type name, such as <c>StaffSymbol</c>.</param>
    /// <param name="cause">What caused it, usually a stream event.</param>
    /// <returns>The spanner.</returns>
    public Spanner MakeSpanner(string grobName, object cause)
        => (Spanner)MakeGrob(Symbol.Intern(grobName), cause, SpannerSymbol);

    /// <summary>Makes a paper column and announces it.</summary>
    /// <param name="grobName">The grob type name, such as <c>NonMusicalPaperColumn</c>.</param>
    /// <returns>The column.</returns>
    public PaperColumn MakeColumn(string grobName)
        => (PaperColumn)MakeGrob(Symbol.Intern(grobName), Nil.Instance, PaperColumnSymbol);

    /// <summary>
    /// Makes a grob of whichever kind its own <c>meta.classes</c> declares, and
    /// announces it.
    /// </summary>
    /// <param name="grobName">The grob type name.</param>
    /// <param name="cause">What caused it.</param>
    /// <param name="expectedClass">
    /// The class the caller expects, or <see langword="null"/> to take whatever the
    /// definition declares. A mismatch is a programming error, exactly as upstream
    /// treats it.
    /// </param>
    /// <returns>The grob.</returns>
    public Grob MakeGrob(Symbol grobName, object cause, Symbol expectedClass = null)
    {
        object properties = LookupGrobDefinition(grobName);
        if (!(properties is Pair))
        {
            Warn.Error("No grob definition found for `" + grobName.Name + "'.");
            return null;
        }

        Pair metaEntry = SchemeUtilities.Assq(MetaSymbol, properties);
        object meta = metaEntry == null ? Nil.Instance : metaEntry.Cdr;
        Pair classesEntry = SchemeUtilities.Assq(ClassesSymbol, meta);
        object classes = classesEntry == null ? Nil.Instance : classesEntry.Cdr;

        Symbol chosen = ChooseGrobClass(classes, expectedClass, grobName);
        if (chosen == null)
        {
            return null;
        }

        Grob grob;
        if (ReferenceEquals(chosen, SpannerSymbol))
        {
            grob = new Spanner(properties);
        }
        else if (ReferenceEquals(chosen, PaperColumnSymbol))
        {
            grob = new PaperColumn(properties);
        }
        else
        {
            grob = new Item(properties);
        }

        AnnounceGrob(grob, cause);
        return grob;
    }

    /// <summary>
    /// Makes a grob that STICKS to another one: same class as the host, parented to it
    /// on both axes, and carrying the host as its <c>sticky-host</c>.
    /// <para>
    /// The class is taken from the host rather than from the grob definition, which is
    /// the whole point — the same definition sticks to an item as an item and to a
    /// spanner as a spanner. A sticky spanner takes its bounds from its host implicitly,
    /// and ending it is the <c>Spanner_tracking_engraver</c>'s job.
    /// </para>
    /// </summary>
    /// <param name="grobName">The grob type name.</param>
    /// <param name="host">The grob to stick to.</param>
    /// <param name="cause">What caused it.</param>
    /// <returns>The sticky grob, or <see langword="null"/> when it could not be made.</returns>
    public Grob MakeSticky(Symbol grobName, Grob host, object cause)
    {
        if (host == null)
        {
            Warn.ProgrammingError("sticky grob created with no host");
            return null;
        }

        Grob sticky = host is Spanner
            ? (Grob)MakeSpanner(grobName.Name, cause)
            : MakeItem(grobName.Name, cause);

        if (sticky == null)
        {
            return null;
        }

        if (!sticky.HasInterface(StickyGrobInterfaceSymbol))
        {
            Warn.ProgrammingError(
                "sticky grob " + sticky.Name
                + " created with a type that does not have the sticky-grob-interface");
        }

        sticky.SetObject(StickyHostSymbol, host);
        sticky.XParent = host;
        sticky.YParent = host;
        return sticky;
    }

    /// <summary>
    /// Records what caused a grob and wraps it for announcement.
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <param name="cause">What caused it.</param>
    /// <returns>The announcement record.</returns>
    public GrobInfo MakeGrobInfo(Grob grob, object cause)
    {
        /* TODO: Remove Music code when it's no longer needed */
        if (cause is MusicObject music)
        {
            cause = music.GetProperty(CauseSymbol);
        }

        if (grob.GetProperty(CauseSymbol) is Nil && (cause is StreamEvent || cause is Grob))
        {
            grob.SetProperty(CauseSymbol, cause);
        }

        return new GrobInfo(this, grob);
    }

    /// <summary>
    /// Announces a grob to this context and every context above it.
    /// </summary>
    /// <param name="grob">The grob.</param>
    /// <param name="cause">What caused it.</param>
    public void AnnounceGrob(Grob grob, object cause) => AnnounceGrob(MakeGrobInfo(grob, cause));

    /// <summary>Announces a grob.</summary>
    /// <param name="info">The announcement record.</param>
    public void AnnounceGrob(GrobInfo info)
    {
        EngraverGroup group = EngraverGroup;
        if (group == null)
        {
            Warn.ProgrammingError("announcing grob in invalid context");
            return;
        }

        group.AddGrobToAnnounce(info, Direction.Positive);
    }

    /// <summary>Announces the END of a grob, which is what closes a spanner.</summary>
    /// <param name="grob">The grob.</param>
    /// <param name="cause">What caused it to end.</param>
    public void AnnounceEndGrob(Grob grob, object cause)
    {
        EngraverGroup group = EngraverGroup;
        if (group == null)
        {
            Warn.ProgrammingError("announcing grob in invalid context");
            return;
        }

        group.AddGrobToAnnounce(MakeGrobInfo(grob, cause), Direction.Negative);
    }

    /// <summary>
    /// Announces a grob in ANOTHER context — <c>Engraver::announce_grob (inf, ctx)</c>.
    /// An auto-beam announces into the context its beam started in, which may no
    /// longer be this engraver's.
    /// </summary>
    /// <param name="info">The announcement record.</param>
    /// <param name="startEnd">Positive to announce a start, negative an end.</param>
    /// <param name="context">The context to announce in.</param>
    public void AnnounceGrob(GrobInfo info, Direction startEnd, Context context)
    {
        if (context?.Implementation is EngraverGroup group)
        {
            group.AddGrobToAnnounce(info, startEnd);
            return;
        }

        Warn.ProgrammingError("announcing grob in invalid context");
    }

    /// <summary>Announces a grob in another context.</summary>
    /// <param name="info">The announcement record.</param>
    /// <param name="context">The context to announce in.</param>
    public void AnnounceGrob(GrobInfo info, Context context)
        => AnnounceGrob(info, Direction.Positive, context);

    /// <summary>Announces the END of a grob in another context.</summary>
    /// <param name="info">The announcement record.</param>
    /// <param name="context">The context to announce in.</param>
    public void AnnounceEndGrob(GrobInfo info, Context context)
        => AnnounceGrob(info, Direction.Negative, context);

    /// <summary>
    /// Announces a grob in THIS context only, without letting it travel up —
    /// <c>Engraver::announce_grob_locally_only</c>.
    /// </summary>
    /// <param name="info">The announcement record.</param>
    public void AnnounceGrobLocallyOnly(GrobInfo info)
    {
        EngraverGroup group = EngraverGroup;
        if (group == null)
        {
            Warn.ProgrammingError("announcing grob in invalid context");
            return;
        }

        group.AddGrobToAnnounceLocallyOnly(info, Direction.Positive);
    }

    /// <summary>Announces the END of a grob in this context only.</summary>
    /// <param name="info">The announcement record.</param>
    public void AnnounceEndGrobLocallyOnly(GrobInfo info)
    {
        EngraverGroup group = EngraverGroup;
        if (group == null)
        {
            Warn.ProgrammingError("announcing grob in invalid context");
            return;
        }

        group.AddGrobToAnnounceLocallyOnly(info, Direction.Negative);
    }

    /// <summary>
    /// Called for every grob any engraver announces in this context or below it.
    /// <para>
    /// Default: ignore the info.
    /// </para>
    /// </summary>
    /// <param name="info">The announcement record.</param>
    public virtual void AcknowledgeGrob(GrobInfo info)
    {
    }

    /// <summary>Called for every grob END announced in this context or below it.</summary>
    /// <param name="info">The announcement record.</param>
    public virtual void AcknowledgeEndGrob(GrobInfo info)
    {
    }

    /// <summary>
    /// Looks a grob definition up through the context's override chain.
    /// <para>
    /// This is <c>Grob_property_info::updated</c>: it folds every enclosing context's
    /// <c>\override</c>s over the global description and expands nested overrides, so
    /// an <c>\override</c> written in a Staff reaches a NoteHead made in a Voice.
    /// </para>
    /// <para>
    /// When the context tree carries no <c>Grob_properties</c> at all — a hand-built
    /// tree with no global context above it — this falls back on the global
    /// <c>all-grob-descriptions</c> table directly. That is not upstream behaviour;
    /// upstream always has a Global context holding the defaults. It is recorded in
    /// PORT-COVERAGE and exists so an engraver can be exercised before the context
    /// definitions arrive with Track P.
    /// </para>
    /// </summary>
    /// <param name="grobName">The grob type name.</param>
    /// <returns>The basic property alist, or the empty list when undefined.</returns>
    protected virtual object LookupGrobDefinition(Symbol grobName)
    {
        if (Context == null)
        {
            return Nil.Instance;
        }

        object properties = new GrobPropertyInfo(Context, grobName).Updated();
        if (properties is Pair)
        {
            return properties;
        }

        object descriptions = Context.GetProperty(AllGrobDescriptionsSymbol);
        Pair entry = SchemeUtilities.Assq(grobName, descriptions);
        return entry == null ? Nil.Instance : entry.Cdr;
    }

    private static Symbol ChooseGrobClass(object classes, Symbol expected, Symbol grobName)
    {
        List<object> declared = Pair.ToList(classes);
        if (declared.Count == 0)
        {
            //was previously: Warn.Error("meta.classes must be non-empty list for " + grobName.Name);
            // Ruling R1: upstream's engraver.cc names the offending VALUE through
            // print_scm_val, not the grob. Reproduced verbatim.
            Warn.Error("meta.classes must be non-empty list, found "
                       + SchemeUtilities.PrintScmVal(classes));
            return null;
        }

        if (expected != null)
        {
            foreach (object candidate in declared)
            {
                if (ReferenceEquals(candidate, expected))
                {
                    return expected;
                }
            }

            Warn.ProgrammingError(
                "grob " + grobName.Name + " created with disallowed class " + expected.Name);
            return expected;
        }

        if (declared.Count != 1)
        {
            Warn.Error(
                "must have only one element in meta.classes to create"
                + " a grob without specifying the class");
            return null;
        }

        Symbol klass = declared[0] as Symbol;
        if (klass == null
            || (!ReferenceEquals(klass, ItemSymbol)
                && !ReferenceEquals(klass, SpannerSymbol)
                && !ReferenceEquals(klass, PaperColumnSymbol)))
        {
            Warn.Error("grob class should be 'Item, 'Spanner or 'Paper_column");
            return null;
        }

        return klass;
    }
}

/// <summary>
/// The translator group for a context that engraves: it collects announcements and
/// hands them round for acknowledgement.
/// <para>
/// Announcements are queued rather than delivered immediately, and flushed once per
/// timestep. That ordering is what lets an engraver acknowledge a grob that was made
/// later in the same timestep than itself.
/// </para>
/// </summary>
public class EngraverGroup : TranslatorGroup
{
    private static readonly Symbol OverrideSymbol = Symbol.Intern("Override");
    private static readonly Symbol RevertSymbol = Symbol.Intern("Revert");
    private static readonly Symbol SymbolSymbol = Symbol.Intern("symbol");
    private static readonly Symbol PropertyPathSymbol = Symbol.Intern("property-path");
    private static readonly Symbol ValueSymbol = Symbol.Intern("value");
    private static readonly Symbol OnceSymbol = Symbol.Intern("once");
    private static readonly Symbol MatchedPopSymbol
        = Symbol.Intern("ly:context-matched-pop-property");

    private readonly List<(GrobInfo Info, Direction StartEnd)> _announceInfos
        = new List<(GrobInfo, Direction)>();

    private Listener _overrideListener;
    private Listener _revertListener;

    /// <summary>Gets the C++ class name this group corresponds to.</summary>
    public override string ClassName => "Engraver_group";

    /// <summary>
    /// Attaches to a context and starts listening for <c>\override</c> and
    /// <c>\revert</c>.
    /// <para>
    /// These live on the GROUP rather than on any engraver because an override is aimed
    /// at a context, not at whoever happens to draw the grob — which is what lets
    /// <c>\override Staff.NoteHead.color</c> reach note heads made in a Voice below.
    /// </para>
    /// </summary>
    /// <param name="context">The context to attach to.</param>
    public override void ConnectToContext(Context context)
    {
        base.ConnectToContext(context);

        if (context == null)
        {
            return;
        }

        _overrideListener = context.EventSource.AddListener(this, Override, OverrideSymbol);
        _revertListener = context.EventSource.AddListener(this, Revert, RevertSymbol);
    }

    /// <summary>Detaches from the context and stops listening.</summary>
    public override void DisconnectFromContext()
    {
        if (Context != null)
        {
            if (_overrideListener != null)
            {
                Context.EventSource.RemoveListener(_overrideListener, OverrideSymbol);
            }

            if (_revertListener != null)
            {
                Context.EventSource.RemoveListener(_revertListener, RevertSymbol);
            }
        }

        _overrideListener = null;
        _revertListener = null;

        base.DisconnectFromContext();
    }

    /// <summary>Applies an <c>\override</c>, temporarily when it was written <c>\once</c>.</summary>
    /// <param name="streamEvent">The <c>Override</c> event.</param>
    public void Override(StreamEvent streamEvent)
    {
        if (!(streamEvent.GetProperty(SymbolSymbol) is Symbol symbol) || Context == null)
        {
            return;
        }

        GrobPropertyInfo info = new GrobPropertyInfo(Context, symbol);

        if (SchemeUtilities.ToBool(streamEvent.GetProperty(OnceSymbol)))
        {
            object token = info.TemporaryOverride(
                streamEvent.GetProperty(PropertyPathSymbol),
                streamEvent.GetProperty(ValueSymbol));
            AddMatchedPopFinalization(symbol, token);
        }
        else
        {
            info.Push(
                streamEvent.GetProperty(PropertyPathSymbol),
                streamEvent.GetProperty(ValueSymbol));
        }
    }

    /// <summary>Applies a <c>\revert</c>, temporarily when it was written <c>\once</c>.</summary>
    /// <param name="streamEvent">The <c>Revert</c> event.</param>
    public void Revert(StreamEvent streamEvent)
    {
        if (!(streamEvent.GetProperty(SymbolSymbol) is Symbol symbol) || Context == null)
        {
            return;
        }

        GrobPropertyInfo info = new GrobPropertyInfo(Context, symbol);

        if (SchemeUtilities.ToBool(streamEvent.GetProperty(OnceSymbol)))
        {
            object token = info.TemporaryRevert(streamEvent.GetProperty(PropertyPathSymbol));
            AddMatchedPopFinalization(symbol, token);
        }
        else
        {
            info.Pop(streamEvent.GetProperty(PropertyPathSymbol));
        }
    }

    private void AddMatchedPopFinalization(Symbol symbol, object token)
    {
        if (!(token is Pair))
        {
            return;
        }

        GlobalContext global = Context?.GlobalContext;
        global?.AddFinalization(Pair.List(
            Bootstrap.LilyPondScheme.LookupProcedure(MatchedPopSymbol),
            Context,
            symbol,
            token));
    }

    /// <summary>Gets the announcements queued this timestep.</summary>
    public IReadOnlyList<(GrobInfo Info, Direction StartEnd)> AnnounceInfos => _announceInfos;

    /// <summary>
    /// Queues a grob announcement in THIS group only.
    /// <para>
    /// Overridden by <see cref="ScoreEngraver"/>, which uses the hook to typeset every
    /// grob into the root system as it goes by. That is the only reason the local half
    /// is separated from the walk up the tree.
    /// </para>
    /// </summary>
    /// <param name="info">The announcement record.</param>
    /// <param name="startEnd">Positive to announce a start, negative an end.</param>
    public virtual void AddGrobToAnnounceLocallyOnly(GrobInfo info, Direction startEnd)
        => _announceInfos.Add((info, startEnd));

    /// <summary>Queues a grob announcement here and in every group above.</summary>
    /// <param name="info">The announcement record.</param>
    /// <param name="startEnd">Positive to announce a start, negative an end.</param>
    public void AddGrobToAnnounce(GrobInfo info, Direction startEnd)
    {
        // Announcements travel UP: an engraver in an ancestor context gets to see
        // grobs made below it, which is how a Staff-level engraver sees Voice grobs.
        EngraverGroup group = this;
        while (group != null)
        {
            group.AddGrobToAnnounceLocallyOnly(info, startEnd);

            Context parent = group.Context?.Parent;
            if (parent == null)
            {
                break;
            }

            group = parent.Implementation as EngraverGroup;
        }
    }

    /// <summary>Gets a value indicating whether this group or any below it has grobs queued.</summary>
    /// <returns><see langword="true"/> when something is still to be acknowledged.</returns>
    public bool PendingGrobs()
    {
        if (_announceInfos.Count > 0)
        {
            return true;
        }

        if (Context == null)
        {
            return false;
        }

        foreach (Context child in Context.Children)
        {
            if (child.Implementation is EngraverGroup group && group.PendingGrobs())
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Runs the whole announce/acknowledge round over this group and everything below
    /// it, until nothing new is announced.
    /// <para>
    /// The loop is not defensive padding. Acknowledging a grob may CREATE a grob —
    /// a Stem_engraver making a stem for the note heads it just heard about — and that
    /// new grob has to be acknowledged in the same timestep, so the queue can grow
    /// while it is being drained.
    /// </para>
    /// </summary>
    public void DoAnnounces()
    {
        do
        {
            /*
              DOCME: why is this inside the loop?
             */
            if (Context != null)
            {
                foreach (Context child in new List<Context>(Context.Children))
                {
                    if (child.Implementation is EngraverGroup group)
                    {
                        group.DoAnnounces();
                    }
                }
            }

            while (true)
            {
                RunPhase(TranslatorPrecomputeIndex.ProcessAcknowledged);
                if (_announceInfos.Count == 0)
                {
                    break;
                }

                AcknowledgeGrobs();
                _announceInfos.Clear();
            }
        }
        while (PendingGrobs());
    }

    /// <summary>
    /// Hands every queued announcement to every engraver in this group, then clears
    /// the queue.
    /// </summary>
    public void AcknowledgeGrobs()
    {
        if (_announceInfos.Count == 0)
        {
            return;
        }

        // N.B. the queue can grow during this loop, which is why it is indexed rather
        // than snapshotted: an engraver may make a grob while acknowledging one.
        for (int i = 0; i < _announceInfos.Count; i++)
        {
            (GrobInfo Info, Direction StartEnd) announcement = _announceInfos[i];

            foreach (Translator translator in new List<Translator>(Translators))
            {
                if (!(translator is Engraver engraver))
                {
                    continue;
                }

                // An engraver does not acknowledge what it made itself.
                if (ReferenceEquals(engraver, announcement.Info.OriginEngraver))
                {
                    continue;
                }

                if (announcement.StartEnd == Direction.Positive)
                {
                    engraver.AcknowledgeGrob(announcement.Info);
                }
                else
                {
                    engraver.AcknowledgeEndGrob(announcement.Info);
                }
            }
        }

        _announceInfos.Clear();
    }
}
