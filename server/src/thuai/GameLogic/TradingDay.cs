namespace Thuai.GameLogic;

using System.Numerics;
using Thuai.GameLogic.StrategyCards;

public record SkillActivation(int SourcePlayerId, string SkillName, string Description, int? TargetPlayerId = null);

public class TradingDay
{
    private readonly object _lock = new();
    private readonly int _maxTicks;
    private readonly long _initialGoldPrice;
    private readonly Dictionary<string, Player> _players;
    private readonly OrderBook _orderBook;
    private readonly MatchEngine _matchEngine;
    private readonly NewsSystem _newsSystem;
    private readonly NPCTrader _npcTrader;
    private readonly ResearchSystem _researchSystem;
    private readonly bool _researchEnabled;
    private readonly int _maxReportsPerTick;
    private readonly int _maxReportsPerNews;
    private readonly int _npcNewsReactionDelayTicks;
    private readonly int _settlementTwapWindowTicks;
    private readonly double _markPriceMaxDeviationRatioFromLastPrice;
    private readonly double _safePriceMaxDailyMoveRatio;
    private readonly double _maxOrderPriceDeviationRatioFromSafePrice;
    private readonly long _researchMaxAbsRewardPerReport;
    private readonly long _researchPositiveRewardBudgetPerPlayerPerMonth;
    private readonly long _systemInitialMora;
    private readonly long _systemInitialGold;
    private readonly int _systemMaxNetBuyQuantityPerDay;
    private readonly int _systemMaxNetSellQuantityPerDay;
    private readonly int _systemMaxGrossTradeQuantityPerDay;
    private readonly bool _circuitBreakerEnabled;
    private readonly double _circuitBreakerTriggerRatio;
    private readonly int _circuitBreakerDurationTicks;
    private readonly int _initialLiquidityLevels;
    private readonly int _initialLiquidityBaseQuantity;
    private readonly int _initialLiquidityQuantityStep;
    private readonly Dictionary<int, long> _markPriceHistory = new();
    private readonly Dictionary<int, long> _safePriceHistory = new();
    private readonly Dictionary<int, BigInteger> _tradeNotionalHistory = new();
    private readonly Dictionary<int, int> _tradeVolumeHistory = new();
    private readonly List<Trade> _tradesThisDay = new();
    private readonly List<ResearchReport> _settledReportsThisDay = new();
    private readonly List<News> _publishedNewsThisDay = new();
    private readonly List<SkillActivation> _skillEffectsThisDay = new();
    private readonly List<(string PlayerToken, News Preview)> _pendingInsiderPreviews = new();

    private int _currentTick;
    private int _circuitBreakerTicksRemaining;
    private bool _isFinished;
    private bool _hasPendingNotifications;

    public int MonthNumber { get; }
    public int CurrentTick => _currentTick;
    public bool IsFinished => _isFinished;
    public bool HasPendingNotifications => _hasPendingNotifications;
    public OrderBook OrderBook => _orderBook;
    public MatchEngine MatchEngine => _matchEngine;
    public NewsSystem NewsSystem => _newsSystem;
    public ResearchSystem ResearchSystem => _researchSystem;
    public IReadOnlyList<Trade> TradesThisDay => _tradesThisDay;
    public IReadOnlyList<ResearchReport> SettledReportsThisDay => _settledReportsThisDay;
    public IReadOnlyList<News> PublishedNewsThisDay => _publishedNewsThisDay;
    public IReadOnlyList<SkillActivation> SkillEffectsThisDay => _skillEffectsThisDay;
    public IReadOnlyList<(string PlayerToken, News Preview)> PendingInsiderPreviews => _pendingInsiderPreviews;

