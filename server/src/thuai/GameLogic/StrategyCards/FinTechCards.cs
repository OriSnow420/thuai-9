namespace Thuai.GameLogic.StrategyCards;

/// <summary>
/// 恶意做空 — For 10 ticks after activation, the system generates fake sell orders
/// visible to the opponent (auto-cancelled before matching).
/// TradingDay should check IsEffectActive() each tick to inject fake orders.
/// Cooldown: 600 ticks.
/// </summary>
public class MaliciousShorting : StrategyCard
{
    public override string Name => "恶意做空";
    public override CardCategory Category => CardCategory.FinTech;
    public override string Description => "释放后10帧内生成虚假卖单干扰对方盘口数据分析";
    public override bool IsPassive => false;
    public override int Cooldown => 600;

    private const int EffectDuration = 10;

    public int EffectEndTick { get; private set; } = -1;

    public bool IsEffectActive(int currentTick) =>
        EffectEndTick > 0 && currentTick < EffectEndTick;

    public override void OnActivate(Player player, int currentTick)
    {
        EffectEndTick = currentTick + EffectDuration;
        StartCooldown();
    }
}

/// <summary>
/// 拔网线 — Exchange enters circuit-breaker for 20 ticks. No new orders allowed
/// from any participant (players or NPCs); only cancel operations permitted.
/// TradingDay should check IsCircuitBreakerActive() before accepting new orders.
/// Cooldown: 1000 ticks.
/// </summary>
public class NetworkDisconnect : StrategyCard
{
    public override string Name => "拔网线";
    public override CardCategory Category => CardCategory.FinTech;
    public override string Description => "交易所进入熔断状态20帧，所有人无法提交新订单，仅允许撤单";
    public override bool IsPassive => false;
    public override int Cooldown => 1000;

    private const int BreakerDuration = 20;

    public int CircuitBreakerEndTick { get; private set; } = -1;

    public bool IsCircuitBreakerActive(int currentTick) =>
        CircuitBreakerEndTick > 0 && currentTick < CircuitBreakerEndTick;

    public override void OnActivate(Player player, int currentTick)
    {
        CircuitBreakerEndTick = currentTick + BreakerDuration;
        StartCooldown();
    }
}

/// <summary>
/// 暗池交易 — Execute a 100-unit trade at mid-price, bypassing the order book (no price impact).
/// TradingDay handles the actual execution: the player specifies buy or sell,
/// and the system fills 100 units at the current mid-price directly.
/// Cooldown: 800 ticks.
/// </summary>
public class DarkPoolTrading : StrategyCard
{
    public override string Name => "暗池交易";
    public override CardCategory Category => CardCategory.FinTech;
    public override string Description => "向系统提交100单位的市价买/卖单，在暗池中按当前中间价成交";
    public override bool IsPassive => false;
    public override int Cooldown => 800;

    public int TradeQuantity => 100;

    public override void OnActivate(Player player, int currentTick)
    {
        // The actual trade execution is handled by TradingDay since it needs
        // OrderBook access to determine mid-price and settle the transaction.
        StartCooldown();
    }
}

/// <summary>
/// 舆情干预 — Broadcast a fake news item (璃月快报). If the opponent submits a research
/// report responding to this fake news, it is automatically judged as wrong with a penalty.
/// TradingDay should inject a fake news item via NewsSystem.InjectFakeNews() on activation,
/// and mark any opponent research reports that reference it as incorrect.
/// Cooldown: 1200 ticks.
/// </summary>
public class SentimentManipulation : StrategyCard
{
    public override string Name => "舆情干预";
    public override CardCategory Category => CardCategory.FinTech;
    public override string Description => "主动广播一条伪造的璃月快报，若对手的研报指令响应此假新闻将被判定预测错误";
    public override bool IsPassive => false;
    public override int Cooldown => 1200;

    public override void OnActivate(Player player, int currentTick)
    {
        // The actual fake news injection is handled by TradingDay
        // using NewsSystem.InjectFakeNews().
        StartCooldown();
    }
}
