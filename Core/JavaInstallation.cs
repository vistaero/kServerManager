namespace kServerManager.Core;

public sealed record JavaInstallation(string Name, string Path)
{
    public override string ToString() => $"{Name} — {Path}";
}

public static class JavaInstallationFinder
{
    public static IReadOnlyList<JavaInstallation> Find(string? additionalRoot = null)
    {
        string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string javaName = OperatingSystem.IsWindows() ? "java.exe" : "java";

        string[] roots =
        [
            Path.Combine(programFiles, "Java"),
            Path.Combine(programFiles, "Eclipse Adoptium"),
            Path.Combine(programFiles, "Adoptium"),
            Path.Combine(programFiles, "Microsoft"),
            Path.Combine(programFiles, "Zulu"),
            Path.Combine(programFiles, "Amazon Corretto"),
            Path.Combine(programFiles, "BellSoft"),
            Path.Combine(programFilesX86, "Minecraft", "runtime"),
            Path.Combine(localAppData, "Programs"),
            Path.Combine(userProfile, "scoop", "apps"),
            Path.Combine(userProfile, ".gradle", "jdks"),
            Path.Combine(userProfile, ".sdkman", "candidates", "java"),
            Path.Combine(appData, "ModrinthApp", "meta", "java_versions"),
            Path.Combine(localAppData, "kServerManager", "jdks"),
            additionalRoot ?? JdkInstaller.DefaultInstallDirectory
        ];

        var found = new Dictionary<string, JavaInstallation>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (string javaPath in EnumerateJavaExecutables(root, javaName))
            {
                string? jdkHome = Directory.GetParent(Path.GetDirectoryName(javaPath)!)?.FullName;
                if (jdkHome is null)
                    continue;

                string fullPath = Path.GetFullPath(javaPath);
                found[fullPath] = new JavaInstallation(Path.GetFileName(jdkHome), fullPath);
            }
        }

        foreach (string javaPath in FindJavaOnPath(javaName))
        {
            string fullPath = Path.GetFullPath(javaPath);
            found.TryAdd(fullPath, new JavaInstallation("JAVA in PATH", fullPath));
        }

        return found.Values.OrderBy(item => item.Path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static IEnumerable<string> FindJavaOnPath(string javaName)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(path))
            yield break;

        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string candidate = Path.Combine(directory.Trim('"'), javaName);
            if (File.Exists(candidate))
                yield return candidate;
        }
    }

    private static IReadOnlyList<string> EnumerateJavaExecutables(string root, string javaName)
    {
        if (!Directory.Exists(root))
            return Array.Empty<string>();

        var found = new List<string>();
        var pending = new Stack<(string Directory, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            (string directory, int depth) = pending.Pop();
            try
            {
                string executable = Path.Combine(directory, "bin", javaName);
                if (File.Exists(executable))
                    found.Add(executable);

                if (depth >= 7)
                    continue;

                foreach (string child in Directory.EnumerateDirectories(directory))
                {
                    if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                        pending.Push((child, depth + 1));
                }
            }
            catch (Exception error) when (error is UnauthorizedAccessException or IOException)
            {
                // Skip folders that cannot be inspected and continue searching other known roots.
            }
        }

        return found;
    }
}
