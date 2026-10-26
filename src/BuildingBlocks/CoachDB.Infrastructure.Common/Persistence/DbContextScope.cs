namespace CoachDB.Infrastructure.Common.Persistence;

/// <summary>
/// Scoped registry of the module DbContexts actually created in this request, so the unit of work saves
/// only those, without resolving (and constructing) every module's context.
/// </summary>
public sealed class DbContextScope
{
    private readonly List<ModuleDbContext> _contexts = [];

    public IReadOnlyList<ModuleDbContext> Contexts => _contexts;

    internal void Track(ModuleDbContext context) => _contexts.Add(context);
}
