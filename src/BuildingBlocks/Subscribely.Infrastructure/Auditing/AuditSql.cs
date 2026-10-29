namespace Subscribely.Infrastructure.Auditing;

/// <summary>SQL de imutabilidade da trilha (usado nas migrations do Catálogo e do banco do tenant).</summary>
public static class AuditSql
{
    public static string AppendOnly(string schema, string table) => $"""
        CREATE OR REPLACE FUNCTION {schema}.audit_append_only() RETURNS trigger LANGUAGE plpgsql AS $fn$
        BEGIN
            RAISE EXCEPTION 'audit entries are append-only';
        END
        $fn$;
        CREATE TRIGGER trg_{table}_append_only BEFORE UPDATE OR DELETE ON {schema}.{table}
            FOR EACH ROW EXECUTE FUNCTION {schema}.audit_append_only();
        CREATE TRIGGER trg_{table}_no_truncate BEFORE TRUNCATE ON {schema}.{table}
            FOR EACH STATEMENT EXECUTE FUNCTION {schema}.audit_append_only();
        DO $do$
        BEGIN
            IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'subscribely_app') THEN
                REVOKE UPDATE, DELETE, TRUNCATE ON {schema}.{table} FROM subscribely_app;
            END IF;
        END
        $do$;
        """;

    public static string DropAppendOnly(string schema, string table) => $"""
        DROP TRIGGER IF EXISTS trg_{table}_append_only ON {schema}.{table};
        DROP TRIGGER IF EXISTS trg_{table}_no_truncate ON {schema}.{table};
        DROP FUNCTION IF EXISTS {schema}.audit_append_only();
        """;
}
