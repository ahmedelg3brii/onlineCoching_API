using System.Reflection;
using CoachDB.Api.Common.Endpoints;
using CoachDB.Application.Abstractions.Messaging;
using CoachDB.Infrastructure.Common.Persistence;
using CoachDB.SharedKernel.Results;
using NetArchTest.Rules;

namespace CoachDB.ArchitectureTests;

/// <summary>
/// One row of the dependency table in CoachDB-Backend-Architecture-Plan.md → "Dependency rules":
/// types in <see cref="Assembly"/> must not reference any namespace starting with one of <see cref="Forbidden"/>.
/// </summary>
public sealed record DependencyRule(string Name, Assembly Assembly, string[] Forbidden)
{
    public override string ToString() => Name;
}

public static class Layers
{
    public const string AspNetCore = "Microsoft.AspNetCore";
    public const string EfCore = "Microsoft.EntityFrameworkCore";
    public const string Npgsql = "Npgsql";

    public static readonly Assembly SharedKernel = typeof(Result).Assembly;
    public static readonly Assembly ApplicationAbstractions = typeof(ICommand).Assembly;
    public static readonly Assembly ApiCommon = typeof(IEndpoint).Assembly;
    public static readonly Assembly InfrastructureCommon = typeof(ModuleDbContext).Assembly;

    /// <summary>All 12 module names, so a module can be forbidden from referencing every other module.</summary>
    public static readonly string[] Modules =
    [
        "Identity", "Tenants", "Team", "Clients", "Questionnaires", "Packages",
        "Payments", "Progress", "Media", "Nutrition", "Training", "Chat",
    ];

    /// <summary>
    /// Rules for one module, per the plan's table. Other modules' <c>*.Contracts</c> are allowed from
    /// Application, so "other module" here means its Domain/Application/Infrastructure/Api namespaces.
    /// </summary>
    public static IEnumerable<DependencyRule> ForModule(
        string module, Assembly domain, Assembly application, Assembly infrastructure, Assembly api, Assembly contracts)
    {
        var others = Modules.Where(m => m != module).ToArray();
        string[] OtherInternals(params string[] layers) =>
            others.SelectMany(m => layers.Select(l => $"CoachDB.{m}.{l}")).ToArray();

        yield return new($"{module}.Domain", domain,
            [AspNetCore, EfCore, Npgsql, "CoachDB.Application", $"CoachDB.{module}.Application",
             $"CoachDB.{module}.Infrastructure", $"CoachDB.{module}.Api", .. others.Select(m => $"CoachDB.{m}")]);

        yield return new($"{module}.Application", application,
            [AspNetCore, EfCore, Npgsql, $"CoachDB.{module}.Infrastructure", $"CoachDB.{module}.Api",
             .. OtherInternals("Domain", "Application", "Infrastructure", "Api")]);

        yield return new($"{module}.Infrastructure", infrastructure,
            [$"CoachDB.{module}.Api", .. OtherInternals("Domain", "Application", "Infrastructure", "Api")]);

        yield return new($"{module}.Api", api,
            [EfCore, Npgsql, $"CoachDB.{module}.Infrastructure", .. OtherInternals("Domain", "Application", "Infrastructure", "Api")]);

        yield return new($"{module}.Contracts", contracts,
            [AspNetCore, EfCore, Npgsql, "CoachDB.Application", "CoachDB.Api", "CoachDB.Infrastructure",
             $"CoachDB.{module}.Domain", $"CoachDB.{module}.Application", $"CoachDB.{module}.Infrastructure",
             $"CoachDB.{module}.Api", .. OtherInternals("Domain", "Application", "Infrastructure", "Api")]);
    }

    public static IEnumerable<DependencyRule> All()
    {
        yield return new("SharedKernel", SharedKernel,
            [AspNetCore, EfCore, Npgsql, "CoachDB.Application", "CoachDB.Api", "CoachDB.Infrastructure",
             .. Modules.Select(m => $"CoachDB.{m}")]);

        yield return new("Application.Abstractions", ApplicationAbstractions,
            [AspNetCore, EfCore, Npgsql, "CoachDB.Api", "CoachDB.Infrastructure", .. Modules.Select(m => $"CoachDB.{m}")]);

        yield return new("Api.Common", ApiCommon,
            [EfCore, Npgsql, "CoachDB.Infrastructure", .. Modules.Select(m => $"CoachDB.{m}")]);

        // Infrastructure works below HTTP: no ASP.NET Core, no API layer, no module code.
        yield return new("Infrastructure.Common", InfrastructureCommon,
            [AspNetCore, "CoachDB.Api", .. Modules.Select(m => $"CoachDB.{m}")]);

        foreach (var rule in ForModule("Identity",
                     domain: typeof(CoachDB.Identity.Domain.Users.User).Assembly,
                     application: typeof(CoachDB.Identity.Application.Auth.AuthResult).Assembly,
                     infrastructure: typeof(CoachDB.Identity.Infrastructure.IdentityModule).Assembly,
                     api: typeof(CoachDB.Identity.Api.IdentityEndpoints).Assembly,
                     contracts: typeof(CoachDB.Identity.Contracts.IUserDirectory).Assembly))
            yield return rule;

        foreach (var rule in ForModule("Tenants",
                     domain: typeof(CoachDB.Tenants.Domain.Tenants.Tenant).Assembly,
                     application: typeof(CoachDB.Tenants.Application.TenantsOptions).Assembly,
                     infrastructure: typeof(CoachDB.Tenants.Infrastructure.TenantsModule).Assembly,
                     api: typeof(CoachDB.Tenants.Api.TenantsEndpoints).Assembly,
                     contracts: typeof(CoachDB.Tenants.Contracts.ITenantDirectory).Assembly))
            yield return rule;
        // Next module — same shape; add its 5 project references to this csproj:
        // foreach (var rule in ForModule("Packages",
        //              domain: typeof(CoachDB.Packages.Domain.Package).Assembly,
        //              application: typeof(CoachDB.Packages.Application.AssemblyMarker).Assembly,
        //              infrastructure: typeof(CoachDB.Packages.Infrastructure.PackagesDbContext).Assembly,
        //              api: typeof(CoachDB.Packages.Api.AssemblyMarker).Assembly,
        //              contracts: typeof(CoachDB.Packages.Contracts.ISubscriptionAccess).Assembly))
        //     yield return rule;
    }
}

public sealed class DependencyRuleTests
{
    public static TheoryData<string> RuleNames() => [.. Layers.All().Select(r => r.Name)];

    [Theory]
    [MemberData(nameof(RuleNames))]
    public void Layer_does_not_depend_on_forbidden_namespaces(string ruleName)
    {
        var rule = Layers.All().Single(r => r.Name == ruleName);

        var result = Types.InAssembly(rule.Assembly)
            .ShouldNot()
            .HaveDependencyOnAny(rule.Forbidden)
            .GetResult();

        var offenders = result.FailingTypeNames ?? [];
        result.IsSuccessful.ShouldBeTrue(
            $"{rule.Name} has forbidden dependencies in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void Every_rule_has_a_unique_name()
    {
        var names = Layers.All().Select(r => r.Name).ToList();
        names.Distinct().Count().ShouldBe(names.Count);
    }

    [Fact]
    public void Rules_catch_a_real_violation()
    {
        // Sanity check that the helper is not vacuous: Api.Common does reference ASP.NET Core.
        var result = Types.InAssembly(Layers.ApiCommon)
            .ShouldNot()
            .HaveDependencyOnAny(Layers.AspNetCore)
            .GetResult();

        result.IsSuccessful.ShouldBeFalse();
    }
}
