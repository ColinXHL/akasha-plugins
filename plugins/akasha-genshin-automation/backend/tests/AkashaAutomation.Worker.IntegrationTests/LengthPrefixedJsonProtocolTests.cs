using System.Buffers.Binary;
using System.Text.Json;
using AkashaAutomation.Worker.Bridge;

namespace AkashaAutomation.Worker.IntegrationTests;

public class LengthPrefixedJsonProtocolTests
{
    [Fact]
    public async Task RoundTrip_ShouldPreserveEnvelopeAndPayload()
    {
        var protocol = new LengthPrefixedJsonProtocol();
        await using var stream = new MemoryStream();
        var message = new CompanionEnvelope
        {
            Type = CompanionProtocol.Request,
            CorrelationId = "request-1",
            Method = "worker.echo",
            Payload = JsonSerializer.SerializeToElement(new { text = "你好", count = 3 }),
        };

        await protocol.WriteAsync(stream, message);
        stream.Position = 0;
        var result = await protocol.ReadAsync(stream);

        Assert.Equal(message.Type, result.Type);
        Assert.Equal(message.CorrelationId, result.CorrelationId);
        Assert.Equal(message.Method, result.Method);
        Assert.Equal("你好", result.Payload!.Value.GetProperty("text").GetString());
        Assert.Equal(3, result.Payload.Value.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task Read_ShouldRejectPayloadLargerThanTheLimit()
    {
        var protocol = new LengthPrefixedJsonProtocol(maximumPayloadBytes: 16);
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, 17);
        await using var stream = new MemoryStream(header);

        await Assert.ThrowsAsync<InvalidDataException>(async () => await protocol.ReadAsync(stream));
    }

    [Fact]
    public async Task Read_ShouldRejectTruncatedPayload()
    {
        var header = new byte[sizeof(int)];
        BinaryPrimitives.WriteInt32LittleEndian(header, 10);
        await using var stream = new MemoryStream([.. header, 1, 2, 3]);
        var protocol = new LengthPrefixedJsonProtocol();

        await Assert.ThrowsAsync<EndOfStreamException>(async () => await protocol.ReadAsync(stream));
    }

    [Fact]
    public async Task Write_ShouldRejectPayloadLargerThanTheLimit()
    {
        var protocol = new LengthPrefixedJsonProtocol(maximumPayloadBytes: 128);
        await using var stream = new MemoryStream();
        var message = new CompanionEnvelope
        {
            Type = CompanionProtocol.Event,
            Payload = JsonSerializer.SerializeToElement(new { text = new string('x', 256) }),
        };

        await Assert.ThrowsAsync<InvalidDataException>(async () => await protocol.WriteAsync(stream, message));
        Assert.Empty(stream.ToArray());
    }
}
