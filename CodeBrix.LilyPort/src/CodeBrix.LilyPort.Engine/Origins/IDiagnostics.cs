/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2020--2026 David Kastrup <dak@gnu.org>

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

using CodeBrix.LilyPort.Flower;

namespace CodeBrix.LilyPort.Engine.Origins; //was previously: lily/diagnostics.cc, lily/include/diagnostics.hh;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Gives anything that can say where it came from a full set of diagnostic calls, each
/// routed through that origin so the message carries a file, line and quoted excerpt.
/// <para>
/// Implement <see cref="Origin"/> and the rest follow. Grobs, music expressions, music
/// iterators and audio elements all do.
/// </para>
/// <para>
/// DIVERGENCE — AN INTERFACE, NOT A BASE CLASS. Upstream's <c>Diagnostics</c> is a mixin
/// reached by multiple inheritance (<c>class Music : public Smob&lt;Music&gt;, public
/// Diagnostics</c>). C# has no multiple inheritance, so this is an interface with default
/// implementations — the same shape, the same routing, and no base-class slot spent.
/// </para>
/// </summary>
public interface IDiagnostics
{
    /// <summary>
    /// Gets where this object came from, or <see langword="null"/> when it has no origin.
    /// Every method below routes through it.
    /// </summary>
    /// <returns>The origin, or <see langword="null"/>.</returns>
    Input Origin();

    /// <summary>Reports a fatal error, at this object's origin when it has one.</summary>
    /// <param name="message">The error text.</param>
    void Error(string message)
    {
        Input origin = Origin();
        if (origin != null)
        {
            origin.Error(message);
        }
        else
        {
            Warn.Error(message);
        }
    }

    /// <summary>Reports an internal error, at this object's origin when it has one.</summary>
    /// <param name="message">The error text.</param>
    void ProgrammingError(string message)
    {
        Input origin = Origin();
        if (origin != null)
        {
            origin.ProgrammingError(message);
        }
        else
        {
            Warn.ProgrammingError(message);
        }
    }

    /// <summary>Reports a non-fatal error, at this object's origin when it has one.</summary>
    /// <param name="message">The error text.</param>
    void NonFatalError(string message)
    {
        Input origin = Origin();
        if (origin != null)
        {
            origin.NonFatalError(message);
        }
        else
        {
            Warn.NonFatalError(message);
        }
    }

    /// <summary>Reports a warning, at this object's origin when it has one.</summary>
    /// <param name="message">The warning text.</param>
    void Warning(string message)
    {
        Input origin = Origin();
        if (origin != null)
        {
            origin.Warning(message);
        }
        else
        {
            Warn.Warning(message);
        }
    }

    /// <summary>Reports a deprecation warning, once per distinct message.</summary>
    /// <param name="message">The warning text.</param>
    void DeprecationWarning(string message)
    {
        Input origin = Origin();
        if (origin != null)
        {
            origin.DeprecationWarning(message);
        }
        else
        {
            Warn.DeprecationWarning(message);
        }
    }

    /// <summary>Reports an informational message, at this object's origin when it has one.</summary>
    /// <param name="message">The message text.</param>
    void Message(string message)
    {
        Input origin = Origin();
        if (origin != null)
        {
            origin.Message(message);
        }
        else
        {
            Warn.Message(message);
        }
    }

    /// <summary>Reports debug output, at this object's origin when it has one.</summary>
    /// <param name="message">The message text.</param>
    void DebugOutput(string message)
    {
        Input origin = Origin();
        if (origin != null)
        {
            origin.DebugOutput(message);
        }
        else
        {
            Warn.Debug(message);
        }
    }
}
