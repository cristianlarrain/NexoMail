using Microsoft.AspNetCore.DataProtection;

namespace NexoMail.Infrastructure.Google;

public sealed class DataProtectionTokenProtector(IDataProtectionProvider provider) : ITokenProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("NexoMail.OAuth.RefreshTokens.v1");
    public string Protect(string value) => _protector.Protect(value);
    public string Unprotect(string protectedValue) => _protector.Unprotect(protectedValue);
}
