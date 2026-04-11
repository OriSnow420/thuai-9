using Thuai.GameLogic.StrategyCards;

namespace Thuai.GameLogic;

public class Player
{
    private readonly object _lock = new();

    public string Token { get; }
    public int PlayerId { get; }

    public long Mora { get; private set; } = 1_000_000;
    public long FrozenMora { get; private set; }
    public int Gold { get; private set; } = 1_000;
    public int FrozenGold { get; private set; }
    public int LockedGold { get; private set; }
    public int LockedGoldUntilTick { get; set; }

    public List<IStrategyCard> ActiveCards { get; } = [];

    public int NetworkDelay { get; set; } = 5;
    public double TransactionFeeRate { get; set; } = 0.0002;
    public int MaxOrdersPerTick { get; set; } = 5;
    public int MaxReportsPerTick { get; set; } = 1;

    public int OrdersSentThisTick { get; set; }
    public int ReportsSentThisTick { get; set; }

    public int TotalTradeCount { get; set; }

    public bool IsImmune { get; set; }
    public int ImmuneUntilTick { get; set; }

    public Player(string token, int playerId)
    {
        Token = token;
        PlayerId = playerId;
    }

    public void ResetForNewDay()
    {
        lock (_lock)
        {
            Mora = 1_000_000;
            FrozenMora = 0;
            Gold = 1_000;
            FrozenGold = 0;
            LockedGold = 0;
            LockedGoldUntilTick = 0;
            OrdersSentThisTick = 0;
            ReportsSentThisTick = 0;
        }
    }

    public void ResetTickCounters()
    {
        OrdersSentThisTick = 0;
        ReportsSentThisTick = 0;
    }

    public long CalculateNAV(long midPrice)
    {
        lock (_lock)
        {
            return Mora + FrozenMora + (long)(Gold + FrozenGold + LockedGold) * midPrice;
        }
    }

    public bool CanPlaceOrder() => OrdersSentThisTick < MaxOrdersPerTick;
    public bool CanSubmitReport() => ReportsSentThisTick < MaxReportsPerTick;

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
