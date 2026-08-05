/*
  This file is part of LilyPond, the GNU music typesetter.

  Copyright (C) 2004--2026 Han-Wen Nienhuys <hanwen@xs4all.nl>

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

using System;
using System.Collections.Generic;
using CodeBrix.LilyPort.Engine.Objects;
using CodeBrix.LilyPort.Engine.Origins;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Music; //was previously: lily/music-function.cc, lily/include/music-function.hh;

// Modified by Jeremy Ellis on 2026-08-04 as part of the CodeBrix port.

/// <summary>
/// A LilyPond music function: a Scheme procedure plus the call signature that says what
/// its arguments must satisfy.
/// <para>
/// Nearly all of LilyPond's user-facing commands are these — <c>\relative</c>,
/// <c>\transpose</c>, <c>\tuplet</c> and the rest of <c>ly/music-functions-init.ly</c>.
/// The signature's car is the RETURN type predicate and its cdr is the argument
/// predicates, each either a plain predicate or a (predicate . default) pair marking an
/// optional argument.
/// </para>
/// </summary>
public sealed class MusicFunction : IApplicable
{
    /// <summary>Initializes a music function.</summary>
    /// <param name="signature">The call signature: return predicate, then argument predicates.</param>
    /// <param name="function">The Scheme procedure to call.</param>
    public MusicFunction(object signature, object function)
    {
        Signature = signature;
        Function = function;
    }

    /// <summary>Gets the call signature.</summary>
    public object Signature { get; }

    /// <summary>Gets the Scheme procedure.</summary>
    public object Function { get; }

    /// <summary>
    /// Calls the function from operator position, which a music function supports because
    /// upstream's smob does.
    /// <para>
    /// <c>music-function.hh</c> declares
    /// <c>LY_DECLARE_SMOB_PROC (&amp;Music_function::call, 0, 0, 1)</c> — an apply hook
    /// taking a single rest argument — and the parser leans on it: the syntax
    /// constructors <c>property-set</c>, <c>property-unset</c> and their neighbours are
    /// built by <c>define-syntax-function</c>, so they ARE music functions, and every
    /// <c>MAKE_SYNTAX (property_set, ...)</c> applies one directly. Without the hook the
    /// first <c>\set</c> in the init layer fails with "wrong type to apply".
    /// </para>
    /// </summary>
    /// <param name="arguments">The evaluated arguments.</param>
    /// <returns>The function's result.</returns>
    public object Apply(object[] arguments) => Call(Pair.List(arguments ?? new object[0]));

    /// <summary>
    /// Calls the function, matching the actual arguments against the signature.
    /// <para>
    /// The matching is NOT Scheme's usual optional-argument handling, and the difference
    /// is deliberate: when an optional argument's predicate rejects the next actual
    /// argument, that argument is not consumed — the default is substituted instead, and
    /// every FOLLOWING optional argument is unconditionally defaulted too. That is what
    /// lets <c>\key</c> and friends be written with trailing optional arguments that the
    /// user may simply omit, and what <c>\default</c> triggers explicitly.
    /// </para>
    /// <para>
    /// Translated from upstream step for step. The loop shape, the do/while over
    /// consecutive optionals, and the two separate "trailing optionals" passes are load
    /// bearing — a tidier rewrite changes which arguments bind to what.
    /// </para>
    /// </summary>
    /// <param name="arguments">The actual arguments, as a Scheme list.</param>
    /// <returns>The function's result, or the signature's fallback value on a type error.</returns>
    public object Call(object arguments)
    {
        object location = MusicFunctionSupport.CurrentLocation();

        // (car signature) is the RETURN type; the arguments start after it.
        object signature = Cdr(Signature);
        object rest = arguments;
        object args = Nil.Instance;

        while (rest is Pair restPair && signature is Pair signaturePair)
        {
            object arg = restPair.Car;
            object predicate = signaturePair.Car;

            if (!(predicate is Pair optional))
            {
                // A non-optional argument. A rejection is an error, and the call returns
                // the signature's surrogate value rather than running the function.
                if (!SchemeUtilities.IsSchemeTrue(
                        MusicFunctionSupport.CallPredicate(predicate, arg)))
                {
                    MusicFunctionSupport.ArgumentError(Pair.Length(args) + 1, predicate, arg);

                    object returnSpec = Car(Signature);
                    object fallback = returnSpec is Pair returnPair ? returnPair.Cdr : (object)false;
                    return WithLocation(fallback, location);
                }
            }
            else if (!SchemeUtilities.IsSchemeTrue(
                         MusicFunctionSupport.CallPredicate(optional.Car, arg)))
            {
                // An optional argument whose predicate did not match. The actual argument
                // is NOT consumed unless it was an explicit \default.
                if (ReferenceEquals(arg, Unspecified.Instance))
                {
                    rest = restPair.Cdr;
                }

                // Substitute this default and every following optional's default.
                do
                {
                    args = new Pair(WithLocation(optional.Cdr, location), args);
                    signature = Cdr(signature);
                    if (!(signature is Pair next))
                    {
                        break;
                    }

                    predicate = next.Car;
                    optional = predicate as Pair;
                }
                while (optional != null);

                continue;
            }

            // Accepted: consume both the argument and the signature entry.
            signature = Cdr(signature);
            args = new Pair(arg, args);
            rest = restPair.Cdr;
        }

        // Trailing optional arguments need no *unspecified* stand-in for \default, because
        // the end of the argument list is recognisable on its own. That is what lets (key)
        // in Scheme mean what \key \default means in LilyPond.
        for (; signature is Pair trailing; signature = Cdr(signature))
        {
            if (!(trailing.Car is Pair trailingOptional))
            {
                break;
            }

            args = new Pair(WithLocation(trailingOptional.Cdr, location), args);
        }

        if (rest is Pair || signature is Pair)
        {
            throw new ArgumentException(
                "wrong number of arguments to music function", nameof(arguments));
        }

        List<object> ordered = Pair.ToList(args);
        ordered.Reverse();
        object result = SchemeUtilities.CallCallback(Function, ordered.ToArray());

        object returnPredicate = Car(Signature);
        if (returnPredicate is Pair returnPredicatePair)
        {
            returnPredicate = returnPredicatePair.Car;
        }

        if (SchemeUtilities.IsSchemeTrue(
                MusicFunctionSupport.CallPredicate(returnPredicate, result)))
        {
            // NOT cloned: the result is freshly made by the function and has no other owner.
            return WithLocation(result, location, false);
        }

        return MusicFunctionSupport.MusicFunctionCallError(this, result);
    }

    /// <summary>
    /// Attaches a location to a music expression on its way in or out of a call.
    /// <para>
    /// Values taken FROM the signature are cloned, because a default in a signature is
    /// shared by every call that uses it and music expressions are mutable — without the
    /// clone, one call's origin would be stamped onto every later call's default.
    /// </para>
    /// </summary>
    /// <param name="value">The value to stamp.</param>
    /// <param name="location">The location to stamp it with.</param>
    /// <param name="clone">Whether to clone before stamping.</param>
    /// <returns>The value, cloned and stamped when it is music.</returns>
    private static object WithLocation(object value, object location, bool clone = true)
    {
        if (!(value is MusicObject music))
        {
            return value;
        }

        if (clone)
        {
            music = music.Clone();
        }

        if (location is Input input)
        {
            music.SetSpot(input);
        }

        return music;
    }

    private static object Car(object value) => value is Pair pair ? pair.Car : Nil.Instance;

    private static object Cdr(object value) => value is Pair pair ? pair.Cdr : Nil.Instance;

    /// <summary>Returns the external representation.</summary>
    /// <returns>A description naming the wrapped procedure.</returns>
    public override string ToString() => "#<Music function " + Function + ">";
}
