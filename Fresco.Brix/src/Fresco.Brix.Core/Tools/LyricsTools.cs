// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Fresco.Brix.Commands;
using Fresco.Brix.Documents;
using Fresco.Brix.Editor;
using Fresco.Brix.Ly;
using Fresco.Brix.Services;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Lily = Fresco.Brix.Ly.Lex.LilyPondMode;
using Token = Fresco.Brix.Ly.Slexing.Token;

namespace Fresco.Brix.Tools; //was previously: frescobaldi/lyrics.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>One word of a lyric, and where it sits in the document.</summary>
public sealed class LyricWord
{
    /// <summary>Creates the word.</summary>
    /// <param name="start">Where it starts.</param>
    /// <param name="end">Where it ends.</param>
    /// <param name="text">The word itself.</param>
    public LyricWord(int start, int end, string text)
    {
        Start = start;
        End = end;
        Text = text;
    }

    /// <summary>Gets where the word starts.</summary>
    public int Start { get; }

    /// <summary>Gets where the word ends.</summary>
    public int End { get; }

    /// <summary>Gets the word.</summary>
    public string Text { get; }

    /// <inheritdoc/>
    public override string ToString() => $"{Start}-{End} {Text}";
}

/// <summary>
/// The three lyric commands: hyphenate the words into syllables, take the
/// hyphenation back out, and copy the text with it taken out.
/// </summary>
/// <remarks>
/// Hyphenating is the interesting one, and its interest is entirely in
/// FINDING the words. A user may have selected a proper <c>\lyricmode</c>
/// block, or a bare run of words that is going to BECOME one, or something in
/// between; <see cref="FindWords"/> is upstream's three attempts at that, in
/// upstream's order.
/// </remarks>
public static class LyricsTools
{
    //Upstream's _word_re: letters, and nothing else — no digits, no
    //underscores, which in a lyric are the extender and the melisma marks.
    private static readonly Regex WordPattern = new Regex(
        @"[^\W0-9_]+", RegexOptions.Compiled);

    //Upstream's removehyphens: the syllable separator, the extender line, and
    //the melisma underscores.
    private static readonly Regex HyphenPattern = new Regex(
        @"[ \t]*--[ \t]*|__[ \t]*|_[ \t]+(_[ \t]+)*", RegexOptions.Compiled);

    /// <summary>The hyphen a hyphenated lyric is written with.</summary>
    /// <remarks>A LilyPond lyric separates syllables with a spaced double
    /// hyphen, which is why the hyphenator is asked for that and not for
    /// <c>-</c>.</remarks>
    public const string LyricHyphen = " -- ";

    /// <summary>
    /// Finds the words that can be hyphenated in a document or a selection.
    /// </summary>
    /// <param name="document">The document.</param>
    /// <param name="start">Where the selection starts.</param>
    /// <param name="end">Where it ends; equal to <paramref name="start"/> for
    /// no selection.</param>
    /// <returns>The words, in document order.</returns>
    /// <remarks>
    /// Three attempts, upstream's: the tokens the document's own tokenizer
    /// calls lyric text; then the selection re-tokenized AS IF it were lyric
    /// mode, which is what catches a run of words the user has typed but not
    /// yet wrapped in <c>\lyricmode</c>; and finally the selection as flat
    /// text. Each is tried only when the one before it found nothing, and the
    /// last two need a selection — over a whole document they would hyphenate
    /// the music.
    /// </remarks>
    public static IReadOnlyList<LyricWord> FindWords(
        EditorDocument document, int start, int end)
    {
        List<LyricWord> found = new List<LyricWord>();
        if (document == null) { return found; }

        AteLyDocument text = DocumentEditorState.For(document).LyDocument;
        bool hasSelection = end > start;
        Cursor cursor = hasSelection
            ? new Cursor(text, start, end)
            : new Cursor(text, 0, document.Text.Length);

        Source source = new Source(cursor, tokensWithPosition: true);
        foreach (var token in source)
        {
            if (token is not Lily.LyricText) { continue; }

            AddWords(found, source.Position(token), token.Text);
        }

        if (found.Count > 0 || !hasSelection) { return found; }

        //Not a lyrics block yet: read the selection as though it were one.
        string selected = document.Text.Substring(start, end - start);
        Ly.Lex.State state = new Ly.Lex.State(new Lily.ParseLyricMode());
        foreach (Token token in state.Tokens(selected))
        {
            if (token is not Lily.LyricText) { continue; }

            AddWords(found, start + token.Pos, token.Text);
        }

        if (found.Count > 0) { return found; }

        //Still nothing: take the selection as plain text.
        AddWords(found, start, selected);
        return found;
    }