    public TradingDay(
        Dictionary<string, Player> players,
        int maxTicks,
        long initialGoldPrice,
        int newsIntervalMin,
        int newsIntervalMax,
        int researchWindow,
        int researchSettlementDelay,
        long baseResearchReward,
        int npcOrdersPerTick,
        int monthNumber = 1,
        bool researchEnabled = true,
        int maxReportsPerTick = 1,
        int maxReportsPerNews = 1,
        IReadOnlyList<int>? scheduledNewsTicks = null,
        int newsSentimentDurationTicks = 3,
        int npcNewsReactionDelayTicks = 1,
        int settlementTwapWindowTicks = 5,
        int markPriceDepthLevels = 3,
        int markPriceMinLevelQuantity = 20,
        int markPriceMinOrderAgeTicks = 1,
        double markPriceMaxDeviationRatioFromLastPrice = 0.30,
        double safePriceMaxDailyMoveRatio = 0.15,
        double maxOrderPriceDeviationRatioFromSafePrice = 0.30,
        long researchMaxAbsRewardPerReport = 100000,
        long researchPositiveRewardBudgetPerPlayerPerMonth = 200000,
        long systemInitialMora = 100_000_000,
        long systemInitialGold = 100_000,
        int systemMaxNetBuyQuantityPerDay = 200,
        int systemMaxNetSellQuantityPerDay = 200,
        int systemMaxGrossTradeQuantityPerDay = 300,
        bool circuitBreakerEnabled = true,
        double circuitBreakerTriggerRatio = 0.25,
        int circuitBreakerDurationTicks = 2,
        int initialLiquidityLevels = 8,
        int initialLiquidityBaseQuantity = 60,
        int initialLiquidityQuantityStep = 10)
    {
        MonthNumber = monthNumber;
        _maxTicks = maxTicks;
        _initialGoldPrice = initialGoldPrice;
        _players = players;
        _orderBook = new OrderBook(
            initialGoldPrice,
            markPriceDepthLevels,
            markPriceMinLevelQuantity,
            markPriceMinOrderAgeTicks,
            markPriceMaxDeviationRatioFromLastPrice);
        _newsSystem = new NewsSystem(newsIntervalMin, newsIntervalMax, researchWindow, scheduledNewsTicks, newsSentimentDurationTicks);
        _npcTrader = new NPCTrader(npcOrdersPerTick);
        _researchEnabled = researchEnabled;
        _maxReportsPerTick = Math.Max(0, maxReportsPerTick);
        _maxReportsPerNews = Math.Max(0, maxReportsPerNews);
        _npcNewsReactionDelayTicks = Math.Max(0, npcNewsReactionDelayTicks);
        _settlementTwapWindowTicks = Math.Max(1, settlementTwapWindowTicks);
        _markPriceMaxDeviationRatioFromLastPrice = Math.Max(0, markPriceMaxDeviationRatioFromLastPrice);
        _safePriceMaxDailyMoveRatio = Math.Max(0, safePriceMaxDailyMoveRatio);
        _maxOrderPriceDeviationRatioFromSafePrice = Math.Max(0, maxOrderPriceDeviationRatioFromSafePrice);
        _researchMaxAbsRewardPerReport = Math.Max(0, researchMaxAbsRewardPerReport);
        _researchPositiveRewardBudgetPerPlayerPerMonth = Math.Max(0, researchPositiveRewardBudgetPerPlayerPerMonth);
        _systemInitialMora = Math.Max(0, systemInitialMora);
        _systemInitialGold = Math.Max(0, systemInitialGold);
        _systemMaxNetBuyQuantityPerDay = Math.Max(0, systemMaxNetBuyQuantityPerDay);
        _systemMaxNetSellQuantityPerDay = Math.Max(0, systemMaxNetSellQuantityPerDay);
        _systemMaxGrossTradeQuantityPerDay = Math.Max(0, systemMaxGrossTradeQuantityPerDay);
        _circuitBreakerEnabled = circuitBreakerEnabled;
        _circuitBreakerTriggerRatio = Math.Max(0, circuitBreakerTriggerRatio);
        _circuitBreakerDurationTicks = Math.Max(0, circuitBreakerDurationTicks);
        _initialLiquidityLevels = Math.Max(1, initialLiquidityLevels);
        _initialLiquidityBaseQuantity = Math.Max(1, initialLiquidityBaseQuantity);
        _initialLiquidityQuantityStep = Math.Max(0, initialLiquidityQuantityStep);

        _matchEngine = new MatchEngine(
            _orderBook,
            players,
            _systemInitialMora,
            _systemInitialGold,
            _systemMaxNetBuyQuantityPerDay,
            _systemMaxNetSellQuantityPerDay,
            _systemMaxGrossTradeQuantityPerDay);
        _researchSystem = new ResearchSystem(
            _newsSystem,
            baseResearchReward,
            researchWindow,
            researchSettlementDelay,
            _researchMaxAbsRewardPerReport,
            _researchPositiveRewardBudgetPerPlayerPerMonth);
    }

