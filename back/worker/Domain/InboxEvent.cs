using System.Text.Json;

namespace PlayOffs.Worker.Domain;

public sealed record InboxEvent(
    long Id,
    string EventType,
    JsonDocument Payload,
    DateTime OccurredAtUtc
);
