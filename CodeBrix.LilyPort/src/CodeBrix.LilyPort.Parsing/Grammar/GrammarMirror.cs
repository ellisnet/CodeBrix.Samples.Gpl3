// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace CodeBrix.LilyPort.Parsing.Grammar;

/// <summary>
/// The vendored grammar and lexer sources, read out of this assembly.
/// <para>
/// They are EMBEDDED rather than read from disk, for the same reason the Scheme layer
/// is: a CodeBrix build must not depend on a path outside the assembly, and the
/// generator has to read exactly the mirror the assembly was built from. See
/// <c>parser-mirror/README.txt</c> for the mirror's rules.
/// </para>
/// </summary>
public static class GrammarMirror
{
    /// <summary>
    /// The SHA-256 of <c>parser.yy</c> at the pinned v2.27.2 release.
    /// <para>
    /// Recorded so a re-sync is announced rather than discovered. The fence test
    /// compares against it; when the mirror is deliberately updated, this constant and
    /// the figures in <c>GrammarShapeTests</c> are updated in the same change.
    /// </para>
    /// </summary>
    public const string PinnedParserSha256
        = "de1c8fba1e532f0d7b7696dfb8c236467a5061adf322d5f2ebdede89f28b50ba";

    /// <summary>The SHA-256 of <c>lexer.ll</c> at the pinned v2.27.2 release.</summary>
    public const string PinnedLexerSha256
        = "574509fb79bcfbd7636c2e2eb26f63b0ccab9acd474de29b5c355f2ee9e24bc1";

    private static string _parserSource;
    private static string _lexerSource;

    /// <summary>Gets the vendored <c>parser.yy</c> text.</summary>
    public static string ParserSource => _parserSource ??= Read("parser.yy");

    /// <summary>Gets the vendored <c>lexer.ll</c> text.</summary>
    public static string LexerSource => _lexerSource ??= Read("lexer.ll");

    /// <summary>Returns the SHA-256 of a mirrored file's bytes, lower-case hex.</summary>
    /// <param name="text">The file text.</param>
    /// <returns>The hash.</returns>
    public static string Sha256Of(string text)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexStringLower(hash);
    }

    private static string Read(string fileName)
    {
        Assembly assembly = typeof(GrammarMirror).Assembly;
        string resource = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("." + fileName, StringComparison.Ordinal));

        if (resource == null)
        {
            throw new InvalidOperationException(
                "The mirrored '" + fileName + "' is missing from the assembly.");
        }

        using Stream stream = assembly.GetManifestResourceStream(resource);
        using StreamReader reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
