using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TradingEngineServer.Core;

if (args is ["--self-test"])
{
    OrderBookChecks.Run();
    CommandReplayChecks.Run();
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

if (args.Length > 0 && args[0] is ("--record-demo" or "--replay"))
{
    try
    {
        if (args.Length != 2)
            throw new InvalidDataException("Usage: --record-demo <new-log-path> or --replay <log-path>");
        ReplayReport report;
        if (args[0] == "--record-demo")
        {
            // CreateNew prevents an accidental overwrite of an existing command log.
            using (var file = new FileStream(args[1], FileMode.CreateNew, FileAccess.Write))
            using (var log = new StreamWriter(file))
                report = CommandReplay.Record(CommandReplay.SyntheticCommands(), log);
            using var verificationLog = File.OpenText(args[1]);
            if (CommandReplay.Replay(verificationLog).Digest != report.Digest)
                throw new InvalidDataException("Recorded commands did not reproduce the original fills and book.");
        }
        else
        {
            using var log = File.OpenText(args[1]);
            report = CommandReplay.Replay(log);
        }
        Console.WriteLine($"Commands={report.Commands} Fills={report.Fills} FilledQuantity={report.FilledQuantity} SuccessfulCancellations={report.SuccessfulCancellations}");
        Console.WriteLine($"Bids={report.Snapshot.Bids.Count} Asks={report.Snapshot.Asks.Count} SHA256={report.Digest}");
    }
    catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or ArgumentException or OverflowException)
    {
        Console.Error.WriteLine($"Replay error: {error.Message}");
        Environment.ExitCode = 1;
    }
    return;
}

using var engine = TradingEngineServerHostBuilder.BuildTradingEngineServer();
TradingEngineServerServiceProvider.ServiceProvider = engine.Services;
{
    using var scope = TradingEngineServerServiceProvider.ServiceProvider.CreateScope();
    await engine.RunAsync().ConfigureAwait(false);
}
