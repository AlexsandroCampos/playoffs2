namespace PlayOffs.Worker;

public sealed class WorkerOptions
{
    public int PollIntervalSeconds { get; set; } = 3;
    public int BatchSize { get; set; } = 50;
    public string EventSourceMode { get; set; } = "database";
    public PostgresOptions Postgres { get; set; } = new();
    public RedisOptions Redis { get; set; } = new();
}

public sealed class PostgresOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    // These queries are intentionally generic so your teammate can align them
    // with the final outbox/inbox table contract.
    public string FetchPendingEventsQuery { get; set; } =
        "SELECT id, event_type, payload_json, occurred_at FROM outbox_events WHERE status = 'Pending' ORDER BY id LIMIT @batchSize";

    public string MarkEventProcessedCommand { get; set; } =
        "UPDATE outbox_events SET status = 'Processed', published_at = NOW() WHERE id = @id";

    public string ResolveChampionshipByMatchIdQuery { get; set; } =
        "SELECT championshipid FROM matches WHERE id = @matchId";

    public string BuildStandingsJsonQuery { get; set; } =
        "SELECT json_build_object('championshipId', @championshipId, 'updatedAt', NOW())::text";

    public string BuildStrikersJsonQuery { get; set; } =
        "SELECT json_build_object('championshipId', @championshipId, 'updatedAt', NOW())::text";
}

public sealed class RedisOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public string StandingsKeyPattern { get; set; } = "championship:{0}:standings";
    public string StrikersKeyPattern { get; set; } = "championship:{0}:strikers";
}
