using System.Numerics;
using Thuai.GameLogic.StrategyCards;

namespace Thuai.GameLogic;

public class MatchEngine
{
    private const string SystemToken = "SYSTEM";

    private readonly OrderBook _orderBook;
    private readonly Dictionary<string, Player> _players;
    private readonly List<Order> _pendingOrders = new();
    private readonly List<Trade> _tradesThisDay = new();
    private readonly int _systemMaxNetBuyQuantityPerDay;
    private readonly int _systemMaxNetSellQuantityPerDay;
    private readonly int _systemMaxGrossTradeQuantityPerDay;

    private long _systemMora;
    private long _systemFrozenMora;
    private long _systemGold;
    private long _systemFrozenGold;
    private int _systemFlowDay = int.MinValue;
    private int _systemNetBuyQuantityThisDay;
    private int _systemNetSellQuantityThisDay;
    private int _systemGrossTradeQuantityThisDay;

    public event Action<Trade>? OnTradeExecuted;

    public OrderBook OrderBook => _orderBook;
    public IReadOnlyList<Order> PendingOrders => _pendingOrders;

    public MatchEngine(
        OrderBook orderBook,
        Dictionary<string, Player> players,
        long systemInitialMora = 100_000_000,
        long systemInitialGold = 100_000,
        int systemMaxNetBuyQuantityPerDay = 200,
        int systemMaxNetSellQuantityPerDay = 200,
        int systemMaxGrossTradeQuantityPerDay = 300)
    {
        _orderBook = orderBook;
        _players = players;
        _systemMora = Math.Max(0, systemInitialMora);
        _systemGold = Math.Max(0, systemInitialGold);
        _systemMaxNetBuyQuantityPerDay = Math.Max(0, systemMaxNetBuyQuantityPerDay);
        _systemMaxNetSellQuantityPerDay = Math.Max(0, systemMaxNetSellQuantityPerDay);
        _systemMaxGrossTradeQuantityPerDay = Math.Max(0, systemMaxGrossTradeQuantityPerDay);
    }

    public Order? SubmitOrder(string playerToken, OrderSide side, long price, int quantity,
        int currentTick, int networkDelay = 0, int priorityRank = 1, bool isIceberg = false)
    {
        if (price <= 0 || quantity <= 0)
            return null;

        // Reject orders whose notional would overflow Int64. A wrapped-negative
        // notional would slip past the affordability check below and let a buy
        // freeze a negative amount, fabricating Mora. price/quantity are positive here.
        if (price > long.MaxValue / quantity)
            return null;

        Player? player = null;
        if (playerToken != SystemToken)
        {
            if (!_players.TryGetValue(playerToken, out player))
                return null;
        }

        long feeReserve = 0;
        if (playerToken != SystemToken)
        {
            if (side == OrderSide.Buy)
            {
                long notional = price * quantity;
                feeReserve = (long)Math.Ceiling(notional * player!.TransactionFeeRate);
                long totalReserve = notional + feeReserve;
                if (player.Mora < totalReserve)
                    return null;
                player.FreezeMora(totalReserve);
            }
            else
            {
                if (player!.Gold < quantity)
                    return null;
                player.FreezeGold(quantity);
            }
        }
        else if (!TryReserveSystemAssets(side, price, quantity))
        {
            return null;
        }

        var order = new Order(playerToken, side, price, quantity, currentTick, networkDelay, priorityRank, isIceberg)
        {
            FrozenFeeRemaining = feeReserve
        };
        _pendingOrders.Add(order);
        return order;
    }

    public bool CancelOrder(string playerToken, long orderId)
    {
        var pending = _pendingOrders.FirstOrDefault(o => o.OrderId == orderId);
        if (pending != null)
        {
            if (pending.PlayerToken != playerToken)
                return false;

            RefundPendingOrder(pending);
            pending.Status = OrderStatus.Cancelled;
            _pendingOrders.Remove(pending);
            return true;
        }

        var order = _orderBook.GetOrder(orderId);
        if (order == null || order.PlayerToken != playerToken)
            return false;
        if (order.Status is OrderStatus.Filled or OrderStatus.Cancelled)
            return false;

        RefundActiveOrder(order);
        order.Status = OrderStatus.Cancelled;
        _orderBook.RemoveOrder(orderId);
        return true;
    }

