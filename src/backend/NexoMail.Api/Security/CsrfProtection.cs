using Microsoft.AspNetCore.Antiforgery;

namespace NexoMail.Api.Security;

public static class CsrfProtection
{
    public static IServiceCollection AddNexoMailCsrf(this IServiceCollection services, IWebHostEnvironment environment)
    {
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "NexoMail.Csrf";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
        });
        return services;
    }

    public static IApplicationBuilder UseNexoMailCsrf(this IApplicationBuilder app)
    {
        return app.Use(async (context, next) =>
        {
            if (!RequiresValidation(context.Request))
            {
                await next();
                return;
            }

            var antiforgery = context.RequestServices.GetRequiredService<IAntiforgery>();
            try
            {
                await antiforgery.ValidateRequestAsync(context);
                await next();
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.Headers["X-NexoMail-CSRF"] = "invalid";
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "La solicitud no superó la validación de seguridad. Actualiza la página e inténtalo nuevamente."
                });
            }
        });
    }

    public static IEndpointRouteBuilder MapNexoMailCsrf(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = tokens.RequestToken });
        }).AllowAnonymous();

        return endpoints;
    }

    private static bool RequiresValidation(HttpRequest request)
    {
        if (!request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase)) return false;

        return HttpMethods.IsPost(request.Method) ||
               HttpMethods.IsPut(request.Method) ||
               HttpMethods.IsPatch(request.Method) ||
               HttpMethods.IsDelete(request.Method);
    }
}
