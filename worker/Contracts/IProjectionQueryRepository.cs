namespace PlayOffs.Worker.Contracts;

public interface IProjectionQueryRepository
{
    Task<int?> ResolveChampionshipIdByMatchIdAsync(int matchId, CancellationToken ct);
    Task<string?> BuildStandingsJsonAsync(int championshipId, CancellationToken ct);
    Task<string?> BuildCardsJsonAsync(int championshipId, CancellationToken ct);
    Task<string?> BuildStrikersJsonAsync(int championshipId, CancellationToken ct);
}
