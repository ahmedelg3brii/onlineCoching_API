-- CoachDB database roles (runs once, on first start of the local Postgres container).
-- ⚠ DEV-ONLY passwords. Real environments create these roles with vault-managed secrets.
--
-- app_migrator : owns the schemas, runs EF migrations in CI/CD (bypasses RLS as owner)
-- app_user     : used by API requests; Row-Level Security always applies
-- app_system   : cross-tenant jobs (webhooks, renewals, platform billing); bypasses RLS
-- app_readonly : reports on the read replica; RLS applies

CREATE ROLE app_migrator LOGIN PASSWORD 'app_migrator_dev' CREATEROLE;
CREATE ROLE app_user     LOGIN PASSWORD 'app_user_dev'     NOBYPASSRLS;
CREATE ROLE app_system   LOGIN PASSWORD 'app_system_dev'   BYPASSRLS;
CREATE ROLE app_readonly LOGIN PASSWORD 'app_readonly_dev' NOBYPASSRLS;

ALTER DATABASE coachdb OWNER TO app_migrator;

GRANT CONNECT ON DATABASE coachdb TO app_user, app_system, app_readonly;