    public List<Trade> ProcessDay(int currentDay)
    {
        _tradesThisDay.Clear();
        ResetSystemFlowCountersIfNeeded(currentDay);

        var arrived = _pendingOrders
            .Where(order => order.ArrivalTick <= currentDay)
            .OrderBy(order => order.PriorityRank)
            .ThenBy(order => order.ArrivalTick)
            .ThenBy(order => order.SubmitSequence)
            .ThenBy(order => order.OrderId)
            .ToList();

        _pendingOrders.RemoveAll(order => order.ArrivalTick <= currentDay);

        foreach (var order in arrived)
        {
            ProcessOrder(order, currentDay);
        }

        return new List<Trade>(_tradesThisDay);
    }

    public List<Trade> ProcessTick(int currentTick) => ProcessDay(currentTick);

    public List<Order> GetPendingOrders(string playerToken)
    {
        return _pendingOrders.Where(o => o.PlayerToken == playerToken).ToList();
    }

    private void ProcessOrder(Order order, int currentDay)
    {
        bool marketable = IsMarketable(order);

        if (_players.TryGetValue(order.PlayerToken, out var player))
        {
            bool accepted = marketable ? player.CanPlaceImmediateOrder() : player.CanPlaceRestingOrder();
            if (!accepted)
            {
                RefundPendingOrder(order);
                order.Status = OrderStatus.Cancelled;
                return;
            }

            if (marketable)
                player.MarkImmediateOrder();
            else
                player.MarkRestingOrder();
        }

        order.Intent = marketable ? OrderIntent.Immediate : OrderIntent.Resting;

        while (order.RemainingQuantity > 0)
        {
            var opposite = order.Side == OrderSide.Buy
                ? _orderBook.BestAskOrder
                : _orderBook.BestBidOrder;

            if (opposite == null)
                break;

            bool crosses = order.Side == OrderSide.Buy
                ? order.Price >= opposite.Price
                : order.Price <= opposite.Price;
            if (!crosses)
                break;

            long tradePrice = opposite.Price;
            int tradeQuantity = Math.Min(order.RemainingQuantity, opposite.RemainingQuantity);
            int allowedQuantity = GetAllowedTradeQuantity(order, opposite, tradeQuantity, currentDay);
            if (allowedQuantity <= 0)
            {
                if (opposite.PlayerToken == SystemToken)
                {
                    CancelActiveOrder(opposite);
                    continue;
                }

                break;
            }

            tradeQuantity = allowedQuantity;
            ExecuteTrade(order, opposite, tradePrice, tradeQuantity, currentDay);
        }

        if (order.RemainingQuantity <= 0)
        {
            order.Status = OrderStatus.Filled;
            return;
        }

        if (order.Intent == OrderIntent.Immediate)
        {
            RefundUnfilledImmediate(order);
            order.Status = order.Status == OrderStatus.PartiallyFilled
                ? OrderStatus.PartiallyFilled
                : OrderStatus.Cancelled;
            return;
        }

        order.Status = order.Status == OrderStatus.PartiallyFilled
            ? OrderStatus.PartiallyFilled
            : OrderStatus.Pending;
        _orderBook.AddOrder(order);
    }

    private bool IsMarketable(Order order)
    {
        return order.Side == OrderSide.Buy
            ? _orderBook.BestAsk is long ask && order.Price >= ask
            : _orderBook.BestBid is long bid && order.Price <= bid;
    }

