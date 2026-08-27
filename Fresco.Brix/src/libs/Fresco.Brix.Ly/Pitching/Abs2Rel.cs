// === python-ly ly.pitch.abs2rel module ===
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
using System.Globalization;
using LilyPondMode = Fresco.Brix.Ly.Lex.LilyPondMode;
using Token = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.Ly.Pitching; //was previously: ly/pitch/abs2rel.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Converts absolute music to relative music.</summary>
public static class Abs2Rel
{
    /// <summary>
    /// Rewrites the pitches in the cursor's range from absolute to relative,
    /// writing the <c>\relative</c> commands. Existing <c>\relative</c>
    /// expressions are left alone.
    /// </summary>
    /// <param name="cursor">The range to convert.</param>
    /// <param name="language">The pitch-name language to start reading in.</param>
    /// <param name="startPitch">Whether to write a starting pitch before the
    /// opening bracket of a relative expression.</param>
    /// <param name="firstPitchAbsolute">Only meaningful when
    /// <paramref name="startPitch"/> is false: whether the first pitch is
    /// written as absolute (LilyPond 2.18 and later, which assume f) instead
    /// of relative to c' (earlier LilyPond).</param>
    public static void Convert(
        Cursor cursor,
        string language = "nederlands",
        bool startPitch = true,
        bool firstPitchAbsolute = false)
        => new Abs2RelRun(cursor, language, startPitch, firstPitchAbsolute).Run();

    /// <summary>
    /// The conversion walk; a class for the same reason upstream uses one — a
    /// generator function cannot be called again while its body is running.
    /// </summary>
    private sealed class Abs2RelRun
    {
        private readonly Cursor _cursor;
        private readonly bool _startPitch;
        private readonly bool _firstPitchAbsolute;
        private readonly Source _source;
        private readonly PitchIterator _pitches;
        private readonly PitchStream _stream;

        internal Abs2RelRun(
            Cursor cursor, string language, bool startPitch, bool firstPitchAbsolute)
        {
            _cursor = cursor;
            _startPitch = startPitch;
            _firstPitchAbsolute = firstPitchAbsolute;

            int start = cursor.Start;
            cursor.Start = 0;

            _source = new Source(cursor, stateFromDocument: true, tokensWithPosition: true);
            _pitches = new PitchIterator(_source, language);
            _stream = new PitchStream(_pitches.Pitches());

            if (start > 0)
            {
                //Consume the tokens before the selection, following the
                //language, and put back the one that overlaps it.
                Token t = _source.Consume(_pitches.Tokens(), start);
                if (t != null) { _stream.Prepend(t); }
            }
        }

        internal void Run()
        {
            using (_cursor.Document.Writing())
            {
                foreach (object item in Iterate())
                {
                    if (!(item is Token bracket)
                        || (bracket.Text != "{" && bracket.Text != "<<"))
                    {
                        continue;
                    }

                    //Parse this expression.
                    int pos = bracket.Pos; //where to insert the \relative command
                    Pitch lastPitch = null;
                    List<Pitch> chord = null;
                    foreach (object t in Context())
                    {
                        if (t is LilyPondMode.PitchCommand)
                        {
                            //Skip commands whose pitches do not count.
                            Consume();
                        }
                        else if (t is LilyPondMode.ChordStart)
                        {
                            chord = new List<Pitch>();
                        }
                        else if (t is LilyPondMode.ChordEnd)
                        {
                            if (chord != null && chord.Count > 0) { lastPitch = chord[0]; }

                            chord = null;
                        }
                        else if (t is Pitch pitch)
                        {
                            if (lastPitch == null)
                            {
                                if (_startPitch)
                                {
                                    lastPitch = Pitch.C1();
                                    lastPitch.Octave = pitch.Octave;
                                    if (pitch.Note > 3) { lastPitch.Octave += 1; }

                                    _cursor.Document.SetText(
                                        pos,
                                        pos,
                                        string.Format(
                                            CultureInfo.InvariantCulture,
                                            "\\relative {0} ",
                                            lastPitch.Output(_pitches.Language)));
                                }
                                else
                                {
                                    lastPitch = _firstPitchAbsolute
                                        ? Pitch.F0()
                                        : Pitch.C1();
                                    _cursor.Document.SetText(pos, pos, "\\relative ");
                                }
                            }

                            Pitch copy = pitch.Copy();
                            pitch.MakeRelative(lastPitch);
                            _pitches.Write(pitch);
                            lastPitch = copy;

                            //Remember the first pitch of a chord.
                            if (chord != null && chord.Count == 0) { chord.Add(copy); }
                        }
                    }
                }
            }
        }

        /// <summary>Upstream's dispatching <c>gen.__next__</c>.</summary>
        private object NextItem()
        {
            object t = _stream.Next();
            while (t is Lex.Space || t is Lex.Comment) { t = _stream.Next(); }

            if (t is LilyPondMode.Command command && command.Text == "\\relative")
            {
                Relative();
                t = _stream.Next();
            }
            else if (t is LilyPondMode.ChordMode)
            {
                Consume(); //do not change chords
                t = _stream.Next();
            }
            else if (t is LilyPondMode.MarkupScore)
            {
                Consume();
                t = _stream.Next();
            }

            return t;
        }

        private IEnumerable<object> Iterate()
        {
            while (true)
            {
                object t;
                try
                {
                    t = NextItem();
                }
                catch (StopIterationSignal)
                {
                    yield break;
                }

                yield return t;
            }
        }

        /// <summary>Consumes items until the level drops (a construct ends).</summary>
        private IEnumerable<object> Context()
        {
            int depth = _source.State.Depth();
            foreach (object t in Iterate())
            {
                yield return t;
                if (_source.State.Depth() < depth) { yield break; }
            }
        }

        /// <summary>Consumes a context, answering its last item.</summary>
        private object Consume()
        {
            object last = null;
            foreach (object t in Context()) { last = t; }

            return last;
        }

        /// <summary>Consumes a whole <c>\relative</c> expression, unchanged.</summary>
        private void Relative()
        {
            //Skip the pitch argument.
            object t = NextItem();
            if (t is Pitch) { t = NextItem(); }

            while (true)
            {
                //Eat stuff like \new Staff == "bla" \new Voice \notes etc.
                if (_source.State.CurrentParser() is LilyPondMode.ParseTranslator)
                {
                    t = Consume();
                }
                else if (t is LilyPondMode.NoteMode)
                {
                    t = NextItem();
                }
                else
                {
                    break;
                }
            }

            if (t is Token bracket
                && (bracket.Text == "{" || bracket.Text == "<<" || bracket.Text == "<"))
            {
                Consume();
            }
        }
    }
}
