/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2012--2026 Mike Solomon <mike@mikesolomon.org>

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

using CodeBrix.LilyPort.Engine.Music;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/footnote-engraver.cc;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Turns a grob's <c>footnote-music</c> into a real <c>Footnote</c> grob stuck onto it.
/// <para>
/// The footnote is made by the grob's OWN origin engraver rather than by this one, which is
/// what puts it in the right context: a footnote on a note head belongs where the note head
/// was made, not where this engraver happens to live.
/// </para>
/// </summary>
public class FootnoteEngraver : Engraver
{
    private static readonly Symbol FootnoteMusicSymbol = Symbol.Intern("footnote-music");
    private static readonly Symbol FootnoteSymbol = Symbol.Intern("Footnote");
    private static readonly Symbol FootnoteEventSymbol = Symbol.Intern("footnote-event");

    /// <summary>Initializes the engraver in a context.</summary>
    /// <param name="context">The context this engraver belongs to.</param>
    public FootnoteEngraver(Context context)
        : base(context)
    {
    }

    /// <summary>Gets the C++ class name this engraver corresponds to.</summary>
    public override string ClassName => "Footnote_engraver";

    /// <summary>Sticks a <c>Footnote</c> onto every grob that carries footnote music.</summary>
    /// <param name="info">The announcement record.</param>
    public override void AcknowledgeGrob(GrobInfo info)
    {
        if (info.Grob == null)
        {
            return;
        }

        if (!(info.Grob.GetProperty(FootnoteMusicSymbol) is MusicObject mus))
        {
            return;
        }

        if (!mus.IsMusicType(FootnoteEventSymbol))
        {
            Flower.Warn.ProgrammingError("Must be footnote-event.");
            return;
        }

        Engraver eng = info.OriginEngraver ?? this;
        eng.MakeSticky(FootnoteSymbol, info.Grob, mus.ToEvent());

        // The grob has now spent its footnote. Without this it would sprout a fresh one
        // every time it is acknowledged, and a grob is acknowledged by every engraver in
        // its context chain.
        info.Grob.SetProperty(FootnoteMusicSymbol, Nil.Instance);
    }
}
