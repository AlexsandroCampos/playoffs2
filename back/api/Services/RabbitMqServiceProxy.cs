using RabbitMQ.Client;

namespace PlayOffsApi.Services
{
    public class RabbitMqServiceProxy : IRabbitMqService
    {
        private readonly Func<Task<IRabbitMqService>> _serviceFactory;
        private IRabbitMqService _realService;
        private readonly SemaphoreSlim _lock = new(1, 1);

        public RabbitMqServiceProxy(Func<Task<IRabbitMqService>> serviceFactory)
        {
            _serviceFactory = serviceFactory;
        }

        private async Task<IRabbitMqService> EnsureInitializedAsync()
        {
            if (_realService != null)
                return _realService;

            await _lock.WaitAsync();
            try
            {
                if (_realService != null)
                    return _realService;

                // Chama a fábrica DE NOVO a cada tentativa — nunca reaproveita uma tentativa que falhou
                _realService = await _serviceFactory();
                return _realService;
            }
            finally
            {
                _lock.Release();
            }
        }

        public async Task PublishMessageAsync(string message)
        {
            var service = await EnsureInitializedAsync();
            await service.PublishMessageAsync(message);
        }

        public async Task DisposeAsync()
        {
            if (_realService != null)
                await _realService.DisposeAsync();
        }
    }
}