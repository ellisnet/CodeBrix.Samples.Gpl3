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
using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/key-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Engraves a key signature.
/// <para>
/// The engraver maintains <c>keyAlterations</c> — which <c>Accidental_engraver</c>
/// reads to seed <c>localAlterations</c> — and <c>lastKeyAlterations</c>, whose
/// difference from the current key decides whether a <c>KeyCancellation</c> is
/// printed.
/// </para>
/// </summary>
public class KeyEngraver : Engraver
{
    private static readonly Symbol C0PositionSymbol = Symbol.Intern("c0-position");
    private static readonly Symbol MiddleCClefPositionSymbol
        = Symbol.Intern("middleCClefPosition");

    private static readonly Symbol LastKeyAlterationsSymbol = Symbol.Intern("lastKeyAlterations");
    private static readonly Symbol KeyAlterationsSymbol = Symbol.Intern("keyAlterations");
    private static readonly Symbol PrintKeyCancellationSymbol
        = Symbol.Intern("printKeyCancellation");

    private static readonly Symbol AlterationAlistSymbol = Symbol.Intern("alteration-alist");
    private static readonly Symbol ExplicitKeySignatureVisibilitySymbol
        = Symbol.Intern("explicitKeySignatureVisibility");

    private static readonly Symbol BreakVisibilitySymbol = Symbol.Intern("break-visibility");
    private static readonly Symbol NonDefaultSymbol = Symbol.Intern("non-default");
    private static readonly Symbol CreateKeyOnClefChangeSymbol
        = Symbol.Intern("createKeyOnClefChange");

    private static readonly Symbol PitchAlistSymbol = Symbol.Intern("pitch-alist");
    private static readonly Symbol KeyAlterationOrderSymbol = Symbol.Intern("keyAlterationOrder");
    private static readonly Symbol TonicSymbol = Symbol.Intern("tonic");
    private static readonly Symbol ClefInterfaceSymbol = Symbol.Intern("clef-interface");

    private StreamEvent _keyEvent;
    private Item _item;
    private Item _cancellation;

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public KeyEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Key_engraver";

    /// <summary>Gets the key signature made this timestep, for tests.</summary>
    public Item KeyItem => _item;

    /// <summary>Gets the key cancellation made this timestep, for tests.</summary>
    public Item Cancellation => _cancellation;

    /// <summary>Starts listening for key changes.</summary>
    public override void ConnectToContext()
    {
        base.ConnectToContext();
        ListenTo("key-change-event", ListenKeyChange);
    }

    /// <summary>Stops listening.</summary>
    public override void DisconnectFromContext()
    {
        RemoveListeners();
        base.DisconnectFromContext();
    }

    /// <summary>Upstream's <c>finalize</c> is deliberately empty.</summary>
    public override void FinalizeTranslation()
    {
    }

    private void CreateKey(bool isDefault)
    {
        if (_item == null)
        {
            _item = MakeItem(
                "KeySignature", _keyEvent != null ? (object)_keyEvent : Nil.Instance);

            /* Use middleCClefPosition rather than middleCPosition, because cue
             * notes with a different clef will modify middleCPosition. The
             * Key signature, however, should still be printed at the original
             * position. */
            _item.SetProperty(C0PositionSymbol, GetProperty(MiddleCClefPositionSymbol));

            object last = GetProperty(LastKeyAlterationsSymbol);
            object key = GetProperty(KeyAlterationsSymbol);

            if ((TranslatorSchemeHelpers.ToBool(GetProperty(PrintKeyCancellationSymbol)) || key is Nil)
                && !ReferenceEquals(last, key))
            {
                object restore = Nil.Instance;
                object cursor = last;
                while (cursor is Pair pair)
                {
                    if (pair.Car is Pair entry)
                    {
                        Pair newAlterPair = TranslatorSchemeHelpers.Assoc(entry.Car, key);
                        Rational oldAlter = TranslatorSchemeHelpers.ToRational(entry.Cdr, Rational.Zero);
                        if (newAlterPair == null
                            || (TranslatorSchemeHelpers.ToRational(newAlterPair.Cdr, Rational.Zero)
                                - oldAlter) * oldAlter < Rational.Zero)
                        {
                            restore = new Pair(entry, restore);
                        }
                    }

                    cursor = pair.Cdr;
                }

                if (restore is Pair)
                {
                    _cancellation = MakeItem(
                        "KeyCancellation",
                        _keyEvent != null ? (object)_keyEvent : Nil.Instance);

                    _cancellation.SetProperty(AlterationAlistSymbol, restore);
                    _cancellation.SetProperty(
                        C0PositionSymbol, GetProperty(MiddleCClefPositionSymbol));
                }
            }

            _item.SetProperty(AlterationAlistSymbol, Reverse(key));
        }

        if (!isDefault)
        {
            object visibility = GetProperty(ExplicitKeySignatureVisibilitySymbol);
            _item.SetProperty(BreakVisibilitySymbol, visibility);
            _item.SetProperty(NonDefaultSymbol, true);
        }
    }

