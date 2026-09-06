using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using NexoMail.Api.Security;
using NexoMail.Application;
using NexoMail.Domain;
using NexoMail.Infrastructure;
using NexoMail.Infrastructure.Data;
using NexoMail.Infrastructure.Google;
using Serilog;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsDevelopment())
{
    builder.WebHost.UseUrls("http://localhost:5052");
}

builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services));

builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));
builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddHttpClient("Gmail", client => client.BaseAddress = new Uri("https://gmail.googleapis.com/gmail/v1/"));
builder.Services.AddHttpClient("GooglePeople", client => client.BaseAddress = new Uri("https://people.googleapis.com/v1/"));
builder.Services.AddOpenApi();
builder.Services.AddMemoryCache(options => options.SizeLimit = 512);
builder.Services.AddSingleton<NexoMail.Api.MailReadCache>();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddDbContext<NexoMailDbContext>(options => options.UseSqlite(builder.Configuration.GetValue<string>("Database:ConnectionString") ?? "Data Source=nexomail.db"));

builder.Services.AddScoped<NexoMailCookieEvents>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "NexoMail.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        options.ExpireTimeSpan = TimeSpan.FromDays(14);
        options.SlidingExpiration = true;
        options.EventsType = typeof(NexoMailCookieEvents);
    });
builder.Services.AddAuthorization();
builder.Services.AddNexoMailCsrf(builder.Environment);
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            error = "Demasiados intentos. Inténtalo nuevamente en unos minutos."
        }, ct);
    };

    options.AddPolicy("auth-login", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(5),
            QueueLimit = 0,
            AutoReplenishment = true
        }));

    options.AddPolicy("auth-register", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(30),
            QueueLimit = 0,
            AutoReplenishment = true
        }));

    options.AddPolicy("auth-send-code", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0,
            AutoReplenishment = true
        }));

    options.AddPolicy("auth-verify-code", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(10),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});
builder.Services.AddScoped<IPasswordHasher<UserEntity>, PasswordHasher<UserEntity>>();
builder.Services.AddScoped<IUserContext, HttpUserContext>();
builder.Services.Configure<RecoveryEmailOptions>(builder.Configuration.GetSection(RecoveryEmailOptions.SectionName));
builder.Services.AddScoped<IPasswordRecoveryEmailSender, SmtpPasswordRecoveryEmailSender>();
NexoMail.Api.AiEndpoints.AddNexoMailAi(builder.Services, builder.Configuration);

var dataProtection = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexoMail", "keys")));
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();

builder.Services.Configure<GmailOptions>(builder.Configuration.GetSection(GmailOptions.SectionName));
builder.Services.AddScoped<ITokenProtector, DataProtectionTokenProtector>();
builder.Services.AddScoped<GoogleOAuthService>();
builder.Services.AddScoped<GoogleContactsService>();
builder.Services.AddScoped<GmailControlCenterService>();
builder.Services.AddScoped<GmailControlCenterActivityService>();

var demoMode = builder.Configuration.GetValue("MailProviders:DemoMode", true);
if (demoMode)
{
    builder.Services.AddSingleton<IMailProvider, DemoMailProvider>();
    builder.Services.AddSingleton<IMailGateway, DemoMailGateway>();
}
else
{
    builder.Services.AddScoped<GmailMailProvider>();
    builder.Services.AddScoped<IMailProvider>(services => new UserScopedMailProvider(
        services.GetRequiredService<GmailMailProvider>(),
        services.GetRequiredService<NexoMailDbContext>(),
        services.GetRequiredService<IUserContext>()));
    builder.Services.AddScoped<IMailGateway, MailGateway>();
}

var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var database = scope.ServiceProvider.GetRequiredService<NexoMailDbContext>();
    await database.Database.EnsureCreatedAsync();
    await DatabaseBootstrap.EnsureAuthenticationSchemaAsync(database);
}

app.Use(async (context, next) =>
{
    try
    {
        await next();
    }
    catch (KeyNotFoundException)
    {
        if (context.Response.HasStarted) throw;
        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status404NotFound;
        await context.Response.WriteAsJsonAsync(new { error = "Recurso no disponible." });
    }
});