    private void ExecuteTrade(Order taker, Order maker, long price, int quantity, int currentDay)
    {
        taker.RemainingQuantity -= quantity;
        maker.RemainingQuantity -= quantity;

        taker.Status = taker.RemainingQuantity == 0
            ? OrderStatus.Filled
            : OrderStatus.PartiallyFilled;
        maker.Status = maker.RemainingQuantity == 0
            ? OrderStatus.Filled
            : OrderStatus.PartiallyFilled;

        if (maker.Status == OrderStatus.Filled)
            _orderBook.RemoveOrder(maker.OrderId);

        long tradeAmount = price * quantity;
        long buyerFee = CalculateFee(taker.Side == OrderSide.Buy ? taker.PlayerToken : maker.PlayerToken, tradeAmount);
        long sellerFee = CalculateFee(taker.Side == OrderSide.Sell ? taker.PlayerToken : maker.PlayerToken, tradeAmount);

        if (taker.Side == OrderSide.Buy)
            ApplyBuyerFill(taker, price, quantity, buyerFee);
        else
            ApplySellerFill(taker, price, quantity, sellerFee);

        if (maker.Side == OrderSide.Buy)
            ApplyBuyerFill(maker, price, quantity, buyerFee);
        else
            ApplySellerFill(maker, price, quantity, sellerFee);

        if (taker.PlayerToken != maker.PlayerToken)
        {
            if (taker.PlayerToken != SystemToken && _players.TryGetValue(taker.PlayerToken, out var takerPlayer))
                takerPlayer.AddMonthlyTradeCount();
            if (maker.PlayerToken != SystemToken && _players.TryGetValue(maker.PlayerToken, out var makerTradePlayer))
                makerTradePlayer.AddMonthlyTradeCount();
        }

        _orderBook.UpdateLastPrice(price);
        _orderBook.IncrementVolume(quantity);

        var trade = new Trade
        {
            BuyOrderId = taker.Side == OrderSide.Buy ? taker.OrderId : maker.OrderId,
            SellOrderId = taker.Side == OrderSide.Sell ? taker.OrderId : maker.OrderId,
            BuyerToken = taker.Side == OrderSide.Buy ? taker.PlayerToken : maker.PlayerToken,
            SellerToken = taker.Side == OrderSide.Sell ? taker.PlayerToken : maker.PlayerToken,
            Price = price,
            Quantity = quantity,
            Tick = currentDay,
            BuyerFee = buyerFee,
            SellerFee = sellerFee
        };

        RecordSystemTradeFlow(trade.BuyerToken, trade.SellerToken, quantity, currentDay);

        _tradesThisDay.Add(trade);
        OnTradeExecuted?.Invoke(trade);
    }

    private long CalculateFee(string playerToken, long tradeAmount)
    {
        if (playerToken == SystemToken)
            return 0;

        if (!_players.TryGetValue(playerToken, out var player))
            return 0;

        return (long)(tradeAmount * player.TransactionFeeRate);
    }

    private void ApplyBuyerFill(Order order, long price, int quantity, long fee)
    {
        long pricePortion = order.Price * quantity;
        long tradeAmount = price * quantity;

        if (order.PlayerToken == SystemToken)
        {
            SpendSystemFrozenMora(tradeAmount);
            long systemPriceRefund = pricePortion - tradeAmount;
            if (systemPriceRefund > 0)
                UnfreezeSystemMora(systemPriceRefund);

            AddSystemGold(quantity);
            return;
        }

        if (!_players.TryGetValue(order.PlayerToken, out var player))
            return;

        long feeFromBuffer = Math.Min(fee, order.FrozenFeeRemaining);

        player.SpendFrozenMora(tradeAmount + feeFromBuffer);
        order.FrozenFeeRemaining -= feeFromBuffer;

        long priceRefund = pricePortion - tradeAmount;
        if (priceRefund > 0)
            player.UnfreezeMora(priceRefund);

        long feeShortfall = fee - feeFromBuffer;
        if (feeShortfall > 0)
        {
            long shortfallFromAvailable = Math.Min(feeShortfall, player.Mora);
            if (shortfallFromAvailable > 0)
                player.AddMora(-shortfallFromAvailable);
        }

        if (order.RemainingQuantity == 0 && order.FrozenFeeRemaining > 0)
        {
            player.UnfreezeMora(order.FrozenFeeRemaining);
            order.FrozenFeeRemaining = 0;
        }

        player.AddGold(quantity);
    }

    private void ApplySellerFill(Order order, long price, int quantity, long fee)
    {
        if (order.PlayerToken == SystemToken)
        {
            SpendSystemFrozenGold(quantity);
            long systemProceeds = price * quantity;
            if (systemProceeds > 0)
                AddSystemMora(systemProceeds);
            return;
        }

        if (!_players.TryGetValue(order.PlayerToken, out var player))
            return;

        player.SpendFrozenGold(quantity);
        long proceeds = price * quantity - fee;
        if (proceeds != 0)
            player.AddMora(proceeds);
    }

    private void RefundPendingOrder(Order order)
    {
        if (order.PlayerToken == SystemToken)
        {
            if (order.Side == OrderSide.Buy)
            {
                long refund = order.Price * order.RemainingQuantity;
                if (refund > 0)
                    UnfreezeSystemMora(refund);
            }
            else if (order.RemainingQuantity > 0)
            {
                UnfreezeSystemGold(order.RemainingQuantity);
            }
            return;
        }

        if (!_players.TryGetValue(order.PlayerToken, out var player))
            return;

        if (order.Side == OrderSide.Buy)
        {
            long refund = order.Price * order.RemainingQuantity + order.FrozenFeeRemaining;
            if (refund > 0)
                player.UnfreezeMora(refund);
            order.FrozenFeeRemaining = 0;
        }
        else
        {
            player.UnfreezeGold(order.RemainingQuantity);
        }
    }

