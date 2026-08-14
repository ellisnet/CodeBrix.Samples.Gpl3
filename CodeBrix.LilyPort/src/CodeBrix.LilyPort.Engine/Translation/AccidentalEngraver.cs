/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1997--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>
  Modified 2001--2002 by Rune Zedeler <rz@daimi.au.dk>

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
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Runtime;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/accidental-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/*
  localAlterations is changed at runtime, which means that references
  in grobs should always store ly_deep_copy ()s of those.
*/

/// <summary>
/// Makes accidentals: catches note heads, ties and key changes, asks the
/// <c>autoAccidentals</c> and <c>autoCautionaries</c> rule lists what each pitch needs,
/// and keeps <c>localAlterations</c> — the running record of which alterations are in
/// force in the current measure — up to date on every context that carries it.
/// <para>
/// This engraver usually lives at Staff level but CREATES each accidental through the
/// engraver that announced the note head, so <c>\override</c>s written at Voice level
/// reach it — upstream's comment on <c>make_standard_accidental</c> spells that out.
/// The tie half is subtle: a tied note's accidental is normally deleted as redundant
/// (<c>ly:accidental-interface::remove-tied</c>), and the <c>localAlterations</c> entry
/// it leaves behind is the symbol <c>tied</c> rather than an alteration, so the NEXT
/// note of that pitch still knows an accidental is owing.
/// </para>
/// </summary>
public class AccidentalEngraver : Engraver
{
    private static readonly Symbol KeyAlterationsSymbol = Symbol.Intern("keyAlterations");
    private static readonly Symbol LocalAlterationsSymbol = Symbol.Intern("localAlterations");
    private static readonly Symbol AutoAccidentalsSymbol = Symbol.Intern("autoAccidentals");
    private static readonly Symbol AutoCautionariesSymbol = Symbol.Intern("autoCautionaries");
    private static readonly Symbol PitchSymbol = Symbol.Intern("pitch");
    private static readonly Symbol CautionarySymbol = Symbol.Intern("cautionary");
    private static readonly Symbol ForceAccidentalSymbol = Symbol.Intern("force-accidental");
    private static readonly Symbol ForcedSymbol = Symbol.Intern("forced");
    private static readonly Symbol TrillSpanEventSymbol = Symbol.Intern("trill-span-event");
    private static readonly Symbol SuggestAccidentalsSymbol = Symbol.Intern("suggestAccidentals");
    private static readonly Symbol ExtraNaturalSymbol = Symbol.Intern("extraNatural");
    private static readonly Symbol RestoreFirstSymbol = Symbol.Intern("restore-first");
    private static readonly Symbol SideAxisSymbol = Symbol.Intern("side-axis");
    private static readonly Symbol AccidentalGroupingSymbol = Symbol.Intern("accidentalGrouping");
    private static readonly Symbol VoiceSymbol = Symbol.Intern("voice");
    private static readonly Symbol AccidentalGrobSymbol = Symbol.Intern("accidental-grob");
    private static readonly Symbol StemSymbol = Symbol.Intern("stem");
    private static readonly Symbol TieSymbol = Symbol.Intern("tie");
    private static readonly Symbol TiedSymbol = Symbol.Intern("tied");
    private static readonly Symbol DurationSymbol = Symbol.Intern("duration");
    private static readonly Symbol StyleSymbol = Symbol.Intern("style");
    private static readonly Symbol HarmonicSymbol = Symbol.Intern("harmonic");
    private static readonly Symbol HarmonicAccidentalsSymbol = Symbol.Intern("harmonicAccidentals");
    private static readonly Symbol NullAccidentalsSymbol = Symbol.Intern("nullAccidentals");

    private static readonly Symbol AccidentalParticipatingHeadInterface
        = Symbol.Intern("accidental-participating-head-interface");

