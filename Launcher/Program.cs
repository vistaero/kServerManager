using System.ComponentModel;
using System.Diagnostics;
using kServerManager.Core;

namespace kServerManager.Launcher;

internal static class Program
{
    private static readonly string ServerDirectory = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
    private static readonly string ConfigPath = Path.Combine(ServerDirectory, "java_config.json");
    private static readonly int[] DefaultJdkVersions = [8, 17, 21, 25];
    private static readonly JdkInstaller JdkInstaller = new();
    private static LauncherConfig _config = new();

    private static async Task<int> Main(string[] args)
    {
        if (args.Length > 0)
        {
            if (args[0].Equals("install-jdks", StringComparison.OrdinalIgnoreCase))
                return await InstallJdksCommandAsync(args[1..]);

            if (args[0] is "--help" or "-h" or "/?")
            {
                ShowHelp();
                return 0;
            }

            Console.Error.WriteLine(Localization.Get("UnknownCommand", args[0]));
            ShowHelp();
            return 2;
        }

        _config = LauncherConfig.Load(ConfigPath);
        return await RunMenuAsync();
    }

    private static void ShowHelp()
    {
        Console.WriteLine(Localization.Get("LauncherName"));
        Console.WriteLine(Localization.Get("HelpNoArgs"));
        Console.WriteLine(Localization.Get("HelpInstallJdks"));
        Console.WriteLine(Localization.Get("HelpInstallJdksFlags"));
        Console.WriteLine(Localization.Get("HelpDownloadJdks"));
        Console.WriteLine(Localization.Get("HelpDefaultJdks"));
    }

