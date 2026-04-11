namespace Thuai.GameLogic;

/// <summary>
/// Limit order book maintaining price-time priority for bids and asks.
/// Bids are sorted by descending price, then ascending arrival tick.
/// Asks are sorted by ascending price, then ascending arrival tick.
/// </summary>
public class OrderBook
{
    // Bids: highest price first, then earliest arrival, then lowest OrderId as tiebreaker.
    // SortedSet.Min returns the first element per the comparer, which is the best bid.
    private readonly SortedSet<Order> _bids;

    // Asks: lowest price first, then earliest arrival, then lowest OrderId as tiebreaker.
    // SortedSet.Min returns the first element per the comparer, which is the best ask.
    private readonly SortedSet<Order> _asks;

    // Fast O(1) lookup by OrderId.
    private readonly Dictionary<long, Order> _orders = new();

    /// <summary>Last traded price. Used as fallback when no mid-price is available.</summary>
    public long LastPrice { get; private set; }

    /// <summary>Cumulative traded volume (in gold units) across all trades.</summary>
    public int TotalVolume { get; private set; }

    public IReadOnlyCollection<Order> Bids => _bids;
    public IReadOnlyCollection<Order> Asks => _asks;

    /// <summary>Best (highest-price) bid order, or null if no bids.</summary>
    public Order? BestBidOrder => _bids.Count > 0 ? _bids.Min : null;

    /// <summary>Best (lowest-price) ask order, or null if no asks.</summary>
    public Order? BestAskOrder => _asks.Count > 0 ? _asks.Min : null;

    /// <summary>Best (highest) bid price, or null if no bids.</summary>
    public long? BestBid => BestBidOrder?.Price;

    /// <summary>Best (lowest) ask price, or null if no asks.</summary>
    public long? BestAsk => BestAskOrder?.Price;

    /// <summary>
    /// Mid-price between best bid and best ask. Falls back to LastPrice
    /// when either side of the book is empty.
    /// </summary>
    public long MidPrice => (BestBid.HasValue && BestAsk.HasValue)
        ? (BestBid.Value + BestAsk.Value) / 2
        : LastPrice;

    public OrderBook(long initialPrice)
    {
        LastPrice = initialPrice;

        _bids = new SortedSet<Order>(Comparer<Order>.Create((a, b) =>
        {
            // Descending price: higher price comes first.
            int cmp = b.Price.CompareTo(a.Price);
            if (cmp != 0) return cmp;
            // Ascending arrival tick: earlier arrival comes first.
            cmp = a.ArrivalTick.CompareTo(b.ArrivalTick);
            if (cmp != 0) return cmp;
            // Tiebreaker by OrderId to ensure SortedSet treats distinct orders as distinct.
            return a.OrderId.CompareTo(b.OrderId);
        }));

        _asks = new SortedSet<Order>(Comparer<Order>.Create((a, b) =>
        {
            // Ascending price: lower price comes first.
            int cmp = a.Price.CompareTo(b.Price);
            if (cmp != 0) return cmp;
            // Ascending arrival tick: earlier arrival comes first.
            cmp = a.ArrivalTick.CompareTo(b.ArrivalTick);
            if (cmp != 0) return cmp;
            // Tiebreaker by OrderId.
            return a.OrderId.CompareTo(b.OrderId);
        }));
    }

    /// <summary>Add an order to the appropriate side of the book.</summary>
    public void AddOrder(Order order)
    {
        if (order.Side == OrderSide.Buy)
            _bids.Add(order);
        else
            _asks.Add(order);
        _orders[order.OrderId] = order;
    }

    /// <summary>Remove an order from the book by its ID. Returns false if not found.</summary>
    public bool RemoveOrder(long orderId)
    {
        if (!_orders.TryGetValue(orderId, out var order))
            return false;

        if (order.Side == OrderSide.Buy)
            _bids.Remove(order);
        else
            _asks.Remove(order);

        _orders.Remove(orderId);
        return true;
    }

    /// <summary>Look up an order by ID. Returns null if not found.</summary>
    public Order? GetOrder(long orderId)
    {
        _orders.TryGetValue(orderId, out var order);
        return order;
    }

    public void UpdateLastPrice(long price)
    {
        LastPrice = price;
    }

    public void IncrementVolume(int quantity)
    {
        TotalVolume += quantity;
    }

    /// <summary>
    /// Get aggregated visible bid levels for market data broadcast.
    /// Iceberg orders only show their visible portion.
    /// </summary>
    public List<(long Price, int Quantity)> GetVisibleBids(int maxLevels = 10)
    {
        return AggregateVisibleLevels(_bids, maxLevels);
    }

    /// <summary>
    /// Get aggregated visible ask levels for market data broadcast.
    /// Iceberg orders only show their visible portion.
    /// </summary>
    public List<(long Price, int Quantity)> GetVisibleAsks(int maxLevels = 10)
    {
        return AggregateVisibleLevels(_asks, maxLevels);
    }

    private static List<(long Price, int Quantity)> AggregateVisibleLevels(
        SortedSet<Order> orders, int maxLevels)
    {
        var levels = new List<(long Price, int Quantity)>();
        long currentPrice = -1;
        int currentQty = 0;

        foreach (var order in orders)
        {
            if (order.Price != currentPrice)
            {
                if (currentPrice >= 0)
                {
                    levels.Add((currentPrice, currentQty));
                    if (levels.Count >= maxLevels)
                        return levels;
                }
                currentPrice = order.Price;
                currentQty = 0;
            }
            currentQty += order.VisibleQuantity;
        }

        // Flush the last level.
        if (currentPrice >= 0 && levels.Count < maxLevels)
            levels.Add((currentPrice, currentQty));

        return levels;
    }

    /// <summary>Get all active (Pending or PartiallyFilled) orders for a specific player.</summary>
    public List<Order> GetPlayerOrders(string playerToken)
    {
        return _orders.Values
            .Where(o => o.PlayerToken == playerToken
                && (o.Status == OrderStatus.Pending || o.Status == OrderStatus.PartiallyFilled))
            .ToList();
    }

    /// <summary>Clear the entire book and reset volume.</summary>
    public void Clear()
    {
        _bids.Clear();
        _asks.Clear();
        _orders.Clear();
        TotalVolume = 0;
    }
}