    private static readonly Symbol ArpeggioInterface = Symbol.Intern("arpeggio-interface");
    private static readonly Symbol ChordBracketInterface = Symbol.Intern("chord-bracket-interface");
    private static readonly Symbol ChordSlurInterface = Symbol.Intern("chord-slur-interface");
    private static readonly Symbol FingerInterface = Symbol.Intern("finger-interface");
    private static readonly Symbol TieInterface = Symbol.Intern("tie-interface");
    private static readonly Symbol NoteColumnInterface = Symbol.Intern("note-column-interface");
    private static readonly Symbol NoteHeadInterface = Symbol.Intern("note-head-interface");

    /// <summary>One note head waiting for its accidental decision.</summary>
    private sealed class AccidentalEntry
    {
        internal bool Done { get; set; }

        internal StreamEvent Melodic { get; set; }

        internal Grob Accidental { get; set; }

        internal Context Origin { get; set; }

        internal Engraver OriginEngraver { get; set; }

        internal Grob Head { get; set; }

        internal bool Tied { get; set; }
    }

    private object _lastKeysig = Nil.Instance;

    private readonly List<Grob> _leftObjects = new List<Grob>();
    private readonly List<Grob> _rightObjects = new List<Grob>();

    private Grob _accidentalPlacement;

    private readonly List<AccidentalEntry> _accidentals = new List<AccidentalEntry>();
    private readonly List<Spanner> _ties = new List<Spanner>();
    private readonly List<Item> _noteColumns = new List<Item>();

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public AccidentalEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Accidental_engraver";

    private void UpdateLocalKeySignature(object newSig)
    {
        _lastKeysig = newSig;
        MeasureCounting.PreorderWalk(Context, ctx =>
            ctx.SetProperty(LocalAlterationsSymbol, SchemeUtilities.DeepCopy(newSig)));

        Context trans = Context?.Parent;

        /*
          Reset parent contexts so that e.g. piano-accidentals won't remember old
          cross-staff accidentals after key-sig-changes.
        */

        while (trans != null && trans.HereDefined(LocalAlterationsSymbol, out object _))
        {
            trans.SetProperty(LocalAlterationsSymbol, SchemeUtilities.DeepCopy(_lastKeysig));
            trans = trans.Parent;
        }
    }

    /// <summary>What one pass over an accidental rule list decided.</summary>
    private struct AccidentalResult
    {
        internal bool NeedAcc;

        internal bool NeedRestore;

        internal AccidentalResult(object scm)
        {
            NeedRestore = scm is Pair pair && SchemeUtilities.ToBool(pair.Car);
            NeedAcc = scm is Pair inner && SchemeUtilities.ToBool(inner.Cdr);
        }

        internal readonly int Score() => (NeedAcc ? 1 : 0) + (NeedRestore ? 1 : 0);
    }

    private static AccidentalResult CheckPitchAgainstRules(
        Pitch pitch, Context origin, object rules, int barNumber)
    {
        AccidentalResult result = default;
        object pitchScm = pitch;
        object barnumScm = (long)barNumber;

        if (rules is Pair first && !(first.Car is Symbol))
        {
            Warn.Warning(
                "accidental typesetting list must begin with context-name: "
                + Printer.Display(first.Car));
        }

        for (; rules is Pair pair && origin != null; rules = pair.Cdr)
        {
            object rule = pair.Car;
            if (SchemeUtilities.IsProcedure(rule))
            {
                object ruleResultScm = SchemeUtilities.CallCallback(rule, origin, pitchScm, barnumScm);
                AccidentalResult ruleResult = new AccidentalResult(ruleResultScm);

                result.NeedAcc |= ruleResult.NeedAcc;
                result.NeedRestore |= ruleResult.NeedRestore;
            }

            /*
              If symbol then it is a context name.  Scan parent contexts to
              find it.
            */
            else if (rule is Symbol symbol)
            {
                Context dad = origin.FindContextAbove(symbol);
                if (dad != null)
                {
                    origin = dad;
                }
            }
            else
            {
                Warn.Warning(
                    "procedure or context-name expected for accidental rule, found "
                    + Printer.Write(rule));
            }
        }

        return result;
    }

