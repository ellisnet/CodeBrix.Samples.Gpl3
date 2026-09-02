// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Fresco.Brix.Services; //was previously: i18n/mofile.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One message/translation pair as it sits in a compiled catalog, before it is
/// split into its context, its forms and its translations.
/// </summary>
/// <remarks>Upstream's <c>parse_mo_split</c> yields these as three-tuples.</remarks>
public sealed class MoRecord
{
    /// <summary>Creates the record.</summary>
    /// <param name="context">The disambiguating context, or null.</param>
    /// <param name="messages">The singular, then the plural if there is one.</param>
    /// <param name="translations">The translated forms.</param>
    public MoRecord(
        string context,
        IReadOnlyList<string> messages,
        IReadOnlyList<string> translations)
    {
        Context = context;
        Messages = messages;
        Translations = translations;
    }

    /// <summary>Gets the disambiguating context, or null.</summary>
    public string Context { get; }

    /// <summary>Gets the message forms.</summary>
    public IReadOnlyList<string> Messages { get; }

    /// <summary>Gets the translated forms.</summary>
    public IReadOnlyList<string> Translations { get; }
}

/// <summary>
/// An empty catalog: every message comes back untranslated.
/// </summary>
/// <remarks>Upstream's <c>NullMoFile</c>, and the default fallback of every
/// <see cref="MoFile"/>.</remarks>
public class NullMoFile
{
    /// <summary>Translates a message.</summary>
    /// <param name="message">The English message.</param>
    /// <returns>The message.</returns>
    public virtual string Gettext(string message) => message;

    /// <summary>Translates a message with a plural form.</summary>
    /// <param name="message">The English singular.</param>
    /// <param name="messagePlural">The English plural.</param>
    /// <param name="count">The count.</param>
    /// <returns>The form for the count.</returns>
    public virtual string Ngettext(string message, string messagePlural, long count)
        => count == 1 ? message : messagePlural;

    /// <summary>Translates a message in a context.</summary>
    /// <param name="context">The context.</param>
    /// <param name="message">The English message.</param>
    /// <returns>The message.</returns>
    public virtual string Pgettext(string context, string message) => message;

    /// <summary>Translates a contextual message with a plural form.</summary>
    /// <param name="context">The context.</param>
    /// <param name="message">The English singular.</param>
    /// <param name="messagePlural">The English plural.</param>
    /// <param name="count">The count.</param>
    /// <returns>The form for the count.</returns>
    public virtual string Npgettext(
        string context, string message, string messagePlural, long count)
        => count == 1 ? message : messagePlural;

    /// <summary>Gets the catalog consulted when this one has no answer.</summary>
    /// <returns>Null — there is nothing behind an empty catalog.</returns>
    public virtual NullMoFile Fallback() => null;
}

/// <summary>
/// A compiled GNU gettext catalog (a <c>.mo</c> file), read whole.
/// </summary>
/// <remarks>
/// <para>
/// //was previously: <c>i18n/mofile.py</c>, "a loader for MO files, written in
/// 2011 by Wilbert Berendsen", which Frescobaldi uses to read all of a catalog
/// — the plural msgids included, which the stdlib's reader throws away. The
/// four <c>*gettext</c> methods, the fallback chain, the header dictionary and
/// the three <c>parse_mo*</c> functions are all here under their own names.
/// </para>
/// <para>
/// The catalogs this reads are written by <c>tools/i18nharvest</c> from
/// Frescobaldi's own PO files, and the writer is checked against GNU
/// <c>msgfmt</c>'s output entry for entry, so the bytes are a real MO file and
/// nothing here is bent to suit a private format.
/// </para>
/// </remarks>
public sealed class MoFile : NullMoFile
{
    private const uint LittleEndianMagic = 0x950412de;
    private const uint BigEndianMagic = 0xde120495;

    //gettext separates a message from its plural with NUL, which is why NUL is
    //safe to key a plural FORM with: it cannot occur in a msgid.
    private const char FormSeparator = '\u0000';

    private readonly Dictionary<string, string> _catalog
        = new Dictionary<string, string>(StringComparer.Ordinal);

    private readonly Dictionary<string, Dictionary<string, string>> _contextCatalog
        = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _info
        = new Dictionary<string, string>(StringComparer.Ordinal);

    private PluralExpression _plural = PluralExpression.Default;
    private NullMoFile _fallback = new NullMoFile();

    private MoFile()
    {
    }

