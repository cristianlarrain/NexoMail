namespace NexoMail.Infrastructure.Google;

public interface ITokenProtector
{
    string Protect(string value);
    string Unprotect(string protectedValue);
}
