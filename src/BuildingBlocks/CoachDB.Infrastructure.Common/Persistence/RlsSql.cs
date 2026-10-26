namespace CoachDB.Infrastructure.Common.Persistence;

/// <summary>
/// SQL for Row-Level Security, used from module migrations with <c>migrationBuilder.Sql(...)</c>. The API connects as
/// <c>app_user</c> (RLS applies); the migrator owns the tables and <c>app_system</c> has BYPASSRLS.
/// The settings are written by <see cref="TenantRlsConnectionInterceptor"/>; an empty value means "none".
/// </summary>
public static class RlsSql
{
    public const string CurrentTenant = "NULLIF(current_setting('app.tenant_id', true), '')::uuid";
    public const string CurrentUser = "NULLIF(current_setting('app.user_id', true), '')::uuid";

    /// <summary>Standard policy: a row is visible and writable only inside its own tenant.</summary>
    public static string EnableTenantIsolation(string schema, string table) =>
        Enable(schema, table, $"tenant_id = {CurrentTenant}", $"tenant_id = {CurrentTenant}");

    /// <summary>
    /// Like <see cref="EnableTenantIsolation"/>, and the user named in <paramref name="userColumn"/> may also read
    /// (never write) their own rows in other tenants.
    /// </summary>
    public static string EnableTenantIsolationWithOwnRows(string schema, string table, string userColumn = "user_id") =>
        Enable(schema, table,
            $"tenant_id = {CurrentTenant} OR {Identifier(userColumn)} = {CurrentUser}",
            $"tenant_id = {CurrentTenant}");

    public static string Disable(string schema, string table) =>
        $"""
        DROP POLICY IF EXISTS tenant_isolation ON {Identifier(schema)}.{Identifier(table)};
        ALTER TABLE {Identifier(schema)}.{Identifier(table)} DISABLE ROW LEVEL SECURITY;
        """;

    private static string Enable(string schema, string table, string usingExpression, string checkExpression) =>
        $"""
        ALTER TABLE {Identifier(schema)}.{Identifier(table)} ENABLE ROW LEVEL SECURITY;
        CREATE POLICY tenant_isolation ON {Identifier(schema)}.{Identifier(table)}
          USING ({usingExpression})
          WITH CHECK ({checkExpression});
        """;

    // Names come from code, not users, but are still validated because they cannot be SQL parameters.
    private static string Identifier(string name) =>
        name.Length > 0 && name.All(c => c is (>= 'a' and <= 'z') or (>= '0' and <= '9') or '_')
            ? name
            : throw new ArgumentException($"Invalid SQL identifier '{name}'.", nameof(name));
}