    /// <summary>Reads a catalog from a file.</summary>
    /// <param name="path">The <c>.mo</c> file.</param>
    /// <returns>The catalog.</returns>
    public static MoFile FromFile(string path)
        => FromData(File.ReadAllBytes(path));

    /// <summary>Reads a catalog from a stream.</summary>
    /// <param name="stream">The open stream.</param>
    /// <returns>The catalog.</returns>
    public static MoFile FromStream(Stream stream)
    {
        using MemoryStream buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return FromData(buffer.ToArray());
    }

    /// <summary>Reads a catalog from bytes.</summary>
    /// <param name="data">The catalog's bytes.</param>
    /// <returns>The catalog.</returns>
    public static MoFile FromData(byte[] data)
    {
        MoFile file = new MoFile();
        file.Load(data);
        return file;
    }

    /// <summary>Gets the catalog's header, its names lower-cased.</summary>
    /// <remarks>Upstream's <c>info()</c>.</remarks>
    public IReadOnlyDictionary<string, string> Info => _info;

    /// <summary>Gets the rule that picks a plural form.</summary>
    public PluralExpression Plural => _plural;

    /// <summary>Gets how many singular entries the catalog holds.</summary>
    public int Count => _catalog.Count;

    /// <summary>Gets how many contexts the catalog holds.</summary>
    public int ContextCount => _contextCatalog.Count;

    /// <summary>Sets the catalog consulted when this one has no answer.</summary>
    /// <param name="fallback">The catalog, or null.</param>
    /// <remarks>Upstream's <c>set_fallback</c>. Null there raises
    /// <c>AttributeError</c> on a miss; here a miss simply answers the message
    /// itself, which is what every caller in this application wants.</remarks>
    public void SetFallback(NullMoFile fallback) => _fallback = fallback;

    /// <inheritdoc/>
    public override NullMoFile Fallback() => _fallback;

    /// <inheritdoc/>
    public override string Gettext(string message)
        => _catalog.TryGetValue(message ?? string.Empty, out var translation)
            ? translation
            : Miss(message);

    /// <inheritdoc/>
    public override string Ngettext(string message, string messagePlural, long count)
        => _catalog.TryGetValue(FormKey(message, count), out var translation)
            ? translation
            : MissPlural(message, messagePlural, count);

    /// <inheritdoc/>
    public override string Pgettext(string context, string message)
        => _contextCatalog.TryGetValue(context ?? string.Empty, out var entries)
            && entries.TryGetValue(message ?? string.Empty, out var translation)
            ? translation
            : MissContext(context, message);

    /// <inheritdoc/>
    public override string Npgettext(
        string context, string message, string messagePlural, long count)
        => _contextCatalog.TryGetValue(context ?? string.Empty, out var entries)
            && entries.TryGetValue(FormKey(message, count), out var translation)
            ? translation
            : MissContextPlural(context, message, messagePlural, count);

    /// <summary>Answers whether the catalog has a translation.</summary>
    /// <param name="context">The context, or null.</param>
    /// <param name="message">The English message.</param>
    /// <returns>True when the catalog carries it.</returns>
    /// <remarks>Not upstream's — Python asks by catching <c>KeyError</c>. The
    /// harvest report and the tests need the question asked out loud.</remarks>
    public bool Has(string context, string message)
        => context == null
            ? _catalog.ContainsKey(message ?? string.Empty)
            : _contextCatalog.TryGetValue(context, out var entries)
                && entries.ContainsKey(message ?? string.Empty);

