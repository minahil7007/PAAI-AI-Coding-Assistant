using System.IO;
using System.Text.Json;

namespace PAAI.Services;

public static class WatchedFoldersService
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PAAI", "watched_folders.json");

    public static List<string> Folders { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            var json = File.ReadAllText(ConfigPath);
            Folders = JsonSerializer.Deserialize<List<string>>(json) ?? new();
        }
        catch { Folders = new(); }
    }

    public static void AddFolder(string path)
    {
        if (!Folders.Contains(path, StringComparer.OrdinalIgnoreCase))
        {
            Folders.Add(path);
            Save();
        }
    }

    public static void RemoveFolder(string path)
    {
        Folders.RemoveAll(f => string.Equals(f, path, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    private static void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        File.WriteAllText(ConfigPath, JsonSerializer.Serialize(Folders));
    }
}