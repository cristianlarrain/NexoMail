using NexoMail.Infrastructure.Google;

namespace NexoMail.Api;

public static class MetadataIndexEndpoints
{
    public static void Map(RouteGroupBuilder mail)
    {
        mail.MapPost("/control-center/index/sync", async (GmailMetadataIndexService service, int? days, int? limitPerAccount, CancellationToken ct) =>
        {
            try { return Results.Ok(await service.SyncAsync(days, limitPerAccount, ct)); }
            catch (InvalidOperationException exception) { return Results.BadRequest(new { error = exception.Message }); }
            catch (HttpRequestException) { return Results.Problem("No fue posible actualizar el índice de metadatos desde Gmail.", statusCode: 502); }
        });

        mail.MapGet("/control-center/contacts", async (GmailMetadataIndexService service, int? days, CancellationToken ct) =>
            Results.Ok(await service.GetContactsAsync(days, ct)));

        mail.MapGet("/control-center/documents", async (GmailMetadataIndexService service, string? search, string? type, int? take, int? skip, CancellationToken ct) =>
            Results.Ok(await service.GetDocumentsAsync(search, type, take, skip, ct)));
    }
}
