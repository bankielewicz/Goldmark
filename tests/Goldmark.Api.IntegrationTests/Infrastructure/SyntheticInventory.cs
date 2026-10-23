namespace Goldmark.Api.IntegrationTests.Infrastructure;

/// <summary>
/// The synthetic estate of the performance suite (NFR-PERF-1, NFR-PERF-5; DEC-044 item 7): 25,000 CIs
/// in the reference environment's proportions (PRD §1.4: VMs and their servers first, then databases,
/// people, datastores and network devices), names in the patterns collectors produce, one in twenty
/// retired, written in one statement as the administrator. Later slices add relationships and history.
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
}
