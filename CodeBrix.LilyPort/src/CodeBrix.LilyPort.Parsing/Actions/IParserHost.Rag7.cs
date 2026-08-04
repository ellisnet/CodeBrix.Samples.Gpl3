// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Parsing.Driver;

namespace CodeBrix.LilyPort.Parsing.Actions;

/// <content>
/// The members RULE ACTION GROUP 7 (symbol lists, property paths and overrides)
/// added to the seam.
/// </content>
public partial interface IParserHost
{
    /// <summary>
    /// Answers whether a symbol names a grob — which is what decides whether a bare
    /// property path gets the implicit <c>Bottom</c> context in <c>\override</c> and
    /// <c>\revert</c>.
    /// <para>Upstream: <c>scm_object_property (x, ly_symbol2scm ("is-grob?"))</c>
    /// read as a boolean. The property is Scheme-side DATA: every grob name symbol
    /// receives it via <c>set-object-property!</c> in <c>scm/define-grobs.scm</c>, so
    /// the answer lives on the host, not in the parser.</para>
    /// </summary>
    /// <param name="value">The value to test; only symbols can answer yes.</param>
    /// <returns><see langword="true"/> when the symbol names a grob.</returns>
    bool IsGrobSymbol(object value);

    /// <summary>
    /// Answers whether a value is a key list — a proper list whose every element is
    /// a key in the sense of <see cref="IsKey"/>.
    /// <para>Upstream: <c>Lily::key_list_p</c>, defined in <c>scm/c++.scm</c> as
    /// <c>(and (list? x) (every key? x))</c> — the sibling of <c>Lily::key_p</c>,
    /// which is already here as <see cref="IsKey"/>.</para>
    /// </summary>
    /// <param name="value">The value to test.</param>
    /// <returns><see langword="true"/> for a key list (the empty list included).</returns>
    bool IsKeyList(object value);

    /// <summary>
    /// Reports a warning at a location — a diagnostic that does NOT raise the error
    /// level, unlike <c>parser_error</c>.
    /// <para>Upstream: <c>Input::warning</c>, as <c>property_path_dot_warning</c> in
    /// <c>parser.yy</c>'s epilogue calls it for a property path written without its
    /// separating dots.</para>
    /// </summary>
    /// <param name="location">Where the warning points.</param>
    /// <param name="message">The message.</param>
    void Warning(SourceSpan location, string message);
}
