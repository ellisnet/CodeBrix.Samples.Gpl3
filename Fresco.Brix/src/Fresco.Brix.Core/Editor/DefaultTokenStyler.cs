// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.Highlighting;
using Fresco.Brix.Ly.Lex;
using System;
using System.Collections.Concurrent;
using Windows.UI;
using Windows.UI.Text;
using FontWeights = Microsoft.UI.Text.FontWeights;
using LilyPondMode = Fresco.Brix.Ly.Lex.LilyPondMode;
using SchemeMode = Fresco.Brix.Ly.Lex.SchemeMode;
using Token = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.Editor;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// The built-in token colors: python-ly's colorize base scheme (keyword bold;
/// function bold blue; variable blue; value olive; string dark red; escape
/// teal; comment gray italic) plus the LilyPond-specific styles Frescobaldi's
/// default scheme draws distinctly.
/// <para>
/// PROVISIONAL for first light: the full textformats port (user-editable
/// schemes, the Fonts &amp; Colors preference page's model) replaces the
/// hard-wired table later in W2/W12; the seam this sits behind
/// (<see cref="ITokenStyler"/>) is the part that stays.
/// </para>
/// </summary>
public sealed class DefaultTokenStyler : ITokenStyler
{
    private readonly ConcurrentDictionary<Type, HighlightingColor> _cache
        = new ConcurrentDictionary<Type, HighlightingColor>();

    private static readonly HighlightingColor KeywordColor = Make(null, bold: true);
    private static readonly HighlightingColor FunctionColor = Make(Color.FromArgb(255, 0x00, 0x00, 0xc0), bold: true);
    private static readonly HighlightingColor VariableColor = Make(Color.FromArgb(255, 0x00, 0x00, 0xff));
    private static readonly HighlightingColor ValueColor = Make(Color.FromArgb(255, 0x80, 0x80, 0x00));
    private static readonly HighlightingColor StringColor = Make(Color.FromArgb(255, 0xc0, 0x00, 0x00));
    private static readonly HighlightingColor EscapeColor = Make(Color.FromArgb(255, 0x00, 0x80, 0x80));
    private static readonly HighlightingColor CommentColor = Make(Color.FromArgb(255, 0x80, 0x80, 0x80), italic: true);
    private static readonly HighlightingColor PitchColor = Make(Color.FromArgb(255, 0x00, 0x60, 0x60));
    private static readonly HighlightingColor DynamicColor = Make(Color.FromArgb(255, 0x80, 0x00, 0x00), bold: true);
    private static readonly HighlightingColor ErrorColor = Make(Color.FromArgb(255, 0xff, 0x00, 0x00), bold: true);

    /// <inheritdoc/>
    public HighlightingColor ColorFor(Token token)
        => _cache.GetOrAdd(token.GetType(), _ => Resolve(token));

    private static HighlightingColor Resolve(Token token) => token switch
    {
        ErrorBase => ErrorColor,
        Fresco.Brix.Ly.Lex.Comment => CommentColor,
        StringBase or Character => StringColor,
        LilyPondMode.Dynamic => DynamicColor,
        LilyPondMode.MusicItem => PitchColor,
        LilyPondMode.Keyword => KeywordColor,
        LilyPondMode.Command or LilyPondMode.Markup => FunctionColor,
        LilyPondMode.Specifier or LilyPondMode.UserCommand
            or LilyPondMode.Variable => VariableColor,
        SchemeMode.Keyword => KeywordColor,
        SchemeMode.Function => FunctionColor,
        SchemeMode.Variable or SchemeMode.Constant => VariableColor,
        Numeric => ValueColor,
        _ => null,
    };

    private static HighlightingColor Make(
        Color? foreground, bool bold = false, bool italic = false)
    {
        HighlightingColor color = new HighlightingColor();
        if (foreground != null)
        {
            color.Foreground = new SimpleHighlightingBrush(foreground.Value);
        }

        if (bold)
        {
            color.FontWeight = FontWeights.Bold;
        }

        if (italic)
        {
            color.FontStyle = FontStyle.Italic;
        }

        return color;
    }
}
