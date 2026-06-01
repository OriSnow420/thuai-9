using Thuai.GameLogic.StrategyCards;
using Thuai.Utility;

namespace Thuai.GameLogic;

public class Player
{
    private readonly object _lock = new();
    private readonly long _initialMora;
    private readonly int _initialGold;
    private readonly int _baseNetworkDelay;
    private readonly double _baseTransactionFeeRate;
    private readonly int _baseImmediateOrdersPerDay;
    private readonly int _baseRestingOrdersPerDay;

    public string Token { get; }
    public int PlayerId { get; }
    public string DisplayName { get; private set; }

    public long Mora { get; private set; } = 1_000_000;
    public long FrozenMora { get; private set; }
    public int Gold { get; private set; } = 1_000;
    public int FrozenGold { get; private set; }
    public int LockedGold { get; private set; }
    public int LockedGoldUntilTick { get; set; }

    public List<IStrategyCard> ActiveCards { get; } = [];

    public int NetworkDelay { get; set; }
    public double TransactionFeeRate { get; set; }
    public int MaxImmediateOrdersPerDay { get; set; }
    public int MaxRestingOrdersPerDay { get; set; }

    public int ImmediateOrdersUsedToday { get; set; }
    public int RestingOrdersUsedToday { get; set; }
    public int BonusImmediateOrdersToday { get; set; }

    public int TotalTradeCount { get; set; }
    public int MonthlyTradeCount { get; set; }
    public int PendingNextOrderExtraDelayDays { get; set; }
    public bool PendingFakeBroadcast { get; set; }
    public bool PendingCheapInsiderCorruption { get; set; }
    public int InsiderPriorityNewsDay { get; set; }
    public int OrdersSentThisTick { get; set; }
    public int ReportsSentThisTick { get; set; }
    public int MaxOrdersPerTick { get; set; }
    public int MaxReportsPerTick { get; set; }
    public int MaxReportsPerNews { get; set; }

    public HashSet<int> ReportedNewsIdsThisMonth { get; } = new();
    private readonly Dictionary<int, int> _reportSubmissionCountsThisMonth = new();

    public bool IsImmune { get; set; }
    public int ImmuneUntilTick { get; set; }
    public long ProtectedMidPrice { get; set; }

    public Player(string token, int playerId, GameSettings settings, string? displayName = null)
    {
        Token = token;
        PlayerId = playerId;
        DisplayName = NormalizeDisplayName(displayName) ?? BuildDefaultDisplayName(playerId);

        _initialMora = settings.InitialMora;
        _initialGold = settings.InitialGold;
        _baseNetworkDelay = settings.DefaultNetworkDelay;
        _baseTransactionFeeRate = settings.DefaultFeeRate;
        _baseImmediateOrdersPerDay = settings.MaxImmediateOrdersPerDay;
        _baseRestingOrdersPerDay = settings.MaxRestingOrdersPerDay;

        ResetForNewMonth();
    }

    public Player(string token, int playerId)
        : this(token, playerId, new GameSettings())
    {
    }

    public static string BuildDefaultDisplayName(int playerId) => $"Player{playerId}";

    public static string? NormalizeDisplayName(string? displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return null;

        return displayName.Trim();
    }

    public void SetDisplayName(string? displayName)
    {
        var normalized = NormalizeDisplayName(displayName);
        if (normalized != null)
            DisplayName = normalized;
    }

    public void ResetForNewMonth()
    {
        lock (_lock)
        {
            Mora = _initialMora;
            FrozenMora = 0;
            Gold = _initialGold;
            FrozenGold = 0;
            LockedGold = 0;
            LockedGoldUntilTick = 0;
            NetworkDelay = _baseNetworkDelay;
            TransactionFeeRate = _baseTransactionFeeRate;
            MaxImmediateOrdersPerDay = _baseImmediateOrdersPerDay;
            MaxRestingOrdersPerDay = _baseRestingOrdersPerDay;
            MaxOrdersPerTick = _baseImmediateOrdersPerDay + _baseRestingOrdersPerDay;
            MaxReportsPerTick = 1;
            MaxReportsPerNews = 1;
            ImmediateOrdersUsedToday = 0;
            RestingOrdersUsedToday = 0;
            BonusImmediateOrdersToday = 0;
            MonthlyTradeCount = 0;
            PendingNextOrderExtraDelayDays = 0;
            PendingFakeBroadcast = false;
            PendingCheapInsiderCorruption = false;
            InsiderPriorityNewsDay = 0;
            ReportedNewsIdsThisMonth.Clear();
            _reportSubmissionCountsThisMonth.Clear();
            IsImmune = false;
            ImmuneUntilTick = 0;
            ProtectedMidPrice = 0;
        }
    }

    public void ResetDailyActionCounters()
    {
        ImmediateOrdersUsedToday = 0;
        RestingOrdersUsedToday = 0;
        BonusImmediateOrdersToday = 0;
        OrdersSentThisTick = 0;
        ReportsSentThisTick = 0;
    }

