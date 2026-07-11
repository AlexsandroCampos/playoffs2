using ServiceStack.Redis;

namespace PlayOffsApi.Services;

public class RedisService
{
	private readonly RedisManagerPool _redis;
	public RedisService(IWebHostEnvironment environment)
	{
		// Check for Redis__Host first (Docker Compose convention)
		var redisHost = Environment.GetEnvironmentVariable("Redis__Host");
		var redisPort = Environment.GetEnvironmentVariable("Redis__Port") ?? "6379";
		
		var url = string.IsNullOrEmpty(redisHost)
			? (Environment.GetEnvironmentVariable("REDIS_URL") ?? "localhost:6379")
			: $"{redisHost}:{redisPort}";

		_redis = new RedisManagerPool(url);
	}

	public async Task<IRedisClientAsync> GetDatabase() => await _redis.GetClientAsync();
}