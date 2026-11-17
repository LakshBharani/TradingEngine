namespace TradingEngineServer.Core;

internal enum Side { Buy, Sell }

internal readonly record struct Fill(long RestingOrderId, long PriceTicks, long Quantity);
internal sealed record OrderResult(long OrderId, long RestingQuantity, IReadOnlyList<Fill> Fills);

// One instrument. Callers submit commands sequentially so order IDs define time priority.
internal sealed class OrderBook
{
    private readonly SortedDictionary<long, Queue<Order>> _bids =
        new(Comparer<long>.Create((left, right) => right.CompareTo(left)));
    private readonly SortedDictionary<long, Queue<Order>> _asks = new();
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

            while (quantity > 0 && level.TryPeek(out var resting))
            {
                var filled = Math.Min(quantity, resting.Remaining);
                quantity -= filled;
                resting.Remaining -= filled;
                (fills ??= new()).Add(new Fill(resting.Id, bestPrice, filled));

                if (resting.Remaining == 0)
                    level.Dequeue();
            }

            if (level.Count == 0)
                oppositeBook.Remove(bestPrice);
        }

        if (quantity > 0)
        {
            if (!ownBook.TryGetValue(priceTicks, out var level))
            {
                level = new Queue<Order>();
                ownBook.Add(priceTicks, level);
            }
            level.Enqueue(new Order(orderId, quantity));
        }

        return new OrderResult(orderId, quantity, fills ?? (IReadOnlyList<Fill>)Array.Empty<Fill>());
    }

    private sealed class Order(long id, long quantity)
    {
        public long Id { get; } = id;
        public long Remaining { get; set; } = quantity;
    }
}