    public void Initialize()
    {
        _currentTick = 0;
        _circuitBreakerTicksRemaining = 0;
        _isFinished = false;
        _hasPendingNotifications = false;
        _markPriceHistory.Clear();
        _safePriceHistory.Clear();
        _tradeNotionalHistory.Clear();
        _tradeVolumeHistory.Clear();
        _tradesThisDay.Clear();
        _settledReportsThisDay.Clear();
        _publishedNewsThisDay.Clear();
        _skillEffectsThisDay.Clear();
        _pendingInsiderPreviews.Clear();

        foreach (var player in _players.Values)
        {
            player.ResetDailyActionCounters();
            player.ConfigureReportLimits(_maxReportsPerTick, _maxReportsPerNews);
        }

        SeedInitialLiquidity();
        RecordMidPrice(0, circuitBreakerActive: false);
    }

    public void Tick()
    {
        lock (_lock)
        {
            if (_isFinished)
                return;

            _currentTick++;
            if (_currentTick > _maxTicks)
            {
                _isFinished = true;
                return;
            }

            foreach (var player in _players.Values)
            {
                player.ResetDailyActionCounters();
                player.UpdateLockedGold(_currentTick);
                StrategyCardManager.TickCards(player, _currentTick);
            }

            var publishedNews = _newsSystem.Tick(_currentTick);
            if (publishedNews != null)
            {
                _publishedNewsThisDay.Add(publishedNews);
            }

            CheckInsiderNewsPreview();

            bool circuitBreakerActive = IsCircuitBreakerActive;
            if (!circuitBreakerActive)
            {
                var npcSentiment = _newsSystem.GetEffectiveSentiment(_currentTick, _npcNewsReactionDelayTicks);
                _npcTrader.GenerateOrders(_matchEngine, _orderBook, npcSentiment, _currentTick);
                var trades = _matchEngine.ProcessDay(_currentTick);
                _tradesThisDay.AddRange(trades);
                RecordTradeHistory(_currentTick, trades);
            }

            long rawMarkPrice = RecordMidPrice(_currentTick, circuitBreakerActive);
            UpdateCircuitBreaker(rawMarkPrice, circuitBreakerActive);
            SettlePendingReports();

            _hasPendingNotifications = _publishedNewsThisDay.Count > 0
                || _pendingInsiderPreviews.Count > 0
                || _skillEffectsThisDay.Count > 0
                || _settledReportsThisDay.Count > 0
                || _tradesThisDay.Count > 0;

            if (_currentTick >= _maxTicks)
            {
                _isFinished = true;
            }
        }
    }

    public void MarkNotificationsPublished()
    {
        _tradesThisDay.Clear();
        _settledReportsThisDay.Clear();
        _publishedNewsThisDay.Clear();
        _skillEffectsThisDay.Clear();
        _pendingInsiderPreviews.Clear();
        _hasPendingNotifications = false;
    }

    public bool HandleLimitBuy(string playerToken, long price, int quantity)
    {
        lock (_lock)
        {
            return HandleLimitOrder(playerToken, OrderSide.Buy, price, quantity);
        }
    }

    public bool HandleLimitSell(string playerToken, long price, int quantity)
    {
        lock (_lock)
        {
            return HandleLimitOrder(playerToken, OrderSide.Sell, price, quantity);
        }
    }

    public bool HandleCancelOrder(string playerToken, long orderId)
    {
        lock (_lock)
        {
            if (_isFinished) return false;
            return _matchEngine.CancelOrder(playerToken, orderId);
        }
    }

    public bool HandleSubmitReport(string playerToken, int newsId, Prediction prediction)
    {
        lock (_lock)
        {
            if (!_researchEnabled) return false;
            if (_isFinished) return false;
            if (!_players.TryGetValue(playerToken, out var player)) return false;
            if (!player.CanSubmitReport()) return false;
            if (!player.CanSubmitReport(newsId)) return false;

            var report = _researchSystem.SubmitReport(playerToken, newsId, prediction, _currentTick);
            if (report == null)
                return false;

            player.MarkReportSubmitted(newsId);
            return true;
        }
    }

