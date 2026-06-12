using PlayOffs.Worker.Contracts;
using PlayOffs.Worker.Domain;

namespace PlayOffs.Worker.Processing.Handlers;

/// <summary>
/// Handler para eventos de falta/punição (yellow card, red card, etc).
/// Exemplo de como adicionar novos tipos de eventos ao worker.
/// </summary>
public sealed class FoulCommittedProjectionHandler : IEventProjectionHandler
{
    private static readonly string[] SupportedEventTypes = new[] 
    { 
        "FoulCommittedEvent", 
        "foul.committed",
        "CardIssuedEvent",
        "card.issued"
    };

    private readonly IProjectionQueryRepository _projectionQueries;
    private readonly IReadModelStore _readModelStore;
    private readonly ILogger<FoulCommittedProjectionHandler> _logger;

    public FoulCommittedProjectionHandler(
        IProjectionQueryRepository projectionQueries,
        IReadModelStore readModelStore,
        ILogger<FoulCommittedProjectionHandler> logger)
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
        // Extrai IDs do payload do evento
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

            // Se só tem matchId, resolve qual campeonato é
            championshipId = await _projectionQueries.ResolveChampionshipIdByMatchIdAsync(matchId.Value, ct);
            if (championshipId is null)
            {
                _logger.LogWarning("Could not resolve championship for match {MatchId}", matchId.Value);
                return;
            }
        }

        // Recalcula as estatísticas de cartões
        var cardsJson = await _projectionQueries.BuildCardsJsonAsync(championshipId.Value, ct);
        if (!string.IsNullOrWhiteSpace(cardsJson))
        {
            // Salva novo snapshot de cartões no Redis
            await _readModelStore.SaveCardsAsync(championshipId.Value, cardsJson, ct);
        }

        // Também recalcula classificação (pois cartão pode afetar)
        var standingsJson = await _projectionQueries.BuildStandingsJsonAsync(championshipId.Value, ct);
        if (!string.IsNullOrWhiteSpace(standingsJson))
        {
            await _readModelStore.SaveStandingsAsync(championshipId.Value, standingsJson, ct);
        }

        _logger.LogInformation(
            "Processed foul event for championship {ChampionshipId}",
            championshipId.Value);
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
