-- 0004 audit retention (slice 4; US-07, NFR-LOG-3; DEC-041): how long the audit log keeps entries,
-- and the one way old entries are deleted.
--
-- Forward-only and immutable once merged (db/migrations.sha256, DEC-028). The runtime login still
-- holds no UPDATE or DELETE on cmdb.AuditLog (0002). It may only run cmdb.PurgeAuditLog, which
-- takes no parameters: what it deletes is decided by cmdb.AuditRetention, which only migrate (the
-- migration login) writes, from the setting Goldmark:Audit:RetentionDays. The runtime login can
-- therefore start a purge but never decide what it deletes (DEC-041 amends DEC-025).

-- One row: the retention period in days, 0 for "keep indefinitely". The floor of 365 days
-- (NFR-LOG-3) is checked here, by migrate before writing, and again inside the procedure. No row is
-- seeded: migrate writes the configured period right after the migrations and audits it, so the
-- log's first entry about retention is the period an operator chose. Until then the procedure
-- deletes nothing.
CREATE TABLE cmdb.AuditRetention
(
    Id            tinyint      NOT NULL CONSTRAINT PK_AuditRetention PRIMARY KEY
        CONSTRAINT CK_AuditRetention_Single CHECK (Id = 1),
    RetentionDays int          NOT NULL CONSTRAINT CK_AuditRetention_Days
        CHECK (RetentionDays = 0 OR RetentionDays BETWEEN 365 AND 36500),
    ChangedAt     datetime2(7) NOT NULL CONSTRAINT DF_AuditRetention_ChangedAt DEFAULT (SYSUTCDATETIME())
);

DENY INSERT, UPDATE, DELETE ON OBJECT::cmdb.AuditRetention TO goldmark_runtime;
GO

-- Deletes the audit entries older than the retention period, counted in whole days back from
-- today's midnight UTC, in batches so that the transaction log stays small on Express. A batch of
-- 4,000 stays under the 5,000 locks on one table or index at which SQL Server escalates to a table
-- lock, so sign-ins and other audit writes are not held up while a purge runs. Each batch
-- commits together with a System entry naming the cutoff and that batch's count, so no deletion
-- commits without its entry; a run that deletes nothing writes nothing. It manages its own
-- transactions and refuses to run inside a caller's (a joined transaction would turn each COMMIT
-- into a decrement). Owned by dbo like cmdb.AuditLog, so its DELETE runs through the ownership
-- chain; the runtime login's DENY DELETE on the table still refuses a DELETE of its own. Static SQL
-- only: dynamic SQL would break the chain.
CREATE PROCEDURE cmdb.PurgeAuditLog
AS
BEGIN
    SET NOCOUNT ON;
    SET XACT_ABORT ON;

    IF @@TRANCOUNT > 0
        THROW 50041, N'cmdb.PurgeAuditLog manages its own transactions: call it outside a transaction.', 1;

    DECLARE @days int = (SELECT RetentionDays FROM cmdb.AuditRetention WHERE Id = 1);
    IF @days IS NULL OR @days = 0
    BEGIN
        SELECT CAST(0 AS int) AS Deleted, CAST(NULL AS datetime2(0)) AS Before, ISNULL(@days, 0) AS RetentionDays;
        RETURN;
    END;

    -- The floor again, so a row changed some other way can never shorten retention.
    IF @days < 365
        SET @days = 365;

    DECLARE @before datetime2(0) = DATEADD(day, -@days, CAST(CAST(SYSUTCDATETIME() AS date) AS datetime2(0)));
    DECLARE @total int = 0, @batch int = 1;
    WHILE @batch > 0
    BEGIN
        BEGIN TRANSACTION;
        DELETE TOP (4000) FROM cmdb.AuditLog WHERE At < @before;
        SET @batch = @@ROWCOUNT;
        IF @batch > 0
            INSERT INTO cmdb.AuditLog (ActorType, Action, Details)
            VALUES ('System', 'audit.retention.purged',
                JSON_OBJECT('before': CONVERT(varchar(19), @before, 126) + 'Z', 'deleted': @batch, 'retentionDays': @days));
        COMMIT TRANSACTION;
        SET @total += @batch;
    END;

    SELECT @total AS Deleted, @before AS Before, @days AS RetentionDays;
END;
GO

GRANT EXECUTE ON OBJECT::cmdb.PurgeAuditLog TO goldmark_runtime;
