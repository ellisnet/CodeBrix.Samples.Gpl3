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
using CodeBrix.LilyPort.Engine.Bootstrap;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/vertical-align-engraver.cc, lily/system-start-delimiter-engraver.cc, lily/staff-collecting-engraver.cc;

// Modified by Jeremy Ellis on 2026-08-07 as part of the CodeBrix port.

/*
  Catch groups (staves, lyrics lines, etc.) and stack them vertically.
*/

/// <summary>
/// Makes the one spanner every staff hangs from: the <c>VerticalAlignment</c> at
/// Score level (<c>topLevelAlignment</c> true), or a <c>StaffGrouper</c> inside a
/// <c>StaffGroup</c>-like context. Each acknowledged staff group is added as an
/// alignment element, which is what plants the parent-positioning offset callback on
/// it — the whole vertical layout of a system hangs off that one call.
/// <para>
/// The acknowledgement filters reproduce upstream's <c>ADD_ACKNOWLEDGER</c> pair:
/// <c>hara-kiri-group-spanner</c> — the interface every <c>VerticalAxisGroup</c>
/// carries — and <c>outside-staff</c>.
/// </para>
/// </summary>
public class VerticalAlignEngraver : Engraver
{
    private static readonly Symbol HasAxisGroup = Symbol.Intern("hasAxisGroup");
    private static readonly Symbol TopLevelAlignment = Symbol.Intern("topLevelAlignment");
    private static readonly Symbol CurrentCommandColumn = Symbol.Intern("currentCommandColumn");
    private static readonly Symbol AlignAboveContext = Symbol.Intern("alignAboveContext");
    private static readonly Symbol AlignBelowContext = Symbol.Intern("alignBelowContext");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol StaffAffinity = Symbol.Intern("staff-affinity");
    private static readonly Symbol StaffGrouperSymbol = Symbol.Intern("staff-grouper");
    private static readonly Symbol HaraKiriInterface
        = Symbol.Intern("hara-kiri-group-spanner-interface");

    private static readonly Symbol OutsideStaffInterface
        = Symbol.Intern("outside-staff-interface");

    private Spanner _valign;
    private Dictionary<string, Grob> _idToGroupHashtab;

    // TODO: consider splitting out a Staff_grouper_engraver.
    // The code paths for top_level_ being true or false seem
    // to share very little. --JeanAS

    private bool _topLevel;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public VerticalAlignEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Vertical_align_engraver";

    /// <summary>Gets the alignment spanner being built, for tests.</summary>
    public Spanner Alignment => _valign;

    /// <summary>Makes the context-id-to-group table.</summary>
    public override void Initialize() => _idToGroupHashtab = new Dictionary<string, Grob>();

    /// <summary>
    /// Makes the alignment (or grouper) spanner on the first timestep, unless this
    /// engraver was mistakenly put in a context that has its own axis group.
    /// </summary>
    public override void ProcessMusic()
    {
        if (_valign == null && _idToGroupHashtab != null)
        {
            if (SchemeUtilities.ToBool(GetProperty(HasAxisGroup)))
            {
                Warn.Warning("Ignoring Vertical_align_engraver in VerticalAxisGroup");
                _idToGroupHashtab = null;
                return;
            }

            _topLevel = SchemeUtilities.ToBool(GetProperty(TopLevelAlignment));

            _valign = MakeSpanner(
                _topLevel ? "VerticalAlignment" : "StaffGrouper", Nil.Instance);
            Grob col = GetProperty(CurrentCommandColumn) as Grob;
            _valign.SetBound(Direction.Negative, col);
        }
    }

    /// <summary>Closes the spanner at the final command column.</summary>
    public override void FinalizeTranslation()
    {
        if (_valign != null)
        {
            Grob col = GetProperty(CurrentCommandColumn) as Grob;
            _valign.SetBound(Direction.Positive, col);
            _valign = null;
        }
    }

    /// <summary>Dispatches on the two acknowledged interfaces.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob.HasInterface(HaraKiriInterface))
        {
            AcknowledgeHaraKiriGroupSpanner(info);
        }

