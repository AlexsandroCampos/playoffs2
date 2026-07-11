namespace PlayOffs.Worker;

public sealed class WorkerOptions
{
    public int PollIntervalSeconds { get; set; } = 3;
    public int BatchSize { get; set; } = 50;
    public RabbitMqOptions RabbitMq { get; set; } = new();
    public PostgresOptions Postgres { get; set; } = new();
    public RedisOptions Redis { get; set; } = new();
}

public sealed class RabbitMqOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string UserName { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string Exchange { get; set; } = "playoffs-exchange";
    public string Queue { get; set; } = "playoffs-queue";
    public string RoutingKey { get; set; } = "playoffs.event";
}

public sealed class PostgresOptions
{
    public string ConnectionString { get; set; } = string.Empty;

    public string ResolveChampionshipByMatchIdQuery { get; set; } =
        "SELECT championshipid FROM matches WHERE id = @matchId";

    public string BuildStandingsJsonQuery { get; set; } =
        "SELECT json_build_object('championshipId', @championshipId, 'updatedAt', NOW())::text";

    public string BuildCardsJsonQuery { get; set; } =
        "SELECT COALESCE(json_agg(row_to_json(t)), '[]'::json)::text FROM ( SELECT COALESCE(f.playerid::text, f.playertempid::text) AS \"playerId\", COUNT(*) FILTER (WHERE f.yellowcard) AS \"yellowCards\", COUNT(*) FILTER (WHERE NOT f.yellowcard) AS \"redCards\" FROM fouls f JOIN matches m ON m.id = f.matchid WHERE m.championshipid = @championshipId AND f.valid = true GROUP BY COALESCE(f.playerid::text, f.playertempid::text) ORDER BY \"yellowCards\" DESC ) t";

    public string BuildStrikersJsonQuery { get; set; } =
        "SELECT json_build_object('championshipId', @championshipId, 'updatedAt', NOW())::text";
}

public sealed class RedisOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public string StandingsKeyPattern { get; set; } = "championship:{0}:standings";
    public string CardsKeyPattern { get; set; } = "championship:{0}:cards";
    public string StrikersKeyPattern { get; set; } = "championship:{0}:strikers";
}