    /// <summary>
    /// Decides, for every note head heard this timestep, whether it needs an
    /// accidental, a cautionary one, or none, and creates what is needed.
    /// </summary>
    public override void ProcessAcknowledged()
    {
        if (_accidentals.Count > 0 && !_accidentals[_accidentals.Count - 1].Done)
        {
            object accidentalRules = GetProperty(AutoAccidentalsSymbol);
            object cautionaryRules = GetProperty(AutoCautionariesSymbol);
            int barnum = MeasureCounting.MeasureNumber(Context);

            for (int i = 0; i < _accidentals.Count; i++)
            {
                if (_accidentals[i].Done)
                {
                    continue;
                }

                _accidentals[i].Done = true;

                StreamEvent note = _accidentals[i].Melodic;
                Context origin = _accidentals[i].Origin;

                Pitch pitch = note.GetProperty(PitchSymbol) as Pitch;
                if (pitch == null)
                {
                    continue;
                }

                AccidentalResult acc = CheckPitchAgainstRules(
                    pitch, origin, accidentalRules, barnum);
                AccidentalResult caut = CheckPitchAgainstRules(
                    pitch, origin, cautionaryRules, barnum);

                bool cautionary = SchemeUtilities.ToBool(note.GetProperty(CautionarySymbol));
                if (caut.Score() > acc.Score())
                {
                    acc.NeedAcc |= caut.NeedAcc;
                    acc.NeedRestore |= caut.NeedRestore;

                    cautionary = true;
                }

                bool forced = SchemeUtilities.ToBool(note.GetProperty(ForceAccidentalSymbol));
                if (!acc.NeedAcc && forced)
                {
                    acc.NeedAcc = true;
                }

                /*
                  Cannot look for ties: it's not guaranteed that they reach
                  us before the notes.
                */
                if (!note.IsInEventClass(TrillSpanEventSymbol))
                {
                    if (acc.NeedAcc)
                    {
                        CreateAccidental(_accidentals[i], acc.NeedRestore, cautionary);
                    }

                    if (forced || cautionary)
                    {
                        // Upstream writes through the entry's accidental_ without a null
                        // check; the flags imply one was just created. The `?.` only
                        // changes what happens in a state upstream would have crashed in.
                        _accidentals[i].Accidental?.SetProperty(ForcedSymbol, true);
                    }
                }
            }
        }
    }

    private void CreateAccidental(AccidentalEntry entry, bool restoreNatural, bool cautionary)
    {
        StreamEvent note = entry.Melodic;
        Grob support = entry.Head;
        object suggest = entry.Origin != null
            ? entry.Origin.GetProperty(SuggestAccidentalsSymbol)
            : Nil.Instance;
        bool bsuggest = SchemeUtilities.ToBool(suggest);
        Grob a;
        if (bsuggest || (cautionary && ReferenceEquals(suggest, CautionarySymbol)))
        {
            a = MakeSuggestedAccidental(note, support, entry.OriginEngraver);
        }
        else
        {
            a = MakeStandardAccidental(note, support, entry.OriginEngraver, cautionary);
        }

        if (restoreNatural)
        {
            if (SchemeUtilities.ToBool(GetProperty(ExtraNaturalSymbol)))
            {
                a.SetProperty(RestoreFirstSymbol, true);
            }
        }

        entry.Accidental = a;
    }

