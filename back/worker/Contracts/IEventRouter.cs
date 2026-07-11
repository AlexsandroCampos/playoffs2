using PlayOffs.Worker.Domain;

namespace PlayOffs.Worker.Contracts;

public interface IEventRouter
{
    Task RouteAsync(InboxEvent inboxEvent, CancellationToken ct);
}
