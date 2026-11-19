namespace TradingEngineServer.Core;

internal static class OrderBookBenchmarkChecks
{
    public static void Run()
    {
        WorkloadIsDeterministic();
        MeasurementChecksItsWorkload();
        PercentilesUseNearestRank();
        InvalidParametersAreRejected();
        Console.WriteLine("4 benchmark checks passed.");
    }

    private static void WorkloadIsDeterministic()
    {
        var first = OrderBookBenchmark.CreateWorkload(2);
        var second = OrderBookBenchmark.CreateWorkload(2);
        Equal(16, first.Length);
        Equal(true, first.SequenceEqual(second));
        using var log = new StringWriter();
        var report = CommandReplay.Record(first, log);
        Equal(8L, report.Fills);
        Equal(30L, report.FilledQuantity);
        Equal(4L, report.SuccessfulCancellations);
        Equal(0, report.Snapshot.Bids.Count + report.Snapshot.Asks.Count);
    }

    private static void MeasurementChecksItsWorkload()
    {
        var report = OrderBookBenchmark.Run(2, 2);
        Equal(16, report.CommandsPerTrial);
        Equal(2, report.Throughput.Count);
        Equal(8L, report.ExpectedFillsPerTrial);
        Equal(30L, report.ExpectedFilledQuantityPerTrial);
        Equal(4L, report.ExpectedCancellationsPerTrial);
        Equal(64, report.WorkloadSha256.Length);
        foreach (var trial in report.Throughput)
        {
            Equal(true, trial.AllocatedBytes > 0);
            Equal(true, double.IsFinite(trial.CommandsPerSecond) && trial.CommandsPerSecond > 0);
            Equal((double)trial.AllocatedBytes / 16, trial.AllocatedBytesPerCommand);
        }
        Equal(true, report.Latency.P50Nanoseconds <= report.Latency.P95Nanoseconds);
        Equal(true, report.Latency.P95Nanoseconds <= report.Latency.P99Nanoseconds);
        Equal(true, report.Latency.P99Nanoseconds <= report.Latency.MaxNanoseconds);
    }

    private static void PercentilesUseNearestRank()
    {
        var summary = OrderBookBenchmark.Summarize([50, 10, 40, 30, 20], 1000000000);
        Equal(new CommandLatency(30, 50, 50, 50), summary);
    }

    private static void InvalidParametersAreRejected()
    {
        Reject(() => OrderBookBenchmark.Run(0, 1));
        Reject(() => OrderBookBenchmark.Run(1, 0));
        Reject(() => OrderBookBenchmark.CreateWorkload(-1));
        Reject(() => OrderBookBenchmark.Summarize([], 1));
        Reject(() => OrderBookBenchmark.Summarize([1], 0));
    }

    private static void Reject(Action action)
    {
        try { action(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Expected invalid benchmark parameters to be rejected.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
