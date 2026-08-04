// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace CodeBrix.LilyPort.Parsing.Actions;

/// <content>
/// The members RULE ACTION GROUP 16 needs: one more piece of <c>Lily_parser</c> state
/// (the remembered tremolo type, beside RAG2's <c>DefaultDuration</c>) and the two
/// <c>Lily::</c> imports the duration multipliers consult.
/// </content>
public partial interface IParserHost
{
    /// <summary>
    /// Gets or sets the tremolo type a bare <c>:</c> repeats — written once as
    /// <c>c4:16</c> and then remembered, so later bare <c>:</c> marks mean the same
    /// subdivision.
    /// <para>Upstream: <c>Lily_parser::default_tremolo_type_</c>, the sibling of
    /// <c>default_duration_</c> and behind the seam for the same reason.</para>
    /// </summary>
    int DefaultTremoloType { get; set; }

    /// <summary>
    /// Answers whether a value can scale a duration: a non-negative exact rational, a
    /// <c>(numerator . denominator)</c> fraction, or a moment whose main part is one.
    /// <para>Upstream: <c>Lily::scale_p</c> — <c>scale?</c> in the vendored
    /// <c>scm/c++.scm</c>, so only the host can answer it, exactly like RAG7's
    /// <see cref="IsKeyList"/>.</para>
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> when the value is a scale.</returns>
    bool IsScale(object value);

    /// <summary>
    /// Reduces a scale to the plain factor it stands for: a fraction pair divides, a
    /// rational is itself, a moment gives up its main part.
    /// <para>Upstream: <c>Lily::scale_to_factor</c> — <c>scale-&gt;factor</c> in the
    /// vendored <c>scm/c++.scm</c>. Its own comment says it "assumes valid input", so
    /// every call site tests with <see cref="IsScale"/> first, as upstream does.</para>
    /// </summary>
    /// <param name="value">The scale.</param>
    /// <returns>The factor.</returns>
    object ScaleToFactor(object value);
}
