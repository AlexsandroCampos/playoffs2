using RabbitMQ.Client;

namespace PlayOffsApi.Services
{
    /// <summary>
    /// Proxy wrapper for RabbitMQ service that handles lazy async initialization
    /// </summary>
    public class RabbitMqServiceProxy : IRabbitMqService
    {
        private readonly Lazy<Task<IRabbitMqService>> _serviceFactory;
        private IRabbitMqService _realService;
        private readonly object _syncLock = new();
        private volatile bool _initialized = false;

        public RabbitMqServiceProxy(Lazy<Task<IRabbitMqService>> serviceFactory)
        {
            _serviceFactory = serviceFactory;
        }

        private async Task<IRabbitMqService> EnsureInitializedAsync()
        {
            if (_initialized && _realService != null)
            {
                return _realService;
            }

            lock (_syncLock)
            {
                if (_initialized && _realService != null)
                {
                    return _realService;
                }

                // Initialize synchronously from the already-started task
                _realService = _serviceFactory.Value.GetAwaiter().GetResult();
                _initialized = true;
            }

            return _realService;
        }

        public async Task PublishMessageAsync(string message)
        {
            var service = await EnsureInitializedAsync();
            await service.PublishMessageAsync(message);
        }

        public async Task DisposeAsync()
        {
            if (_realService != null)
            {
                await _realService.DisposeAsync();
            }
        }
    }
}
