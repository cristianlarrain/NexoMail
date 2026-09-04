namespace NexoMail.Application;

public interface IUserContext
{
    bool IsAuthenticated { get; }
    Guid UserId { get; }
    string Email { get; }
    string DisplayName { get; }
}
