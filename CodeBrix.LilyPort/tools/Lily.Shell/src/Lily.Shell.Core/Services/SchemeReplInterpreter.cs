// Copyright (c) 2026 Jeremy Ellis and contributors
//
// Lily.Shell is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.

using Lily.Shell.Kernel;
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Lily.Shell.Services;

/// <summary>
/// The 'scheme' sub-mode: each complete form evaluates against the live
/// engine interpreter and prints its result with `write` conventions. Input
/// with unbalanced parens accumulates under a continuation prompt. 'exit'
/// (or Ctrl+D on an empty line) returns to the shell.
/// </summary>
public sealed class SchemeReplInterpreter : ILineInterpreter
{
    private readonly LilyPortHost _host;
    private readonly StringBuilder _pending = new();

    /// <summary>Creates the REPL over the engine host.</summary>
    public SchemeReplInterpreter(LilyPortHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <inheritdoc/>
    public string Prompt => _pending.Length == 0 ? "scheme> " : "    ..> ";

    /// <inheritdoc/>
    public async Task HandleLineAsync(ShellSession session, string line,
        CancellationToken cancellationToken)
    {
        if (_pending.Length == 0 && line.Trim() is "exit" or "(exit)")
        {
            session.PopInterpreter();
            return;
        }

        _pending.AppendLine(line);
        var source = _pending.ToString();

        if (SchemeSourceScanner.HasOpenForms(source))
        {
            //Mid-expression - keep accumulating under the continuation prompt
            return;
        }

        _pending.Clear();
        if (string.IsNullOrWhiteSpace(source)) { return; }

        try
        {
            var result = await _host.EvaluateSchemeAsync(source, cancellationToken)
                .ConfigureAwait(false);
            //Match REPL etiquette: unspecified results (define, display, set!) print nothing
            if (!string.IsNullOrEmpty(result) && result != "#<unspecified>")
            {
                session.Output.WriteLine(result);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            //Scheme errors carry their own "Scheme error:" style prefix - print as-is
            session.Output.WriteLine(ShellSession.DeepestMessage(ex));
        }
    }
}
