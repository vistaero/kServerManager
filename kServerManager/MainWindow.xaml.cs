using System;
using System.Threading.Tasks;
using System.Windows;
using System.Diagnostics;
using System.IO.Compression;
using System.ComponentModel;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Net.Sockets;
using System.Windows.Media;
using System.Globalization;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Threading;

namespace BackupAndStart
{
    internal enum AccentState
    {
        ACCENT_DISABLED = 0,
        ACCENT_ENABLE_GRADIENT = 1,
        ACCENT_ENABLE_TRANSPARENTGRADIENT = 2,
        ACCENT_ENABLE_BLURBEHIND = 3,
        ACCENT_INVALID_STATE = 4

    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AccentPolicy
    {
        public AccentState AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;

    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowCompositionAttributeData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;

    }

    internal enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19

    }

    [StructLayout(LayoutKind.Sequential)]
    public class MARGINS
    {
        public int cxLeftWidth, cxRightWidth, cyTopHeight, cyBottomHeight;

    }

    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        readonly static Char directorySeparator = System.IO.Path.DirectorySeparatorChar;
        string sysFormat = CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern;

        Process serverProcess = new Process();
        String lastPidPath;
        String latestOutput = "";

        String batPath;
        String directory;
        
        String worldName;

        String backupsDirectory;
        BackupFile[] backups;

        String eulaPath;

        Dictionary<string, string> serverPropertiesDic =
            new Dictionary<string, string>();

        String publicIP;
        int port;

        public MainWindow()
        {
            InitializeComponent();
            
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            EnableBlur();

            directory = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            backupsDirectory = directory + directorySeparator + "Backups" + directorySeparator;
            lastPidPath = directory + directorySeparator + "lastPid.txt";
            worldName = GetPropertyValue("level-name");

            CheckCrashed();
            CheckBat();
            StartServer(batPath);
            ListBackups();
            OptionalBackup();
            GetPublicIP();
            CheckPort();
            LoadServerIcon();

        }

        void LoadServerIcon()
        {
            string serverIconPath = directory + directorySeparator + @"server-icon.png";

            if (!System.IO.File.Exists(serverIconPath))
            {
                ServerImage.Visibility = Visibility.Collapsed;
                return;
            }

            ServerImage.Source = new BitmapImage(new Uri(serverIconPath));
        }

        async void GetPublicIP()
        {
            await Task.Run(() =>
            {
                do
                {
                    try
                    {
                        publicIP = new System.Net.WebClient().DownloadString("http://icanhazip.com");
                    }
                    catch
                    {

                    }
                } while (publicIP == null);
                
            });
            String tempIP = "";
            foreach(Char c in publicIP)
            {
                if (!((int)c).Equals(10))
                {
                    tempIP += c;

                }
            }
            publicIP = tempIP;
            IPButton.Content = "IP: " + publicIP + Environment.NewLine + "Port: " + port.ToString();
            IPButton.IsEnabled = true;

        }

        async void CheckPort()
        {
            int.TryParse(GetPropertyValue("server-port"), out port);

            await Task.Run(() =>
            {
                while (true)
                {
                    try
                    {
                        TcpClient client = new TcpClient(publicIP, port);
                        Dispatcher.Invoke(() => ColorizeIPButton(true));
                    }
                    catch
                    {
                        Dispatcher.Invoke(() => ColorizeIPButton(false));
                    }
                    Task.Delay(5000);
                }

            });
        }

        void ColorizeIPButton(bool isVisible)
        {
            if (isVisible)
                IPButton.Foreground = Brushes.LightGreen;
            else
                IPButton.Foreground = Brushes.IndianRed;
        }