        if (info.Grob.HasInterface(OutsideStaffInterface))
        {
            AcknowledgeOutsideStaff(info);
        }
    }

    private void AcknowledgeHaraKiriGroupSpanner(GrobInfo i)
    {
        if (_idToGroupHashtab == null)
        {
            return;
        }

        if (_topLevel)
        {
            Context originCtx = i.OriginEngraver.Context;
            string id = originCtx.IdString;

            _idToGroupHashtab[id] = i.Grob;

            object beforeId = originCtx.GetProperty(AlignAboveContext);
            object afterId = originCtx.GetProperty(AlignBelowContext);

            AlignInterface.AddElement(_valign, i.Grob);

            if (beforeId is Nil && afterId is Nil)
            {
                return;
            }

            Grob beforeGrob = LookupGroup(beforeId);
            Grob afterGrob = LookupGroup(afterId);

            if (beforeGrob == null && afterGrob == null)
            {
                if (AsIdString(beforeId) != null)
                {
                    Warn.Warning("alignAboveContext not found: " + AsIdString(beforeId));
                }
                else
                {
                    Warn.Warning(
                        "alignBelowContext not found: " + (AsIdString(afterId) ?? string.Empty));
                }

                return;
            }

            GrobArray ga = _valign.GetObject(ElementsSymbol) as GrobArray;
            List<Grob> entries = new List<Grob>(ga.Array);
            Grob added = entries[entries.Count - 1];
            entries.RemoveAt(entries.Count - 1);
            int index = -1;
            for (int j = 0; j < entries.Count; j++)
            {
                if (ReferenceEquals(entries[j], beforeGrob)
                    || ReferenceEquals(entries[j], afterGrob))
                {
                    index = j;
                    break;
                }
            }

            if (index >= 0)
            {
                Direction staffAffinity = ReferenceEquals(entries[index], afterGrob)
                    ? Direction.Positive
                    : Direction.Negative;
                if (ReferenceEquals(entries[index], afterGrob))
                {
                    index++;
                }

                entries.Insert(index, added);

                // Only set staff affinity if it already has one.  That way we won't
                // set staff-affinity on things that don't want it (like staves).
                if (SchemeConvert.IsNumber(added.GetProperty(StaffAffinity)))
                {
                    added.SetProperty(StaffAffinity, (long)(int)staffAffinity);
                }
            }

            // When the searched-for group is not among the earlier elements the popped
            // grob is NOT put back — upstream pops before searching and only the found
            // branch re-inserts, so the group silently drops out of the alignment.
            // Ported as-is; the guard above makes it unreachable except for a context
            // aligning against itself.
            ga.SetArray(entries);
        }
        else
        {
            PointerGroupInterface.AddGrob(_valign, ElementsSymbol, i.Grob);
            if (!(i.Grob.GetObject(StaffGrouperSymbol) is Grob))
            {
                i.Grob.SetObject(StaffGrouperSymbol, _valign);
            }
        }
    }

    private void AcknowledgeOutsideStaff(GrobInfo i)
    {
        if (!_topLevel) // valign_ is a staff grouper
        {
            if (_valign != null)
            {
                // Claim outside-staff grobs created by engravers in this immediate
                // context.
                if (ReferenceEquals(i.OriginEngraver.Context, Context))
                {
                    i.Grob.SetParent(_valign, Axis.Y);
                }
            }
            else
            {
                Warn.ProgrammingError(
                    "cannot claim outside-staff grob before creating staff grouper");
            }
        }
    }

    private Grob LookupGroup(object id)
    {
        string key = AsIdString(id);
        return key != null && _idToGroupHashtab.TryGetValue(key, out Grob found)
            ? found
            : null;
    }

    private static string AsIdString(object value)
    {
        switch (value)
        {
            case string s:
                return s;
            case MutableString m:
                return m.ToString();
            default:
                return null;
        }
    }
}

/// <summary>
/// Makes the <c>SystemStartBar</c> / <c>SystemStartBrace</c> /
/// <c>SystemStartBracket</c> / <c>SystemStartSquare</c> spanners in front of a
/// system, nested according to <c>systemStartDelimiterHierarchy</c>, and hands every
/// staff symbol it hears to the delimiter that spans it.
/// </summary>
public class SystemStartDelimiterEngraver : Engraver
{
    private static readonly Symbol SystemStartDelimiterProperty
        = Symbol.Intern("systemStartDelimiter");

