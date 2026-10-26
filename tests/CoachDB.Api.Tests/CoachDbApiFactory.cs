using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace CoachDB.Api.Tests;

/// <summary>Boots the real host in-memory. Shared per test class via <see cref="IClassFixture{TFixture}"/>.</summary>
public sealed class CoachDbApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("Jwt:SigningKey", "test-only-signing-key-0123456789abcdef0123");
    }
}
