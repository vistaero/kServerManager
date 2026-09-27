using System.Text.Json;

namespace kServerManager.Core;

public sealed class LauncherConfig
{
    public string? JavaPath { get; set; }
    public string? JarPath { get; set; }
    public int MaxMemoryGB { get; set; } = 8;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static LauncherConfig Load(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
                return new LauncherConfig();

            LauncherConfig? config = JsonSerializer.Deserialize<LauncherConfig>(File.ReadAllText(configPath), JsonOptions);
            if (config is null)
                return new LauncherConfig();

            if (config.MaxMemoryGB < 1)
                config.MaxMemoryGB = 8;

            return config;
        }
        catch (Exception error) when (error is IOException or JsonException or UnauthorizedAccessException)
        {
            return new LauncherConfig();
        }
    }

    public void Save(string configPath)
    {
        string? parent = Path.GetDirectoryName(configPath);
        if (!string.IsNullOrEmpty(parent))
            Directory.CreateDirectory(parent);

        string temporaryPath = configPath + ".tmp";
        File.WriteAllText(temporaryPath, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(temporaryPath, configPath, overwrite: true);
    }
}
