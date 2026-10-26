using CoachDB.Application.Abstractions.Context;
using CoachDB.Application.Abstractions.Messaging;
using CoachDB.Identity.Application.Abstractions;
using CoachDB.Identity.Domain;
using CoachDB.Identity.Domain.Users;
using CoachDB.SharedKernel.Results;

namespace CoachDB.Identity.Application.Users.GetCurrentUser;

/// <summary>The signed-in user's profile (<c>GET /me</c>). Tenant memberships are added by the Tenants module.</summary>
public sealed record GetCurrentUserQuery : IQuery<UserResponse>;

internal sealed class GetCurrentUserHandler(IUserContext currentUser, IUserRepository users)
    : IQueryHandler<GetCurrentUserQuery, UserResponse>
{
    public async ValueTask<Result<UserResponse>> Handle(GetCurrentUserQuery query, CancellationToken cancellationToken)
    {
        if (!currentUser.IsAuthenticated)
            return Error.Unauthorized("auth_required", "Sign in to continue.");

        var user = await users.GetByIdAsync(new UserId(currentUser.UserId), cancellationToken);
        return user is null ? IdentityErrors.UserNotFound : UserResponse.From(user);
    }
}
