namespace Thuai.GameLogic.StrategyCards;

public enum CardCategory { Infrastructure, RiskControl, FinTech }

public interface IStrategyCard
{
    string Name { get; }
    CardCategory Category { get; }
    string Description { get; }
    bool IsPassive { get; }
    int Cooldown { get; }
    int CurrentCooldown { get; set; }

    void OnAcquire(Player player);
    void OnTick(Player player, int currentTick);
    void OnActivate(Player player, int currentTick);
}

public abstract class StrategyCard : IStrategyCard
{
    public abstract string Name { get; }
    public abstract CardCategory Category { get; }
    public abstract string Description { get; }
    public virtual bool IsPassive => true;
    public virtual int Cooldown => 0;
    public int CurrentCooldown { get; set; }

    public virtual void OnAcquire(Player player) { }

    public virtual void OnTick(Player player, int currentTick)
    {
        if (CurrentCooldown > 0) CurrentCooldown--;
    }

    public virtual void OnActivate(Player player, int currentTick) { }

    public bool CanActivate() => !IsPassive && CurrentCooldown <= 0;
    public void StartCooldown() { CurrentCooldown = Cooldown; }
}
