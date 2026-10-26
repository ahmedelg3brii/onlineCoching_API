using Npgsql;

namespace CoachDB.IntegrationTests.Tenants;

/// <summary>
/// The second isolation layer, checked in SQL as the API's own role (<c>app_user</c>): even a query that forgot the EF
/// tenant filter can't read or write another business's rows.
/// </summary>
public sealed class RowLevelSecurityTests(IntegrationFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task App_user_sees_only_the_tenant_in_app_tenant_id()
    {
        var (ownerA, a) = await Session.OwnerOfNewTenant(fixture.Factory);
        var (_, b) = await Session.OwnerOfNewTenant(fixture.Factory);
        await ownerA.Post("/api/v1/tenant/payment-accounts", new { provider = "paymob" });

        await using var db = await AppUser(tenantId: b.Str("id"));
        (await Count(db, "SELECT count(*) FROM tenants.tenant_payment_accounts WHERE tenant_id = @p0", a.Str("id"))).ShouldBe(0);
        (await Count(db, "SELECT count(*) FROM tenants.tenant_plan_subscriptions WHERE tenant_id = @p0", a.Str("id"))).ShouldBe(0);
        (await Count(db, "SELECT count(*) FROM tenants.tenant_members WHERE tenant_id = @p0", a.Str("id"))).ShouldBe(0);
        (await Count(db, "SELECT count(*) FROM tenants.tenant_members WHERE tenant_id = @p0", b.Str("id"))).ShouldBe(1);
    }

    [Fact]
    public async Task No_tenant_set_means_no_tenant_rows()
    {
        await Session.OwnerOfNewTenant(fixture.Factory);

        await using var db = await AppUser(tenantId: null);
        (await Count(db, "SELECT count(*) FROM tenants.tenant_payment_accounts")).ShouldBe(0);
        (await Count(db, "SELECT count(*) FROM tenants.tenant_members")).ShouldBe(0);
    }

    [Fact]
    public async Task Writing_into_another_tenant_is_refused_by_the_database()
    {
        var (_, a) = await Session.OwnerOfNewTenant(fixture.Factory);
        var (_, b) = await Session.OwnerOfNewTenant(fixture.Factory);

        await using var db = await AppUser(tenantId: b.Str("id"));
        await using var insert = new NpgsqlCommand(
            "INSERT INTO tenants.tenant_payment_accounts (id, tenant_id, provider) VALUES (gen_random_uuid(), @p0, 'fawry')", db);
        insert.Parameters.AddWithValue("p0", Guid.Parse(a.Str("id")));

        var ex = await Should.ThrowAsync<PostgresException>(() => insert.ExecuteNonQueryAsync(Ct));
        ex.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege); // 42501: new row violates row-level security policy

        // Updates can't move rows across tenants either: A's rows are invisible, so nothing is updated.
        await using var update = new NpgsqlCommand("UPDATE tenants.tenant_members SET role = 'owner' WHERE tenant_id = @p0", db);
        update.Parameters.AddWithValue("p0", Guid.Parse(a.Str("id")));
        (await update.ExecuteNonQueryAsync(Ct)).ShouldBe(0);
    }

    [Fact]
    public async Task A_user_reads_only_their_own_memberships_across_tenants()
    {
        var (ownerA, _) = await Session.OwnerOfNewTenant(fixture.Factory);
        await Session.OwnerOfNewTenant(fixture.Factory);
        await ownerA.Post("/api/v1/tenants", new { name = "Second", slug = Session.NewSlug() });

        await using var lookup = new NpgsqlConnection(fixture.MigratorConnection);
        await lookup.OpenAsync(Ct);
        await using var find = new NpgsqlCommand("SELECT id FROM identity.users WHERE email = @p0", lookup);
        find.Parameters.AddWithValue("p0", ownerA.Email);
        var userId = (Guid)(await find.ExecuteScalarAsync(Ct))!;

        await using var db = await AppUser(tenantId: null, userId: userId.ToString());
        (await Count(db, "SELECT count(*) FROM tenants.tenant_members")).ShouldBe(2);
        (await Count(db, "SELECT count(*) FROM tenants.tenant_members WHERE user_id <> @p0", userId.ToString())).ShouldBe(0);
    }

    private async Task<NpgsqlConnection> AppUser(string? tenantId, string? userId = null)
    {
        var connection = new NpgsqlConnection(fixture.AppUserConnection);
        await connection.OpenAsync(Ct);
        await using var set = new NpgsqlCommand("SELECT set_config('app.tenant_id', @t, false), set_config('app.user_id', @u, false)", connection);
        set.Parameters.AddWithValue("t", tenantId ?? string.Empty);
        set.Parameters.AddWithValue("u", userId ?? string.Empty);
        await set.ExecuteNonQueryAsync(Ct);
        return connection;
    }

    private static async Task<long> Count(NpgsqlConnection db, string sql, string? p0 = null)
    {
        await using var command = new NpgsqlCommand(sql, db);
        if (p0 is not null)
            command.Parameters.AddWithValue("p0", Guid.Parse(p0));
        return (long)(await command.ExecuteScalarAsync(Ct))!;
    }
}
