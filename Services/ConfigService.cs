using System.IO;
using System.Text.Json;

namespace PAAI.Services;

public static class ConfigService
{
    private static readonly string ConfigPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "PAAI", "config.json");

    public static string ApiKey { get; private set; } = "";

    public static void Load()
    {
        try
        {
            if (!File.Exists(ConfigPath)) return;
            var json = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (config != null && config.TryGetValue("ApiKey", out var key))
                ApiKey = key;
        }
        catch { }
    }

    public static void Save(string apiKey)
    {
        ApiKey = apiKey;
        Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath)!);
        var json = JsonSerializer.Serialize(new Dictionary<string, string> { ["ApiKey"] = apiKey });
        File.WriteAllText(ConfigPath, json);
    }

    public static bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
}