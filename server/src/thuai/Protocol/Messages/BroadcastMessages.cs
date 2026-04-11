namespace Thuai.Protocol.Messages;

using System.Text.Json.Serialization;

public record GameStateMessage : Message
{
    public override string MessageType => "GAME_STATE";

    [JsonPropertyName("stage")]
    public string Stage { get; init; } = "";

    [JsonPropertyName("currentDay")]
    public int CurrentDay { get; init; }

    [JsonPropertyName("currentTick")]
    public int CurrentTick { get; init; }

    [JsonPropertyName("totalTicks")]
    public int TotalTicks { get; init; }

    [JsonPropertyName("scores")]
    public List<PlayerScore>? Scores { get; init; }
}

public record PlayerScore
{
    [JsonPropertyName("token")]
    public string Token { get; init; } = "";

    [JsonPropertyName("score")]
    public int Score { get; init; }
}

public record MarketStateMessage : Message
{
    public override string MessageType => "MARKET_STATE";

    [JsonPropertyName("bids")]
    public List<PriceLevel>? Bids { get; init; }

    [JsonPropertyName("asks")]
    public List<PriceLevel>? Asks { get; init; }

    [JsonPropertyName("lastPrice")]
    public long LastPrice { get; init; }

    [JsonPropertyName("midPrice")]
    public long MidPrice { get; init; }

    [JsonPropertyName("volume")]
    public int Volume { get; init; }

    [JsonPropertyName("tick")]
    public int Tick { get; init; }
}

public record PriceLevel
{
    [JsonPropertyName("price")]
    public long Price { get; init; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }
}

public record PlayerStateMessage : Message
{
    public override string MessageType => "PLAYER_STATE";

    [JsonPropertyName("mora")]
    public long Mora { get; init; }

    [JsonPropertyName("frozenMora")]
    public long FrozenMora { get; init; }

    [JsonPropertyName("gold")]
    public int Gold { get; init; }

    [JsonPropertyName("frozenGold")]
    public int FrozenGold { get; init; }

    [JsonPropertyName("lockedGold")]
    public int LockedGold { get; init; }

    [JsonPropertyName("nav")]
    public long Nav { get; init; }

    [JsonPropertyName("activeCards")]
    public List<string>? ActiveCards { get; init; }

    [JsonPropertyName("pendingOrders")]
    public List<OrderInfo>? PendingOrders { get; init; }
}

public record OrderInfo
{
    [JsonPropertyName("orderId")]
    public long OrderId { get; init; }

    [JsonPropertyName("side")]
    public string Side { get; init; } = "";

    [JsonPropertyName("price")]
    public long Price { get; init; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }

    [JsonPropertyName("remainingQuantity")]
    public int RemainingQuantity { get; init; }

    [JsonPropertyName("status")]
    public string Status { get; init; } = "";
}

public record NewsBroadcastMessage : Message
{
    public override string MessageType => "NEWS_BROADCAST";

    [JsonPropertyName("newsId")]
    public int NewsId { get; init; }

    [JsonPropertyName("content")]
    public string Content { get; init; } = "";

    [JsonPropertyName("publishTick")]
    public int PublishTick { get; init; }
}

public record ReportResultMessage : Message
{
    public override string MessageType => "REPORT_RESULT";

    [JsonPropertyName("newsId")]
    public int NewsId { get; init; }

    [JsonPropertyName("prediction")]
    public string Prediction { get; init; } = "";

    [JsonPropertyName("isCorrect")]
    public bool IsCorrect { get; init; }

    [JsonPropertyName("reward")]
    public long Reward { get; init; }

    [JsonPropertyName("actualChange")]
    public long ActualChange { get; init; }
}

public record StrategyOptionsMessage : Message
{
    public override string MessageType => "STRATEGY_OPTIONS";

    [JsonPropertyName("infrastructure")]
    public CardOption? Infrastructure { get; init; }

    [JsonPropertyName("riskControl")]
    public CardOption? RiskControl { get; init; }

    [JsonPropertyName("finTech")]
    public CardOption? FinTech { get; init; }
}

public record CardOption
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";

    [JsonPropertyName("category")]
    public string Category { get; init; } = "";
}

public record TradeNotificationMessage : Message
{
    public override string MessageType => "TRADE_NOTIFICATION";

    [JsonPropertyName("tradeId")]
    public long TradeId { get; init; }

    [JsonPropertyName("orderId")]
    public long OrderId { get; init; }

    [JsonPropertyName("price")]
    public long Price { get; init; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; }

    [JsonPropertyName("side")]
    public string Side { get; init; } = "";

    [JsonPropertyName("fee")]
    public long Fee { get; init; }
}

public record SkillEffectMessage : Message
{
    public override string MessageType => "SKILL_EFFECT";

    [JsonPropertyName("skillName")]
    public string SkillName { get; init; } = "";

    [JsonPropertyName("sourcePlayer")]
    public string SourcePlayer { get; init; } = "";

    [JsonPropertyName("description")]
    public string Description { get; init; } = "";
}

public record ErrorMessage : Message
{
    public override string MessageType => "ERROR";

    [JsonPropertyName("errorCode")]
    public int ErrorCode { get; init; }

    [JsonPropertyName("message")]
    public string ErrorText { get; init; } = "";
}
