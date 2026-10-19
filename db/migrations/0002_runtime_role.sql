-- 0002 runtime role (slice 2, NFR-SEC-4, DEC-025). The runtime login is a member
-- of this role (added by the migrate command) and holds nothing else:
--   - DML on the application schema; no DDL, so no TRUNCATE, no ALTER and no
--     switching off system versioning on the temporal tables;
--   - no UPDATE or DELETE on the audit log (append-only, US-07);
--   - read access to the migration journal, for the readiness schema check.
--
-- Forward-only and immutable once merged (db/migrations.sha256).

CREATE ROLE goldmark_runtime AUTHORIZATION dbo;
GO

GRANT SELECT, INSERT, UPDATE, DELETE ON SCHEMA::cmdb TO goldmark_runtime;
DENY UPDATE, DELETE ON OBJECT::cmdb.AuditLog TO goldmark_runtime;
GRANT SELECT ON OBJECT::dbo.SchemaVersions TO goldmark_runtime;
