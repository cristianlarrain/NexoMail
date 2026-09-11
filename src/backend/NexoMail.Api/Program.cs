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
builder.Services.AddScoped<AiUsageRetentionService>();
builder.Services.AddHostedService<AiUsageRetentionHostedService>();

var dataProtection = builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NexoMail", "keys")));
if (OperatingSystem.IsWindows()) dataProtection.ProtectKeysWithDpapi();

builder.Services.Configure<GmailOptions>(builder.Configuration.GetSection(GmailOptions.SectionName));
builder.Services.AddScoped<ITokenProtector, DataProtectionTokenProtector>();
builder.Services.AddScoped<MailAccountConnectionPolicy>();
builder.Services.AddScoped<GoogleOAuthService>();
builder.Services.AddScoped<GoogleContactsService>();
builder.Services.AddScoped<GmailRuleService>();
builder.Services.AddScoped<IMailRuleProvider>(services => services.GetRequiredService<GmailRuleService>());
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
NexoMail.Api.CommercialEndpoints.MapNexoMailCommercial(api);
NexoMail.Api.AiUsageEndpoints.Map(api);

var oauth = api.MapGroup("/oauth").RequireAuthorization();
oauth.MapGet("/google/start", async (GoogleOAuthService service, CancellationToken ct) =>
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
NexoMail.Api.MailRuleEndpoints.Map(mail);
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
        () => service.GetOverviewAsync(accountId, ct));
    return Results.Ok(value);
});
mail.MapGet("/control-center/activity", async (GmailControlCenterActivityService service, Guid? accountId, CancellationToken ct) => Results.Ok(await service.GetActivityAsync(accountId, ct)));
mail.MapGet("/control-center/contacts", async (GmailControlCenterService service, Guid? accountId, CancellationToken ct) => Results.Ok(await service.GetContactsAsync(accountId, ct)));
mail.MapGet("/control-center/documents", async (GmailControlCenterService service, Guid? accountId, CancellationToken ct) => Results.Ok(await service.GetDocumentsAsync(accountId, ct)));
mail.MapPost("/control-center/index/sync", async (GmailControlCenterService service, CancellationToken ct) => Results.Ok(await service.SyncIndexAsync(ct)));
mail.MapPost("/control-center/tracking/{accountId:guid}/{messageId}", async (Guid accountId, string messageId, GmailControlCenterService service, CancellationToken ct) => Results.Ok(await service.TrackMessageAsync(accountId, messageId, ct)));
mail.MapPatch("/control-center/{accountId:guid}/{conversationId}/state", async (Guid accountId, string conversationId, NexoMail.Api.UpdateConversationStateRequest request, GmailControlCenterService service, CancellationToken ct) => Results.Ok(await service.UpdateConversationStateAsync(accountId, conversationId, request.Status, request.SnoozedUntil, ct)));
mail.MapGet("/messages", async (Guid accountId, string? folderId, string? cursor, int? pageSize, IMailGateway gateway, CancellationToken ct) => Results.Ok(await gateway.GetMessagesAsync(new MailQuery(accountId, folderId, Math.Clamp(pageSize ?? 50, 1, 100), cursor), ct)));
mail.MapGet("/messages/{accountId:guid}/{messageId}", async (Guid accountId, string messageId, IMailGateway gateway, CancellationToken ct) =>
{
    var message = await gateway.GetMessageAsync(accountId, messageId, ct);
    return message is null ? Results.NotFound() : Results.Ok(message);
});
mail.MapGet("/messages/{accountId:guid}/{messageId}/thread", async (Guid accountId, string messageId, IMailGateway gateway, CancellationToken ct) => Results.Ok(await gateway.GetThreadAsync(accountId, messageId, ct)));
mail.MapPost("/messages/{accountId:guid}/{messageId}/read", async (Guid accountId, string messageId, bool isRead, IMailGateway gateway, CancellationToken ct) => { await gateway.MarkReadAsync(accountId, messageId, isRead, ct); return Results.NoContent(); });
mail.MapPost("/send", async (ComposeMessage request, IMailGateway gateway, CancellationToken ct) => { await gateway.SendAsync(request, ct); return Results.NoContent(); });
mail.MapPost("/messages/{accountId:guid}/{messageId}/reply", async (Guid accountId, string messageId, ComposeMessage request, IMailGateway gateway, CancellationToken ct) => { await gateway.ReplyAsync(accountId, messageId, request, ct); return Results.NoContent(); });
mail.MapPost("/messages/{accountId:guid}/{messageId}/forward", async (Guid accountId, string messageId, ComposeMessage request, IMailGateway gateway, CancellationToken ct) => { await gateway.ForwardAsync(accountId, messageId, request, ct); return Results.NoContent(); });
mail.MapPost("/drafts", async (ComposeMessage request, IMailGateway gateway, CancellationToken ct) => { await gateway.SaveDraftAsync(request.AccountId, null, request, ct); return Results.NoContent(); });
mail.MapPut("/drafts/{accountId:guid}/{draftId}", async (Guid accountId, string draftId, ComposeMessage request, IMailGateway gateway, CancellationToken ct) => { await gateway.UpdateDraftAsync(accountId, draftId, request, ct); return Results.NoContent(); });
mail.MapPost("/drafts/{accountId:guid}/{draftId}/send", async (Guid accountId, string draftId, ComposeMessage request, IMailGateway gateway, CancellationToken ct) => { await gateway.SendDraftAsync(accountId, draftId, request, ct); return Results.NoContent(); });
mail.MapPost("/messages/{accountId:guid}/{messageId}/archive", async (Guid accountId, string messageId, IMailGateway gateway, CancellationToken ct) => { await gateway.ArchiveAsync(accountId, messageId, ct); return Results.NoContent(); });
mail.MapPost("/messages/{accountId:guid}/{messageId}/spam", async (Guid accountId, string messageId, IMailGateway gateway, CancellationToken ct) => { await gateway.SpamAsync(accountId, messageId, ct); return Results.NoContent(); });
mail.MapDelete("/messages/{accountId:guid}/{messageId}", async (Guid accountId, string messageId, IMailGateway gateway, CancellationToken ct) => { await gateway.DeleteAsync(accountId, messageId, ct); return Results.NoContent(); });
mail.MapPost("/messages/{accountId:guid}/{messageId}/move", async (Guid accountId, string messageId, NexoMail.Api.MoveMessageRequest request, IMailGateway gateway, CancellationToken ct) => { await gateway.MoveAsync(accountId, messageId, request.FolderId, ct); return Results.NoContent(); });
mail.MapGet("/folders/{accountId:guid}", async (Guid accountId, IMailGateway gateway, CancellationToken ct) => Results.Ok(await gateway.GetFoldersAsync(accountId, ct)));
mail.MapGet("/contacts/{accountId:guid}", async (Guid accountId, GoogleContactsService service, CancellationToken ct) => Results.Ok(await service.GetContactsAsync(accountId, ct)));
mail.MapDelete("/accounts/{accountId:guid}", async (Guid accountId, NexoMailDbContext database, IUserContext userContext, CancellationToken ct) =>
{
    var account = await database.MailAccounts.SingleOrDefaultAsync(x => x.Id == accountId && x.UserId == userContext.UserId, ct);
    if (account is null) return Results.NotFound();
    account.IsActive = false;
    await database.SaveChangesAsync(ct);
    return Results.NoContent();
});

app.Run();
