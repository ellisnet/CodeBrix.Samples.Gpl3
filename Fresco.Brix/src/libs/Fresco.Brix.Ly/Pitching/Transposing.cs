// === python-ly ly.pitch.transpose module (the transpose function) ===
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

namespace Fresco.Brix.Ly.Pitching; //was previously: ly/pitch/transpose.py (function transpose);

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>Transposes the music in a document range.</summary>
public static class Transposing
{
    /// <summary>
    /// Transposes the pitches in the cursor's range with a transposer.
    /// </summary>
    /// <param name="cursor">The range to transpose.</param>
    /// <param name="transposer">The transposer to apply.</param>
    /// <param name="language">The pitch-name language to start reading in.</param>
    /// <param name="relativeFirstPitchAbsolute">Whether the first pitch of a
    /// <c>\relative</c> expression without a start pitch counts as absolute —
    /// LilyPond 2.18 and later behave that way; earlier versions read it
    /// relative to c', which is still the default here.</param>
    public static void Transpose(
        Cursor cursor,
        TransposerBase transposer,
        string language = "nederlands",
        bool relativeFirstPitchAbsolute = false)
        => new TransposeRun(cursor, transposer, language, relativeFirstPitchAbsolute).Run();

    /// <summary>
    /// The transpose walk. Upstream keeps this state in the closure of the
    /// <c>transpose()</c> function and dispatches through a small class,
    /// because a generator function "doesn't like to be called again while
    /// there is already a body running" — the same reason this is a class.
    /// </summary>
    private sealed class TransposeRun
    {
        private readonly Cursor _cursor;
        private readonly TransposerBase _transposer;
        private readonly bool _relativeFirstPitchAbsolute;
        private readonly int _start;
        private readonly Source _source;
        private readonly PitchIterator _pitches;
        private readonly PitchStream _stream;

        internal TransposeRun(
            Cursor cursor,
            TransposerBase transposer,
            string language,
            bool relativeFirstPitchAbsolute)
        {
            _cursor = cursor;
            _transposer = transposer;
            _relativeFirstPitchAbsolute = relativeFirstPitchAbsolute;

            _start = cursor.Start;
            cursor.Start = 0;

            _source = new Source(cursor, stateFromDocument: true, tokensWithPosition: true);
            _pitches = new PitchIterator(_source, language);
            _stream = new PitchStream(_pitches.Pitches());
        }

        internal void Run()
        {
            using (_cursor.Document.Writing())
            {
                Absolute(Iterate());
            }
        }

        /// <summary>Upstream's dispatching <c>gen.__next__</c>.</summary>
        private object NextItem()
        {
            while (true)
            {
                object t = _stream.Next();
                if (t is Lex.Space || t is Lex.Comment) { continue; }

                //Everything that behaves the same relative and absolute.
                if (t is Token relative && relative.Text == "\\relative")
                {
                    Relative();
                }
                else if (t is LilyPondMode.MarkupScore)
                {
                    Absolute(Context());
                }
                else if (t is LilyPondMode.ChordMode)
                {
                    ChordMode();
                }
                else if (t is LilyPondMode.Command command && command.Text == "\\stringTuning")
                {
                    StringTuning();
                }
                else if (t is LilyPondMode.PitchCommand pitchCommand)
                {
                    switch (pitchCommand.Text)
                    {
                        case "\\transposition":
                            _stream.Next(); //skip the pitch
                            break;
                        case "\\transpose":
                            foreach (Pitch p in GetPitches(Context())) { TransposePitch(p); }

                            break;
                        case "\\key":
                            foreach (Pitch p in GetPitches(Context())) { TransposePitch(p, 0); }

                            break;
                        default:
                            return t;
                    }
                }
                else
                {
                    return t;
                }
            }
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

        /// <summary>Whether the pitch may be replaced, i.e. was selected.</summary>
        private bool InSelection(Pitch pitch)
            => _start == 0 || _pitches.Position(pitch) >= _start;

