using PlayOffs.Worker.Domain;

namespace PlayOffs.Worker.Contracts;

public interface IEventProjectionHandler
{
    bool CanHandle(string eventType);
    Task HandleAsync(InboxEvent inboxEvent, CancellationToken ct);
}
