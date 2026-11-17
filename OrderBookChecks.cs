namespace TradingEngineServer.Core;

internal static class OrderBookChecks
{
    public static void Run()
    {
        BestPriceFirst();
        FifoAndPartialFill();
        ResidualOrderRests();
        InvalidOrdersAreRejected();
        CancelMiddlePreservesFifo();
        CancelPriceLevel();
        CancelAfterPartialFill();
        LookupTracksOrderLifetime();
        SnapshotsFollowMatchingPriority();
        SnapshotsAreDetachedAndReadOnly();
        UnknownCancellationIsANoOp();
        Console.WriteLine("11 order book checks passed.");
    }

    private static void BestPriceFirst()
    {
        var book = new OrderBook();
        book.Submit(Side.Sell, 105, 3);
        var bestAsk = book.Submit(Side.Sell, 101, 2);
        var nextAsk = book.Submit(Side.Sell, 102, 3);
        var buy = book.Submit(Side.Buy, 103, 6);
        Equal(new Fill(bestAsk.OrderId, 101, 2), buy.Fills[0]);
        Equal(new Fill(nextAsk.OrderId, 102, 3), buy.Fills[1]);
        Equal(2, buy.Fills.Count);
        Equal(1L, buy.RestingQuantity);
    }

    private static void FifoAndPartialFill()
    {
        var book = new OrderBook();
        var first = book.Submit(Side.Buy, 100, 4);
        var second = book.Submit(Side.Buy, 100, 6);
        var best = book.Submit(Side.Buy, 103, 2);
        var sell = book.Submit(Side.Sell, 99, 5);
        Equal(new Fill(best.OrderId, 103, 2), sell.Fills[0]);
        Equal(new Fill(first.OrderId, 100, 3), sell.Fills[1]);
        Equal(0L, sell.RestingQuantity);
        var nextSell = book.Submit(Side.Sell, 100, 2);
        Equal(new Fill(first.OrderId, 100, 1), nextSell.Fills[0]);
        Equal(new Fill(second.OrderId, 100, 1), nextSell.Fills[1]);
    }

    private static void ResidualOrderRests()
    {
        var book = new OrderBook();
        book.Submit(Side.Sell, 100, 1);
        var buy = book.Submit(Side.Buy, 101, 4);
        Equal(3L, buy.RestingQuantity);
        Equal(0, book.Submit(Side.Sell, 102, 1).Fills.Count);
        var sell = book.Submit(Side.Sell, 101, 3);
        Equal(new Fill(buy.OrderId, 101, 3), sell.Fills.Single());
        Equal(0L, sell.RestingQuantity);
        Equal(0, book.Submit(Side.Sell, 101, 1).Fills.Count);
    }

    private static void InvalidOrdersAreRejected()
    {
        var book = new OrderBook();
        Reject(() => book.Submit(Side.Buy, 0, 1));
        Reject(() => book.Submit(Side.Buy, 100, -1));
        Reject(() => book.Submit((Side)10, 100, 1));
        Equal(1L, book.Submit(Side.Buy, 100, 1).OrderId);
    }

    private static void CancelMiddlePreservesFifo()
    {
        foreach (var side in new[] { Side.Buy, Side.Sell })
        {
            var book = new OrderBook();
            var first = book.Submit(side, 100, 2);
            var middle = book.Submit(side, 100, 3);
            var last = book.Submit(side, 100, 4);
            Equal(true, book.Cancel(middle.OrderId));
            Equal<OrderSnapshot?>(null, book.FindOrder(middle.OrderId));
            var crossingSide = side == Side.Buy ? Side.Sell : Side.Buy;
            var result = book.Submit(crossingSide, 100, 6);
            Equal(2, result.Fills.Count);
            Equal(new Fill(first.OrderId, 100, 2), result.Fills[0]);
            Equal(new Fill(last.OrderId, 100, 4), result.Fills[1]);
            Equal(0L, result.RestingQuantity);
            Equal(0, book.Snapshot().Bids.Count + book.Snapshot().Asks.Count);
        }
    }

    private static void CancelPriceLevel()
    {
        foreach (var side in new[] { Side.Buy, Side.Sell })
        {
            var book = new OrderBook();
            var bestPrice = side == Side.Buy ? 101L : 99L;
            var best = book.Submit(side, bestPrice, 2);
            var next = book.Submit(side, 100, 3);
            Equal(true, book.Cancel(best.OrderId));
            var crossingSide = side == Side.Buy ? Side.Sell : Side.Buy;
            var fill = book.Submit(crossingSide, 100, 3);
            Equal(new Fill(next.OrderId, 100, 3), fill.Fills.Single());
            Equal(false, book.Cancel(next.OrderId));
            Equal(0, book.Snapshot().Bids.Count + book.Snapshot().Asks.Count);
            // Reusing a removed level must create fresh nodes and lookup entries.
            var fresh = book.Submit(side, bestPrice, 1);
            Equal(true, book.Cancel(fresh.OrderId));
        }
    }

