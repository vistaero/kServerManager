using System.Globalization;
using System.Threading;
using System.Windows;

namespace BackupAndStart
{
    /// <summary>
    /// Lógica de interacción para App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            CultureInfo currentCulture = CultureInfo.CurrentCulture;
            CultureInfo currentUiCulture = CultureInfo.CurrentUICulture;

            CultureInfo.DefaultThreadCurrentCulture = currentCulture;
            CultureInfo.DefaultThreadCurrentUICulture = currentUiCulture;
            Thread.CurrentThread.CurrentCulture = currentCulture;
            Thread.CurrentThread.CurrentUICulture = currentUiCulture;

            base.OnStartup(e);
        }
    }
}