    private static readonly Symbol SystemStartDelimiterHierarchy
        = Symbol.Intern("systemStartDelimiterHierarchy");

    private static readonly Symbol CurrentCommandColumn = Symbol.Intern("currentCommandColumn");
    private static readonly Symbol ElementsSymbol = Symbol.Intern("elements");
    private static readonly Symbol StaffSymbolInterface = Symbol.Intern("staff-symbol-interface");
    private static readonly Symbol SystemStartDelimiterInterface
        = Symbol.Intern("system-start-delimiter-interface");

    private static readonly Symbol SystemStartBrace = Symbol.Intern("SystemStartBrace");
    private static readonly Symbol SystemStartBracket = Symbol.Intern("SystemStartBracket");
    private static readonly Symbol SystemStartBar = Symbol.Intern("SystemStartBar");
    private static readonly Symbol SystemStartSquare = Symbol.Intern("SystemStartSquare");

    /// <summary>One node of the delimiter-nesting tree.</summary>
    private class BracketNestingNode
    {
        internal virtual bool AddStaff(Grob grob) => false;

        internal virtual void AddSupport(Grob grob)
        {
        }

        internal virtual void SetBound(Direction d, Grob grob)
        {
        }

        internal virtual void SetNestingSupport(Grob grob)
        {
        }

        internal virtual void CreateGrobs(Engraver engraver, object defaultType)
        {
        }
    }

    /// <summary>A nesting group: one delimiter spanner plus its children.</summary>
    private sealed class BracketNestingGroup : BracketNestingNode
    {
        internal Spanner Delimiter { get; private set; }

        internal List<BracketNestingNode> Children { get; } = new List<BracketNestingNode>();

        internal object TypeSymbol { get; private set; } = Nil.Instance;

        internal void FromList(object x)
        {
            object cursor = x;
            while (cursor is Pair pair)
            {
                object entry = pair.Car;
                if (entry is Pair)
                {
                    BracketNestingGroup node = new BracketNestingGroup();
                    node.FromList(entry);
                    Children.Add(node);
                }
                else if (ReferenceEquals(entry, SystemStartBrace)
                         || ReferenceEquals(entry, SystemStartBracket)
                         || ReferenceEquals(entry, SystemStartBar)
                         || ReferenceEquals(entry, SystemStartSquare))
                {
                    TypeSymbol = entry;
                }
                else
                {
                    Children.Add(new BracketNestingStaff(null));
                }

                cursor = pair.Cdr;
            }
        }

        internal override void AddSupport(Grob grob)
        {
            SidePositionInterface.AddSupport(grob, Delimiter);
            for (int i = 0; i < Children.Count; i++)
            {
                Children[i].AddSupport(grob);
            }
        }

        internal override bool AddStaff(Grob grob)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                if (Children[i].AddStaff(grob))
                {
                    PointerGroupInterface.AddGrob(Delimiter, ElementsSymbol, grob);
                    return true;
                }
            }

