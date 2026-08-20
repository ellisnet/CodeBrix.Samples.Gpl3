// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Lily.Docs.Manuals;
using Lily.Docs.Snippets;
using Lily.Shell.Kernel;
using Lily.Shell.Kernel.Commands;
using Lily.Shell.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;

namespace Lily.Shell.Commands;

/// <summary>
/// Renders one of the port's nine manuals — print-shaped HTML and PDF — from the
/// documentation the port generates, through the published CodeBrix.Texinfo packages.
/// </summary>
/// <remarks>
/// <para>
/// This is Phase 5's user-visible capability in the shell, per decision D52: Lily.Docs
/// is a repo tool that ships nothing, AND the shell carries a <c>docs</c> command so the
/// capability is reachable without building a separate tool. The manual list is a read of
/// <see cref="ManualCatalog"/> rather than a list of its own, so a manual added there
/// appears here with no edit — a hand-kept second list is how the two drift.
/// </para>
/// <para>
/// ⚠ NO <c>--baseline</c>. Lily.Docs can freeze a manual's expected-warnings baseline
/// from a run; the shell deliberately cannot. A baseline is frozen from a run that was
/// READ and reviewed, and it belongs to the repository rather than to a session.
/// </para>
/// </remarks>
public sealed class DocsCommand : IShellCommand
{
    /// <summary>How often the progress line is printed during a long render.</summary>
    private static readonly TimeSpan ProgressInterval = TimeSpan.FromSeconds(20);

    private readonly LilyPortHost _host;
    private readonly DocsRunner _runner;

    /// <summary>Creates the command over the engine host.</summary>
    /// <param name="host">The host that owns the in-process engine.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is null.</exception>
    public DocsCommand(LilyPortHost host)
        : this(host, new DocsRunner())
    {
    }

    /// <summary>Creates the command over the engine host and a runner.</summary>
    /// <param name="host">The host that owns the in-process engine.</param>
    /// <param name="runner">The documentation runner — one per session, because it holds
    /// the once-per-process generation.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    internal DocsCommand(LilyPortHost host, DocsRunner runner)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    /// <inheritdoc/>
    public string Name => "docs";

    /// <inheritdoc/>
    public string Summary => "Renders one of the port's nine manuals to HTML and PDF.";

    /// <inheritdoc/>
    public string Usage => "docs [<manual>] [--html] [--pdf] [--no-snippets] [-o <dir>]";

    /// <inheritdoc/>
    public async Task ExecuteAsync(ShellCommandContext context)
    {
        DocsCommandLine command = DocsCommandLine.Parse(context.Arguments);
        if (command.Error != null)
        {
            context.IO.WriteLine(command.Error);
            context.IO.WriteLine("usage: " + Usage);
            WriteManualList(context);
            return;
        }

        if (command.ListOnly)
        {
            WriteManualList(context);
            context.IO.WriteLine();
            context.IO.WriteLine("usage: " + Usage);
            context.IO.WriteLine(
                "  --html / --pdf     which formats; with neither, BOTH (one render, not two)");
            context.IO.WriteLine(
                "  --no-snippets      render with no engraver: the control run, seconds not minutes");
            context.IO.WriteLine(
                "  -o <dir>           where to write (default: " + DocsRunner.ScratchRoot + "/<manual>)");
            context.IO.WriteLine();
            context.IO.WriteLine("The nineteen generated files are written once per session and reused; "
                + "the notation manual then takes about six minutes to engrave.");
            return;
        }

        DocsRunRequest request = new DocsRunRequest(command.Manual)
        {
            WantHtml = command.WantHtml,
            WantPdf = command.WantPdf,
            EngraveSnippets = command.EngraveSnippets,
            OutputDirectory = command.OutputDirectory,
        };

        // The whole run goes through the engine host: generation and every snippet
        // engraving are engine work, the engine is process-global, and its Scheme layer
        // needs the host's big stack. Routing through the host is also what serializes
        // this against an `engrave' or `scheme' the user runs next.
        Task<DocsRunResult> job = _host.RunEngineWorkAsync(
            () => _runner.Render(request, context.IO.WriteLine), context.CancellationToken);

        DocsRunResult result;
        try
        {
            result = await AwaitWithProgress(job, context).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A running Scheme evaluation cannot be interrupted, so Ctrl+C stops the WAIT
            // and not the work. Saying so is better than letting the prompt come back and
            // look as though the render was abandoned.
            context.IO.WriteLine("(stopped waiting; the render is still running in the background)");
            return;
        }
        catch (Exception exception)
        {
            context.IO.WriteLine("docs FAILED: " + ShellSession.DeepestMessage(exception));
            return;
        }

        WriteResult(context, result);
    }

