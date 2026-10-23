-- 0006 CI types and their attributes (slice 5; US-10, US-11; DEC-044): attribute definitions per CI
-- type, the built-in CI types of PRD §9.3 with their groups, the attributes the PRD names (DEC-044
-- item 3; each collector slice adds its own), and the built-in relationship types of §9.4.
--
-- Forward-only and immutable once merged (db/migrations.sha256, DEC-028). Every table is in schema
-- cmdb, so goldmark_runtime's schema-level DML grant (0002) covers it; nothing here grants more.

-- The PRD §9.3 group a type is listed under (the search page's CI type filter); custom types (slice
-- 5b) may have none.
ALTER TABLE cmdb.CiType ADD Category nvarchar(50) NULL;
GO

-- An attribute of a CI type: its value lives in Ci.Attributes under Name. DataType is one of US-10's
-- kinds; Enum lists its choices in EnumValues (a JSON array of strings), CiRef may name the type the
-- linked CI must have. Admins add custom ones through the API (DEC-044 items 4 and 8: add only in slice
-- 5). IsPromoted (FR-CM-2) and the source policy (FR-RC-6) come with the slices that use them.
CREATE TABLE cmdb.AttributeDef
(
    Id          int           NOT NULL IDENTITY(1, 1) CONSTRAINT PK_AttributeDef PRIMARY KEY,
    CiTypeId    int           NOT NULL CONSTRAINT FK_AttributeDef_CiType REFERENCES cmdb.CiType (Id),
    Name        varchar(64)   NOT NULL,
    Label       nvarchar(100) NOT NULL,
    DataType    varchar(10)   NOT NULL
        CONSTRAINT CK_AttributeDef_DataType CHECK (DataType IN ('String', 'Number', 'Bool', 'Date', 'Enum', 'CiRef')),
    IsRequired  bit           NOT NULL CONSTRAINT DF_AttributeDef_IsRequired DEFAULT (0),
    EnumValues  json          NULL,
    RefCiTypeId int           NULL CONSTRAINT FK_AttributeDef_RefCiType REFERENCES cmdb.CiType (Id),
    IsBuiltIn   bit           NOT NULL CONSTRAINT DF_AttributeDef_IsBuiltIn DEFAULT (0),
    SortOrder   int           NOT NULL,
    CreatedAt   datetime2(7)  NOT NULL CONSTRAINT DF_AttributeDef_CreatedAt DEFAULT (SYSUTCDATETIME()),
    CreatedBy   nvarchar(256) NULL,
    CONSTRAINT UQ_AttributeDef_Name UNIQUE (CiTypeId, Name),
    CONSTRAINT UQ_AttributeDef_Label UNIQUE (CiTypeId, Label),
    CONSTRAINT CK_AttributeDef_EnumValues CHECK ((DataType = 'Enum' AND EnumValues IS NOT NULL) OR (DataType <> 'Enum' AND EnumValues IS NULL)),
    CONSTRAINT CK_AttributeDef_RefCiType CHECK (RefCiTypeId IS NULL OR DataType = 'CiRef')
);
GO

-- The built-in CI types (PRD §9.3), in its order. Icon is a key the web app maps to an icon.
INSERT INTO cmdb.CiType (Name, IsBuiltIn, Icon, Category)
VALUES
    (N'Location', 1, 'location', N'Location & ownership'),
    (N'Team', 1, 'team', N'Location & ownership'),
    (N'Person', 1, 'person', N'Location & ownership'),
    (N'HypervisorManager', 1, 'hypervisor-manager', N'Compute'),
    (N'HypervisorCluster', 1, 'hypervisor-cluster', N'Compute'),
    (N'HypervisorHost', 1, 'hypervisor-host', N'Compute'),
    (N'Chassis', 1, 'chassis', N'Compute'),
    (N'VirtualMachine', 1, 'virtual-machine', N'Compute'),
    (N'Server', 1, 'server', N'Compute'),
    (N'Appliance', 1, 'appliance', N'Compute'),
    (N'NetworkDevice', 1, 'network-device', N'Network'),
    (N'Datastore', 1, 'datastore', N'Storage'),
    (N'StorageGroup', 1, 'storage-group', N'Storage'),
    (N'StorageArray', 1, 'storage-array', N'Storage'),
    (N'WindowsCluster', 1, 'windows-cluster', N'Clustering & SQL'),
    (N'ClusterNetworkName', 1, 'cluster-network-name', N'Clustering & SQL'),
    (N'SqlInstance', 1, 'sql-instance', N'Clustering & SQL'),
    (N'SqlAvailabilityGroup', 1, 'sql-availability-group', N'Clustering & SQL'),
    (N'SqlDatabase', 1, 'sql-database', N'Clustering & SQL'),
    (N'BusinessService', 1, 'business-service', N'Business');

