// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Fresco.Brix is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using CodeBrix.LilyPort.Importers;
using Fresco.Brix.Engrave;
using Fresco.Brix.Services;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace Fresco.Brix.Import; //was previously: frescobaldi/file_import/toly_dialog.py (configure_job) + job/__init__.py

// Modified by Jeremy Ellis - 2026 - as part of the Fresco.Brix port.

/// <summary>
/// One file conversion, run as a job so its messages arrive in the log the way
/// an engrave's do.
/// </summary>
/// <remarks>
/// <para>
/// ⚠ THIS IS A REPLACE, NOT A PORT, AND IT IS THE SHAPE W11 MADE FOR EXPORT
/// AUDIO. Upstream builds a <c>job.Job</c> around a command line —
/// <c>musicxml2ly --output=… FILE</c> — runs it in a subprocess and shows the
/// output in a <c>job.dialog.Dialog</c>; a machine without LilyPond's scripts
/// installed cannot import anything, and the dialog is where the user finds
/// that out. Here <c>CodeBrix.LilyPort.Importers</c> is the same program in
/// this process: the conversion is a function call, there is no command line,
/// and <c>externalcommand.py</c> stays dropped (board §6). Ruling FD1 and
/// ruling FR5.1 between them also remove the version chooser that decided WHICH
/// scripts to run.
/// </para>
/// <para>
/// What is deliberately KEPT is the job: upstream's conversion had a start, a
/// stream of messages, an end and a success, and the log panel, the queue and
/// the status bar in this application are all written against exactly that. So
/// the converter's captured stderr — <see cref="ImportResult.Messages"/> —
/// becomes the job's error channel, and the log shows it beside the document it
/// made, which is more than the transient dialog upstream throws away.
/// </para>
/// <para>
/// Board trap 22: the conversion runs OFF the UI thread and every message is
/// posted back on the thread that started the job, which
/// <see cref="EngraveJob"/> captures.
/// </para>
/// </remarks>
public sealed class ImportJob : EngraveJob
{
    private readonly ImportFormat _format;
    private readonly string _inputPath;
    private readonly object _options;

    /// <summary>Creates the job.</summary>
    /// <param name="format">Which converter to run.</param>
    /// <param name="inputPath">The file to convert.</param>
    /// <param name="options">The converter's options, from the dialog.</param>
    /// <exception cref="ArgumentNullException">No input path.</exception>
    public ImportJob(ImportFormat format, string inputPath, object options)
        : base(string.Empty)
    {
        _format = format;
        _inputPath = inputPath ?? throw new ArgumentNullException(nameof(inputPath));
        _options = options;

        FileName = inputPath;
        Directory = Path.GetDirectoryName(inputPath);

        //Upstream's job title is the command line it is about to run. There is
        //no command line here, so the title says what is being converted by
        //what — which is what the log's first line is for.
        Title = I18n.Format(
            I18n.Get("{converter} [{document}]"),
            ("converter", ImportFormats.ConverterName(format)),
            ("document", Path.GetFileName(inputPath)));
    }

    /// <summary>Gets what the converter produced, once the job has ended.</summary>
    public ImportResult Result { get; private set; }

    /// <summary>Gets the converted source, or null when nothing was produced.</summary>
    public string Text => Result?.Text;

