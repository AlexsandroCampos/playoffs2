using Microsoft.Extensions.Options;
using PlayOffsApi.Services;
using PlayOffsApi.Models;

namespace PlayOffsApi.HostedService
{
    /// <summary>
    /// Hosted service that ensures RabbitMQ is properly initialized on startup
    /// </summary>
    public class RabbitMqInitializationService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<RabbitMqInitializationService> _logger;
        private readonly RabbitMqSettings _settings;

        public RabbitMqInitializationService(
            IServiceProvider serviceProvider,
            ILogger<RabbitMqInitializationService> logger,
            IOptions<RabbitMqSettings> options)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _settings = options.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Give other services time to start
            await Task.Delay(2000, stoppingToken);

            try
            {
                _logger.LogInformation("Initializing RabbitMQ connection...");
                
                // Initialize RabbitMQ with retries
                var maxRetries = 5;
                var retryDelay = TimeSpan.FromSeconds(3);

                for (int attempt = 1; attempt <= maxRetries; attempt++)
                {
                    try
                    {
                        var service = await RabbitMqService.CreateAsync(
                            _settings.Host,
                            _settings.Port,
                            _settings.UserName,
                            _settings.Password
                        );

                        _logger.LogInformation("RabbitMQ connection established successfully");
                        return;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            $"RabbitMQ connection attempt {attempt}/{maxRetries} failed: {ex.Message}. " +
                            $"Retrying in {retryDelay.TotalSeconds}s...");

                        if (attempt < maxRetries)
                        {
                            await Task.Delay(retryDelay, stoppingToken);
                        }
                        else
                        {
                            _logger.LogError($"Failed to connect to RabbitMQ after {maxRetries} attempts. " +
                                "The application may not function properly.");
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Critical error during RabbitMQ initialization");
                // Don't throw - allow app to start anyway (messaging will fail when attempted)
            }
        }
    }

}
