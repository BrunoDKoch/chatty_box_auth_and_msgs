using Microsoft.Extensions.Caching.Distributed;
using Newtonsoft.Json;
using StackExchange.Redis;

namespace ChattyBox.Services;

public class CachingService {
  private IDatabaseAsync _db;

  public CachingService(IConfiguration configuration) {
    var connectionString = configuration.GetValue<string>("Redis");
    ArgumentException.ThrowIfNullOrEmpty(connectionString);
    _db = ConfigureRedis(connectionString);
  }

  private static IDatabaseAsync ConfigureRedis(string connectionString) {
    var redis = ConnectionMultiplexer.Connect(connectionString);
    var db = redis.GetDatabase();
    return db;
  }

  async public Task<T?> GetCache<T>(string key) {
    var value = await _db.StringGetAsync(key);
    if (!string.IsNullOrEmpty(value) && value != RedisValue.Null) {
      return JsonConvert.DeserializeObject<T>(value!);
    }
    return default;
  }

  async public Task<bool> SetCache<T>(string key, T value, TimeSpan? expiry = null) {
    var isSet = await _db.StringSetAsync(
      key,
      JsonConvert.SerializeObject(value, new JsonSerializerSettings {
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore
      }),
      expiry,
      When.Always
    );
    return isSet;
  }

  async public Task<bool> DeleteKey(string key) {
    return await _db.KeyDeleteAsync(key);
  }
}