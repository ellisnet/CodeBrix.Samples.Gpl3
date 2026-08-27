// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Highlighting;
using Fresco.Brix.Ly.Colorizing;
using System;
using System.Collections.Concurrent;
using Windows.UI.Text;
using FontWeights = Microsoft.UI.Text.FontWeights;
using Token = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.Editor;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// Colours the editor's tokens from a Fonts &amp; Colors scheme.
/// <para>
/// Which style a token gets is python-ly's decision
/// (<see cref="Colorize.CssMapper"/>, parity-verified against python-ly
/// itself); what that style LOOKS like is the scheme's decision
/// (<see cref="TextFormatData"/>, which merges the user's overrides over
/// python-ly's defaults). This class only joins the two and caches the answer
/// per token class.
/// </para>
/// </summary>
public sealed class SchemeTokenStyler : ITokenStyler
{
    private readonly TokenMapper<CssClass> _mapper = Colorize.CssMapper();
    private readonly ConcurrentDictionary<Type, HighlightingColor> _cache
        = new ConcurrentDictionary<Type, HighlightingColor>();
    private TextFormatData _scheme;

    /// <summary>Creates the styler.</summary>
    /// <param name="scheme">The scheme, or null for python-ly's defaults.</param>
    public SchemeTokenStyler(TextFormatData scheme = null)
        => _scheme = scheme ?? new TextFormatData();

    /// <summary>Gets or sets the scheme; setting it re-colours everything.</summary>
    public TextFormatData Scheme
    {
        get => _scheme;
        set
        {
            _scheme = value ?? new TextFormatData();
            _cache.Clear();
        }
    }

    /// <inheritdoc/>
    public HighlightingColor ColorFor(Token token)
        => _cache.GetOrAdd(token.GetType(), type => Resolve(type));

    private HighlightingColor Resolve(Type tokenClass)
    {
        CssClass style = _mapper.ValueForClass(tokenClass);
        if (style == null) { return null; }

        TextFormat format = _scheme.TextFormatFor(style);
        if (format.IsEmpty) { return null; }

        HighlightingColor color = new HighlightingColor();
        if (format.Foreground != null)
        {
            color.Foreground = new SimpleHighlightingBrush(format.Foreground.Value);
        }

        if (format.Background != null)
        {
            color.Background = new SimpleHighlightingBrush(format.Background.Value);
        }

        if (format.IsBold != null)
        {
            color.FontWeight = format.IsBold.Value
                ? FontWeights.Bold
                : FontWeights.Normal;
        }

        if (format.IsItalic != null)
        {
            color.FontStyle = format.IsItalic.Value
                ? FontStyle.Italic
                : FontStyle.Normal;
        }

        if (format.IsUnderlined != null)
        {
            color.Underline = format.IsUnderlined.Value;
        }

        return color;
    }
}
