using System.IO.Compression;

namespace kServerManager.Core;

public sealed record ServerJarInfo(string Path, int? MinimumJavaMajor)
{
    public string DisplayName => MinimumJavaMajor is int version
        ? Localization.Get("JarDisplayWithJava", System.IO.Path.GetFileName(Path), version)
        : Localization.Get("JarDisplayJavaUnknown", System.IO.Path.GetFileName(Path));
}

public static class JavaRequirementDetector
{
    public static ServerJarInfo Inspect(string jarPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jarPath);
        string fullPath = System.IO.Path.GetFullPath(jarPath);
        int? minimumJavaMajor = null;

        using FileStream file = File.OpenRead(fullPath);
        using var archive = new ZipArchive(file, ZipArchiveMode.Read);
        minimumJavaMajor = GetMinimumJavaMajor(archive);

        if (IsFabricServerLauncher(archive))
        {
            string serverJarPath = Path.Combine(Path.GetDirectoryName(fullPath)!, "server.jar");
            if (File.Exists(serverJarPath))
            {
                using FileStream serverFile = File.OpenRead(serverJarPath);
                using var serverArchive = new ZipArchive(serverFile, ZipArchiveMode.Read);
                int? serverJavaMajor = GetMinimumJavaMajor(serverArchive);
                if (serverJavaMajor is int serverMajor)
                    minimumJavaMajor = Math.Max(minimumJavaMajor ?? serverMajor, serverMajor);
            }
        }

        return new ServerJarInfo(fullPath, minimumJavaMajor);
    }

    private static int? GetMinimumJavaMajor(ZipArchive archive)
    {
        Span<byte> header = stackalloc byte[8];
        int? minimumJavaMajor = null;
        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith(".class", StringComparison.OrdinalIgnoreCase) ||
                IsMultiReleaseEntry(entry.FullName))
                continue;

            using Stream classFile = entry.Open();
            header.Clear();
            if (!TryReadExactly(classFile, header) ||
                header[0] != 0xCA || header[1] != 0xFE || header[2] != 0xBA || header[3] != 0xBE)
                continue;

            int classFileMajor = (header[6] << 8) | header[7];
            if (classFileMajor < 45)
                continue;

            int javaMajor = classFileMajor == 45 ? 1 : classFileMajor - 44;
            minimumJavaMajor = Math.Max(minimumJavaMajor ?? javaMajor, javaMajor);
        }

        return minimumJavaMajor;
    }

    private static bool IsFabricServerLauncher(ZipArchive archive)
    {
        ZipArchiveEntry? manifest = archive.GetEntry("META-INF/MANIFEST.MF");
        ZipArchiveEntry? launcherProperties = archive.GetEntry("fabric-server-launch.properties");
        if (manifest is null || launcherProperties is null)
            return false;

        using Stream manifestStream = manifest.Open();
        using var reader = new StreamReader(manifestStream);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (line.Equals(
                    "Main-Class: net.fabricmc.loader.impl.launch.server.FabricServerLauncher",
                    StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool IsMultiReleaseEntry(string entryName) =>
        entryName.StartsWith("META-INF/versions/", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadExactly(Stream stream, Span<byte> buffer)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = stream.Read(buffer[offset..]);
            if (read == 0)
                return false;
            offset += read;
        }

        return true;
    }
}
