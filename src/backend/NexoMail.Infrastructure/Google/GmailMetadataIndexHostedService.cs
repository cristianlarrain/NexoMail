using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NexoMail.Domain;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Infrastructure.Google;

public sealed class GmailMetadataIndexSyncOptions
{
    public const string SectionName = "MailIndexSync";
    public bool Enabled { get; set; } = true;
    public int IntervalMinutes { get; set; } = 5;
    public int WindowDays { get; set; } = 90;
    public int LimitPerAccount { get; set; } = 300;
}

public sealed class GmailMetadataIndexHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<GmailMetadataIndexSyncOptions> options,
    ILogger<GmailMetadataIndexHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var settings = options.Value;
            if (settings.Enabled)
            {
                try
                {
                    await SynchronizeAsync(settings, stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Falló la sincronización periódica del índice de Gmail.");
                }
            }

            var interval = TimeSpan.FromMinutes(Math.Clamp(settings.IntervalMinutes, 1, 60));
            await Task.Delay(interval, stoppingToken);
        }
    }

    private async Task SynchronizeAsync(GmailMetadataIndexSyncOptions settings, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<NexoMailDbContext>();
        var service = scope.ServiceProvider.GetRequiredService<GmailMetadataIndexService>();
        var userIds = await database.MailAccounts
            .AsNoTracking()
            .Where(x => x.IsActive && x.Provider == MailProviderType.Gmail)
            .Select(x => x.UserId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        foreach (var userId in userIds)
        {
            try
            {
                await service.SyncForUserAsync(
                    userId,
                    Math.Clamp(settings.WindowDays, 7, 365),
                    Math.Clamp(settings.LimitPerAccount, 25, 1500),
                    cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "No se pudo actualizar el índice Gmail del usuario {UserId}.", userId);
            }
        }
    }
}
