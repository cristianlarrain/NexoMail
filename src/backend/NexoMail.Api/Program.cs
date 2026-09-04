using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;
using Serilog;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services));
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod()));
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("Gmail", client => client.BaseAddress = new Uri("https://gmail.googleapis.com/gmail/v1/"));
builder.Services.AddHttpClient("GooglePeople", client => client.BaseAddress = new Uri("https://people.googleapis.com/v1/"));
builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddDbContext<NexoMailDbContext>(options => options.UseSqlite(builder.Configuration.GetValue<string>("Database:ConnectionString") ?? "Data Source=nexomail.db"));
var dataProtection = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexoMail", "keys")));
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();
builder.Services.Configure<GmailOptions>(builder.Configuration.GetSection(GmailOptions.SectionName));
builder.Services.AddScoped<ITokenProtector, DataProtectionTokenProtector>();
builder.Services.AddScoped<GoogleOAuthService>();
builder.Services.AddScoped<GoogleContactsService>();
var demoMode = builder.Configuration.GetValue("MailProviders:DemoMode", true);
if (demoMode)
{
    builder.Services.AddSingleton<IMailProvider, DemoMailProvider>();
    builder.Services.AddSingleton<IMailGateway, DemoMailGateway>();
}
else
{
    builder.Services.AddScoped<IMailProvider, GmailMailProvider>();
    builder.Services.AddScoped<IMailGateway, MailGateway>();
}
var app = builder.Build();
using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<NexoMailDbContext>().Database.EnsureCreatedAsync();
app.UseCors();
app.MapOpenApi();

var api = app.MapGroup("/api");
api.MapGet("/health", () => Results.Ok(new { status = "ok", demoMode }));
api.MapGet("/oauth/google/start", (GoogleOAuthService oauth) => Results.Redirect(oauth.BeginAuthorization()));
api.MapGet("/oauth/google/callback", async (string? code, string? state, string? error, GoogleOAuthService oauth, CancellationToken ct) =>
{
    if (!string.IsNullOrWhiteSpace(error)) return Results.Redirect(oauth.FailureRedirect("Google canceló la autorización."));
    if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state)) return Results.Redirect(oauth.FailureRedirect("La respuesta de Google está incompleta."));
    try { await oauth.CompleteAuthorizationAsync(code, state, ct); return Results.Redirect(oauth.SuccessRedirect()); }
    catch (InvalidOperationException exception) { return Results.Redirect(oauth.FailureRedirect(exception.Message)); }
    catch (HttpRequestException) { return Results.Redirect(oauth.FailureRedirect("Google no pudo completar la conexión. Revisa los permisos y vuelve a intentarlo.")); }
});
api.MapGet("/mail/accounts", async (IMailGateway gateway, CancellationToken ct) => Results.Ok(await gateway.GetAccountsAsync(ct)));
api.MapGet("/mail/contacts", async (Guid accountId, string? search, GoogleContactsService contacts, CancellationToken ct) =>
{
    try { return Results.Ok(await contacts.GetContactsAsync(accountId, search, ct)); }
    catch (InvalidOperationException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (HttpRequestException) { return Results.Problem("No se pudo consultar Contactos de Google. Verifica que People API esté habilitada e inténtalo nuevamente.", statusCode: 502); }
});
api.MapPatch("/mail/accounts/{accountId:guid}", async (IMailGateway gateway, Guid accountId, MailAccountSettings request, CancellationToken ct) =>
{
    var displayName = request.DisplayName.Trim();
    var color = request.Color.Trim();
    if (displayName.Length is < 1 or > 80) return Results.BadRequest(new { error = "El nombre debe tener entre 1 y 80 caracteres." });
    if (color.Length != 7 || color[0] != '#' || !color[1..].All(char.IsAsciiHexDigit)) return Results.BadRequest(new { error = "El color debe tener formato hexadecimal, por ejemplo #c6524b." });
    return await gateway.UpdateAccountAsync(accountId, new MailAccountSettings(displayName, color.ToLowerInvariant()), ct) is { } account ? Results.Ok(account) : Results.NotFound();
});
api.MapGet("/mail/messages", async (IMailGateway gateway, Guid? accountId, string? folder, int? take, string? search, CancellationToken ct) => Results.Ok(await gateway.GetMessagesAsync(new MailQuery(accountId, folder ?? "inbox", take ?? 50, null, search), ct)));
api.MapGet("/mail/messages/{accountId:guid}/{messageId}", async (IMailGateway gateway, Guid accountId, string messageId, CancellationToken ct) =>
    await gateway.GetMessageAsync(accountId, messageId, ct) is { } message ? Results.Ok(message) : Results.NotFound());
api.MapGet("/mail/messages/{accountId:guid}/{messageId}/attachments/{attachmentId}", async (IMailGateway gateway, Guid accountId, string messageId, string attachmentId, bool? download, CancellationToken ct) =>
{
    try
    {
        var attachment = await gateway.GetAttachmentAsync(accountId, messageId, attachmentId, ct);
        return attachment is null
            ? Results.NotFound()
            : download == true
                ? Results.File(attachment.Content, attachment.ContentType, attachment.FileName, enableRangeProcessing: true)
                : Results.File(attachment.Content, attachment.ContentType, enableRangeProcessing: true);
    }
    catch (InvalidOperationException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (HttpRequestException exception) { return Results.Problem($"Gmail no pudo entregar el adjunto ({exception.StatusCode?.ToString() ?? "sin código"}).", statusCode: 502); }
});
api.MapPatch("/mail/messages/{accountId:guid}/{messageId}/read", async (IMailGateway gateway, Guid accountId, string messageId, ReadState request, CancellationToken ct) => { await gateway.MarkReadAsync(accountId, messageId, request.Read, ct); return Results.NoContent(); });
api.MapPost("/mail/messages/{accountId:guid}/{messageId}/trash", async (IMailGateway gateway, Guid accountId, string messageId, CancellationToken ct) => { await gateway.MoveToTrashAsync(accountId, messageId, ct); return Results.NoContent(); });
api.MapPost("/mail/folders/{folderId}/empty", async (IMailGateway gateway, string folderId, Guid? accountId, CancellationToken ct) =>
{
    try { await gateway.EmptyFolderAsync(accountId, folderId, ct); return Results.NoContent(); }
    catch (HttpRequestException exception) { return Results.Problem($"Gmail rechazó el vaciado de Papelera ({exception.StatusCode?.ToString() ?? "sin código"}).", statusCode: 502); }
    catch (InvalidOperationException exception) { return Results.Problem(exception.Message, statusCode: 400); }
});
api.MapPost("/mail/send", async (IMailGateway gateway, ComposeMessage request, CancellationToken ct) => { await gateway.SendAsync(request, ct); return Results.Accepted(); });
api.MapPost("/mail/messages/{accountId:guid}/{messageId}/reply", async (IMailGateway gateway, Guid accountId, string messageId, ReplyRequest request, CancellationToken ct) => { await gateway.ReplyAsync(accountId, messageId, request.Message, request.ReplyAll, ct); return Results.Accepted(); });
api.MapPost("/mail/messages/{accountId:guid}/{messageId}/forward", async (IMailGateway gateway, Guid accountId, string messageId, ComposeMessage request, CancellationToken ct) => { await gateway.ForwardAsync(accountId, messageId, request, ct); return Results.Accepted(); });
app.Run();

public sealed record ReadState(bool Read);
public sealed record ReplyRequest(ComposeMessage Message, bool ReplyAll);
