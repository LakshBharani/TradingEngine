namespace TradingEngineServer.Core;

internal enum Side { Buy, Sell }

internal readonly record struct Fill(long RestingOrderId, long PriceTicks, long Quantity);
internal sealed record OrderResult(long OrderId, long RestingQuantity, IReadOnlyList<Fill> Fills);
internal readonly record struct OrderSnapshot(long OrderId, Side Side, long PriceTicks, long RemainingQuantity);
internal sealed record OrderBookSnapshot(IReadOnlyList<OrderSnapshot> Bids, IReadOnlyList<OrderSnapshot> Asks);

// One instrument. Callers submit commands sequentially so order IDs define time priority.
internal sealed class OrderBook
{
    private readonly SortedDictionary<long, LinkedList<Order>> _bids =
        new(Comparer<long>.Create((left, right) => right.CompareTo(left)));
    private readonly SortedDictionary<long, LinkedList<Order>> _asks = new();
    private readonly Dictionary<long, LinkedListNode<Order>> _orders = new();
    private long _lastOrderId;

    public OrderResult Submit(Side side, long priceTicks, long quantity)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(priceTicks);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(quantity);
        if (side is not (Side.Buy or Side.Sell))
            throw new ArgumentOutOfRangeException(nameof(side));

        var orderId = checked(++_lastOrderId);
        var buys = side == Side.Buy;
        var ownBook = buys ? _bids : _asks;
        var oppositeBook = buys ? _asks : _bids;
        List<Fill>? fills = null;

        while (quantity > 0 && oppositeBook.Count > 0)
        {
            var (bestPrice, level) = oppositeBook.First();
            if (buys ? priceTicks < bestPrice : priceTicks > bestPrice)
                break;

            while (quantity > 0 && level.First is { } first)
            {
                var resting = first.Value;
                var filled = Math.Min(quantity, resting.Remaining);
                quantity -= filled;
                resting.Remaining -= filled;
                (fills ??= new()).Add(new Fill(resting.Id, bestPrice, filled));

                if (resting.Remaining == 0)
                {
                    level.RemoveFirst();
                    _orders.Remove(resting.Id);
                }
            }

            if (level.Count == 0)
                oppositeBook.Remove(bestPrice);
        }

        if (quantity > 0)
        {
            if (!ownBook.TryGetValue(priceTicks, out var level))
            {
                level = new LinkedList<Order>();
                ownBook.Add(priceTicks, level);
            }
            var node = level.AddLast(new Order(orderId, side, priceTicks, quantity));
            _orders.Add(orderId, node);
        }

        return new OrderResult(orderId, quantity, fills ?? (IReadOnlyList<Fill>)Array.Empty<Fill>());
    }

    // Unknown, already cancelled, and fully filled IDs are harmless no-ops.
    public bool Cancel(long orderId)
    {
        if (!_orders.Remove(orderId, out var node))
            return false;

        var order = node.Value;
        var ownBook = order.Side == Side.Buy ? _bids : _asks;
        var level = ownBook[order.PriceTicks];
        level.Remove(node);
        if (level.Count == 0)
            ownBook.Remove(order.PriceTicks);
        return true;
    }

    public OrderSnapshot? FindOrder(long orderId) =>
        _orders.TryGetValue(orderId, out var node) ? node.Value.Snapshot() : null;

    // Copies in matching priority order. Later fills/cancels cannot mutate a snapshot.
    public OrderBookSnapshot Snapshot() =>
        new(SnapshotSide(_bids), SnapshotSide(_asks));

    private static IReadOnlyList<OrderSnapshot> SnapshotSide(SortedDictionary<long, LinkedList<Order>> side)
    {
        var orders = new List<OrderSnapshot>();
        foreach (var level in side.Values)
            foreach (var order in level)
                orders.Add(order.Snapshot());
        return orders.AsReadOnly();
    }

    private sealed class Order(long id, Side side, long priceTicks, long quantity)
    {
        public long Id { get; } = id;
        public Side Side { get; } = side;
        public long PriceTicks { get; } = priceTicks;
        public long Remaining { get; set; } = quantity;
        public OrderSnapshot Snapshot() => new(Id, Side, PriceTicks, Remaining);
    }
}
