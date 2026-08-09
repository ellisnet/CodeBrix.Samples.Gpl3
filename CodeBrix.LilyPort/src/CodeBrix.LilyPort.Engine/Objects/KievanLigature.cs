/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2013--2026 Aleksandr Andreev <aleksandr.andreev@gmail.com>

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

using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Objects; //was previously: lily/kievan-ligature.cc, lily/include/kievan-ligature.hh;

// Modified by Jeremy Ellis on 2026-08-09 as part of the CodeBrix port.

/// <summary>
/// A Kievan ligature — in square-notation terms a melisma, since the heads keep a fixed
/// small distance rather than fusing into one shape.
/// </summary>
/// <remarks>
/// <para>
/// The grob draws NOTHING itself, exactly as upstream: every mark on the page comes from
/// the note heads <see cref="Translation.KievanLigatureEngraver"/> lines up, and the
/// spanner exists to own the spacing rod that keeps them together. Its
/// <c>minimum-length</c> is what the engraver writes and what
/// <c>ly:spanner::set-spacing-rods</c> then enforces.
/// </para>
/// <para>
/// The empty stencil is therefore CORRECT, not a stub — and it is registered for exactly
/// that reason. An unregistered <c>ly:kievan-ligature::print</c> would answer the inert
/// unported placeholder, which is truthy, so the backend would try to draw it.
/// </para>
/// </remarks>
public static class KievanLigature
{
    /// <summary>The <c>stencil</c> callback: a Kievan ligature draws nothing itself.</summary>
    /// <param name="me">The ligature spanner.</param>
    /// <returns><c>'()</c>, always.</returns>
    public static object Print(Grob me) => Nil.Instance;
}
