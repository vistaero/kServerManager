using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace BackupAndStart
{
    /// <summary>
    /// Lógica de interacción para Eula.xaml
    /// </summary>
    public partial class Eula : Window
    {

        public bool accept;

        public Eula(String eulaUrl)
        {
            InitializeComponent();
            WebBrowser1.Source = new Uri(eulaUrl);
            WaitToEnableButtons();

        }

        async private void WaitToEnableButtons()
        {
            await Task.Delay(5000);

            AgreeButton.IsEnabled = true;
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
