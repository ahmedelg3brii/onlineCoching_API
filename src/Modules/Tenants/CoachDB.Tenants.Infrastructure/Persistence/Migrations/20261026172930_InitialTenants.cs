using System;
using CoachDB.Infrastructure.Common.Persistence;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CoachDB.Tenants.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialTenants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "tenants");

            migrationBuilder.CreateTable(
                name: "inbox_messages",
                schema: "tenants",
                columns: table => new
                {
                    message_id = table.Column<Guid>(type: "uuid", nullable: false),
                    handler = table.Column<string>(type: "text", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inbox_messages", x => new { x.message_id, x.handler });
                });

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                schema: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    type = table.Column<string>(type: "text", nullable: false),
                    content = table.Column<string>(type: "jsonb", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "platform_plans",
                schema: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    max_clients = table.Column<int>(type: "integer", nullable: true),
                    max_coaches = table.Column<int>(type: "integer", nullable: true),
                    features = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    monthly_price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "char(3)", nullable: false, defaultValueSql: "'USD'")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_plans", x => x.id);
                    table.CheckConstraint("ck_platform_plans_limits", "(max_clients IS NULL OR max_clients > 0) AND (max_coaches IS NULL OR max_coaches > 0)");
                    table.CheckConstraint("ck_platform_plans_monthly_price", "monthly_price >= 0");
                });

            migrationBuilder.CreateTable(
                name: "tenants",
                schema: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "varchar(255)", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'trial'"),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: true),
                    default_currency = table.Column<string>(type: "char(3)", nullable: false, defaultValueSql: "'EGP'"),
                    timezone = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'Africa/Cairo'"),
                    default_locale = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'ar'"),
                    branding = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    settings = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    trial_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenants", x => x.id);
                    table.CheckConstraint("ck_tenants_default_currency", "default_currency ~ '^[A-Z]{3}$'");
                    table.CheckConstraint("ck_tenants_slug", "slug ~ '^[a-z0-9]([a-z0-9-]{1,61}[a-z0-9])$' AND slug NOT LIKE '%--%'");
                    table.CheckConstraint("ck_tenants_status", "status IN ('trial', 'active', 'past_due', 'suspended', 'cancelled')");
                    table.ForeignKey(
                        name: "fk_tenants_platform_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "tenants",
                        principalTable: "platform_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "tenant_members",
                schema: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'active'"),
                    bio = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_members", x => x.id);
                    table.CheckConstraint("ck_tenant_members_role", "role IN ('owner', 'coach', 'assistant')");
                    table.CheckConstraint("ck_tenant_members_status", "status IN ('active', 'suspended')");
                    table.ForeignKey(
                        name: "fk_tenant_members_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenants",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_payment_accounts",
                schema: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: false),
                    provider_account_id = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValueSql: "'pending'"),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_payment_accounts", x => x.id);
                    table.CheckConstraint("ck_tenant_payment_accounts_provider", "provider IN ('paymob', 'fawry', 'stripe')");
                    table.CheckConstraint("ck_tenant_payment_accounts_status", "status IN ('pending', 'active', 'disabled')");
                    table.ForeignKey(
                        name: "fk_tenant_payment_accounts_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenants",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "tenant_plan_subscriptions",
                schema: "tenants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    current_period_start = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    current_period_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    provider = table.Column<string>(type: "text", nullable: true),
                    provider_subscription_id = table.Column<string>(type: "text", nullable: true),
                    cancel_at_period_end = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tenant_plan_subscriptions", x => x.id);
                    table.CheckConstraint("ck_tenant_plan_subscriptions_period", "current_period_end > current_period_start");
                    table.CheckConstraint("ck_tenant_plan_subscriptions_provider", "provider IS NULL OR provider IN ('paymob', 'fawry', 'stripe')");
                    table.CheckConstraint("ck_tenant_plan_subscriptions_status", "status IN ('trialing', 'active', 'past_due', 'cancelled', 'expired')");
                    table.ForeignKey(
                        name: "fk_tenant_plan_subscriptions_platform_plans_plan_id",
                        column: x => x.plan_id,
                        principalSchema: "tenants",
                        principalTable: "platform_plans",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_tenant_plan_subscriptions_tenants_tenant_id",
                        column: x => x.tenant_id,
                        principalSchema: "tenants",
                        principalTable: "tenants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_occurred_at",
                schema: "tenants",
                table: "outbox_messages",
                column: "occurred_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_platform_plans_code",
                schema: "tenants",
                table: "platform_plans",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_members_tenant_id_user_id",
                schema: "tenants",
                table: "tenant_members",
                columns: new[] { "tenant_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_members_user_id",
                schema: "tenants",
                table: "tenant_members",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_payment_accounts_tenant_id_provider",
                schema: "tenants",
                table: "tenant_payment_accounts",
                columns: new[] { "tenant_id", "provider" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_tenant_plan_subscriptions_current",
                schema: "tenants",
                table: "tenant_plan_subscriptions",
                column: "tenant_id",
                unique: true,
                filter: "status IN ('trialing', 'active', 'past_due')");

            migrationBuilder.CreateIndex(
                name: "ix_tenant_plan_subscriptions_plan_id",
                schema: "tenants",
                table: "tenant_plan_subscriptions",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_created_at",
                schema: "tenants",
                table: "tenants",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_plan_id",
                schema: "tenants",
                table: "tenants",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_tenants_slug",
                schema: "tenants",
                table: "tenants",
                column: "slug",
                unique: true);

            // ---- Hand-written: what the EF model can't express ----

            // Cross-module FK (Identity is migrated first).
            migrationBuilder.Sql("""
                ALTER TABLE tenants.tenant_members
                  ADD CONSTRAINT fk_tenant_members_users_user_id
                  FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE CASCADE;
                """);

            // At most one default payout account per tenant, checked at commit so the default can move in one save.
            migrationBuilder.Sql("""
                ALTER TABLE tenants.tenant_payment_accounts
                  ADD CONSTRAINT ex_tenant_payment_accounts_one_default
                  EXCLUDE USING btree (tenant_id WITH =) WHERE (is_default)
                  DEFERRABLE INITIALLY DEFERRED;
                """);

            // Row-Level Security. tenants and platform_plans are global (looked up by slug before a tenant is chosen).
            // A user may read their own memberships in every tenant, for "my businesses" and tenant resolution.
            migrationBuilder.Sql(RlsSql.EnableTenantIsolationWithOwnRows("tenants", "tenant_members"));
            migrationBuilder.Sql(RlsSql.EnableTenantIsolation("tenants", "tenant_plan_subscriptions"));
            migrationBuilder.Sql(RlsSql.EnableTenantIsolation("tenants", "tenant_payment_accounts"));

            // Starting SaaS plans (prices are placeholders; change them with PUT /admin/plans/{id}).
            // "starter" is the trial plan (Tenants:TrialPlanCode).
            migrationBuilder.Sql("""
                INSERT INTO tenants.platform_plans (id, code, name, monthly_price, currency, max_clients, max_coaches, features)
                VALUES
                  (gen_random_uuid(), 'starter', 'Starter', 19.00, 'USD', 25, 1,
                   '{"nutrition": true, "training": false, "chat": true, "custom_branding": false, "custom_domain": false}'),
                  (gen_random_uuid(), 'pro', 'Pro', 49.00, 'USD', 100, 3,
                   '{"nutrition": true, "training": true, "chat": true, "custom_branding": true, "custom_domain": false}'),
                  (gen_random_uuid(), 'studio', 'Studio', 129.00, 'USD', NULL, 10,
                   '{"nutrition": true, "training": true, "chat": true, "custom_branding": true, "custom_domain": true}')
                ON CONFLICT (code) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Policies, the exclusion constraint and the cross-module FK go with their tables.
            migrationBuilder.DropTable(
                name: "inbox_messages",
                schema: "tenants");

            migrationBuilder.DropTable(
                name: "outbox_messages",
                schema: "tenants");

            migrationBuilder.DropTable(
                name: "tenant_members",
                schema: "tenants");

            migrationBuilder.DropTable(
                name: "tenant_payment_accounts",
                schema: "tenants");

            migrationBuilder.DropTable(
                name: "tenant_plan_subscriptions",
                schema: "tenants");

            migrationBuilder.DropTable(
                name: "tenants",
                schema: "tenants");

            migrationBuilder.DropTable(
                name: "platform_plans",
                schema: "tenants");
        }
    }
}
