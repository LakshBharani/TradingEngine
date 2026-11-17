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
The 11 self-test checks cover best price, FIFO, partial fills, resting residuals,
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

## Validation

```sh
dotnet build
dotnet run --no-build -- --self-test
dotnet run --no-build -- --demo
```

## Next steps

Completed cancellation, active-order lookup, and book snapshots.

1. Record submitted commands and add deterministic replay with a fixed
   synthetic order stream. Verify that replay produces identical fills and state.
2. Benchmark throughput, tail latency, and allocations with documented hardware
   and workload parameters. Use the baseline to guide performance changes.
