using MailKit.Security;
using NexoMail.Application;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Imap;
using NexoMail.Infrastructure.Microsoft;

namespace NexoMail.Api;

public static class MailProviderBetaModule
{
    public static void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<Microsoft365Options>(configuration.GetSection(Microsoft365Options.SectionName));
        services.AddScoped<MicrosoftOAuthService>();
        services.AddScoped<MicrosoftGraphClientFactory>();
        services.AddScoped<MicrosoftGraphMailProvider>();
        services.AddScoped<MicrosoftGraphDraftProvider>();
        services.AddScoped<IMailProvider>(provider => new UserScopedMailProvider(
            provider.GetRequiredService<MicrosoftGraphMailProvider>(),
            provider.GetRequiredService<NexoMailDbContext>(),
            provider.GetRequiredService<IUserContext>()));
        services.AddScoped<IMailDraftProvider>(provider => provider.GetRequiredService<MicrosoftGraphDraftProvider>());

        services.AddScoped<ImapAccountService>();
        services.AddScoped<ImapMailProvider>();
        services.AddScoped<IMailProvider>(provider => new UserScopedMailProvider(
            provider.GetRequiredService<ImapMailProvider>(),
            provider.GetRequiredService<NexoMailDbContext>(),
            provider.GetRequiredService<IUserContext>()));
    }

    public static Task EnsureSchemaAsync(NexoMailDbContext database, CancellationToken ct = default) =>
        ImapSchemaBootstrap.EnsureAsync(database, ct);

    public static void Map(RouteGroupBuilder api)
    {
        var oauth = api.MapGroup("/oauth").RequireAuthorization();
        oauth.MapGet("/microsoft/start", async (MicrosoftOAuthService service, CancellationToken ct) =>
        {
            try
            {
                await service.EnsureCanConnectAnotherAccountAsync(ct);
                return Results.Redirect(service.BeginAuthorization());
            }
            catch (InvalidOperationException exception)
            {
                return Results.Redirect(service.FailureRedirect(exception.Message));
            }
        });
        oauth.MapGet("/microsoft/callback", async (
            string? code,
            string? state,
            string? error,
            string? error_description,
            MicrosoftOAuthService service,
            CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(error))
                return Results.Redirect(service.FailureRedirect(MicrosoftOAuthErrors.ToUserMessage(error_description ?? error)));
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state))
                return Results.Redirect(service.FailureRedirect("La respuesta de Microsoft está incompleta. Inicia nuevamente la conexión."));
            try
            {
                await service.CompleteAuthorizationAsync(code, state, ct);
                return Results.Redirect(service.SuccessRedirect());
            }
            catch (InvalidOperationException exception)
            {
                return Results.Redirect(service.FailureRedirect(exception.Message));
            }
            catch (HttpRequestException)
            {
                return Results.Redirect(service.FailureRedirect("Microsoft 365 no pudo completar la conexión. Revisa los permisos de la cuenta e inténtalo nuevamente."));
            }
        });

        var mail = api.MapGroup("/mail").RequireAuthorization();
        mail.MapPost("/accounts/imap/connect", async (ImapAccountService service, ImapConnectionRequest request, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await service.ConnectAsync(request, ct));
            }
            catch (AuthenticationException)
            {
                return Results.BadRequest(new { error = "El servidor rechazó el usuario o la contraseña. Si tu proveedor usa verificación en dos pasos, prueba con una contraseña de aplicación." });
            }
            catch (System.Security.Authentication.AuthenticationException)
            {
                return Results.BadRequest(new { error = "No fue posible establecer una conexión TLS segura con el servidor de correo." });
            }
            catch (InvalidOperationException exception)
            {
                return Results.BadRequest(new { error = exception.Message });
            }
            catch (IOException)
            {
                return Results.Problem("No fue posible comunicarse con el servidor IMAP/SMTP indicado.", statusCode: 502);
            }
            catch (System.Net.Sockets.SocketException)
            {
                return Results.Problem("No fue posible comunicarse con el servidor IMAP/SMTP indicado.", statusCode: 502);
            }
        });
    }
}
