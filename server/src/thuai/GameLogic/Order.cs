namespace Thuai.GameLogic;

public class Order
{
    private static long _nextId = 1;

    public long OrderId { get; }
    public string PlayerToken { get; }
    public OrderSide Side { get; }
    public long Price { get; }
    public int Quantity { get; }
    public int RemainingQuantity { get; set; }
    public int SubmitTick { get; }
    public int ArrivalTick { get; }
    public OrderStatus Status { get; set; }
    public bool IsIceberg { get; }
    public int VisibleQuantity => IsIceberg ? Math.Max(1, RemainingQuantity / 10) : RemainingQuantity;

    public Order(string playerToken, OrderSide side, long price, int quantity,
                 int submitTick, int networkDelay, bool isIceberg = false)
    {
        OrderId = Interlocked.Increment(ref _nextId);
        PlayerToken = playerToken;
        Side = side;
        Price = price;
        Quantity = quantity;
        RemainingQuantity = quantity;
        SubmitTick = submitTick;
        ArrivalTick = submitTick + networkDelay;
        Status = OrderStatus.Pending;
        IsIceberg = isIceberg;
    }
}
