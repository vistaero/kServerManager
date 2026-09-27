using System.Diagnostics;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace BackupAndStart
{
    public partial class Eula : Window
    {
        private readonly Uri? _eulaUri;
        private bool _enableDelayStarted;

        public bool accept { get; private set; }

        public Eula(String eulaUrl)
        {
            InitializeComponent();
            if (Uri.TryCreate(eulaUrl, UriKind.Absolute, out Uri? uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                _eulaUri = uri;
            }
            else
            {
                EulaStatusTextBlock.Text = "The EULA URL is invalid. You cannot accept until it can be displayed.";
            }
        }

        private async void EulaWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_eulaUri is null)
                return;

            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            {
                EulaStatusTextBlock.Text = "The embedded browser requires Windows 10 version 1809 or later. Open the EULA in your browser instead.";
                OpenEulaInBrowserButton.IsEnabled = true;
                return;
            }

            OpenEulaInBrowserButton.IsEnabled = true;
            EulaStatusTextBlock.Text = "Loading the Minecraft EULA…";
            try
            {
                await EulaWebView.EnsureCoreWebView2Async();
                EulaWebView.CoreWebView2.NavigationCompleted += EulaWebView_NavigationCompleted;
                EulaWebView.Source = _eulaUri;
            }
            catch (Exception error)
            {
                EulaStatusTextBlock.Text = $"The embedded browser could not start: {error.Message}";
            }
        }

        private async void EulaWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                EulaStatusTextBlock.Text = $"The EULA page could not be loaded ({e.WebErrorStatus}). You can open it in your browser.";
                return;
            }

            if (_enableDelayStarted)
                return;

            _enableDelayStarted = true;
            EulaStatusTextBlock.Text = "EULA loaded. Please review it before agreeing.";
            await Task.Delay(TimeSpan.FromSeconds(5));
            if (IsLoaded)
                AgreeButton.IsEnabled = true;
        }

        private void OpenEulaInBrowserButton_Click(object sender, RoutedEventArgs e)
        {
            if (_eulaUri is null)
                return;

            try
            {
                Process.Start(new ProcessStartInfo(_eulaUri.AbsoluteUri) { UseShellExecute = true });
            }
            catch (Exception error)
            {
                EulaStatusTextBlock.Text = $"Could not open the EULA in your browser: {error.Message}";
            }
        }

        private void DenyButton_Click(object sender, RoutedEventArgs e)
        {
            accept = false;
            Close();
        }

        private void AgreeButton_Click(object sender, RoutedEventArgs e)
        {
            accept = true;
            Close();
        }

    }
}
