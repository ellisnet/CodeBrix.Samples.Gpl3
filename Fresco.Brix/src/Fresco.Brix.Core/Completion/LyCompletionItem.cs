// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.Platform.UI.AdvancedTextEdit.CodeCompletion;
using CodeBrix.Platform.UI.AdvancedTextEdit.Document;
using CodeBrix.Platform.UI.AdvancedTextEdit.Editing;
using Microsoft.UI.Xaml.Media;
using System;

namespace Fresco.Brix.Completion;

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One row of the completion popup, over a <see cref="CompletionEntry"/>.
/// </summary>
/// <remarks>
/// The editor filters on <see cref="Text"/> and inserts it; upstream's
/// completer does the same, because Qt's completion role is the EDIT role.
/// The row SHOWS <see cref="Content"/>, which is how <c>title</c> can be
/// listed while <c>title = </c> is what gets typed.
/// </remarks>
public sealed class LyCompletionItem : ICompletionData
{
    /// <summary>Creates a row.</summary>
    /// <param name="entry">The entry.</param>
    public LyCompletionItem(CompletionEntry entry) => Entry = entry;

    /// <summary>Gets the entry.</summary>
    public CompletionEntry Entry { get; }

    /// <inheritdoc/>
    public ImageSource Image => null;

    /// <inheritdoc/>
    public string Text => Entry.Insert;

    /// <inheritdoc/>
    public object Content => Entry.Display;

    /// <inheritdoc/>
    public object Description => null;

    /// <inheritdoc/>
    public double Priority => 0;

    /// <inheritdoc/>
    public void Complete(
        TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        => textArea.Document.Replace(completionSegment, Text);
}
