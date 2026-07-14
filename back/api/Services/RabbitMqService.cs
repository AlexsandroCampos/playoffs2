using RabbitMQ.Client;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace PlayOffsApi.Services
{
    public interface IRabbitMqService
    {
        Task PublishMessageAsync(string message);
        Task DisposeAsync();
    }

    public class RabbitMqService : IRabbitMqService 
    {
        private IConnection _connection;
        private IChannel _channel;
        private readonly string _hostName;
        private readonly int _port;
        private readonly string _userName;
        private readonly string _password;
        private readonly string _exchangeName = "playoffs-exchange";
        private readonly string _queueName = "playoffs-queue";
        private readonly string _routingKey = "playoffs.event";
        
        // Protege contra recriação paralela da conexão
        private readonly SemaphoreSlim _connectionLock = new(1, 1);

        private RabbitMqService(string hostName, int port, string userName, string password)
        {
            _hostName = hostName;
            _port = port;
            _userName = userName;
            _password = password;
        }

        public static async Task<RabbitMqService> CreateAsync(string hostName, int port, string userName, string password)
        {
            var service = new RabbitMqService(hostName, port, userName, password);
            await service.InitializeAsync();
            return service;
        }

        private async Task InitializeAsync()
        {
            await CleanupAsync(); // Garante que limpamos o lixo antes de tentar de novo

            var factory = new ConnectionFactory() 
            { 
                HostName = _hostName, 
                Port = _port,
                UserName = _userName, 
                Password = _password,
                AutomaticRecoveryEnabled = true, // Opcional, ajuda em pequenas quedas, mas nossa lógica abaixo é mais robusta
                NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
            };

            _connection = await factory.CreateConnectionAsync();
            _channel = await _connection.CreateChannelAsync();

            // Declara o exchange
            await _channel.ExchangeDeclareAsync(
                exchange: _exchangeName,
                type: ExchangeType.Topic, // Topic = Pub/Sub
                durable: true,
                autoDelete: false
            );

            // Declara a fila
            await _channel.QueueDeclareAsync(
                queue: _queueName,
                durable: true,
                exclusive: false,
                autoDelete: false
            );

            // Faz o bind da fila ao exchange com a routing key
            await _channel.QueueBindAsync(
                queue: _queueName,
                exchange: _exchangeName,
                routingKey: _routingKey
            );
        }

        private async Task EnsureConnectionAsync()
        {
            if (_channel != null && _channel.IsOpen)
                return;

            await _connectionLock.WaitAsync();
            try
            {
                // Double-check
                if (_channel != null && _channel.IsOpen)
                    return;

                await InitializeAsync();
            }
            catch (RabbitMQ.Client.Exceptions.BrokerUnreachableException ex)
            {
                // Se não conseguir alcançar o broker durante a tentativa de reconexão,
                // embrulha numa IOException para que o OutboxPublisherHostedService
                // a classifique como falha de infraestrutura (falha transitória).
                throw new System.IO.IOException("Falha ao tentar restabelecer conexão com o RabbitMQ.", ex);
            }
            finally
            {
                _connectionLock.Release();
            }
        }

        public async Task PublishMessageAsync(string message)
        {
            // Auto-Cura: Garante que a conexão está viva antes de publicar
            await EnsureConnectionAsync();

            var body = System.Text.Encoding.UTF8.GetBytes(message);

            var properties = new BasicProperties
            {
                Persistent = true
            };

            await _channel.BasicPublishAsync(
                exchange: _exchangeName,
                routingKey: _routingKey,
                mandatory: false,
                basicProperties: properties,
                body: body
            );
        }

        private async Task CleanupAsync()
        {
            try
            {
                if (_channel != null)
                {
                    await _channel.CloseAsync();
                    _channel.Dispose();
                    _channel = null;
                }

                if (_connection != null)
                {
                    await _connection.CloseAsync();
                    _connection.Dispose();
                    _connection = null;
                }
            }
            catch
            {
                // Ignorar erros durante o cleanup
            }
        }

        public async Task DisposeAsync()
        {
            await CleanupAsync();
            _connectionLock.Dispose();
        }
    }
}