namespace Thuai.GameLogic;

public class ResearchReport
{
    public string PlayerToken { get; init; } = "";
    public int NewsId { get; init; }
    public Prediction Prediction { get; init; }
    public int SubmitTick { get; init; }
    public int TicksUsed { get; init; }

    /// <summary>
    /// Multiplier applied to the time decay factor. Default is 1.0 (normal decay).
    /// For quant cluster, this is 0.5 (decay halved, meaning slower penalty growth).
    /// </summary>
    public double DecayMultiplier { get; init; } = 1.0;

    /// <summary>
    /// The effective research window used for this report's time decay calculation.
    /// Default 50; extended to 80 for quant cluster players.
    /// </summary>
    public int EffectiveWindow { get; init; } = 50;

    public bool? IsCorrect { get; set; }
    public long Reward { get; set; }
}
