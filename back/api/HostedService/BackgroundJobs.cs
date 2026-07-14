using System.Linq.Expressions;
using System.Net.Mime;
using System.Text.Json;
using PlayOffsApi.Enum;
using PlayOffsApi.Models;
using PlayOffsApi.Services;
using ServiceStack.Redis;

namespace PlayOffsApi.HostedService;

public class BackgroundJobs : BackgroundService, IBackgroundJobsService
{
    private readonly RedisService _redisService;
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private IRedisSubscriptionAsync _subscription;
    private CancellationToken _cts;
    private readonly ILogger<BackgroundJob> _logger;
    private readonly string _mountPath = Environment.GetEnvironmentVariable("MOUNT_PATH");
    private static readonly Dictionary<string, string> ContentTypeMappings = new()
    {
        { "image/jpeg", ".jpg" },
        { "image/png", ".png" },
        { "image/gif", ".gif" },
        { "image/bmp", ".bmp" },
        { "image/tiff", ".tiff" },
        { "image/webp", ".webp" },
        { "application/pdf", ".pdf" }
    };

    public BackgroundJobs(RedisService redisService, IServiceScopeFactory serviceScopeFactory, ILogger<BackgroundJob> logger)
    {
        _redisService = redisService;
        _serviceScopeFactory = serviceScopeFactory;
        _logger = logger;
    }
    
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _cts = stoppingToken;
        Task.Run(ExecutePeriodicallyAsync, stoppingToken);
        return StartBackgroundJobs();
    }

    private async Task StartBackgroundJobs()
    {
        var delay = TimeSpan.FromSeconds(2);
        var maxDelay = TimeSpan.FromSeconds(30);

        while (!_cts.IsCancellationRequested)
        {
            try
            {
                await using var dataBase = await _redisService.GetDatabase();
                _subscription = await dataBase.CreateSubscriptionAsync(_cts);

                _subscription.OnMessageAsync += async (channel, message) => {
                    try
                    {
                        var jobDeserialized = JsonSerializer.Deserialize<BackgroundJob>(message);
                        if (jobDeserialized != null)
                        {
                            await ExecuteBackgroundJob(jobDeserialized);
                        }
                    }
                    catch (Exception e)
                    {
                        using var scope = _serviceScopeFactory.CreateScope();
                        var error = scope.ServiceProvider.GetRequiredService<ErrorLogService>();
                        await error.HandleExceptionValidationAsync(new DefaultHttpContext(), e);
                    }
                };

                _logger.LogInformation("Subscrição do Redis Pub/Sub estabelecida com sucesso.");
                
                // Esta chamada bloqueia a execução enquanto a subscrição estiver ativa.
                // Se a conexão com o Redis cair, ela lançará uma exceção ou sairá do método.
                await _subscription.SubscribeToChannelsAsync("jobs");

                // Reset do delay caso saia de forma limpa (raro)
                delay = TimeSpan.FromSeconds(2);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Falha na conexão do Redis Pub/Sub. Tentando novamente em {Delay}s...", delay.TotalSeconds);
                await Task.Delay(delay, _cts);
                
                delay = delay * 2;
                if (delay > maxDelay) delay = maxDelay;
            }
        }
    }

    private async Task ExecuteBackgroundJob(BackgroundJob jobDeserialized)
    {
        var paramList = (from param in jobDeserialized.Params
            let type = Type.GetType(param.Type)
            select param.Value.Deserialize(type!)).ToArray();

        var methodInfo = GetType().GetMethod(jobDeserialized.MethodName);
        if (methodInfo != null)
        {
            var task = (Task)methodInfo.Invoke(this, paramList);
            await task; // AQUI ESTÁ A MÁGICA: Aguarda a execução e expõe erros!
        }
    }

    public async Task EnqueueJob(Expression<Func<Task>> methodExpression, TimeSpan? period = null)
    {
        try
        {
            var (methodName, parameters) = GetMethodDetails(methodExpression);
            _logger.LogInformation("Nome do método: {methodName}", methodName);
            _logger.LogInformation("Período: {period}", period);

            var jobObject = new BackgroundJob
            {
                MethodName = methodName, 
                Params = parameters.Select(param => new BackgroundJobParameter { Type = param.GetType().AssemblyQualifiedName, Value = JsonSerializer.SerializeToElement(param, param.GetType()) }).ToArray(),
            };

            var jobObjectSerialized = JsonSerializer.Serialize(jobObject);
            await using var database = await _redisService.GetDatabase();
            
            if (period is null)
            {
                await database.PublishMessageAsync("jobs", jobObjectSerialized, _cts);
                return;
            }

            var scheduledDate = DateTime.UtcNow.Add(period.Value);
            _logger.LogInformation("scheduledDate: {scheduledDate} ", scheduledDate);

            var unixTimestamp = (long)(scheduledDate - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
            _logger.LogInformation("unixTimestamp: {unixTimestamp} ", unixTimestamp);
            await database.AddItemToSortedSetAsync("scheduled_jobs", jobObjectSerialized, unixTimestamp, _cts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Falha ao enfileirar job no Redis. (Se o Redis estiver indisponível, este erro é capturado para não afetar as regras de negócio).");
        }
    }
    
    private static (string Name, object[] Parameters) GetMethodDetails(Expression<Func<Task>> expression)
    {
        if (expression.Body is not MethodCallExpression methodCall)
            throw new ArgumentException("Expression is not a method call", nameof(expression));
        
        var methodName = methodCall.Method.Name;
        var parameters = methodCall.Arguments.Select(arg => Expression.Lambda(arg).Compile().DynamicInvoke()).ToArray();
        return (methodName, parameters);
    }

    private async Task DequeueDueJobsAsync()
    {
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using var database = await _redisService.GetDatabase();

        // 1. Aguarda a busca terminar PRIMEIRO
        var fetchedJobs = await database.GetRangeFromSortedSetByLowestScoreAsync("scheduled_jobs", double.NegativeInfinity, now, _cts);
        
        // 2. Só então dispara e aguarda a remoção
        await database.RemoveRangeFromSortedSetByScoreAsync("scheduled_jobs", double.NegativeInfinity, now, _cts);

        var jobs = fetchedJobs.Select(job => JsonSerializer.Deserialize<BackgroundJob>(job)).Where(backgroundJob => backgroundJob is not null).ToList();
        _logger.LogInformation("Quantidade de jobs: {jobsCount}", jobs.Count);
        
        foreach (var job in jobs)  
        {
            await ExecuteBackgroundJob(job);
        }
    }
    
    private async Task ExecutePeriodicallyAsync()
    {
        while (true)
        {
            try 
            {
                await DequeueDueJobsAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Erro no processamento dos jobs. O loop vai tentar novamente.");
            }
            
            // await Task.Delay(TimeSpan.FromHours(4), _cts);
            await Task.Delay(TimeSpan.FromSeconds(10), _cts);
        }
    }

    public async Task ChangeChampionshipStatusValidation(int championshipId, int status)
    {
        using var scope = _serviceScopeFactory.CreateScope();
        var dbService = scope.ServiceProvider.GetRequiredService<DbService>();
        var elasticService = scope.ServiceProvider.GetRequiredService<ElasticService>();
        var activityLogService = new ChampionshipActivityLogService(dbService);
        
        var activityFromChampionship = await activityLogService.GetAllFromChampionshipValidation(championshipId);
        await using var database = await _redisService.GetDatabase();
        
        var cancelJob = await database.GetValueAsync($"cancelJob_championship:{championshipId}", _cts);
        var championship = await dbService.GetAsync<Championship>("SELECT * FROM championships WHERE id = @id", new { id = championshipId });

        _logger.LogInformation(cancelJob);
        _logger.LogInformation("Data de início do camp: " + championship.InitialDate);

        if (cancelJob is not null)
        {
            if (DateTime.Parse(cancelJob) != championship.InitialDate) 
            {
                // Se as datas são diferentes, significa que a data mudou e o job atual é o antigo (que deve ser cancelado).
                // Então deletamos a chave do Redis para não bloquear o job NOVO que ainda vai rodar.
                await database.RemoveAsync($"cancelJob_championship:{championshipId}");
                return; 
            }
        }

        var statusEnum = (ChampionshipStatus)status;

        _logger.LogInformation("Status que o campeonato irá ser setado: " + statusEnum);
        _logger.LogInformation("Se a lista de atividade do campeonato tem alguma coisa: " + activityFromChampionship.Any());

        if (activityFromChampionship.Any() && statusEnum == ChampionshipStatus.Inactive)
        {
            var lastActivity = activityFromChampionship.OrderByDescending(d => d.DateOfActivity).Last();
              _logger.LogInformation("Data da última atividade: " + lastActivity.DateOfActivity);
            if (DateTime.UtcNow - lastActivity.DateOfActivity < TimeSpan.FromDays(14)) return;
        }

        await ChangeChampionshipStatusSend(championshipId, statusEnum, dbService);
        championship.Status = statusEnum;
        
        var isDevelopment = Environment.GetEnvironmentVariable("IS_DEVELOPMENT");
        var indexName = isDevelopment == "true" ? "championships-dev" : "championships";
        var response = await elasticService._client.IndexAsync(championship, indexName, _cts);

        if (!response.IsValidResponse)
        {
            _logger.LogError("ERRO NO ELASTICSEARCH: " + response.DebugInformation);
        }
        else
        {
            _logger.LogInformation("Elasticsearch atualizado com status 0 com sucesso!");
        }
    }

    private static async Task ChangeChampionshipStatusSend(int championshipId, ChampionshipStatus status, DbService dbService) 
        => await dbService.EditData("UPDATE championships SET status = @status WHERE id = @id", new { id = championshipId, status });
    
    private static string GetFileExtension(ContentType type)
    {
        ContentTypeMappings.TryGetValue(type.MediaType, out var value);
        return value;
    }
}