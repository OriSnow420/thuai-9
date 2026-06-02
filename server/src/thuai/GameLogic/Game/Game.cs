namespace Thuai.GameLogic;

using System.Numerics;
using Thuai.Utility;
using Thuai.GameLogic.StrategyCards;

public record MonthSettlementResult(
    int Month,
    Dictionary<string, long> MonthNavs,
    Dictionary<string, long> CumulativeNavs,
    string WinnerToken,
    string Reason,
    string FinalBonusWinnerToken,
    int FinalBonusPoints);

public partial class Game
{
    private readonly object _lock = new();
    private readonly GameSettings _settings;

    public GameStage Stage { get; private set; } = GameStage.Waiting;
    public int CurrentTick { get; private set; }
    public int CurrentMonthNumber { get; private set; }
    public int CurrentDayNumber { get; private set; }
    public TradingDay? CurrentTradingDay { get; private set; }
    public StrategyCardManager CardManager { get; } = new();
    public MonthSettlementResult? LatestSettlement { get; private set; }
    public bool HasPendingSettlementNotification { get; private set; }
    public bool HasPendingStrategyOptions { get; private set; }
    public Dictionary<string, long> CumulativeNavs { get; } = new();

    /// <summary>Configured number of trading days (ticks) in one month.</summary>
    public int TradingDayTicks => _settings.TradingDayTicks;

    private int _waitingTicksRemaining;
    private int _strategyTicksRemaining;
    private bool _settlementProcessed;
    private readonly Dictionary<string, bool> _playerStrategySelected = new();
    // Number of months each token has actually been settled into CumulativeNavs.
    // Used so a late joiner's net income subtracts the baseline only for the
    // months they actually played, not the whole game's month count.
    private readonly Dictionary<string, int> _monthsSettled = new();

    public Game(GameSettings settings)
    {
        _settings = settings;
        _waitingTicksRemaining = settings.PlayerWaitingTicks;
    }

    public void Initialize()
    {
        Stage = _settings.InfiniteMode ? GameStage.PreparingGame : GameStage.Waiting;
        CurrentTick = 0;
        CurrentMonthNumber = 0;
        CurrentDayNumber = 0;
        CurrentTradingDay = null;
        LatestSettlement = null;
        HasPendingSettlementNotification = false;
        HasPendingStrategyOptions = false;
        _queuedPlayerJoins.Clear();
        _queuedPlayerRemovals.Clear();
        _monthsSettled.Clear();
        CardManager.Reset();
        foreach (var token in Players.Keys.ToList())
        {
            Scoreboard[token] = 0;
            CumulativeNavs[token] = 0;
        }
    }

    public void Tick()
    {
        lock (_lock)
        {
            ProcessQueuedPlayerChanges();

            switch (Stage)
            {
                case GameStage.Waiting:
                    TickWaiting();
                    break;
                case GameStage.PreparingGame:
                    TransitionToStrategySelection();
                    break;
                case GameStage.StrategySelection:
                    TickStrategySelection();
                    break;
                case GameStage.TradingDay:
                    TickTradingDay();
                    break;
                case GameStage.Settlement:
                    TickSettlement();
                    break;
                case GameStage.Finished:
                    break;
            }

            CurrentTick++;
        }

        AfterGameTickEvent?.Invoke(this, new AfterGameTickEventArgs(this));
    }

    private void TickWaiting()
    {
        if (Players.Count >= _settings.MinimumPlayerCount)
        {
            _waitingTicksRemaining--;
            if (_waitingTicksRemaining <= 0)
            {
                Stage = GameStage.PreparingGame;
            }
        }
    }

    private void TransitionToStrategySelection()
    {
        if (!_settings.InfiniteMode && CurrentMonthNumber >= _settings.TradingDayCount)
        {
            Stage = GameStage.Finished;
            return;
        }

        CurrentMonthNumber++;
        CurrentDayNumber = 0;
        _strategyTicksRemaining = _settings.StrategySelectionTicks;

        foreach (var player in Players.Values)
        {
            player.ResetForNewMonth();
            StrategyCardManager.ResetMonthlyCardState(player);
        }

        CardManager.GenerateDraftOptions();
        _playerStrategySelected.Clear();
        foreach (var player in Players.Values)
        {
            _playerStrategySelected[player.Token] = false;
        }

        Stage = GameStage.StrategySelection;
        HasPendingStrategyOptions = true;
    }

