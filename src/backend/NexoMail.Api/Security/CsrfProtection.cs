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
            try
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
            }
            catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
            {
                // Navigating away, reloading, or cancelling a React Query request aborts the
                // underlying Gmail HttpClient call. That is an expected client cancellation,
                // not an application failure. Real provider timeouts still propagate because
                // RequestAborted is not set in that case.
                if (!context.Response.HasStarted)
                {
                    context.Response.Clear();
                    context.Response.StatusCode = 499; // Client Closed Request
                }
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
