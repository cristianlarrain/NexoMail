using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using NexoMail.Infrastructure.Data;

namespace NexoMail.Api.Security;

public static class AuthEndpoints
{
    private static readonly Guid LegacyLocalUserId = Guid.Parse("a15ac2a9-ca17-4b60-878a-9d42b9a0d001");
    private const int MaximumRecoveryAttempts = 5;

    public static IEndpointRouteBuilder MapNexoMailAuth(this IEndpointRouteBuilder endpoints)
    {
        var auth = endpoints.MapGroup("/api/auth");

        auth.MapPost("/register", RegisterAsync);
        auth.MapPost("/login", LoginAsync);
        auth.MapPost("/forgot-password", ForgotPasswordAsync);
        auth.MapPost("/verify-reset-code", VerifyResetCodeAsync);
        auth.MapPost("/reset-password", ResetPasswordAsync);
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

    private static async Task<IResult> ForgotPasswordAsync(
        ForgotPasswordRequest request,
        NexoMailDbContext database,
        IWebHostEnvironment environment,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await database.Users.SingleOrDefaultAsync(x => x.Email == email && x.IsActive, ct);

        if (user is not null)
        {
            var verificationCode = CreateVerificationCode();
            user.PasswordResetTokenHash = HashResetToken(verificationCode);
            user.PasswordResetTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
            user.PasswordResetAttempts = 0;
            await database.SaveChangesAsync(ct);

            if (environment.IsDevelopment())
                loggerFactory.CreateLogger("NexoMail.PasswordRecovery")
                    .LogInformation("Código de recuperación NexoMail para {Email}: {VerificationCode}", email, verificationCode);
        }

        return Results.Ok(new
        {
            message = "Si existe una cuenta NexoMail con ese correo, recibirás un código de verificación."
        });
    }

    private static async Task<IResult> VerifyResetCodeAsync(
        VerifyResetCodeRequest request,
        NexoMailDbContext database,
        CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var code = request.Code.Trim();
        var user = await database.Users.SingleOrDefaultAsync(x => x.Email == email && x.IsActive, ct);

        if (user is null || string.IsNullOrWhiteSpace(user.PasswordResetTokenHash) ||
            user.PasswordResetTokenExpiresAt <= DateTimeOffset.UtcNow ||
            user.PasswordResetAttempts >= MaximumRecoveryAttempts)
            return Results.BadRequest(new { error = "El código no es válido o ya expiró." });

        var suppliedHash = HashResetToken(code);
        var matches = CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(user.PasswordResetTokenHash),
            Encoding.UTF8.GetBytes(suppliedHash));

        if (!matches)
        {
            user.PasswordResetAttempts++;
            if (user.PasswordResetAttempts >= MaximumRecoveryAttempts)
            {
                user.PasswordResetTokenHash = null;
                user.PasswordResetTokenExpiresAt = null;
            }
            await database.SaveChangesAsync(ct);
            return Results.BadRequest(new { error = "El código no es válido o ya expiró." });
        }

        var resetToken = CreateResetToken();
        user.PasswordResetTokenHash = HashResetToken(resetToken);
        user.PasswordResetTokenExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10);
        user.PasswordResetAttempts = 0;
        await database.SaveChangesAsync(ct);
        return Results.Ok(new { resetToken });
    }

    private static async Task<IResult> ResetPasswordAsync(
        ResetPasswordRequest request,
        NexoMailDbContext database,
        IPasswordHasher<UserEntity> passwordHasher,
        CancellationToken ct)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var passwordValidation = ValidatePassword(request.NewPassword);
        if (passwordValidation is not null) return Results.BadRequest(new { error = passwordValidation });

        var user = await database.Users.SingleOrDefaultAsync(x => x.Email == email && x.IsActive, ct);
        if (user is null || string.IsNullOrWhiteSpace(user.PasswordResetTokenHash) || user.PasswordResetTokenExpiresAt <= DateTimeOffset.UtcNow)
            return Results.BadRequest(new { error = "La autorización para cambiar la contraseña no es válida o ya expiró." });

        var suppliedHash = HashResetToken(request.Token);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(user.PasswordResetTokenHash),
                Encoding.UTF8.GetBytes(suppliedHash)))
            return Results.BadRequest(new { error = "La autorización para cambiar la contraseña no es válida o ya expiró." });

        user.PasswordHash = passwordHasher.HashPassword(user, request.NewPassword);
        user.PasswordResetTokenHash = null;
        user.PasswordResetTokenExpiresAt = null;
        user.PasswordResetAttempts = 0;
        await database.SaveChangesAsync(ct);
        return Results.NoContent();
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

        return ValidatePassword(password);
    }

    private static string? ValidatePassword(string password)
    {
        if (password.Length < 10) return "La contraseña debe tener al menos 10 caracteres.";
        if (!password.Any(char.IsUpper) || !password.Any(char.IsLower) || !password.Any(char.IsDigit))
            return "La contraseña debe incluir mayúsculas, minúsculas y números.";
        return null;
    }

    private static string CreateVerificationCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    private static string CreateResetToken()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    private static string HashResetToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

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
public sealed record ForgotPasswordRequest(string Email);
public sealed record VerifyResetCodeRequest(string Email, string Code);
public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);
public sealed record AuthSession(Guid Id, string DisplayName, string Email);
