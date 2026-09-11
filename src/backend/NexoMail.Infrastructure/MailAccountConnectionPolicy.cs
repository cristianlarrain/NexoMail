using Microsoft.EntityFrameworkCore;
using NexoMail.Application;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure;

public sealed class MailAccountConnectionPolicy(NexoMailDbContext database, IUserContext userContext)
{
    public async Task EnsureCanConnectAnotherAccountAsync(CancellationToken cancellationToken)
    {
        var access = await CommercialAccessStore.GetAsync(database, userContext.UserId, cancellationToken)
            ?? throw new InvalidOperationException("No fue posible determinar el plan de la cuenta.");
        var plan = access.EffectivePlan;
        if (!plan.MaxAccounts.HasValue) return;

        var connectedAccounts = await database.MailAccounts.AsNoTracking()
            .CountAsync(x => x.UserId == userContext.UserId && x.IsActive, cancellationToken);
        if (connectedAccounts >= plan.MaxAccounts.Value)
            throw new InvalidOperationException($"Su plan efectivo {plan.Name} permite hasta {plan.MaxAccounts.Value} cuentas de correo. Cambie de plan o regularice su suscripción para conectar una cuenta adicional.");
    }
}
