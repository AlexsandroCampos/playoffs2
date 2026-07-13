using Dapper;
using Npgsql;
using System.Data;

namespace PlayOffsApi.Services;

public sealed record OutboxEventRow(
    long Id,
    string EventType,
    string PayloadJson,
    DateTime OccurredAtUtc
);

public interface IOutboxEventRepository
{
    Task<IReadOnlyList<OutboxEventRow>> ClaimPendingAsync(int batchSize, CancellationToken ct);
    Task MarkPublishedAsync(long eventId, CancellationToken ct);
    Task MarkFailedAsync(long eventId, string errorMessage, CancellationToken ct);
}

public sealed class OutboxEventRepository : IOutboxEventRepository
{
    private const int MaxAttempts = 900;
    private readonly string _connectionString;

    public OutboxEventRepository(IConfiguration configuration, IWebHostEnvironment environment)
    {
        if (environment.IsProduction())
        {
            var pguser = Environment.GetEnvironmentVariable("PGUSER");
            var pgpassword = Environment.GetEnvironmentVariable("PGPASSWORD");
            var pghost = Environment.GetEnvironmentVariable("PGHOST");
            var pgport = Environment.GetEnvironmentVariable("PGPORT");
            var pgdatabase = Environment.GetEnvironmentVariable("PGDATABASE");

            _connectionString = $"User ID={pguser};Password={pgpassword};Host={pghost};Port={pgport};Database={pgdatabase};";
        }
        else
        {
            _connectionString = configuration.GetConnectionString("LOCALHOST");
        }
    }

    public async Task<IReadOnlyList<OutboxEventRow>> ClaimPendingAsync(int batchSize, CancellationToken ct)
    {
        const string query = @"
WITH claimed AS (
    SELECT id
    FROM outbox_events
    WHERE status IN ('Pending', 'Failed')
      AND attempts < @maxAttempts
    ORDER BY id
    FOR UPDATE SKIP LOCKED
    LIMIT @batchSize
)
UPDATE outbox_events o
SET status = 'Publishing',
    attempts = attempts + 1,
    last_error = NULL
FROM claimed
WHERE o.id = claimed.id
RETURNING o.id AS Id, o.event_type AS EventType, o.payload_json::text AS PayloadJson, o.occurred_at AS OccurredAtUtc;";

        await using var connection = new NpgsqlConnection(_connectionString);
        var rows = await connection.QueryAsync<OutboxEventRow>(new CommandDefinition(
            query,
            new { batchSize, maxAttempts = MaxAttempts },
            cancellationToken: ct));

        return rows.ToList();
    }

    public async Task MarkPublishedAsync(long eventId, CancellationToken ct)
    {
        const string command = @"
UPDATE outbox_events
SET status = 'Published',
    published_at = NOW(),
    last_error = NULL
WHERE id = @eventId;";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(command, new { eventId }, cancellationToken: ct));
    }

    public async Task MarkFailedAsync(long eventId, string errorMessage, CancellationToken ct)
    {
        const string command = @"
UPDATE outbox_events
SET status = CASE WHEN attempts >= @maxAttempts THEN 'Failed' ELSE 'Pending' END,
    last_error = @errorMessage
WHERE id = @eventId;";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.ExecuteAsync(new CommandDefinition(
            command,
            new { eventId, errorMessage, maxAttempts = MaxAttempts },
            cancellationToken: ct));
    }
}