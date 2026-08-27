// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Services;

namespace Fresco.Brix.Commands; //was previously: frescobaldi/docbrowser/__init__.py (class Actions)

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>The documentation browser's commands.</summary>
/// <remarks>
/// <para>
/// Upstream's eight, with ruling FR8's consequences. <c>help_back</c> and
/// <c>help_forward</c> survive as the history a reader builds by following
/// contents entries; <c>help_home</c> survives as "the manual's first page";
/// <c>help_web_browser</c> and <c>help_web_browser_homepage</c> become ONE
/// command — open this manual in the desktop's own PDF viewer — because there
/// is no web browser in the picture and no remote page to distinguish from a
/// local one; and <c>help_print</c> does NOT survive, permanently, under ruling
/// FR5.5: the application produces PDFs and the user prints those outside it.
/// </para>
/// <para>
/// ⚠ Names and shortcuts stay upstream's. F9 opens the documentation and
/// Shift+F9 asks about the word at the caret, because that is what a
/// Frescobaldi user's fingers already do.
/// </para>
/// </remarks>
public sealed class DocumentationActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "docbrowser";

    /// <summary>Creates the documentation commands.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public DocumentationActions(SettingsStore settings = null)
        : base(CollectionName, settings)
        => Initialize();

    /// <inheritdoc/>
    public override string Title => I18n.Get("Documentation Browser");

    /// <summary>Go back to the previous place.</summary>
    public AppAction HelpBack { get; private set; }

    /// <summary>Go forward again.</summary>
    public AppAction HelpForward { get; private set; }

    /// <summary>Go to the current manual's first page.</summary>
    public AppAction HelpHome { get; private set; }

    /// <summary>Open the current manual in the desktop's PDF viewer.</summary>
    public AppAction HelpExternalViewer { get; private set; }

    /// <summary>Show the documentation panel.</summary>
    public AppAction HelpDocumentation { get; private set; }

    /// <summary>Look up the word at the caret.</summary>
    public AppAction HelpContext { get; private set; }

    /// <summary>Show the previous page.</summary>
    public AppAction HelpPreviousPage { get; private set; }

    /// <summary>Show the next page.</summary>
    public AppAction HelpNextPage { get; private set; }

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        HelpBack = Add("help_back").WithIcon("go-previous");
        HelpForward = Add("help_forward").WithIcon("go-next");
        HelpHome = Add("help_home").WithIcon("go-home");
        HelpExternalViewer = Add("help_web_browser").WithIcon("document-open");
        HelpDocumentation = Add("help_lilypond_doc")
            .WithIcon("help-contents")
            .WithShortcut("F9");
        HelpContext = Add("help_lilypond_context").WithShortcut("Shift+F9");
        HelpPreviousPage = Add("help_previous_page").WithIcon("go-up");
        HelpNextPage = Add("help_next_page").WithIcon("go-down");
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        HelpBack.Text = I18n.Get("Back");
        HelpForward.Text = I18n.Get("Forward");

        // L10N: Home page of the LilyPond manual
        HelpHome.Text = I18n.Get("Home");

        //was previously: "Open Current Page in Web Browser". There is no web
        //browser and no current PAGE to hand to one — a manual is a file, and
        //what a reader wants is the whole of it in the viewer they already use.
        HelpExternalViewer.Text = I18n.Get("Open in External Viewer");

        //was previously: "&LilyPond Documentation" / "&Contextual LilyPond
        //Help". No UI element of Fresco.Brix names LilyPond (ruling FR13); the
        //engine whose documentation this is, is LilyPort. The manuals' own
        //titles are untouched — they are the documents' names, ruling FD12.
        HelpDocumentation.Text = I18n.Get("&LilyPort Documentation");
        HelpContext.Text = I18n.Get("&Contextual LilyPort Help");

        HelpPreviousPage.Text = I18n.Get("Previous Page");
        HelpNextPage.Text = I18n.Get("Next Page");
    }
}