    /// <summary>
    /// Awaits the render, printing a progress line every <see cref="ProgressInterval"/>.
    /// </summary>
    /// <param name="job">The running render.</param>
    /// <param name="context">The command context.</param>
    /// <returns>The render's result.</returns>
    /// <remarks>
    /// Progress is printed from the COMMAND's own execution, never from a timer thread:
    /// the terminal is fed from one place at a time, and a background writer would
    /// interleave with the render's own output.
    /// </remarks>
    private async Task<DocsRunResult> AwaitWithProgress(
        Task<DocsRunResult> job, ShellCommandContext context)
    {
        while (true)
        {
            Task finished = await Task.WhenAny(
                job, Task.Delay(ProgressInterval, context.CancellationToken)).ConfigureAwait(false);
            if (ReferenceEquals(finished, job))
            {
                return await job.ConfigureAwait(false);
            }

            context.CancellationToken.ThrowIfCancellationRequested();
            int asked = _runner.SnippetsAsked;
            if (asked > 0)
            {
                context.IO.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "  ... {0} snippets engraved so far", asked));
            }
            else
            {
                context.IO.WriteLine("  ... still working");
            }
        }
    }

    private void WriteManualList(ShellCommandContext context)
    {
        context.IO.WriteLine("manuals:");
        foreach (ManualDefinition manual in ManualCatalog.All)
        {
            context.IO.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "  {0,-15} {1}{2}", manual.Name, manual.Title,
                manual.EngravesSnippets ? string.Empty : "  (no music)"));
        }

        string generated = _runner.GeneratedDirectory;
        context.IO.WriteLine(generated == null
            ? "The port's nineteen documentation files have not been generated yet this session."
            : "The port's nineteen documentation files are in " + generated + ".");
    }

    private static void WriteResult(ShellCommandContext context, DocsRunResult result)
    {
        if (result.Html != null)
        {
            context.IO.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "HTML: {0}  ({1:0.0} s, {2} warnings, {3} pictures)",
                result.Html.HtmlPath, result.Html.Elapsed.TotalSeconds,
                result.Html.Warnings.Count, result.Html.Result.Images.Count));
        }

        if (result.Pdf != null)
        {
            context.IO.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "PDF:  {0}  ({1:0.0} s, {2} pages, {3} texinfo + {4} pdf warnings)",
                result.Pdf.PdfPath, result.Pdf.Elapsed.TotalSeconds, result.Pdf.PageCount,
                result.Pdf.TexinfoWarnings.Count, result.Pdf.PdfWarnings.Count));
            foreach (string row in result.Pdf.DropRows())
            {
                context.IO.WriteLine("      " + row.Replace('\t', ' '));
            }
        }

        // ⚠ ASKED AND FAILED, NEVER "it finished". The Texinfo package catches a snippet
        // renderer that throws and prints the snippet's source instead, so a render that
        // completed is compatible with every engraving in it having failed.
        if (result.SnippetsAsked > 0 || result.Manual.EngravesSnippets)
        {
            context.IO.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "snippets: {0} asked, {1} engraved, {2} pictures, {3} failed, {4} declined",
                result.SnippetsAsked, result.SnippetsEngraved, result.Pictures,
                result.SnippetFailures, result.SnippetDeclines));
            foreach (SnippetFailure failure in result.Failures)
            {
                context.IO.WriteLine("  FAILED  " + failure);
            }
        }
    }
}

/// <summary>The parsed form of a <c>docs</c> command line.</summary>
/// <remarks>
/// Split out from the command so that what the shell accepts can be gated without a
/// forty-second generation and a six-minute render behind it.
/// </remarks>
internal sealed class DocsCommandLine
{
    private DocsCommandLine()
    {
    }

    /// <summary>The message to print instead of running, or null when the line parsed.</summary>
    public string Error { get; private set; }

    /// <summary>True when the line asked for the manual list rather than a render.</summary>
    public bool ListOnly { get; private set; }

    /// <summary>The manual to render, or null when <see cref="ListOnly"/>.</summary>
    public ManualDefinition Manual { get; private set; }

    /// <summary>Whether HTML was asked for.</summary>
    public bool WantHtml { get; private set; } = true;

    /// <summary>Whether PDF was asked for.</summary>
    public bool WantPdf { get; private set; } = true;

    /// <summary>Whether to register the engraver.</summary>
    public bool EngraveSnippets { get; private set; } = true;

    /// <summary>Where to write, or null for the default.</summary>
    public string OutputDirectory { get; private set; }

    /// <summary>Parses a <c>docs</c> command line.</summary>
    /// <param name="arguments">The arguments after the command name.</param>
    /// <returns>The parsed line, with <see cref="Error"/> set when it did not parse.</returns>
    public static DocsCommandLine Parse(IReadOnlyList<string> arguments)
    {
        DocsCommandLine parsed = new DocsCommandLine();
        if (arguments == null || arguments.Count == 0)
        {
            parsed.ListOnly = true;
            return parsed;
        }

        bool html = false;
        bool pdf = false;

        for (int i = 0; i < arguments.Count; i++)
        {
            string argument = arguments[i];
            switch (argument)
            {
                case "--html":
                    html = true;
                    break;
                case "--pdf":
                    pdf = true;
                    break;
                case "--no-snippets":
                    parsed.EngraveSnippets = false;
                    break;
                case "-o":
                case "--output":
                    if (++i >= arguments.Count)
                    {
                        parsed.Error = argument + " needs a directory";
                        return parsed;
                    }

                    parsed.OutputDirectory = Path.GetFullPath(arguments[i]);
                    break;
                default:
                    if (argument.StartsWith("-", StringComparison.Ordinal))
                    {
                        parsed.Error = "unknown option '" + argument + "'";
                        return parsed;
                    }

                    if (parsed.Manual != null)
                    {
                        parsed.Error = "one manual at a time, please ('" + argument + "' is a second)";
                        return parsed;
                    }

                    parsed.Manual = ManualCatalog.Find(argument);
                    if (parsed.Manual == null)
                    {
                        parsed.Error = "unknown manual '" + argument + "'";
                        return parsed;
                    }

                    break;
            }
        }

        // Options with no manual name list the manuals rather than rendering a default one:
        // there is no sensible default among nine, and the cheapest is a forty-second
        // generation.
        if (parsed.Manual == null)
        {
            parsed.ListOnly = true;
            return parsed;
        }

        // With neither format asked for, both — and both is ONE render, which is the whole
        // reason the runner takes the pair rather than being called twice.
        if (html || pdf)
        {
            parsed.WantHtml = html;
            parsed.WantPdf = pdf;
        }

        return parsed;
    }
}
