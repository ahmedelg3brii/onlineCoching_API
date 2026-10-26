using CoachDB.Identity.Domain.Users;

namespace CoachDB.Identity.Application.Users;

/// <summary>The signed-in user as the API returns it. Shared by register, login, refresh and <c>GET /me</c>.</summary>
public sealed record UserResponse(
    Guid Id,
    string Email,
    string FullName,
    string? Phone,
    string? AvatarUrl,
    string Locale,
    bool EmailVerified,
    bool IsPlatformAdmin,
    DateTimeOffset CreatedAt)
{
    public static UserResponse From(User user) => new(
        user.Id.Value,
        user.Email.Value,
        user.FullName,
        user.Phone,
        user.AvatarUrl,
        user.Locale,
        user.EmailVerifiedAt.HasValue,
        user.IsPlatformAdmin,
        user.CreatedAt);
}
