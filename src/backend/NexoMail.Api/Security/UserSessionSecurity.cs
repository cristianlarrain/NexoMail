using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Api.Security;

public sealed class NexoMailCookieEvents(NexoMailDbContext database) : CookieAuthenticationEvents
{
    internal const string SessionClaimType = "nexomail:session-id";
    private static readonly TimeSpan LastSeenWriteInterval = TimeSpan.FromMinutes(5);

    public override async Task SigningIn(CookieSigningInContext context)
    {
        if (context.Principal is null || !TryUserId(context.Principal, out var userId)) return;

        var user = await database.Users.AsNoTracking()
            .SingleOrDefaultAsync(x => x.Id == userId && x.IsActive && x.IsEmailVerified, context.HttpContext.RequestAborted);
        if (user is null) return;

        var now = DateTimeOffset.UtcNow;
        var securityStamp = CreateSecurityStamp(user.PasswordHash);
        UserSessionEntity? session = null;

        if (TrySessionId(context.HttpContext.User, out var existingSessionId))
        {
            session = await database.UserSessions.SingleOrDefaultAsync(
                x => x.Id == existingSessionId && x.UserId == userId,
                context.HttpContext.RequestAborted);

            if (session is not null &&
                (session.RevokedAt is not null || session.ExpiresAt <= now || !FixedTimeEquals(session.SecurityStamp, securityStamp)))
            {
                session = null;
            }
        }

        if (session is null)
        {
            session = new UserSessionEntity
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CreatedAt = now,
                LastSeenAt = now,
                ExpiresAt = context.Properties.ExpiresUtc ?? now.AddDays(14),
                IpAddress = Trim(context.HttpContext.Connection.RemoteIpAddress?.ToString(), 64),
                UserAgent = Trim(context.HttpContext.Request.Headers.UserAgent.ToString(), 512),
                SecurityStamp = securityStamp
            };
            database.UserSessions.Add(session);
        }
        else
        {
            session.LastSeenAt = now;
            session.ExpiresAt = context.Properties.ExpiresUtc ?? session.ExpiresAt;
            session.IpAddress = Trim(context.HttpContext.Connection.RemoteIpAddress?.ToString(), 64);
            session.UserAgent = Trim(context.HttpContext.Request.Headers.UserAgent.ToString(), 512);
        }

        await database.SaveChangesAsync(context.HttpContext.RequestAborted);

        if (context.Principal.Identity is ClaimsIdentity identity)
        {
            foreach (var claim in identity.FindAll(SessionClaimType).ToArray()) identity.RemoveClaim(claim);
            identity.AddClaim(new Claim(SessionClaimType, session.Id.ToString()));
        }
    }

    public override async Task ValidatePrincipal(CookieValidatePrincipalContext context)
    {
        if (context.Principal is null ||
            !TryUserId(context.Principal, out var userId) ||
            !TrySessionId(context.Principal, out var sessionId))
        {
            context.RejectPrincipal();
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var session = await database.UserSessions.SingleOrDefaultAsync(
            x => x.Id == sessionId && x.UserId == userId,
            context.HttpContext.RequestAborted);
        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(
            x => x.Id == userId && x.IsActive && x.IsEmailVerified,
            context.HttpContext.RequestAborted);

        if (session is null || user is null || session.RevokedAt is not null || session.ExpiresAt <= now)
        {
            context.RejectPrincipal();
            return;
        }

        var currentStamp = CreateSecurityStamp(user.PasswordHash);
        if (!FixedTimeEquals(session.SecurityStamp, currentStamp))
        {
            session.RevokedAt = now;
            await database.SaveChangesAsync(context.HttpContext.RequestAborted);
            context.RejectPrincipal();
            return;
        }

        if (now - session.LastSeenAt >= LastSeenWriteInterval)
        {
            session.LastSeenAt = now;
            session.IpAddress = Trim(context.HttpContext.Connection.RemoteIpAddress?.ToString(), 64);
            session.UserAgent = Trim(context.HttpContext.Request.Headers.UserAgent.ToString(), 512);
            await database.SaveChangesAsync(context.HttpContext.RequestAborted);
        }
    }

    public override async Task SigningOut(CookieSigningOutContext context)
    {
        if (!TrySessionId(context.HttpContext.User, out var sessionId)) return;

        var session = await database.UserSessions.SingleOrDefaultAsync(
            x => x.Id == sessionId,
            context.HttpContext.RequestAborted);
        if (session is null || session.RevokedAt is not null) return;

        session.RevokedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(context.HttpContext.RequestAborted);
    }

    public override Task RedirectToLogin(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    }

    public override Task RedirectToAccessDenied(RedirectContext<CookieAuthenticationOptions> context)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    }

    internal static string CreateSecurityStamp(string? passwordHash) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(passwordHash ?? string.Empty)));

    internal static bool TrySessionId(ClaimsPrincipal principal, out Guid sessionId) =>
        Guid.TryParse(principal.FindFirstValue(SessionClaimType), out sessionId);

    private static bool TryUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private static bool FixedTimeEquals(string left, string right) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(left), Encoding.UTF8.GetBytes(right));

    private static string? Trim(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= maxLength ? value : value[..maxLength];
}

