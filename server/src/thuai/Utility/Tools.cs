namespace Thuai.Utility;

using System.Text.Json;

public static class Tools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public static Config LoadOrCreateConfig(string path)
    {
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Config>(json, JsonOptions) ?? new Config();
        }
        else
        {
            Config config = new();
            string dir = Path.GetDirectoryName(path) ?? ".";
            Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(config, JsonOptions));
            return config;
        }
    }

    public static string[] LoadTokens(TokenSettings settings)
    {
        if (settings.LoadTokenFromEnv)
        {
            string? tokenEnv = Environment.GetEnvironmentVariable(settings.TokenLocation);
            if (string.IsNullOrEmpty(tokenEnv))
                return Array.Empty<string>();
            return tokenEnv.Split(settings.TokenDelimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
        else
        {
            if (!File.Exists(settings.TokenLocation))
                return Array.Empty<string>();
            string content = File.ReadAllText(settings.TokenLocation);
            return content.Split(settings.TokenDelimiter, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }
    }
}