app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseNexoMailCsrf();
app.UseAuthorization();
app.MapOpenApi();
app.MapNexoMailCsrf();
app.MapNexoMailAuth();
app.MapNexoMailSessions();

var api = app.MapGroup("/api");
api.MapGet("/health", () => Results.Ok(new { status = "ok", demoMode }));

var oauth = api.MapGroup("/oauth").RequireAuthorization();
oauth.MapGet("/google/start", (GoogleOAuthService service) => Results.Redirect(service.BeginAuthorization()));
oauth.MapGet("/google/callback", async (string? code, string? state, string? error, GoogleOAuthService service, CancellationToken ct) =>
{
    if (!string.IsNullOrWhiteSpace(error)) return Results.Redirect(service.FailureRedirect("Google canceló la autorización."));
    if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(state)) return Results.Redirect(service.FailureRedirect("La respuesta de Google está incompleta."));
    try { await service.CompleteAuthorizationAsync(code, state, ct); return Results.Redirect(service.SuccessRedirect()); }
    catch (InvalidOperationException exception) { return Results.Redirect(service.FailureRedirect(exception.Message)); }
    catch (HttpRequestException) { return Results.Redirect(service.FailureRedirect("Google no pudo completar la conexión. Revisa los permisos y vuelve a intentarlo.")); }
});

