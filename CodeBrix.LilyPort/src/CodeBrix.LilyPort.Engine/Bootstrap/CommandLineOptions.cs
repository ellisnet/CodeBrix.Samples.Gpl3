// Copyright (c) 2026 Jeremy Ellis and contributors
//
// CodeBrix.LilyPort is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using CodeBrix.LilyPort.Flower;
using CodeBrix.LilyScheme.Reader;
using CodeBrix.LilyScheme.Values;

namespace CodeBrix.LilyPort.Engine.Bootstrap; //was previously: lily/main.cc + scm/lily.scm;

// Modified by Jeremy Ellis - 2026 - as part of the CodeBrix.LilyPort port.

/// <summary>
/// Reads one <c>-d</c> option the way a <c>lilypond</c> command line does, and applies
/// it to the option store.
/// <para>
/// Upstream splits this across two files. <c>lily/main.cc:576-590</c> takes the text
/// after <c>-d</c>, splits it at the first <c>=</c>, defaults a missing value to the
/// text <c>#t</c>, and stacks the pair for <c>ly:command-line-options</c> to hand to
/// Scheme; <c>scm/lily.scm:497-543</c> then turns each pair's TEXT into a VALUE,
/// branching four ways on the option's declared type, warning on a text it cannot read,
/// and routing the result to <c>ly:append-to-option</c> or <c>ly:set-option</c>. This
/// type carries both halves.
/// </para>
/// <para>
/// ⚠ THE PORT HAS NO COMMAND LINE, AND THE VENDORED SCHEME HALF IS DEAD CODE. The
/// engine is a library in a host's process: <c>ly:command-line-options</c> answers the
/// empty list, so the <c>lily.scm</c> block above never applies anything. It is also
/// the wrong LIFETIME for a host — it runs once, when the Scheme layer loads, where a
/// host needs options that live for ONE run (a preview engrave wants point-and-click
/// anchors, the publish of the same document does not). Rather than reshape vendored
/// Scheme — which would shift the line numbers the documentation gate compares
/// byte-for-byte, for no behaviour that is wanted at load time — the decision table is
/// ported here and driven per run from <c>BatchRunOptions.Options</c>, after the
/// per-file restore that opens every run. The vendored block stays as upstream wrote
/// it.
/// </para>
/// </summary>
public static class CommandLineOptions
{
    /// <summary>
    /// Applies one <c>-d</c> option, given as the text that FOLLOWS the <c>-d</c>:
    /// <c>debug-voices</c>, <c>no-point-and-click</c>,
    /// <c>include-settings=/path/to/formatter.ily</c>.
    /// </summary>
    /// <param name="options">The option store to apply it to.</param>
    /// <param name="argument">The option text, without its <c>-d</c> prefix.</param>
    /// <remarks>
    /// A null or blank argument is ignored rather than warned about: it is a host
    /// passing an empty entry, not a user mistyping an option.
    /// </remarks>
    public static void Apply(ProgramOptions options, string argument)
    {
        if (options == null)
        {
            throw new ArgumentNullException(nameof(options));
        }

        if (string.IsNullOrWhiteSpace(argument))
        {
            return;
        }

        // main.cc:579-588. The FIRST '=' splits, so a value may contain more of them,
        // and a bare option means the text "#t" rather than the value #t -- the
        // difference matters, because a #:type string option then takes the STRING
        // "#t", exactly as it does upstream.
        int equals = argument.IndexOf('=');
        string key = equals < 0 ? argument : argument.Substring(0, equals);
        string text = equals < 0 ? "#t" : argument.Substring(equals + 1);

        // lily.scm:500. The type is looked up under the key AS WRITTEN -- a `no-'
        // prefixed name is not a declared option, so it takes the unknown-option arm,
        // and the prefix is dealt with later, by SetFromOptionName.
        object value;
        switch (options.ValueSyntaxOf(key))
        {
            case OptionValueSyntax.String:
                value = new MutableString(text);
                break;

            case OptionValueSyntax.StringOrFalse:
                value = text == "#f" ? (object)false : new MutableString(text);
                break;

            case OptionValueSyntax.StringOrBoolean:
                // Type #f means an option this engine has never declared, "probably
                // used privately by the user" -- so #t and #f work as expected and
                // anything else is handled as a string, since we do not know the type.
                value = text == "#t"
                    ? (object)true
                    : text == "#f" ? (object)false : new MutableString(text);
                break;

            default:
                if (!TryRead(text, out value, out string error))
                {
                    // lily.scm:536-540, and upstream's own wording. It warns and
                    // changes NOTHING -- a host that asks for something unreadable is
                    // told, not quietly obeyed with a value nobody chose.
                    Warn.Warning(
                        "Ignoring option -" + "d" + key + "=\"" + text
                        + "\" due to read error: " + error);
                    return;
                }

                break;
        }

        // lily.scm:541-543. An accumulative option GATHERS its values -- this is what
        // lets -dinclude-settings be passed several times -- and everything else is set.
        if (options.IsAccumulative(key))
        {
            options.AppendTo(key, value);
            return;
        }

        options.SetFromOptionName(key, value);
    }

    /// <summary>
    /// Reads the text as one Scheme datum, upstream's
    /// <c>(with-input-from-string str-val read)</c> under a <c>read-error</c> catch.
    /// </summary>
    /// <param name="text">The text to read.</param>
    /// <param name="value">The datum read.</param>
    /// <param name="error">Why it could not be read.</param>
    /// <returns>Whether a datum was read.</returns>
    /// <remarks>
    /// <c>Read</c> rather than <c>ReadDatum</c>, because upstream's <c>read</c> answers
    /// the EOF OBJECT for text with no datum in it (<c>-dfoo=</c>) and raises only for
    /// text it cannot finish (<c>-dfoo=(1 2</c>) — so an empty value sets the option to
    /// the eof object here exactly as it does upstream, and only the unfinishable text
    /// is warned about. Upstream also strips the string port's name, line and column
    /// out of the message with two regexes and rewrites "end of file" as "end of
    /// string"; the port's reader reports neither a port name nor a position, so the
    /// message already arrives in the shape those regexes leave behind.
    /// </remarks>
    private static bool TryRead(string text, out object value, out string error)
    {
        value = null;
        error = null;

        try
        {
            value = new SchemeReader(text, null).Read();
            return true;
        }
        catch (Exception failure)
        {
            error = failure.Message;
            return false;
        }
    }
}