    public bool HandleActivateSkill(string playerToken, string skillName, int? targetPlayerId = null, string? variant = null)
    {
        lock (_lock)
        {
            if (_isFinished) return false;
            if (!_players.TryGetValue(playerToken, out var player)) return false;

            string? targetToken = null;
            if (targetPlayerId.HasValue)
            {
                var targetPlayer = _players.Values.FirstOrDefault(p => p.PlayerId == targetPlayerId.Value);
                if (targetPlayer == null) return false;
                targetToken = targetPlayer.Token;
            }

            var card = StrategyCardManager.FindActiveCard(player, skillName);
            if (card == null) return false;

            return card switch
            {
                InsiderInfo insider => ActivateInsiderInfo(player, insider, variant),
                FlashTrading flash => ActivateFlashTrading(player, flash),
                StopLossBlade blade => ActivateStopLossBlade(player, blade),
                TargetedPurchase targeted => ActivateTargetedPurchase(player, targeted),
                NetworkStorm storm => ActivateNetworkStorm(player, storm, targetToken),
                PublicOpinionAttack attack => ActivatePublicOpinionAttack(player, attack),
                _ => false
            };
        }
    }

    public Dictionary<string, long> CalculateSettlement()
    {
        lock (_lock)
        {
            long settlementPrice = GetSettlementPriceAtTick(_currentTick);
            return _players.Values.ToDictionary(player => player.Token, player => player.CalculateNAV(settlementPrice));
        }
    }

    // Order-book / pending-order state is mutated by socket-thread order handlers
    // (which all take _lock) while the per-tick broadcaster and recorder read it
    // from the game-loop thread. These read paths therefore take _lock too —
    // _lock is reentrant, so internal callers that already hold it (e.g.
    // CancelPlayerOrders, ActivateStopLossBlade) are unaffected.
    public List<Order> GetPlayerPendingOrders(string playerToken)
    {
        lock (_lock)
        {
            return _matchEngine.GetPendingOrders(playerToken)
                .Concat(_orderBook.GetPlayerOrders(playerToken))
                .OrderBy(order => order.ArrivalTick)
                .ThenBy(order => order.OrderId)
                .ToList();
        }
    }

    public sealed record MarketSnapshot(
        List<(long Price, int Quantity)> Bids,
        List<(long Price, int Quantity)> Asks,
        long LastPrice,
        long MidPrice,
        int Volume);

    /// <summary>Consistent market-data snapshot taken under the trading-day lock.</summary>
    public MarketSnapshot SnapshotMarket(int maxLevels = 10)
    {
        lock (_lock)
        {
            return new MarketSnapshot(
                _orderBook.GetVisibleBids(maxLevels),
                _orderBook.GetVisibleAsks(maxLevels),
                _orderBook.LastPrice,
                _orderBook.MidPrice,
                _orderBook.TotalVolume);
        }
    }

    /// <summary>Mid price read under the lock, for NAV display during a live day.</summary>
    public long CurrentMidPrice
    {
        get { lock (_lock) { return _orderBook.MidPrice; } }
    }

    public long CurrentSafePrice
    {
        get
        {
            lock (_lock)
            {
                return GetSafePriceAtTick(_currentTick);
            }
        }
    }

    public bool IsCircuitBreakerActive
    {
        get
        {
            lock (_lock)
            {
                return _circuitBreakerTicksRemaining > 0;
            }
        }
    }

    public void CancelPlayerOrders(string playerToken)
    {
        lock (_lock)
        {
            foreach (var order in GetPlayerPendingOrders(playerToken))
            {
                _matchEngine.CancelOrder(playerToken, order.OrderId);
            }
        }
    }

    public int GetPlayerTradeCount(string playerToken)
    {
        return _players.TryGetValue(playerToken, out var player)
            ? player.MonthlyTradeCount
            : 0;
    }

    public long GetMidPriceAtTick(int tick)
    {
        if (_markPriceHistory.TryGetValue(tick, out var price))
            return price;

        if (_markPriceHistory.Count == 0)
            return _initialGoldPrice;

        int nearestTick = _markPriceHistory.Keys.MinBy(key => Math.Abs(key - tick));
        return _markPriceHistory[nearestTick];
    }

