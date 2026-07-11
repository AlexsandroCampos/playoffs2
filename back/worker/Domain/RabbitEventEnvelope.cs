using System.Text.Json;

namespace PlayOffs.Worker.Domain;

public sealed record RabbitEventEnvelope(
    long? EventId,
    string EventType,
    JsonElement Payload,
    DateTime OccurredAtUtc
);