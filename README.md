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
two units against the first order and three against the second. The checks
cover best price, FIFO, partial fills, resting residuals, and invalid input.

The matching core is single-threaded and in-memory. The original .NET host
starts and waits for shutdown. Use the CLI flags to run the simulator.

## Next steps

1. Add cancellation, an order lookup, and book snapshots. Test cancellation of
   an order in the middle of a price level and after a partial fill.
2. Record submitted commands and add deterministic replay with a fixed
   synthetic order stream. Verify that replay produces identical fills and state.
3. Benchmark throughput, tail latency, and allocations with documented hardware
   and workload parameters. Use the baseline to guide performance changes.
