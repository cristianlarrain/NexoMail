using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

namespace NexoMail.Api;

public sealed class MailReadCache(IMemoryCache memoryCache)
{
    private readonly ConcurrentDictionary<string, long> generations = new(StringComparer.Ordinal);

    public async Task<T> GetOrCreateAsync<T>(
        string userKey,
        string area,
        string key,
        TimeSpan lifetime,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken)
        where T : class
    {
        var generation = generations.GetOrAdd(userKey, 0);
        var cacheKey = $"mail-read:{userKey}:{generation}:{area}:{key}";

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
        generations.AddOrUpdate(userKey, 1, static (_, current) => current + 1);
    }
}
