using Microsoft.Extensions.Options;
using PlayOffs.Worker.Contracts;

namespace PlayOffs.Worker.Processing;

public sealed class WorkerService : BackgroundService
{
    private readonly WorkerOptions _options;
    private readonly IEventInboxReader _eventInboxReader;
    private readonly IEventRouter _router;
    private readonly ILogger<WorkerService> _logger;

    public WorkerService(
        IOptions<WorkerOptions> options,
        IEventInboxReader eventInboxReader,
        IEventRouter router,
        ILogger<WorkerService> logger)
    {
        _options = options.Value;
        _eventInboxReader = eventInboxReader;
        _router = router;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Worker started with poll interval {Poll}s", _options.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var events = await _eventInboxReader.FetchPendingAsync(_options.BatchSize, stoppingToken);
                foreach (var inboxEvent in events)
                {
                    await _router.RouteAsync(inboxEvent, stoppingToken);
                    await _eventInboxReader.MarkProcessedAsync(inboxEvent.Id, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected worker loop error");
            }

            await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
        }
    }
}
