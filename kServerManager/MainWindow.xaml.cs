using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using kServerManager;
using kServerManager.Core;

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
        private const int WmGetMinMaxInfo = 0x0024;
        private const int WmNcHitTest = 0x0084;
        private const uint MonitorDefaultToNearest = 2;
        private const int HtLeft = 10;
        private const int HtRight = 11;
        private const int HtTop = 12;
        private const int HtTopLeft = 13;
        private const int HtTopRight = 14;
        private const int HtBottom = 15;
        private const int HtBottomLeft = 16;
        private const int HtBottomRight = 17;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeMinMaxInfo
        {
            public NativePoint Reserved;
            public NativePoint MaxSize;
            public NativePoint MaxPosition;
            public NativePoint MinTrackSize;
            public NativePoint MaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct NativeMonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public uint Flags;
        }

        [DllImport("user32.dll")]
        internal static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WindowCompositionAttributeData data);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref NativeMonitorInfo monitorInfo);

        private static readonly HttpClient httpClient = new();
        private static readonly JdkInstaller jdkInstaller = new();
        readonly static Char directorySeparator = System.IO.Path.DirectorySeparatorChar;
        readonly string sysFormat = CultureInfo.CurrentCulture.DateTimeFormat.ShortDatePattern;

        Process serverProcess = new Process();
        String lastPidPath = "";
        String latestOutput = "";

        String directory = "";
        String launcherConfigPath = "";
        LauncherConfig launcherConfig = new();
        
        String worldName = "";

        String backupsDirectory = "";
        BackupFile[] backups = Array.Empty<BackupFile>();

        String eulaPath = "";

        Dictionary<string, string> serverPropertiesDic =
            new Dictionary<string, string>();

        String publicIP = "";
        int port;

        public MainWindow()
        {
            InitializeComponent();
            
        }

        private void Window_SourceInitialized(object? sender, EventArgs e)
        {
            HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowProc);
        }

        private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == WmGetMinMaxInfo)
            {
                SetMaximizedWorkArea(hwnd, lParam);
                handled = true;
                return IntPtr.Zero;
            }

            if (message != WmNcHitTest || WindowState != WindowState.Normal || ResizeMode == ResizeMode.NoResize)
                return IntPtr.Zero;

            long packedCoordinates = lParam.ToInt64();
            int screenX = unchecked((short)(packedCoordinates & 0xFFFF));
            int screenY = unchecked((short)((packedCoordinates >> 16) & 0xFFFF));
            Point point = PointFromScreen(new Point(screenX, screenY));
            const double resizeBorder = 6;
            bool left = point.X <= resizeBorder;
            bool right = point.X >= ActualWidth - resizeBorder;
            bool top = point.Y <= resizeBorder;
            bool bottom = point.Y >= ActualHeight - resizeBorder;

            int hitTest = (left, right, top, bottom) switch
            {
                (true, _, true, _) => HtTopLeft,
                (_, true, true, _) => HtTopRight,
                (true, _, _, true) => HtBottomLeft,
                (_, true, _, true) => HtBottomRight,
                (true, _, _, _) => HtLeft,
                (_, true, _, _) => HtRight,
                (_, _, true, _) => HtTop,
                (_, _, _, true) => HtBottom,
                _ => 0
            };

            if (hitTest == 0)
                return IntPtr.Zero;

            handled = true;
            return new IntPtr(hitTest);
        }

        private void SetMaximizedWorkArea(IntPtr hwnd, IntPtr minMaxInfoPointer)
        {
            IntPtr monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor == IntPtr.Zero)
                return;

            var monitorInfo = new NativeMonitorInfo { Size = Marshal.SizeOf<NativeMonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref monitorInfo))
                return;

            NativeMinMaxInfo minMaxInfo = Marshal.PtrToStructure<NativeMinMaxInfo>(minMaxInfoPointer);
            minMaxInfo.MaxPosition = new NativePoint
            {
                X = monitorInfo.Work.Left - monitorInfo.Monitor.Left,
                Y = monitorInfo.Work.Top - monitorInfo.Monitor.Top
            };
            minMaxInfo.MaxSize = new NativePoint
            {
                X = monitorInfo.Work.Right - monitorInfo.Work.Left,
                Y = monitorInfo.Work.Bottom - monitorInfo.Work.Top
            };

            DpiScale dpi = VisualTreeHelper.GetDpi(this);
            minMaxInfo.MinTrackSize = new NativePoint
            {
                X = (int)Math.Ceiling(MinWidth * dpi.DpiScaleX),
                Y = (int)Math.Ceiling(MinHeight * dpi.DpiScaleY)
            };

            Marshal.StructureToPtr(minMaxInfo, minMaxInfoPointer, false);
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            EnableBlur();

            directory = ResolveServerDirectory();
            backupsDirectory = directory + directorySeparator + "Backups" + directorySeparator;
            lastPidPath = directory + directorySeparator + "lastPid.txt";
            launcherConfigPath = Path.Combine(directory, "java_config.json");
            launcherConfig = LauncherConfig.Load(launcherConfigPath);
            worldName = GetPropertyValue("level-name");

            CheckCrashed();
            await RefreshServerJarsAsync();
            await StartServerAsync();
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

        private static string ResolveServerDirectory()
        {
            string applicationDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppContext.BaseDirectory));
            var candidates = new List<string>();

            string workingDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Environment.CurrentDirectory));
            candidates.Add(workingDirectory);
            candidates.Add(applicationDirectory);

            DirectoryInfo? parent = Directory.GetParent(applicationDirectory);
            for (int depth = 0; parent is not null && depth < 5; depth++, parent = parent.Parent)
                candidates.Add(parent.FullName);

            foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string configPath = Path.Combine(candidate, "java_config.json");
                LauncherConfig config = LauncherConfig.Load(configPath);
                if (!string.IsNullOrWhiteSpace(config.JarPath) && File.Exists(config.JarPath))
                    return Path.GetDirectoryName(Path.GetFullPath(config.JarPath)) ?? candidate;

                if (LooksLikeServerDirectory(candidate))
                    return candidate;
            }

            return applicationDirectory;
        }

        private static bool LooksLikeServerDirectory(string candidate)
        {
            if (File.Exists(Path.Combine(candidate, "server.jar")) ||
                File.Exists(Path.Combine(candidate, "server.properties")) ||
                File.Exists(Path.Combine(candidate, "eula.txt")))
                return true;

            try
            {
                return Directory.EnumerateFiles(candidate, "*.jar", SearchOption.TopDirectoryOnly).Any();
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }

        async void GetPublicIP()
        {
            while (string.IsNullOrWhiteSpace(publicIP))
            {
                try
                {
                    publicIP = await httpClient.GetStringAsync("https://icanhazip.com");
                }
                catch (HttpRequestException)
                {
                    await Task.Delay(TimeSpan.FromSeconds(1));
                }
            }
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

        private async Task<bool> EnsureServerConfigurationAsync()
        {
            if (ServerJarComboBox.SelectedItem is not ServerJarInfo selectedJar)
            {
                JavaRequirementTextBlock.Text = "Select a server JAR from the list before starting.";
                return false;
            }

            launcherConfig.JarPath = selectedJar.Path;
            int? requiredVersion = selectedJar.MinimumJavaMajor;
            string? selectedJavaPath = null;
            int? selectedJavaVersion = null;

            var candidates = JavaInstallationFinder.Find()
                .Select(installation => installation.Path)
                .ToList();
            if (!string.IsNullOrWhiteSpace(launcherConfig.JavaPath) && File.Exists(launcherConfig.JavaPath))
                candidates.Insert(0, launcherConfig.JavaPath);

            var detectedJava = new List<(string Path, int Major)>();
            foreach (string candidate in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                int? major = await JdkInstaller.GetJavaMajorAsync(candidate);
                if (major is int detectedMajor)
                    detectedJava.Add((candidate, detectedMajor));
            }

            if (requiredVersion is int minimumVersion)
            {
                (string Path, int Major) match = detectedJava
                    .Where(java => java.Major >= minimumVersion)
                    .OrderBy(java => java.Major)
                    .ThenBy(java => java.Path, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();

                if (match.Path is not null)
                {
                    selectedJavaPath = match.Path;
                    selectedJavaVersion = match.Major;
                }
                else
                {
                    MessageBoxResult confirmation = MessageBox.Show(
                        $"This server JAR requires Java {minimumVersion}, but no compatible Java installation was found.\n\nDownload and install Java {minimumVersion} now?",
                        "Java installation required",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);
                    if (confirmation != MessageBoxResult.Yes)
                    {
                        JavaRequirementTextBlock.Text = $"Java {minimumVersion} is required. No server was started.";
                        return false;
                    }

                    try
                    {
                        selectedJavaPath = await jdkInstaller.InstallAsync(minimumVersion);
                        selectedJavaVersion = await JdkInstaller.GetJavaMajorAsync(selectedJavaPath);
                    }
                    catch (Exception error) when (error is IOException or HttpRequestException or InvalidOperationException)
                    {
                        MessageBox.Show(error.Message, "Java installation failed", MessageBoxButton.OK, MessageBoxImage.Error);
                        return false;
                    }
                }
            }
            else if (detectedJava.Count > 0)
            {
                (selectedJavaPath, selectedJavaVersion) = detectedJava[0];
            }
            else
            {
                JavaRequirementTextBlock.Text = "The JAR's Java requirement could not be inferred and no Java installation was found.";
                return false;
            }

            launcherConfig.JavaPath = selectedJavaPath;
            launcherConfig.Save(launcherConfigPath);
            JavaRequirementTextBlock.Text = requiredVersion is int required
                ? $"Minimum Java inferred from JAR bytecode: {required}. Using installed Java {selectedJavaVersion}."
                : $"Java requirement could not be inferred. Using installed Java {selectedJavaVersion}.";
            return true;
        }

        private async Task RefreshServerJarsAsync()
        {
            RefreshJarsButton.IsEnabled = false;
            JavaRequirementTextBlock.Text = "Inspecting server JAR files…";
            try
            {
                ServerJarInfo[] jars = await Task.Run(() => Directory
                    .EnumerateFiles(directory, "*.jar", SearchOption.TopDirectoryOnly)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .Select(path =>
                    {
                        try
                        {
                            return JavaRequirementDetector.Inspect(path);
                        }
                        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
                        {
                            return new ServerJarInfo(Path.GetFullPath(path), null);
                        }
                    })
                    .ToArray());

                ServerJarComboBox.ItemsSource = jars;
                ServerJarInfo? selected = jars.FirstOrDefault(jar =>
                    string.Equals(jar.Path, launcherConfig.JarPath, StringComparison.OrdinalIgnoreCase));
                if (selected is null && jars.Length == 1)
                    selected = jars[0];

                ServerJarComboBox.SelectedItem = selected;
                if (selected is null)
                    JavaRequirementTextBlock.Text = jars.Length == 0
                        ? $"No .jar files were found in: {directory}"
                        : "Select a server JAR to inspect its Java requirement.";
                else
                    UpdateJavaRequirement(selected);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                ServerJarComboBox.ItemsSource = Array.Empty<ServerJarInfo>();
                JavaRequirementTextBlock.Text = $"Could not read server JAR files: {error.Message}";
            }
            finally
            {
                RefreshJarsButton.IsEnabled = true;
            }
        }

        private void UpdateJavaRequirement(ServerJarInfo jar)
        {
            JavaRequirementTextBlock.Text = jar.MinimumJavaMajor is int version
                ? $"Minimum Java inferred from JAR bytecode: {version}."
                : "Could not infer the minimum Java version from this JAR's class files.";
        }

        private void ServerJarComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ServerJarComboBox.SelectedItem is not ServerJarInfo jar)
                return;

            launcherConfig.JarPath = jar.Path;
            UpdateJavaRequirement(jar);
            launcherConfig.Save(launcherConfigPath);
        }

        private async void RefreshServerJars_Click(object sender, RoutedEventArgs e)
        {
            await RefreshServerJarsAsync();
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

        private async Task StartServerAsync()
        {
            if (IsServerRunning())
                return;

            if (!await EnsureServerConfigurationAsync())
                return;

            try
            {
                serverProcess = new Process
                {
                    StartInfo = ServerProcessFactory.CreateStartInfo(launcherConfig, directory)
                };
                serverProcess.StartInfo.RedirectStandardInput = true;
                serverProcess.StartInfo.RedirectStandardOutput = true;
                serverProcess.StartInfo.CreateNoWindow = true;
                serverProcess.OutputDataReceived += RunOutPut;
                serverProcess.EnableRaisingEvents = true;
                serverProcess.Exited += MyProcess_Exited;
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

        private void MyProcess_Exited(object? sender, System.EventArgs e)
        {
            Dispatcher.Invoke(() => UpdateState(false));
        }

        bool savingPendingForBackup;
        public void RunOutPut(object sender, DataReceivedEventArgs e)
        {
            if (e.Data is not string output)
                return;

            if (savingPendingForBackup && output.EndsWith("Saved the world"))
                MakeBackup();

            Dispatcher.Invoke(() => AppendLine(output));
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

        DispatcherTimer? autoBackupTimer;

        private void OptionalBackup()
        {
            string label = AutoBackupLabel.Content?.ToString() ?? "";
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
            if (BackupsDataGrid.SelectedItem is not BackupFile selectedBackup)
                return;

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
                await StartServerAsync();

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

        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            await StartServerAsync();
        }

        private void RestartButton_Click(object sender, RoutedEventArgs e)
        {
            RestartServer();
        }

        async private void RestartServer()
        {
            if (StopServer())
            {
                while (IsServerRunning())
                    await Task.Delay(250);
            }

            await StartServerAsync();
        }

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton != System.Windows.Input.MouseButton.Left)
                return;

            if (e.ClickCount == 2)
            {
                WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                return;
            }

            if (WindowState == WindowState.Normal)
                DragMove();
        }

        private void Window_StateChanged(object? sender, EventArgs e)
        {
            bool isMaximized = WindowState == WindowState.Maximized;
            MaximizeRestoreGlyph.Text = isMaximized ? "❐" : "□";
            MaximizeRestoreButton.ToolTip = isMaximized ? "Restore" : "Maximize";
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
            Process.Start(new ProcessStartInfo(directory) { UseShellExecute = true });
        }

        private void BackupsDataGrid_CellEditEnding(object sender, System.Windows.Controls.DataGridCellEditEndingEventArgs e)
        {
            String oldName = backups[e.Row.GetIndex()].BackupName;
            if (e.EditingElement is not System.Windows.Controls.TextBox tb)
                return;

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

            if (e.PropertyType == typeof(System.DateTime) && e.Column is DataGridTextColumn column)
                column.Binding.StringFormat = sysFormat + " HH:mm:ss";
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
