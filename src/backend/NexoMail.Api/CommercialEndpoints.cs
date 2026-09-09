using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Api;

public static class CommercialEndpoints
{
    public static void MapNexoMailCommercial(this RouteGroupBuilder api)
    {
        var commercial = api.MapGroup("/commercial").RequireAuthorization();

        commercial.MapGet("/subscription", async (NexoMailDbContext database, IUserContext userContext, CancellationToken ct) =>
        {
            var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userContext.UserId, ct);
            if (user is null) return Results.NotFound();

            var connectedAccounts = await database.MailAccounts.AsNoTracking()
                .CountAsync(x => x.UserId == userContext.UserId && x.IsActive, ct);
            var current = CommercialPlanCatalog.Resolve(user.PlanCode);
            var remaining = current.MaxAccounts.HasValue
                ? Math.Max(0, current.MaxAccounts.Value - connectedAccounts)
                : (int?)null;
            var overLimit = current.MaxAccounts.HasValue && connectedAccounts > current.MaxAccounts.Value;
            var canAddAccount = !current.MaxAccounts.HasValue || connectedAccounts < current.MaxAccounts.Value;

            return Results.Ok(new CommercialSubscriptionSnapshot(
                ToDto(current),
                connectedAccounts,
                remaining,
                canAddAccount,
                overLimit,
                CommercialPlanCatalog.All.Select(ToDto).ToArray()));
        });
    }

    private static CommercialPlanDto ToDto(CommercialPlanDefinition plan) => new(
        plan.Code,
        plan.Name,
        plan.Price,
        plan.Cadence,
        plan.MaxAccounts,
        plan.Description,
        plan.Features,
        plan.IsFeatured,
        plan.IsCorporate,
        plan.IsWhiteLabel);
}

public sealed record CommercialPlanDto(
    string Code,
    string Name,
    string Price,
    string Cadence,
    int? MaxAccounts,
    string Description,
    IReadOnlyList<string> Features,
    bool IsFeatured,
    bool IsCorporate,
    bool IsWhiteLabel);

public sealed record CommercialSubscriptionSnapshot(
    CommercialPlanDto CurrentPlan,
    int ConnectedAccounts,
    int? RemainingAccounts,
    bool CanAddAccount,
    bool OverLimit,
    IReadOnlyList<CommercialPlanDto> Plans);
