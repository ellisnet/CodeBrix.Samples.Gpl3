// === python-ly ly.pitch.transform module ===
//
// Copyright (c) 2008 - 2015 by Wilbert Berendsen
//
// This program is free software; you can redistribute it and/or
// modify it under the terms of the GNU General Public License
// as published by the Free Software Foundation; either version 3
// of the License, or (at your option) any later version.
//
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program; if not, write to the Free Software
// Foundation, Inc., 51 Franklin St, Fifth Floor, Boston, MA  02110-1301  USA
// See http://www.gnu.org/licenses/ for more information.

using System.Collections.Generic;
using System.Linq;

namespace Fresco.Brix.Ly.Pitching; //was previously: ly/pitch/transform.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Transforms music by manipulating its pitches.</summary>
public static class PitchTransform
{
    /// <summary>Reverses the order of the pitches in the cursor's range.</summary>
    /// <param name="cursor">The range to transform.</param>
    /// <param name="language">The pitch-name language to read in.</param>
    public static void Retrograde(Cursor cursor, string language = "nederlands")
    {
        var source = new Source(cursor, stateFromDocument: true, tokensWithPosition: true);
        var pitches = new PitchIterator(source, language);

        List<Pitch> forward = pitches.Pitches().OfType<Pitch>().ToList();
        List<Pitch> backward = Enumerable.Reverse(forward).Select(p => p.Copy()).ToList();

        using (cursor.Document.Writing())
        {
            for (int i = 0; i < forward.Count; i++)
            {
                Pitch p = forward[i];
                Pitch r = backward[i];
                p.Note = r.Note;
                p.Alter = r.Alter;
                p.Octave = r.Octave;
                pitches.Write(p);
            }
        }
    }

    /// <summary>
    /// Inverts the intervals between the pitches in the cursor's range, around
    /// the first pitch.
    /// </summary>
    /// <param name="cursor">The range to transform.</param>
    /// <param name="language">The pitch-name language to read in.</param>
    public static void Inversion(Cursor cursor, string language = "nederlands")
    {
        var source = new Source(cursor, stateFromDocument: true, tokensWithPosition: true);
        var pitches = new PitchIterator(source, language);

        Pitch previousNote = null;
        Pitch reference = null;

        using (cursor.Document.Writing())
        {
            foreach (object item in pitches.Pitches())
            {
                if (!(item is Pitch p)) { continue; }

                if (previousNote == null)
                {
                    previousNote = p;
                    reference = p;
                    continue;
                }

                var transposer = new Transposer(p, previousNote);
                previousNote = p.Copy();
                p.Note = reference.Note;
                p.Alter = reference.Alter;
                p.Octave = reference.Octave;
                transposer.Transpose(p);
                reference = p;
                pitches.Write(p);
            }
        }
    }
}
