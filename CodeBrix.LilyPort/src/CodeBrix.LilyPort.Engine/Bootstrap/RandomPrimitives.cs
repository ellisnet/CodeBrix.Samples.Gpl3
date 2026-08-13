// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Diagnostics;
using System.Globalization;
using CodeBrix.LilyScheme;
using CodeBrix.LilyScheme.Primitives;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap;

/// <summary>
/// <c>lily/lily-random.cc</c>: the seed the run's pseudo-random generators start from,
/// and the randomly-suffixed temporary file name.
/// <para>
/// ⚠ NOT a long-tail leaf. <c>scm/lily.scm</c>'s <c>randomize-rand-seed</c> calls
/// <c>ly:make-rand-seed</c> and <c>ly:set-rand-seed</c> on EVERY run, before anything is
/// engraved, so while these were stubs the first returned the inert placeholder and the
/// second silently discarded it.
/// </para>
/// </summary>
public static class RandomPrimitives
{
    private const int HexCharCount = 6;

    // Upstream: constexpr unsigned max_file_id = 2 << (4 * hex_char_count - 1), which is
    // 2 << 23 == 0x1000000. Written the same way rather than as the literal, because the
    // shift is one MORE than the hex digits can hold and reading it as 0xFFFFFF would be
    // an off-by-one that only shows up on one draw in sixteen million.
    private const uint MaxFileId = 2u << ((4 * HexCharCount) - 1);

    private static readonly MersenneTwister Generator = new MersenneTwister();

    /// <summary>Installs the random primitives, replacing the corresponding stubs.</summary>
    /// <param name="interpreter">The interpreter to extend.</param>
    public static void Install(Interpreter interpreter)
    {
        if (interpreter == null)
        {
            throw new ArgumentNullException(nameof(interpreter));
        }

        // Combine a high-resolution clock reading with the process id, exactly as
        // upstream does, and narrow to unsigned.
        interpreter.DefinePrimitive("ly:make-rand-seed", 0, 0, a =>
            (long)unchecked((uint)(Stopwatch.GetTimestamp() ^ Environment.ProcessId)));

        interpreter.DefinePrimitive("ly:set-rand-seed", 1, 1, a =>
        {
            Generator.Seed(unchecked((uint)SchemeConvert.ToLong(a[0], "ly:set-rand-seed")));
            return Unspecified.Instance;
        });

        interpreter.DefinePrimitive("ly:make-tmpfile-name", 1, 1, a =>
        {
            if (!(a[0] is MutableString) && !(a[0] is string))
            {
                throw SchemeErrors.WrongType("ly:make-tmpfile-name", "string", a[0]);
            }

            uint fileId = Generator.NextBelowOrEqual(MaxFileId);

            // Upstream's format is "%s-%0*x" with a width of six. The width is a MINIMUM,
            // not a truncation: the one drawable value above 0xFFFFFF prints seven
            // digits, in C and here alike.
            return new MutableString(
                StringPrimitives.Text(a[0], "ly:make-tmpfile-name")
                + "-"
                + fileId.ToString("x" + HexCharCount.ToString(CultureInfo.InvariantCulture),
                    CultureInfo.InvariantCulture));
        });
    }
}

/// <summary>
/// The Mersenne Twister MT19937, the generator <c>std::mt19937</c> names.
/// <para>
/// Carried because <c>lily-random.cc</c> holds a <c>std::mt19937</c> whose state
/// <c>ly:set-rand-seed</c> sets and <c>ly:make-tmpfile-name</c> draws from, and .NET has
/// no MT19937. The algorithm itself is fully specified (Matsumoto and Nishimura, 1998),
/// so this half is exact.
/// </para>
/// <para>
/// ⚠ What is NOT exact, and is recorded in PORT-COVERAGE as a deliberate divergence:
/// <c>std::uniform_int_distribution</c>'s reduction from the generator's output to a
/// bounded range is IMPLEMENTATION-DEFINED, so an identical seed yields an identical
/// generator sequence but not necessarily the identical bounded draw. The only consumer
/// is the hex suffix of a temporary file name — a value that is random by design, never
/// reaches engraved output, and is compared by nothing.
/// </para>
/// </summary>
internal sealed class MersenneTwister
{
    private const int N = 624;
    private const int M = 397;
    private const uint MatrixA = 0x9908B0DFu;
    private const uint UpperMask = 0x80000000u;
    private const uint LowerMask = 0x7FFFFFFFu;

    // std::mt19937's default_seed. A default-constructed std::mt19937 starts here, and
    // upstream default-constructs its generator, so a run that never calls
    // ly:set-rand-seed draws from this state.
    private const uint DefaultSeed = 5489u;

    private readonly uint[] _state = new uint[N];
    private int _index = N + 1;

    /// <summary>Initializes the generator at <c>std::mt19937</c>'s default seed.</summary>
    internal MersenneTwister() => Seed(DefaultSeed);

    /// <summary>Reseeds the generator, discarding any existing state.</summary>
    /// <param name="seed">The seed value.</param>
    internal void Seed(uint seed)
    {
        _state[0] = seed;
        for (uint i = 1; i < N; i++)
        {
            _state[i] = unchecked((1812433253u * (_state[i - 1] ^ (_state[i - 1] >> 30))) + i);
        }

        _index = N;
    }

    /// <summary>Returns the next 32-bit output.</summary>
    /// <returns>The next value in the sequence.</returns>
    internal uint Next()
    {
        if (_index >= N)
        {
            Twist();
        }

        uint y = _state[_index++];

        // The tempering transform.
        y ^= y >> 11;
        y ^= (y << 7) & 0x9D2C5680u;
        y ^= (y << 15) & 0xEFC60000u;
        y ^= y >> 18;
        return y;
    }

    /// <summary>
    /// Returns a value in <c>[0, bound]</c> — INCLUSIVE at both ends, matching
    /// <c>std::uniform_int_distribution</c>'s closed interval.
    /// </summary>
    /// <param name="bound">The largest value that may be returned.</param>
    /// <returns>The draw.</returns>
    /// <remarks>
    /// Rejection sampling on a power-of-two mask: unbiased, and cheap because the caller's
    /// bound is one above a 24-bit boundary. This is a reduction of the SAME quality as
    /// libstdc++'s and not the same reduction — see the type's own remarks.
    /// </remarks>
    internal uint NextBelowOrEqual(uint bound)
    {
        if (bound == uint.MaxValue)
        {
            return Next();
        }

        uint range = bound + 1;
        uint mask = range - 1;
        mask |= mask >> 1;
        mask |= mask >> 2;
        mask |= mask >> 4;
        mask |= mask >> 8;
        mask |= mask >> 16;

        uint draw;
        do
        {
            draw = Next() & mask;
        }
        while (draw >= range);

        return draw;
    }

    private void Twist()
    {
        for (int i = 0; i < N; i++)
        {
            uint y = (_state[i] & UpperMask) | (_state[(i + 1) % N] & LowerMask);
            uint next = _state[(i + M) % N] ^ (y >> 1);
            if ((y & 1u) != 0)
            {
                next ^= MatrixA;
            }

            _state[i] = next;
        }

        _index = 0;
    }
}