    public long GetSafePriceAtTick(int tick)
    {
        if (_safePriceHistory.TryGetValue(tick, out var price))
            return price;

        if (_safePriceHistory.Count == 0)
            return _initialGoldPrice;

        int nearestTick = _safePriceHistory.Keys.MinBy(key => Math.Abs(key - tick));
        return _safePriceHistory[nearestTick];
    }

    private bool HandleLimitOrder(string playerToken, OrderSide side, long price, int quantity)
    {
        if (_isFinished) return false;
        if (!_players.TryGetValue(playerToken, out var player)) return false;
        if (_circuitBreakerTicksRemaining > 0) return false;
        if (!player.CanPlaceOrder()) return false;
        if (!IsWithinSafePriceBand(price)) return false;

        int extraDelay = player.ConsumeNextOrderExtraDelayDays();
        int effectiveDelay = player.NetworkDelay + extraDelay;
        int arrivalTick = _currentTick + effectiveDelay;
        if (arrivalTick > _maxTicks)
            return false;

        int priorityRank = player.HasInsiderPriorityDay(arrivalTick) ? 0 : 1;
        var order = _matchEngine.SubmitOrder(playerToken, side, price, quantity, _currentTick, effectiveDelay, priorityRank);
        if (order != null)
            player.MarkOrderSubmitted();
        return order != null;
    }

    private void SeedInitialLiquidity()
    {
        for (int offset = 1; offset <= _initialLiquidityLevels; offset++)
        {
            int quantity = _initialLiquidityBaseQuantity + offset * _initialLiquidityQuantityStep;
            _matchEngine.SubmitOrder("SYSTEM", OrderSide.Buy, Math.Max(1, _initialGoldPrice - offset), quantity, 0);
            _matchEngine.SubmitOrder("SYSTEM", OrderSide.Sell, _initialGoldPrice + offset, quantity, 0);
        }

        _matchEngine.ProcessDay(0);
    }

    private void CheckInsiderNewsPreview()
    {
        var previewNews = _newsSystem.PreGenerateNextNews();
        if (previewNews == null)
            return;

        foreach (var player in _players.Values)
        {
            var insider = player.ActiveCards.OfType<InsiderInfo>().FirstOrDefault();
            if (insider == null)
                continue;

            if (!insider.TryConsumePreview(_currentTick, out var isFake))
                continue;

            var delivered = isFake ? _newsSystem.CreateSpoofedView(previewNews) : previewNews;
            _pendingInsiderPreviews.Add((player.Token, delivered));
        }
    }

    private void SettlePendingReports()
    {
        if (!_researchEnabled)
            return;

        foreach (var report in _researchSystem.SettleReports(_currentTick, GetSettlementPriceAtTick))
        {
            if (!_players.TryGetValue(report.PlayerToken, out var player))
                continue;

            if (report.Reward > 0)
            {
                player.AddMora(report.Reward);
            }
            else if (report.Reward < 0)
            {
                long penalty = Math.Min(Math.Abs(report.Reward), player.Mora);
                player.AddMora(-penalty);
                report.Reward = -penalty;
            }

            _settledReportsThisDay.Add(report);
        }
    }

    private long RecordMidPrice(int tick, bool circuitBreakerActive)
    {
        long previousSafePrice = tick > 0 && _safePriceHistory.TryGetValue(tick - 1, out var earlierSafePrice)
            ? earlierSafePrice
            : _initialGoldPrice;
        long rawMarkPrice = _orderBook.GetMarkPrice(tick, previousSafePrice);
        _markPriceHistory[tick] = rawMarkPrice;
        _safePriceHistory[tick] = circuitBreakerActive
            ? previousSafePrice
            : ClampSafePrice(rawMarkPrice, previousSafePrice);
        return rawMarkPrice;
    }

    private long GetSettlementPriceAtTick(int tick)
    {
        int endTick = Math.Max(0, tick);
        int startTick = Math.Max(0, endTick - _settlementTwapWindowTicks + 1);
        BigInteger tradeNotional = BigInteger.Zero;
        int tradeVolume = 0;

        for (int sampleTick = startTick; sampleTick <= endTick; sampleTick++)
        {
            if (_tradeNotionalHistory.TryGetValue(sampleTick, out var tickNotional))
                tradeNotional += tickNotional;
            if (_tradeVolumeHistory.TryGetValue(sampleTick, out var tickVolume))
                tradeVolume += tickVolume;
        }

        if (tradeVolume > 0)
            return ClampToInt64(tradeNotional / tradeVolume);

        BigInteger total = BigInteger.Zero;
        int count = 0;

        for (int sampleTick = startTick; sampleTick <= endTick; sampleTick++)
        {
            total += GetSafePriceAtTick(sampleTick);
            count++;
        }

        return count == 0 ? _initialGoldPrice : ClampToInt64(total / count);
    }

