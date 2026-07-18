using System.Buffers.Binary;
using System.Text.Json;

namespace AkashaAutomation.Worker.Bridge;

public sealed class LengthPrefixedJsonProtocol(int maximumPayloadBytes = CompanionProtocol.MaximumPayloadBytes)
{
    public int MaximumPayloadBytes { get; } = maximumPayloadBytes > 0
        ? maximumPayloadBytes
        : throw new ArgumentOutOfRangeException(nameof(maximumPayloadBytes));

    public async ValueTask WriteAsync(
        Stream stream,
        CompanionEnvelope message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(message);

        var payload = JsonSerializer.SerializeToUtf8Bytes(message, CompanionProtocol.JsonOptions);
        if (payload.Length is <= 0 || payload.Length > MaximumPayloadBytes)
        {
            throw new InvalidDataException(
                $"Companion payload length {payload.Length} is outside the allowed range 1-{MaximumPayloadBytes}.");
        }

        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, payload.Length);
        await stream.WriteAsync(header, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<CompanionEnvelope> ReadAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);

        var header = new byte[sizeof(int)];
        await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(header);
        if (payloadLength is <= 0 || payloadLength > MaximumPayloadBytes)
        {
            throw new InvalidDataException(
                $"Companion payload length {payloadLength} is outside the allowed range 1-{MaximumPayloadBytes}.");
        }

        var payload = new byte[payloadLength];
        await stream.ReadExactlyAsync(payload, cancellationToken).ConfigureAwait(false);

        return JsonSerializer.Deserialize<CompanionEnvelope>(payload, CompanionProtocol.JsonOptions)
               ?? throw new JsonException("Companion payload deserialized to null.");
    }
}
