namespace Thuai.GameLogic;

using System.Numerics;

/// <summary>
/// Limit order book maintaining price-time priority for bids and asks.
/// Bids are sorted by descending price, then fastest priority first.
/// Asks are sorted by ascending price, then fastest priority first.
/// </summary>
public class OrderBook
{
    private readonly int _markPriceDepthLevels;
    private readonly int _markPriceMinLevelQuantity;
    private readonly int _markPriceMinOrderAgeTicks;

    // Bids: highest price first, then lower priority rank, then earliest arrival.
    // SortedSet.Min returns the first element per the comparer, which is the best bid.
    private readonly SortedSet<Order> _bids;

    // Asks: lowest price first, then lower priority rank, then earliest arrival.
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

    public OrderBook(
        long initialPrice,
        int markPriceDepthLevels = 3,
        int markPriceMinLevelQuantity = 20,
        int markPriceMinOrderAgeTicks = 1)
    {
        LastPrice = initialPrice;
        _markPriceDepthLevels = Math.Max(1, markPriceDepthLevels);
        _markPriceMinLevelQuantity = Math.Max(1, markPriceMinLevelQuantity);
        _markPriceMinOrderAgeTicks = Math.Max(0, markPriceMinOrderAgeTicks);

        _bids = new SortedSet<Order>(Comparer<Order>.Create((a, b) =>
        {
            // Descending price: higher price comes first.
            int cmp = b.Price.CompareTo(a.Price);
            if (cmp != 0) return cmp;
            cmp = a.PriorityRank.CompareTo(b.PriorityRank);
            if (cmp != 0) return cmp;
            cmp = a.ArrivalTick.CompareTo(b.ArrivalTick);
            if (cmp != 0) return cmp;
            cmp = a.SubmitSequence.CompareTo(b.SubmitSequence);
            if (cmp != 0) return cmp;
            return a.OrderId.CompareTo(b.OrderId);
        }));

        _asks = new SortedSet<Order>(Comparer<Order>.Create((a, b) =>
        {
            // Ascending price: lower price comes first.
            int cmp = a.Price.CompareTo(b.Price);
            if (cmp != 0) return cmp;
            cmp = a.PriorityRank.CompareTo(b.PriorityRank);
            if (cmp != 0) return cmp;
            cmp = a.ArrivalTick.CompareTo(b.ArrivalTick);
            if (cmp != 0) return cmp;
            cmp = a.SubmitSequence.CompareTo(b.SubmitSequence);
            if (cmp != 0) return cmp;
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

    public long GetMarkPrice(int currentTick, long? fallbackPrice = null)
    {
        long fallback = fallbackPrice ?? MidPrice;
        var bidLevels = AggregateEligibleLevels(_bids, _markPriceDepthLevels, currentTick);
        var askLevels = AggregateEligibleLevels(_asks, _markPriceDepthLevels, currentTick);

        if (bidLevels.Count == 0 || askLevels.Count == 0)
            return fallback;

        long weightedBid = WeightedAverage(bidLevels);
        long weightedAsk = WeightedAverage(askLevels);
        if (weightedBid <= 0 || weightedAsk <= 0 || weightedBid > weightedAsk)
            return fallback;

        var midpoint = (BigInteger)weightedBid + weightedAsk;
        return ClampToInt64((midpoint + 1) / 2);
    }

    /// <summary>
    /// Get aggregated visible bid levels for market data broadcast.
    /// </summary>
    public List<(long Price, int Quantity)> GetVisibleBids(int maxLevels = 10)
    {
        return AggregateVisibleLevels(_bids, maxLevels);
    }

    /// <summary>
    /// Get aggregated visible ask levels for market data broadcast.
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

    private List<(long Price, int Quantity)> AggregateEligibleLevels(
        SortedSet<Order> orders,
        int maxLevels,
        int currentTick)
    {
        var levels = new List<(long Price, int Quantity)>();
        long? currentPrice = null;
        int currentQty = 0;

        foreach (var order in orders)
        {
            if (!IsEligibleForMarkPrice(order, currentTick))
                continue;

            if (currentPrice != order.Price)
            {
                if (currentPrice.HasValue && currentQty >= _markPriceMinLevelQuantity)
                {
                    levels.Add((currentPrice.Value, currentQty));
                    if (levels.Count >= maxLevels)
                        return levels;
                }

                currentPrice = order.Price;
                currentQty = 0;
            }

            currentQty += order.RemainingQuantity;
        }

        if (currentPrice.HasValue && currentQty >= _markPriceMinLevelQuantity && levels.Count < maxLevels)
            levels.Add((currentPrice.Value, currentQty));

        return levels;
    }

    private bool IsEligibleForMarkPrice(Order order, int currentTick)
    {
        return order.RemainingQuantity > 0
            && currentTick - order.ArrivalTick >= _markPriceMinOrderAgeTicks;
    }

    private static long WeightedAverage(IReadOnlyList<(long Price, int Quantity)> levels)
    {
        BigInteger weightedSum = BigInteger.Zero;
        BigInteger totalQuantity = BigInteger.Zero;

        foreach (var (price, quantity) in levels)
        {
            weightedSum += (BigInteger)price * quantity;
            totalQuantity += quantity;
        }

        if (totalQuantity == BigInteger.Zero)
            return 0;

        return ClampToInt64(weightedSum / totalQuantity);
    }

    private static long ClampToInt64(BigInteger value)
    {
        if (value > long.MaxValue)
            return long.MaxValue;
        if (value < long.MinValue)
            return long.MinValue;
        return (long)value;
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
