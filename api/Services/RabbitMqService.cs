using RabbitMQ.Client;
using System;
using System.Threading.Tasks;

namespace PlayOffsApi.Services
{
    public interface IRabbitMqService
    {
        Task PublishMessageAsync(string message);
        Task<string> ConsumeMessageAsync();
        Task DisposeAsync();
    }

    public class RabbitMqService : IRabbitMqService 
    {
        private IConnection _connection;
        private IChannel _channel;
        private readonly string _hostName;
        private readonly string _userName;
        private readonly string _password;
        private readonly string _exchangeName = "playoffs-exchange";
        private readonly string _queueName = "playoffs-queue";
        private readonly string _routingKey = "playoffs.*";

        private RabbitMqService(string hostName, string userName, string password)
        {
            _hostName = hostName;
            _userName = userName;
            _password = password;
        }

        public static async Task<RabbitMqService> CreateAsync(string hostName, string userName, string password)
        {
            var service = new RabbitMqService(hostName, userName, password);
            await service.InitializeAsync();
            return service;
        }

        private async Task InitializeAsync()
        {
            var factory = new ConnectionFactory() 
            { 
                HostName = _hostName, 
                UserName = _userName, 
                Password = _password 
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

            await Task.CompletedTask;
        }

        public async Task PublishMessageAsync(string message)
        {
            var body = System.Text.Encoding.UTF8.GetBytes(message);

            var properties = new BasicProperties
            {
                Persistent = true
            };

            await _channel.BasicPublishAsync(
                exchange: _exchangeName,
                routingKey: "playoffs.event",
                mandatory: false,
                basicProperties: properties,
                body: body
            );
        }

        // Usado no worker para consumir mensagens da fila
        public async Task<string> ConsumeMessageAsync()
        {
            var result = await _channel.BasicGetAsync(queue: _queueName, autoAck: true);
            
            if (result != null)
            {
                var message = System.Text.Encoding.UTF8.GetString(result.Body.ToArray());
                return message;
            }
            
            return null;
        }

        public async Task DisposeAsync()
        {
            if (_channel != null)
            {
                await _channel.CloseAsync();
                _channel.Dispose();
            }

            if (_connection != null)
            {
                await _connection.CloseAsync();
                _connection.Dispose();
            }
        }
    }
}