    /// <summary>Reads every record in a catalog's bytes, undecoded.</summary>
    /// <param name="data">The catalog's bytes.</param>
    /// <returns>The message/translation byte pairs.</returns>
    /// <remarks>Upstream's <c>parse_mo</c>.</remarks>
    public static IEnumerable<(byte[] Message, byte[] Translation)> ParseMo(byte[] data)
    {
        if (data == null || data.Length < 20)
        {
            throw new InvalidDataException("Invalid MO data");
        }

        bool bigEndian;
        uint magic = ReadUInt32(data, 0, bigEndian: false);
        if (magic == LittleEndianMagic)
        {
            bigEndian = false;
        }
        else if (magic == BigEndianMagic)
        {
            bigEndian = true;
        }
        else
        {
            throw new InvalidDataException("Invalid MO data");
        }

        int count = (int)ReadUInt32(data, 8, bigEndian);
        int masterIndex = (int)ReadUInt32(data, 12, bigEndian);
        int translationIndex = (int)ReadUInt32(data, 16, bigEndian);

        for (int i = 0; i < count; i++)
        {
            int messageLength = (int)ReadUInt32(data, masterIndex, bigEndian);
            int messageOffset = (int)ReadUInt32(data, masterIndex + 4, bigEndian);
            int translationLength = (int)ReadUInt32(data, translationIndex, bigEndian);
            int translationOffset = (int)ReadUInt32(data, translationIndex + 4, bigEndian);

            //⚠ Upstream's own bound: it demands the END be STRICTLY inside the
            //buffer, which is true of a real MO file because every string is
            //NUL-terminated. The test is kept as it is written.
            if (messageOffset + messageLength >= data.Length
                || translationOffset + translationLength >= data.Length)
            {
                throw new InvalidDataException("Corrupt MO data");
            }

            byte[] message = new byte[messageLength];
            Array.Copy(data, messageOffset, message, 0, messageLength);
            byte[] translation = new byte[translationLength];
            Array.Copy(data, translationOffset, translation, 0, translationLength);

            yield return (message, translation);

            masterIndex += 8;
            translationIndex += 8;
        }
    }

    /// <summary>Reads every record, split into its context and its forms.</summary>
    /// <param name="data">The catalog's bytes.</param>
    /// <returns>The records, still undecoded.</returns>
    /// <remarks>Upstream's <c>parse_mo_split</c>.</remarks>
    public static IEnumerable<(byte[] Context, byte[][] Messages, byte[][] Translations)>
        ParseMoSplit(byte[] data)
    {
        foreach (var (message, translation) in ParseMo(data))
        {
            byte[] context = null;
            byte[] body = message;

            //Upstream splits on EOT and takes the two-part answer only; a
            //message with more than one EOT raises ValueError there and is
            //treated as having no context, which is what this does.
            byte[][] parts = Split(message, 0x04);
            if (parts.Length == 2)
            {
                context = parts[0];
                body = parts[1];
            }

            yield return (context, Split(body, 0x00), Split(translation, 0x00));
        }
    }

    /// <summary>Reads every record, decoded to text.</summary>
    /// <param name="data">The catalog's bytes.</param>
    /// <returns>The decoded records.</returns>
    /// <remarks>Upstream's <c>parse_mo_decode</c>: the charset comes out of the
    /// header when the header is reached, so the records BEFORE it are decoded
    /// with the default. In a real catalog the header sorts first.</remarks>
    public static IEnumerable<MoRecord> ParseMoDecode(
        byte[] data, string defaultCharset = "UTF-8")
    {
        Encoding encoding = GetEncoding(defaultCharset);
        foreach (var (context, messages, translations) in ParseMoSplit(data))
        {
            if (messages.Length > 0 && messages[0].Length == 0)
            {
                Dictionary<string, string> info = ParseHeader(
                    Encoding.UTF8.GetString(translations.Length > 0
                        ? translations[0]
                        : Array.Empty<byte>()));
                if (info.TryGetValue("content-type", out var contentType))
                {
                    string charset = CharsetOf(contentType);
                    if (charset != null) { encoding = GetEncoding(charset); }
                }
            }

            yield return new MoRecord(
                context == null ? null : encoding.GetString(context),
                Decode(messages, encoding),
                Decode(translations, encoding));
        }
    }

    /// <summary>Splits a catalog header into its named fields.</summary>
    /// <param name="text">The header, the msgstr of the empty msgid.</param>
    /// <returns>The fields, their names lower-cased.</returns>
    /// <remarks>Upstream's <c>parse_header</c>: a line with no colon continues
    /// the previous field.</remarks>
    public static Dictionary<string, string> ParseHeader(string text)
    {
        Dictionary<string, string> info
            = new Dictionary<string, string>(StringComparer.Ordinal);
        string last = null;

        foreach (var raw in (text ?? string.Empty).Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length == 0) { continue; }

            int colon = line.IndexOf(':');
            if (colon >= 0)
            {
                last = line.Substring(0, colon).Trim().ToLowerInvariant();
                info[last] = line.Substring(colon + 1).Trim();
            }
            else if (last != null)
            {
                info[last] += "\n" + line;
            }
        }

