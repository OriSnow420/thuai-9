using Thuai.GameLogic.StrategyCards;

namespace Thuai.GameLogic;

/// <summary>
/// Matching engine that processes limit orders with price-time priority.
/// Handles order submission (with network delay), cancellation, and trade execution.
/// </summary>
public class MatchEngine
{
    private const string SystemToken = "SYSTEM";

    private readonly OrderBook _orderBook;
    private readonly Dictionary<string, Player> _players;
    private readonly List<Order> _pendingOrders = new();
    private readonly List<Trade> _tradesThisTick = new();

    /// <summary>Fired after each trade is executed.</summary>
    public event Action<Trade>? OnTradeExecuted;

    public OrderBook OrderBook => _orderBook;
    public IReadOnlyList<Order> PendingOrders => _pendingOrders;

    public MatchEngine(OrderBook orderBook, Dictionary<string, Player> players)
    {
        _orderBook = orderBook;
        _players = players;
    }

    /// <summary>
    /// Submit a new order. The order enters a pending queue and will arrive at the
    /// order book after the player's network delay elapses.
    /// Returns null if validation fails (insufficient assets, rate limit, invalid params).
    /// </summary>
    public Order? SubmitOrder(string playerToken, OrderSide side, long price, int quantity,
        int currentTick, bool isIceberg = false)
    {
        if (price <= 0 || quantity <= 0)
            return null;

        // System/NPC orders skip all asset and rate-limit checks.
        if (playerToken == SystemToken)
        {
            var sysOrder = new Order(playerToken, side, price, quantity, currentTick, 0, isIceberg);
            _pendingOrders.Add(sysOrder);
            return sysOrder;
        }

        if (!_players.TryGetValue(playerToken, out var player))
            return null;

        if (!player.CanPlaceOrder())
            return null;

        if (side == OrderSide.Buy)
        {
            long cost = price * quantity;
            if (player.Mora < cost)
                return null;
            player.FreezeMora(cost);
        }
        else
        {
            if (player.Gold < quantity)
                return null;
            player.FreezeGold(quantity);
        }

        player.OrdersSentThisTick++;

        var order = new Order(playerToken, side, price, quantity,
            currentTick, player.NetworkDelay, isIceberg);
        _pendingOrders.Add(order);
        return order;
    }

    /// <summary>
    /// Cancel an active order. Unfreezes the remaining frozen assets.
    /// Returns false if the order doesn't exist, belongs to another player,
    /// or is already filled/cancelled.
    /// </summary>
    public bool CancelOrder(string playerToken, long orderId)
    {
        var order = _orderBook.GetOrder(orderId);
        if (order == null)
            return false;
        if (order.PlayerToken != playerToken)
            return false;
        if (order.Status == OrderStatus.Filled || order.Status == OrderStatus.Cancelled)
            return false;

        // Unfreeze assets for the remaining (unfilled) portion.
        if (order.PlayerToken != SystemToken
            && _players.TryGetValue(order.PlayerToken, out var player))
        {
            if (order.Side == OrderSide.Buy)
                player.UnfreezeMora(order.Price * order.RemainingQuantity);
            else
                player.UnfreezeGold(order.RemainingQuantity);
        }

        order.Status = OrderStatus.Cancelled;
        _orderBook.RemoveOrder(orderId);
        return true;
    }

    /// <summary>
    /// Process one tick: move arrived orders into the book, then run the matching loop.
    /// Returns all trades executed during this tick.
    /// </summary>
    public List<Trade> ProcessTick(int currentTick)
    {
        _tradesThisTick.Clear();

        // Partition pending orders into arrived vs still-waiting.
        var arrived = new List<Order>();
        var stillPending = new List<Order>();

        foreach (var order in _pendingOrders)
        {
            if (order.ArrivalTick <= currentTick)
                arrived.Add(order);
            else
                stillPending.Add(order);
        }

        _pendingOrders.Clear();
        _pendingOrders.AddRange(stillPending);

        // Insert arrived orders one at a time and attempt matching after each.
        // This preserves correct price-time priority for orders arriving in the same tick.
        foreach (var order in arrived)
        {
            _orderBook.AddOrder(order);
            MatchOrders(currentTick);
        }

        return new List<Trade>(_tradesThisTick);
    }

