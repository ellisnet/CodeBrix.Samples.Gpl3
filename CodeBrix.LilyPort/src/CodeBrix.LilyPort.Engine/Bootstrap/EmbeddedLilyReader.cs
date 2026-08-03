// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System.Collections.Generic;
using System.Text;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap; //was previously: scm/parser-ly-from-scheme.scm;

// Modified by Jeremy Ellis on 2026-08-02 as part of the CodeBrix port.

/// <summary>
/// The <c>#{ ... #}</c> reader extension, which embeds LilyPond syntax inside Scheme.
/// <para>
/// Upstream implements this in Scheme, in <c>scm/parser-ly-from-scheme.scm</c>, using a
/// soft port that copies every character it reads into a string. It is ported here rather
/// than left to that file for one reason: the extension has to exist BEFORE any file that
/// uses <c>#{</c> can be READ, and <c>scm/define-markup-commands.scm</c> uses it. A
/// Scheme-level implementation cannot bootstrap itself past that.
/// </para>
/// <para>
/// The emitted form matches upstream's exactly -- a call to
/// <c>read-lily-expression-internal</c> with the LilyPond source, the file, the line, and
/// an alist of byte offset to thunk for each embedded Scheme expression. Emitting thunks
/// rather than values is what lets embedded Scheme see the enclosing lexical scope, which
/// is the whole point of the construct:
/// <c>(let ((mus ...)) #{ \transpose c d #mus #})</c>.
/// </para>
/// </summary>
public static class EmbeddedLilyReader
{
    /// <summary>Registers the extension with the Scheme reader.</summary>
    public static void Install()
        => SchemeReader.RegisterHashExtension('{', Read);

    /// <summary>Reads one <c>#{ ... #}</c> block.</summary>
    /// <param name="reader">The reader, positioned on the opening brace.</param>
    /// <returns>The Scheme form that reconstructs the embedded music.</returns>
    public static object Read(SchemeReader reader)
    {
        // Consume the '{' the dispatch stopped on.
        reader.ReadCharacterRaw();

        int line = reader.CurrentLine;
        StringBuilder lily = new StringBuilder();
        List<object> closures = new List<object>();

        while (!reader.IsAtEnd)
        {
            char c = reader.ReadCharacterRaw();

            // We stop when #} is encountered.
            if (c == '#' && !reader.IsAtEnd && reader.PeekCharacter() == '}')
            {
                reader.ReadCharacterRaw();
                break;
            }

            lily.Append(c);

            switch (c)
            {
                case '#':
                case '$':
                    ReadEmbeddedScheme(reader, lily, closures);
                    break;

                case '"':
                    CopyLilyString(reader, lily);
                    break;

                case '%':
                    CopyLilyComment(reader, lily);
                    break;
            }
        }

        closures.Reverse();
        return Pair.List(
            Pair.List(
                Symbol.Intern("@@"),
                Pair.List(Symbol.Intern("lily")),
                Symbol.Intern("read-lily-expression-internal")),
            new MutableString(lily.ToString()),
            new MutableString(reader.SourceFileName),
            (long)line,
            new Pair(Symbol.Intern("list"), Pair.ListFrom(closures)));
    }

    private static void ReadEmbeddedScheme(SchemeReader reader, StringBuilder lily, List<object> closures)
    {
        // The offset is taken before the '@' and before the expression text, matching
        // upstream's (ftell out) immediately after writing the '#' or '$'.
        long offset = lily.Length;

        bool isMultiple = !reader.IsAtEnd && reader.PeekCharacter() == '@';
        if (isMultiple)
        {
            lily.Append(reader.ReadCharacterRaw());
        }

        int start = reader.Position;
        object expression = reader.ReadDatum();
        lily.Append(reader.SourceText, start, reader.Position - start);

        // Only symbols and non-quote lists get a closure: a constant needs no lexical
        // environment to evaluate, so wrapping one would be pure overhead.
        bool needsClosure = expression is Symbol
                            || (expression is Pair pair
                                && !(pair.Car is Symbol head && head.Name == "quote"));
        if (!needsClosure)
        {
            return;
        }

        object body = isMultiple
            ? Pair.List(Symbol.Intern("apply"), Symbol.Intern("values"), expression)
            : expression;

        closures.Add(Pair.List(
            Symbol.Intern("cons"),
            offset,
            Pair.List(Symbol.Intern("lambda"), Nil.Instance, body)));
    }

    private static void CopyLilyString(SchemeReader reader, StringBuilder lily)
    {
        // A LilyPond string ends at the quote unless the quote is escaped. Note that
        // \"xxx" is a valid LilyPond construct too, so leading backslashes are not
        // tracked here -- upstream makes the same call, for the same reason.
        while (!reader.IsAtEnd)
        {
            char c = reader.ReadCharacterRaw();
            lily.Append(c);
            if (c == '"')
            {
                return;
            }

            if (c == '\\' && !reader.IsAtEnd)
            {
                lily.Append(reader.ReadCharacterRaw());
            }
        }
    }

    private static void CopyLilyComment(SchemeReader reader, StringBuilder lily)
    {
        if (reader.IsAtEnd)
        {
            return;
        }

        char next = reader.ReadCharacterRaw();
        lily.Append(next);

        if (next == '\n')
        {
            // An empty comment.
            return;
        }

        if (next != '{')
        {
            // A line comment.
            while (!reader.IsAtEnd)
            {
                char c = reader.ReadCharacterRaw();
                lily.Append(c);
                if (c == '\n')
                {
                    return;
                }
            }

            return;
        }

        // A %{ ... %} block comment.
        while (!reader.IsAtEnd)
        {
            char c = reader.ReadCharacterRaw();
            lily.Append(c);
            if (c == '%' && !reader.IsAtEnd && reader.PeekCharacter() == '}')
            {
                lily.Append(reader.ReadCharacterRaw());
                return;
            }
        }
    }
}
