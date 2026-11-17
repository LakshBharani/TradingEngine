namespace TradingEngineServer.Core;

internal static class OrderBookChecks
{
    public static void Run()
    {
        BestPriceFirst();
        FifoAndPartialFill();
        ResidualOrderRests();
        InvalidOrdersAreRejected();
        Console.WriteLine("4 order book checks passed.");
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
