using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace TradingEngineServer.Core;

internal sealed record ThroughputTrial(
    double ElapsedMilliseconds, double CommandsPerSecond, long AllocatedBytes, double AllocatedBytesPerCommand,
    int Gen0Collections, int Gen1Collections, int Gen2Collections);
internal sealed record CommandLatency(double P50Nanoseconds, double P95Nanoseconds, double P99Nanoseconds, double MaxNanoseconds);
internal sealed record BenchmarkReport(
    string Workload, string WorkloadSha256, int Cycles, int CommandsPerTrial, int WarmupPasses,
    string BuildConfiguration, string Framework, string OperatingSystem, string Architecture, int LogicalProcessors,
    long StopwatchFrequency, string TieredCompilation, string AssemblyVersion,
    long ExpectedFillsPerTrial, long ExpectedFilledQuantityPerTrial, long ExpectedCancellationsPerTrial,
    IReadOnlyList<ThroughputTrial> Throughput, CommandLatency Latency);

internal static class OrderBookBenchmark
{
    private const int CommandsPerCycle = 8;
    private const int Warmups = 2;
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    public static BenchmarkReport Run(int cycles = 25000, int trials = 5)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cycles);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(trials);
        var commands = CreateWorkload(cycles);
        for (var pass = 0; pass < Warmups; pass++)
            Measure(commands, cycles, null);

        var measurements = new List<ThroughputTrial>();
        for (var trial = 0; trial < trials; trial++)
            measurements.Add(Measure(commands, cycles, null));

        // Latency instrumentation runs separately from throughput measurements.
        var samples = new long[commands.Length];
        Measure(commands, cycles, samples);
        return new BenchmarkReport(
            "bounded-fifo-match-cancel-v1", WorkloadDigest(commands), cycles, commands.Length, Warmups,
            Configuration, RuntimeInformation.FrameworkDescription, RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(), Environment.ProcessorCount,
            Stopwatch.Frequency, Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "runtime default",
            Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown",
            checked(cycles * 4L), checked(cycles * 15L), checked(cycles * 2L),
            measurements.AsReadOnly(), Summarize(samples, Stopwatch.Frequency));
    }

    public static BookCommand[] CreateWorkload(int cycles)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(cycles);
        var commands = new BookCommand[checked(cycles * CommandsPerCycle)];
        for (var cycle = 0; cycle < cycles; cycle++)
        {
            // Explicit arithmetic keeps the synthetic prices stable across .NET versions.
            var price = 10000L + unchecked((uint)cycle * 2654435761u + 0x9e3779b9u) % 101;
            var orderId = cycle * 6L;
            var offset = cycle * CommandsPerCycle;
            commands[offset] = new SubmitCommand(Side.Sell, price, 5);
            commands[offset + 1] = new SubmitCommand(Side.Sell, price, 7);
            commands[offset + 2] = new SubmitCommand(Side.Buy, price + 1, 8);
            commands[offset + 3] = new CancelCommand(orderId + 2);
            commands[offset + 4] = new SubmitCommand(Side.Buy, price - 2, 4);
            commands[offset + 5] = new SubmitCommand(Side.Buy, price - 2, 6);
            commands[offset + 6] = new SubmitCommand(Side.Sell, price - 3, 7);
            commands[offset + 7] = new CancelCommand(orderId + 5);
        }
        return commands;
    }

    private static ThroughputTrial Measure(BookCommand[] commands, int cycles, long[]? samples)
    {
        var book = new OrderBook();
        long fills = 0, filledQuantity = 0, cancellations = 0;
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        for (var index = 0; index < commands.Length; index++)
        {
            var operationStarted = samples is null ? 0 : Stopwatch.GetTimestamp();
            OrderResult? submission = null;
            bool cancelled = false;
            switch (commands[index])
            {
                case SubmitCommand submit:
                    submission = book.Submit(submit.Side, submit.PriceTicks, submit.Quantity);
                    break;
                case CancelCommand cancel:
                    cancelled = book.Cancel(cancel.OrderId);
                    break;
                default:
                    throw new InvalidOperationException("Unsupported benchmark command.");
            }
            if (samples is not null)
                samples[index] = Stopwatch.GetTimestamp() - operationStarted;

            if (submission is not null)
            {
                fills += submission.Fills.Count;
                foreach (var fill in submission.Fills)
                    filledQuantity += fill.Quantity;
            }
            if (cancelled)
                cancellations++;
        }
        var elapsedTicks = Stopwatch.GetTimestamp() - started;
        var allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        gen0 = GC.CollectionCount(0) - gen0;
        gen1 = GC.CollectionCount(1) - gen1;
        gen2 = GC.CollectionCount(2) - gen2;

        var snapshot = book.Snapshot();
        if (fills != cycles * 4L || filledQuantity != cycles * 15L ||
            cancellations != cycles * 2L || snapshot.Bids.Count != 0 || snapshot.Asks.Count != 0)
            throw new InvalidOperationException("Benchmark workload produced incorrect fills or final state.");

        var seconds = (double)elapsedTicks / Stopwatch.Frequency;
        return new ThroughputTrial(seconds * 1000, commands.Length / seconds,
            allocated, (double)allocated / commands.Length, gen0, gen1, gen2);
    }

    internal static CommandLatency Summarize(long[] samples, long frequency)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(frequency);
        if (samples.Length == 0)
            throw new ArgumentException("At least one latency sample is required.", nameof(samples));
        Array.Sort(samples);
        double Percentile(double p) => samples[(int)Math.Ceiling(p * samples.Length) - 1] * (1e9 / frequency);
        return new CommandLatency(Percentile(0.50), Percentile(0.95), Percentile(0.99),
            samples[^1] * (1e9 / frequency));
    }

    private static string WorkloadDigest(BookCommand[] commands)
    {
        using var log = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var command in commands)
        {
            var line = command switch
            {
                SubmitCommand submit => FormattableString.Invariant($"S,{(int)submit.Side},{submit.PriceTicks},{submit.Quantity}"),
                CancelCommand cancel => FormattableString.Invariant($"C,{cancel.OrderId}"),
                _ => throw new InvalidOperationException("Unsupported benchmark command.")
            };
            log.Write(line);
            log.Write('\n');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(log.ToString()))).ToLowerInvariant();
    }
}
