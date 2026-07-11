namespace PlayOffs.Worker.Contracts;

public interface IReadModelStore
{
    Task SaveStandingsAsync(int championshipId, string jsonPayload, CancellationToken ct);
    Task SaveCardsAsync(int championshipId, string jsonPayload, CancellationToken ct);
    Task SaveStrikersAsync(int championshipId, string jsonPayload, CancellationToken ct);
}