var mail = api.MapGroup("/mail").RequireAuthorization();
NexoMail.Api.AiEndpoints.MapNexoMailAi(mail);
mail.MapGet("/accounts", async (IMailGateway gateway, CancellationToken ct) => Results.Ok(await gateway.GetAccountsAsync(ct)));
mail.MapPost("/refresh", (NexoMail.Api.MailReadCache cache, IUserContext userContext) =>
{
    cache.Invalidate(userContext.UserId.ToString());
    return Results.NoContent();
});
mail.MapGet("/control-center", async (GmailControlCenterService service, NexoMail.Api.MailReadCache cache, IUserContext userContext, Guid? accountId, CancellationToken ct) =>
{
    var scope = accountId?.ToString("N") ?? "all";
    var value = await cache.GetOrCreateAsync(
        userContext.UserId.ToString(),
        "control-center",
        scope,
        TimeSpan.FromSeconds(90),
        token => service.GetSnapshotAsync(accountId, token),
        ct);
    return Results.Ok(value);
});
mail.MapGet("/control-center/activity", async (GmailControlCenterActivityService service, NexoMail.Api.MailReadCache cache, IUserContext userContext, Guid? accountId, int? days, int? offsetDays, CancellationToken ct) =>
{
    var normalizedDays = days is 7 or 14 or 30 ? days.Value : 7;
    var normalizedOffset = Math.Max(0, offsetDays ?? 0);
    var scope = $"{accountId?.ToString("N") ?? "all"}:{normalizedDays}:{normalizedOffset}";
    var value = await cache.GetOrCreateAsync(
        userContext.UserId.ToString(),
        "control-center-activity",
        scope,
        TimeSpan.FromMinutes(5),
        token => service.GetActivityAsync(accountId, normalizedDays, normalizedOffset, token),
        ct);
    return Results.Ok(value);
});
mail.MapPatch("/control-center/{accountId:guid}/{conversationId}/state", async (GmailControlCenterService service, NexoMail.Api.MailReadCache cache, IUserContext userContext, Guid accountId, string conversationId, ControlCenterStateRequest request, CancellationToken ct) =>
{
    var action = request.Action.Trim().ToLowerInvariant();
    if (string.IsNullOrWhiteSpace(conversationId) || string.IsNullOrWhiteSpace(request.MessageId))
        return Results.BadRequest(new { error = "La conversación no es válida." });
    if (action is not ("resolved" or "snoozed"))
        return Results.BadRequest(new { error = "La acción solicitada no es válida." });
    if (action == "snoozed" && request.SnoozeHours is < 1 or > 720)
        return Results.BadRequest(new { error = "El plazo de posposición no es válido." });

    var updated = await service.UpdateStateAsync(accountId, conversationId, request.MessageId, action, request.SnoozeHours, ct);
    if (updated) cache.Invalidate(userContext.UserId.ToString());
    return updated ? Results.NoContent() : Results.NotFound();
});
mail.MapGet("/contacts", async (Guid accountId, string? search, GoogleContactsService contacts, CancellationToken ct) =>
{
    try { return Results.Ok(await contacts.GetContactsAsync(accountId, search, ct)); }
    catch (InvalidOperationException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (HttpRequestException) { return Results.Problem("No se pudo consultar Contactos de Google. Verifica que People API esté habilitada e inténtalo nuevamente.", statusCode: 502); }
});
mail.MapPatch("/accounts/{accountId:guid}", async (IMailGateway gateway, NexoMail.Api.MailReadCache cache, IUserContext userContext, Guid accountId, MailAccountSettings request, CancellationToken ct) =>
{
    var displayName = request.DisplayName.Trim();
    var color = request.Color.Trim();
    if (displayName.Length is < 1 or > 80) return Results.BadRequest(new { error = "El nombre debe tener entre 1 y 80 caracteres." });
    if (color.Length != 7 || color[0] != '#' || !color[1..].All(char.IsAsciiHexDigit)) return Results.BadRequest(new { error = "El color debe tener formato hexadecimal, por ejemplo #c6524b." });
    var account = await gateway.UpdateAccountAsync(accountId, new MailAccountSettings(displayName, color.ToLowerInvariant()), ct);
    if (account is not null) cache.Invalidate(userContext.UserId.ToString());
    return account is not null ? Results.Ok(account) : Results.NotFound();
});
mail.MapDelete("/accounts/{accountId:guid}", async (NexoMailDbContext database, NexoMail.Api.MailReadCache cache, IUserContext userContext, Guid accountId, CancellationToken ct) =>
{
    var userId = userContext.UserId;
    var account = await database.MailAccounts.SingleOrDefaultAsync(x => x.Id == accountId && x.UserId == userId, ct);
    if (account is null) return Results.NotFound();

    var credentials = await database.OAuthCredentials.Where(x => x.MailAccountId == accountId).ToArrayAsync(ct);
    if (credentials.Length > 0) database.OAuthCredentials.RemoveRange(credentials);
    database.MailAccounts.Remove(account);
    await database.SaveChangesAsync(ct);
    cache.Invalidate(userContext.UserId.ToString());
    return Results.NoContent();
});
mail.MapGet("/messages", async (IMailGateway gateway, NexoMail.Api.MailReadCache cache, IUserContext userContext, Guid? accountId, string? folder, int? take, string? cursor, string? search, CancellationToken ct) =>
{
    var normalizedFolder = folder ?? "inbox";
    var normalizedTake = take ?? 50;
    var normalizedSearch = search?.Trim() ?? string.Empty;
    var normalizedCursor = cursor ?? string.Empty;
    var scope = $"{accountId?.ToString("N") ?? "all"}:{normalizedFolder}:{normalizedTake}:{normalizedCursor}:{normalizedSearch}";
    var value = await cache.GetOrCreateAsync(
        userContext.UserId.ToString(),
        "messages",
        scope,
        TimeSpan.FromSeconds(45),
        token => gateway.GetMessagesAsync(new MailQuery(accountId, normalizedFolder, normalizedTake, cursor, search), token),
        ct);
    return Results.Ok(value);
});
mail.MapGet("/messages/{accountId:guid}/{messageId}", async (IMailGateway gateway, Guid accountId, string messageId, CancellationToken ct) =>
    await gateway.GetMessageAsync(accountId, messageId, ct) is { } message ? Results.Ok(message) : Results.NotFound());
mail.MapGet("/messages/{accountId:guid}/{messageId}/attachments/{attachmentId}", async (IMailGateway gateway, Guid accountId, string messageId, string attachmentId, string? fileName, bool? download, CancellationToken ct) =>
{
    try
    {
        var attachment = await gateway.GetAttachmentAsync(accountId, messageId, attachmentId, ct);
        var safeFileName = Path.GetFileName(string.IsNullOrWhiteSpace(fileName) ? attachment?.FileName ?? "adjunto" : fileName);
        var contentType = Path.GetExtension(safeFileName).ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf", ".png" => "image/png", ".jpg" or ".jpeg" => "image/jpeg", ".gif" => "image/gif", ".webp" => "image/webp",
            ".txt" or ".log" or ".csv" => "text/plain; charset=utf-8", ".json" => "application/json", ".xml" => "application/xml", _ => attachment?.ContentType ?? "application/octet-stream"
        };
        return attachment is null
            ? Results.NotFound()
            : download == true
                ? Results.File(attachment.Content, contentType, safeFileName, enableRangeProcessing: true)
                : Results.File(attachment.Content, contentType, enableRangeProcessing: true);
    }
    catch (InvalidOperationException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (HttpRequestException exception) { return Results.Problem($"Gmail no pudo entregar el adjunto ({exception.StatusCode?.ToString() ?? "sin código"}).", statusCode: 502); }
});
mail.MapPatch("/messages/{accountId:guid}/{messageId}/read", async (IMailGateway gateway, NexoMail.Api.MailReadCache cache, IUserContext userContext, Guid accountId, string messageId, ReadState request, CancellationToken ct) =>
{
    await gateway.MarkReadAsync(accountId, messageId, request.Read, ct);
    cache.Invalidate(userContext.UserId.ToString());
    return Results.NoContent();
});
mail.MapPost("/messages/{accountId:guid}/{messageId}/trash", async (IMailGateway gateway, NexoMail.Api.MailReadCache cache, IUserContext userContext, Guid accountId, string messageId, CancellationToken ct) =>
{
    await gateway.MoveToTrashAsync(accountId, messageId, ct);
    cache.Invalidate(userContext.UserId.ToString());
    return Results.NoContent();
});
mail.MapPost("/messages/{accountId:guid}/{messageId}/move", async (IMailGateway gateway, NexoMail.Api.MailReadCache cache, IUserContext userContext, Guid accountId, string messageId, MoveRequest request, CancellationToken ct) =>
{
    try
    {
        await gateway.MoveToFolderAsync(accountId, messageId, request.FolderId, ct);
        cache.Invalidate(userContext.UserId.ToString());
        return Results.NoContent();
    }
    catch (InvalidOperationException exception) { return Results.BadRequest(new { error = exception.Message }); }
    catch (HttpRequestException exception) { return Results.Problem($"Gmail no pudo mover el correo ({exception.StatusCode?.ToString() ?? "sin código"}).", statusCode: 502); }
});
mail.MapPost("/folders/{folderId}/empty", async (IMailGateway gateway, NexoMail.Api.MailReadCache cache, IUserContext userContext, string folderId, Guid? accountId, CancellationToken ct) =>
{
    try
    {
        await gateway.EmptyFolderAsync(accountId, folderId, ct);
        cache.Invalidate(userContext.UserId.ToString());
        return Results.NoContent();
    }
    catch (HttpRequestException exception) { return Results.Problem($"Gmail rechazó el vaciado de Papelera ({exception.StatusCode?.ToString() ?? "sin código"}).", statusCode: 502); }
    catch (InvalidOperationException exception) { return Results.Problem(exception.Message, statusCode: 400); }
});
mail.MapPost("/send", async (IMailGateway gateway, NexoMail.Api.MailReadCache cache, IUserContext userContext, ComposeMessage request, CancellationToken ct) =>
{
    await gateway.SendAsync(request, ct);
    cache.Invalidate(userContext.UserId.ToString());
    return Results.Accepted();
});
mail.MapPost("/messages/{accountId:guid}/{messageId}/reply", async (IMailGateway gateway, NexoMail.Api.MailReadCache cache, IUserContext userContext, Guid accountId, string messageId, ReplyRequest request, CancellationToken ct) =>
{
    await gateway.ReplyAsync(accountId, messageId, request.Message, request.ReplyAll, ct);
    cache.Invalidate(userContext.UserId.ToString());
    return Results.Accepted();
});
mail.MapPost("/messages/{accountId:guid}/{messageId}/forward", async (IMailGateway gateway, NexoMail.Api.MailReadCache cache, IUserContext userContext, Guid accountId, string messageId, ComposeMessage request, CancellationToken ct) =>
{
    await gateway.ForwardAsync(accountId, messageId, request, ct);
    cache.Invalidate(userContext.UserId.ToString());
    return Results.Accepted();
});

app.Run();

public sealed record ReadState(bool Read);
public sealed record MoveRequest(string FolderId);
public sealed record ReplyRequest(ComposeMessage Message, bool ReplyAll);
public sealed record ControlCenterStateRequest(string MessageId, string Action, int? SnoozeHours);
