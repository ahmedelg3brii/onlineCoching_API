using CoachDB.IntegrationTests;
using CoachDB.Migrator;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Testcontainers.PostgreSql;

[assembly: AssemblyFixture(typeof(IntegrationFixture))]

namespace CoachDB.IntegrationTests;

/// <summary>
/// One real PostgreSQL 16 for the whole run, prepared exactly like a deployed environment: the dev role
/// script (deploy/postgres/init/01-roles.sql) runs as superuser, then CoachDB.Migrator applies every module
/// migration as <c>app_migrator</c>. The API under test connects as the restricted <c>app_user</c>.
/// Tests isolate their data with unique emails, so no reset is needed and they can run in parallel.
/// </summary>
public sealed class IntegrationFixture : IAsyncLifetime
{
    public const string Database = "coachdb";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16")
        .WithDatabase(Database)
        .WithUsername("postgres")
        .WithPassword("postgres")
        .Build();

    private ApiFactory? _factory;

    public ApiFactory Factory => _factory ?? throw new InvalidOperationException("Fixture not initialised.");

    public string SuperuserConnection => _postgres.GetConnectionString();
    public string MigratorConnection => As("app_migrator", "app_migrator_dev");
    public string AppUserConnection => As("app_user", "app_user_dev");
    public string ReadonlyConnection => As("app_readonly", "app_readonly_dev");

    public async ValueTask InitializeAsync()
    {
        await _postgres.StartAsync();

        await using (var connection = new NpgsqlConnection(SuperuserConnection))
        {
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(await File.ReadAllTextAsync(RolesScriptPath()), connection);
            await command.ExecuteNonQueryAsync();
        }

        await MigrationRunner.RunAsync(MigratorConnection, NullLoggerFactory.Instance, CancellationToken.None);

        _factory = new ApiFactory(AppUserConnection);
    }

    public async ValueTask DisposeAsync()
    {
        if (_factory is not null)
            await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    private string As(string user, string password) =>
        new NpgsqlConnectionStringBuilder(SuperuserConnection) { Username = user, Password = password }.ConnectionString;

    private static string RolesScriptPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "CoachDB.slnx")))
            dir = dir.Parent;

        return dir is null
            ? throw new InvalidOperationException("Repository root (CoachDB.slnx) not found.")
            : Path.Combine(dir.FullName, "deploy", "postgres", "init", "01-roles.sql");
    }
}

/// <summary>The real host, pointed at the test container as <c>app_user</c>.</summary>
public sealed class ApiFactory(string appUserConnection) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("ConnectionStrings:CoachDb", appUserConnection);
        builder.UseSetting("Jwt:SigningKey", "integration-test-signing-key-0123456789abcdef");
        builder.UseSetting("RateLimits:AuthPerMinute", "1000");
    }

    /// <summary>
    /// Browser-like client. HTTPS base address because the refresh cookie is <c>Secure</c>: the cookie
    /// container never sends a Secure cookie to an http:// URI.
    /// </summary>
    public HttpClient CreateBrowserClient(bool https = true) => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri(https ? "https://localhost" : "http://localhost"),
        HandleCookies = true,
        AllowAutoRedirect = false,
    });

    /// <summary>Client without cookies, like a native app sending the refresh token in the body.</summary>
    public HttpClient CreateBareClient() => CreateClient(new WebApplicationFactoryClientOptions
    {
        BaseAddress = new Uri("https://localhost"),
        HandleCookies = false,
        AllowAutoRedirect = false,
    });
}