    /// <summary>Reads a MusicXML document, honouring the encoding it declares.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The document text.</returns>
    /// <remarks>
    /// ⚠ NOT <c>File.ReadAllText</c>. Upstream hands the converter a PATH and
    /// its XML parser reads the declaration; a library is handed a string, so
    /// the declaration has to be honoured here or a Latin-1 document arrives
    /// with its accented names mangled. A byte-order mark wins, then the
    /// <c>encoding="…"</c> of the declaration, then UTF-8 — which is what the
    /// XML specification's own detection amounts to for the encodings a
    /// desktop is likely to meet.
    /// </remarks>
    public static string ReadXmlText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        return DecodeXml(bytes);
    }

    /// <summary>Reads a text file that declares nothing about its encoding.</summary>
    /// <param name="path">The file.</param>
    /// <returns>The text.</returns>
    /// <remarks>
    /// ABC files carry no encoding declaration and upstream's <c>abc2ly</c>
    /// opens them in the process's own locale encoding. A byte-order mark wins;
    /// otherwise the bytes are read as UTF-8 when they ARE valid UTF-8 and as
    /// Latin-1 when they are not, which reads every ABC file this application
    /// is likely to be given and never throws on one.
    /// </remarks>
    public static string ReadPlainText(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Encoding declared = ByteOrderMark(bytes, out int skip);
        if (declared != null)
        {
            return declared.GetString(bytes, skip, bytes.Length - skip);
        }

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true)
                .GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(bytes);
        }
    }

    /// <inheritdoc/>
    protected override async Task<bool> RunAsync()
    {
        //Board trap 6 and trap 22: the converter is a parser and a writer and
        //has no business on the thread that draws the window.
        ImportResult result = await Task.Run(Convert).ConfigureAwait(true);
        Result = result;

        //Upstream's converters write their remarks to standard error and the
        //job dialog shows them; the same remarks, on the same channel.
        foreach (string message in result.Messages)
        {
            Message(message.TrimEnd('\n') + "\n", MessageType.StdErr);
        }

        //⚠ THE TEST IS "DID A DOCUMENT COME OUT", NOT `Succeeded'. Upstream's
        //job succeeds when the command EXITS ZERO, and `abc2ly' exits zero for
        //a file it only partly understood: its `error()' writes "Huh? Don't
        //understand" to stderr and returns, and only `--strict' (which
        //Frescobaldi's dialog never passes) makes it exit. So a warning-laden
        //ABC file opens in Frescobaldi with its warnings in the log, and it
        //opens here the same way. `ImportResult.Succeeded' is the error COUNT
        //being zero, which is a different question; it is on the FIXLIST for
        //the package rather than worked around by hiding the document.
        return result.Text != null;
    }

    /// <inheritdoc/>
    /// <remarks>Upstream's job writes "Starting <c>the command line</c>...";
    /// there is no command line, so the converter and the file stand in.</remarks>
    protected override string NameForMessages() => Title;

    private static Encoding ByteOrderMark(byte[] bytes, out int skip)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            skip = 3;
            return new UTF8Encoding(false);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            skip = 2;
            return new UnicodeEncoding(bigEndian: false, byteOrderMark: false);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            skip = 2;
            return new UnicodeEncoding(bigEndian: true, byteOrderMark: false);
        }

        skip = 0;
        return null;
    }

    private static string DecodeXml(byte[] bytes)
    {
        Encoding mark = ByteOrderMark(bytes, out int skip);
        if (mark != null)
        {
            return mark.GetString(bytes, skip, bytes.Length - skip);
        }

        //The declaration itself is ASCII-compatible in every encoding a
        //declaration can be written in, so it can be read as Latin-1 first.
        string opening = Encoding.Latin1.GetString(
            bytes, 0, Math.Min(bytes.Length, 256));
        System.Text.RegularExpressions.Match declared
            = System.Text.RegularExpressions.Regex.Match(
                opening,
                "<\\?xml[^>]*?encoding\\s*=\\s*[\"']([^\"']+)[\"']",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (declared.Success)
        {
            try
            {
                return Encoding.GetEncoding(declared.Groups[1].Value).GetString(bytes);
            }
            catch (ArgumentException)
            {
                //An encoding this runtime has never heard of is not a reason to
                //refuse the file; UTF-8 is what the XML specification defaults to.
            }
        }

        return new UTF8Encoding(false).GetString(bytes);
    }

    private ImportResult Convert()
    {
        switch (_format)
        {
            case ImportFormat.MusicXml when ImportFormats.IsCompressedMusicXml(_inputPath):
                return MusicXmlImporter.ImportCompressed(
                    File.ReadAllBytes(_inputPath),
                    _options as MusicXmlImportOptions);

            case ImportFormat.MusicXml:
                return MusicXmlImporter.Import(
                    ReadXmlText(_inputPath), _options as MusicXmlImportOptions);

            case ImportFormat.Midi:
                return MidiImporter.Import(
                    File.ReadAllBytes(_inputPath), _options as MidiImportOptions);

            default:
                return AbcImporter.Import(
                    ReadPlainText(_inputPath), _options as AbcImportOptions);
        }
    }
}
