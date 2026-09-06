using System.Collections.Concurrent;

namespace NexoMail.Api.Security;

public sealed record AuthRateLimitRule(int PermitLimit, TimeSpan Window);
public sealed record AuthRateLimitResult(bool Allowed, int Limit, int Remaining, TimeSpan RetryAfter);

public sealed class AuthRateLimitTracker
{
    private sealed class WindowCounter
    {
        public object SyncRoot { get; } = new();
        public int Count { get; set; }
        public DateTimeOffset ResetAt { get; set; }
    }

    private readonly ConcurrentDictionary<string, WindowCounter> counters = new(StringComparer.Ordinal);

    public AuthRateLimitResult Acquire(string policy, string clientKey, AuthRateLimitRule rule)
    {
        var now = DateTimeOffset.UtcNow;
        var key = $"{policy}:{clientKey}";
        var counter = counters.GetOrAdd(key, _ => new WindowCounter { ResetAt = now.Add(rule.Window) });

        lock (counter.SyncRoot)
        {
            if (now >= counter.ResetAt)
            {
                counter.Count = 0;
                counter.ResetAt = now.Add(rule.Window);
            }

            var retryAfter = counter.ResetAt - now;
            if (counter.Count >= rule.PermitLimit)
                return new AuthRateLimitResult(false, rule.PermitLimit, 0, retryAfter);

            counter.Count++;
            return new AuthRateLimitResult(true, rule.PermitLimit, rule.PermitLimit - counter.Count, retryAfter);
        }
    }
}

public sealed class AuthRateLimitFilter(string policy, int permitLimit, TimeSpan window) : IEndpointFilter
{
    private static readonly AuthRateLimitTracker Tracker = new();
    private readonly AuthRateLimitRule rule = new(permitLimit, window);

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var httpContext = context.HttpContext;
        var clientKey = httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = Tracker.Acquire(policy, clientKey, rule);
        var retrySeconds = Math.Max(1, (int)Math.Ceiling(result.RetryAfter.TotalSeconds));

        httpContext.Response.Headers["X-RateLimit-Limit"] = result.Limit.ToString();
        httpContext.Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString();
        httpContext.Response.Headers["X-RateLimit-Reset-Seconds"] = retrySeconds.ToString();

        if (!result.Allowed)
        {
            httpContext.Response.Headers.RetryAfter = retrySeconds.ToString();
            return Results.Json(new
            {
                error = "Demasiados intentos.",
                retryAfterSeconds = retrySeconds,
                attemptsRemaining = 0
            }, statusCode: StatusCodes.Status429TooManyRequests);
        }

        return await next(context);
    }
}
