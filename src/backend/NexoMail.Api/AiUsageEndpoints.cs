using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Api;

public static class AiUsageEndpoints
{
    public static void Map(RouteGroupBuilder api)
    {
        var usage = api.MapGroup("/ai-usage/admin").RequireAuthorization();

        usage.MapGet("/summary", async (
            string? period,
            NexoMailDbContext database,
            IUserContext userContext,
            CancellationToken ct) =>
        {
            if (!await IsOwnerAsync(database, userContext, ct)) return Results.Forbid();
            var normalized = (period ?? "week").Trim().ToLowerInvariant();
            var requestedPeriod = normalized switch
            {
                "week" => AiUsagePeriod.Week,
                "month" => AiUsagePeriod.Month,
                _ => (AiUsagePeriod?)null
            };
            if (requestedPeriod is null)
                return Results.BadRequest(new { error = "El período debe ser week o month." });

            var service = new AiUsageAdminService(database);
            return Results.Ok(await service.GetSummaryAsync(requestedPeriod.Value, ct));
        });

        usage.MapGet("/users", async (
            string? sort,
            NexoMailDbContext database,
            IUserContext userContext,
            CancellationToken ct) =>
        {
            if (!await IsOwnerAsync(database, userContext, ct)) return Results.Forbid();
            var normalized = string.IsNullOrWhiteSpace(sort) ? "projected" : sort.Trim().ToLowerInvariant();
            if (normalized is not ("projected" or "accumulated"))
                return Results.BadRequest(new { error = "El orden debe ser projected o accumulated." });

            var service = new AiUsageAdminService(database);
            return Results.Ok(await service.GetUsersAsync(normalized, ct));
        });

        usage.MapGet("/users/{userId:guid}", async (
            Guid userId,
            NexoMailDbContext database,
            IUserContext userContext,
            CancellationToken ct) =>
        {
            if (!await IsOwnerAsync(database, userContext, ct)) return Results.Forbid();
            var service = new AiUsageAdminService(database);
            var detail = await service.GetUserAsync(userId, ct);
            return detail is null ? Results.NotFound() : Results.Ok(detail);
        });

        usage.MapGet("/settings", async (
            NexoMailDbContext database,
            IUserContext userContext,
            CancellationToken ct) =>
        {
            if (!await IsOwnerAsync(database, userContext, ct)) return Results.Forbid();
            var service = new AiUsageAdminService(database);
            return Results.Ok(await service.GetSettingsAsync(ct));
        });

        usage.MapPatch("/settings", async (
            AiUsageSettingsRequest request,
            NexoMailDbContext database,
            IUserContext userContext,
            CancellationToken ct) =>
        {
            if (!await IsOwnerAsync(database, userContext, ct)) return Results.Forbid();
            var service = new AiUsageAdminService(database);
            try
            {
                return Results.Ok(await service.UpdateSettingsAsync(
                    request.GreenMaxClp,
                    request.YellowMaxClp,
                    request.ReferenceClpPerUsd,
                    ct));
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
        });
    }

    public static Task<bool> IsOwnerAsync(
        NexoMailDbContext database,
        IUserContext userContext,
        CancellationToken ct) =>
        database.Users.AsNoTracking()
            .AnyAsync(x => x.Id == userContext.UserId && x.IsActive && x.IsOwner, ct);
}

public sealed record AiUsageSettingsRequest(
    decimal GreenMaxClp,
    decimal YellowMaxClp,
    decimal ReferenceClpPerUsd);