    private Grob MakeStandardAccidental(
        StreamEvent note, Grob noteHead, Engraver trans, bool cautionary)
    {
        _ = note;

        /*
          We construct the accidentals at the originating Voice
          level, so that we get the property settings for
          Accidental from the respective Voice.
        */
        Grob a = cautionary
            ? trans.MakeItem("AccidentalCautionary", noteHead)
            : trans.MakeItem("Accidental", noteHead);

        /*
          We add the accidentals to the support of the arpeggio,
          so it is put left of the accidentals.
        */
        for (int i = 0; i < _leftObjects.Count; i++)
        {
            if (SchemeUtilities.IsEqual(_leftObjects[i].GetProperty(SideAxisSymbol), (long)Axis.X))
            {
                SidePositionInterface.AddSupport(_leftObjects[i], a);
            }
        }

        for (int i = 0; i < _rightObjects.Count; i++)
        {
            SidePositionInterface.AddSupport(a, _rightObjects[i]);
        }

        a.YParent = noteHead;

        if (_accidentalPlacement == null)
        {
            _accidentalPlacement = MakeItem("AccidentalPlacement", a);
        }

        AccidentalPlacement.AddAccidental(
            _accidentalPlacement, a,
            ReferenceEquals(GetProperty(AccidentalGroupingSymbol), VoiceSymbol),
            trans);

        noteHead.SetObject(AccidentalGrobSymbol, a);

        return a;
    }

    private static Grob MakeSuggestedAccidental(StreamEvent note, Grob noteHead, Engraver trans)
    {
        _ = note;

        Grob a = trans.MakeItem("AccidentalSuggestion", noteHead);

        SidePositionInterface.AddSupport(a, noteHead);
        if (a.GetObject(StemSymbol) is Grob stem)
        {
            SidePositionInterface.AddSupport(a, stem);
        }

        a.XParent = noteHead;
        return a;
    }

    /// <summary>Forgets the key signature at the end of the piece.</summary>
    public override void FinalizeTranslation()
    {
        _lastKeysig = Nil.Instance;
    }

    /// <summary>
    /// Marries the timestep's ties to their accidentals, then records every heard
    /// alteration into <c>localAlterations</c> on every context that carries it.
    /// </summary>
    public override void StopTranslationTimestep()
    {
        for (int j = _ties.Count; j-- > 0;)
        {
            Grob r = TieHead(_ties[j], Direction.Positive);
            Grob l = TieHead(_ties[j], Direction.Negative);
            if (l != null && r != null)
            {
                // Don't mark accidentals as "tied" when the pitch is not
                // actually the same.  This is relevant for enharmonic ties.
                StreamEvent le = l.EventCause();
                StreamEvent re = r.EventCause();
                if (le != null && re != null
                    && !SchemeUtilities.IsEqual(
                        le.GetProperty(PitchSymbol), re.GetProperty(PitchSymbol)))
                {
                    continue;
                }
            }

            for (int i = _accidentals.Count; i-- > 0;)
            {
                if (ReferenceEquals(_accidentals[i].Head, r))
                {
                    if (_accidentals[i].Accidental is Grob g)
                    {
                        g.SetObject(TieSymbol, _ties[j]);
                        _accidentals[i].Tied = true;
                    }

                    _ties.RemoveAt(j);
                    break;
                }
            }
        }

        for (int i = _accidentals.Count; i-- > 0;)
        {
            StreamEvent note = _accidentals[i].Melodic;
            Context origin = _accidentals[i].Origin;

            int barnum = MeasureCounting.MeasureNumber(origin);

            Pitch pitch = note.GetProperty(PitchSymbol) as Pitch;
            if (pitch == null)
            {
                continue;
            }

            int n = pitch.NoteName;
            int o = pitch.Octave;
            Rational a = pitch.Alteration;
            object key = new Pair((long)o, (long)n);

            Duration? dur = note.GetProperty(DurationSymbol) is Duration d ? d : (Duration?)null;
            Moment endMom = MeasureCounting.NoteEndMom(Context, dur);
            object position = new Pair((long)barnum, endMom);

            while (origin != null)
            {
                Context where = origin.WhereDefined(LocalAlterationsSymbol, out object localsig);
                if (where == null)
                {
                    break;
                }

                bool change = false;
                if (_accidentals[i].Tied
                    && !SchemeUtilities.ToBool(
                        _accidentals[i].Accidental.GetProperty(ForcedSymbol)))
                {
                    /*
                      Remember an alteration that is different both from
                      that of the tied note and of the key signature.
                    */
                    localsig = AssocPrependX(localsig, key, new Pair(TiedSymbol, position));
                    change = true;
                }
                else
                {
                    /*
                      not really correct if there is more than one
                      note head with the same notename.
                    */
                    localsig = AssocPrependX(
                        localsig, key, new Pair(SchemeConvert.FromRational(a), position));
                    change = true;
                }

                if (change)
                {
                    // TODO: This is suspicious because `origin` is not necessarily
                    // where where_defined() found localAlterations.  If the intent
                    // is to update the value in place, use `where` instead; also set
                    // `origin = where->get_parent ()` below, probably.  If this is
                    // actually correct, it deserves an explanation.
                    origin.SetProperty(LocalAlterationsSymbol, localsig);
                }

                origin = origin.Parent;
            }
        }

        if (_accidentalPlacement != null)
        {
            for (int i = 0; i < _noteColumns.Count; i++)
            {
                SeparationItem.AddConditionalItem(_noteColumns[i], _accidentalPlacement);
            }
        }

        _accidentalPlacement = null;
        _accidentals.Clear();
        _noteColumns.Clear();
        _leftObjects.Clear();
        _rightObjects.Clear();
    }

