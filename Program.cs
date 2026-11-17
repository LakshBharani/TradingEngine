using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TradingEngineServer.Core;

if (args is ["--self-test"])
{
    OrderBookChecks.Run();
    return;
}

if (args is ["--demo"])
{
    var book = new OrderBook();
    book.Submit(Side.Sell, 100, 2);
    var secondSell = book.Submit(Side.Sell, 100, 4);
    var buy = book.Submit(Side.Buy, 102, 5);
    foreach (var fill in buy.Fills)
        Console.WriteLine($"Filled {fill.Quantity} at {fill.PriceTicks} ticks against order {fill.RestingOrderId}.");
    var remaining = book.FindOrder(secondSell.OrderId);
    Console.WriteLine($"Order {secondSell.OrderId} has {remaining!.Value.RemainingQuantity} units remaining.");
    Console.WriteLine($"Cancelled order {secondSell.OrderId}: {book.Cancel(secondSell.OrderId)}.");
    var snapshot = book.Snapshot();
    Console.WriteLine($"Book snapshot: {snapshot.Bids.Count} bids, {snapshot.Asks.Count} asks.");
    return;
}

using var engine = TradingEngineServerHostBuilder.BuildTradingEngineServer();
TradingEngineServerServiceProvider.ServiceProvider = engine.Services;
{
    using var scope = TradingEngineServerServiceProvider.ServiceProvider.CreateScope();
    await engine.RunAsync().ConfigureAwait(false);
}
