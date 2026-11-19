# Local matching benchmark

Recorded on 2026-11-19 using an Apple M3 Pro, 11 physical/logical cores,
18 GiB RAM, macOS 27.2.1, Arm64, .NET SDK 10.0.103, and runtime .NET 10.0.3.
The matching loop runs on one thread.

## Reproduce

```sh
dotnet build -c Release
DOTNET_TieredCompilation=0 dotnet bin/Release/net10.0/TradingEngineServer.Core.dll --benchmark 250000 7
```

The CLI defaults to 25,000 cycles and five trials when arguments are omitted.
The published run uses 250,000 cycles, two complete warmup passes, seven timed
throughput trials, and one separate instrumented latency pass. Every pass
starts with an empty book and verifies fill counts, filled quantity,
successful cancellations, and the final empty book after measurement.

The input contains 2,000,000 pre-generated commands per trial. Each cycle
submits two FIFO sells, crosses them with a partial fill, cancels the remaining
sell, then repeats the pattern on the buy side. Six submits and two successful
cancellations produce four fills totaling 15 units per cycle. Prices vary
deterministically over 101 tick values. The book holds at most two resting
orders at one price level at a time.

Expected totals per pass are 1,000,000 fills, 3,750,000 filled units, and 500,000
successful cancellations. Workload SHA-256 is
`1c5364cb5cc75b5b2081a50d501186044c2d3adef8e4e02dfd2deb117a433c0a`.

## Results

| Measurement | Result |
| --- | --- |
| Median throughput | 15.498 million commands/second |
| Throughput range across seven trials | 14.640–15.743 million commands/second |
| Managed allocation on the executing thread | 184.000068 bytes/command |
| p50 instrumented command latency | 42 ns |
| p95 instrumented command latency | 84 ns |
| p99 instrumented command latency | 208 ns |
| Maximum instrumented command latency | 113.542 microseconds |

[The raw report](local-baseline.json) preserves every trial, collection counts,
runtime details, and timer frequency. The reported assembly version refers to
base revision `e6f2925` because the harness was measured before its commit.
The matching core was unchanged, with `OrderBook.cs` Git blob
`958cb8d350b4e23a4c9e96f76107be5183a95fd0`.

## Measurement boundaries

Throughput times command dispatch, synchronous matching/cancellation, and
consumption of fill results. Command generation, book construction,
snapshots, correctness assertions, hashing, and JSON output occur outside
the timed interval. Throughput trials collect allocation deltas with
`GC.GetAllocatedBytesForCurrentThread`, excluding input preparation and the
book constructor. Collection counts are process-wide.

Latency uses a separate pass with `Stopwatch.GetTimestamp` around dispatch
and matching/cancellation, before result consumption. Percentiles use
nearest rank over all commands. Timer overhead and clock granularity remain
in the samples, and GC/scheduling pauses can affect the maximum.
The mixed command distribution includes both submits and cancellations.

These measurements cover synchronous closed-loop execution on a shallow
synthetic book. Network I/O, persistence, JSON replay, queueing, concurrent
producers, and deeper resting books require separate workloads. Allocation
bytes measure cumulative managed allocation on the executing thread.