    /// <summary>
    /// The port's single acknowledge hook, filtering by interface where upstream
    /// declares one <c>ADD_ACKNOWLEDGER</c> per interface: participating note heads
    /// queue an entry, arpeggios / chord brackets / chord slurs and fingerings become
    /// side supports, and note columns are remembered for the conditional spacing item.
    /// </summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        Grob grob = info.Grob;
        if (grob == null)
        {
            return;
        }

        if (grob.HasInterface(AccidentalParticipatingHeadInterface))
        {
            AcknowledgeAccidentalParticipatingHead(info);
        }

        if (grob.HasInterface(ArpeggioInterface)
            || grob.HasInterface(ChordBracketInterface)
            || grob.HasInterface(ChordSlurInterface))
        {
            _leftObjects.Add(grob);
        }

        if (grob.HasInterface(FingerInterface))
        {
            _leftObjects.Add(grob);
        }

        if (grob is Item item && item.HasInterface(NoteColumnInterface))
        {
            _noteColumns.Add(item);
        }
    }

    /// <summary>Catches the END of every tie, for the tie-forgetting protocol.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeEndGrob(GrobInfo info)
    {
        if (info.Grob is Spanner spanner && spanner.HasInterface(TieInterface))
        {
            _ties.Add(spanner);
        }
    }

    private void AcknowledgeAccidentalParticipatingHead(GrobInfo info)
    {
        StreamEvent note = info.EventCause;
        if (note != null
            // option to skip accidentals on string harmonics
            && (SchemeUtilities.ToBool(GetProperty(HarmonicAccidentalsSymbol))
                || !ReferenceEquals(info.Grob.GetProperty(StyleSymbol), HarmonicSymbol))
            // ignore accidentals in non-printing voices like NullVoice
            && !SchemeUtilities.ToBool(
                info.OriginEngraver.Context.GetProperty(NullAccidentalsSymbol)))
        {
            AccidentalEntry entry = new AccidentalEntry();
            entry.Head = info.Grob;
            entry.OriginEngraver = info.OriginEngraver;
            entry.Origin = entry.OriginEngraver.Context;
            entry.Melodic = note;

            _accidentals.Add(entry);
        }
    }

    /// <summary>Reseeds <c>localAlterations</c> whenever the key signature changes.</summary>
    public override void ProcessMusic()
    {
        object sig = GetProperty(KeyAlterationsSymbol);
        if (!ReferenceEquals(_lastKeysig, sig))
        {
            UpdateLocalKeySignature(sig);
        }
    }

    /// <summary>
    /// <c>Tie::head</c>, carried forward from <c>lily/tie.cc</c>: the
    /// bound on one side, when it is a note head. Recorded in PORT-COVERAGE so the
    /// tie group knows the method exists here.
    /// </summary>
    private static Grob TieHead(Spanner me, Direction d)
    {
        Item it = me.GetBound(d) as Item;
        return it != null && it.HasInterface(NoteHeadInterface) ? it : null;
    }

    /// <summary>
    /// <c>ly_assoc_prepend_x</c>: removes any <c>equal?</c>-keyed entry, then prepends
    /// a fresh one, so the newest alteration for a key is always first.
    /// </summary>
    private static object AssocPrependX(object alist, object key, object value)
    {
        List<object> kept = new List<object>();
        object cursor = alist;
        while (cursor is Pair pair)
        {
            if (!(pair.Car is Pair entry && SchemeUtilities.IsEqual(entry.Car, key)))
            {
                kept.Add(pair.Car);
            }

            cursor = pair.Cdr;
        }

        object result = cursor is Nil ? Nil.Instance : (cursor ?? Nil.Instance);
        for (int i = kept.Count - 1; i >= 0; i--)
        {
            result = new Pair(kept[i], result);
        }

        return new Pair(new Pair(key, value), result);
    }
}

