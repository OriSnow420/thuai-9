namespace Thuai.GameLogic;

public record Trade
{
    private static long _nextId = 1;

    public long TradeId { get; }
    public long BuyOrderId { get; init; }
    public long SellOrderId { get; init; }
    public string BuyerToken { get; init; } = "";
    public string SellerToken { get; init; } = "";
    public long Price { get; init; }
    public int Quantity { get; init; }
    public int Tick { get; init; }
    public long BuyerFee { get; init; }
    public long SellerFee { get; init; }
    public bool IsWashTrade => BuyerToken == SellerToken;

    public Trade() { TradeId = Interlocked.Increment(ref _nextId); }
}
