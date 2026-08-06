/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 1999--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

namespace CodeBrix.LilyPort.Engine.Translation; //was previously: lily/context-handle.cc, lily/include/context-handle.hh;

// Modified by Jeremy Ellis on 2026-08-05 as part of the CodeBrix port.

/// <summary>
/// A counted reference to a context: while any handle points at a context, that context
/// is not removable.
/// <para>
/// This is what keeps an ossia staff alive for exactly as long as the iterator that
/// created it, and no longer. <see cref="Context.CheckRemoval"/> asks
/// <see cref="Context.IsRemovable"/>, which is false while the count is above zero, so
/// forgetting to <see cref="Reset"/> a handle does not corrupt anything — it keeps a
/// context alive too long, which is a visible fault rather than a crash.
/// </para>
/// <para>
/// DIVERGENCE, recorded in PORT-COVERAGE: upstream's destructor asserts the handle was
/// cleared, because garbage collection makes RAII unreliable and it wants the leak
/// detected as early as possible. C# has no deterministic destructor to assert in and a
/// finalizer would fire on the collector's thread, so the port carries no such check.
/// The owner's obligation is the same: nullify the handle at the right moment during
/// music translation.
/// </para>
/// </summary>
public sealed class ContextHandle
{
    private Context _context;

    /// <summary>Initializes an empty handle.</summary>
    public ContextHandle()
    {
    }

    /// <summary>Initializes a copy of another handle, taking a reference of its own.</summary>
    /// <param name="source">The handle to copy.</param>
    public ContextHandle(ContextHandle source)
    {
        _context = source?._context;
        MaybeIncrement();
    }

    /// <summary>Gets the context this handle points at, or <see langword="null"/>.</summary>
    public Context Context => _context;

    /// <summary>Gets how many handles point at this handle's context.</summary>
    public int Count => _context?.ClientCount ?? 0;

    /// <summary>Points this handle at a context, releasing whatever it held.</summary>
    /// <param name="context">The context to point at.</param>
    public void Set(Context context)
    {
        if (!ReferenceEquals(_context, context))
        {
            MaybeDecrement();
            _context = context;
            MaybeIncrement();
        }
    }

    /// <summary>Releases the context this handle held.</summary>
    public void Reset()
    {
        MaybeDecrement();
        _context = null;
    }

    /// <summary>Returns the external representation.</summary>
    /// <returns>The handle and the context it holds.</returns>
    public override string ToString()
        => "#<Context_handle " + (_context == null ? "()" : _context.ToString()) + ">";

    private void MaybeIncrement()
    {
        if (_context != null)
        {
            _context.ClientCount++;
        }
    }

    private void MaybeDecrement()
    {
        if (_context != null)
        {
            _context.ClientCount--;
        }
    }
}
