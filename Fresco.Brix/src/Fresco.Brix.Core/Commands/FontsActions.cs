// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;

namespace Fresco.Brix.Commands; //was previously: frescobaldi/fonts/__init__.py (class Actions)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>The one way into the Document Fonts dialog.</summary>
public sealed class FontsActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    /// <remarks>Upstream's own <c>name = "fonts"</c>.</remarks>
    public const string CollectionName = "fonts";

    /// <summary>Creates the collection.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public FontsActions(SettingsStore settings = null)
        : base(CollectionName, settings) => Initialize();

    /// <summary>Gets the command that opens the Document Fonts dialog.</summary>
    public AppAction DocumentFonts { get; private set; }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Document Fonts");

    /// <inheritdoc/>
    protected override void CreateActions()
        => DocumentFonts = Add("fonts_document_fonts")
            .WithIcon("preferences-desktop-font");

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        DocumentFonts.Text = I18n.Get("&Document Fonts...");

        //Upstream: "Show and select text and music fonts available in the
        //LilyPond version of the current document". FR13 forbids naming
        //LilyPond in the chrome, and FR5.1 means there is only ever one
        //engine, so there is no "version of the current document" to speak of
        //either.
        DocumentFonts.ToolTip = I18n.Get(
            "Show and select the text and music fonts available to LilyPort");
    }
}
