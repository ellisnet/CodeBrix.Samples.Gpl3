// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace CodeBrix.LilyPort.Engine.Origins;

/// <summary>
/// The seam <c>ly:lily-lexer?</c> tests against — the same shape
/// <see cref="ILilyParser"/> gives <c>ly:lily-parser?</c>.
/// <para>
/// Upstream's <c>Lily_lexer</c> declares <c>type_p_name_ = "ly:lily-lexer?"</c>, so the
/// predicate is a smob test over a concrete class. The port's lexer is
/// <c>ModalScanner</c>, which lives in <c>CodeBrix.LilyPort.Parsing</c> — and Parsing
/// references the Engine, not the other way round, so the Engine cannot name the type.
/// A marker interface is how the predicate stays HONEST across that boundary: it
/// answers for the port's real lexer rather than for nothing.
/// </para>
/// <para>
/// The interface is deliberately empty. Nothing in the vendored Scheme layer calls a
/// lexer method — <c>ly:lily-lexer?</c> is reached only through <c>lily.scm</c>'s
/// <c>type-name-alist</c>, which needs the predicate and never the object — so
/// declaring members here would be inventing a surface upstream does not expose to
/// Scheme.
/// </para>
/// </summary>
public interface ILilyLexer
{
}
