using System.IO.Compression;

namespace kServerManager.Core;

public sealed record ServerJarInfo(string Path, int? MinimumJavaMajor)
{
    public string DisplayName => MinimumJavaMajor is int version
        ? $"{System.IO.Path.GetFileName(Path)} — Java {version}+"
        : $"{System.IO.Path.GetFileName(Path)} — Java version unknown";
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
        Span<byte> header = stackalloc byte[8];
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

        return new ServerJarInfo(fullPath, minimumJavaMajor);
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
