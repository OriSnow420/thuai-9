namespace Thuai.GameLogic.StrategyCards;

/// <summary>
/// Manages the strategy card draft process between trading days.
/// Each draft round: randomly picks 1 card from each category (no repeats across rounds),
/// and both players blindly choose within 40 ticks.
/// </summary>
public class StrategyCardManager
{
    private readonly Random _rng = new();

    private static readonly List<Func<IStrategyCard>> InfrastructureFactory =
    [
        () => new HighFrequencyLine(),
        () => new LowLatencyBoard(),
        () => new InsiderInfo(),
        () => new QuantCluster(),
        () => new FlashTrading()
    ];

    private static readonly List<Func<IStrategyCard>> RiskControlFactory =
    [
        () => new FeeExemption(),
        () => new IcebergOrder(),
        () => new StopLossBlade(),
        () => new TargetedPurchase()
    ];

    private static readonly List<Func<IStrategyCard>> FinTechFactory =
    [
        () => new MaliciousShorting(),
        () => new NetworkDisconnect(),
        () => new DarkPoolTrading(),
        () => new SentimentManipulation()
    ];

    /// <summary>
    /// Cards that have already been offered in previous draft rounds (no repeats per game).
    /// </summary>
    private readonly HashSet<string> _offeredCards = [];

    /// <summary>Current draft option from the Infrastructure category.</summary>
    public IStrategyCard? CurrentInfrastructure { get; private set; }

    /// <summary>Current draft option from the RiskControl category.</summary>
    public IStrategyCard? CurrentRiskControl { get; private set; }

    /// <summary>Current draft option from the FinTech category.</summary>
    public IStrategyCard? CurrentFinTech { get; private set; }

    /// <summary>
    /// Generate new draft options: one random card from each category,
    /// excluding cards that have already been offered in this game.
    /// Returns false if no cards remain in any category.
    /// </summary>
    public bool GenerateDraftOptions()
    {
        CurrentInfrastructure = PickRandom(InfrastructureFactory);
        CurrentRiskControl = PickRandom(RiskControlFactory);
        CurrentFinTech = PickRandom(FinTechFactory);

        if (CurrentInfrastructure != null) _offeredCards.Add(CurrentInfrastructure.Name);
        if (CurrentRiskControl != null) _offeredCards.Add(CurrentRiskControl.Name);
        if (CurrentFinTech != null) _offeredCards.Add(CurrentFinTech.Name);

        return CurrentInfrastructure != null
            || CurrentRiskControl != null
            || CurrentFinTech != null;
    }

    /// <summary>
    /// Player selects a card by name from the current draft options.
    /// The card is acquired (OnAcquire called) and added to the player's ActiveCards.
    /// Both players receive independent card instances — each player gets their own copy.
    /// Returns the card if valid, null if the card name doesn't match any current option.
    /// </summary>
    public IStrategyCard? SelectCard(Player player, string cardName)
    {
        // Find the matching draft option
        IStrategyCard? template = null;
        if (CurrentInfrastructure?.Name == cardName) template = CurrentInfrastructure;
        else if (CurrentRiskControl?.Name == cardName) template = CurrentRiskControl;
        else if (CurrentFinTech?.Name == cardName) template = CurrentFinTech;

        if (template == null) return null;

        // Check the player doesn't already have a card with this exact name
        if (player.ActiveCards.Any(c => c.Name == cardName))
            return null;

        // Create a fresh instance for this player (so each player has independent state)
        IStrategyCard card = CreateFreshInstance(template);
        card.OnAcquire(player);
        player.ActiveCards.Add(card);
        return card;
    }

    /// <summary>
    /// Find an active card on a player by name, for activation during trading.
    /// </summary>
    public static IStrategyCard? FindActiveCard(Player player, string cardName)
    {
        return player.ActiveCards.FirstOrDefault(c => c.Name == cardName);
    }

    /// <summary>
    /// Get a list of the current draft option names (for sending to players).
    /// </summary>
    public List<string> GetCurrentDraftOptionNames()
    {
        var names = new List<string>(3);
        if (CurrentInfrastructure != null) names.Add(CurrentInfrastructure.Name);
        if (CurrentRiskControl != null) names.Add(CurrentRiskControl.Name);
        if (CurrentFinTech != null) names.Add(CurrentFinTech.Name);
        return names;
    }

    /// <summary>
    /// Reset all daily card state for a player (fee exemptions, usage flags, etc.).
    /// Called at the start of each trading day.
    /// </summary>
    public static void ResetDailyCardState(Player player)
    {
        foreach (var card in player.ActiveCards)
        {
            // Reset cooldowns for active cards
            if (!card.IsPassive)
            {
                card.CurrentCooldown = 0;
            }

            // Reset per-day state on specific card types
            switch (card)
            {
                case FlashTrading flash:
                    flash.ResetDaily();
                    break;
                case FeeExemption fee:
                    fee.ResetDaily();
                    break;
                case StopLossBlade stopLoss:
                    stopLoss.ResetDaily();
                    break;
                case TargetedPurchase targeted:
                    targeted.ResetDaily();
                    break;
            }
        }
    }

    /// <summary>
    /// Tick all active cards for a player (decrement cooldowns, check timed effects).
    /// </summary>
    public static void TickCards(Player player, int currentTick)
    {
        foreach (var card in player.ActiveCards)
        {
            card.OnTick(player, currentTick);
        }
    }

    /// <summary>
    /// Reset the manager entirely (for a new game).
    /// </summary>
    public void Reset()
    {
        CurrentInfrastructure = null;
        CurrentRiskControl = null;
        CurrentFinTech = null;
        _offeredCards.Clear();
    }

    private IStrategyCard? PickRandom(List<Func<IStrategyCard>> factories)
    {
        // Filter out factories whose cards have already been offered
        var available = new List<Func<IStrategyCard>>();
        foreach (var factory in factories)
        {
            var sample = factory();
            if (!_offeredCards.Contains(sample.Name))
            {
                available.Add(factory);
            }
        }

        if (available.Count == 0) return null;
        return available[_rng.Next(available.Count)]();
    }

    /// <summary>
    /// Create a fresh instance of a card based on its type, so each player gets
    /// independent mutable state.
    /// </summary>
    private static IStrategyCard CreateFreshInstance(IStrategyCard template)
    {
        return template switch
        {
            HighFrequencyLine => new HighFrequencyLine(),
            LowLatencyBoard => new LowLatencyBoard(),
            InsiderInfo => new InsiderInfo(),
            QuantCluster => new QuantCluster(),
            FlashTrading => new FlashTrading(),
            FeeExemption => new FeeExemption(),
            IcebergOrder => new IcebergOrder(),
            StopLossBlade => new StopLossBlade(),
            TargetedPurchase => new TargetedPurchase(),
            MaliciousShorting => new MaliciousShorting(),
            NetworkDisconnect => new NetworkDisconnect(),
            DarkPoolTrading => new DarkPoolTrading(),
            SentimentManipulation => new SentimentManipulation(),
            _ => throw new InvalidOperationException($"Unknown card type: {template.GetType().Name}")
        };
    }
}
