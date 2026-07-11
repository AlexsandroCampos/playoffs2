using Microsoft.Extensions.Options;
using Npgsql;
using PlayOffs.Worker.Contracts;
using PlayOffs.Worker.Processing;

namespace PlayOffs.Worker.Infrastructure;

public sealed class PostgresProjectionQueryRepository : IProjectionQueryRepository
{
    private readonly WorkerOptions _options;
    private readonly ILogger<PostgresProjectionQueryRepository> _logger;
    private readonly StandingsBuilderService _standingsBuilder;

    public PostgresProjectionQueryRepository(IOptions<WorkerOptions> options, ILogger<PostgresProjectionQueryRepository> logger, StandingsBuilderService standingsBuilder)
    {
        _options = options.Value;
        _logger = logger;
        _standingsBuilder = standingsBuilder;
    }

    public async Task<int?> ResolveChampionshipIdByMatchIdAsync(int matchId, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_options.Postgres.ConnectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(_options.Postgres.ResolveChampionshipByMatchIdQuery, conn);
        cmd.Parameters.AddWithValue("matchId", matchId);

        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is null || result is DBNull)
        {
            return null;
        }

        return Convert.ToInt32(result);
    }

    public async Task<string?> BuildStandingsJsonAsync(int championshipId, CancellationToken ct)
    {
        return await _standingsBuilder.BuildStandingsJsonAsync(championshipId);
    }

    public async Task<string?> BuildCardsJsonAsync(int championshipId, CancellationToken ct)
    {
        return await ExecuteProjectionJsonAsync(_options.Postgres.BuildCardsJsonQuery, championshipId, ct);
    }

    public async Task<string?> BuildStrikersJsonAsync(int championshipId, CancellationToken ct)
    {
        return await ExecuteProjectionJsonAsync(_options.Postgres.BuildStrikersJsonQuery, championshipId, ct);
    }

    private async Task<string?> ExecuteProjectionJsonAsync(string query, int championshipId, CancellationToken ct)
    {
        await using var conn = new NpgsqlConnection(_options.Postgres.ConnectionString);
        await conn.OpenAsync(ct);

        await using var cmd = new NpgsqlCommand(query, conn);
        cmd.Parameters.AddWithValue("championshipId", championshipId);

        try
        {
            var result = await cmd.ExecuteScalarAsync(ct);
            return result?.ToString();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Projection query failed for championship {ChampionshipId}", championshipId);
            return null;
        }
    }
}
