using RabbitMQ.Client;
using System;
using System.Threading.Tasks;

namespace PlayOffsApi.Services
{
    public interface IRabbitMqService
    {
        Task InicializeAsync();
        Task PublishMessageAsync(string queueName, string message);
        Task<string> ConsumeMessageAsync(string queueName);
        void Dispose();
    }
}