    private void RecordTradeHistory(int tick, IReadOnlyList<Trade> trades)
    {
        if (trades.Count == 0)
            return;

        BigInteger notional = BigInteger.Zero;
        int volume = 0;
        foreach (var trade in trades)
        {
            notional += (BigInteger)trade.Price * trade.Quantity;
            volume += trade.Quantity;
        }

        if (volume <= 0)
            return;

        _tradeNotionalHistory[tick] = _tradeNotionalHistory.GetValueOrDefault(tick, BigInteger.Zero) + notional;
        _tradeVolumeHistory[tick] = _tradeVolumeHistory.GetValueOrDefault(tick, 0) + volume;
    }

    private bool IsWithinSafePriceBand(long price)
    {
        if (_maxOrderPriceDeviationRatioFromSafePrice <= 0)
            return true;

        long safePrice = GetSafePriceAtTick(_currentTick);
        if (safePrice <= 0)
            return true;

        (long lowerBound, long upperBound) = CalculatePriceBand(safePrice, _maxOrderPriceDeviationRatioFromSafePrice);
        return price >= lowerBound && price <= upperBound;
    }

    private long ClampSafePrice(long candidatePrice, long referencePrice)
    {
        if (_safePriceMaxDailyMoveRatio <= 0 || referencePrice <= 0)
            return Math.Max(1, candidatePrice);

        (long lowerBound, long upperBound) = CalculatePriceBand(referencePrice, _safePriceMaxDailyMoveRatio);
        return Math.Clamp(candidatePrice, lowerBound, upperBound);
    }

    private static (long LowerBound, long UpperBound) CalculatePriceBand(long referencePrice, double ratio)
    {
        decimal safeRatio = Math.Max(0m, (decimal)ratio);
        decimal lowerMultiplier = Math.Max(0m, 1m - safeRatio);
        decimal upperMultiplier = 1m + safeRatio;

        long lowerBound = ClampPositiveDecimal(decimal.Floor(referencePrice * lowerMultiplier));
        long upperBound = ClampPositiveDecimal(decimal.Ceiling(referencePrice * upperMultiplier));
        if (upperBound < lowerBound)
            upperBound = lowerBound;

        return (lowerBound, upperBound);
    }

    private void UpdateCircuitBreaker(long rawMarkPrice, bool wasActiveThisTick)
    {
        if (!_circuitBreakerEnabled || _circuitBreakerDurationTicks <= 0)
            return;

        if (wasActiveThisTick)
        {
            _circuitBreakerTicksRemaining = Math.Max(0, _circuitBreakerTicksRemaining - 1);
            return;
        }

        long referenceSafePrice = _currentTick > 0
            ? GetSafePriceAtTick(_currentTick - 1)
            : _initialGoldPrice;
        if (referenceSafePrice <= 0)
            return;

        decimal moveRatio = Math.Abs((decimal)rawMarkPrice - referenceSafePrice) / referenceSafePrice;
        if (moveRatio >= (decimal)_circuitBreakerTriggerRatio)
            _circuitBreakerTicksRemaining = _circuitBreakerDurationTicks;
    }

    private static long ClampToInt64(BigInteger value)
    {
        if (value > long.MaxValue)
            return long.MaxValue;
        if (value < long.MinValue)
            return long.MinValue;
        return (long)value;
    }

    private static long ClampPositiveDecimal(decimal value)
    {
        if (value < 1)
            return 1;
        if (value > long.MaxValue)
            return long.MaxValue;
        return (long)value;
    }