        void MsgBox(string message)
        {
            MessageBox.Show(message, "Debug Box", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        
        void CheckCrashed()
        {
            if (System.IO.File.Exists(lastPidPath)){
                String lastPid = System.IO.File.ReadAllText(lastPidPath);
                foreach (Process item in Process.GetProcesses())
                {
                    if (item.Id.ToString().Equals(lastPid))
                    {
                        MessageBoxResult result = MessageBox.Show("The server we ran the last time is still running, but kServer Manager was forced to stop and we couldn't stop the server.\n\nPlease connect to the server using Minecraft and use the command to stop.\n\nIf you can't, you can force the server to stop pressing OK.\n\nIf you don't have an auto-save feature enabled, you may experience a rollback.", "kServer Manager has lose control of the server", MessageBoxButton.OKCancel, MessageBoxImage.Exclamation);
                        if (result == MessageBoxResult.OK)
                        {
                            item.Kill();
                        }
                        else
                            Environment.Exit(0);
                    }
                }
            }
        }

        void CheckBat()
        {
            batPath = SearchFile(".bat");
            if (batPath.Equals(""))
            {
                String jarName = SearchJar();
                if (jarName.Equals(""))
                {
                    MessageBox.Show("No .jar found. Can't start server.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Environment.Exit(0);
                }
                String command = "java -Xmx1024M -Xms1024M -jar \"" + jarName + "\" nogui";
                batPath = directory + directorySeparator + "start.bat";
                System.IO.File.WriteAllText(batPath, command);
                
            }
        }

        private String SearchJar()
        {
            String[] jars = System.IO.Directory.GetFiles(directory, "*.jar");

            if (jars.Length == 1)
            {
                return System.IO.Path.GetFileName(jars[0]);
            }
            else
            {
                MessageBox.Show("There are various .jar files. Please select the one you need to open. Proceed with caution. The wrong .jar can corrupt your world.", "Multiple Jar Files", MessageBoxButton.OK, MessageBoxImage.Warning);

                Microsoft.Win32.OpenFileDialog dlg = new Microsoft.Win32.OpenFileDialog();
                dlg.FileName = "minecraft_server";
                dlg.DefaultExt = ".jar";
                dlg.Filter = "Java Application (.jar)|*.jar";
                dlg.InitialDirectory = directory;
                Nullable<bool> result = dlg.ShowDialog();

                if (result == true)
                    return dlg.SafeFileName;

            }

            return "";
            
        }

        private String CheckJava()
        {
            try
            {
                Process.Start("java");
                return "java";
            }catch{}

            string programFilesX86 = Environment.ExpandEnvironmentVariables("%ProgramFiles(x86)%");
            String minecraftRuntimePath = programFilesX86 + directorySeparator + "Minecraft" + directorySeparator + "runtime";
            if (System.IO.Directory.Exists(minecraftRuntimePath))
            {
                String[] exeFiles = System.IO.Directory.GetFiles(minecraftRuntimePath, "java.exe", System.IO.SearchOption.AllDirectories);
                if (exeFiles.Length > 0)
                return exeFiles[exeFiles.Length - 1];
            }

            MessageBox.Show("Can't find Java runtime. Please install Java or Minecraft to start a server.", "Can't find runtime", MessageBoxButton.OK, MessageBoxImage.Error);
            return "";

        }

        internal void EnableBlur()
        {
            if (Environment.OSVersion.Version.Major.Equals(10))
            {
                var windowHelper = new WindowInteropHelper(this);
                var accent = new AccentPolicy();
                accent.AccentState = AccentState.ACCENT_ENABLE_BLURBEHIND;
                var accentStructSize = Marshal.SizeOf(accent);
                var accentPtr = Marshal.AllocHGlobal(accentStructSize);
                Marshal.StructureToPtr(accent, accentPtr, false);
                var data = new WindowCompositionAttributeData();
                data.Attribute = WindowCompositionAttribute.WCA_ACCENT_POLICY;
                data.SizeOfData = accentStructSize;
                data.Data = accentPtr;
                SetWindowCompositionAttribute(windowHelper.Handle, ref data);
                Marshal.FreeHGlobal(accentPtr);

                return;

            }

        }

        private void UpdateState(bool running)
        {
            String runningState;
            if (running)
            {
                runningState = " (Running) ";
                StartButton.Visibility = Visibility.Collapsed;
                StopButton.Visibility = Visibility.Visible;
                RestartButton.Visibility = Visibility.Visible;
            }
            else
            {
                runningState = "";
                StartButton.Visibility = Visibility.Visible;
                StopButton.Visibility = Visibility.Collapsed;
                RestartButton.Visibility = Visibility.Collapsed;
                serverProcess = new Process();
            }

            String title = System.IO.Path.GetFileName(directory) + runningState + " - vistaero kServer Manager"; ;
            TitleLabel.Content = title;
            Title = title;
        }

        void CheckEula()
        {
            eulaPath = directory + directorySeparator + "eula.txt";

            if (!System.IO.File.Exists(eulaPath))
            {
                MessageBox.Show("There's no eula.txt file yet. Start the server to generate it.");
                return;
            }
                
            String[] eulaContent = System.IO.File.ReadAllLines("eula.txt");

            for (int i=0; i < eulaContent.Length; i++)
                if (eulaContent[i].StartsWith("eula"))
                {
                    String url = eulaContent[0].Split('(')[1].Split(')')[0];
                    Eula eulaWindow = new Eula(url);
                    eulaWindow.ShowDialog();
                    if (eulaWindow.accept)
                        eulaContent[i] = "eula=true";
                    else
                        eulaContent[i] = "eula=false";
                    System.IO.File.WriteAllLines("eula.txt", eulaContent);

                }
        }

        private String SearchFile(String endsWith)
        {
            String[] files = System.IO.Directory.GetFiles(directory, "*" + endsWith);
            if (files.Length > 0)
            {
                return files[0];
            }
                
            return "";

        }

        bool IsServerRunning()
        {
            if (serverProcess.StartInfo.FileName.Length < 1)
                return false;
            if (serverProcess.HasExited == false)
                return true;
            else
                return false;
        }

        void StartServer(String file)
        {
            if (IsServerRunning())
                return;
            
            try
            {
                String[] lines = System.IO.File.ReadAllLines(file);
                String[] words = lines[0].Split(' ');

                // Configure the process using the StartInfo properties.
                if (words[0].Equals("java"))
                {
                    serverProcess.StartInfo.FileName = CheckJava();
                    if (serverProcess.StartInfo.FileName.Equals(""))
                        Environment.Exit(0);
                }
                else
                {
                    serverProcess.StartInfo.FileName = words[0];
                }

                String arguments = "";

                for (int i = 1; i < words.Length; i++)
                {
                    arguments += words[i] + ' ';
                }
                serverProcess.StartInfo.Arguments = arguments;
                serverProcess.StartInfo.WorkingDirectory = directory;
                serverProcess.StartInfo.UseShellExecute = false;
                serverProcess.StartInfo.RedirectStandardInput = true;
                serverProcess.StartInfo.RedirectStandardOutput = true;
                serverProcess.StartInfo.CreateNoWindow = true;
                serverProcess.OutputDataReceived += RunOutPut;
                serverProcess.EnableRaisingEvents = true;
                serverProcess.Exited += new EventHandler(MyProcess_Exited);
                serverProcess.Start();
                serverProcess.BeginOutputReadLine();
                System.IO.File.WriteAllText(lastPidPath, serverProcess.Id.ToString());
                UpdateState(true);

            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            
        }

        private void MyProcess_Exited(object sender, System.EventArgs e)
        {
            Dispatcher.Invoke(() => UpdateState(false));
        }

        bool savingPendingForBackup;
        public void RunOutPut(object sender, DataReceivedEventArgs e)
        {
            if (savingPendingForBackup && e.Data.EndsWith("Saved the world"))
                MakeBackup();

            Dispatcher.Invoke(() => AppendLine(e.Data));
        }
        
        private void AppendLine(String line)
        {
            ConsoleOutput.AppendText(line + Environment.NewLine);
            ConsoleOutput.ScrollToEnd();
            latestOutput = line;
        }

        private void BackupButton_Click(object sender, RoutedEventArgs e)
        {
            if (IsServerRunning())
            {
                serverProcess.StandardInput.WriteLine("save-all");
                savingPendingForBackup = true;
            }
            else
            {
                MakeBackup();
            }
        }

        private void ReadServerProperties()
        {
            String propertiesPath = SearchFile("server.properties");
            
            if (!propertiesPath.Equals(""))
            {
                String[] propertiesContent = System.IO.File.ReadAllLines(propertiesPath);
                foreach (String line in propertiesContent)
                {
                    String[] keyAndValue = line.Split('=');
                    if (keyAndValue.Length > 1)
                    serverPropertiesDic.Add(keyAndValue[0], keyAndValue[1]);
                    
                }
            }
        }

        private String GetPropertyValue(String property)
        {
            if (serverPropertiesDic.Count < 1)
                ReadServerProperties();

            if (serverPropertiesDic.ContainsKey(property))
                return serverPropertiesDic[property];
            else
                return "";
        }

        DispatcherTimer autoBackupTimer;

        private void OptionalBackup()
        {
            string label = AutoBackupLabel.Content.ToString();
            AutoBackupLabel.Content = label + " 10";
            int i = 10;
            //INSTANCIANDO EL TIMER CON LA CLASE DISPATCHERTIMER 
            autoBackupTimer = new DispatcherTimer();

            //EL INTERVALO DEL TIMER ES DE 0 HORAS,0 MINUTOS Y 1 SEGUNDO 
            autoBackupTimer.Interval = new TimeSpan(0, 0, 1);

            //EL EVENTO TICK SE SUBSCRIBE A UN CONTROLADOR DE EVENTOS UTILIZANDO LAMBDA 
            autoBackupTimer.Tick += (s, a) =>
            {
                //AQUI VA LO QUE QUIERES QUE HAGA CADA 1 SEGUNDO 
                
                if (i == 0)
                {
                    /*kServerManager.TextForm textForm = new kServerManager.TextForm();
                    textForm.ShowDialog();
                    if (textForm.returnValue.Length > 0)
                    {
                        MakeBackup(textForm.returnValue);
                        MsgBox(textForm.returnValue);
                    }

                    else
                    {*/
                        MakeBackup();
                        StopAutoBackupTimer();
                    //}
                        
                }else
                    AutoBackupLabel.Content = label + " " + (i -= 1).ToString();

            };
            autoBackupTimer.Start();
            
        }

        private void MakeBackup(string name = "default")
        {
            int latestIndex = 0;
            
            foreach (BackupFile file in backups)
            {
                int index = 0;

                try
                {
                    String currentIndex = file.BackupName.Remove(0, worldName.Length + 1);
                    int.TryParse(currentIndex, out index);
                    if (index > latestIndex)
                    {
                        latestIndex = index;
                    }
                }
                catch(Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }

            }

            if (name.Equals("default"))
                CompressFolder(directory + directorySeparator + worldName, backupsDirectory + directorySeparator + worldName + "." + (latestIndex + 1));
            else
                CompressFolder(directory + directorySeparator + worldName, backupsDirectory + directorySeparator + name + "." + (latestIndex + 1));

        }

        private void ListBackups()
        {
            if (!System.IO.Directory.Exists(backupsDirectory))
                System.IO.Directory.CreateDirectory(backupsDirectory);

            string[] currentBackups = System.IO.Directory.GetFiles(backupsDirectory, "*.*.zip");
            backups = new BackupFile[currentBackups.Length];
            BackupsDataGrid.ItemsSource = backups;

            for (int i = 0; i< currentBackups.Length; i++)
            {
                BackupFile backupTest = new BackupFile(currentBackups[i]);
                backups[i] = backupTest;
                
            }
            BackupsDataGrid.Items.Refresh();
            BackupsDataGrid.SelectionUnit = System.Windows.Controls.DataGridSelectionUnit.FullRow;
   
        }
        
        async void CompressFolder(string folder, string targetFilename)
        {
            try
            {
                
                BackupButton.IsEnabled = false;
                BackupButton.Content = "Backing Up";

                await Task.Run(() =>
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.CopyDirectory(folder, targetFilename);
                });

                await Task.Run(() =>
                 {
                ZipFile.CreateFromDirectory(targetFilename, targetFilename + ".zip");
                });

                await Task.Run(() =>
                {
                    System.IO.Directory.Delete(targetFilename, true);
                });

                BackupButton.IsEnabled = true;

                BackupButton.Content = "Back Up";
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }

            ListBackups();

        }

        private void ConsoleInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter && IsServerRunning())
            {
                e.Handled = true;
                serverProcess.StandardInput.WriteLine(ConsoleInput.Text);
                ConsoleInput.Text = "";
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            foreach(BackupFile item in BackupsDataGrid.SelectedItems)
            {
                DeleteButton.Content = "Deleting...";
                String fileToDelete = backupsDirectory + directorySeparator + item.BackupName + ".zip";
                try
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(fileToDelete, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
                catch(Exception ex)
                {
                    MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                
            }
            DeleteButton.Content = "Delete";
            BackupsDataGrid.SelectedIndex = -1;
            ListBackups();

        }

        private void BackupsDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (BackupsDataGrid.SelectedItems.Count > 1)
            {
                RestoreButton.IsEnabled = false;

            }
            else
            {
                if (BackupsDataGrid.SelectedIndex > -1)
                {
                    RestoreButton.IsEnabled = true;
                    DeleteButton.IsEnabled = true;
                }
                else
                {
                    RestoreButton.IsEnabled = false;
                    DeleteButton.IsEnabled = false;
                }
            }
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            BackupFile selectedBackup = BackupsDataGrid.SelectedItem as BackupFile;
            RestoreBackup(backupsDirectory + directorySeparator + selectedBackup.BackupName + ".zip");
            
            BackupsDataGrid.SelectedIndex = -1;
        }

        async private void RestoreBackup(String file)
        {
            RestoreButton.Content = "Restoring...";
            RestoreButton.IsEnabled = false;
            bool wasRunning = StopServer();

            await Task.Run(() =>
            {
                do
                {
                    if (!IsServerRunning())
                    {
                        String directoryToDelete = directory + directorySeparator + worldName;
                        try
                        {
                            if (System.IO.Directory.Exists(directoryToDelete))
                            {
                                Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                                    directoryToDelete,
                                    Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                                    Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                            }

                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show(ex.Message);
                            Dispatcher.Invoke(() => RestoreButton.Content = "Restore");
                        }
                        break;
                    }

                } while (true);
            });

            await Task.Run(() =>
            {
                ZipFile.ExtractToDirectory(file, directory + directorySeparator + worldName);
            });

            if (wasRunning)
                StartServer(batPath);

            RestoreButton.Content = "Restore";
        }

        private bool StopServer()
        {
            if (IsServerRunning())
            {
                serverProcess.StandardInput.WriteLine("stop");
                return true;
            }
            return false;
        }

        private void ConsoleButton_Click(object sender, RoutedEventArgs e)
        {
            ConsoleButton.IsEnabled = false;
            BackupsButton.IsEnabled = true;
            ConsolePanel.Visibility = Visibility.Visible;
            BackupsPanel.Visibility = Visibility.Collapsed;
        }

        private void BackupsButton_Click(object sender, RoutedEventArgs e)
        {
            BackupsButton.IsEnabled = false;
            ConsoleButton.IsEnabled = true;
            ConsolePanel.Visibility = Visibility.Collapsed;
            BackupsPanel.Visibility = Visibility.Visible;
        }

        private void StopButton_Click(object sender, RoutedEventArgs e)
        {
            StopServer();
        }

        private void StartButton_Click(object sender, RoutedEventArgs e)
        {
            StartServer(batPath);
        }

        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            RestartServer();
        }

        async private void RestartServer()
        {
            StopServer();

            await Task.Run(() =>
            {
                do
                {
                    if (!IsServerRunning())
                    {
                        Dispatcher.Invoke(() => StartServer(batPath));
                        break;
                    }

                } while (true);
            });
            
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)

                this.DragMove();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();

        }
        
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void FolderButton_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(directory);
        }

