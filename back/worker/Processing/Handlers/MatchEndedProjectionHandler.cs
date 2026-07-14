using PlayOffs.Worker.Contracts;
using PlayOffs.Worker.Domain;

namespace PlayOffs.Worker.Processing.Handlers;

public sealed class MatchEndedProjectionHandler : IEventProjectionHandler
{
    private static readonly string[] SupportedEventTypes = new[] { "MatchEndedEvent", "match.ended" };

    private readonly IProjectionQueryRepository _projectionQueries;
    private readonly IReadModelStore _readModelStore;
    private readonly ILogger<MatchEndedProjectionHandler> _logger;

    public MatchEndedProjectionHandler(
        IProjectionQueryRepository projectionQueries,
        IReadModelStore readModelStore,
        ILogger<MatchEndedProjectionHandler> logger)
    {
        _projectionQueries = projectionQueries;
        _readModelStore = readModelStore;
        _logger = logger;
    }

    public bool CanHandle(string eventType)
    {
        return SupportedEventTypes.Contains(eventType, StringComparer.OrdinalIgnoreCase);
    }

    public async Task HandleAsync(InboxEvent inboxEvent, CancellationToken ct)
    {
        var championshipId = TryReadInt(inboxEvent.Payload, "championshipId")
                             ?? TryReadInt(inboxEvent.Payload, "ChampionshipId");

        if (championshipId is null)
        {
            var matchId = TryReadInt(inboxEvent.Payload, "matchId")
                          ?? TryReadInt(inboxEvent.Payload, "MatchId");

            if (matchId is null)
            {
                _logger.LogWarning("Event {EventId} has no championshipId or matchId", inboxEvent.Id);
                return;
            }

            championshipId = await _projectionQueries.ResolveChampionshipIdByMatchIdAsync(matchId.Value, ct);
            if (championshipId is null)
            {
                _logger.LogWarning("Could not resolve championship for match {MatchId}", matchId.Value);
                return;
            }
        }

        var standingsJson = await _projectionQueries.BuildStandingsJsonAsync(championshipId.Value, ct);
        if (!string.IsNullOrWhiteSpace(standingsJson) && standingsJson != "[]")
        {
            await _readModelStore.SaveStandingsAsync(championshipId.Value, standingsJson, ct);
        }
        else
        {
            _logger.LogWarning("BuildStandingsJsonAsync não retornou dados para o campeonato {ChampionshipId} — Redis não foi atualizado", championshipId.Value);
        }
    }

    private static int? TryReadInt(System.Text.Json.JsonDocument payload, string propName)
    {
        if (!payload.RootElement.TryGetProperty(propName, out var prop))
        {
            return null;
        }

        return prop.ValueKind switch
        {
            System.Text.Json.JsonValueKind.Number when prop.TryGetInt32(out var asInt) => asInt,
            System.Text.Json.JsonValueKind.String when int.TryParse(prop.GetString(), out var parsed) => parsed,
            _ => null
        };
    }
}