    public void MarkStrategyOptionsPublished()
    {
        HasPendingStrategyOptions = false;
    }

    private void TickStrategySelection()
    {
        _strategyTicksRemaining--;

        // Transition when all players have selected or time runs out.
        bool allSelected = _playerStrategySelected.Values.All(v => v);
        if (allSelected || _strategyTicksRemaining <= 0)
        {
            TransitionToTradingDay();
        }
    }

    private void TransitionToTradingDay()
    {
        CurrentTradingDay = new TradingDay(
            Players,
            _settings.TradingDayTicks,
            _settings.InitialGoldPrice,
            _settings.NewsIntervalMin,
            _settings.NewsIntervalMax,
            _settings.ResearchWindowTicks,
            _settings.ResearchSettlementDelay,
            _settings.BaseResearchReward,
            _settings.NpcOrdersPerTick,
            monthNumber: CurrentMonthNumber,
            researchEnabled: _settings.ResearchEnabled,
            maxReportsPerTick: _settings.MaxReportsPerTick,
            maxReportsPerNews: _settings.MaxReportsPerNews,
            scheduledNewsTicks: _settings.ResearchNewsScheduleTicks,
            newsSentimentDurationTicks: _settings.NewsSentimentDurationTicks,
            npcNewsReactionDelayTicks: _settings.NpcNewsReactionDelayTicks,
            settlementTwapWindowTicks: _settings.SettlementTwapWindowTicks,
            markPriceDepthLevels: _settings.MarkPriceDepthLevels,
            markPriceMinLevelQuantity: _settings.MarkPriceMinLevelQuantity,
            markPriceMinOrderAgeTicks: _settings.MarkPriceMinOrderAgeTicks,
            markPriceMaxDeviationRatioFromLastPrice: _settings.MarkPriceMaxDeviationRatioFromLastPrice,
            safePriceMaxDailyMoveRatio: _settings.SafePriceMaxDailyMoveRatio,
            maxOrderPriceDeviationRatioFromSafePrice: _settings.MaxOrderPriceDeviationRatioFromSafePrice,
            researchMaxAbsRewardPerReport: _settings.ResearchMaxAbsRewardPerReport,
            researchPositiveRewardBudgetPerPlayerPerMonth: _settings.ResearchPositiveRewardBudgetPerPlayerPerMonth,
            systemInitialMora: _settings.SystemInitialMora,
            systemInitialGold: _settings.SystemInitialGold,
            systemMaxNetBuyQuantityPerDay: _settings.SystemMaxNetBuyQuantityPerDay,
            systemMaxNetSellQuantityPerDay: _settings.SystemMaxNetSellQuantityPerDay,
            systemMaxGrossTradeQuantityPerDay: _settings.SystemMaxGrossTradeQuantityPerDay,
            circuitBreakerEnabled: _settings.CircuitBreakerEnabled,
            circuitBreakerTriggerRatio: _settings.CircuitBreakerTriggerRatio,
            circuitBreakerDurationTicks: _settings.CircuitBreakerDurationTicks,
            initialLiquidityLevels: _settings.InitialLiquidityLevels,
            initialLiquidityBaseQuantity: _settings.InitialLiquidityBaseQuantity,
            initialLiquidityQuantityStep: _settings.InitialLiquidityQuantityStep
        );
        CurrentTradingDay.Initialize();

        Stage = GameStage.TradingDay;
    }

    private void TickTradingDay()
    {
        CurrentTradingDay?.Tick();
        CurrentDayNumber = CurrentTradingDay?.CurrentTick ?? CurrentDayNumber;

        if (CurrentTradingDay?.IsFinished == true)
        {
            Stage = GameStage.Settlement;
        }
    }

    private void TickSettlement()
    {
        if (!_settlementProcessed)
        {
            if (CurrentTradingDay != null)
            {
                var navs = CurrentTradingDay.CalculateSettlement();
                foreach (var (token, nav) in navs)
                {
                    CumulativeNavs[token] = ClampToInt64((BigInteger)CumulativeNavs.GetValueOrDefault(token, 0) + nav);
                    _monthsSettled[token] = _monthsSettled.GetValueOrDefault(token, 0) + 1;
                }

                LatestSettlement = DetermineMonthResult(navs);
                HasPendingSettlementNotification = true;
            }
            _settlementProcessed = true;
            return;
        }

        _settlementProcessed = false;
        if (!_settings.InfiniteMode && CurrentMonthNumber >= _settings.TradingDayCount)
        {
            Stage = GameStage.Finished;
        }
        else
        {
            TransitionToStrategySelection();
        }
    }

