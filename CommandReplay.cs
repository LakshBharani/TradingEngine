using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TradingEngineServer.Core;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SubmitCommand), "submit")]
[JsonDerivedType(typeof(CancelCommand), "cancel")]
internal abstract record BookCommand;
internal sealed record SubmitCommand(
    [property: JsonRequired] Side Side,
    [property: JsonRequired] long PriceTicks,
    [property: JsonRequired] long Quantity) : BookCommand;
internal sealed record CancelCommand([property: JsonRequired] long OrderId) : BookCommand;
internal sealed record ReplayReport(
    long Commands, long Fills, long FilledQuantity, long SuccessfulCancellations,
    OrderBookSnapshot Snapshot, string Digest);

internal static class CommandReplay
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new JsonStringEnumConverter<Side>(allowIntegerValues: false) }
    };

    public static ReplayReport Record(IEnumerable<BookCommand> commands, TextWriter log) =>
        Run(commands, log);

    public static ReplayReport Replay(TextReader log) => Run(ReadCommands(log), null);

    public static IEnumerable<BookCommand> SyntheticCommands() =>
    [
        new SubmitCommand(Side.Sell, 105, 4),
        new SubmitCommand(Side.Sell, 101, 2),
        new SubmitCommand(Side.Sell, 101, 6),
        new SubmitCommand(Side.Buy, 102, 5),
        new CancelCommand(3),
        new SubmitCommand(Side.Buy, 100, 7),
        new SubmitCommand(Side.Buy, 100, 4),
        new SubmitCommand(Side.Sell, 99, 8),
        new CancelCommand(1),
        new CancelCommand(2),
        new CancelCommand(999),
        new SubmitCommand(Side.Sell, 103, 5),
        new SubmitCommand(Side.Buy, 103, 2),
        new SubmitCommand(Side.Buy, 98, 2)
    ];

    private static ReplayReport Run(IEnumerable<BookCommand> commands, TextWriter? log)
    {
        var book = new OrderBook();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long count = 0, fills = 0, filledQuantity = 0, cancellations = 0;
        foreach (var command in commands)
        {
            OrderResult? submission = null;
            bool? cancelled = null;
            switch (command)
            {
                case SubmitCommand submit:
                    submission = book.Submit(submit.Side, submit.PriceTicks, submit.Quantity);
                    foreach (var fill in submission.Fills)
                    {
                        fills = checked(fills + 1);
                        filledQuantity = checked(filledQuantity + fill.Quantity);
                    }
                    break;
                case CancelCommand cancel:
                    cancelled = book.Cancel(cancel.OrderId);
                    if (cancelled.Value)
                        cancellations = checked(cancellations + 1);
                    break;
                default:
                    throw new InvalidDataException("Unknown command type.");
            }

            log?.WriteLine(JsonSerializer.Serialize(command, Options));
            AppendHash(hash, new CommandEvent(command, submission, cancelled));
            count = checked(count + 1);
        }

        var snapshot = book.Snapshot();
        AppendHash(hash, snapshot);
        log?.Flush();
        return new ReplayReport(count, fills, filledQuantity, cancellations, snapshot,
            Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant());
    }

    private static IEnumerable<BookCommand> ReadCommands(TextReader log)
    {
        string? line;
        long lineNumber = 0;
        while ((line = log.ReadLine()) is not null)
        {
            lineNumber++;
            BookCommand command;
            try
            {
                command = JsonSerializer.Deserialize<BookCommand>(line, Options)
                    ?? throw new JsonException("Expected a command object.");
            }
            catch (JsonException error)
            {
                throw new InvalidDataException($"Invalid command on line {lineNumber}.", error);
            }
            // Validate before execution so semantic errors identify their log position.
            if (command is SubmitCommand submit &&
                (submit.PriceTicks <= 0 || submit.Quantity <= 0 ||
                 submit.Side is not (Side.Buy or Side.Sell)))
                throw new InvalidDataException($"Invalid submit command on line {lineNumber}.");
            yield return command;
        }
    }

    private static void AppendHash<T>(IncrementalHash hash, T value)
    {
        hash.AppendData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, Options)));
        hash.AppendData("\n"u8);
    }

    private sealed record CommandEvent(BookCommand Command, OrderResult? Submission, bool? Cancelled);
}
