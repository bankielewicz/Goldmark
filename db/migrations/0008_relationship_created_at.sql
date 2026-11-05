-- 0008 when each relationship was added (slice 6; US-11, FR-CM-6; DEC-046 item 13): screen 5's Relationships
-- tab shows it as "Since". The current row's ValidFrom moves whenever its redundancy group changes, and
-- cmdb.RelationshipHistory has only its period index, so reading the earliest version on every tab read would
-- scan the history; a column keeps the date. Slice 10's collectors fill it as "first seen".
--
-- Forward-only and immutable once merged (db/migrations.sha256, DEC-028). Every table is in schema
-- cmdb, so goldmark_runtime's schema-level DML grant (0002) covers it; nothing here grants more.

-- The backfill must not version the rows it fills: an UPDATE of a system-versioned table writes a history
-- row for every relationship, and with versioning off but the period kept SQL Server still moves each row's
-- ValidFrom (measured on SQL Server 2025, 2026-10-25), so slice 7's "as of" view would show every relationship
-- changed by its last writer at migration time. So versioning goes off and the period with it, and both come
-- back as they were, in this script's one transaction.
ALTER TABLE cmdb.Relationship SET (SYSTEM_VERSIONING = OFF);
ALTER TABLE cmdb.Relationship DROP PERIOD FOR SYSTEM_TIME;

-- Set on insert by the default; existing rows are filled below.
ALTER TABLE cmdb.Relationship ADD CreatedAt datetime2(7) NULL
    CONSTRAINT DF_Relationship_CreatedAt DEFAULT (SYSUTCDATETIME());
ALTER TABLE cmdb.RelationshipHistory ADD CreatedAt datetime2(7) NULL;
GO

-- Each relationship's earliest version, current or past; history rows get it too, so a past version (and a
-- deleted relationship, which lives only there) shows the same date.
WITH Earliest AS
(
    SELECT Id, MIN(ValidFrom) AS CreatedAt
    FROM (SELECT Id, ValidFrom FROM cmdb.Relationship UNION ALL SELECT Id, ValidFrom FROM cmdb.RelationshipHistory) AS v
    GROUP BY Id
)
UPDATE r SET CreatedAt = e.CreatedAt
FROM cmdb.Relationship AS r
JOIN Earliest AS e ON e.Id = r.Id;

WITH Earliest AS
(
    SELECT Id, MIN(ValidFrom) AS CreatedAt
    FROM (SELECT Id, ValidFrom FROM cmdb.Relationship UNION ALL SELECT Id, ValidFrom FROM cmdb.RelationshipHistory) AS v
    GROUP BY Id
)
UPDATE h SET CreatedAt = e.CreatedAt
FROM cmdb.RelationshipHistory AS h
JOIN Earliest AS e ON e.Id = h.Id;

ALTER TABLE cmdb.Relationship ADD PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo);
ALTER TABLE cmdb.Relationship SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = cmdb.RelationshipHistory, DATA_CONSISTENCY_CHECK = ON));
