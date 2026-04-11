namespace Thuai.GameLogic.StrategyCards;

/// <summary>
/// 高频专线 — Max orders per tick raised to 10.
/// </summary>
public class HighFrequencyLine : StrategyCard
{
    public override string Name => "高频专线";
    public override CardCategory Category => CardCategory.Infrastructure;
    public override string Description => "交易指令的发送上限提升至每帧10条";

    public override void OnAcquire(Player player)
    {
        player.MaxOrdersPerTick = 10;
    }
}

/// <summary>
/// 低延迟主板 — Orders treated as arriving 1 tick earlier for time-priority.
/// Implemented by reducing NetworkDelay by 1 (since ArrivalTick = SubmitTick + NetworkDelay).
/// </summary>
public class LowLatencyBoard : StrategyCard
{
    public override string Name => "低延迟主板";
    public override CardCategory Category => CardCategory.Infrastructure;
    public override string Description => "你的限价单在时间优先排序时视为提前1帧到达";

    public override void OnAcquire(Player player)
    {
        player.NetworkDelay = Math.Max(0, player.NetworkDelay - 1);
    }
}

/// <summary>
/// 内幕消息 — Receive news 3 ticks early.
/// The effect is checked at news broadcast time by the GameController/TradingDay.
/// Check via: player.ActiveCards.Any(c => c is InsiderInfo)
/// </summary>
public class InsiderInfo : StrategyCard
{
    public override string Name => "内幕消息";
    public override CardCategory Category => CardCategory.Infrastructure;
    public override string Description => "你将比对手提前3帧收到璃月快报的文本内容";

    public int EarlyTicks => 3;
}

/// <summary>
/// 量化集群 — Research window extends from 50 to 80 ticks; reward time decay halved.
/// The effect is checked at research report submission time by the ResearchSystem.
/// Check via: player.ActiveCards.OfType&lt;QuantCluster&gt;().FirstOrDefault()
/// </summary>
public class QuantCluster : StrategyCard
{
    public override string Name => "量化集群";
    public override CardCategory Category => CardCategory.Infrastructure;
    public override string Description => "研报指令有效提交窗口延长至80帧，且奖励金的时间惩罚衰减速度减半";

    public int ExtendedResearchWindow => 80;
    public double DecayMultiplier => 0.5;
}

/// <summary>
/// 闪电交易 — Active skill. Network delay drops to 0 for 50 ticks after activation.
/// One-shot per trading day.
/// </summary>
public class FlashTrading : StrategyCard
{
    public override string Name => "闪电交易";
    public override CardCategory Category => CardCategory.Infrastructure;
    public override string Description => "激活后50帧内网络延迟降为0帧";
    public override bool IsPassive => false;
    public override int Cooldown => 0;

    private int _originalDelay;
    private int _effectEndTick = -1;
    private bool _used;

    public bool IsUsed => _used;
    public int EffectDuration => 50;

    public override void OnActivate(Player player, int currentTick)
    {
        if (_used) return;

        _used = true;
        _originalDelay = player.NetworkDelay;
        player.NetworkDelay = 0;
        _effectEndTick = currentTick + EffectDuration;
        CurrentCooldown = int.MaxValue; // Prevent re-activation via CanActivate()
    }

    public override void OnTick(Player player, int currentTick)
    {
        base.OnTick(player, currentTick);

        if (_effectEndTick > 0 && currentTick >= _effectEndTick)
        {
            player.NetworkDelay = _originalDelay;
            _effectEndTick = -1;
        }
    }

    public void ResetDaily()
    {
        _used = false;
        _effectEndTick = -1;
        CurrentCooldown = 0;
    }
}
