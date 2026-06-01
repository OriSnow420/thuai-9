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

    public static Dictionary<string, string> LoadPlayerNames(TokenSettings settings)
    {
        var playerNames = new Dictionary<string, string>(StringComparer.Ordinal);
        string? raw = Environment.GetEnvironmentVariable(settings.PlayerNameLocation);
        if (string.IsNullOrWhiteSpace(raw))
            return playerNames;

        raw = raw.Trim();
        if (raw.StartsWith("{", StringComparison.Ordinal))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(raw, JsonOptions);
                if (parsed != null)
                {
                    foreach (var (token, displayName) in parsed)
                        AddPlayerName(playerNames, token, displayName);

                    return playerNames;
                }
            }
            catch (JsonException)
            {
            }
        }

        foreach (var entry in raw.Split(
            new[] { settings.TokenDelimiter, "\r\n", "\n", "\r" },
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = entry.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0 || separatorIndex >= entry.Length - 1)
                continue;

            AddPlayerName(
                playerNames,
                entry[..separatorIndex],
                entry[(separatorIndex + 1)..]);
        }

        return playerNames;
    }

    private static void AddPlayerName(Dictionary<string, string> playerNames, string token, string? displayName)
    {
        if (string.IsNullOrWhiteSpace(token))
            return;

        var normalized = GameLogic.Player.NormalizeDisplayName(displayName);
        if (normalized == null)
            return;

        playerNames[token.Trim()] = normalized;
    }
}
