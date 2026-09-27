using System.ComponentModel;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace kServerManager.Core;

public sealed class JdkInstaller
{
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly Regex JavaVersionPattern = new("version \\\"(?:1\\.)?(\\d+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string DefaultInstallDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "kServerManager",
        "jdks");

    public async Task<string> InstallAsync(int majorVersion, string? installDirectory = null, CancellationToken cancellationToken = default)
    {
        if (majorVersion < 1)
            throw new ArgumentOutOfRangeException(nameof(majorVersion), "The JDK version must be a positive integer.");

        string installRoot = Path.GetFullPath(installDirectory ?? DefaultInstallDirectory);
        string javaName = OperatingSystem.IsWindows() ? "java.exe" : "java";

        string? installedHome = await FindInstalledJavaHomeAsync(majorVersion, cancellationToken);
        if (installedHome is not null)
            return Path.Combine(installedHome, "bin", javaName);

        string cachedHome = Path.Combine(installRoot, $"jdk-{majorVersion}");
        string cachedJava = Path.Combine(cachedHome, "bin", javaName);
        if (File.Exists(cachedJava) && await GetJavaMajorAsync(cachedJava, cancellationToken) == majorVersion)
            return cachedJava;

        if (Directory.Exists(cachedHome))
            throw new IOException($"The JDK cache folder exists but does not contain a valid JDK {majorVersion}: {cachedHome}");

        string operatingSystem = GetAdoptiumOperatingSystem();
        string architecture = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "aarch64" : "x64";
        string archiveExtension = OperatingSystem.IsWindows() ? ".zip" : ".tar.gz";
        string downloadUrl = $"https://api.adoptium.net/v3/binary/latest/{majorVersion}/ga/{operatingSystem}/{architecture}/jdk/hotspot/normal/eclipse?project=jdk";

        Directory.CreateDirectory(installRoot);
        string stagingDirectory = Path.Combine(installRoot, $".jdk-{majorVersion}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            string archivePath = Path.Combine(stagingDirectory, $"jdk-{majorVersion}{archiveExtension}");
            using (HttpResponseMessage response = await HttpClient.GetAsync(
                downloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken))
            {
                response.EnsureSuccessStatusCode();
                await using Stream responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using FileStream archive = new(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                await responseStream.CopyToAsync(archive, cancellationToken);
            }

            string extractionDirectory = Path.Combine(stagingDirectory, "extracted");
            Directory.CreateDirectory(extractionDirectory);
            if (OperatingSystem.IsWindows())
            {
                ZipFile.ExtractToDirectory(archivePath, extractionDirectory);
            }
            else
            {
                await using FileStream archive = File.OpenRead(archivePath);
                await using GZipStream gzip = new(archive, CompressionMode.Decompress);
                TarFile.ExtractToDirectory(gzip, extractionDirectory, overwriteFiles: false);
            }

            string? downloadedJava = Directory
                .EnumerateFiles(extractionDirectory, javaName, SearchOption.AllDirectories)
                .FirstOrDefault(path => Path.GetFileName(Path.GetDirectoryName(path)) == "bin");

            if (downloadedJava is null || await GetJavaMajorAsync(downloadedJava, cancellationToken) != majorVersion)
                throw new InvalidDataException($"Downloaded JDK {majorVersion} does not contain a working Java executable of that version.");

            string downloadedHome = Directory.GetParent(Path.GetDirectoryName(downloadedJava)!)!.FullName;
            Directory.Move(downloadedHome, cachedHome);

            string installedJava = Path.Combine(cachedHome, "bin", javaName);
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(installedJava, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);

            return installedJava;
        }
        finally
        {
            if (Directory.Exists(stagingDirectory))
                Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(30) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("kServerManager-JDK-Installer/1.0");
        return client;
    }

    private static string GetAdoptiumOperatingSystem() =>
        OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "mac" : "linux";

    private static async Task<string?> FindInstalledJavaHomeAsync(int requiredMajor, CancellationToken cancellationToken)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
            candidates.Add(Path.GetFullPath(javaHome));

        string javaName = OperatingSystem.IsWindows() ? "java.exe" : "java";
        foreach (JavaInstallation installation in JavaInstallationFinder.Find())
        {
            string? home = Directory.GetParent(Path.GetDirectoryName(installation.Path)!)?.FullName;
            if (home is not null)
                candidates.Add(home);
        }

        if (OperatingSystem.IsMacOS())
        {
            try
            {
                var startInfo = new ProcessStartInfo("/usr/libexec/java_home")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                startInfo.ArgumentList.Add("-v");
                startInfo.ArgumentList.Add(requiredMajor.ToString(System.Globalization.CultureInfo.InvariantCulture));
                using Process? process = Process.Start(startInfo);
                if (process is not null)
                {
                    string output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
                    await process.WaitForExitAsync(cancellationToken);
                    if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                        candidates.Add(output.Trim());
                }
            }
            catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException)
            {
                // Continue with JAVA_HOME, PATH, and the local toolchain cache.
            }
        }

        foreach (string candidate in candidates)
        {
            string executable = Path.Combine(candidate, "bin", javaName);
            if (await GetJavaMajorAsync(executable, cancellationToken) == requiredMajor)
                return candidate;
        }

        return null;
    }

    public static async Task<int?> GetJavaMajorAsync(string executable, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(executable))
            return null;

        try
        {
            var startInfo = new ProcessStartInfo(executable)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-version");

            using Process? process = Process.Start(startInfo);
            if (process is null)
                return null;

            Task<string> standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            Task<string> standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode != 0)
                return null;

            string versionOutput = await standardOutput + "\n" + await standardError;
            Match match = JavaVersionPattern.Match(versionOutput);
            return match.Success && int.TryParse(match.Groups[1].Value, out int major) ? major : null;
        }
        catch (Exception error) when (error is Win32Exception or IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