    /// <summary>Writes the hyphenation into a document's lyric words.</summary>
    /// <param name="document">The document.</param>
    /// <param name="words">The words <see cref="FindWords"/> found.</param>
    /// <param name="hyphenator">The dictionary to hyphenate with.</param>
    /// <returns>How many words gained a hyphen.</returns>
    public static int Hyphenate(
        EditorDocument document,
        IReadOnlyList<LyricWord> words,
        Hyphenator hyphenator)
    {
        if (document == null || words == null || hyphenator == null) { return 0; }

        AteLyDocument text = DocumentEditorState.For(document).LyDocument;
        int changed = 0;
        using (text.Writing())
        {
            foreach (var word in words)
            {
                string hyphenated = hyphenator.Inserted(word.Text, LyricHyphen);
                if (string.Equals(word.Text, hyphenated, StringComparison.Ordinal))
                {
                    continue;
                }

                text.SetText(word.Start, word.End, hyphenated);
                changed++;
            }
        }

        return changed;
    }

    /// <summary>Takes the hyphenation and the extenders out of text.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The text as prose.</returns>
    /// <remarks>Upstream's <c>removehyphens</c>: the separators go, and the
    /// two characters that stand for a space in a lyric become spaces.</remarks>
    public static string RemoveHyphens(string text)
    {
        if (string.IsNullOrEmpty(text)) { return text; }

        return HyphenPattern.Replace(text, string.Empty)
            .Replace('_', ' ')
            .Replace('~', ' ');
    }

    /// <summary>Answers whether text carries any hyphenation to remove.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Whether it does.</returns>
    /// <remarks>Upstream's own test, and it is deliberately the cheap one: a
    /// selection with no <c> --</c> in it is left alone entirely, extenders
    /// and all.</remarks>
    public static bool HasHyphens(string text)
        => text != null && text.Contains(" --", StringComparison.Ordinal);

    private static void AddWords(List<LyricWord> found, int position, string text)
    {
        foreach (Match match in WordPattern.Matches(text))
        {
            found.Add(new LyricWord(
                position + match.Index, position + match.Index + match.Length, match.Value));
        }
    }
}

/// <summary>The Lyrics menu's commands.</summary>
public sealed class LyricsActions : ActionCollection
{
    /// <summary>The collection name.</summary>
    public const string CollectionName = "lyrics";

    /// <summary>Creates the collection.</summary>
    /// <param name="settings">The store shortcuts are remembered in.</param>
    public LyricsActions(SettingsStore settings = null)
        : base(CollectionName, settings) => Initialize();

    /// <summary>The commands that need a selection to act on.</summary>
    public static readonly IReadOnlyList<string> SelectionActionNames = new[]
    {
        "lyrics_dehyphenate", "lyrics_copy_dehyphenated",
    };

    /// <summary>Gets the hyphenate command.</summary>
    public AppAction LyricsHyphenate { get; private set; }

    /// <summary>Gets the remove-hyphenation command.</summary>
    public AppAction LyricsDehyphenate { get; private set; }

    /// <summary>Gets the copy-without-hyphenation command.</summary>
    public AppAction LyricsCopyDehyphenated { get; private set; }

    /// <inheritdoc/>
    public override string Title => I18n.Get("Lyrics");

    /// <inheritdoc/>
    protected override void CreateActions()
    {
        LyricsHyphenate = Add("lyrics_hyphenate").WithShortcut("Ctrl+L");
        LyricsDehyphenate = Add("lyrics_dehyphenate");
        LyricsCopyDehyphenated = Add("lyrics_copy_dehyphenated");
    }

    /// <inheritdoc/>
    public override void TranslateUI()
    {
        LyricsHyphenate.Text = I18n.Get("&Hyphenate Lyrics Text...");
        LyricsDehyphenate.Text = I18n.Get("&Remove hyphenation");
        LyricsCopyDehyphenated.Text
            = I18n.Get("&Copy Lyrics with hyphenation removed");
    }
}
