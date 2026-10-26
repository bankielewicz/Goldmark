namespace Goldmark.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The synthetic estate of the performance suite (NFR-PERF-1, NFR-PERF-5; DEC-044 item 7): 25,000 CIs
/// in the reference environment's proportions (PRD §1.4: VMs and their servers first, then databases,
/// people, datastores and network devices), names in the patterns collectors produce, one in twenty
/// retired, written in one statement as the administrator. Slice 6 adds relationships
/// (<see cref="SeedRelationshipsAsync"/>); later slices add history.
/// </summary>
public static class SyntheticInventory
{
    /// <summary>Type, how many, and the name's prefix (a number follows).</summary>
    public static readonly IReadOnlyList<(string Type, int Count, string Prefix)> Shape =
    [
        ("VirtualMachine", 10_000, "app-"),
        ("Server", 7_600, "APP-"),
        ("SqlDatabase", 2_400, "DB_"),
        ("Person", 2_000, "person "),
        ("Datastore", 900, "DS-"),
        ("NetworkDevice", 600, "sw-"),
        ("HypervisorHost", 360, "esx-"),
        ("BusinessService", 300, "service "),
        ("Appliance", 200, "appliance-"),
        ("Team", 120, "team "),
        ("ClusterNetworkName", 100, "cnn-"),
        ("Chassis", 100, "chassis-"),
        ("SqlInstance", 70, "SQL"),
        ("StorageGroup", 60, "SG-"),
        ("WindowsCluster", 50, "wcl-"),
        ("HypervisorCluster", 48, "CL-"),
        ("SqlAvailabilityGroup", 35, "AG-"),
        ("Location", 27, "DC-"),
        ("StorageArray", 20, "array-"),
        ("HypervisorManager", 10, "vc"),
    ];

    public static int Count => Shape.Sum(s => s.Count);

    /// <summary>Writes the estate; VMs and servers are named after one of five applications ("app-erp-01234").</summary>
    public static Task SeedAsync(SqlServerFixture sql, Installation installation, Guid changedBy)
    {
        var shape = string.Join(",\n", Shape.Select(s => $"(N'{s.Type}', {s.Count}, N'{s.Prefix}')"));
        return sql.AsAdminAsync(installation.Database, $"""
            INSERT INTO cmdb.Ci (Id, TenantId, CiTypeId, Name, LifecycleState, Attributes, ChangedBy)
            SELECT NEWID(), '00000000-0000-0000-0000-000000000001', t.Id,
                CASE WHEN s.Type IN (N'VirtualMachine', N'Server')
                    THEN CONCAT(s.Prefix, CHOOSE(n.value % 5 + 1, N'erp', N'crm', N'web', N'sql', N'rpt'), N'-', RIGHT(CONCAT(N'0000', n.value), 5))
                    ELSE CONCAT(s.Prefix, RIGHT(CONCAT(N'0000', n.value), 5)) END,
                CASE WHEN n.value % 20 = 0 THEN 'Retired' ELSE 'Active' END,
                CASE WHEN s.Type = N'Server' THEN JSON_OBJECT('platform': CHOOSE(n.value % 3 + 1, 'vmware', 'physical', 'hyperv')) ELSE N'{"{}"}' END,
                N'{changedBy:D}'
            FROM (VALUES {shape}) AS s (Type, Count, Prefix)
            JOIN cmdb.CiType AS t ON t.Name = s.Type
            CROSS APPLY GENERATE_SERIES(1, s.Count) AS n;
            """);
    }

