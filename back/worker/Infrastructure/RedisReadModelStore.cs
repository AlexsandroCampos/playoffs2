using Microsoft.Extensions.Options;
using PlayOffs.Worker.Contracts;
using StackExchange.Redis;

namespace PlayOffs.Worker.Infrastructure;

public sealed class RedisReadModelStore : IReadModelStore
{
    private readonly WorkerOptions _options;
    private readonly ConnectionMultiplexer _redis;

    public RedisReadModelStore(IOptions<WorkerOptions> options)
    {
        _options = options.Value;
        _redis = ConnectionMultiplexer.Connect(_options.Redis.ConnectionString);
    }

    public async Task SaveStandingsAsync(int championshipId, string jsonPayload, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var key = string.Format(_options.Redis.StandingsKeyPattern, championshipId);
        await db.StringSetAsync(key, jsonPayload);
    }

    public async Task SaveCardsAsync(int championshipId, string jsonPayload, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var key = string.Format(_options.Redis.CardsKeyPattern, championshipId);
        await db.StringSetAsync(key, jsonPayload);
    }

    public async Task SaveStrikersAsync(int championshipId, string jsonPayload, CancellationToken ct)
    {
        var db = _redis.GetDatabase();
        var key = string.Format(_options.Redis.StrikersKeyPattern, championshipId);
        await db.StringSetAsync(key, jsonPayload);
    }
}
