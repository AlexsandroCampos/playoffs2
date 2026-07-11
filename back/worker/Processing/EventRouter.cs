using PlayOffs.Worker.Contracts;
using PlayOffs.Worker.Domain;

namespace PlayOffs.Worker.Processing;

public sealed class EventRouter : IEventRouter
{
    private readonly IEnumerable<IEventProjectionHandler> _handlers;
    private readonly ILogger<EventRouter> _logger;

    public EventRouter(IEnumerable<IEventProjectionHandler> handlers, ILogger<EventRouter> logger)
    {
        _handlers = handlers;
        _logger = logger;
    }

    public async Task RouteAsync(InboxEvent inboxEvent, CancellationToken ct)
    {
        var matchingHandlers = _handlers.Where(h => h.CanHandle(inboxEvent.EventType)).ToList();

        if (matchingHandlers.Count == 0)
        {
            _logger.LogDebug("No handler for event type {EventType}", inboxEvent.EventType);
            return;
        }

        foreach (var handler in matchingHandlers)
        {
            await handler.HandleAsync(inboxEvent, ct);
        }
    }
}
