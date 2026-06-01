namespace Thuai.GameLogic;

/// <summary>
/// NPC trader (系统做市商) that generates symmetric limit orders each tick to
/// provide liquidity. News nudges the quote center slightly, but the NPC no
/// longer chases price direction or crosses the spread.
/// </summary>
public class NPCTrader
{
    private readonly Random _rng = new();
    private readonly int _ordersPerTick;

    /// <summary>
    /// Creates an NPC trader that generates approximately <paramref name="ordersPerTick"/>
    /// orders each tick (actual count varies by +/- 1 for realism).
    /// </summary>
    public NPCTrader(int ordersPerTick = 3)
    {
        _ordersPerTick = ordersPerTick;
    }

    /// <summary>
    /// Generate NPC orders for the current tick. The maker always refreshes both
    /// sides of the book, while sentiment only shifts the quote center a little.
    /// </summary>
    public void GenerateOrders(MatchEngine engine, OrderBook orderBook,
        NewsSentiment? sentiment, int currentTick)
    {
        if (_ordersPerTick <= 0)
            return;

        long mid = orderBook.MidPrice;

        // If both sides are empty and no last trade, skip — we have no price reference.
        if (mid <= 0)
            return;

        int pairedQuotes = _ordersPerTick / 2;
        for (int level = 0; level < pairedQuotes; level++)
        {
            SubmitQuote(engine, orderBook, OrderSide.Buy, mid, sentiment, currentTick, level);
            SubmitQuote(engine, orderBook, OrderSide.Sell, mid, sentiment, currentTick, level);
        }

        if (_ordersPerTick % 2 == 1)
            SubmitQuote(engine, orderBook, ChooseExtraSide(orderBook), mid, sentiment, currentTick, pairedQuotes);
    }

    private void SubmitQuote(
        MatchEngine engine,
        OrderBook orderBook,
        OrderSide side,
        long mid,
        NewsSentiment? sentiment,
        int currentTick,
        int level)
    {
        int quantity = _rng.Next(4, 13);
        long price = ComputeQuotePrice(side, mid, orderBook, sentiment, level);
        if (price <= 0)
            price = 1;

        engine.SubmitOrder("SYSTEM", side, price, quantity, currentTick);
    }

    private long ComputeQuotePrice(
        OrderSide side,
        long mid,
        OrderBook orderBook,
        NewsSentiment? sentiment,
        int level)
    {
        long centerShift = sentiment switch
        {
            NewsSentiment.Bullish => 1,
            NewsSentiment.Bearish => -1,
            _ => 0
        };
        long quotedMid = Math.Max(1, mid + centerShift);
        long levelOffset = 1 + (level % 3);

        if (side == OrderSide.Buy)
        {
            long buyCap = orderBook.BestAsk.HasValue
                ? orderBook.BestAsk.Value - 1
                : quotedMid - 1;
            long anchor = orderBook.BestBid ?? (quotedMid - levelOffset);
            long price = Math.Min(anchor + _rng.Next(0, 2), quotedMid - levelOffset);
            return Math.Max(1, Math.Min(price, buyCap));
        }

        long sellFloor = orderBook.BestBid.HasValue
            ? orderBook.BestBid.Value + 1
            : quotedMid + 1;
        long sellAnchor = orderBook.BestAsk ?? (quotedMid + levelOffset);
        long sellPrice = Math.Max(sellAnchor - _rng.Next(0, 2), quotedMid + levelOffset);
        return Math.Max(sellFloor, sellPrice);
    }

    private OrderSide ChooseExtraSide(OrderBook orderBook)
    {
        var bestBidLevel = orderBook.GetVisibleBids(1).FirstOrDefault();
        var bestAskLevel = orderBook.GetVisibleAsks(1).FirstOrDefault();

        if (bestBidLevel == default && bestAskLevel == default)
            return OrderSide.Buy;
        if (bestBidLevel == default)
            return OrderSide.Buy;
        if (bestAskLevel == default)
            return OrderSide.Sell;

        if (bestBidLevel.Quantity < bestAskLevel.Quantity)
            return OrderSide.Buy;
        if (bestAskLevel.Quantity < bestBidLevel.Quantity)
            return OrderSide.Sell;

        return _rng.Next(2) == 0 ? OrderSide.Buy : OrderSide.Sell;
    }
}