public static class UserSessionEndpoints
{
    public static IEndpointRouteBuilder MapNexoMailSessions(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/auth/signout", SignOutAsync).AllowAnonymous();

        var sessions = endpoints.MapGroup("/api/auth/sessions").RequireAuthorization();

        sessions.MapGet("/", GetSessionsAsync);
        sessions.MapDelete("/{sessionId:guid}", RevokeSessionAsync);
        sessions.MapPost("/revoke-others", RevokeOtherSessionsAsync);

        return endpoints;
    }

    private static async Task<IResult> SignOutAsync(HttpContext context, NexoMailDbContext database, CancellationToken ct)
    {
        if (NexoMailCookieEvents.TrySessionId(context.User, out var sessionId))
        {
            var session = await database.UserSessions.SingleOrDefaultAsync(x => x.Id == sessionId, ct);
            if (session is not null && session.RevokedAt is null)
            {
                session.RevokedAt = DateTimeOffset.UtcNow;
                await database.SaveChangesAsync(ct);
            }
        }

        context.Response.Cookies.Delete("NexoMail.Auth", new CookieOptions
        {
            Path = "/",
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps
        });

        return Results.NoContent();
    }

    private static async Task<IResult> GetSessionsAsync(HttpContext context, NexoMailDbContext database, CancellationToken ct)
    {
        if (!TryUser(context.User, out var userId, out var currentSessionId)) return Results.Unauthorized();

        var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId, ct);
        if (user is null) return Results.Unauthorized();

        var now = DateTimeOffset.UtcNow;
        var stamp = NexoMailCookieEvents.CreateSecurityStamp(user.PasswordHash);
        var sessions = await database.UserSessions
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.RevokedAt == null && x.SecurityStamp == stamp)
            .ToArrayAsync(ct);

        var active = sessions
            .Where(x => x.ExpiresAt > now)
            .OrderByDescending(x => x.LastSeenAt)
            .Select(x => new UserSessionResponse(
                x.Id,
                x.Id == currentSessionId,
                x.CreatedAt,
                x.LastSeenAt,
                x.ExpiresAt,
                x.IpAddress,
                x.UserAgent))
            .ToArray();

        return Results.Ok(active);
    }

    private static async Task<IResult> RevokeSessionAsync(Guid sessionId, HttpContext context, NexoMailDbContext database, CancellationToken ct)
    {
        if (!TryUser(context.User, out var userId, out var currentSessionId)) return Results.Unauthorized();
        if (sessionId == currentSessionId)
            return Results.BadRequest(new { error = "La sesión actual se cierra desde la opción Cerrar sesión." });

        var session = await database.UserSessions.SingleOrDefaultAsync(
            x => x.Id == sessionId && x.UserId == userId && x.RevokedAt == null,
            ct);
        if (session is null) return Results.NotFound(new { error = "La sesión ya no está activa." });

        session.RevokedAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(ct);
        return Results.NoContent();
    }

    private static async Task<IResult> RevokeOtherSessionsAsync(HttpContext context, NexoMailDbContext database, CancellationToken ct)
    {
        if (!TryUser(context.User, out var userId, out var currentSessionId)) return Results.Unauthorized();

        var now = DateTimeOffset.UtcNow;
        var otherSessions = await database.UserSessions
            .Where(x => x.UserId == userId && x.Id != currentSessionId && x.RevokedAt == null)
            .ToArrayAsync(ct);

        foreach (var session in otherSessions) session.RevokedAt = now;
        if (otherSessions.Length > 0) await database.SaveChangesAsync(ct);

        return Results.Ok(new { revoked = otherSessions.Length });
    }

    private static bool TryUser(ClaimsPrincipal principal, out Guid userId, out Guid sessionId)
    {
        sessionId = Guid.Empty;
        return Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId) &&
               NexoMailCookieEvents.TrySessionId(principal, out sessionId);
    }
}

public sealed record UserSessionResponse(
    Guid Id,
    bool IsCurrent,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastSeenAt,
    DateTimeOffset ExpiresAt,
    string? IpAddress,
    string? UserAgent);