    /// <summary>
    /// Continuously match the best bid against the best ask while they cross.
    /// Trade price is the maker's price (the order that arrived earlier).
    /// </summary>
    private void MatchOrders(int currentTick)
    {
        while (true)
        {
            var bestBid = _orderBook.BestBidOrder;
            var bestAsk = _orderBook.BestAskOrder;

            if (bestBid == null || bestAsk == null)
                break;

            if (bestBid.Price < bestAsk.Price)
                break; // No crossing; done.

            // Trade price = maker's price. The maker is the order that was resting
            // in the book first (earlier ArrivalTick). If both arrived in the same tick,
            // use the bid's price (buyer pays their limit, which is >= ask price).
            long tradePrice = bestBid.ArrivalTick <= bestAsk.ArrivalTick
                ? bestBid.Price
                : bestAsk.Price;

            int tradeQuantity = Math.Min(bestBid.RemainingQuantity, bestAsk.RemainingQuantity);

            long tradeAmount = tradePrice * tradeQuantity;
            long buyerFee = CalculateFee(bestBid.PlayerToken, tradeAmount);
            long sellerFee = CalculateFee(bestAsk.PlayerToken, tradeAmount);

            ExecuteTrade(bestBid, bestAsk, tradePrice, tradeQuantity,
                buyerFee, sellerFee, currentTick);
        }
    }

    private long CalculateFee(string playerToken, long tradeAmount)
    {
        if (playerToken == SystemToken)
            return 0;
        if (_players.TryGetValue(playerToken, out var player))
        {
            long rawFee = (long)(tradeAmount * player.TransactionFeeRate);
            var feeCard = player.ActiveCards
                .OfType<StrategyCards.FeeExemption>()
                .FirstOrDefault();
            if (feeCard != null)
                return feeCard.CalculateEffectiveFee(rawFee);
            return rawFee;
        }
        return 0;
    }