        private void BackupsDataGrid_CellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
        {
            String oldName = backups[e.Row.GetIndex()].BackupName;
            var tb = e.EditingElement as System.Windows.Controls.TextBox;
            String newName = tb.Text;
            try
            {
                if (!oldName.Equals(newName))
                    Microsoft.VisualBasic.FileIO.FileSystem.RenameFile(backupsDirectory + directorySeparator + oldName + ".zip", newName + ".zip");
            }catch(Exception ex)
            {
                MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                tb.Text = oldName;
            }
        }

        private void BackupsDataGrid_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            
        }

        private void BackupsDataGrid_AutoGeneratingColumn(object sender, DataGridAutoGeneratingColumnEventArgs e)
        {
            switch (e.PropertyName)
            {
                case "BackupName":
                    e.Column.Header = "Name";
                    break;

                case "CreationDate":
                    e.Column.Header = "Date";
                    break;

                default:
                    break;
            }

            if (e.PropertyType == typeof(System.DateTime))
                (e.Column as DataGridTextColumn).Binding.StringFormat = sysFormat + " HH:mm:ss";
        }

        private void IPButton_Click(object sender, RoutedEventArgs e)
        {
            if (port.Equals(25565))
            {
                Clipboard.SetText(publicIP);
            }
            else
            {
                Clipboard.SetText(publicIP + ":" + port);
            }
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (IsServerRunning())
            {
                MessageBoxResult result = MessageBox.Show("Please stop the server before closing this kServer Manager.\n\nIf you continue, you are forcing the server to stop and that can produce unexpected results, like data lose or corruption.\n\nAlthough the server could have an auto-save feature, that does not guarantee that your latest changes are saved.", "Stop before close", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                    e.Cancel = true;
            }
        }

        private void FixEulaButton_Click(object sender, RoutedEventArgs e)
        {
            CheckEula();
        }

        private void CancelAutoBackup_Click(object sender, RoutedEventArgs e)
        {
            StopAutoBackupTimer();
        }

        void StopAutoBackupTimer()
        {
            AutoBackupLabel.Visibility = Visibility.Collapsed;
            CancelAutoBackup.Visibility = Visibility.Collapsed;
            if (autoBackupTimer != null)
                autoBackupTimer.Stop();
        }

    }
}
