/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2007--2026 Nicolas Sceaux <nicolas.sceaux@free.fr>

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

namespace CodeBrix.LilyPort.Engine.Layout; //was previously: lily/page-marker.cc, lily/include/page-marker.hh;

// Modified by Jeremy Ellis on 2026-08-05 as part of the CodeBrix port.

/// <summary>
/// A toplevel instruction that carries no music: <c>\pageBreak</c>,
/// <c>\noPageTurn</c>, <c>\label</c> and their kin.
/// <para>
/// It travels through the book's score list alongside real scores, and the page
/// breaker reads it when it decides where pages end. Two shapes, never both at once: a
/// PERMISSION (a property symbol and the value to give it) or a LABEL.
/// </para>
/// </summary>
public class PageMarker
{
    /// <summary>Initializes an empty marker.</summary>
    public PageMarker()
    {
        PermissionSymbol = Nil.Instance;
        PermissionValue = Nil.Instance;
        Label = Nil.Instance;
    }

    /// <summary>Initializes a copy of another marker.</summary>
    /// <param name="source">The marker to copy.</param>
    public PageMarker(PageMarker source)
    {
        PermissionSymbol = source.PermissionSymbol;
        PermissionValue = source.PermissionValue;
        Label = source.Label;
    }

    /// <summary>Gets the property this marker sets, or the empty list.</summary>
    public object PermissionSymbol { get; private set; }

    /// <summary>Gets the value the property is set to, or the empty list.</summary>
    public object PermissionValue { get; private set; }

    /// <summary>Gets the label this marker places, or the empty list.</summary>
    public object Label { get; private set; }

    /// <summary>Records a page-breaking or page-turning permission.</summary>
    /// <param name="symbol">The property name.</param>
    /// <param name="permission">The value.</param>
    public void SetPermission(object symbol, object permission)
    {
        PermissionSymbol = symbol;
        PermissionValue = permission;
    }

    /// <summary>Records a label.</summary>
    /// <param name="label">The label symbol.</param>
    public void SetLabel(object label) => Label = label;

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description of the marker.</returns>
    public override string ToString()
        => Label is Nil
            ? "#<Page_marker " + PermissionSymbol + " " + PermissionValue + ">"
            : "#<Page_marker label " + Label + ">";
}