    public void MarkSettlementNotificationPublished()
    {
        HasPendingSettlementNotification = false;
    }

    /// <summary>
    /// Force-exit the Waiting stage. Intended for admin debug use to start
    /// the game without configuring a low PlayerWaitingTicks. Has no effect
    /// outside of Waiting.
    /// </summary>
    public void SkipWaiting()
    {
        lock (_lock)
        {
            if (Stage == GameStage.Waiting && Players.Count >= _settings.MinimumPlayerCount)
                _waitingTicksRemaining = 0;
        }
    }

    /// <summary>
    /// Handle a player's strategy card selection during the draft phase.
    /// Returns true if the selection was accepted.
    /// </summary>
    public bool SelectStrategy(string playerToken, string cardName)
    {
        lock (_lock)
        {
            if (Stage != GameStage.StrategySelection) return false;
            if (!Players.TryGetValue(playerToken, out var player)) return false;
            if (_playerStrategySelected.GetValueOrDefault(playerToken, false)) return false;

            var card = CardManager.SelectCard(player, cardName);
            if (card == null) return false;

            _playerStrategySelected[playerToken] = true;
            return true;
        }
    }

    private MonthSettlementResult DetermineMonthResult(Dictionary<string, long> navs)
    {
        string winnerToken = "";
        string reason = "tie";

        var orderedByNav = navs
            .OrderByDescending(entry => entry.Value)
            .ThenByDescending(entry => Players[entry.Key].MonthlyTradeCount)
            .ToList();

        if (orderedByNav.Count >= 2)
        {
            var top = orderedByNav[0];
            var second = orderedByNav[1];

            if (top.Value > second.Value)
            {
                winnerToken = top.Key;
                reason = "higher NAV";
            }
            else
            {
                int topTrades = Players[top.Key].MonthlyTradeCount;
                int secondTrades = Players[second.Key].MonthlyTradeCount;
                if (topTrades > secondTrades)
                {
                    winnerToken = top.Key;
                    reason = "trade-count tiebreaker";
                }
            }
        }
        else if (orderedByNav.Count == 1)
        {
            winnerToken = orderedByNav[0].Key;
            reason = "only player";
        }

        // Final ranking is based on cumulative net income (累计净收入) across
        // every month played, not on per-month win counts. Each month begins
        // with the same baseline NAV (initial mora + initial gold @ initial
        // price); CumulativeNavs sums the end-of-month NAVs, so subtracting
        // the baseline-per-month yields net income.
        long baselinePerMonth = _settings.InitialMora
            + (long)_settings.InitialGold * _settings.InitialGoldPrice;
        foreach (var (token, cumulative) in CumulativeNavs)
        {
            // Subtract the baseline once per month the player actually played —
            // a late joiner has fewer settled months than the global month count.
            int monthsPlayed = _monthsSettled.GetValueOrDefault(token, 0);
            long netIncome = ClampToInt64((BigInteger)cumulative - (BigInteger)baselinePerMonth * monthsPlayed);
            Scoreboard[token] = netIncome;
        }

        // The "final bonus" winner is reported for the settlement display only;
        // no extra points are awarded — ranking is already encoded in Scoreboard.
        string finalBonusWinnerToken = "";
        if (!_settings.InfiniteMode && CurrentMonthNumber == _settings.TradingDayCount)
        {
            var ranked = CumulativeNavs
                .OrderByDescending(entry => entry.Value)
                .ToList();
            if (ranked.Count >= 1)
                finalBonusWinnerToken = ranked[0].Key;
        }

        return new MonthSettlementResult(
            CurrentMonthNumber,
            new Dictionary<string, long>(navs),
            new Dictionary<string, long>(CumulativeNavs),
            winnerToken,
            reason,
            finalBonusWinnerToken,
            FinalBonusPoints: 0);
    }

    private static long ClampToInt64(BigInteger value)
    {
        if (value > long.MaxValue)
            return long.MaxValue;
        if (value < long.MinValue)
            return long.MinValue;
        return (long)value;
    }
}
