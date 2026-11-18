namespace TradingEngineServer.Core;

internal static class CommandReplayChecks
{
    public static void Run()
    {
        SyntheticRoundTrip();
        EmptyLog();
        RecordingIsRepeatable();
        InvalidLogsIdentifyTheirLine();
        DigestIncludesExecutionHistory();
        Console.WriteLine("5 command replay checks passed.");
    }

    private static void SyntheticRoundTrip()
    {
        using var log = new StringWriter();
        var original = CommandReplay.Record(CommandReplay.SyntheticCommands(), log);
        Equal(14L, original.Commands);
        Equal(5L, original.Fills);
        Equal(15L, original.FilledQuantity);
        Equal(2L, original.SuccessfulCancellations);
        Equal(true, original.Snapshot.Bids.SequenceEqual(new[] {
            new OrderSnapshot(6, Side.Buy, 100, 3),
            new OrderSnapshot(10, Side.Buy, 98, 2)
        }));
        Equal(new OrderSnapshot(8, Side.Sell, 103, 3), original.Snapshot.Asks.Single());

        foreach (var text in new[] { log.ToString(), log.ToString().ReplaceLineEndings("\r\n") })
        {
            using var reader = new StringReader(text);
            var replay = CommandReplay.Replay(reader);
            Equal(original.Digest, replay.Digest);
            Equal(original.Fills, replay.Fills);
            Equal(original.FilledQuantity, replay.FilledQuantity);
            Equal(original.SuccessfulCancellations, replay.SuccessfulCancellations);
            Equal(true, original.Snapshot.Bids.SequenceEqual(replay.Snapshot.Bids));
            Equal(true, original.Snapshot.Asks.SequenceEqual(replay.Snapshot.Asks));
        }
    }

    private static void EmptyLog()
    {
        using var log = new StringWriter();
        var original = CommandReplay.Record(Array.Empty<BookCommand>(), log);
        using var reader = new StringReader("");
        var replay = CommandReplay.Replay(reader);
        Equal(original.Digest, replay.Digest);
        Equal(0L, replay.Commands);
        Equal(0L, replay.Fills);
        Equal(0, replay.Snapshot.Bids.Count + replay.Snapshot.Asks.Count);
    }

    private static void RecordingIsRepeatable()
    {
        using var first = new StringWriter();
        using var second = new StringWriter();
        var a = CommandReplay.Record(CommandReplay.SyntheticCommands(), first);
        var b = CommandReplay.Record(CommandReplay.SyntheticCommands(), second);
        Equal(first.ToString(), second.ToString());
        Equal(a.Digest, b.Digest);
        Equal(14, first.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries).Length);
    }

    private static void InvalidLogsIdentifyTheirLine()
    {
        var valid = """{"type":"submit","side":"Buy","priceTicks":100,"quantity":2}""";
        string[] invalid = [
            "",
            "null",
            "{",
            """{"type":"unknown"}""",
            """{"type":"submit","priceTicks":100,"quantity":2}""",
            """{"type":"submit","side":"Buy","quantity":2}""",
            """{"type":"submit","side":0,"priceTicks":100,"quantity":2}""",
            """{"type":"submit","side":"Invalid","priceTicks":100,"quantity":2}""",
            """{"type":"submit","side":"Buy","priceTicks":0,"quantity":2}""",
            """{"type":"submit","side":"Buy","priceTicks":100,"quantity":-1}""",
            """{"type":"submit","side":"Buy","priceTicks":100,"quantity":9223372036854775808}""",
            """{"type":"cancel"}""",
            """{"type":"cancel","orderId":1,"typo":true}"""
        ];
        foreach (var command in invalid)
        {
            using var reader = new StringReader(valid + "\n" + command + "\n");
            try { CommandReplay.Replay(reader); }
            catch (InvalidDataException error)
            {
                Equal(true, error.Message.Contains("line 2"));
                continue;
            }
            throw new InvalidOperationException($"Accepted invalid log line: {command}");
        }
    }

    private static void DigestIncludesExecutionHistory()
    {
        // Both runs end with empty books. Fill history must still affect the digest.
        ReplayReport FilledAt(long price)
        {
            using var log = new StringWriter();
            return CommandReplay.Record(new BookCommand[] {
                new SubmitCommand(Side.Sell, price, 2),
                new SubmitCommand(Side.Buy, price, 2)
            }, log);
        }
        var first = FilledAt(100);
        var second = FilledAt(101);
        Equal(0, first.Snapshot.Bids.Count + first.Snapshot.Asks.Count);
        Equal(0, second.Snapshot.Bids.Count + second.Snapshot.Asks.Count);
        Equal(false, first.Digest == second.Digest);
        Equal(1L, first.Fills);
        Equal(2L, first.FilledQuantity);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }
}
