namespace CoachDB.Identity.Contracts;

/// <summary>Read-only lookup of users for other modules (e.g. Team invites, Clients). Never exposes password data.</summary>
public interface IUserDirectory
{
    Task<UserSummary?> GetAsync(Guid userId, CancellationToken cancellationToken);

    Task<UserSummary?> FindByEmailAsync(string email, CancellationToken cancellationToken);
}

public sealed record UserSummary(Guid Id, string Email, string FullName, string? AvatarUrl, string Locale);