    private void RefundActiveOrder(Order order)
    {
        RefundPendingOrder(order);
    }

    private void RefundUnfilledImmediate(Order order)
    {
        if (order.RemainingQuantity <= 0)
            return;

        RefundPendingOrder(order);
    }

    private bool TryReserveSystemAssets(OrderSide side, long price, int quantity)
    {
        if (side == OrderSide.Buy)
        {
            long notional = price * quantity;
            if (_systemMora < notional)
                return false;

            _systemMora -= notional;
            _systemFrozenMora += notional;
            return true;
        }

        if (_systemGold < quantity)
            return false;

        _systemGold -= quantity;
        _systemFrozenGold += quantity;
        return true;
    }

    private int GetAllowedTradeQuantity(Order taker, Order maker, int requestedQuantity, int currentDay)
    {
        ResetSystemFlowCountersIfNeeded(currentDay);

        string buyerToken = taker.Side == OrderSide.Buy ? taker.PlayerToken : maker.PlayerToken;
        string sellerToken = taker.Side == OrderSide.Sell ? taker.PlayerToken : maker.PlayerToken;
        if (buyerToken != SystemToken && sellerToken != SystemToken)
            return requestedQuantity;

        int allowedQuantity = requestedQuantity;
        if (_systemMaxGrossTradeQuantityPerDay > 0)
        {
            allowedQuantity = Math.Min(
                allowedQuantity,
                Math.Max(0, _systemMaxGrossTradeQuantityPerDay - _systemGrossTradeQuantityThisDay));
        }

        if (buyerToken == SystemToken && _systemMaxNetBuyQuantityPerDay > 0)
        {
            allowedQuantity = Math.Min(
                allowedQuantity,
                Math.Max(0, _systemMaxNetBuyQuantityPerDay - _systemNetBuyQuantityThisDay));
        }

        if (sellerToken == SystemToken && _systemMaxNetSellQuantityPerDay > 0)
        {
            allowedQuantity = Math.Min(
                allowedQuantity,
                Math.Max(0, _systemMaxNetSellQuantityPerDay - _systemNetSellQuantityThisDay));
        }

        return allowedQuantity;
    }

    private void RecordSystemTradeFlow(string buyerToken, string sellerToken, int quantity, int currentDay)
    {
        ResetSystemFlowCountersIfNeeded(currentDay);

        if (buyerToken != SystemToken && sellerToken != SystemToken)
            return;

        _systemGrossTradeQuantityThisDay += quantity;
        if (buyerToken == SystemToken)
            _systemNetBuyQuantityThisDay += quantity;
        if (sellerToken == SystemToken)
            _systemNetSellQuantityThisDay += quantity;
    }

    private void ResetSystemFlowCountersIfNeeded(int currentDay)
    {
        if (_systemFlowDay == currentDay)
            return;

        _systemFlowDay = currentDay;
        _systemNetBuyQuantityThisDay = 0;
        _systemNetSellQuantityThisDay = 0;
        _systemGrossTradeQuantityThisDay = 0;
    }

    private void CancelActiveOrder(Order order)
    {
        RefundActiveOrder(order);
        order.Status = OrderStatus.Cancelled;
        _orderBook.RemoveOrder(order.OrderId);
    }

    private void SpendSystemFrozenMora(long amount)
    {
        _systemFrozenMora -= amount;
    }

    private void UnfreezeSystemMora(long amount)
    {
        _systemFrozenMora -= amount;
        AddSystemMora(amount);
    }

    private void AddSystemMora(long amount)
    {
        _systemMora = ClampToInt64((BigInteger)_systemMora + amount);
    }

    private void SpendSystemFrozenGold(int amount)
    {
        _systemFrozenGold -= amount;
    }

    private void UnfreezeSystemGold(int amount)
    {
        _systemFrozenGold -= amount;
        AddSystemGold(amount);
    }

    private void AddSystemGold(int amount)
    {
        _systemGold = ClampToInt64((BigInteger)_systemGold + amount);
    }

    private static long ClampToInt64(BigInteger value)
    {
        if (value > long.MaxValue)
            return long.MaxValue;
        if (value < long.MinValue)
            return long.MinValue;
        return (long)value;
    }
}