        /// <summary>Transposes an absolute pitch, forcing an octave if given.</summary>
        private void TransposePitch(Pitch pitch, int? resetOctave = null)
        {
            _transposer.Transpose(pitch);
            if (resetOctave != null) { pitch.Octave = resetOctave.Value; }

            if (InSelection(pitch)) { _pitches.Write(pitch); }
        }

        /// <summary>Called inside <c>\chordmode</c> or <c>\chords</c>.</summary>
        private void ChordMode()
        {
            foreach (Pitch p in GetPitches(Context())) { TransposePitch(p, 0); }
        }

        /// <summary>Called after <c>\stringTuning</c>; the chord that follows
        /// is left alone.</summary>
        private void StringTuning()
        {
            foreach (object t in Iterate())
            {
                if (t is LilyPondMode.ChordStart) { Consume(); }

                break;
            }
        }

        /// <summary>Called when outside a possible <c>\relative</c>.</summary>
        private void Absolute(IEnumerable<object> items)
        {
            foreach (Pitch p in GetPitches(items)) { TransposePitch(p); }
        }

        /// <summary>Called when <c>\relative</c> is encountered.</summary>
        private void Relative()
        {
            //A list so the local function below can clear it.
            var relPitch = new List<Pitch>();

            Pitch TransposeRelative(Pitch p, Pitch lastPitch)
            {
                //The absolute pitch, from the UNTRANSPOSED last pitch.
                p.MakeAbsolute(lastPitch);
                if (!InSelection(p)) { return p; }

                //This pitch may change; make it relative against the
                //transposed last pitch.
                Pitch last = lastPitch.Transposed ?? lastPitch;

                //Transpose a copy and remember it on the new last pitch, so
                //the next pitch is made relative correctly.
                Pitch newLastPitch = p.Copy();
                _transposer.Transpose(p);
                newLastPitch.Transposed = p.Copy();
                if (p.Octavecheck != null) { p.Octavecheck = p.Octave; }

                p.MakeRelative(last);
                if (relPitch.Count > 0)
                {
                    //The pitch after the \relative command may be changed;
                    //lastPitch is that pitch.
                    lastPitch.Octave += p.Octave;
                    p.Octave = 0;
                    _pitches.Write(lastPitch);
                    relPitch.Clear();
                }

                _pitches.Write(p);
                return newLastPitch;
            }

            Pitch last;
            object t = NextItem();
            if (t is Pitch startPitch)
            {
                last = startPitch;
                if (InSelection(startPitch)) { relPitch.Add(last); }

                t = NextItem();
            }
            else if (_relativeFirstPitchAbsolute)
            {
                last = Pitch.F0();
            }
            else
            {
                last = Pitch.C1();
            }

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

            if (t is Token bracket && (bracket.Text == "{" || bracket.Text == "<<"))
            {
                //A full music expression { … } or << … >>
                foreach (object item in Context())
                {
                    if (item is Token check && check.Text == "\\octaveCheck")
                    {
                        foreach (Pitch p in GetPitches(Context()))
                        {
                            last = p.Copy();
                            relPitch.Clear();
                            if (InSelection(p))
                            {
                                _transposer.Transpose(p);
                                last.Transposed = p;
                                _pitches.Write(p);
                            }
                        }
                    }
                    else if (item is LilyPondMode.ChordStart)
                    {
                        var chord = new List<Pitch> { last };
                        foreach (Pitch p in GetPitches(Context()))
                        {
                            chord.Add(TransposeRelative(p, chord[chord.Count - 1]));
                        }

                        //Upstream's chord[:2][-1]: the same pitch or the first.
                        last = chord.Count >= 2 ? chord[1] : chord[0];
                    }
                    else if (item is Pitch pitch)
                    {
                        last = TransposeRelative(pitch, last);
                    }
                }
            }
            else if (t is LilyPondMode.ChordStart)
            {
                //Just one chord.
                foreach (Pitch p in GetPitches(Context()))
                {
                    last = TransposeRelative(p, last);
                }
            }
            else if (t is Pitch only)
            {
                //Just one pitch.
                TransposeRelative(only, last);
            }
        }
    }
}
