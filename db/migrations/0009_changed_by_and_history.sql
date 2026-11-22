-- 0009 who changed a CI or a relationship comes from the session, and history can be read by CI
-- (slice 7; US-42, US-43, FR-CM-5; DEC-047).
--
-- Forward-only and immutable once merged (db/migrations.sha256, DEC-028). Every table is in schema
-- cmdb, so goldmark_runtime's schema-level DML grant (0002) covers it; nothing here grants more.
-- Nothing here changes a row: no version's ValidFrom moves and no history row is written.

-- FR-CM-5: "The acting principal (user or source) is captured via SESSION_CONTEXT into a ChangedBy column
-- on every write." The app sets the key once per connection (Data/DbSession.cs, read-only for the rest of
-- that connection's use; a pooled connection is reset before its next use), and its statements write
-- ChangedBy as DEFAULT, on INSERT and on UPDATE alike. ChangedBy is NOT NULL on both tables, so a write
-- on a connection that names nobody fails instead of recording nobody. A statement may still give
-- ChangedBy itself (a migration, a test's fixture): the default is not a check.
ALTER TABLE cmdb.Ci ADD CONSTRAINT DF_Ci_ChangedBy
    DEFAULT (CONVERT(nvarchar(256), SESSION_CONTEXT(N'Goldmark.ChangedBy'))) FOR ChangedBy;
ALTER TABLE cmdb.Relationship ADD CONSTRAINT DF_Relationship_ChangedBy
    DEFAULT (CONVERT(nvarchar(256), SESSION_CONTEXT(N'Goldmark.ChangedBy'))) FOR ChangedBy;
-- cmdb.AttributeDef and cmdb.RelationshipType get no default: there a null ChangedBy means "never changed
-- since it was added" (0007), which a default would end. The app's statements name the session value.
GO

-- A CI's history (US-43) reads every version of one CI and every version of the relationships at either
-- end of it. The history tables had only their period index (ValidTo, ValidFrom), so each of those reads
-- scanned the whole table.
CREATE INDEX IX_CiHistory_Id ON cmdb.CiHistory (Id, ValidFrom);
CREATE INDEX IX_RelationshipHistory_FromCi ON cmdb.RelationshipHistory (FromCiId, ValidFrom);
CREATE INDEX IX_RelationshipHistory_ToCi ON cmdb.RelationshipHistory (ToCiId, ValidFrom);

-- Who ended a relationship is the actor of its "relationship.deleted" audit entry (DEC-046 item 1), which
-- a CI's history looks up by the relationship's ID. The audit log had only its index on At.
CREATE INDEX IX_AuditLog_Target ON cmdb.AuditLog (TargetType, TargetId)
    INCLUDE (Action, At, ActorType, ActorId);
