using System.Diagnostics;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using kServerManager.Properties;

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
                EulaStatusTextBlock.Text = LocalizedStrings.Get("InvalidEulaUrl");
            }
        }

        private async void EulaWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_eulaUri is null)
                return;

            if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 17763))
            {
                EulaStatusTextBlock.Text = LocalizedStrings.Get("WebViewRequiresWindows");
                OpenEulaInBrowserButton.IsEnabled = true;
                return;
            }

            OpenEulaInBrowserButton.IsEnabled = true;
            EulaStatusTextBlock.Text = LocalizedStrings.Get("LoadingEula");
            try
            {
                await EulaWebView.EnsureCoreWebView2Async();
                EulaWebView.CoreWebView2.NavigationCompleted += EulaWebView_NavigationCompleted;
                EulaWebView.Source = _eulaUri;
            }
            catch (Exception error)
            {
                EulaStatusTextBlock.Text = LocalizedStrings.Get("WebViewStartFailed", error.Message);
            }
        }

        private async void EulaWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (!e.IsSuccess)
            {
                EulaStatusTextBlock.Text = LocalizedStrings.Get("EulaLoadFailed", e.WebErrorStatus);
                return;
            }

            if (_enableDelayStarted)
                return;

            _enableDelayStarted = true;
            EulaStatusTextBlock.Text = LocalizedStrings.Get("EulaLoaded");
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
                EulaStatusTextBlock.Text = LocalizedStrings.Get("OpenEulaFailed", error.Message);
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
