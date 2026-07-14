using System.Text.Json;
using PlayOffsApi.Services;
using RabbitMQ.Client.Exceptions;

namespace PlayOffsApi.HostedService;

public sealed class OutboxPublisherHostedService : BackgroundService
{
    private readonly IOutboxEventRepository _outboxEvents;
    private readonly IRabbitMqService _rabbitMqService;
    private readonly ILogger<OutboxPublisherHostedService> _logger;

    private const int PollIntervalSeconds = 3;
    private const int BatchSize = 50;

    public OutboxPublisherHostedService(
        IOutboxEventRepository outboxEvents,
        IRabbitMqService rabbitMqService,
        ILogger<OutboxPublisherHostedService> logger)
    {
        _outboxEvents = outboxEvents;
        _rabbitMqService = rabbitMqService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Outbox publisher started");

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var events = await _outboxEvents.ClaimPendingAsync(BatchSize, stoppingToken);

                if (events.Count == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), stoppingToken);
                    continue;
                }

                foreach (var outboxEvent in events)
                {
                    await PublishAsync(outboxEvent, stoppingToken);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Outbox publisher stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while publishing outbox events");
        }
    }

    private async Task PublishAsync(OutboxEventRow outboxEvent, CancellationToken stoppingToken)
    {
        try
        {
            using var payloadDocument = JsonDocument.Parse(outboxEvent.PayloadJson);

            var envelopeJson = JsonSerializer.Serialize(new
            {
                eventId = outboxEvent.Id,
                eventType = outboxEvent.EventType,
                occurredAtUtc = outboxEvent.OccurredAtUtc,
                payload = payloadDocument.RootElement
            });

            await _rabbitMqService.PublishMessageAsync(envelopeJson);
            await _outboxEvents.MarkPublishedAsync(outboxEvent.Id, stoppingToken);
        }
        // 1. FALHAS TRANSITÓRIAS (Infraestrutura) -> Continua tentando com paciência
        catch (Exception ex) when (ex is RabbitMQClientException || ex is IOException || ex is TimeoutException)
        {
            _logger.LogWarning(ex, "Falha de infraestrutura ao publicar o evento {EventId}. Será feita uma nova tentativa futuramente.", outboxEvent.Id);
            await _outboxEvents.MarkFailedAsync(outboxEvent.Id, ex.Message, stoppingToken);
        }
        // 2. FALHAS PERMANENTES (Código/Dados) -> Desiste imediatamente
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro fatal/não-recuperável ao processar o payload do evento {EventId}. Marcando como falha permanente.", outboxEvent.Id);
            await _outboxEvents.MarkPermanentlyFailedAsync(outboxEvent.Id, ex.Message, stoppingToken);
        }
    }
}