    private bool ActivateInsiderInfo(Player player, InsiderInfo insider, string? variant)
    {
        int nextNewsDay = _newsSystem.NextNewsTick;
        if (nextNewsDay == int.MaxValue || !insider.CanActivate(_currentTick, nextNewsDay))
            return false;

        bool cheapMode = string.Equals(variant, "cheap", StringComparison.OrdinalIgnoreCase);
        long cost = cheapMode ? insider.CheapCost : insider.PremiumCost;
        if (player.Mora < cost)
            return false;

        bool previewIsFake = false;
        if (cheapMode)
        {
            previewIsFake = player.ConsumePendingCheapInsiderCorruption() || Random.Shared.NextDouble() < 0.5;
        }

        player.AddMora(-cost);
        insider.Activate(player, _currentTick, nextNewsDay, cheapMode, previewIsFake);
        _skillEffectsThisDay.Add(new SkillActivation(
            player.PlayerId,
            insider.Name,
            cheapMode ? $"cheap preview for day {nextNewsDay}" : $"premium preview for day {nextNewsDay}"));
        return true;
    }

    private bool ActivateFlashTrading(Player player, FlashTrading flash)
    {
        if (!flash.CanActivateThisMonth || player.Mora < flash.ActivationCost)
            return false;

        player.AddMora(-flash.ActivationCost);
        flash.OnActivate(player, _currentTick);
        _skillEffectsThisDay.Add(new SkillActivation(player.PlayerId, flash.Name, "gains one extra immediate trade for the next 3 days"));
        return true;
    }

    private bool ActivateStopLossBlade(Player player, StopLossBlade blade)
    {
        if (blade.UsedThisMonth || player.Mora < blade.ActivationCost)
            return false;

        foreach (var order in GetPlayerPendingOrders(player.Token))
        {
            _matchEngine.CancelOrder(player.Token, order.OrderId);
        }

        player.AddMora(-blade.ActivationCost);
        blade.Activate(player, _currentTick, _orderBook.MidPrice);
        _skillEffectsThisDay.Add(new SkillActivation(player.PlayerId, blade.Name, "all open orders cancelled and downside protection enabled"));
        return true;
    }

    private bool ActivateTargetedPurchase(Player player, TargetedPurchase targeted)
    {
        if (targeted.IsUsed)
            return false;

        long bestBid = _orderBook.BestBid ?? _orderBook.MidPrice;
        long discountPrice = Math.Max(1, (long)Math.Floor(bestBid * (1.0 - targeted.DiscountRate)));
        long cost = discountPrice * targeted.PurchaseQuantity;
        if (player.Mora < cost)
            return false;

        player.AddMora(-cost);
        player.AddLockedGold(targeted.PurchaseQuantity, _currentTick + targeted.LockDuration);
        targeted.MarkUsed();
        _skillEffectsThisDay.Add(new SkillActivation(player.PlayerId, targeted.Name, $"bought {targeted.PurchaseQuantity} locked gold at {discountPrice}"));
        return true;
    }

    private bool ActivateNetworkStorm(Player player, NetworkStorm storm, string? targetToken)
    {
        if (!storm.CanUse || string.IsNullOrWhiteSpace(targetToken))
            return false;
        if (!_players.TryGetValue(targetToken, out var targetPlayer))
            return false;
        if (player.Mora < storm.ActivationCost)
            return false;

        player.AddMora(-storm.ActivationCost);
        targetPlayer.AddNextOrderExtraDelayDays(1);
        storm.MarkUsed();
        _skillEffectsThisDay.Add(new SkillActivation(player.PlayerId, storm.Name, "next order delayed by 1 day", targetPlayer.PlayerId));
        return true;
    }

    private bool ActivatePublicOpinionAttack(Player player, PublicOpinionAttack attack)
    {
        if (!attack.CanUse || player.Mora < attack.ActivationCost)
            return false;

        player.AddMora(-attack.ActivationCost);
        attack.MarkUsed();

        var sentiment = Random.Shared.Next(2) == 0
            ? NewsSentiment.Bullish
            : NewsSentiment.Bearish;
        var fakeNews = _newsSystem.InjectFakeNews(_currentTick, player.Token, sentiment);
        _publishedNewsThisDay.Add(fakeNews);

        foreach (var other in _players.Values.Where(other => other.Token != player.Token))
        {
            other.PendingFakeBroadcast = true;
            other.PendingCheapInsiderCorruption = true;
        }

        _skillEffectsThisDay.Add(new SkillActivation(player.PlayerId, attack.Name, "broadcasted a fake market news item"));
        return true;
    }
}