            return false;
        }

        internal override void SetNestingSupport(Grob parent)
        {
            if (parent != null)
            {
                SidePositionInterface.AddSupport(Delimiter, parent);
            }

            for (int i = 0; i < Children.Count; i++)
            {
                Children[i].SetNestingSupport(Delimiter);
            }
        }

        internal override void SetBound(Direction d, Grob grob)
        {
            Delimiter.SetBound(d, grob);
            for (int i = 0; i < Children.Count; i++)
            {
                Children[i].SetBound(d, grob);
            }
        }

        internal override void CreateGrobs(Engraver engraver, object defaultType)
        {
            object type = TypeSymbol is Symbol ? TypeSymbol : defaultType;

            // ly_symbol2string upstream raises a Guile type error on a non-symbol;
            // the cast below is the same failure, equally loud.
            Delimiter = engraver.MakeSpanner(((Symbol)type).Name, Nil.Instance);

            for (int i = 0; i < Children.Count; i++)
            {
                Children[i].CreateGrobs(engraver, defaultType);
            }
        }
    }

    /// <summary>A leaf holding one staff.</summary>
    private sealed class BracketNestingStaff : BracketNestingNode
    {
        private Grob _staff;

        internal BracketNestingStaff(Grob staff) => _staff = staff;

        internal override bool AddStaff(Grob g)
        {
            if (_staff == null)
            {
                _staff = g;
                return true;
            }

            return false;
        }
    }

    private BracketNestingGroup _nesting;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public SystemStartDelimiterEngraver(Context context)
        : base(context)
    {
        _nesting = null;
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "System_start_delimiter_engraver";

    /// <summary>Builds the nesting tree and its delimiter grobs on the first timestep.</summary>
    public override void ProcessMusic()
    {
        if (_nesting == null)
        {
            _nesting = new BracketNestingGroup();
            object hierarchy = GetProperty(SystemStartDelimiterHierarchy);
            object delimiterName = GetProperty(SystemStartDelimiterProperty);

            _nesting.FromList(hierarchy);
            _nesting.CreateGrobs(this, delimiterName);
            _nesting.SetBound(
                Direction.Negative, GetProperty(CurrentCommandColumn) as Grob);
        }
    }

    /// <summary>Closes every delimiter and wires the nesting supports.</summary>
    public override void FinalizeTranslation()
    {
        if (_nesting != null)
        {
            _nesting.SetBound(
                Direction.Positive, GetProperty(CurrentCommandColumn) as Grob);
            _nesting.SetNestingSupport(null);

            _nesting = null;
        }
    }

    /// <summary>Dispatches on the two acknowledged interfaces.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob is Spanner staff && staff.HasInterface(StaffSymbolInterface))
        {
            AcknowledgeStaffSymbol(staff);
        }

        if (info.Grob.HasInterface(SystemStartDelimiterInterface))
        {
            AcknowledgeSystemStartDelimiter(info);
        }
    }

    private void AcknowledgeStaffSymbol(Spanner staff)
    {
        bool succ = _nesting.AddStaff(staff);

        if (!succ)
        {
            _nesting.Children.Add(new BracketNestingStaff(null));
            _nesting.AddStaff(staff);
        }
    }

    private void AcknowledgeSystemStartDelimiter(GrobInfo inf)
    {
        _nesting.AddSupport(inf.Grob);
    }
}

/// <summary>
/// Maintains the <c>stavesFound</c> context property: the list of staff symbols
/// currently alive, newest first. Trivial-looking, and half the score-level engravers
/// to come read it — every mark, bar number and jump positions against the staves it
/// names.
/// </summary>
public class StaffCollectingEngraver : Engraver
{
    private static readonly Symbol StavesFound = Symbol.Intern("stavesFound");
    private static readonly Symbol StaffSymbolInterface = Symbol.Intern("staff-symbol-interface");

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public StaffCollectingEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Staff_collecting_engraver";

    /// <summary>Adds an announced staff symbol to <c>stavesFound</c>.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (!(info.Grob is Spanner) || !info.Grob.HasInterface(StaffSymbolInterface))
        {
            return;
        }

        object staffs = GetProperty(StavesFound);
        staffs = new Pair(info.Grob, staffs is Nil ? (object)Nil.Instance : staffs);

        Context.SetProperty(StavesFound, staffs);
    }

    /// <summary>Removes an ended staff symbol from <c>stavesFound</c>.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeEndGrob(GrobInfo info)
    {
        if (!(info.Grob is Spanner) || !info.Grob.HasInterface(StaffSymbolInterface))
        {
            return;
        }

        object staffs = GetProperty(StavesFound);
        staffs = Delq(info.Grob, staffs);

        Context.SetProperty(StavesFound, staffs);
    }

    /// <summary>Guile's <c>delq</c>: a fresh list with every <c>eq?</c> match removed.</summary>
    private static object Delq(object item, object list)
    {
        List<object> kept = new List<object>();
        object cursor = list;
        while (cursor is Pair pair)
        {
            if (!ReferenceEquals(pair.Car, item))
            {
                kept.Add(pair.Car);
            }

            cursor = pair.Cdr;
        }

        object result = Nil.Instance;
        for (int i = kept.Count - 1; i >= 0; i--)
        {
            result = new Pair(kept[i], result);
        }

        return result;
    }
}
