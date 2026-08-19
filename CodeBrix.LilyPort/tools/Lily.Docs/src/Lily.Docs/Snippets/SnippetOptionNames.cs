// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

namespace Lily.Docs.Snippets;

/// <summary>
/// The snippet option names, spelled as lilypond-book spells them.
/// <para>
/// Ported from <c>python/book_snippets.py:44-79</c> (the module's own name constants),
/// read in <c>book-mirror/</c>. They are the DICTIONARY KEYS of the composed source's
/// option set, so a misspelling here is a silently different composition rather than a
/// compile error — which is why they are named constants rather than inline literals.
/// </para>
/// </summary>
public static class SnippetOptionNames
{
    /// <summary>The alt text of a snippet's picture.</summary>
    public const string Alt = "alt";

    /// <summary>Print the snippet's title from its <c>\header</c>.</summary>
    public const string DocTitle = "doctitle";

    /// <summary>The indentation <c>quote</c> subtracts from the line width.</summary>
    public const string ExampleIndent = "exampleindent";

    /// <summary>Wrap the snippet in a bare music expression.</summary>
    public const string Fragment = "fragment";

    /// <summary>The <c>\paper</c> indent of the first system.</summary>
    public const string Indent = "indent";

    /// <summary>Set the snippet small, to sit inside a paragraph.</summary>
    public const string Inline = "inline";

    /// <summary>The width the music is set to.</summary>
    public const string LineWidth = "line-width";

    /// <summary>Cancel a <c>fragment</c> that would otherwise apply.</summary>
    public const string NoFragment = "nofragment";

    /// <summary>Cancel an <c>indent</c> that would otherwise apply.</summary>
    public const string NoIndent = "noindent";

    /// <summary>Turn ragged-right setting off.</summary>
    public const string NoRaggedRight = "noragged-right";

    /// <summary>Drop the time signature and the timing.</summary>
    public const string NoTime = "notime";

    /// <summary>The paper height.</summary>
    public const string PaperHeight = "paper-height";

    /// <summary>The paper size, as a name or a constructed pair.</summary>
    public const string PaperSize = "papersize";

    /// <summary>The paper width.</summary>
    public const string PaperWidth = "paper-width";

    /// <summary>Indent the snippet in the DOCUMENT, and narrow the music to match.</summary>
    public const string Quote = "quote";

    /// <summary>Turn ragged-right setting on.</summary>
    public const string RaggedRight = "ragged-right";

    /// <summary>The octave the fragment's music is relative to.</summary>
    public const string Relative = "relative";

    /// <summary>The global staff size.</summary>
    public const string StaffSize = "staffsize";

    /// <summary>Print the snippet's <c>texidoc</c> field.</summary>
    public const string TexiDoc = "texidoc";

    /// <summary>Show the snippet's source as well as its engraving.</summary>
    public const string Verbatim = "verbatim";

    /// <summary>Print the snippet's file name (HTML output).</summary>
    public const string HtmlPrintFileName = "htmlprintfilename";

    /// <summary>Do not translate the snippet's comments.</summary>
    public const string NoGettext = "nogettext";

    /// <summary>Print the snippet's file name.</summary>
    public const string PrintFileName = "printfilename";

    /// <summary>Print the LilyPond version.</summary>
    public const string LilypondVersion = "lilypondversion";
}