        return info;
    }

    private void Load(byte[] data)
    {
        Encoding encoding = Encoding.UTF8;

        foreach (var (context, messages, translations) in ParseMoSplit(data))
        {
            if (messages.Length > 0 && messages[0].Length == 0)
            {
                Dictionary<string, string> info = ParseHeader(
                    Encoding.UTF8.GetString(translations.Length > 0
                        ? translations[0]
                        : Array.Empty<byte>()));

                if (info.TryGetValue("content-type", out var contentType))
                {
                    string charset = CharsetOf(contentType);
                    if (charset != null) { encoding = GetEncoding(charset); }
                }

                if (info.TryGetValue("plural-forms", out var forms))
                {
                    PluralExpression plural = PluralExpression.Parse(PluralOf(forms));
                    if (plural != null) { _plural = plural; }
                }

                foreach (var pair in info) { _info[pair.Key] = pair.Value; }
                continue;
            }

            Dictionary<string, string> target = _catalog;
            if (context != null)
            {
                string name = encoding.GetString(context);
                if (!_contextCatalog.TryGetValue(name, out target))
                {
                    target = new Dictionary<string, string>(StringComparer.Ordinal);
                    _contextCatalog[name] = target;
                }
            }

            string singular = encoding.GetString(messages[0]);
            if (messages.Length > 1)
            {
                for (int form = 0; form < translations.Length; form++)
                {
                    target[singular + FormSeparator + form.ToString(
                        System.Globalization.CultureInfo.InvariantCulture)]
                        = encoding.GetString(translations[form]);
                }
            }
            else
            {
                target[singular] = translations.Length > 0
                    ? encoding.GetString(translations[0])
                    : string.Empty;
            }
        }
    }

    /// <summary>The key a plural form is stored under.</summary>
    /// <remarks>Upstream keys these by the TUPLE <c>(msgid, form)</c>; C#
    /// dictionaries want one key, so the two are joined by the separator that
    /// cannot occur in a msgid — the same NUL gettext itself joins forms
    /// with.</remarks>
    private string FormKey(string message, long count)
        => (message ?? string.Empty) + FormSeparator
            + _plural.Evaluate(count).ToString(
                System.Globalization.CultureInfo.InvariantCulture);

    private string Miss(string message)
        => _fallback == null ? message : _fallback.Gettext(message);

    private string MissPlural(string message, string messagePlural, long count)
        => _fallback == null
            ? (count == 1 ? message : messagePlural)
            : _fallback.Ngettext(message, messagePlural, count);

    private string MissContext(string context, string message)
        => _fallback == null ? message : _fallback.Pgettext(context, message);

    private string MissContextPlural(
        string context, string message, string messagePlural, long count)
        => _fallback == null
            ? (count == 1 ? message : messagePlural)
            : _fallback.Npgettext(context, message, messagePlural, count);

    private static string CharsetOf(string contentType)
    {
        int marker = contentType.IndexOf("charset=", StringComparison.Ordinal);
        return marker < 0 ? null : contentType.Substring(marker + "charset=".Length).Trim();
    }

    private static string PluralOf(string forms)
    {
        //Upstream: split on ';', take the SECOND field, split it on 'plural='
        //and take what follows. A header that does not have that shape leaves
        //the catalog on its default rule.
        string[] fields = forms.Split(';');
        if (fields.Length < 2) { return null; }

        int marker = fields[1].IndexOf("plural=", StringComparison.Ordinal);
        return marker < 0 ? null : fields[1].Substring(marker + "plural=".Length);
    }

    private static Encoding GetEncoding(string charset)
    {
        try
        {
            return Encoding.GetEncoding(charset);
        }
        catch (ArgumentException)
        {
            return Encoding.UTF8;
        }
    }

    private static IReadOnlyList<string> Decode(byte[][] parts, Encoding encoding)
    {
        string[] decoded = new string[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            decoded[i] = encoding.GetString(parts[i]);
        }

        return decoded;
    }

    private static uint ReadUInt32(byte[] data, int offset, bool bigEndian)
        => bigEndian
            ? ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16)
                | ((uint)data[offset + 2] << 8) | data[offset + 3]
            : ((uint)data[offset + 3] << 24) | ((uint)data[offset + 2] << 16)
                | ((uint)data[offset + 1] << 8) | data[offset];

    private static byte[][] Split(byte[] data, byte separator)
    {
        List<byte[]> parts = new List<byte[]>();
        int start = 0;
        for (int i = 0; i < data.Length; i++)
        {
            if (data[i] != separator) { continue; }

            byte[] part = new byte[i - start];
            Array.Copy(data, start, part, 0, part.Length);
            parts.Add(part);
            start = i + 1;
        }

        byte[] last = new byte[data.Length - start];
        Array.Copy(data, start, last, 0, last.Length);
        parts.Add(last);
        return parts.ToArray();
    }
}
