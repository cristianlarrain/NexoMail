using System.Security.Claims;
using NexoMail.Application;

namespace NexoMail.Api.Security;

public sealed class HttpUserContext(IHttpContextAccessor httpContextAccessor) : IUserContext
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;

    public Guid UserId
    {
        get
        {
            var value = Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(value, out var userId)
                ? userId
                : throw new InvalidOperationException("No existe un usuario autenticado válido.");
        }
    }

    public string Email => Principal?.FindFirstValue(ClaimTypes.Email) ?? string.Empty;
    public string DisplayName => Principal?.FindFirstValue(ClaimTypes.Name) ?? Email;
}
