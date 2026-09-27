using System.Diagnostics;

namespace kServerManager.Core;

public static class ServerProcessFactory
{
    public static ProcessStartInfo CreateStartInfo(LauncherConfig config, string workingDirectory)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (string.IsNullOrWhiteSpace(config.JavaPath))
            throw new InvalidOperationException(Localization.Get("JavaNotConfigured"));
        if (string.IsNullOrWhiteSpace(config.JarPath))
            throw new InvalidOperationException(Localization.Get("ServerJarNotConfigured"));
        if (config.MaxMemoryGB < 1)
            throw new InvalidOperationException(Localization.Get("MinimumMemory"));

        var startInfo = new ProcessStartInfo
        {
            FileName = config.JavaPath,
            WorkingDirectory = Path.GetFullPath(workingDirectory),
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add($"-Xmx{config.MaxMemoryGB}G");
        startInfo.ArgumentList.Add($"-Xms{config.MaxMemoryGB}G");
        startInfo.ArgumentList.Add("-jar");
        startInfo.ArgumentList.Add(config.JarPath);
        startInfo.ArgumentList.Add("nogui");
        return startInfo;
    }
}