-- Only the attributes PRD §9.3 names (DEC-044 item 3), none required: a manually entered CI may not
-- know them, and collectors fill them later.
INSERT INTO cmdb.AttributeDef (CiTypeId, Name, Label, DataType, EnumValues, IsBuiltIn, SortOrder)
SELECT t.Id, a.Name, a.Label, a.DataType, a.EnumValues, 1, a.SortOrder
FROM (VALUES
    (N'HypervisorHost', 'virtual', N'Virtual', 'Bool', NULL, 1),
    (N'Server', 'platform', N'Platform', 'Enum', N'["physical","vmware","hyperv","xen","cloud","other"]', 1),
    (N'Appliance', 'kind', N'Kind', 'Enum', N'["hcx-mobility-agent","ise","nas-head","other"]', 1),
    (N'NetworkDevice', 'role', N'Role', 'Enum', N'["switch","router","firewall","load-balancer","fabric-interconnect","chassis-iom","fc-switch"]', 1),
    (N'StorageArray', 'kind', N'Kind', 'Enum', N'["block","nas"]', 1),
    (N'StorageArray', 'family', N'Family', 'String', NULL, 2)
) AS a (TypeName, Name, Label, DataType, EnumValues, SortOrder)
JOIN cmdb.CiType AS t ON t.Name = a.TypeName;

-- The built-in relationship types (PRD §9.4), dependent → dependency. A null end means any type:
-- depends_on (BusinessService or any → any), and owned_by, whose dependency is a Team or a Person,
-- which one column can't say; slice 6 (relationships) checks the ends when it creates them.
INSERT INTO cmdb.RelationshipType (Name, FromCiTypeId, ToCiTypeId, ImpactPropagation, IsBuiltIn)
SELECT r.Name, f.Id, t.Id, r.ImpactPropagation, 1
FROM (VALUES
    (N'runs_on', N'VirtualMachine', N'HypervisorHost', 'Down'),
    (N'member_of_cluster', N'HypervisorHost', N'HypervisorCluster', 'None'),
    (N'os_of', N'Server', N'VirtualMachine', 'Down'),
    (N'uses_datastore', N'VirtualMachine', N'Datastore', 'Down'),
    (N'backed_by', N'Datastore', N'StorageGroup', 'Down'),
    (N'part_of_array', N'StorageGroup', N'StorageArray', 'Down'),
    (N'virtualizes', N'StorageArray', N'StorageArray', 'Down'),
    (N'housed_in', N'HypervisorHost', N'Chassis', 'Down'),
    (N'uplinks_to', N'HypervisorHost', N'NetworkDevice', 'Down'),
    (N'hosted_on', N'SqlInstance', N'Server', 'Down'),
    (N'database_of', N'SqlDatabase', N'SqlInstance', 'Down'),
    (N'replica_on', N'SqlAvailabilityGroup', N'SqlInstance', 'Down'),
    (N'in_ag', N'SqlDatabase', N'SqlAvailabilityGroup', 'Down'),
    (N'listener_for', N'ClusterNetworkName', N'SqlAvailabilityGroup', 'Down'),
    (N'cluster_node_of', N'WindowsCluster', N'Server', 'Down'),
    (N'depends_on', NULL, NULL, 'Down'),
    (N'owned_by', NULL, NULL, 'None')
) AS r (Name, FromType, ToType, ImpactPropagation)
LEFT JOIN cmdb.CiType AS f ON f.Name = r.FromType
LEFT JOIN cmdb.CiType AS t ON t.Name = r.ToType;