    public long CalculateNAV(long midPrice)
    {
        lock (_lock)
        {
            long effectivePrice = midPrice;
            if (IsImmune && ProtectedMidPrice > 0 && midPrice < ProtectedMidPrice)
            {
                effectivePrice = ProtectedMidPrice - (ProtectedMidPrice - midPrice) / 5;
            }

            return Mora + FrozenMora + (long)(Gold + FrozenGold + LockedGold) * effectivePrice;
        }
    }

    public bool CanPlaceImmediateOrder() =>
        ImmediateOrdersUsedToday < MaxImmediateOrdersPerDay + BonusImmediateOrdersToday;

    public bool CanPlaceRestingOrder() =>
        RestingOrdersUsedToday < MaxRestingOrdersPerDay;

    public bool CanSubmitReport(int newsId) =>
        _reportSubmissionCountsThisMonth.GetValueOrDefault(newsId) < MaxReportsPerNews;

    public bool CanPlaceOrder() => OrdersSentThisTick < MaxOrdersPerTick;

    public bool CanSubmitReport() => ReportsSentThisTick < MaxReportsPerTick;

    public void MarkImmediateOrder()
    {
        ImmediateOrdersUsedToday++;
        OrdersSentThisTick++;
    }

    public void MarkRestingOrder()
    {
        RestingOrdersUsedToday++;
        OrdersSentThisTick++;
    }

    public void MarkReportSubmitted(int newsId)
    {
        _reportSubmissionCountsThisMonth[newsId] = _reportSubmissionCountsThisMonth.GetValueOrDefault(newsId) + 1;
        ReportedNewsIdsThisMonth.Add(newsId);
        ReportsSentThisTick++;
    }

    public void ConfigureReportLimits(int maxReportsPerTick, int maxReportsPerNews)
    {
        MaxReportsPerTick = Math.Max(0, maxReportsPerTick);
        MaxReportsPerNews = Math.Max(0, maxReportsPerNews);
    }

    public int ConsumeNextOrderExtraDelayDays()
    {
        lock (_lock)
        {
            int extra = PendingNextOrderExtraDelayDays;
            PendingNextOrderExtraDelayDays = 0;
            return extra;
        }
    }

    public void AddMonthlyTradeCount(int quantity = 1)
    {
        MonthlyTradeCount += quantity;
        TotalTradeCount += quantity;
    }

    public void AddNextOrderExtraDelayDays(int amount)
    {
        lock (_lock)
        {
            PendingNextOrderExtraDelayDays += amount;
        }
    }

    public void ResetForNewDay()
    {
        ResetForNewMonth();
        ResetDailyActionCounters();
    }

    public void ResetTickCounters() => ResetDailyActionCounters();

    public bool ConsumePendingFakeBroadcast()
    {
        if (!PendingFakeBroadcast)
            return false;

        PendingFakeBroadcast = false;
        return true;
    }

    public bool ConsumePendingCheapInsiderCorruption()
    {
        if (!PendingCheapInsiderCorruption)
            return false;

        PendingCheapInsiderCorruption = false;
        return true;
    }

    public void SetInsiderPriorityDay(int day)
    {
        InsiderPriorityNewsDay = day;
    }

    public bool HasInsiderPriorityDay(int day) => InsiderPriorityNewsDay == day;

    public void FreezeMora(long amount)
    {
        lock (_lock)
        {
            Mora -= amount;
            FrozenMora += amount;
        }
    }

    public void UnfreezeMora(long amount)
    {
        lock (_lock)
        {
            FrozenMora -= amount;
            Mora += amount;
        }
    }

    public void FreezeGold(int amount)
    {
        lock (_lock)
        {
            Gold -= amount;
            FrozenGold += amount;
        }
    }

    public void UnfreezeGold(int amount)
    {
        lock (_lock)
        {
            FrozenGold -= amount;
            Gold += amount;
        }
    }

    /// <summary>Permanently remove Mora from the frozen pool (e.g., spent on a trade).</summary>
    public void SpendFrozenMora(long amount)
    {
        lock (_lock)
        {
            FrozenMora -= amount;
        }
    }

    /// <summary>Permanently remove Gold from the frozen pool (e.g., sold in a trade).</summary>
    public void SpendFrozenGold(int amount)
    {
        lock (_lock)
        {
            FrozenGold -= amount;
        }
    }

    /// <summary>Add Mora to available balance (e.g., proceeds from a sale).</summary>
    public void AddMora(long amount)
    {
        lock (_lock)
        {
            Mora += amount;
        }
    }

    /// <summary>Add Gold to available balance (e.g., purchased gold).</summary>
    public void AddGold(int amount)
    {
        lock (_lock)
        {
            Gold += amount;
        }
    }

    public void UpdateLockedGold(int currentTick)
    {
        lock (_lock)
        {
            if (LockedGold > 0 && currentTick >= LockedGoldUntilTick)
            {
                Gold += LockedGold;
                LockedGold = 0;
            }
        }
    }

    public void AddLockedGold(int amount, int untilTick)
    {
        lock (_lock)
        {
            LockedGold += amount;
            LockedGoldUntilTick = untilTick;
        }
    }
}
