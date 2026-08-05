using CodeBrix.LilyPort;
using CodeBrix.Platform.Simple;
using Lily.Shell.Commands;
using Lily.Shell.Kernel;
using Lily.Shell.Kernel.Commands;
using Lily.Shell.Services;
using System;
using System.Diagnostics;

namespace Lily.Shell.ViewModels;

public interface ICopyToClipboard { Action<string> CopyTextToClipboard { get; set; } }

[Microsoft.UI.Xaml.Data.Bindable]
public class MainViewModel : SimpleViewModel, ICopyToClipboard
{
    private const string NormalTitle = "Lily.Shell";
    private const string LoadingTitleBase = "Lily.Shell - loading Scheme ";
    private const int InitialLoadingDots = 3;
    private const int MaxLoadingDots = 40;

    private readonly ShellSession _session;
    private readonly LilyPortHost _host;
    private bool _terminalStarted;
    private bool _engineLoadFinished;
    private Microsoft.UI.Xaml.DispatcherTimer _titleTimer;
    private int _loadingDots;

    public MainViewModel()
    {
        if (IsDesignMode(true)) { return; } //Leave as the first line of constructor

        Debug.WriteLine("Main view model startup.");

        var registry = new CommandRegistry();
        _session = new ShellSession(registry, new ShellSessionOptions
        {
            Prompt = "lily> ",
            Banner =
            [
                $"Lily.Shell for CodeBrix.LilyPort - a port of GNU LilyPond {LilyPortInfo.UpstreamVersion}",
                "Copyright (c) 2026 Jeremy Ellis and contributors. This is free software",
                "under the GNU GPL v3, with ABSOLUTELY NO WARRANTY.",
                "",
                "Loading the LilyPond Scheme layer in the background (~20 s)...",
                "Type 'help' for commands.",
                ""
            ]
        });

        _host = new LilyPortHost(_session);
        _host.LoadFinished += () => InvokeOnMainThread(OnEngineLoadFinished);

        registry.Register(new HelpCommand(registry));
        registry.Register(new ClearCommand());
        registry.Register(new ExitCommand(() => InvokeOnMainThread(
            () => Microsoft.UI.Xaml.Application.Current.Exit())));
        registry.Register(new VersionCommand(_host));
        registry.Register(new SchemeCommand(_host));
        registry.Register(new EngraveCommand(_host));
        registry.Register(new ParseCommand(_host));
        registry.Register(new DemoCommand(_host));
        registry.Register(new IncludeCommand(_host));

        _session.OutputProduced += text => FeedToTerminal?.Invoke(text);
    }

    #region | Bindable properties |

    //No bindable properties yet - the terminal is wired through the bridge region below

    #endregion

    #region | Commands and their implementations |

    //No commands yet...

    #endregion

    #region | Terminal bridge - wired by MainPage code-behind |

    /// <summary>Set by the page to the terminal control's Feed method.</summary>
    public Action<string> FeedToTerminal { get; set; }

    /// <summary>Set by the page to place text on the system clipboard.</summary>
    public Action<string> CopyTextToClipboard { get; set; }

    /// <summary>Routes the terminal's keyboard input into the shell session.</summary>
    public void OnTerminalInput(string data) => _session?.SendInput(data);

    /// <summary>Routes the terminal's copy request (selected text) to the clipboard.</summary>
    public void OnTerminalCopyRequested(string text) => CopyTextToClipboard?.Invoke(text);

    /// <summary>
    /// Called once the terminal control is on screen: starts the shell and
    /// kicks off the background engine load.
    /// </summary>
    public void OnTerminalReady()
    {
        if (_terminalStarted) { return; }
        _terminalStarted = true;

        _session.Start();
        _host.BeginLoading();
        StartLoadingTitle();
    }

    #endregion

    #region | Window-title loading progress |

    private void StartLoadingTitle()
    {
        if (_engineLoadFinished) { return; }

        //The title is a mini progress bar: one more dot every 5 seconds
        _loadingDots = InitialLoadingDots;
        WindowChrome.SetTitle(LoadingTitleBase + new string('.', _loadingDots));

        _titleTimer = new Microsoft.UI.Xaml.DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _titleTimer.Tick += (_, _) =>
        {
            if (_loadingDots >= MaxLoadingDots)
            {
                _titleTimer.Stop();
                return;
            }

            _loadingDots++;
            WindowChrome.SetTitle(LoadingTitleBase + new string('.', _loadingDots));
        };
        _titleTimer.Start();
    }

    private void OnEngineLoadFinished()
    {
        _engineLoadFinished = true;
        _titleTimer?.Stop();
        WindowChrome.SetTitle(NormalTitle);
    }

    #endregion
}