    /// <summary>
    /// Execute a single trade between a buy order and a sell order.
    ///
    /// Asset flow for BUYER (non-SYSTEM):
    ///   At order placement: FreezeMora(orderPrice * orderQuantity)
    ///   At trade execution for matchedQty units at tradePrice:
    ///     1. Refund the price difference: UnfreezeMora((orderPrice - tradePrice) * matchedQty)
    ///        (if orderPrice > tradePrice, excess frozen Mora returns to available)
    ///     2. Permanently spend: SpendFrozenMora(tradePrice * matchedQty + buyerFee)
    ///        (actual cost + fee are removed from frozen Mora entirely)
    ///     3. If buyerFee > (orderPrice - tradePrice) * matchedQty, the fee partly
    ///        comes from available Mora. Handle via: SpendFrozenMora for the frozen part,
    ///        deduct remainder from Mora.
    ///     4. Receive gold: AddGold(matchedQty)
    ///
    ///   Detailed math:
    ///     frozenForPortion = orderPrice * matchedQty
    ///     actualCostPlusFee = tradePrice * matchedQty + buyerFee
    ///     If frozenForPortion >= actualCostPlusFee:
    ///       SpendFrozenMora(actualCostPlusFee)
    ///       UnfreezeMora(frozenForPortion - actualCostPlusFee)  // refund excess
    ///     Else:
    ///       SpendFrozenMora(frozenForPortion)  // spend all frozen
    ///       Mora -= (actualCostPlusFee - frozenForPortion)  // fee exceeds frozen surplus
    ///       (This case is rare: only when fee > price difference * qty)
    ///
    /// Asset flow for SELLER (non-SYSTEM):
    ///   At order placement: FreezeGold(orderQuantity)
    ///   At trade execution for matchedQty units at tradePrice:
    ///     1. SpendFrozenGold(matchedQty)  -- gold is sold, permanently leaves frozen pool
    ///     2. AddMora(tradePrice * matchedQty - sellerFee)  -- receive proceeds minus fee
    /// </summary>
    private void ExecuteTrade(Order buyOrder, Order sellOrder, long price, int quantity,
        long buyerFee, long sellerFee, int currentTick)
    {
        // --- Update order quantities ---
        buyOrder.RemainingQuantity -= quantity;
        sellOrder.RemainingQuantity -= quantity;

        buyOrder.Status = buyOrder.RemainingQuantity == 0
            ? OrderStatus.Filled
            : OrderStatus.PartiallyFilled;
        sellOrder.Status = sellOrder.RemainingQuantity == 0
            ? OrderStatus.Filled
            : OrderStatus.PartiallyFilled;

        // Remove fully filled orders from the book.
        if (buyOrder.Status == OrderStatus.Filled)
            _orderBook.RemoveOrder(buyOrder.OrderId);
        if (sellOrder.Status == OrderStatus.Filled)
            _orderBook.RemoveOrder(sellOrder.OrderId);

        // --- Transfer buyer assets ---
        if (buyOrder.PlayerToken != SystemToken
            && _players.TryGetValue(buyOrder.PlayerToken, out var buyer))
        {
            long frozenForPortion = buyOrder.Price * quantity;
            long actualCost = price * quantity;
            long totalDeduction = actualCost + buyerFee;

            if (frozenForPortion >= totalDeduction)
            {
                // Common case: frozen amount covers cost + fee; refund the excess.
                buyer.SpendFrozenMora(totalDeduction);
                long refund = frozenForPortion - totalDeduction;
                if (refund > 0)
                    buyer.UnfreezeMora(refund);
            }
            else
            {
                // Edge case: fee makes total exceed frozen amount.
                // Spend everything that was frozen, deduct remainder from available Mora.
                buyer.SpendFrozenMora(frozenForPortion);
                long shortfall = totalDeduction - frozenForPortion;
                buyer.FreezeMora(shortfall);
                buyer.SpendFrozenMora(shortfall);
            }

            buyer.AddGold(quantity);
        }

        // --- Transfer seller assets ---
        if (sellOrder.PlayerToken != SystemToken
            && _players.TryGetValue(sellOrder.PlayerToken, out var seller))
        {
            // Gold was frozen at order placement; now it's sold permanently.
            seller.SpendFrozenGold(quantity);

            // Receive Mora proceeds minus fee.
            long proceeds = price * quantity - sellerFee;
            if (proceeds > 0)
                seller.AddMora(proceeds);
        }

        // Increment trade count only for non-wash, non-system trades
        bool isWashTrade = buyOrder.PlayerToken == sellOrder.PlayerToken;
        if (!isWashTrade)
        {
            if (buyOrder.PlayerToken != SystemToken && _players.TryGetValue(buyOrder.PlayerToken, out var b))
                b.TotalTradeCount++;
            if (sellOrder.PlayerToken != SystemToken && _players.TryGetValue(sellOrder.PlayerToken, out var s))
                s.TotalTradeCount++;
        }

        // --- Update order book state ---
        _orderBook.UpdateLastPrice(price);
        _orderBook.IncrementVolume(quantity);

        // --- Create trade record ---
        var trade = new Trade
        {
            BuyOrderId = buyOrder.OrderId,
            SellOrderId = sellOrder.OrderId,
            BuyerToken = buyOrder.PlayerToken,
            SellerToken = sellOrder.PlayerToken,
            Price = price,
            Quantity = quantity,
            Tick = currentTick,
            BuyerFee = buyerFee,
            SellerFee = sellerFee
        };

        _tradesThisTick.Add(trade);
        OnTradeExecuted?.Invoke(trade);
    }
}
