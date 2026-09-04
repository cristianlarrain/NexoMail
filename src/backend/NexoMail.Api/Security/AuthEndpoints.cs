using System.Net.Mail;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Api.Security;

public static class AuthEndpoints
{
    private static readonly Guid LegacyLocalUserId = Guid.Parse("a15ac2a9-ca17-4b60-878a-9d42b9a0d001");

    public static IEndpointRouteBuilder MapNexoMailAuth(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/auth");

        auth.MapPost("/register", RegisterAsync);
        auth.MapPost("/login", LoginAsync);
        auth.MapPost("/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.NoContent();
        }).RequireAuthorization();

        auth.MapGet("/me", async (HttpContext context, NexoMailDbContext database, CancellationToken ct) =>
        {
            if (!TryUserId(context.User, out var userId)) return Results.Unauthorized();
            var user = await database.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == userId && x.IsActive, ct);
            return user is null ? Results.Unauthorized() : Results.Ok(ToSession(user));
        }).RequireAuthorization();

        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        HttpContext context,
        NexoMailDbContext database,
        IPasswordHasher<UserEntity> passwordHasher,
        IWebHostEnvironment environment,
        CancellationToken ct)
    {
        var displayName = request.DisplayName.Trim();
        var email = request.Email.Trim().ToLowerInvariant();
        var validation = Validate(displayName, email, request.Password);
        if (validation is not null) return Results.BadRequest(new { error = validation });

        if (await database.Users.AnyAsync(x => x.Email == email, ct))
            return Results.Conflict(new { error = "Ya existe una cuenta NexoMail con ese correo." });

        UserEntity user;
        if (environment.IsDevelopment())
        {
            var legacy = await database.Users.SingleOrDefaultAsync(x => x.Id == LegacyLocalUserId && x.Email == "local@nexomail.invalid" && x.PasswordHash == null, ct);
            if (legacy is not null)
            {
                user = legacy;
                user.DisplayName = displayName;
                user.Email = email;
                user.IsActive = true;
            }
            else
            {
                user = NewUser(displayName, email);
                database.Users.Add(user);
            }
        }
        else
        {
            user = NewUser(displayName, email);
            database.Users.Add(user);
        }

        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
        user.LastLoginAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(ct);
        await SignInAsync(context, user);
        return Results.Ok(ToSession(user));
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        HttpContext context,
        NexoMailDbContext database,
        IPasswordHasher<UserEntity> passwordHasher,
        CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await database.Users.SingleOrDefaultAsync(x => x.Email == email && x.IsActive, ct);
        if (user is null || string.IsNullOrWhiteSpace(user.PasswordHash))
            return Results.Unauthorized();

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed) return Results.Unauthorized();
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await database.SaveChangesAsync(ct);
        await SignInAsync(context, user);
        return Results.Ok(ToSession(user));
    }

    private static UserEntity NewUser(string displayName, string email) => new()
    {
        Id = Guid.NewGuid(),
        DisplayName = displayName,
        Email = email,
        CreatedAt = DateTimeOffset.UtcNow,
        IsActive = true
    };

    private static string? Validate(string displayName, string email, string password)
    {
        if (displayName.Length is < 2 or > 120) return "El nombre debe tener entre 2 y 120 caracteres.";
        try
        {
            var address = new MailAddress(email);
            if (!string.Equals(address.Address, email, StringComparison.OrdinalIgnoreCase)) return "Ingresa un correo válido.";
        }
        catch (FormatException) { return "Ingresa un correo válido."; }

        if (password.Length < 10) return "La contraseña debe tener al menos 10 caracteres.";
        if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit))
            return "La contraseña debe incluir mayúsculas, minúsculas y números.";
        return null;
    }

    private static async Task SignInAsync(HttpContext context, UserEntity user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.DisplayName),
            new Claim(ClaimTypes.Email, user.Email)
        };
        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        await context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties { IsPersistent = true, AllowRefresh = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(14) });
    }

    private static bool TryUserId(ClaimsPrincipal principal, out Guid userId) =>
        Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out userId);

    private static AuthSession ToSession(UserEntity user) => new(user.Id, user.DisplayName, user.Email);
}

public sealed record RegisterRequest(string DisplayName, string Email, string Password);
public sealed record LoginRequest(string Email, string Password);
public sealed record AuthSession(Guid Id, string DisplayName, string Email);