    private static void CancelAfterPartialFill()
    {
        var book = new OrderBook();
        var first = book.Submit(Side.Sell, 100, 5);
        var next = book.Submit(Side.Sell, 100, 2);
        book.Submit(Side.Buy, 100, 3);
        Equal<OrderSnapshot?>(new(first.OrderId, Side.Sell, 100, 2), book.FindOrder(first.OrderId));
        Equal(true, book.Cancel(first.OrderId));
        var buy = book.Submit(Side.Buy, 100, 3);
        Equal(new Fill(next.OrderId, 100, 2), buy.Fills.Single());
        Equal(1L, buy.RestingQuantity);
        Equal<OrderSnapshot?>(new(buy.OrderId, Side.Buy, 100, 1), book.FindOrder(buy.OrderId));
        Equal(true, book.Cancel(buy.OrderId));
    }

    private static void LookupTracksOrderLifetime()
    {
        var book = new OrderBook();
        var sell = book.Submit(Side.Sell, 100, 4);
        var initial = book.FindOrder(sell.OrderId);
        Equal<OrderSnapshot?>(new(sell.OrderId, Side.Sell, 100, 4), initial);
        var partialBuy = book.Submit(Side.Buy, 100, 1);
        Equal<OrderSnapshot?>(null, book.FindOrder(partialBuy.OrderId));
        Equal<OrderSnapshot?>(new(sell.OrderId, Side.Sell, 100, 3), book.FindOrder(sell.OrderId));
        Equal(4L, initial!.Value.RemainingQuantity);
        book.Submit(Side.Buy, 100, 3);
        Equal<OrderSnapshot?>(null, book.FindOrder(sell.OrderId));
        Equal(false, book.Cancel(sell.OrderId));
    }

    private static void SnapshotsFollowMatchingPriority()
    {
        var book = new OrderBook();
        Equal(0, book.Snapshot().Bids.Count + book.Snapshot().Asks.Count);
        var lowBid = book.Submit(Side.Buy, 95, 1);
        var highBid = book.Submit(Side.Buy, 99, 2);
        var sameBid = book.Submit(Side.Buy, 99, 3);
        var highAsk = book.Submit(Side.Sell, 105, 4);
        var lowAsk = book.Submit(Side.Sell, 101, 5);
        var sameAsk = book.Submit(Side.Sell, 101, 6);
        var snapshot = book.Snapshot();
        SequenceEqual(new[] {
            new OrderSnapshot(highBid.OrderId, Side.Buy, 99, 2),
            new OrderSnapshot(sameBid.OrderId, Side.Buy, 99, 3),
            new OrderSnapshot(lowBid.OrderId, Side.Buy, 95, 1),
        }, snapshot.Bids);
        SequenceEqual(new[] {
            new OrderSnapshot(lowAsk.OrderId, Side.Sell, 101, 5),
            new OrderSnapshot(sameAsk.OrderId, Side.Sell, 101, 6),
            new OrderSnapshot(highAsk.OrderId, Side.Sell, 105, 4),
        }, snapshot.Asks);

        // Removing the head or tail of a nonempty level must keep its survivors.
        Equal(true, book.Cancel(highBid.OrderId));
        Equal(true, book.Cancel(sameAsk.OrderId));
        Equal(sameBid.OrderId, book.Snapshot().Bids[0].OrderId);
        Equal(lowAsk.OrderId, book.Snapshot().Asks[0].OrderId);
    }

    private static void SnapshotsAreDetachedAndReadOnly()
    {
        var book = new OrderBook();
        var sell = book.Submit(Side.Sell, 100, 5);
        var snapshot = book.Snapshot();
        book.Submit(Side.Buy, 100, 2);
        Equal(true, book.Cancel(sell.OrderId));
        Equal(5L, snapshot.Asks.Single().RemainingQuantity);
        Equal(0, book.Snapshot().Asks.Count);
        foreach (var orders in new[] { snapshot.Bids, snapshot.Asks })
        {
            var list = (IList<OrderSnapshot>)orders;
            Equal(true, list.IsReadOnly);
            try { list.Clear(); }
            catch (NotSupportedException) { continue; }
            throw new InvalidOperationException("Snapshot collection allowed mutation.");
        }
    }

    private static void UnknownCancellationIsANoOp()
    {
        var book = new OrderBook();
        var bid = book.Submit(Side.Buy, 99, 2);
        var before = book.Snapshot();
        foreach (var id in new[] { 0L, -1L, long.MaxValue, bid.OrderId + 1 })
        {
            Equal(false, book.Cancel(id));
            Equal<OrderSnapshot?>(null, book.FindOrder(id));
        }
        SequenceEqual(before.Bids, book.Snapshot().Bids);
        Equal(true, book.Cancel(bid.OrderId));
        Equal(false, book.Cancel(bid.OrderId));
        Equal(bid.OrderId + 1, book.Submit(Side.Buy, 99, 1).OrderId);
    }

    private static void SequenceEqual(IReadOnlyList<OrderSnapshot> expected, IReadOnlyList<OrderSnapshot> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException("Order snapshots differ.");
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentOutOfRangeException) { return; }
        throw new InvalidOperationException("Expected order to be rejected.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
