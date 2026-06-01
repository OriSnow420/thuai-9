namespace Thuai.Recorder;

using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using Thuai.GameLogic;
using Thuai.Runtime;

public sealed class PlayerStatisticsWriter
{
    private readonly string _targetFilePath;
    private readonly int _saveIntervalTicks;
    private int _lastSavedTick = -1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    public PlayerStatisticsWriter(
        string recordsDir = "./data",
        string fileName = "player-stats.json",
        int saveIntervalTicks = 100)
    {
        Directory.CreateDirectory(recordsDir);
        _targetFilePath = Path.Combine(recordsDir, fileName);
        _saveIntervalTicks = Math.Max(1, saveIntervalTicks);
    }

    public bool MaybeSave(Game game, IReadOnlyList<PlayerSessionSnapshot> sessions)
    {
        if (_lastSavedTick >= 0 && game.CurrentTick - _lastSavedTick < _saveIntervalTicks)
            return false;

        Save(game, sessions);
        return true;
    }

    public void Save(Game game, IReadOnlyList<PlayerSessionSnapshot> sessions)
    {
        try
        {
            long? midPrice = game.CurrentTradingDay?.OrderBook.MidPrice;
            var sessionByToken = sessions.ToDictionary(session => session.Token, StringComparer.Ordinal);
            var tokens = game.Players.Keys
                .Concat(sessionByToken.Keys)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(token => token, StringComparer.Ordinal)
                .ToList();

            var players = tokens.Select(token =>
            {
                game.Players.TryGetValue(token, out var player);
                sessionByToken.TryGetValue(token, out var session);

                return new PlayerStatisticsSnapshot
                {
                    Token = token,
                    PlayerName = player?.DisplayName,
                    PlayerId = player?.PlayerId,
                    InGame = player != null,
                    Connected = session?.IsConnected ?? false,
                    RemovedFromGame = session?.RemovedFromGame ?? false,
                    Score = game.Scoreboard.GetValueOrDefault(token),
                    CumulativeNav = game.CumulativeNavs.GetValueOrDefault(token),
                    CurrentNav = player != null && midPrice.HasValue ? player.CalculateNAV(midPrice.Value) : null,
                    Mora = player?.Mora,
                    FrozenMora = player?.FrozenMora,
                    Gold = player?.Gold,
                    FrozenGold = player?.FrozenGold,
                    LockedGold = player?.LockedGold,
                    TotalTradeCount = player?.TotalTradeCount,
                    MonthlyTradeCount = player?.MonthlyTradeCount,
                    ActiveCards = player?.ActiveCards.Select(card => card.Name).ToList(),
                    ConnectionCount = session?.ConnectionCount ?? 0,
                    DisconnectCount = session?.DisconnectCount ?? 0,
                    LastConnectedTick = session?.LastConnectedTick,
                    LastDisconnectedTick = session?.LastDisconnectedTick,
                    LastRemovedTick = session?.LastRemovedTick,
                    DisconnectedForTicks = session?.DisconnectedForTicks ?? 0
                };
            }).ToList();

            var snapshot = new PlayerStatisticsDocument
            {
                UpdatedAtUtc = DateTime.UtcNow,
                Stage = game.Stage.ToString(),
                CurrentMonth = game.CurrentMonthNumber,
                CurrentDay = game.CurrentDayNumber,
                CurrentTick = game.CurrentTick,
                ConnectedPlayerCount = players.Count(player => player.Connected),
                InGamePlayerCount = players.Count(player => player.InGame),
                Players = players
            };

            string tempPath = _targetFilePath + ".tmp";
            string json = JsonSerializer.Serialize(snapshot, JsonOptions);
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _targetFilePath, overwrite: true);
            _lastSavedTick = game.CurrentTick;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to save player statistics");
        }
    }
}

public sealed record PlayerStatisticsDocument
{
    [JsonPropertyName("updatedAtUtc")]
    public DateTime UpdatedAtUtc { get; init; }

    [JsonPropertyName("stage")]
    public string Stage { get; init; } = "";

    [JsonPropertyName("currentMonth")]
    public int CurrentMonth { get; init; }

    [JsonPropertyName("currentDay")]
    public int CurrentDay { get; init; }

    [JsonPropertyName("currentTick")]
    public int CurrentTick { get; init; }

    [JsonPropertyName("connectedPlayerCount")]
    public int ConnectedPlayerCount { get; init; }

    [JsonPropertyName("inGamePlayerCount")]
    public int InGamePlayerCount { get; init; }

    [JsonPropertyName("players")]
    public List<PlayerStatisticsSnapshot> Players { get; init; } = [];
}

public sealed record PlayerStatisticsSnapshot
{
    [JsonPropertyName("token")]
    public string Token { get; init; } = "";

    [JsonPropertyName("playerName")]
    public string? PlayerName { get; init; }

    [JsonPropertyName("playerId")]
    public int? PlayerId { get; init; }

    [JsonPropertyName("inGame")]
    public bool InGame { get; init; }

    [JsonPropertyName("connected")]
    public bool Connected { get; init; }

    [JsonPropertyName("removedFromGame")]
    public bool RemovedFromGame { get; init; }

    [JsonPropertyName("score")]
    public long Score { get; init; }

    [JsonPropertyName("cumulativeNav")]
    public long CumulativeNav { get; init; }

    [JsonPropertyName("currentNav")]
    public long? CurrentNav { get; init; }

    [JsonPropertyName("mora")]
    public long? Mora { get; init; }

    [JsonPropertyName("frozenMora")]
    public long? FrozenMora { get; init; }

    [JsonPropertyName("gold")]
    public int? Gold { get; init; }

    [JsonPropertyName("frozenGold")]
    public int? FrozenGold { get; init; }

    [JsonPropertyName("lockedGold")]
    public int? LockedGold { get; init; }

    [JsonPropertyName("totalTradeCount")]
    public int? TotalTradeCount { get; init; }

    [JsonPropertyName("monthlyTradeCount")]
    public int? MonthlyTradeCount { get; init; }

    [JsonPropertyName("activeCards")]
    public List<string>? ActiveCards { get; init; }

    [JsonPropertyName("connectionCount")]
    public int ConnectionCount { get; init; }

    [JsonPropertyName("disconnectCount")]
    public int DisconnectCount { get; init; }

    [JsonPropertyName("lastConnectedTick")]
    public int? LastConnectedTick { get; init; }

    [JsonPropertyName("lastDisconnectedTick")]
    public int? LastDisconnectedTick { get; init; }

    [JsonPropertyName("lastRemovedTick")]
    public int? LastRemovedTick { get; init; }

    [JsonPropertyName("disconnectedForTicks")]
    public int DisconnectedForTicks { get; init; }
}