/// <summary>
/// The measure/clock helpers of <c>lily/context.cc</c> that its ported half
/// (<c>Translation/Context.cs</c>) does not carry yet: <c>measure_number</c>,
/// <c>note_end_mom</c> and <c>preorder_walk</c>. They live here because the accidental
/// engravers are their first port-side callers; PORT-COVERAGE flags them for a move
/// into <c>Context.cs</c> at integration.
/// </summary>
internal static class MeasureCounting
{
    private static readonly Symbol InternalBarNumberSymbol = Symbol.Intern("internalBarNumber");
    private static readonly Symbol MeasurePositionSymbol = Symbol.Intern("measurePosition");

    /// <summary>
    /// <c>measure_number</c>: the bar number, counted one back when the measure
    /// position is negative — inside an upbeat the previous measure is still current.
    /// </summary>
    /// <param name="context">The context whose clock is read.</param>
    /// <returns>The measure number.</returns>
    internal static int MeasureNumber(Context context)
    {
        object barnum = context != null
            ? context.GetProperty(InternalBarNumberSymbol)
            : Nil.Instance;
        object smp = context != null
            ? context.GetProperty(MeasurePositionSymbol)
            : Nil.Instance;

        int bn = SchemeConvert.IsNumber(barnum)
            ? SchemeConvert.ToInt(barnum, "internalBarNumber")
            : 0;
        Moment mp = smp is Moment moment ? moment : Moment.Zero;
        if (mp.MainPart < Rational.Zero)
        {
            bn--;
        }

        return bn;
    }

    /// <summary>
    /// <c>note_end_mom</c>: the moment where a note of the given duration, happening
    /// now, will end. Inside grace time the whole length stays in the grace part.
    /// </summary>
    /// <param name="context">The context whose clock is read.</param>
    /// <param name="dur">The duration, or <see langword="null"/> for none.</param>
    /// <returns>The end moment.</returns>
    internal static Moment NoteEndMom(Context context, Duration? dur)
    {
        Moment now = context != null ? context.NowMoment : Moment.Zero;
        Rational durLength = dur.HasValue ? dur.Value.ToWholeNotes() : Rational.Zero;

        return now.GracePart < Rational.Zero
            ? new Moment(now.MainPart, now.GracePart + durLength)
            : new Moment(now.MainPart + durLength, Rational.Zero);
    }

    /// <summary>
    /// <c>preorder_walk</c>: visits a context and then, recursively, every child.
    /// </summary>
    /// <param name="context">The context to start from.</param>
    /// <param name="visit">The visitor.</param>
    internal static void PreorderWalk(Context context, Action<Context> visit)
    {
        if (context == null || visit == null)
        {
            return;
        }

        visit(context);
        foreach (Context child in new List<Context>(context.Children))
        {
            PreorderWalk(child, visit);
        }
    }
}