    private void ListenKeyChange(StreamEvent ev)
    {
        /* do this only once, just to be on the safe side.  */
        if (StreamEvent.AssignEventOnce(ref _keyEvent, ev))
        {
            ReadEvent(_keyEvent);
        }
    }

    /// <summary>Recreates the key when a clef arrives, if asked to.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob.HasInterface(ClefInterfaceSymbol))
        {
            if (TranslatorSchemeHelpers.ToBool(GetProperty(CreateKeyOnClefChangeSymbol)))
            {
                CreateKey(false);
            }
        }
    }

    /// <summary>Creates the key signature where it can be seen.</summary>
    public override void ProcessMusic()
    {
        // Efficiency: don't create a KeySignature where it would not be
        // visible anyway.
        if (Context.BreakAllowed(Context))
        {
            CreateKey(true);
        }

        if (_keyEvent != null
            || !ReferenceEquals(
                GetProperty(LastKeyAlterationsSymbol), GetProperty(KeyAlterationsSymbol)))
        {
            CreateKey(false);
        }
    }

    /// <summary>Remembers the key the timestep ends with.</summary>
    public override void StopTranslationTimestep()
    {
        _item = null;
        Context?.SetProperty(LastKeyAlterationsSymbol, GetProperty(KeyAlterationsSymbol));
        _cancellation = null;
        _keyEvent = null;
    }

    private void ReadEvent(StreamEvent ev)
    {
        object pitchAlist = ev.GetProperty(PitchAlistSymbol);
        if (!(pitchAlist is Pair))
        {
            return;
        }

        object accs = Nil.Instance;

        List<object> alist = Pair.ToList(pitchAlist);
        object order = GetProperty(KeyAlterationOrderSymbol);
        object orderCursor = order;
        while (orderCursor is Pair orderPair && alist.Count > 0)
        {
            // scm_member uses equal?, and so does the removal that follows it.
            int index = IndexOfEqual(alist, orderPair.Car);
            if (index >= 0)
            {
                accs = new Pair(alist[index], accs);
                alist.RemoveAt(index);
            }

            orderCursor = orderPair.Cdr;
        }

        if (alist.Count > 0)
        {
            bool warn = false;
            foreach (object entry in alist)
            {
                if (entry is Pair pair
                    && TranslatorSchemeHelpers.ToRational(pair.Cdr, Rational.Zero).IsNonZero)
                {
                    warn = true;
                    accs = new Pair(entry, accs);
                }
            }

            if (warn)
            {
                TranslatorSchemeHelpers.EventWarning(
                    ev, "Incomplete keyAlterationOrder for key signature");
            }
        }

        Context?.SetProperty(KeyAlterationsSymbol, Reverse(accs));
        Context?.SetProperty(TonicSymbol, ev.GetProperty(TonicSymbol));
    }

    /// <summary>Seeds the key properties before the first timestep.</summary>
    public override void Initialize()
    {
        Context?.SetProperty(KeyAlterationsSymbol, Nil.Instance);
        Context?.SetProperty(LastKeyAlterationsSymbol, Nil.Instance);

        Context?.SetProperty(TonicSymbol, new Pitch(0, 0, Rational.Zero));
    }

    private static object Reverse(object list)
    {
        object result = Nil.Instance;
        object cursor = list;
        while (cursor is Pair pair)
        {
            result = new Pair(pair.Car, result);
            cursor = pair.Cdr;
        }

        return result;
    }

    private static int IndexOfEqual(List<object> list, object value)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (CorePrimitives.SchemeEqual(list[i], value))
            {
                return i;
            }
        }

        return -1;
    }
}
