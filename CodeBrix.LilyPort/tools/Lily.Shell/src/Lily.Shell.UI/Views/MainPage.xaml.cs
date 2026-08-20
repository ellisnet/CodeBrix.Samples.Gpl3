using CodeBrix.Platform.Simple;
using Lily.Shell.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace Lily.Shell.Views;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        DataContextChanged += (_, _) =>
        {
            //Give the view model's SimpleDialog helpers a XamlRoot to attach dialogs to
            (DataContext as IXamlRootGetter)?.SetXamlRootGetter(() => XamlRoot);
        };

        this.InitializeComponent(); //Leave this line last

        //Bridge wiring: keyboard -> shell, shell output -> terminal, focus on
        //  arrival. Clipboard copy/paste is owned by the TerminalView control.
        Terminal.InputEmitted += data => ViewModel?.OnTerminalInput(data);
        Terminal.Loaded += (_, _) =>
        {
            if (ViewModel is { } viewModel)
            {
                viewModel.FeedToTerminal = Terminal.Feed;
                viewModel.OnTerminalReady();
            }

            Terminal.GrabFocus();
        };
    }

    private MainViewModel ViewModel => DataContext as MainViewModel;
}
