using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace NexoMail.Api;

public sealed class MailReadCache(IMemoryCache memoryCache)
{
    private readonly ConcurrentDictionary<string, long> userGenerations = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> areaGenerations = new(StringComparer.Ordinal);

    public async Task<T> GetOrCreateAsync<T>(
        string userKey,
        string area,
        string key,
        TimeSpan lifetime,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
        where T : class
    {
        var userGeneration = userGenerations.GetOrAdd(userKey, 0);
        var areaGeneration = areaGenerations.GetOrAdd(AreaKey(userKey, area), 0);
        var cacheKey = $"mail-read:{userKey}:{userGeneration}:{area}:{areaGeneration}:{key}";

        var value = await memoryCache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = lifetime;
            entry.Size = 1;
            return await factory(cancellationToken);
        });

        return value ?? throw new InvalidOperationException("No fue posible obtener los datos de correo en caché.");
    }

    public void Invalidate(string userKey)
    {
        userGenerations.AddOrUpdate(userKey, 1, static (_, current) => current + 1);
    }

    public void InvalidateAreas(string userKey, params string[] areas)
    {
        foreach (var area in areas.Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal))
        {
            areaGenerations.AddOrUpdate(AreaKey(userKey, area), 1, static (_, current) => current + 1);
        }
    }

    private static string AreaKey(string userKey, string area) => $"{userKey}:{area}";
}
