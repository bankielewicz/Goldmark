-- 0007 the Types & attributes screen (slice 5b; US-10, FR-CM-1, FR-IM-1; DEC-045): a hidden flag for
-- custom CI types, descriptions for relationship types, who changed a definition last, and history for
-- the definitions (item 8), so a CI's past values keep their meaning after an attribute is deleted or
-- relabelled, a choice removed or a relationship type's impact changed.
--
-- Forward-only and immutable once merged (db/migrations.sha256, DEC-028). Every table is in schema
-- cmdb, so goldmark_runtime's schema-level DML grant (0002) covers it; nothing here grants more. The
-- runtime login can't switch versioning off or touch the history tables, as for cmdb.CiHistory.

-- A hidden custom type (DEC-045 item 5) leaves the New CI type picker and Search's type filter; its CIs
-- keep working, and no new CI of it can be created. The groups are PRD §9.3's; a custom type made
-- through the API before slice 5b may have none.
ALTER TABLE cmdb.CiType ADD IsHidden bit NOT NULL CONSTRAINT DF_CiType_IsHidden DEFAULT (0);
ALTER TABLE cmdb.CiType ADD CONSTRAINT CK_CiType_Category CHECK (Category IS NULL OR Category IN (N'Location & ownership',
    N'Compute', N'Network', N'Storage', N'Clustering & SQL', N'Business'));

-- The person who changed a custom attribute or relationship type last (an account ID, as Ci.ChangedBy);
-- null until one is changed.
ALTER TABLE cmdb.AttributeDef ADD ChangedBy nvarchar(256) NULL;
ALTER TABLE cmdb.RelationshipType ADD Description nvarchar(400) NULL, ChangedBy nvarchar(256) NULL;
GO

-- What each built-in relationship type means (PRD §9.4), as screen 14 lists them.
UPDATE r SET Description = d.Description
FROM cmdb.RelationshipType AS r
JOIN (VALUES
    (N'runs_on', N'A VM runs on a hypervisor host.'),
    (N'member_of_cluster', N'A host is a member of a cluster. Carries no impact.'),
    (N'os_of', N'The operating system running in a VM.'),
    (N'uses_datastore', N'A VM keeps its disks on a datastore.'),
    (N'backed_by', N'A datastore is backed by an FC storage group (found automatically).'),
    (N'part_of_array', N'A storage group belongs to a storage array (mapped by hand).'),
    (N'virtualizes', N'A virtualizing array (SVC) presents a back-end array’s storage.'),
    (N'housed_in', N'A blade host sits in a chassis.'),
    (N'uplinks_to', N'A host’s uplink to a switch. Grouped by virtual switch: the host loses the network only if all its uplinks are down.'),
    (N'hosted_on', N'A SQL Server instance runs on a server.'),
    (N'database_of', N'A database outside an availability group, on its instance.'),
    (N'replica_on', N'An availability group has a replica on this SQL instance. Grouped by availability group: the group is down only if all its replicas are down.'),
    (N'in_ag', N'A database in an availability group.'),
    (N'listener_for', N'An availability group’s listener name.'),
    (N'cluster_node_of', N'A Windows cluster’s node. Grouped by cluster: the cluster is down only if all its nodes are down.'),
    (N'depends_on', N'A general dependency, set by hand.'),
    (N'owned_by', N'Who owns the CI: a team or a person. Carries no impact.')
) AS d (Name, Description) ON d.Name = r.Name
WHERE r.IsBuiltIn = 1;
GO

-- History for the definitions (DEC-045 item 8), as for CIs and relationships. The rows that exist now
-- start their period at 1900-01-01: they held before any CI did, so a point-in-time read (US-42) finds
-- them for every CI version. A version's ValidFrom is its ETag (as a CI's is).
ALTER TABLE cmdb.CiType ADD
    ValidFrom datetime2(7) GENERATED ALWAYS AS ROW START NOT NULL CONSTRAINT DF_CiType_ValidFrom DEFAULT ('19000101'),
    ValidTo datetime2(7) GENERATED ALWAYS AS ROW END NOT NULL CONSTRAINT DF_CiType_ValidTo DEFAULT ('99991231 23:59:59.9999999'),
    PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo);

ALTER TABLE cmdb.AttributeDef ADD
    ValidFrom datetime2(7) GENERATED ALWAYS AS ROW START NOT NULL CONSTRAINT DF_AttributeDef_ValidFrom DEFAULT ('19000101'),
    ValidTo datetime2(7) GENERATED ALWAYS AS ROW END NOT NULL CONSTRAINT DF_AttributeDef_ValidTo DEFAULT ('99991231 23:59:59.9999999'),
    PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo);

ALTER TABLE cmdb.RelationshipType ADD
    ValidFrom datetime2(7) GENERATED ALWAYS AS ROW START NOT NULL CONSTRAINT DF_RelationshipType_ValidFrom DEFAULT ('19000101'),
    ValidTo datetime2(7) GENERATED ALWAYS AS ROW END NOT NULL CONSTRAINT DF_RelationshipType_ValidTo DEFAULT ('99991231 23:59:59.9999999'),
    PERIOD FOR SYSTEM_TIME (ValidFrom, ValidTo);
GO

ALTER TABLE cmdb.CiType SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = cmdb.CiTypeHistory, DATA_CONSISTENCY_CHECK = ON));
ALTER TABLE cmdb.AttributeDef SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = cmdb.AttributeDefHistory, DATA_CONSISTENCY_CHECK = ON));
ALTER TABLE cmdb.RelationshipType SET (SYSTEM_VERSIONING = ON (HISTORY_TABLE = cmdb.RelationshipTypeHistory, DATA_CONSISTENCY_CHECK = ON));
