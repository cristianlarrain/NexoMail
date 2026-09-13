using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure;

public sealed class AiUsageRetentionService(NexoMailDbContext database)
{
    public async Task<int> DeleteExpiredDetailAsync(DateTimeOffset now, CancellationToken ct)
    {
        await AiUsageSchemaBootstrap.EnsureAsync(database, ct);
        var cutoff = now.AddMonths(-12).UtcDateTime;
        return await database.AiUsageEvents
            .Where(x => x.OccurredAt < cutoff)
            .ExecuteDeleteAsync(ct);
    }
}

public sealed class AiUsageRetentionHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<AiUsageRetentionHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var retention = scope.ServiceProvider.GetRequiredService<AiUsageRetentionService>();
                var deleted = await retention.DeleteExpiredDetailAsync(DateTimeOffset.UtcNow, stoppingToken);
                if (deleted > 0)
                    logger.LogInformation("NexoMail eliminó {DeletedCount} registros detallados de consumo Nexi vencidos.", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "No fue posible ejecutar la limpieza de consumo Nexi.");
            }

            try
            {
                await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
