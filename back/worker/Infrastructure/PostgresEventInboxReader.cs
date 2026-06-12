using System.Text.Json;
using Microsoft.Extensions.Options;
using Npgsql;
using PlayOffs.Worker.Contracts;
using PlayOffs.Worker.Domain;

namespace PlayOffs.Worker.Infrastructure;

public sealed class PostgresEventInboxReader : IEventInboxReader
{
    private readonly WorkerOptions _options;
    private readonly ILogger<PostgresEventInboxReader> _logger;

    public PostgresEventInboxReader(IOptions<WorkerOptions> options, ILogger<PostgresEventInboxReader> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<InboxEvent>> FetchPendingAsync(int batchSize, CancellationToken ct)
    {
        var output = new List<InboxEvent>();

        await using var conn = new NpgsqlConnection(_options.Postgres.ConnectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(_options.Postgres.FetchPendingEventsQuery, conn);
        cmd.Parameters.AddWithValue("batchSize", batchSize);

        try
        {
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var id = reader.GetInt64(0);
                var eventType = reader.GetString(1);
                var payloadText = reader.GetString(2);
                var occurredAt = reader.GetDateTime(3);
                var payload = JsonDocument.Parse(payloadText);

                output.Add(new InboxEvent(id, eventType, payload, DateTime.SpecifyKind(occurredAt, DateTimeKind.Utc)));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not fetch pending events. Keep running with generic configuration.");
        }

        return output;
    }

    public async Task MarkProcessedAsync(long eventId, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_options.Postgres.ConnectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(_options.Postgres.MarkEventProcessedCommand, conn);
        cmd.Parameters.AddWithValue("id", eventId);

        try
        {
            await cmd.ExecuteNonQueryAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not mark event {EventId} as processed. Check table contract later.", eventId);
        }
    }
}
