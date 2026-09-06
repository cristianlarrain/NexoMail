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

public sealed class AuthRateLimitMiddleware(RequestDelegate next, AuthRateLimitTracker tracker)
{
    private static readonly IReadOnlyDictionary<string, (string Policy, AuthRateLimitRule Rule)> Rules =
        new Dictionary<string, (string, AuthRateLimitRule)>(StringComparer.OrdinalIgnoreCase)
        {
            ["/api/auth/login"] = ("auth-login", new AuthRateLimitRule(10, TimeSpan.FromMinutes(5))),
            ["/api/auth/register"] = ("auth-register", new AuthRateLimitRule(5, TimeSpan.FromMinutes(30))),
            ["/api/auth/resend-verification"] = ("auth-send-code", new AuthRateLimitRule(5, TimeSpan.FromMinutes(15))),
            ["/api/auth/forgot-password"] = ("auth-send-code", new AuthRateLimitRule(5, TimeSpan.FromMinutes(15))),
            ["/api/auth/verify-email"] = ("auth-verify-code", new AuthRateLimitRule(10, TimeSpan.FromMinutes(10))),
            ["/api/auth/verify-reset-code"] = ("auth-verify-code", new AuthRateLimitRule(10, TimeSpan.FromMinutes(10))),
            ["/api/auth/reset-password"] = ("auth-verify-code", new AuthRateLimitRule(10, TimeSpan.FromMinutes(10)))
        };

    public async Task InvokeAsync(HttpContext context)
    {
        if (!HttpMethods.IsPost(context.Request.Method) || !Rules.TryGetValue(context.Request.Path.Value ?? string.Empty, out var configured))
        {
            await next(context);
            return;
        }

        var clientKey = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        var result = tracker.Acquire(configured.Policy, clientKey, configured.Rule);
        var retrySeconds = Math.Max(1, (int)Math.Ceiling(result.RetryAfter.TotalSeconds));

        context.Response.Headers["X-RateLimit-Limit"] = result.Limit.ToString();
        context.Response.Headers["X-RateLimit-Remaining"] = result.Remaining.ToString();
        context.Response.Headers["X-RateLimit-Reset-Seconds"] = retrySeconds.ToString();

        if (!result.Allowed)
        {
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = retrySeconds.ToString();
            await context.Response.WriteAsJsonAsync(new
            {
                error = "Demasiados intentos.",
                retryAfterSeconds = retrySeconds,
                attemptsRemaining = 0
            });
            return;
        }

        await next(context);
    }
}
