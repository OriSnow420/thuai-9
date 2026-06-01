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

    [JsonPropertyName("acceptAnyToken")]
    public bool AcceptAnyToken { get; init; } = false;
}

public record TokenSettings
{
    [JsonPropertyName("loadTokenFromEnv")]
    public bool LoadTokenFromEnv { get; init; } = true;

    [JsonPropertyName("tokenLocation")]
    public string TokenLocation { get; init; } = "TOKENS";

    [JsonPropertyName("tokenDelimiter")]
    public string TokenDelimiter { get; init; } = ",";

    [JsonPropertyName("playerNameLocation")]
    public string PlayerNameLocation { get; init; } = "PLAYER_NAMES_JSON";
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
    public int TradingDayTicks { get; init; } = 30;

    [JsonPropertyName("tradingDayCount")]
    public int TradingDayCount { get; init; } = 3;

    [JsonPropertyName("infiniteMode")]
    public bool InfiniteMode { get; init; } = false;

    [JsonPropertyName("strategySelectionTicks")]
    public int StrategySelectionTicks { get; init; } = 40;

    [JsonPropertyName("minimumPlayerCount")]
    public int MinimumPlayerCount { get; init; } = 2;

    [JsonPropertyName("playerWaitingTicks")]
    public int PlayerWaitingTicks { get; init; } = 200;

    [JsonPropertyName("disconnectedPlayerRetentionTicks")]
    public int DisconnectedPlayerRetentionTicks { get; init; } = 0;

    [JsonPropertyName("initialMora")]
    public long InitialMora { get; init; } = 1_000_000;

    [JsonPropertyName("initialGold")]
    public int InitialGold { get; init; } = 1000;

    [JsonPropertyName("initialGoldPrice")]
    public long InitialGoldPrice { get; init; } = 1000;

    [JsonPropertyName("defaultNetworkDelay")]
    public int DefaultNetworkDelay { get; init; } = 1;

    [JsonPropertyName("defaultFeeRate")]
    public double DefaultFeeRate { get; init; } = 0.001;

    [JsonPropertyName("maxOrdersPerTick")]
    public int MaxOrdersPerTick { get; init; } = 2;

    // Per-day action quotas that the engine actually enforces (Player.CanPlace*).
    [JsonPropertyName("maxImmediateOrdersPerDay")]
    public int MaxImmediateOrdersPerDay { get; init; } = 1;

    [JsonPropertyName("maxRestingOrdersPerDay")]
    public int MaxRestingOrdersPerDay { get; init; } = 1;

    [JsonPropertyName("maxReportsPerNews")]
    public int MaxReportsPerNews { get; init; } = 1;

    [JsonPropertyName("maxReportsPerTick")]
    public int MaxReportsPerTick { get; init; } = 1;

    [JsonPropertyName("newsIntervalMin")]
    public int NewsIntervalMin { get; init; } = 200;

    [JsonPropertyName("newsIntervalMax")]
    public int NewsIntervalMax { get; init; } = 400;

    [JsonPropertyName("researchEnabled")]
    public bool ResearchEnabled { get; init; } = true;

    [JsonPropertyName("researchNewsScheduleTicks")]
    public int[] ResearchNewsScheduleTicks { get; init; } = [1, 11, 21];

    [JsonPropertyName("researchWindowTicks")]
    public int ResearchWindowTicks { get; init; } = 2;

    [JsonPropertyName("researchSettlementDelay")]
    public int ResearchSettlementDelay { get; init; } = 3;

    [JsonPropertyName("baseResearchReward")]
    public long BaseResearchReward { get; init; } = 10000;

    [JsonPropertyName("npcOrdersPerTick")]
    public int NpcOrdersPerTick { get; init; } = 3;

    [JsonPropertyName("newsSentimentDurationTicks")]
    public int NewsSentimentDurationTicks { get; init; } = 3;

    [JsonPropertyName("npcNewsReactionDelayTicks")]
    public int NpcNewsReactionDelayTicks { get; init; } = 1;

    [JsonPropertyName("settlementTwapWindowTicks")]
    public int SettlementTwapWindowTicks { get; init; } = 5;

    [JsonPropertyName("markPriceDepthLevels")]
    public int MarkPriceDepthLevels { get; init; } = 3;

    [JsonPropertyName("markPriceMinLevelQuantity")]
    public int MarkPriceMinLevelQuantity { get; init; } = 20;

    [JsonPropertyName("markPriceMinOrderAgeTicks")]
    public int MarkPriceMinOrderAgeTicks { get; init; } = 1;

    [JsonPropertyName("initialLiquidityLevels")]
    public int InitialLiquidityLevels { get; init; } = 8;

    [JsonPropertyName("initialLiquidityBaseQuantity")]
    public int InitialLiquidityBaseQuantity { get; init; } = 60;

    [JsonPropertyName("initialLiquidityQuantityStep")]
    public int InitialLiquidityQuantityStep { get; init; } = 10;
}

public record RecorderSettings
{
    [JsonPropertyName("keepRecord")]
    public bool KeepRecord { get; init; } = false;

    [JsonPropertyName("flushEveryRecords")]
    public int FlushEveryRecords { get; init; } = 1000;

    [JsonPropertyName("statisticsSaveIntervalTicks")]
    public int StatisticsSaveIntervalTicks { get; init; } = 100;

    [JsonPropertyName("enableStatRecording")]
    public bool EnableStatRecording { get; init; } = true;

    [JsonPropertyName("statFlushEveryRecords")]
    public int StatFlushEveryRecords { get; init; } = 500;
}