    /// <summary>
    /// The estate's relationships (slice 6; DEC-046's graph read measured on them), in §1.4's shape: every VM runs on a
    /// host (about 28 a host) and keeps its disks on a datastore (about 11 a datastore), hosts sit in clusters (about
    /// 8 a cluster), the first 200 in chassis, each uplinks to two switches (one redundancy group); datastores are
    /// backed by storage groups, storage groups belong to arrays; 7,600 VMs have their OS's server; 70 servers host the
    /// SQL instances, which hold the databases (about 34 each); each availability group has two replicas (a redundancy
    /// group); each business service depends on five VMs; every VM and server is owned by one of five teams (about
    /// 3,500 each: the hub the graph's limit was chosen for) and each service by one of the others; Windows clusters
    /// have two nodes, the first 35 cluster names listen for the AGs. About 52,000 relationships, written in one
    /// statement. A CI is found by its type and its name's number (every name ends in five digits).
    /// </summary>
    public static Task SeedRelationshipsAsync(SqlServerFixture sql, Installation installation, Guid changedBy)
    {
        int Count(string type) => Shape.Single(s => s.Type == type).Count;
        var (vms, hosts, clusters, datastores) = (Count("VirtualMachine"), Count("HypervisorHost"), Count("HypervisorCluster"), Count("Datastore"));
        var (groups, arrays, switches, chassis) = (Count("StorageGroup"), Count("StorageArray"), Count("NetworkDevice"), Count("Chassis"));
        var (servers, instances, databases, ags) = (Count("Server"), Count("SqlInstance"), Count("SqlDatabase"), Count("SqlAvailabilityGroup"));
        var (services, teams, windowsClusters) = (Count("BusinessService"), Count("Team"), Count("WindowsCluster"));
        return sql.AsAdminAsync(installation.Database, $$"""
            SELECT t.Name AS Type, CAST(RIGHT(c.Name, 5) AS int) AS N, c.Id INTO #c
            FROM cmdb.Ci AS c JOIN cmdb.CiType AS t ON t.Id = c.CiTypeId;
            CREATE UNIQUE CLUSTERED INDEX IX_c ON #c (Type, N);
            WITH r (FromType, FromN, RelType, ToType, ToN, Grp) AS
            (
                SELECT N'VirtualMachine', value, N'runs_on', N'HypervisorHost', (value - 1) % {{hosts}} + 1, NULL FROM GENERATE_SERIES(1, {{vms}})
                UNION ALL SELECT N'VirtualMachine', value, N'uses_datastore', N'Datastore', (value - 1) % {{datastores}} + 1, NULL FROM GENERATE_SERIES(1, {{vms}})
                UNION ALL SELECT N'HypervisorHost', value, N'member_of_cluster', N'HypervisorCluster', (value - 1) % {{clusters}} + 1, NULL FROM GENERATE_SERIES(1, {{hosts}})
                UNION ALL SELECT N'HypervisorHost', value, N'housed_in', N'Chassis', (value - 1) % {{chassis}} + 1, NULL FROM GENERATE_SERIES(1, 200)
                UNION ALL SELECT N'HypervisorHost', value, N'uplinks_to', N'NetworkDevice', (2 * value - 2) % {{switches}} + 1, N'vSwitch0' FROM GENERATE_SERIES(1, {{hosts}})
                UNION ALL SELECT N'HypervisorHost', value, N'uplinks_to', N'NetworkDevice', (2 * value - 1) % {{switches}} + 1, N'vSwitch0' FROM GENERATE_SERIES(1, {{hosts}})
                UNION ALL SELECT N'Datastore', value, N'backed_by', N'StorageGroup', (value - 1) % {{groups}} + 1, NULL FROM GENERATE_SERIES(1, {{datastores}})
                UNION ALL SELECT N'StorageGroup', value, N'part_of_array', N'StorageArray', (value - 1) % {{arrays}} + 1, NULL FROM GENERATE_SERIES(1, {{groups}})
                UNION ALL SELECT N'Server', value, N'os_of', N'VirtualMachine', value, NULL FROM GENERATE_SERIES(1, {{servers}})
                UNION ALL SELECT N'SqlInstance', value, N'hosted_on', N'Server', value, NULL FROM GENERATE_SERIES(1, {{instances}})
                UNION ALL SELECT N'SqlDatabase', value, N'database_of', N'SqlInstance', (value - 1) % {{instances}} + 1, NULL FROM GENERATE_SERIES(1, {{databases}})
                UNION ALL SELECT N'SqlAvailabilityGroup', value, N'replica_on', N'SqlInstance', 2 * value - 1, CONCAT(N'AG-', value) FROM GENERATE_SERIES(1, {{ags}})
                UNION ALL SELECT N'SqlAvailabilityGroup', value, N'replica_on', N'SqlInstance', 2 * value, CONCAT(N'AG-', value) FROM GENERATE_SERIES(1, {{ags}})
                UNION ALL SELECT N'ClusterNetworkName', value, N'listener_for', N'SqlAvailabilityGroup', value, NULL FROM GENERATE_SERIES(1, {{ags}})
                UNION ALL SELECT N'WindowsCluster', value, N'cluster_node_of', N'Server', {{servers}} - 2 * value + 1, NULL FROM GENERATE_SERIES(1, {{windowsClusters}})
                UNION ALL SELECT N'WindowsCluster', value, N'cluster_node_of', N'Server', {{servers}} - 2 * value + 2, NULL FROM GENERATE_SERIES(1, {{windowsClusters}})
                UNION ALL SELECT N'BusinessService', s.value, N'depends_on', N'VirtualMachine', (s.value - 1) * 5 + k.value, NULL
                    FROM GENERATE_SERIES(1, {{services}}) AS s CROSS JOIN GENERATE_SERIES(1, 5) AS k
                UNION ALL SELECT N'VirtualMachine', value, N'owned_by', N'Team', value % 5 + 1, NULL FROM GENERATE_SERIES(1, {{vms}})
                UNION ALL SELECT N'Server', value, N'owned_by', N'Team', value % 5 + 1, NULL FROM GENERATE_SERIES(1, {{servers}})
                UNION ALL SELECT N'BusinessService', value, N'owned_by', N'Team', 6 + (value - 1) % ({{teams}} - 5), NULL FROM GENERATE_SERIES(1, {{services}})
            )
            INSERT INTO cmdb.Relationship (Id, TenantId, FromCiId, ToCiId, RelationshipTypeId, RedundancyGroup, ChangedBy)
            SELECT NEWID(), '00000000-0000-0000-0000-000000000001', f.Id, t.Id, rt.Id, r.Grp, N'{{changedBy:D}}'
            FROM r
            JOIN #c AS f ON f.Type = r.FromType AND f.N = r.FromN
            JOIN #c AS t ON t.Type = r.ToType AND t.N = r.ToN
            JOIN cmdb.RelationshipType AS rt ON rt.Name = r.RelType;
            """);
    }
}
