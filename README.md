# Limit Order Matching Engine

A C# / .NET 10 exchange simulator. The in-memory order book processes limit orders
for one instrument. Prices use integer ticks, quantities use whole units, and
orders at the same price are filled in submission order. Each fill executes at
the resting order's price. An unfilled remainder rests in the book.

```sh
dotnet run -- --demo
dotnet run -- --self-test
```

The demo submits two sells at the same price, then buys five units. It fills
two units against the first order and three against the second. It then looks
up and cancels the second order's remaining unit and prints an empty snapshot.
The 11 order-book checks cover best price, FIFO, partial fills, resting residuals,
invalid input, cancellation at the head/middle/tail of a level, empty-level
removal, lookup consistency, and detached read-only snapshots.

The matching core is single-threaded and in-memory. The original .NET host
starts and waits for shutdown. Use the CLI flags to run the simulator.

## Cancellation and inspection

- `Cancel(orderId)` removes a resting order's remaining quantity and returns
  `true`. Unknown, already cancelled, and fully filled IDs return `false`.
  Cancellation preserves the FIFO priority of surviving orders.
- `FindOrder(orderId)` returns an immutable value containing the active order's
  ID, side, limit price, and remaining quantity, or `null` for an inactive ID.
- `Snapshot()` copies all active orders into read-only bid and ask lists.
  Bids run from highest to lowest price and asks from lowest to highest.
  Orders within a price level retain FIFO order. Later fills, submissions, and
  cancellations cannot change an earlier snapshot.

Price levels use linked lists with an order-ID index for direct node removal.
Fully filled and cancelled orders leave the index, and empty levels leave the
price tree. All operations, including inspection, use the same sequential caller.

## Deterministic command recording and replay

```sh
dotnet run -- --record-demo synthetic-orders.jsonl
dotnet run -- --replay synthetic-orders.jsonl
```

Recording creates a new JSON Lines file containing a fixed synthetic stream of
14 submit/cancel commands. It then reopens the saved file and verifies that
replay produces the same execution digest. An existing file is rejected to
preserve its contents.

Replay starts with a fresh empty book and runs commands in file order. Order
IDs follow submission order. Each invocation prints command/fill counts, filled
quantity, successful cancellations, final order counts, and a SHA-256 digest
covering every command, its ordered fills or cancellation result, and the final
price/FIFO-ordered book. Line endings do not affect the digest.

The fixed stream exercises best price, FIFO, partial fills, cancellation of a
partially filled order, and failed cancellation of filled/unknown IDs. Its
expected result is 5 fills totaling 15 units, 2 successful cancellations,
2 resting bids, and 1 resting ask. These are correctness fixtures.

Commands use these shapes, with positive integer ticks and quantities:

```json
{"type":"submit","side":"Buy","priceTicks":100,"quantity":2}
{"type":"cancel","orderId":1}
```

Fields are required and case-sensitive, and unknown fields or command types are
rejected. Replay reports the line of malformed or invalid commands and returns
a failing exit code. A recording interrupted by an I/O failure may leave an
incomplete new file. Compare the replay digest with a successfully completed
recording to verify that the same commands, outcomes, and final state were
reproduced.

Five additional replay checks cover synthetic round trips, empty logs, repeated
recordings, invalid log lines, and execution-history digests.

## Reproducible matching benchmark

```sh
dotnet build -c Release
DOTNET_TieredCompilation=0 dotnet bin/Release/net10.0/TradingEngineServer.Core.dll --benchmark 250000 7
```

The benchmark pre-generates a deterministic submit/cancel workload, warms the
matching core, then reports JSON containing per-trial throughput, managed
allocation, collection counts, and a separate latency distribution. Each pass
verifies fills and final state. Optional arguments select cycles and measured
trials, defaulting to 25,000 and five.

[The published local baseline](benchmarks/README.md) documents the Apple M3 Pro
hardware, workload, exact command, measurement boundaries, and raw results.
Four benchmark checks cover workload/replay consistency, measurement
invariants, nearest-rank percentiles, and rejected parameters.

## Validation

```sh
dotnet build
dotnet run --no-build -- --self-test
dotnet run --no-build -- --demo
```

## Next steps

The current roadmap is complete. Cancellation, active-order lookup, snapshots,
deterministic command recording/replay, and a reproducible synthetic benchmark
with throughput, tail latency, allocations, and documented hardware are implemented.
