using CoachDB.Identity.Domain.Sessions;
using CoachDB.Identity.Domain.Users;
using CoachDB.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CoachDB.Identity.Infrastructure.Persistence;

/// <summary>Identity owns the <c>identity</c> schema: <c>users</c> and <c>refresh_tokens</c> (+ outbox/inbox).</summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options, ModuleDbContextDependencies dependencies)
    : ModuleDbContext(options, dependencies)
{
    public const string SchemaName = "identity";

    public override string Schema => SchemaName;

    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
}
