namespace Thuai.Utility;

using System.Text.Json.Serialization;

public record Config
{
    [JsonPropertyName("server")]
    public ServerSettings Server { get; init; } = new();

    [JsonPropertyName("token")]
    public TokenSettings Token { get; init; } = new();

    [JsonPropertyName("log")]
    public LogSettings Log { get; init; } = new();

    [JsonPropertyName("game")]
    public GameSettings Game { get; init; } = new();

    [JsonPropertyName("recorder")]
    public RecorderSettings Recorder { get; init; } = new();
}

public record ServerSettings
{
    [JsonPropertyName("port")]
    public int Port { get; init; } = 14514;
}

public record TokenSettings
{
    [JsonPropertyName("loadTokenFromEnv")]
    public bool LoadTokenFromEnv { get; init; } = true;

    [JsonPropertyName("tokenLocation")]
    public string TokenLocation { get; init; } = "TOKENS";

    [JsonPropertyName("tokenDelimiter")]
    public string TokenDelimiter { get; init; } = ",";
}

public record LogSettings
{
    [JsonPropertyName("target")]
    public string Target { get; init; } = "Console";

    [JsonPropertyName("minimumLevel")]
    public string MinimumLevel { get; init; } = "Information";

    [JsonPropertyName("targetDirectory")]
    public string TargetDirectory { get; init; } = "./logs";

    [JsonPropertyName("rollingInterval")]
    public string RollingInterval { get; init; } = "Day";
}

public record GameSettings
{
    [JsonPropertyName("ticksPerSecond")]
    public int TicksPerSecond { get; init; } = 10;

    [JsonPropertyName("tradingDayTicks")]
    public int TradingDayTicks { get; init; } = 2000;

    [JsonPropertyName("tradingDayCount")]
    public int TradingDayCount { get; init; } = 3;

    [JsonPropertyName("strategySelectionTicks")]
    public int StrategySelectionTicks { get; init; } = 40;

    [JsonPropertyName("minimumPlayerCount")]
    public int MinimumPlayerCount { get; init; } = 2;

    [JsonPropertyName("playerWaitingTicks")]
    public int PlayerWaitingTicks { get; init; } = 200;

    [JsonPropertyName("initialMora")]
    public long InitialMora { get; init; } = 1_000_000;

    [JsonPropertyName("initialGold")]
    public int InitialGold { get; init; } = 1000;

    [JsonPropertyName("initialGoldPrice")]
    public long InitialGoldPrice { get; init; } = 1000;

    [JsonPropertyName("defaultNetworkDelay")]
    public int DefaultNetworkDelay { get; init; } = 5;

    [JsonPropertyName("defaultFeeRate")]
    public double DefaultFeeRate { get; init; } = 0.0002;

    [JsonPropertyName("maxOrdersPerTick")]
    public int MaxOrdersPerTick { get; init; } = 5;

    [JsonPropertyName("newsIntervalMin")]
    public int NewsIntervalMin { get; init; } = 200;

    [JsonPropertyName("newsIntervalMax")]
    public int NewsIntervalMax { get; init; } = 400;

    [JsonPropertyName("researchWindowTicks")]
    public int ResearchWindowTicks { get; init; } = 50;

    [JsonPropertyName("researchSettlementDelay")]
    public int ResearchSettlementDelay { get; init; } = 100;

    [JsonPropertyName("baseResearchReward")]
    public long BaseResearchReward { get; init; } = 10000;

    [JsonPropertyName("npcOrdersPerTick")]
    public int NpcOrdersPerTick { get; init; } = 3;
}

public record RecorderSettings
{
    [JsonPropertyName("keepRecord")]
    public bool KeepRecord { get; init; } = false;
}
