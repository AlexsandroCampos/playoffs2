using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using PlayOffs.Worker.Contracts;
using PlayOffs.Worker.Domain;
using RabbitMQ.Client;

namespace PlayOffs.Worker.Processing;

public sealed class WorkerService : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly WorkerOptions _options;
    private readonly IEventRouter _router;
    private readonly ILogger<WorkerService> _logger;

    private IConnection? _connection;
    private IChannel? _channel;

    public WorkerService(
        IOptions<WorkerOptions> options,
        IEventRouter router,
        ILogger<WorkerService> logger)
    {
        _options = options.Value;
        _router = router;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Usa o novo loop de reconexão inicial
        await ConnectWithRetryAsync(stoppingToken);

        _logger.LogInformation(
            "Worker started consuming {Queue} from exchange {Exchange}",
            _options.RabbitMq.Queue,
            _options.RabbitMq.Exchange);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var processedCount = 0;

                    for (var index = 0; index < _options.BatchSize && !stoppingToken.IsCancellationRequested; index++)
                    {
                        var result = await _channel!.BasicGetAsync(
                            queue: _options.RabbitMq.Queue,
                            autoAck: false,
                            cancellationToken: stoppingToken);

                        if (result is null)
                        {
                            break;
                        }

                        processedCount++;
                        await HandleMessageAsync(result, stoppingToken);
                    }

                    if (processedCount == 0)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    // Captura qualquer erro de TCP/RabbitMQ (ChannelClosed, IOException, etc)
                    _logger.LogError(ex, "Erro de conexão ou falha crítica no loop do RabbitMQ. Iniciando reconexão automática...");
                    
                    // Bloqueia a execução até conseguir voltar
                    await ConnectWithRetryAsync(stoppingToken);
                }
            }
        }
        finally
        {
            await CleanupAsync();
        }
    }

    private async Task ConnectWithRetryAsync(CancellationToken stoppingToken)
    {
        var delay = TimeSpan.FromSeconds(2);
        var maxDelay = TimeSpan.FromSeconds(30);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await CleanupAsync(); // Limpa as conexões mortas antes de tentar novamente
                await InitializeRabbitAsync(stoppingToken);
                
                _logger.LogInformation("Conexão com RabbitMQ estabelecida com sucesso.");
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha ao conectar no RabbitMQ. Tentando novamente em {Delay}s...", delay.TotalSeconds);
                await Task.Delay(delay, stoppingToken);
                
                delay = delay * 2;
                if (delay > maxDelay) delay = maxDelay;
            }
        }
    }

    private async Task InitializeRabbitAsync(CancellationToken stoppingToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.RabbitMq.Host,
            Port = _options.RabbitMq.Port,
            UserName = _options.RabbitMq.UserName,
            Password = _options.RabbitMq.Password
        };

        _connection = await factory.CreateConnectionAsync(stoppingToken);
        _channel = await _connection.CreateChannelAsync();

        await _channel.ExchangeDeclareAsync(
            exchange: _options.RabbitMq.Exchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await _channel.QueueDeclareAsync(
            queue: _options.RabbitMq.Queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: stoppingToken);

        await _channel.QueueBindAsync(
            queue: _options.RabbitMq.Queue,
            exchange: _options.RabbitMq.Exchange,
            routingKey: _options.RabbitMq.RoutingKey,
            cancellationToken: stoppingToken);

        await _channel.BasicQosAsync(0, (ushort)_options.BatchSize, false, stoppingToken);
    }

    private async Task HandleMessageAsync(BasicGetResult result, CancellationToken stoppingToken)
    {
        InboxEvent? inboxEvent = null;

        try
        {
            var body = Encoding.UTF8.GetString(result.Body.ToArray());
            var envelope = JsonSerializer.Deserialize<RabbitEventEnvelope>(body, JsonOptions);

            if (envelope is null || string.IsNullOrWhiteSpace(envelope.EventType))
            {
                _logger.LogWarning("Received malformed event envelope. Message will be discarded.");
                await _channel!.BasicNackAsync(result.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
                return;
            }

            using var payloadDocument = JsonDocument.Parse(envelope.Payload.GetRawText());
            inboxEvent = new InboxEvent(
                envelope.EventId ?? (long)result.DeliveryTag,
                envelope.EventType,
                payloadDocument,
                envelope.OccurredAtUtc.Kind == DateTimeKind.Utc
                    ? envelope.OccurredAtUtc
                    : envelope.OccurredAtUtc.ToUniversalTime());

            await _router.RouteAsync(inboxEvent, stoppingToken);
            await _channel!.BasicAckAsync(result.DeliveryTag, multiple: false, cancellationToken: stoppingToken);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Could not deserialize RabbitMQ message. Message will not be requeued.");
            await _channel!.BasicNackAsync(result.DeliveryTag, multiple: false, requeue: false, cancellationToken: stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while processing RabbitMQ message. Message will be requeued if possible.");
            
            // Se a exceção for queda de conexão, o Nack também vai falhar.
            // Repassamos para o catch principal para não mascarar a exceção de rede.
            if (_channel is not null && _channel.IsOpen)
            {
                await _channel.BasicNackAsync(result.DeliveryTag, multiple: false, requeue: true, cancellationToken: stoppingToken);
            }
            else
            {
                throw; 
            }
        }
        finally
        {
            inboxEvent?.Payload.Dispose();
        }
    }

    private async Task CleanupAsync()
    {
        try 
        {
            if (_channel is not null)
            {
                await _channel.CloseAsync();
                _channel.Dispose();
            }

            if (_connection is not null)
            {
                await _connection.CloseAsync();
                _connection.Dispose();
            }
        }
        catch 
        {
            // Ignoramos erros no cleanup para não prender o loop de reconexão
        }
    }
}