    private static async Task<int> InstallJdksCommandAsync(string[] args)
    {
        string installDirectory = JdkInstaller.DefaultInstallDirectory;
        var versions = new List<int>();

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i].Equals("--output", StringComparison.OrdinalIgnoreCase) ||
                args[i].Equals("--output-directory", StringComparison.OrdinalIgnoreCase))
            {
                if (++i >= args.Length)
                {
                    Console.Error.WriteLine(Localization.Get("OutputPathRequired"));
                    return 2;
                }
                installDirectory = Path.GetFullPath(args[i]);
                continue;
            }

            if (args[i].Equals("--jdk-versions", StringComparison.OrdinalIgnoreCase))
            {
                int firstVersion = ++i;
                while (i < args.Length && !args[i].StartsWith("--", StringComparison.Ordinal))
                    i++;
                if (firstVersion == i)
                {
                    Console.Error.WriteLine(Localization.Get("JdkVersionsRequired"));
                    return 2;
                }
                i--;
                for (int versionIndex = firstVersion; versionIndex <= i; versionIndex++)
                {
                    if (!int.TryParse(args[versionIndex], out int jdkMajor) || jdkMajor < 1)
                    {
                        Console.Error.WriteLine(Localization.Get("InvalidJdkVersion", args[versionIndex]));
                        return 2;
                    }
                    versions.Add(jdkMajor);
                }
                continue;
            }

            if (!int.TryParse(args[i], out int version) || version < 1)
            {
                Console.Error.WriteLine(Localization.Get("InvalidJdkVersion", args[i]));
                return 2;
            }

            versions.Add(version);
        }

        if (versions.Count == 0)
        {
            Console.Write(Localization.Get("PromptJdkVersions"));
            string? input = Console.ReadLine();
            if (!string.IsNullOrWhiteSpace(input))
            {
                foreach (string part in input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!int.TryParse(part, out int version) || version < 1)
                    {
                        Console.Error.WriteLine(Localization.Get("PositiveJdkVersions"));
                        return 2;
                    }
                    versions.Add(version);
                }
            }
            else
            {
                versions.AddRange(DefaultJdkVersions);
            }
        }

        return await InstallJdkVersionsAsync(versions, installDirectory);
    }

    private static async Task<int> InstallJdkVersionsAsync(IEnumerable<int> versions, string installDirectory)
    {
        Console.WriteLine(Localization.Get("InstallingUnder", Path.GetFullPath(installDirectory)));
        var results = new List<(int Version, string? JavaPath, Exception? Error)>();
        foreach (int version in versions.Distinct())
        {
            Console.WriteLine(Localization.Get("JdkCheckDownload", version));
            try
            {
                string javaPath = await JdkInstaller.InstallAsync(version, installDirectory);
                results.Add((version, javaPath, null));
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                results.Add((version, null, error));
            }
        }

        foreach ((int version, string? javaPath, Exception? error) in results)
        {
            if (error is null)
                Console.WriteLine(Localization.Get("JdkInstalledPath", version, Path.GetDirectoryName(Path.GetDirectoryName(javaPath)!)!));
            else
                Console.Error.WriteLine(Localization.Get("JdkFailed", version, error.Message));
        }

        return results.Any(result => result.Error is not null) ? 1 : 0;
    }

    private static async Task<int> RunMenuAsync()
    {
        while (true)
        {
            ShowMenu();
            char? selection = await WaitForMenuSelectionAsync();
            string? choice = selection?.ToString();

            if (choice is null)
            {
                Console.Write(Localization.Get("SelectOption"));
                choice = Console.ReadLine();
            }

            switch (choice)
            {
                case "1":
                    await StartServerAsync();
                    break;
                case "2":
                    if (!SelectJar())
                        WaitForEnter();
                    break;
                case "3":
                    if (!await SelectJavaAsync())
                        WaitForEnter();
                    break;
                case "4":
                    if (!SetMaximumMemory())
                        WaitForEnter();
                    break;
                case "5":
                    SyncWithPi();
                    WaitForEnter();
                    break;
                case "6":
                    await InstallFromMenuAsync();
                    break;
                default:
                    Console.WriteLine(Localization.Get("InvalidInput"));
                    await Task.Delay(TimeSpan.FromSeconds(1));
                    break;
            }
        }
    }

    private static void ShowMenu()
    {
        Console.Clear();
        Console.WriteLine("======================================");
        Console.WriteLine(Localization.Get("LauncherTitle"));
        Console.WriteLine("======================================");
        Console.WriteLine(Localization.Get("CurrentJava", DisplayJavaPath(_config.JavaPath)));
        Console.WriteLine(Localization.Get("CurrentJar", DisplayJarPath(_config.JarPath)));
        Console.WriteLine(Localization.Get("CurrentMemory", _config.MaxMemoryGB));
        Console.WriteLine();
        Console.WriteLine(Localization.Get("MenuStartServer"));
        Console.WriteLine(Localization.Get("MenuChangeJar"));
        Console.WriteLine(Localization.Get("MenuChangeJava"));
        Console.WriteLine(Localization.Get("MenuChangeMemory"));
        Console.WriteLine(Localization.Get("MenuSyncPi"));
        Console.WriteLine(Localization.Get("MenuInstallJdks"));
        Console.WriteLine();
    }

    private static string DisplayJavaPath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? Localization.Get("NotSet") : Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(path))) ?? path;

    private static string DisplayJarPath(string? path) =>
        string.IsNullOrWhiteSpace(path) ? Localization.Get("NotSet") : Path.GetFileName(path) ?? path;

    private static async Task<char?> WaitForMenuSelectionAsync()
    {
        Console.WriteLine(Localization.Get("AutoStartCountdown"));
        if (!TryFlushPendingKeys())
        {
            await Task.Delay(TimeSpan.FromSeconds(5));
            return '1';
        }

        for (int remaining = 5; remaining > 0; remaining--)
        {
            Console.WriteLine(Localization.Get("Countdown", remaining));
            for (int tenth = 0; tenth < 10; tenth++)
            {
                try
                {
                    if (Console.KeyAvailable)
                    {
                        ConsoleKeyInfo key = Console.ReadKey(intercept: true);
                        if (key.KeyChar is >= '0' and <= '9')
                        {
                            Console.WriteLine(Localization.Get("InputKey", key.KeyChar));
                            return key.KeyChar;
                        }

                        Console.WriteLine(Localization.Get("AutoStartCanceled"));
                        return null;
                    }
                }
                catch (InvalidOperationException)
                {
                    await Task.Delay(TimeSpan.FromSeconds(5));
                    return '1';
                }

                await Task.Delay(100);
            }
        }

        Console.WriteLine(Localization.Get("AutoStart"));
        return '1';
    }

    private static bool TryFlushPendingKeys()
    {
        try
        {
            while (Console.KeyAvailable)
                _ = Console.ReadKey(intercept: true);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool SelectJar()
    {
        FileInfo[] jars = new DirectoryInfo(ServerDirectory)
            .EnumerateFiles("*.jar", SearchOption.TopDirectoryOnly)
            .OrderBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (jars.Length == 0)
        {
            Console.WriteLine(Localization.Get("NoJarFiles"));
            return false;
        }

        if (jars.Length == 1)
        {
            _config.JarPath = jars[0].FullName;
            SaveConfig();
            Console.WriteLine(Localization.Get("UsingServerJar", jars[0].Name));
            return true;
        }

        Console.WriteLine(Localization.Get("SeveralJarFiles"));
        for (int i = 0; i < jars.Length; i++)
            Console.WriteLine(Localization.Get("JarChoice", i + 1, jars[i].Name));

        int? choice = ReadNumber(Localization.Get("SelectServerNumber"), 1, jars.Length);
        if (choice is null)
        {
            Console.WriteLine(Localization.Get("InvalidInput"));
            return false;
        }

        _config.JarPath = jars[choice.Value - 1].FullName;
        SaveConfig();
        Console.WriteLine(Localization.Get("ServerJarSaved", Path.GetFileName(_config.JarPath)));
        return true;
    }

    private static async Task<bool> SelectJavaAsync()
    {
        IReadOnlyList<JavaInstallation> installs = JavaInstallationFinder.Find();
        if (installs.Count == 0)
        {
            Console.WriteLine(Localization.Get("NoJavaInstallations"));
            return false;
        }

        Console.WriteLine(Localization.Get("DetectedJdks"));
        for (int i = 0; i < installs.Count; i++)
            Console.WriteLine(Localization.Get("JdkChoice", i + 1, installs[i].Name, installs[i].Path));

        int? choice = ReadNumber(Localization.Get("SelectDefaultJdk"), 1, installs.Count);
        if (choice is null)
        {
            Console.WriteLine(Localization.Get("InvalidInput"));
            return false;
        }

        _config.JavaPath = installs[choice.Value - 1].Path;
        SaveConfig();
        Console.WriteLine(Localization.Get("JavaSaved"));
        Console.WriteLine(Localization.Get("JavaSelected", _config.JavaPath));
        await Task.CompletedTask;
        return true;
    }

    private static bool SetMaximumMemory()
    {
        Console.Write(Localization.Get("EnterMaxMemory"));
        if (!int.TryParse(Console.ReadLine(), out int memory) || memory < 1)
        {
            Console.WriteLine(Localization.Get("InvalidInput"));
            return false;
        }

        _config.MaxMemoryGB = memory;
        SaveConfig();
        Console.WriteLine(Localization.Get("MemorySaved"));
        Console.WriteLine(Localization.Get("MemorySet", memory));
        return true;
    }

    private static async Task InstallFromMenuAsync()
    {
        Console.Write(Localization.Get("PromptJdkVersions"));
        string? input = Console.ReadLine();
        int[] versions;

        if (string.IsNullOrWhiteSpace(input))
        {
            versions = DefaultJdkVersions;
        }
        else
        {
            string[] parts = input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var parsed = new List<int>();
            foreach (string part in parts)
            {
                if (!int.TryParse(part, out int version) || version < 1)
                {
                    Console.WriteLine(Localization.Get("PositiveJdkVersions"));
                    return;
                }
                parsed.Add(version);
            }
            versions = parsed.ToArray();
        }

        int result = await InstallJdkVersionsAsync(versions, JdkInstaller.DefaultInstallDirectory);
        if (result != 0)
            Console.WriteLine(Localization.Get("JdkInstallFailed"));
        WaitForEnter();
    }

    private static async Task StartServerAsync()
    {
        if (string.IsNullOrWhiteSpace(_config.JavaPath) || !File.Exists(_config.JavaPath))
        {
            Console.WriteLine(Localization.Get("JavaMissingInvalid"));
            if (!await SelectJavaAsync())
            {
                WaitForEnter();
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(_config.JarPath) || !File.Exists(_config.JarPath))
        {
            Console.WriteLine(Localization.Get("JarMissingInvalid"));
            if (!SelectJar())
            {
                WaitForEnter();
                return;
            }
        }

        if (_config.MaxMemoryGB < 1)
        {
            _config.MaxMemoryGB = 8;
            SaveConfig();
        }

        Console.Clear();
        Console.WriteLine("======================================");
        Console.WriteLine(Localization.Get("LauncherTitle"));
        Console.WriteLine("======================================");
        string javaPath = _config.JavaPath!;
        string jarPath = _config.JarPath!;
        Console.WriteLine(Localization.Get("JavaPathLabel", javaPath));
        Console.WriteLine(Localization.Get("JarPathLabel", Path.GetFileName(jarPath)));
        Console.WriteLine(Localization.Get("RamLabel", _config.MaxMemoryGB));
        Console.WriteLine();
        Console.WriteLine(Localization.Get("RunningJar", Path.GetFileName(jarPath)));
        Console.WriteLine();

        var startInfo = ServerProcessFactory.CreateStartInfo(_config, ServerDirectory);

        try
        {
            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(Localization.Get("JavaProcessNotStarted"));
            await process.WaitForExitAsync();
            Console.WriteLine();
            Console.WriteLine(Localization.Get("ServerStoppedCorrectly"));
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException)
        {
            Console.WriteLine(Localization.Get("StartServerFailed", error.Message));
            WaitForEnter();
            return;
        }

        await WaitForCountdownOrKeyAsync(5, Localization.Get("ReturnMenuCountdown"));
    }

    private static void SyncWithPi()
    {
        Console.Write(Localization.Get("SyncQuestion"));
        string? answer = Console.ReadLine();
        if (string.IsNullOrWhiteSpace(answer) || char.ToLowerInvariant(answer[0]) is not ('s' or 'y'))
            return;

        string local = Path.GetFullPath(ServerDirectory);
        string windowsDirectory = Path.GetFullPath(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
        string driveRoot = Path.GetPathRoot(local) ?? string.Empty;
        if (local.Equals(driveRoot, StringComparison.OrdinalIgnoreCase) ||
            local.Equals(windowsDirectory, StringComparison.OrdinalIgnoreCase) ||
            local.StartsWith(windowsDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine(Localization.Get("SyncSecurity", local));
            return;
        }

        const string remote = "vistaero@192.168.18.22:/home/vistaero/MinecraftServer";
        string sshKey = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ssh", "id_ed25519");
        Console.WriteLine(Localization.Get("LocalPath", local));
        Console.WriteLine(Localization.Get("RemotePath", remote));

        string[] entries = Directory.GetFileSystemEntries(local);
        if (entries.Length == 0)
        {
            Console.WriteLine(Localization.Get("NoFilesToSync"));
            return;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "scp",
            WorkingDirectory = local,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(sshKey);
        startInfo.ArgumentList.Add("-r");
        foreach (string entry in entries)
            startInfo.ArgumentList.Add(entry);
        startInfo.ArgumentList.Add(remote);

        try
        {
            Console.WriteLine(Localization.Get("Syncing"));
            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException(Localization.Get("ScpNotStarted"));
            process.WaitForExit();
            if (process.ExitCode != 0)
                Console.WriteLine(Localization.Get("SyncErrorCode", process.ExitCode));
            else
                Console.WriteLine(Localization.Get("SyncCompleted"));
        }
        catch (Exception error) when (error is Win32Exception or InvalidOperationException or IOException)
        {
            Console.WriteLine(Localization.Get("SyncFailed", error.Message));
        }
    }

    private static async Task WaitForCountdownOrKeyAsync(int seconds, string message)
    {
        Console.WriteLine(message);
        if (!TryFlushPendingKeys())
        {
            await Task.Delay(TimeSpan.FromSeconds(seconds));
            return;
        }

        for (int remaining = seconds; remaining > 0; remaining--)
        {
            Console.WriteLine(Localization.Get("Countdown", remaining));
            for (int tenth = 0; tenth < 10; tenth++)
            {
                try
                {
                    if (Console.KeyAvailable)
                    {
                        _ = Console.ReadKey(intercept: true);
                        Console.WriteLine(Localization.Get("AutoReturnCanceled"));
                        WaitForKey();
                        return;
                    }
                }
                catch (InvalidOperationException)
                {
                    await Task.Delay(TimeSpan.FromSeconds(seconds));
                    return;
                }

                await Task.Delay(100);
            }
        }
    }

    private static int? ReadNumber(string prompt, int minimum, int maximum)
    {
        Console.Write(prompt);
        string? input = Console.ReadLine();
        if (int.TryParse(input, out int choice) && choice >= minimum && choice <= maximum)
            return choice;
        return null;
    }

    private static void SaveConfig() => _config.Save(ConfigPath);

    private static void WaitForEnter()
    {
        Console.Write(Localization.Get("PressEnterToMenu"));
        _ = Console.ReadLine();
    }

    private static void WaitForKey()
    {
        try
        {
            _ = Console.ReadKey(intercept: true);
        }
        catch (InvalidOperationException)
        {
            WaitForEnter();
        }
    }
}
