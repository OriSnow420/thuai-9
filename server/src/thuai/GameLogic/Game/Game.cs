namespace Thuai.GameLogic;

using Thuai.Utility;
using Thuai.GameLogic.StrategyCards;

/// <summary>
/// Master state machine that manages the full game lifecycle:
/// Waiting -> PreparingGame -> (StrategySelection -> TradingDay -> Settlement) x3 -> Finished
/// </summary>
public partial class Game
{
    private readonly object _lock = new();
    private readonly GameSettings _settings;

    public GameStage Stage { get; private set; } = GameStage.Waiting;
    public int CurrentTick { get; private set; }
    public int CurrentDayNumber { get; private set; } // 1-indexed
    public TradingDay? CurrentTradingDay { get; private set; }
    public StrategyCardManager CardManager { get; } = new();

    private int _waitingTicksRemaining;
    private int _strategyTicksRemaining;
    private bool _settlementProcessed;
    private readonly Dictionary<string, bool> _playerStrategySelected = new();

    public Game(GameSettings settings)
    {
        _settings = settings;
        _waitingTicksRemaining = settings.PlayerWaitingTicks;
    }

    public void Initialize()
    {
        Stage = GameStage.Waiting;
        CurrentTick = 0;
        CurrentDayNumber = 0;
    }

    public void Tick()
    {
        lock (_lock)
        {
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
        CurrentDayNumber++;
        _strategyTicksRemaining = _settings.StrategySelectionTicks;

        // Generate draft options for this round.
        CardManager.GenerateDraftOptions();
        _playerStrategySelected.Clear();
        foreach (var player in Players.Values)
        {
            _playerStrategySelected[player.Token] = false;
        }

        Stage = GameStage.StrategySelection;
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
            _settings.NpcOrdersPerTick
        );
        CurrentTradingDay.Initialize();

        Stage = GameStage.TradingDay;
    }

    private void TickTradingDay()
    {
        CurrentTradingDay?.Tick();

        if (CurrentTradingDay?.IsFinished == true)
        {
            Stage = GameStage.Settlement;
        }
    }

    private void TickSettlement()
    {
        if (!_settlementProcessed)
        {
            // First tick in Settlement: calculate NAV and determine the day's winner.
            if (CurrentTradingDay != null)
            {
                var navs = CurrentTradingDay.CalculateSettlement();
                DetermineRoundWinner(navs);
            }
            _settlementProcessed = true;
            return; // Stay in Settlement for one tick so clients can see the results
        }

        // Second tick: transition out
        _settlementProcessed = false;
        if (CurrentDayNumber >= _settings.TradingDayCount)
        {
            Stage = GameStage.Finished;
        }
        else
        {
            TransitionToStrategySelection();
        }
    }

    private void DetermineRoundWinner(Dictionary<string, long> navs)
    {
        if (navs.Count < 2) return;

        var sorted = navs.OrderByDescending(kv => kv.Value).ToList();

        if (sorted[0].Value > sorted[1].Value)
        {
            // Clear winner by NAV.
            Scoreboard[sorted[0].Key] = Scoreboard.GetValueOrDefault(sorted[0].Key, 0) + 1;
        }
        else
        {
            // Tie in NAV -- use total trade count (excluding wash trades) as tiebreaker.
            var p1 = Players[sorted[0].Key];
            var p2 = Players[sorted[1].Key];

            if (p1.TotalTradeCount > p2.TotalTradeCount)
            {
                Scoreboard[p1.Token] = Scoreboard.GetValueOrDefault(p1.Token, 0) + 1;
            }
            else if (p2.TotalTradeCount > p1.TotalTradeCount)
            {
                Scoreboard[p2.Token] = Scoreboard.GetValueOrDefault(p2.Token, 0) + 1;
            }
            // If still tied, no one gets a point for this round.
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
}
