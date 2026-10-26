using Npgsql;

namespace CoachDB.IntegrationTests.Identity;

/// <summary>What the API writes, checked directly in PostgreSQL.</summary>
public sealed class PersistenceTests(IntegrationFixture fixture)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task User_and_refresh_token_are_stored_safely()
    {
        var email = Http.NewEmail();
        var cookieToken = (await fixture.Factory.CreateBareClient().Register(email.ToUpperInvariant(), password: "Secret123!")).RefreshToken();

        await using var db = await Open(fixture.MigratorConnection);
        await using var command = new NpgsqlCommand(
            """
            SELECT u.email, u.password_hash, t.token_hash
            FROM identity.users u JOIN identity.refresh_tokens t ON t.user_id = u.id
            WHERE u.email = @email
            """, db);
        command.Parameters.AddWithValue("email", email);
        await using var reader = await command.ExecuteReaderAsync(Ct);

        (await reader.ReadAsync(Ct)).ShouldBeTrue();
        reader.GetString(0).ShouldBe(email); // stored lower-case
        reader.GetString(1).ShouldNotBe("Secret123!");
        reader.GetString(1).Length.ShouldBeGreaterThan(40); // PBKDF2 hash, not the password
        var tokenHash = reader.GetString(2);
        tokenHash.ShouldMatch("^[0-9a-f]{64}$");
        tokenHash.ShouldNotBe(cookieToken);
    }

    [Fact]
    public async Task UserRegistered_event_is_delivered_through_the_outbox()
    {
        var email = Http.NewEmail();
        await fixture.Factory.CreateBareClient().Register(email);

        await using var db = await Open(fixture.MigratorConnection);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        bool? processed = null;
        while (DateTime.UtcNow < deadline)
        {
            await using var command = new NpgsqlCommand(
                """
                SELECT processed_at IS NOT NULL
                FROM identity.outbox_messages
                WHERE type LIKE 'CoachDB.Identity.Contracts.Events.UserRegistered,%' AND content->>'email' = @email
                """, db);
            command.Parameters.AddWithValue("email", email);
            processed = (bool?)await command.ExecuteScalarAsync(Ct);
            if (processed == true)
                break;
            await Task.Delay(250, Ct);
        }

        processed.ShouldBe(true, "the outbox row should exist and be marked processed by the dispatcher");
    }

    [Fact]
    public async Task Runtime_roles_have_exactly_the_granted_access()
    {
        await using (var appUser = await Open(fixture.AppUserConnection))
        await using (var select = new NpgsqlCommand("SELECT count(*) FROM identity.users", appUser))
        {
            ((long)(await select.ExecuteScalarAsync(Ct))!).ShouldBeGreaterThanOrEqualTo(0);
        }

        await using var readOnly = await Open(fixture.ReadonlyConnection);
        await using var insert = new NpgsqlCommand(
            "INSERT INTO identity.users (id, email, full_name) VALUES (gen_random_uuid(), 'ro@example.com', 'RO')", readOnly);

        var ex = await Should.ThrowAsync<PostgresException>(() => insert.ExecuteNonQueryAsync(Ct));
        ex.SqlState.ShouldBe(PostgresErrorCodes.InsufficientPrivilege); // 42501
    }

    private static async Task<NpgsqlConnection> Open(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return connection;
    }
}
