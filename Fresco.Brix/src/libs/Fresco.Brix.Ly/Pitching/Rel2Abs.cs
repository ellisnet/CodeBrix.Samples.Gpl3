// === python-ly ly.pitch.rel2abs module ===
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
using LilyPondMode = Fresco.Brix.Ly.Lex.LilyPondMode;
using Token = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.Ly.Pitching; //was previously: ly/pitch/rel2abs.py;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Converts relative music to absolute music.</summary>
public static class Rel2Abs
{
    /// <summary>
    /// Rewrites the pitches in the cursor's range from relative to absolute,
    /// removing the <c>\relative</c> commands.
    /// </summary>
    /// <param name="cursor">The range to convert.</param>
    /// <param name="language">The pitch-name language to start reading in.</param>
    /// <param name="firstPitchAbsolute">Whether the first pitch of a
    /// <c>\relative</c> expression without a start pitch counts as absolute
    /// (LilyPond 2.18 and later, which assume f); otherwise the start pitch is
    /// c' (earlier LilyPond).</param>
    public static void Convert(
        Cursor cursor, string language = "nederlands", bool firstPitchAbsolute = false)
        => new Rel2AbsRun(cursor, language, firstPitchAbsolute).Run();

    /// <summary>
    /// The conversion walk; a class for the same reason upstream uses one — a
    /// generator function cannot be called again while its body is running.
    /// </summary>
    private sealed class Rel2AbsRun
    {
        private readonly Cursor _cursor;
        private readonly bool _firstPitchAbsolute;
        private readonly Source _source;
        private readonly PitchIterator _pitches;
        private readonly PitchStream _stream;

        internal Rel2AbsRun(Cursor cursor, string language, bool firstPitchAbsolute)
        {
            _cursor = cursor;
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
                foreach (object _ in Iterate())
                {
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
                Relative(command);
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

        private static IEnumerable<Pitch> GetPitches(IEnumerable<object> items)
        {
            foreach (object item in items)
            {
                if (item is Pitch pitch) { yield return pitch; }
            }
        }

        /// <summary>Makes a pitch absolute, honouring and dropping an octave
        /// check.</summary>
        private void MakeAbsolute(Pitch pitch, Pitch lastPitch)
        {
            if (pitch.Octavecheck != null)
            {
                pitch.Octave = pitch.Octavecheck.Value;
                pitch.Octavecheck = null;
            }
            else
            {
                pitch.MakeAbsolute(lastPitch);
            }

            _pitches.Write(pitch);
        }

        private void Relative(Token command)
        {
            int pos = command.Pos;
            Pitch last;

            object t = NextItem();
            if (t is Pitch startPitch)
            {
                last = startPitch;
                t = NextItem();
            }
            else if (_firstPitchAbsolute)
            {
                last = Pitch.F0();
            }
            else
            {
                last = Pitch.C1();
            }

            //Remove the \relative <pitch> tokens.
            _cursor.Document.Delete(pos, PositionOf(t));

            while (true)
            {
                //Eat stuff like \new Staff == "bla" \new Voice \notes etc.
                if (_source.State.CurrentParser() is LilyPondMode.ParseTranslator)
                {
                    t = Consume();
                }
                else if (t is LilyPondMode.ChordMode || t is LilyPondMode.NoteMode)
                {
                    t = NextItem();
                }
                else
                {
                    break;
                }
            }

            if (t is Token bracket && (bracket.Text == "{" || bracket.Text == "<<"))
            {
                //A full music expression { … } or << … >>
                foreach (object item in Context())
                {
                    if (item is LilyPondMode.PitchCommand pitchCommand)
                    {
                        //Skip commands whose pitches do not count.
                        if (pitchCommand.Text == "\\octaveCheck")
                        {
                            int checkPos = pitchCommand.Pos;
                            foreach (Pitch p in GetPitches(Context()))
                            {
                                //Remove the \octaveCheck.
                                last = p;
                                Token endToken =
                                    p.AccidentalToken ?? p.OctaveToken ?? p.NoteToken;
                                _cursor.Document.Delete(checkPos, endToken.End);
                                break;
                            }
                        }
                        else
                        {
                            Consume();
                        }
                    }
                    else if (item is LilyPondMode.ChordStart)
                    {
                        var chord = new List<Pitch> { last };
                        foreach (Pitch p in GetPitches(Context()))
                        {
                            MakeAbsolute(p, chord[chord.Count - 1]);
                            chord.Add(p);
                        }

                        //Upstream's chord[:2][-1]: the same pitch or the first.
                        last = chord.Count >= 2 ? chord[1] : chord[0];
                    }
                    else if (item is Pitch pitch)
                    {
                        MakeAbsolute(pitch, last);
                        last = pitch;
                    }
                }
            }
            else if (t is LilyPondMode.ChordStart)
            {
                //Just one chord.
                foreach (Pitch p in GetPitches(Context()))
                {
                    MakeAbsolute(p, last);
                    last = p;
                }
            }
            else if (t is Pitch only)
            {
                //Just one pitch.
                MakeAbsolute(only, last);
            }
        }

        /// <summary>
        /// The document position of an item. Upstream reads <c>t.pos</c>, which
        /// only a token has — a pitch there would raise; the note token's
        /// position is the equivalent reading.
        /// </summary>
        private static int PositionOf(object item)
            => item switch
            {
                Token token => token.Pos,
                Pitch pitch => pitch.NoteToken.Pos,
                _ => 0,
            };
    }
}
