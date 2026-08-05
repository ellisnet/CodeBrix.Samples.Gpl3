using CodeBrix.Platform.Simple;
using Lily.Shell.ViewModels;
using Microsoft.UI.Xaml.Controls;
using Windows.ApplicationModel.DataTransfer;

namespace Lily.Shell.Views;

public sealed partial class MainPage : Page
{
    public MainPage()
    {
        DataContextChanged += (_, _) =>
        {
            //Give the view model's SimpleDialog helpers a XamlRoot to attach dialogs to
            (DataContext as IXamlRootGetter)?.SetXamlRootGetter(() => XamlRoot);

            if (DataContext is ICopyToClipboard copy)
            {
                copy.CopyTextToClipboard = (text) =>
                {
                    if (!string.IsNullOrEmpty(text))
                    {
                        var clipData = new DataPackage();
                        clipData.SetText(text);
                        Clipboard.SetContent(clipData);
                    }
                };
            }
        };

        this.InitializeComponent(); //Leave this line last

        //Bridge wiring: keyboard -> shell, shell output -> terminal, focus on arrival
        Terminal.InputEmitted += data => ViewModel?.OnTerminalInput(data);
        Terminal.CopyRequested += text => ViewModel?.OnTerminalCopyRequested(text);
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
